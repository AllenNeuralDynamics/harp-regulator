using Harp.Devices;
using Harp.Devices.Pico;
using PicobootConnection;
using System;
using System.Diagnostics;

namespace HarpRegulator;

partial class UploadFirmwareCommand
{
    private enum MismatchLevel
    {
        None,
        Minor,
        Major,
        Fatal,
    }

    //TODO: Check if the device is an RP2350 device with a partition table since we don't support partitions yet
    /// <returns>True if the upload should continue, false otherwise.</returns>
    private bool VerifyFirmwareCompatibility(Device device, Uf2View view, bool interactive, bool force)
    {
        if (device.PicobootDevice is null)
            throw new InvalidOperationException("This method must be called on a device which has an established PICOBOOT connection.");

        MismatchLevel mismatchLevel = MismatchLevel.None;
        void DeclareMismatch(MismatchLevel level, string message)
        {
            if (mismatchLevel < level)
                mismatchLevel = level;
            ConsoleColor oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine();
            Console.Error.WriteLine(message);
            Console.ForegroundColor = oldColor;
        }

        Console.WriteLine("Checking whether the firmware is applicable to the target device...");
        AddressRange deviceFlashRange;
        try
        { deviceFlashRange = device.PicobootDevice.TryGetFlashRange(); }
        catch (PicobootCommandFailureException ex) when (ex.Status == picoboot_status.PICOBOOT_NOT_PERMITTED)
        {
            // picotool also does this (see info_guts), not entirely sure when this might happen
            // (This status code was introduced by the RP2350, so it's presumably related to some RP2350 security feature.)
            Trace.WriteLine("Could not determine the size of the device's flash chip due to a permission errors.");
            deviceFlashRange = default;
        }
        AddressRange firmwareFlashRange = view.GetUsedFlashRange();

        PicoFirmwareInfo firmwareInfo = PicoFirmwareInfo.GetInfo(view);
        Device firmwareDevice = new Device()
        {
            Source = view.ToString(),
            Kind = view.FamilyId.ToDeviceKind()
        }.WithMetadataFromFirmwareInfo(firmwareInfo);

        Console.WriteLine();
        Console.WriteLine("Physical device characteristics:");
        Console.WriteLine($"         WhoAmI: {device.WhoAmI?.ToString() ?? "N/A"}");
        Console.WriteLine($"    Description: {device.DeviceDescription ?? "N/A"}");
        Console.WriteLine($"        Version: {device.FirmwareVersion?.ToString() ?? "N/A"}");
        Trace.WriteLine($"    Device kind: {device.Kind}");
        Trace.WriteLine($"     Pico model: {device.PicobootDevice.Model.FriendlyName()}");
        if (deviceFlashRange.Size == 0)
            Trace.WriteLine($"     Flash size: Unknown");
        else
            Trace.WriteLine($"     Flash size: {Utilities.FriendlyByteCount(deviceFlashRange.Size)} - {deviceFlashRange}");
        Console.WriteLine();
        Console.WriteLine("Firmware characteristics:");
        Console.WriteLine($"         WhoAmI: {firmwareDevice.WhoAmI?.ToString() ?? "N/A"}");
        Console.WriteLine($"    Description: {firmwareDevice.DeviceDescription ?? "N/A"}");
        Console.WriteLine($"        Version: {firmwareDevice.FirmwareVersion?.ToString() ?? "N/A"}");
        Trace.WriteLine($"    Device kind: {firmwareDevice.Kind}");
        Trace.WriteLine($"     Pico model: {view.FamilyId.ToPicoModel().FriendlyName()}");
        if (firmwareFlashRange.Size == 0)
            Trace.WriteLine($"     Flash size: None");
        else
            Trace.WriteLine($"     Flash size: {Utilities.FriendlyByteCount(firmwareFlashRange.Size)} - {firmwareFlashRange}");

        if (deviceFlashRange.Size == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Could not determine the size of the device's flash storage! It will not be validated.");
        }
        else if (firmwareFlashRange.Size > 0 && !deviceFlashRange.Contains(firmwareFlashRange))
        {
            if (firmwareFlashRange.Start == deviceFlashRange.Start)
            {
                DeclareMismatch(MismatchLevel.Fatal, "The firmware's flash region will not fit within the usable portion of the device's flash storage:");
                Console.Error.WriteLine($"      Device: {Utilities.FriendlyByteCount(deviceFlashRange.Size)}");
                Console.Error.WriteLine($"    Firmware: {Utilities.FriendlyByteCount(firmwareFlashRange.Size)}");
            }
            else
            {
                DeclareMismatch(MismatchLevel.Fatal, "The firmware's flash region will not fit within the usable portion of the device's flash storage:");
                Console.Error.WriteLine($"      Device: {deviceFlashRange}");
                Console.Error.WriteLine($"    Firmware: {firmwareFlashRange}");
            }

            // picotool doesn't let you ignore this problem so we don't either
            Console.Error.WriteLine("(This error is non-recoverable)");
        }

        if (!firmwareInfo.HaveInfo)
        { DeclareMismatch(MismatchLevel.Major, "Firmware does not have embedded firmware info! Cannot check if it is compatible with the target device."); }
        else
        {
            if (firmwareDevice.Confidence != DeviceConfidence.High)
                DeclareMismatch(MismatchLevel.Major, "Firmware does not appear to be Harp firmware!");

            if (device.WhoAmI != firmwareDevice.WhoAmI)
            {
                DeclareMismatch(MismatchLevel.Major, "The WhoAmI does not match between the device and the firmware!");
                Console.Error.WriteLine($"      Device: {device.WhoAmI?.ToString() ?? "<unknown>"}");
                Console.Error.WriteLine($"    Firmware: {firmwareDevice.WhoAmI?.ToString() ?? "<unknown>"}");
            }

            if (firmwareDevice.Kind != device.Kind)
            {
                DeclareMismatch(MismatchLevel.Major, "The device kind does not match between the device and the firmware!");
                Console.Error.WriteLine($"      Device: {device.Kind}");
                Console.Error.WriteLine($"    Firmware: {firmwareDevice.Kind}");
            }

            if (view.FamilyId.ToPicoModel() != device.PicobootDevice.Model)
            {
                DeclareMismatch(MismatchLevel.Major, "The Pico model does not match between the device and the firmware!");
                Console.Error.WriteLine($"      Device: {device.PicobootDevice.Model.FriendlyName()}");
                Console.Error.WriteLine($"    Firmware: {view.FamilyId.ToPicoModel().FriendlyName()}");
            }

            if (firmwareDevice.FirmwareVersion <= device.FirmwareVersion)
            {
                string difference = firmwareDevice.FirmwareVersion == device.FirmwareVersion ? "the same as" : "older than";
                DeclareMismatch(MismatchLevel.Minor, $"The firmware version is {difference} what's already on the device:");
                Console.Error.WriteLine($"      Device: {device.FirmwareVersion}");
                Console.Error.WriteLine($"    Firmware: {firmwareDevice.FirmwareVersion}");
            }
        }

        Console.WriteLine();
        if (mismatchLevel == MismatchLevel.None)
        {
            Console.WriteLine("Everything checks out!");
            return true;
        }
        else if (mismatchLevel == MismatchLevel.Fatal)
        {
            Console.Error.WriteLine("Cannot continue due to fatal error(s) above.");
            return false;
        }
        else if (force)
        {
            Console.WriteLine("Force mode enabled, ignoring the above discrepencies.");
            return true;
        }
        else if (interactive)
        {
            if (YesNo("Continue with the firmware upload despite the above discrepencies?", defaultChoice: false))
            { return true; }
            else
            {
                Console.Error.WriteLine("Upload aborted.");
                return false;
            }
        }
        else if (mismatchLevel == MismatchLevel.Major)
        {
            Console.Error.WriteLine("Failing due to critical mistmatch!");
            return false;
        }
        else if (mismatchLevel == MismatchLevel.Minor)
        {
            Console.WriteLine("Interactive mode disabled, ignoring non-critical discrepencies.");
            return true;
        }
        else
        { throw new UnreachableException(); }
    }

    /// <summary>
    /// Performs limited firmware compatibility verification when using the mass storage fallback.
    /// Since we don't have access to the PICOBOOT interface, we can only verify firmware-side information.
    /// </summary>
    /// <returns>True if the upload should continue, false otherwise.</returns>
    private bool VerifyFirmwareCompatibilityForMassStorage(Uf2View view, bool interactive, bool force)
    {
        Console.WriteLine();
        Console.WriteLine("Note: Using mass storage fallback - limited firmware verification available.");
        Console.WriteLine("      (Cannot verify device characteristics without PICOBOOT interface)");
        Console.WriteLine();

        PicoFirmwareInfo firmwareInfo = PicoFirmwareInfo.GetInfo(view);
        Device firmwareDevice = new Device()
        {
            Source = view.ToString(),
            Kind = view.FamilyId.ToDeviceKind()
        }.WithMetadataFromFirmwareInfo(firmwareInfo);

        Console.WriteLine("Firmware characteristics:");
        Console.WriteLine($"         WhoAmI: {firmwareDevice.WhoAmI?.ToString() ?? "N/A"}");
        Console.WriteLine($"    Description: {firmwareDevice.DeviceDescription ?? "N/A"}");
        Console.WriteLine($"        Version: {firmwareDevice.FirmwareVersion?.ToString() ?? "N/A"}");
        Trace.WriteLine($"    Device kind: {firmwareDevice.Kind}");
        Trace.WriteLine($"     Pico model: {view.FamilyId.ToPicoModel().FriendlyName()}");

        bool hasWarnings = false;

        if (!firmwareInfo.HaveInfo)
        {
            Console.WriteLine();
            ConsoleColor oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠ Warning: Firmware does not have embedded firmware info!");
            Console.WriteLine("           Cannot verify if it is Harp firmware or check WhoAmI.");
            Console.ForegroundColor = oldColor;
            hasWarnings = true;
        }
        else if (firmwareDevice.Confidence != DeviceConfidence.High)
        {
            Console.WriteLine();
            ConsoleColor oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠ Warning: Firmware does not appear to be Harp firmware!");
            Console.ForegroundColor = oldColor;
            hasWarnings = true;
        }

        Console.WriteLine();

        if (!hasWarnings)
        {
            Console.WriteLine("Firmware appears to be valid Harp firmware.");
            return true;
        }
        else if (force)
        {
            Console.WriteLine("Force mode enabled, proceeding despite warnings.");
            return true;
        }
        else if (interactive)
        {
            if (YesNo("Continue with the firmware upload despite the above warning(s)?", defaultChoice: false))
                return true;
            else
            {
                Console.Error.WriteLine("Upload aborted.");
                return false;
            }
        }
        else
        {
            Console.Error.WriteLine("Non-interactive mode: use --force to proceed despite warnings.");
            return false;
        }
    }
}
