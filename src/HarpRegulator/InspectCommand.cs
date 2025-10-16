using Harp.Devices;
using Harp.Devices.Pico;
using PicobootConnection;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarpRegulator;

internal sealed class InspectCommand : CommandBase
{
    public override string Verb => "inspect";
    public override string Description => "Shows information about firmware files.";
    public override string? UsageHelp => "inspect <firmware-file-path> [--json]";
    public override string? ArgumentsHelp =>
        """
        <firmware-file-path>
            Path to a firmware blob in UF2 format to inspect.

        --json
            Formats the output using JSON.
        """;

    private struct Uf2FamilyInfo
    {
        public AddressRange AddressRange { get; }
        public AddressRange? FlashRange { get; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ImmutableSortedSet<memory_type>? MemoryTypes { get; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public PicoFirmwareInfo PicoFirmwareInfo { get; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Device? HarpInfo { get; }

        public Uf2FamilyInfo(string filePath, Uf2View view)
        {
            AddressRange = new AddressRange(view.MinAddress, view.MaxAddress);

            AddressRange flashRange = view.GetUsedFlashRange();
            if (flashRange.Size > 0)
                FlashRange = flashRange;

            if (view.FamilyId.ToPicoModel() == model_t.unknown)
            {
                if (view.FamilyId is Uf2FamilyId.RP2XXX_ABSOLUTE or Uf2FamilyId.RP2XXX_DATA)
                    Trace.WriteLine($"'{view.FamilyId.Description()}'-family blobs cannot contain Harp metadata.");
                else
                    Trace.WriteLine($"Not sure how to read Harp metadata from non-Pico device family '{view.FamilyId.Description()}'.");
                MemoryTypes = null;
                PicoFirmwareInfo = default;
                HarpInfo = null;
                return;
            }

            MemoryTypes = view.GetMemoryTypes();
            PicoFirmwareInfo = PicoFirmwareInfo.GetInfo(view);

            if (!PicoFirmwareInfo.HaveInfo)
            {
                Trace.WriteLine("Could not read any Pico firmware information.");
                HarpInfo = null;
            }
            else
            {
                HarpInfo = new()
                {
                    Source = view.ToString(),
                    Kind = DeviceKind.Pico,
                };
                HarpInfo = HarpInfo.WithMetadataFromFirmwareInfo(PicoFirmwareInfo);
            }
        }
    }

    public override CommandResult Execute(Queue<string> arguments)
    {
        string? filePath = null;
        bool useJson = false;

        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (TryHandleCommonArgument(argument))
            {
                case CommonArgumentResult.Handled:
                    continue;
                case CommonArgumentResult.ShowHelp:
                    return CommandResult.ShowHelp;
            }

            if (argument.ToLowerInvariant() == "--json")
            {
                useJson = true;
                continue;
            }

            if (filePath is not null)
            {
                Console.Error.WriteLine($"Unknown argument '{argument}'");
                return CommandResult.Failure;
            }

            filePath = argument;
        }

        if (filePath is null)
        {
            Console.WriteLine("Missing required parameters.");
            Console.WriteLine();
            return CommandResult.ShowHelp;
        }

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File '{filePath}' does not exist.");
            return CommandResult.Failure;
        }

        if (Uf2File.IsUf2File(filePath))
        {
            Uf2File uf2 = new(filePath);
            Dictionary<Uf2FamilyId, Uf2FamilyInfo>? jsonInfos = useJson ? new(uf2.FamilyIds.Count) : null;
            bool first = true;

            if ((uf2.FamilyIds.Count > 1 || VerboseMode) && !useJson)
            {
                first = false;
                Console.WriteLine($"'{filePath}' is a UF2 file containing {uf2.Blocks.Length} blocks for the following device families:");
                foreach (Uf2FamilyId family in uf2.FamilyIds)
                    Console.WriteLine($"* {family.Description()}");
            }

            foreach (Uf2FamilyId family in uf2.FamilyIds)
            {
                Trace.WriteLine($"Processing family '{family.Description()}' from '{filePath}'");
                Uf2View view = new(uf2, family);
                Uf2FamilyInfo info = new(filePath, view);

                if (useJson)
                {
                    jsonInfos?.Add(family, info);
                    continue;
                }

                if (first)
                { first = false; }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine(new String('=', DisplayWidth));
                }

                if (info.HarpInfo is Device device && device.Confidence == DeviceConfidence.High)
                {
                    Debug.Assert(device.WhoAmI is not null);
                    Console.WriteLine($"'{filePath}' data for '{family.Description()}' is Harp device firmware:");
                    Console.WriteLine($"         WhoAmI: {device.WhoAmI?.ToString() ?? "N/A"}");
                    Console.WriteLine($"    Description: {device.DeviceDescription ?? "N/A"}");
                    Console.WriteLine($"        Version: {device.FirmwareVersion?.ToString() ?? "N/A"}");
                    Console.WriteLine();
                    Console.WriteLine("Raw firmware info:");
                }
                else
                {
                    Console.WriteLine($"'{filePath}' data for '{family.Description()}' is not identified explicitly as Harp device firmware.");
                    Console.WriteLine();
                    Console.WriteLine("Raw Pico SDK firmware info:");
                }

                Console.WriteLine($"    Program name: {info.PicoFirmwareInfo.ProgramName ?? "N/A"}");
                Console.WriteLine($"     Description: {info.PicoFirmwareInfo.Description ?? "N/A"}");
                Console.WriteLine($"         Version: {info.PicoFirmwareInfo.Version ?? "N/A"}");
                if (info.FlashRange is AddressRange flashRange && flashRange.Size > 0)
                    Console.WriteLine($"      Flash size: {Utilities.FriendlyByteCount(flashRange.Size)} - {flashRange}");
                else
                    Console.WriteLine($"      Flash size: None");
            }

            if (useJson)
            {
                string json = JsonSerializer.Serialize(jsonInfos, JsonOptions);
                Console.WriteLine(json);
            }

            return CommandResult.Success;
        }
        else if (HexFileHelper.IsHexFile(filePath))
        {
            try
            {
                var firmware = HexFileHelper.LoadFirmware(filePath);
                
                if (useJson)
                {
                    var info = new
                    {
                        FileType = "Intel HEX",
                        Metadata = firmware.Metadata.ToString()
                    };
                    string json = JsonSerializer.Serialize(info, JsonOptions);
                    Console.WriteLine(json);
                }
                else
                {
                    Console.WriteLine($"'{filePath}' is an Intel HEX firmware file for ATxmega-based Harp devices:");
                    Console.WriteLine();
                    Console.WriteLine($"Firmware metadata:");
                    Console.WriteLine($"{firmware.Metadata}");
                }

                return CommandResult.Success;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error reading Intel HEX file: {ex.Message}");
                return CommandResult.Failure;
            }
        }
        else
        {
            switch (Path.GetExtension(filePath).ToLowerInvariant())
            {
                case ".uf2":
                    Console.Error.WriteLine($"'{filePath}' does not appear to be a valid UF2 file.");
                    break;
                case ".hex":
                case ".mcs":
                case ".int":
                case ".ihex":
                case ".ihe":
                case ".ihx":
                    Console.Error.WriteLine($"'{filePath}' appears to be an Intel HEX file but could not be parsed.");
                    break;
                default:
                    Console.Error.WriteLine($"'{filePath}' does not seem to be a supported format.");
                    break;
            }

            return CommandResult.Failure;
        }
    }
}
