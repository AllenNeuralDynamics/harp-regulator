using Bonsai.Harp;
using System;
using System.IO;

namespace HarpRegulator;

/// <summary>
/// Helper class for working with Intel HEX firmware files for ATxmega devices.
/// </summary>
internal static class HexFileHelper
{
    /// <summary>
    /// Determines if a file is likely an Intel HEX file based on its extension.
    /// </summary>
    public static bool IsHexFile(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension is ".hex" or ".mcs" or ".int" or ".ihex" or ".ihe" or ".ihx";
    }

    /// <summary>
    /// Loads firmware from an Intel HEX file.
    /// </summary>
    public static DeviceFirmware LoadFirmware(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Firmware file not found: {filePath}");

        if (!IsHexFile(filePath))
            throw new ArgumentException($"File does not appear to be an Intel HEX file: {filePath}");

        return DeviceFirmware.FromFile(filePath);
    }

    /// <summary>
    /// Attempts to extract firmware metadata from a HEX file.
    /// </summary>
    public static string GetFirmwareInfo(string filePath)
    {
        try
        {
            var firmware = LoadFirmware(filePath);
            return firmware.Metadata.ToString();
        }
        catch (Exception ex)
        {
            return $"Unable to read firmware metadata: {ex.Message}";
        }
    }
}
