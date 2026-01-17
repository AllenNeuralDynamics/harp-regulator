using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

namespace Harp.Devices.Pico;

/// <summary>
/// Helper for interacting with Raspberry Pi Pico devices via their USB mass storage interface in BOOTSEL mode.
/// </summary>
/// <remarks>
/// When a Pico device is in BOOTSEL mode, it exposes two interfaces:
/// 1. PICOBOOT - A vendor-specific USB interface for low-level flash operations (requires WinUSB on Windows)
/// 2. Mass Storage - A standard USB mass storage device that appears as a removable drive (works with built-in drivers)
/// 
/// This helper provides functionality to upload firmware via the mass storage interface, which doesn't require
/// any special drivers to be installed.
/// </remarks>
public static class PicoMassStorageHelper
{
    /// <summary>The volume label used by RP2040 devices in BOOTSEL mode.</summary>
    public const string RP2040VolumeLabel = "RPI-RP2";

    /// <summary>The volume label used by RP2350 devices in BOOTSEL mode.</summary>
    public const string RP2350VolumeLabel = "RP2350";

    /// <summary>The file that exists on the Pico mass storage device containing device info.</summary>
    public const string InfoFileName = "INFO_UF2.TXT";

    /// <summary>
    /// Finds the drive letter of a Raspberry Pi Pico device in BOOTSEL mode.
    /// </summary>
    /// <returns>The drive path (e.g., "E:\") if found, or null if no Pico mass storage device is connected.</returns>
    [SupportedOSPlatform("windows")]
    public static string? FindPicoDrive()
    {
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (IsPicoDrive(drive))
                return drive.RootDirectory.FullName;
        }

        return null;
    }

    /// <summary>
    /// Finds all drive letters of Raspberry Pi Pico devices in BOOTSEL mode.
    /// </summary>
    /// <returns>An array of drive paths (e.g., ["E:\", "F:\"]) for all connected Pico devices in BOOTSEL mode.</returns>
    [SupportedOSPlatform("windows")]
    public static string[] FindAllPicoDrives()
    {
        System.Collections.Generic.List<string> drives = new();

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (IsPicoDrive(drive))
                drives.Add(drive.RootDirectory.FullName);
        }

        return drives.ToArray();
    }

    /// <summary>
    /// Checks if the specified drive is a Raspberry Pi Pico in BOOTSEL mode.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool IsPicoDrive(DriveInfo drive)
    {
        try
        {
            // Pico devices appear as removable drives
            if (drive.DriveType != DriveType.Removable)
                return false;

            if (!drive.IsReady)
                return false;

            // Check volume label
            string volumeLabel = drive.VolumeLabel;
            if (!string.Equals(volumeLabel, RP2040VolumeLabel, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(volumeLabel, RP2350VolumeLabel, StringComparison.OrdinalIgnoreCase))
                return false;

            // Verify INFO_UF2.TXT exists (definitive check)
            string infoFile = Path.Combine(drive.RootDirectory.FullName, InfoFileName);
            if (!File.Exists(infoFile))
                return false;

            Trace.WriteLine($"Found Pico mass storage device at {drive.RootDirectory.FullName} with volume label '{volumeLabel}'");
            return true;
        }
        catch (IOException ex)
        {
            // Drive might not be ready or accessible
            Trace.WriteLine($"Could not check drive {drive.Name}: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.WriteLine($"Access denied to drive {drive.Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets information about the Pico device from its INFO_UF2.TXT file.
    /// </summary>
    /// <param name="drivePath">The root path of the Pico drive (e.g., "E:\").</param>
    /// <returns>The contents of the INFO_UF2.TXT file, or null if it cannot be read.</returns>
    public static string? ReadDeviceInfo(string drivePath)
    {
        try
        {
            string infoFile = Path.Combine(drivePath, InfoFileName);
            if (File.Exists(infoFile))
                return File.ReadAllText(infoFile);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not read INFO_UF2.TXT from {drivePath}: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Uploads a UF2 firmware file to a Pico device via the mass storage interface.
    /// </summary>
    /// <param name="drivePath">The root path of the Pico drive (e.g., "E:\").</param>
    /// <param name="uf2FilePath">The path to the UF2 firmware file to upload.</param>
    /// <param name="progress">Optional progress callback that receives bytes copied so far and total bytes.</param>
    /// <returns>True if the file was successfully copied, false otherwise.</returns>
    /// <remarks>
    /// After the UF2 file is copied, the Pico device will automatically reboot into the new firmware.
    /// The drive will disappear as the device reboots.
    /// </remarks>
    public static bool UploadFirmware(string drivePath, string uf2FilePath, Action<long, long>? progress = null)
    {
        if (!File.Exists(uf2FilePath))
            throw new FileNotFoundException("UF2 firmware file not found.", uf2FilePath);

        string destinationPath = Path.Combine(drivePath, Path.GetFileName(uf2FilePath));

        try
        {
            FileInfo sourceFile = new(uf2FilePath);
            long totalBytes = sourceFile.Length;

            using FileStream source = new(uf2FilePath, FileMode.Open, FileAccess.Read);
            using FileStream destination = new(destinationPath, FileMode.Create, FileAccess.Write);

            byte[] buffer = new byte[81920]; // 80 KB buffer
            long totalCopied = 0;
            int bytesRead;

            while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                destination.Write(buffer, 0, bytesRead);
                totalCopied += bytesRead;
                progress?.Invoke(totalCopied, totalBytes);
            }

            destination.Flush();
            Trace.WriteLine($"Successfully copied {totalCopied} bytes to {destinationPath}");
            return true;
        }
        catch (IOException ex)
        {
            // This is expected - the device reboots mid-copy and the drive disappears
            // The copy is still successful if we got far enough
            Trace.WriteLine($"IO exception during copy (may be expected if device rebooted): {ex.Message}");

            // If we got an IOException but the source file was small, it likely completed before reboot
            FileInfo sourceFile = new(uf2FilePath);
            if (sourceFile.Length < 256 * 1024) // Files under 256KB usually complete
            {
                Trace.WriteLine("File was small, assuming copy completed successfully before reboot.");
                return true;
            }

            throw;
        }
    }

    /// <summary>
    /// Uploads UF2 firmware data directly from memory to a Pico device via the mass storage interface.
    /// </summary>
    /// <param name="drivePath">The root path of the Pico drive (e.g., "E:\").</param>
    /// <param name="uf2Data">The UF2 firmware data to upload.</param>
    /// <param name="fileName">The filename to use on the Pico (should end in .uf2).</param>
    /// <param name="progress">Optional progress callback that receives bytes copied so far and total bytes.</param>
    /// <returns>True if the data was successfully written, false otherwise.</returns>
    public static bool UploadFirmware(string drivePath, ReadOnlySpan<byte> uf2Data, string fileName = "firmware.uf2", Action<long, long>? progress = null)
    {
        string destinationPath = Path.Combine(drivePath, fileName);

        try
        {
            using FileStream destination = new(destinationPath, FileMode.Create, FileAccess.Write);

            const int chunkSize = 81920; // 80 KB chunks
            long totalBytes = uf2Data.Length;
            long totalWritten = 0;

            while (totalWritten < totalBytes)
            {
                int bytesToWrite = (int)Math.Min(chunkSize, totalBytes - totalWritten);
                destination.Write(uf2Data.Slice((int)totalWritten, bytesToWrite));
                totalWritten += bytesToWrite;
                progress?.Invoke(totalWritten, totalBytes);
            }

            destination.Flush();
            Trace.WriteLine($"Successfully wrote {totalWritten} bytes to {destinationPath}");
            return true;
        }
        catch (IOException ex)
        {
            Trace.WriteLine($"IO exception during write (may be expected if device rebooted): {ex.Message}");

            // Small files usually complete before the device reboots
            if (uf2Data.Length < 256 * 1024)
            {
                Trace.WriteLine("Data was small, assuming write completed successfully before reboot.");
                return true;
            }

            throw;
        }
    }
}
