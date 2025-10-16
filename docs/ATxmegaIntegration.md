# ATxmega Support Integration Guide

## Overview

The Harp Regulator application has been successfully enhanced to support ATxmega-based Harp devices in addition to the existing Raspberry Pi Pico support. This integration leverages the `Bonsai.Harp` package from the Harp CLI project to provide ATxmega bootloader functionality.

**Key Feature:** The `upload` command now **automatically detects** the device type based on the firmware file format, eliminating the need for separate commands for different device architectures.

## Changes Made

### 1. Package Dependencies

**File:** `src/HarpRegulator/HarpRegulator.csproj`

Added the following NuGet packages:
- `Bonsai.Harp` (v3.6.1) - Provides ATxmega bootloader support, firmware parsing, and device communication
- `System.IO.Ports` (v9.0.6) - Enables serial port communication for ATxmega devices

### 2. New Components

#### HexFileHelper.cs
**Location:** `src/HarpRegulator/HexFileHelper.cs`

A utility class for working with Intel HEX firmware files:
- `IsHexFile(string)` - Identifies Intel HEX files by extension
- `LoadFirmware(string)` - Loads firmware from a HEX file using `Bonsai.Harp.DeviceFirmware`
- `GetFirmwareInfo(string)` - Extracts firmware metadata

#### UploadFirmwareCommand.cs (Enhanced)
**Location:** `src/HarpRegulator/UploadFirmwareCommand.cs`

Enhanced the existing upload command to support both device types:
- **Automatic Detection**: Detects firmware file format (UF2 vs Intel HEX)
- **Unified Interface**: Single command for all device types
- **Pico Upload**: Handles UF2 firmware with PICOBOOT
- **ATxmega Upload**: Handles Intel HEX firmware via serial bootloader
- Validates serial port availability for ATxmega devices
- Verifies device compatibility by reading WhoAmI register
- Uploads firmware using `Bonsai.Harp.Bootloader.UpdateFirmwareAsync` for ATxmega
- Provides progress feedback during upload
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
- New `upload-atxmega` command documentation
- Usage examples for ATxmega devices
- Updated feature list

## Usage

### Unified Upload Command

The `upload` command automatically detects the firmware type and device architecture:

#### Uploading to ATxmega Devices (Intel HEX)

##### Windows
```bash
HarpRegulator upload firmware.hex --target COM4
```

##### Linux
```bash
HarpRegulator upload firmware.hex --target /dev/ttyUSB0
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
HarpRegulator upload firmware.hex --target COM4 --force

# Non-interactive mode
HarpRegulator upload firmware.hex --target COM4 --no-interactive

# Hide progress bar
HarpRegulator upload firmware.hex --target COM4 --no-progress

# Allow device connection for enumeration
HarpRegulator upload firmware.uf2 --target COM3 --allow-connect
```

### Inspecting Intel HEX Files
```bash
HarpRegulator inspect firmware.hex
HarpRegulator inspect firmware.hex --json
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
   - `Bonsai.Harp.DeviceFirmware.FromFile()` for ATxmega (Intel HEX parsing)
3. **Device Communication**: `Bonsai.Harp.AsyncDevice` provides Harp protocol communication for ATxmega
4. **Firmware Upload**: 
   - Pico: Direct flash operations via PICOBOOT
   - ATxmega: `Bonsai.Harp.Bootloader.UpdateFirmwareAsync()` manages the bootloader protocol
5. **Progress Reporting**: Uses `IProgress<int>` for upload progress tracking

## Device Compatibility Verification

The ATxmega upload command includes compatibility verification:

1. **Port Availability**: Checks if the specified serial port exists
2. **File Format**: Validates that the firmware file is in Intel HEX format
3. **Device Communication**: Attempts to read the WhoAmI register from the device
4. **Metadata Comparison**: Compares firmware metadata with device info (when available)

Verification can be bypassed using the `--force` flag.

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
| Bootloader Entry | Can trigger reboot to BOOTSEL | Device must be in bootloader mode |
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
2. **Recovery Mode**: Support for recovery from failed uploads

## Dependencies

### Runtime Dependencies
- `Bonsai.Harp` (v3.6.1)
  - Provides: `AsyncDevice`, `Bootloader`, `DeviceFirmware`, `FirmwareMetadata`
- `System.IO.Ports` (v9.0.6)
  - Provides: Serial port communication

### Harp CLI Reference
This integration is based on functionality from:
- Repository: https://github.com/harp-tech/harp-cli
- License: MIT
- Key files referenced:
  - `Harp.Toolkit/Program.cs` - Command structure and workflow
  - `Bootloader.UpdateFirmwareAsync` - Firmware upload implementation
  - `DeviceFirmware.FromFile` - Intel HEX parsing

## Troubleshooting

### Common Issues

**"Serial port not found"**
- Solution: Run `HarpRegulator list` to see available ports
- On Linux: Check user has permissions for serial devices (`/dev/ttyUSB*`)

**"Unable to find package Bonsai.Harp"**
- Solution: Ensure NuGet package sources include the Bonsai packages feed
- Check: https://www.myget.org/F/bonsai/api/v3/index.json

**"Device doesn't respond"**
- Solution: Verify device is in bootloader mode
- Check: Device COM port is correct and not in use by another application
- Try: Increase timeout with `--timeout 2000`

**"Firmware upload failed"**
- Solution: Try with `--force` flag if compatibility check is blocking
- Verify: Firmware file is valid Intel HEX format
- Check: Device has sufficient memory for the firmware

## Conclusion

The Harp Regulator now provides comprehensive support for both Raspberry Pi Pico and ATxmega-based Harp devices, offering a unified interface for firmware management across different hardware platforms. The integration maintains the tool's architecture while cleanly separating concerns between device types.
