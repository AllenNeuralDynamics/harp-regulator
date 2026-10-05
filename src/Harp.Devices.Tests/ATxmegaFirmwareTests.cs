using HarpRegulator;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Harp.Devices.Tests;

public sealed class ATxmegaFirmwareTests : IDisposable
{
    const string FirmwareName = "Behavior-fw3.3-harp1.15-hw2.0-ass0";
    const string ValidHex = ":0400000001020304F2\n:00000001FF\n";
    readonly string directory = Path.Combine(Path.GetTempPath(), "HarpRegulatorTests", Guid.NewGuid().ToString("N"));

    public ATxmegaFirmwareTests()
    {
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }

    string WriteFirmware(string fileName = FirmwareName + ".hex", string contents = ValidHex)
    {
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    [Theory]
    [InlineData(".hex")]
    [InlineData(".HEX")]
    [InlineData(".mcs")]
    [InlineData(".int")]
    [InlineData(".ihex")]
    [InlineData(".ihe")]
    [InlineData(".ihx")]
    public void LoadFirmware_PreservesSupportedExtensions(string extension)
    {
        string path = WriteFirmware(FirmwareName + extension);

        Assert.True(HexFileHelper.IsHexFile(path));
        var firmware = HexFileHelper.LoadFirmware(path);

        Assert.Equal(FirmwareName, firmware.Metadata.ToString());
        Assert.Equal("Behavior", firmware.Metadata.DeviceName);
        Assert.Equal(512, firmware.Data.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, firmware.Data[..4]);
        Assert.Equal(byte.MaxValue, firmware.Data[4]);
    }

    [Theory]
    [InlineData("Behavior-fw3.3-harp1.15-hw2.x-assx")]
    [InlineData("Behavior-fw3.3-harp1.15-hw2.0-ass0-preview1")]
    public void LoadFirmware_PreservesWildcardAndPreviewMetadata(string name)
    {
        var firmware = HexFileHelper.LoadFirmware(WriteFirmware(name + ".hex"));

        Assert.Equal(name, firmware.Metadata.ToString());
    }

    [Fact]
    public void LoadFirmware_RejectsMissingFile()
    {
        Assert.Throws<FileNotFoundException>(() => HexFileHelper.LoadFirmware(Path.Combine(directory, FirmwareName + ".hex")));
    }

    [Fact]
    public void LoadFirmware_RejectsUnsupportedExtension()
    {
        Assert.Throws<ArgumentException>(() => HexFileHelper.LoadFirmware(WriteFirmware(FirmwareName + ".bin")));
    }

    [Fact]
    public void LoadFirmware_RejectsInvalidMetadata()
    {
        Assert.Throws<ArgumentException>(() => HexFileHelper.LoadFirmware(WriteFirmware("firmware.hex")));
    }

    [Fact]
    public void LoadFirmware_RejectsInvalidChecksum()
    {
        string path = WriteFirmware(contents: ":0400000001020304F3\n:00000001FF\n");

        Assert.Throws<ArgumentException>(() => HexFileHelper.LoadFirmware(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InspectFirmware_SupportsTextAndJson(bool useJson)
    {
        Queue<string> arguments = new([WriteFirmware()]);
        if (useJson)
            arguments.Enqueue("--json");

        Assert.Equal(CommandResult.Success, new InspectCommand().Execute(arguments));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UploadFirmware_NoUploadDoesNotRequireDevice(bool force)
    {
        Queue<string> arguments = new([WriteFirmware(), "--target", "nonexistent-port", "--no-upload", "--no-interactive", "--no-progress"]);
        if (force)
            arguments.Enqueue("--force");

        Assert.Equal(CommandResult.Success, new UploadFirmwareCommand().Execute(arguments));
    }

    [Fact]
    public void UploadFirmware_NoRebootIsRejectedBeforeDeviceAccess()
    {
        Queue<string> arguments = new([WriteFirmware(), "--target", "nonexistent-port", "--no-reboot", "--no-interactive", "--no-progress"]);

        Assert.Equal(CommandResult.Failure, new UploadFirmwareCommand().Execute(arguments));
    }

    [Fact]
    public void UploadFirmware_ForceDoesNotBypassInvalidHex()
    {
        string path = WriteFirmware(contents: ":0400000001020304F3\n:00000001FF\n");
        Queue<string> arguments = new([path, "--target", "nonexistent-port", "--force", "--no-upload", "--no-interactive", "--no-progress"]);

        Assert.Equal(CommandResult.Failure, new UploadFirmwareCommand().Execute(arguments));
    }
}