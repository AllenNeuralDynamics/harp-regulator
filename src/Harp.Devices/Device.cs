using Harp.Devices.Pico;
using Harp.Protocol;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.Json.Serialization;

namespace Harp.Devices;

public sealed partial record Device
{
    public DeviceConfidence Confidence { get; init; }
    public DeviceKind Kind { get; init; }
    public DeviceState State { get; init; }
    public string? PortName { get; init; }
    public ushort? WhoAmI { get; init; }
    public string? DeviceDescription { get; init; }
    public ulong? SerialNumber { get; init; }
    public HarpVersion? HardwareVersion { get; init; }
    public HarpVersion? FirmwareVersion { get; init; }

    /// <summary>Human-readable descpiption describing the origin of this device during enumeration.</summary>
    /// <remarks>For informational pruposes only.</remarks>
    public required string Source { get; init; }

    [JsonIgnore] public PicobootDevice? PicobootDevice { get; init; }

    /// <summary>Populates <see cref="WhoAmI"/> and <see cref="DeviceDescription"/> from a USB description string.</summary>
    /// <remarks>
    /// If the USB description string is not in the expected format, only the <see cref="DeviceDescription"/> will be updated.
    ///
    /// If the <see cref="WhoAmI"/> is able to be parsed, the <see cref="Confidence"/> will be promoted to <see cref="DeviceConfidence.High"/>.
    /// </remarks>
    public Device WithMetadataFromUsbDescription(ReadOnlySpan<char> usbDescription)
    {
        ReadOnlySpan<char> remaining;

        if (usbDescription.Length == 0)
            return this;

        if (WhoAmI is not null)
            throw new InvalidOperationException("Device already has a WhoAmI!");

        static bool TryReadPart(ReadOnlySpan<char> description, string prefix, out ReadOnlySpan<char> valuePart, out ReadOnlySpan<char> remainingPart)
        {
            if (!description.StartsWith(prefix, StringComparison.Ordinal))
            {
                valuePart = ReadOnlySpan<char>.Empty;
                remainingPart = description;
                return false;
            }

            int splitIndex = description.IndexOf('|');
            valuePart = description;
            if (splitIndex < 0)
            {
                valuePart = description;
                remainingPart = ReadOnlySpan<char>.Empty;
            }
            else
            {
                valuePart = description.Slice(0, splitIndex);
                remainingPart = description.Slice(splitIndex + 1).TrimStart();
            }

            valuePart = valuePart.Slice(prefix.Length).TrimEnd();
            return true;
        }

        // Try parse WhoAmI from USB description
        ushort whoAmI;
        if (TryReadPart(usbDescription, "Harp", out ReadOnlySpan<char> whoAmIString, out remaining) && ushort.TryParse(whoAmIString, out whoAmI))
            usbDescription = remaining;
        else
        {
            // Required `Harp<WhoAmI>` prefix is not present, this isn't a Harp device
            switch (usbDescription)
            {
                // Special case: Don't use overly generic name
                case "Board CDC": // Used by older Pico core devices
                case "USB Serial Port":
                    return this;
                default:
                    return this with { DeviceDescription = usbDescription.ToString() };
            }
        }

        // Try to parse the firmware version from the USB description
        HarpVersion? firmwareVersion = null;
        if (TryReadPart(usbDescription, "Fw", out ReadOnlySpan<char> versionString, out remaining) && HarpVersion.TryParse(versionString, out HarpVersion _version))
        {
            firmwareVersion = _version;
            usbDescription = remaining;
        }

        return this with
        {
            // Getting to this point means we're sure this is supposed to be a Harp device
            Confidence = Confidence.PromoteTo(DeviceConfidence.High),
            WhoAmI = whoAmI,
            FirmwareVersion = firmwareVersion ?? FirmwareVersion,
            DeviceDescription = usbDescription.Length == 0 ? DeviceDescription : usbDescription.ToString(),
        };
    }

    public Device WithMetadataFromFirmwareInfo(in PicoFirmwareInfo firmwareInfo)
    {
        if (!firmwareInfo.HaveInfo)
            return this;

        HarpVersion? oldVersion = FirmwareVersion;
        Device result = WithMetadataFromUsbDescription(firmwareInfo.Description);

        // Prefer the explicit version from the firmware info if it is different from the one in the description
        // (This ideally should not happen. If it's present in both, it should match.)
        if (firmwareInfo.Version is string versionString && HarpVersion.TryParse(versionString, out HarpVersion versionFromInfo) && result.FirmwareVersion != versionFromInfo)
        {
            // Complain if the version difference came from the description
            if (result.FirmwareVersion is not null && result.FirmwareVersion != oldVersion)
                Trace.WriteLine($"Version from firmware info description '{firmwareInfo.Description}' does not match explicit version string '{firmwareInfo.Version}'!");
            else if (oldVersion is not null)
                Trace.WriteLine($"Version was previously detected as '{oldVersion}', which does not match the version from the firmware info '{firmwareInfo.Version}'. The latter will be used.");

            result = result with { FirmwareVersion = versionFromInfo };
        }

        // If we still don't have a description, fall back to the program name
        // (The Pico SDK populates it by default, so it's a useful fallback)
        if (result.DeviceDescription is null && firmwareInfo.ProgramName is not null)
            result = result with { DeviceDescription = firmwareInfo.ProgramName };

        return result;
    }

    private Device __WithMetadataFromHarpProtocol(HarpConnection? harp)
    {
        Debug.Assert(State is DeviceState.Online or DeviceState.Unknown); // We don't expect other states to reach this method

        // Used to improve exception messages
        CommonRegister? currentlyReading = null;

        bool ownsConnection = false;
        try
        {
            if (harp is null)
            {
                ownsConnection = true;
                harp = new HarpConnection
                (
                    PortName ?? throw new InvalidOperationException("Cannot get metadata from a device without a serial port to connect to."),
                    timeoutMilliseconds: 500
                );
            }

            HarpMessage<T>? ReadRegisterOrNull<T>(CommonRegister register, int minimumLength = 1)
                where T : unmanaged
            {
                currentlyReading = register;
                HarpMessage<T> response = harp.Read<T>(register);
                currentlyReading = null;
                if (!response.IsValid)
                    Trace.WriteLine($"Got an invalid repsonse when trying to read {register} from {PortName}");
                else if (response.MessageType != MessageType.Read)
                    Trace.WriteLine($"Got {response.MessageType} response when trying to read {register} from {PortName}");
                else if (response.Payload.Length < minimumLength)
                    Trace.WriteLine($"Got response with less than {minimumLength} element(s) when reading {register} from {PortName}");
                else if (response.PayloadType.Type != typeof(T))
                    Trace.WriteLine($"Expected {PayloadType.GetType<T>()} but got {response.PayloadType} when reading {register} from {PortName}");
                else
                    return response;
                return null;
            }

            T? ReadRegisterValueOrNull<T>(CommonRegister register)
                where T : unmanaged
                => ReadRegisterOrNull<T>(register)?.Payload[0];

            ushort? whoAmI = WhoAmI ?? ReadRegisterValueOrNull<ushort>(CommonRegister.R_WHO_AM_I);

            HarpVersion? hardwareVersion = HardwareVersion;
            if (hardwareVersion is null
                && ReadRegisterValueOrNull<byte>(CommonRegister.R_HW_VERSION_H) is byte hwMajor
                && ReadRegisterValueOrNull<byte>(CommonRegister.R_HW_VERSION_L) is byte hwMinor)
            {
                hardwareVersion = new HarpVersion(hwMajor, hwMinor, 0);
            }

            HarpVersion? firmwareVersion = FirmwareVersion;
            if (firmwareVersion is null
                && ReadRegisterValueOrNull<byte>(CommonRegister.R_FW_VERSION_H) is byte versionMajor
                && ReadRegisterValueOrNull<byte>(CommonRegister.R_FW_VERSION_L) is byte versionMinor)
            {
                firmwareVersion = new HarpVersion(versionMajor, versionMinor, 0);
            }

            string? deviceDescription = DeviceDescription;
            {
                if (deviceDescription is null && ReadRegisterOrNull<byte>(CommonRegister.R_DEVICE_NAME, minimumLength: 0) is HarpMessage<byte> response)
                {
                    ReadOnlySpan<byte> deviceName = response.Payload.SliceNullTerminated(0);
                    if (deviceName.Length > 0)
                        deviceDescription = Encoding.UTF8.GetString(deviceName);
                }
            }

            ulong? serialNumber = SerialNumber;
            if (serialNumber is null && ReadRegisterValueOrNull<ushort>(CommonRegister.R_SERIAL_NUMBER) is ushort harpSerialNumber)
            {
                switch (harpSerialNumber)
                {
                    case 0:
                    // This is the dummy ID returned when an RP2040 board has no flash
                    // https://github.com/raspberrypi/pico-sdk/blob/bddd20f928ce76142793bef434d4f75f4af6e433/src/rp2_common/pico_unique_id/unique_id.c#L22
                    case 0xeeee when Kind is DeviceKind.Pico:
                        Trace.WriteLine($"Ignoring invalid serial number {harpSerialNumber:x4} returned by {PortName}.");
                        break;
                    // This is the default serial number used by the official core.pico example
                    // https://github.com/harp-tech/core.pico/blob/ef3745f6a1e6776910bcafaf1dc86445ed294d03/examples/harp_c_app_example/src/main.cpp#L20
                    case 0xCAFE when Kind is DeviceKind.Pico:
                        Trace.WriteLine($"Ignoring dubious serial number {harpSerialNumber:x4} returned by {PortName}.");
                        break;
                    default:
                        serialNumber = harpSerialNumber;
                        break;
                }
            }

            return this with
            {
                State = DeviceState.Online,
                WhoAmI = whoAmI,
                HardwareVersion = hardwareVersion,
                FirmwareVersion = firmwareVersion,
                DeviceDescription = deviceDescription,
                SerialNumber = serialNumber,
            };
        }
        catch (TimeoutException)
        {
            Trace.WriteLine($"Timed out while trying to read {currentlyReading?.ToString() ?? "???"} from {PortName}");
            return this;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (currentlyReading is null)
                Trace.WriteLine($"Error when accessing {PortName}: {ex.Message}");
            else
                Trace.WriteLine($"Error when reading {currentlyReading} from {PortName}: {ex.Message}");
            return this;
        }
        finally
        {
            if (ownsConnection)
                harp?.Dispose();
        }
    }

    public Device WithMetadataFromHarpProtocol()
        => __WithMetadataFromHarpProtocol(null);

    public Device WithMetadataFromHarpProtocol(HarpConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return __WithMetadataFromHarpProtocol(connection);
    }

    public Device WithMetadataFromPicobootDevice(PicobootDevice picobootDevice)
    {
        if (PicobootDevice is not null)
            throw new InvalidOperationException($"This device is already associated with a {nameof(Pico.PicobootDevice)}.");

        Trace.WriteLine($"Populating details on '{Source}' via {picobootDevice}.");

        ulong? serialNumber = picobootDevice.UniqueId;
        if (serialNumber is not null && SerialNumber is not null)
            Trace.WriteLine($"Serial number {serialNumber:x16} provided by Picoboot will replace previous serial number {SerialNumber:X16}.");

        Device result = this with
        {
            PicobootDevice = picobootDevice,
            SerialNumber = serialNumber ?? SerialNumber,
        };

        PicoFirmwareInfo firmwareInfo = PicoFirmwareInfo.GetInfo(picobootDevice);
        result = result.WithMetadataFromFirmwareInfo(firmwareInfo);

        return result;
    }

    /// <summary>Enumerates devices connected to the system</summary>
    /// <param name="allowConnection">If specified, devices at the specified confidence level or above have missing metadata populated using the Harp protocol.</param>
    public static ImmutableArray<Device> EnumerateDevices(DeviceConfidence? allowConnection)
    {
        ImmutableArray<Device>.Builder builder = ImmutableArray.CreateBuilder<Device>();

        // Enumerate Harp devices directly if possible
        if (OperatingSystem.IsWindows())
            WindowsDeviceEnumerator.EnumerateDevices(builder);
        else if (OperatingSystem.IsLinux())
            LinuxDeviceEnumerator.EnumerateDevices(builder);
        else
            Trace.WriteLine("Warning: Support for enumerating Harp devices via USB descriptors is not implemneted on this platform.");

        // Add any serial ports not enumerated above
        {
            SortedSet<string> ports = new(SerialPort.GetPortNames());
            foreach (Device device in builder)
                if (device.PortName is not null)
                    ports.Remove(device.PortName);

            foreach (string port in ports)
                builder.Add(new Device() { PortName = port, Source = typeof(SerialPort).FullName! });
        }

        // Connect to online Harp devices to fill in missing information (if applicable)
        if (allowConnection is DeviceConfidence connectionFilter)
        {
            for (int i = 0; i < builder.Count; i++)
            {
                Device device = builder[i];
                if (device.PortName is null)
                    continue;

                if (device.Confidence < connectionFilter)
                    continue;

                Trace.WriteLine($"Populating {device.PortName}'s metadata via Harp registers.");
                builder[i] = device.WithMetadataFromHarpProtocol();
            }
        }

        return builder.ToImmutable();
    }
}
