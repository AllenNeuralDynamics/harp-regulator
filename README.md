# Harp Regulator

> **Regulator** (musical)
>
> Noun. A person who maintains and tunes ("regulates") a harp.

⚠ This tool is in alpha and relies on Harp specifications which have not been ratified. 

This tool provides firmware management and device inspection capabilities for Harp devices based on multiple microcontroller architectures:
- **Raspberry Pi Pico (RP2040/RP2350)**: Supports UF2 firmware files and PICOBOOT mode for Pico-based devices
- **ATxmega**: Supports Intel HEX firmware files for ATxmega-based devices using the standard Harp bootloader

## Features

- **Device Enumeration**: List all connected Harp devices with detailed information
- **Firmware Upload**: 
  - Upload UF2 firmware to Pico-based devices
  - Upload Intel HEX firmware to ATxmega-based devices
- **Firmware Inspection**: Inspect and validate firmware files before uploading
- **Device Inspection**: Query device information via Harp protocol

## Commands

### list
Displays information about Harp devices connected to the system.

```bash
HarpRegulator list [--json] [--all[!]] [--allow-connect[!]]
```

### upload
Uploads firmware to Harp devices. **Automatically detects device type** (Pico or ATxmega) based on firmware file format:
- **UF2 files** → Pico-based devices
- **Intel HEX files** → ATxmega-based devices

```bash
HarpRegulator upload <firmware-file> --target <device> [options]
```

Examples:
```bash
# Upload UF2 firmware to Pico device
HarpRegulator upload firmware.uf2 --target COM3
HarpRegulator upload firmware.uf2 --target PICOBOOT

# Upload Intel HEX firmware to ATxmega device
HarpRegulator upload Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --target COM4
HarpRegulator upload Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --target /dev/ttyUSB0
```

ATxmega uploads use [Harp Toolkit](https://github.com/harp-tech/toolkit) Core 0.4.0.
Intel HEX filenames must follow `<device>-fw<firmware>-harp<core>-hw<hardware>-ass<assembly>.hex`;
metadata is read from the filename, not the image contents. Device name and hardware compatibility
are checked before resetting the device. Use `--force` only for an intentional override or to
recover a device already in bootloader mode. After writing, Regulator waits up to 20 seconds
for the device to respond. `--no-upload` validates the file without connecting; `--no-reboot`
is not supported for ATxmega uploads because Toolkit always restarts the device.

Toolkit Core still depends on Bonsai.Harp transitively; this replaces Regulator's direct
Bonsai API usage, not the underlying protocol dependency.

### inspect
Shows information about firmware files (supports both UF2 and Intel HEX formats).

```bash
HarpRegulator inspect <firmware-file> [--json]
```

### install-drivers
Installs necessary USB drivers (Windows only).

```bash
HarpRegulator install-drivers
```

## Usage Examples

### List all connected Harp devices
```bash
HarpRegulator list
```

### Upload firmware (auto-detects device type)
```bash
# Pico device with UF2 firmware
HarpRegulator upload firmware.uf2 --target COM3
HarpRegulator upload firmware.uf2 --target PICOBOOT

# ATxmega device with Intel HEX firmware  
HarpRegulator upload Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --target COM4
HarpRegulator upload Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --target /dev/ttyUSB0

# Force upload without compatibility checks
HarpRegulator upload Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --target COM4 --force

# Show progress during upload
HarpRegulator upload Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --target COM4 --progress
```

### Inspect a firmware file
```bash
# Inspect UF2 firmware
HarpRegulator inspect firmware.uf2

# Inspect Intel HEX firmware
HarpRegulator inspect Behavior-fw3.3-harp1.15-hw2.0-ass0.hex

# JSON output
HarpRegulator inspect Behavior-fw3.3-harp1.15-hw2.0-ass0.hex --json
```

## Building

Ensure the dependencies listed below for your platform are installed and run `dotnet build` in the root.

### Windows

| Depdency | Version known to work |
|----------|-----------------------|
| [Visual Studio](https://visualstudio.microsoft.com/vs/) w/ C++ workload | 17.14.7 |
| [.NET 8 SDK](https://dot.net/) | 8.0.411 |
| [CMake](https://cmake.org/) | 3.30.2 |

### Linux

Linux support is not well-tested and may require refinement. Building is known to work on Ubuntu 24.04 Noble x64

| Depdency | Version known to work |
|----------|-----------------------|
| `build-essential` | 12.10ubuntu1 |
| `libusb-1.0-0-dev` | 2:1.0.27-1 |
| `dotnet-sdk-8.0` | 8.0.116-0ubuntu1~24.04.1 |
