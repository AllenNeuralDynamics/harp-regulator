# ATxmega Support Integration Guide

## Overview

Harp Regulator supports ATxmega-based Harp devices alongside Raspberry Pi Pico devices. ATxmega firmware parsing and uploads use `Harp.Toolkit.Core` 0.4.0 from https://github.com/harp-tech/toolkit.

The Core package targets .NET 8 and is pinned to an exact version because its public API is unstable. It still depends on `Bonsai.Harp` 3.6.1 for protocol communication, so Bonsai remains a transitive runtime dependency even though Regulator no longer calls its APIs directly.

**Key Feature:** The `upload` command now **automatically detects** the device type based on the firmware file format, eliminating the need for separate commands for different device architectures.

## Changes Made

### 1. Package Dependencies

**File:** `src/HarpRegulator/HarpRegulator.csproj`

Added the following NuGet packages:
- `Harp.Toolkit.Core` (exactly v0.4.0) - Provides ATxmega bootloader support, firmware parsing, and post-update readiness checks
- `System.IO.Ports` (v9.0.6) - Enables serial port communication for ATxmega devices

### 2. New Components

#### HexFileHelper.cs
**Location:** `src/HarpRegulator/HexFileHelper.cs`

A utility class for working with Intel HEX firmware files:
- `IsHexFile(string)` - Identifies Intel HEX files by extension
- `LoadFirmware(string)` - Loads firmware using `Harp.Toolkit.Firmware.ATxmega.DeviceFirmware`
- `GetFirmwareInfo(string)` - Reads firmware metadata from the filename

Filenames must follow `<device>-fw<firmware>-harp<core>-hw<hardware>-ass<assembly>.hex`, for example `Behavior-fw3.3-harp1.15-hw2.0-ass0.hex`. Versions have two components; hardware and assembly may use `x` wildcards. Preview builds may append `-preview<number>`. Generic names such as `firmware.hex` are not valid metadata, even with `--force`. HEX checksums are validated when loading the file.

#### UploadFirmwareCommand.cs (Enhanced)
**Location:** `src/HarpRegulator/UploadFirmwareCommand.cs`

Enhanced the existing upload command to support both device types:
- **Automatic Detection**: Detects firmware file format (UF2 vs Intel HEX)
- **Unified Interface**: Single command for all device types
- **Pico Upload**: Handles UF2 firmware with PICOBOOT
- **ATxmega Upload**: Handles Intel HEX firmware via serial bootloader
- Validates serial port availability for ATxmega devices
- Uses Toolkit's device-name and hardware compatibility check before resetting the device
- Uploads firmware using `Harp.Toolkit.Firmware.ATxmega.Bootloader.UpdateFirmwareAsync`
- Provides ordered stage and percentage feedback with `ImmediateProgress<UpdateProgress>`
- Waits up to 20 seconds for the device to answer Harp after flashing
- Honors `--no-upload` without connecting and rejects `--no-reboot` for actual ATxmega uploads
- Supports interactive and non-interactive modes

### 3. Modified Components

#### InspectCommand.cs
**File:** `src/HarpRegulator/InspectCommand.cs`

Enhanced to support Intel HEX firmware inspection:
- Added HEX file detection and parsing
- Displays firmware metadata from Intel HEX files
- Supports both JSON and human-readable output
- Maintains backward compatibility with UF2 files

#### Program.cs
**File:** `src/HarpRegulator/Program.cs`

Command registration remains streamlined with unified upload:
```csharp
AllCommands =
[
    new ListDevicesCommand(),
    new UploadFirmwareCommand(),    // Handles BOTH Pico (UF2) and ATxmega (HEX)
    new InspectCommand(),
    new InstallDriversCommand(),
]
```

#### README.md
**File:** `README.md`

Updated documentation to include:
- Overview of dual-architecture support (Pico and ATxmega)
- Unified `upload` command documentation
- Usage examples for ATxmega devices
- Updated feature list

## Usage

### Unified Upload Command

The `upload` command automatically detects the firmware type and device architecture:

#### Uploading to ATxmega Devices (Intel HEX)

##### Windows
```bash
HarpRegulator upload Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --target COM4
```

##### Linux
```bash
HarpRegulator upload Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --target /dev/ttyUSB0
```

#### Uploading to Pico Devices (UF2)

```bash
# By port
HarpRegulator upload firmware.uf2 --target COM3

# By PICOBOOT mode
HarpRegulator upload firmware.uf2 --target PICOBOOT

# By serial number
HarpRegulator upload firmware.uf2 --target 1234ABCD
```

### With Options
```bash
# Force upload without compatibility checks
HarpRegulator upload Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --target COM4 --force

# Non-interactive mode
HarpRegulator upload Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --target COM4 --no-interactive

# Hide progress bar
HarpRegulator upload Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --target COM4 --no-progress

# Validate the image without opening the serial port
HarpRegulator upload Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --target COM4 --no-upload

# Allow device connection for enumeration
HarpRegulator upload firmware.uf2 --target COM3 --allow-connect
```

### Inspecting Intel HEX Files
```bash
HarpRegulator inspect Behavior-fw3.3-harp1.15-hw2.0-ass0.hex
HarpRegulator inspect Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --json
```

### Listing Devices
The existing `list` command already enumerates serial port devices that could be ATxmega-based:
```bash
HarpRegulator list
HarpRegulator list --allow-connect
```

## Architecture

### Device Type Support Matrix

| Device Type | Firmware Format | Upload Command | Detection Method | Bootloader |
|-------------|----------------|----------------|------------------|------------|
| Raspberry Pi Pico (RP2040/RP2350) | UF2 | `upload` | File extension (`.uf2`) | PICOBOOT |
| ATxmega | Intel HEX | `upload` | File extension (`.hex`, etc.) | Harp Bootloader |

### Key Integration Points

1. **Automatic Detection**: File extension analysis determines device type (UF2 → Pico, HEX → ATxmega)
2. **Firmware Loading**: 
   - `Uf2File` for Pico devices
  - `Harp.Toolkit.Firmware.ATxmega.DeviceFirmware.FromFile()` for ATxmega (Intel HEX parsing)
3. **Device Communication**: Toolkit internally uses `Bonsai.Harp.AsyncDevice` for ATxmega protocol communication
4. **Firmware Upload**: 
   - Pico: Direct flash operations via PICOBOOT
  - ATxmega: `Harp.Toolkit.Firmware.ATxmega.Bootloader.UpdateFirmwareAsync()` manages compatibility checking, reset, writing, and restart
5. **Progress Reporting**: Uses Toolkit's `ImmediateProgress<UpdateProgress>` for ordered stage and percentage reports
6. **Readiness**: `FirmwareUpdate.WaitUntilReadyAsync()` confirms the device responds after restart

## Device Compatibility Verification

The ATxmega upload command includes compatibility verification:

1. **Port Availability**: Checks if the specified serial port exists
2. **File Format**: Validates that the firmware file is in Intel HEX format
3. **Device Communication**: Toolkit reads the hardware version and device name with a 2000 ms timeout per command
4. **Metadata Comparison**: Toolkit checks the device name and compatible hardware against filename metadata before reset

Verification can be bypassed using `--force`, but firmware parsing cannot. Interactive verification failures can be explicitly overridden; the retry then uses force. Failures after reset do not trigger an interactive retry.

## Error Handling

The implementation includes comprehensive error handling for:
- Missing or invalid firmware files
- Serial port connection issues
- Device communication timeouts
- Firmware upload failures
- Incompatible firmware/device combinations

## Pico vs ATxmega Upload Differences

| Feature | Pico (UF2) | ATxmega (Intel HEX) |
|---------|------------|---------------------|
| Detection | `.uf2` file extension | `.hex`, `.ihex`, etc. extensions |
| Firmware Format | UF2 binary blocks | Intel HEX ASCII format |
| Device Discovery | USB enumeration + serial | Serial port only |
| Bootloader Entry | Can trigger reboot to BOOTSEL | Toolkit resets running firmware into the bootloader; recovery uses `--force` |
| Flash Operations | Direct flash read/write/erase | Bootloader-managed upload |
| Target Selection | Port, serial number, or "PICOBOOT" | Serial port required |
| Command | `upload firmware.uf2 --target <device>` | `upload firmware.hex --target <port>` |

## Testing Recommendations

1. **Device Detection**: Verify ATxmega devices appear in `list` command output
2. **Firmware Inspection**: Test `inspect` command with various Intel HEX files
3. **Upload Workflow**: Test complete upload with valid firmware and device
4. **Error Cases**: Test with invalid files, wrong ports, disconnected devices
5. **Interactive Mode**: Verify prompts and user interactions work correctly
6. **Progress Display**: Confirm progress bar updates correctly during upload

## Future Enhancements

Potential improvements:

1. **Multi-Device Upload**: Batch upload to multiple devices
2. **Configurable Timeouts**: Expose Toolkit's response and readiness timeouts as command options

## Dependencies

### Runtime Dependencies
- `Harp.Toolkit.Core` (exactly v0.4.0)
  - Provides: ATxmega `Bootloader`, `DeviceFirmware`, `FirmwareMetadata`, `FirmwareUpdate`, `ImmediateProgress<T>`
  - Transitively depends on `Bonsai.Harp` (v3.6.1)
- `System.IO.Ports` (v9.0.6)
  - Provides: Serial port communication

### Harp Toolkit Reference
This integration is based on functionality from:
- Repository: https://github.com/harp-tech/toolkit (v0.4.0)
- Documentation: https://harp-tech.org/toolkit/articles/update.html
- License: MIT
- Key files referenced:
  - `src/Harp.Toolkit.Core/Firmware/ATxmega/Bootloader.cs` - ATxmega update workflow
  - `Bootloader.UpdateFirmwareAsync` - Firmware upload implementation
  - `DeviceFirmware.FromFile` - Intel HEX parsing

## Troubleshooting

### Common Issues

**"Serial port not found"**
- Solution: Run `HarpRegulator list` to see available ports
- On Linux: Check user has permissions for serial devices (`/dev/ttyUSB*`)

**"Unable to find package Harp.Toolkit.Core"**
- Solution: Ensure NuGet package sources include https://api.nuget.org/v3/index.json
- Run `dotnet restore` to restore Toolkit and its transitive dependencies

**"Device doesn't respond"**
- Check: Device COM port is correct and not in use by another application
- If an interrupted update left the device in bootloader mode, repeat the upload with `--force`; power cycle first if recovery fails
- Regulator currently uses a fixed 2000 ms response timeout and a 20-second post-update readiness timeout

**"Invalid Harp firmware metadata specification string"**
- Check: The filename follows the convention above; metadata is not extracted from HEX contents

**"Firmware upload failed"**
- Solution: Try with `--force` flag if compatibility check is blocking
- Verify: Firmware file is valid Intel HEX format
- Check: Device has sufficient memory for the firmware

## Conclusion

The Harp Regulator now provides comprehensive support for both Raspberry Pi Pico and ATxmega-based Harp devices, offering a unified interface for firmware management across different hardware platforms. The integration maintains the tool's architecture while cleanly separating concerns between device types.
