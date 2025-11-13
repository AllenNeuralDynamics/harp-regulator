using Harp.Devices;
using Harp.Devices.Pico;
using Harp.Protocol;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;

namespace HarpRegulator;

partial class UploadFirmwareCommand
{
    private bool SwitchToBootloader(ref ImmutableArray<Device> allDevices, [DisallowNull, NotNullWhen(true)] ref Device? device, bool interactive, bool force)
    {
        switch (device.State)
        {
            case DeviceState.Online:
            case DeviceState.Unknown:
                break;
            default:
                throw new InvalidOperationException($"Cannot switch to bootloader when device is in the {device.State} state.");
        }

        // First try to send a reboot command using the Harp protocol
        if (TryRebootUsingFirmwareUpdateCapabilitiesRegister(ref device) is string failReason)
        {
            Console.Error.WriteLine("Could not automatically place the device into bootloader mode!");
            Console.Error.WriteLine(failReason);

            if (!interactive)
                return false;

            Console.WriteLine("Manually place the device into BOOTSEL mode and press Enter to continue or Escape to abort.");
            while (true)
            {
                ConsoleKey key = Console.ReadKey(intercept: true).Key;
                if (key == ConsoleKey.Escape)
                    return false;
                else if (key == ConsoleKey.Enter)
                    break;
            }
        }

        // Disconnect from existing Picoboot devices before reenumeration
        foreach (PicobootDevice? picobootDevice in allDevices.Select(d => d.PicobootDevice))
            picobootDevice?.Dispose();

        // Find our device again now that it's in picoboot mode
        Console.WriteLine("Finding device again now that it's in BOOTSEL mode...");
        
        string? serialNumberFilter = device.SerialNumber?.ToString("x");
        ImmutableArray<Device> bootselDevices = [];
        
        // Retry with increasing delays to give Windows time to enumerate the device
        int[] retryDelaysMs = [1000, 2000, 3000];
        foreach (int delayMs in retryDelaysMs)
        {
            Thread.Sleep(delayMs);
            allDevices = Device.EnumerateDevices(allowConnection: null);
            bootselDevices = allDevices.Filter(d => d.Kind == DeviceKind.Pico && d.State is DeviceState.Bootloader);
            
            if (bootselDevices.Length > 0)
            {
                Console.WriteLine($"Found {bootselDevices.Length} BOOTSEL device(s) after {delayMs}ms");
                break;
            }
            else
            {
                Console.WriteLine($"No BOOTSEL devices found after {delayMs}ms, retrying...");
            }
        }
        
        device = null;

        if (serialNumberFilter is not null)
        {
            ImmutableArray<Device> snFiltered = bootselDevices.Filter(d => d.SerialNumberPartialMatch(serialNumberFilter));
            if (snFiltered.Length == 1)
            { device = snFiltered[0]; }
            else if (snFiltered.Length > 1)
            {
                Console.Error.WriteLine($"Serial number '{serialNumberFilter}' is ambiguous and matches multiple devices! Cannot continue.");
                Console.Error.WriteLine();
                ListDevicesCommand.ListDevices(snFiltered, output: Console.Error);
                return false;
            }
        }

        if (device is null)
        {
            switch (bootselDevices.Length)
            {
                case 1:
                    device = bootselDevices[0];
                    break;
                case 0:
                    Console.Error.WriteLine("Could not find any devices in BOOTSEL mode");
                    
                    if (allDevices.Length > 0)
                    {
                        Console.Error.WriteLine($"Found {allDevices.Length} device(s), but none in BOOTSEL mode:");
                        ListDevicesCommand.ListDevices(allDevices, output: Console.Error);
                    }
                    
                    if (allDevices.Any(d => d.State == DeviceState.DriverError))
                    {
                        Console.Error.WriteLine("One or more devices has a driver in an erroneous state.");
                        Console.Error.WriteLine("    Try running `HarpRegulator install-drivers` as admin.");
                    }
                    return false;
                case > 1:
                    Console.Error.WriteLine("More than one BOOTSEL device is connected to the system and we couldn't figure out which one to udpate:");
                    ListDevicesCommand.ListDevices(bootselDevices, output: Console.Error);
                    Console.Error.WriteLine();
                    return false;
                default:
                    throw new UnreachableException();
            }
        }

        Console.WriteLine("Found BOOTSEL device after reboot:");
        ListDevicesCommand.ListDevices([device]);
        Console.WriteLine();

        if (serialNumberFilter is not null && !device.SerialNumberPartialMatch(serialNumberFilter))
        {
            Console.Error.WriteLine($"Before rebooting we had a device with serial number {serialNumberFilter}, but this device has a serial number of {device.SerialNumber?.ToString("x") ?? "N/A"}");
            if (force)
            { Console.WriteLine("Force mode enabled, discrepancy ignored."); }
            else if (!interactive || !YesNo("Continue anyway despite this discrepancy?", defaultChoice: false))
            {
                Console.Error.WriteLine("Upload aborted.");
                return false;
            }
        }

        return true;
    }

    private string? TryRebootUsingFirmwareUpdateCapabilitiesRegister(ref Device device)
    {
        if (device.PortName is null)
            return "We could not determine the serial port associated wtih the device.";

        try
        {
            using (HarpConnection harp = new(device.PortName, timeoutMilliseconds: 500))
            {
                // Try to get more details about the device via the Harp protocol
                // (Not strictly necessary, this is just a good time to do this since we're connected now)
                Device oldDevice = device;
                device = device.WithMetadataFromHarpProtocol(harp);
                if (device != oldDevice)
                {
                    Console.WriteLine("Got more info about device via Harp protocol:");
                    ListDevicesCommand.ListDevices([device]);
                    Console.WriteLine();
                }

                // Reboot the device into BOOTSEL mode using R_RESET_DEV register
                // Bit 5 (RST_DFU_OFFSET) triggers reset_usb_boot(0,0) in Pico firmware
                Console.WriteLine("Instructing device to reboot into BOOTSEL mode...");
                const byte RST_DFU_BIT = 0b00100000; // Bit 5 set
                
                try
                {
                    harp.Write(CommonRegister.R_RESET_DEV, RST_DFU_BIT);
                    //TODO: Validate response if we get one
                }
                catch (TimeoutException)
                {
                    // This is expected - the device reboots immediately and can't send a response
                    Console.WriteLine("Device disconnected (expected behavior during reboot)");
                }
                catch (OperationCanceledException)
                {
                    // This is also expected - the serial port is disconnected during reboot
                    Console.WriteLine("Device disconnected (expected behavior during reboot)");
                }
            }

            return null;
        }
        catch (Exception ex)
        { return $"An exception occurred while trying to reboot: {ex}"; }
    }
}
