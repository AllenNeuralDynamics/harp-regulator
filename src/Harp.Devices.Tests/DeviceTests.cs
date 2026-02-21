using Xunit;
using Harp.Protocol;
using System.Runtime.CompilerServices;

namespace Harp.Devices.Tests;

public sealed class DeviceTests
{
    [Fact]
    public void DefaultDevice()
    {
        Device device = new()
        {
            Source = "Test"
        };

        // By default, devices should have zero confidence that they represent Harp devices, be unknown, in an unknown state, and have no metadata
        Assert.Equal(DeviceConfidence.Zero, device.Confidence);
        Assert.Equal(DeviceKind.Unknown, device.Kind);
        Assert.Equal(DeviceState.Unknown, device.State);
        Assert.Null(device.PortName);
        Assert.Null(device.WhoAmI);
        Assert.Null(device.DeviceDescription);
        Assert.Null(device.FirmwareVersion);

        // They do get a source though
        Assert.Equal("Test", device.Source);
    }

    [Theory]
    [InlineData("Harp0", 0, null, null)]
    [InlineData("Harp123", 123, null, null)]
    [InlineData("Harp123|Hobgoblin", 123, null, "Hobgoblin")]
    [InlineData("Harp123|Fw0.1.0|Hobgoblin", 123, new byte[] { 0, 1, 0 }, "Hobgoblin")]
    [InlineData("Harp123 | Fw0.1.0 | Hobgoblin", 123, new byte[] { 0, 1, 0 }, "Hobgoblin")]
    [InlineData("Harp1152|Fw1.0.0|Clock Synchronizer", 1152, new byte[] { 1, 0, 0 }, "Clock Synchronizer")]
    [InlineData("Harp1401 | Fw0.2.1 | Sniff Detector (Far corner)", 1401, new byte[] { 0, 2, 1 }, "Sniff Detector (Far corner)")]
    [InlineData("Not a Harp Device", null, null, "Not a Harp Device")]
    [InlineData(" Harp123 | Whitespace only trimmed from delimiters", null, null, " Harp123 | Whitespace only trimmed from delimiters")]
    [InlineData("Harp123 | Whitespace only trimmed from delimiters ", 123, null, "Whitespace only trimmed from delimiters ")]
    [InlineData("Harp123 | Version comes before description | Fw1.0.0", 123, null, "Version comes before description | Fw1.0.0")]
    [InlineData("", null, null, null)]
    public void BasicUsbDescriptionTests(string usbDescription, int? expectedWhoAmI, byte[]? versionParts, string? expectedDeviceDescription)
    {
        Device device = new()
        {
            Source = "Test",
            Confidence = DeviceConfidence.Zero,
        };

        device = device.WithMetadataFromUsbDescription(usbDescription);

        Assert.Equal(checked((ushort?)expectedWhoAmI), device.WhoAmI);

        HarpVersion? expectedVersion = versionParts is null ? null : new HarpVersion(versionParts[0], versionParts[1], versionParts[2]);
        Assert.Equal(expectedVersion, device.FirmwareVersion);

        Assert.Equal(expectedDeviceDescription, device.DeviceDescription);

        // Only devices which get a WhoAmI from the description have their confidence promoted
        Assert.Equal
        (
            expectedWhoAmI is null ? DeviceConfidence.Zero : DeviceConfidence.High,
            device.Confidence
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("Harp123")]
    [InlineData("Harp123|")]
    [InlineData("Harp123 | ")]
    public void DeviceDescriptionUnaffectedWheMissingFromUsbDescription(string usbDescription)
    {
        const string expectedDescription = "Test Device Name";
        Device device = new()
        {
            Source = "Test",
            DeviceDescription = expectedDescription,
        };

        device = device.WithMetadataFromUsbDescription(usbDescription);
        Assert.Equal(expectedDescription, device.DeviceDescription);
    }

    [Fact]
    public void PayloadType_Int32_IsSignedButNotFloat()
    {
        PayloadType payloadType = PayloadType.GetType<int>();

        Assert.True(payloadType.IsSigned);
        Assert.False(payloadType.IsFloat);
        Assert.Equal(typeof(int), payloadType.Type);
    }

    [Fact]
    public void PayloadType_Float32_IsFloatButNotSigned()
    {
        PayloadType payloadType = PayloadType.GetType<float>();

        Assert.False(payloadType.IsSigned);
        Assert.True(payloadType.IsFloat);
        Assert.Equal(typeof(float), payloadType.Type);
    }

    [Fact]
    public void PayloadType_BothSignedAndFloat_IsInvalidButNonThrowingInTryGetType()
    {
        PayloadType payloadType = new(hasTimestamp: false, isSigned: true, isFloat: true, numBits: 16);

        Assert.False(payloadType.TryGetType(out var type));
        Assert.Null(type);
    }

    [Fact]
    public void PayloadType_UnknownWidth_IsInvalidButNonThrowingInTryGetType()
    {
        PayloadType payloadType = Unsafe.BitCast<byte, PayloadType>(0x03);

        Assert.Equal(24, payloadType.NumBits);
        Assert.False(payloadType.TryGetType(out var type));
        Assert.Null(type);
    }
}
