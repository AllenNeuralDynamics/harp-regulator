using Harp.Devices.Pico;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HarpRegulator;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(List<DeviceJsonInfo>))]
[JsonSerializable(typeof(Dictionary<Uf2FamilyId, InspectCommand.Uf2FamilyJsonInfo>))]
[JsonSerializable(typeof(InspectCommand.HexInspectionJsonInfo))]
internal sealed partial class HarpRegulatorJsonSerializerContext : JsonSerializerContext;
