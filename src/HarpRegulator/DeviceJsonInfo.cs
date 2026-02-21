using Harp.Devices;

namespace HarpRegulator;

internal sealed record DeviceJsonInfo
{
    public DeviceConfidence Confidence { get; init; }
    public DeviceKind Kind { get; init; }
    public DeviceState State { get; init; }
    public string? PortName { get; init; }
    public ushort? WhoAmI { get; init; }
    public string? DeviceDescription { get; init; }
    public ulong? SerialNumber { get; init; }
    public string? HardwareVersion { get; init; }
    public string? FirmwareVersion { get; init; }
    public string? Source { get; init; }
}