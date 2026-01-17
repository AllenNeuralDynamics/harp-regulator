Identifying Harp devices
===================================================================================================

This document outlines how Harp Regulator (via `Harp.Devices`) identifies whether a particular serial port represents a Harp device.

Harp Regulator attempts to identify devices without connecting to them using the Harp protocol, and only connects to them if permitted. (Connections via non-Harp interfaces such as Picoboot is permitted.)

Harp Regulator can identify devices with the following levels of confidence:

* High Confidence - The deivce is definitely a Harp device
* Low Confidence - The device is maybe a Harp device
* Zero Confidence - All other serial ports

The following kinds of devices can be identified:

* Pico - RP2040 and RP2350-based devices
* ATxmega - ATxmega-based devices
* FTDI - Unknown devices using an FTDI serial interface
* Unknown - All other devices

Devices may be in the following states:

* Online - The normal operating state where they can respond to Harp messages
* Bootloader - The state where firmware updates can be accepted, but Harp messages will not be accepted
* Unknown - The state of the device cannot be known without attempting to connect to it

Devices which support online firmware updates (IE: devices which meet the definitions of both Online and Bootloader) shall be regarded as Online.

<!-- TOC -->

- [Online Pico device](#online-pico-device)
- [Bootloader Pico device](#bootloader-pico-device)
- [Unknown FTDI-based device](#unknown-ftdi-based-device)
    - [Unknown ATxmega device](#unknown-atxmega-device)
- [USB descriptor identification](#usb-descriptor-identification)

<!-- /TOC -->

## Online Pico device

A serial port is identified as an online Pico device with Low Confidence if it is a USB serial port with the following attributes:

* Vendor ID: 0x2e8a (`VENDOR_ID_RASPBERRY_PI`)
* Product ID:
    * RP2040-based devices: 0x000a (`PRODUCT_ID_RP2040_STDIO_USB`)
    * RP2350-based devices: 0x0009 (`PRODUCT_ID_STDIO_USB`)

The device is promoted to High Confidence if it meets the USB descriptor specification described below.

## Bootloader Pico device

A USB device is identified as a Pico device in bootloader mode with Low Confidence if it has all of the following attributes:

* Vendor ID: 0x2e8a (`VENDOR_ID_RASPBERRY_PI`)
* Product ID:
    * RP2040-based devices: 0x0003 (`PRODUCT_ID_RP2040_USBBOOT`)
    * RP2350-based devices: 0x000f (`PRODUCT_ID_RP2350_USBBOOT`)
* Interface class: 0xff (Vendor USB interface)
    * The bootrom of the RP2040 and RP2350 additionally designate the Picoboot interface as subclass 0x00, but Picotool does not check this so we do not do so either.

The device is promoted to High Confidence if the firmware on the device has [binary information](https://github.com/raspberrypi/picotool/blob/de8ae5ac334e1126993f72a5c67949712fd1e1a4/README.md#binary-information) with ID `RP_PROGRAM_DESCRIPTION` meeting the USB descriptor specification described below.

## Unknown FTDI-based device

A serial port is identified as an FTDI-based device in an Unknown state if it has a USB vendor ID of 0x0403.

These devices can be regarded as Low Confidence ATxmega-based Harp devices.

*(Non-normative: The idea behind Low Confidence device identificaiton is to help migrate older devices to conform to this specification.)*

### FTDI devices and Harp protocol identification

FTDI-based devices that do not have Harp metadata in their USB descriptor can be identified as ATxmega devices by connecting to them using the Harp protocol.

When running `HarpRegulator list --allow-connect`, the application will attempt to connect to FTDI devices and read their WhoAmI register. If successful:

* The device is promoted from `FTDI` to `ATxmega` kind
* The confidence is promoted to `High`
* Additional metadata (WhoAmI, DeviceName, FirmwareVersion, SerialNumber) is populated

This approach mirrors how `harp-tech/toolkit` and `Bonsai.Harp` identify devices - by connecting to each serial port and reading Harp protocol registers.

### Unknown ATxmega device

An Unknown FTDI-based device is promoted to an Unknown-state ATxmega device with high confidence if it meets the USB descriptor specification described below.

(The running state of an FTDI-based device cannot be known without first connecting to it.)

## USB descriptor identification

Where noted above, metadata about devices can be read from their USB descriptor in order to promote their confidence level.

The metadata is encoded as a string in the form of `Harp<WhoAmI>[|Fw<FirmwareVersion>][|<Description>]` where:

* `<WhoAmI>` is the [WhoAmI](https://github.com/harp-tech/whoami) a numeric identifier assigned to the device (IE: the value read from the `R_WHO_AM_I` register.)
    * Currently this value must be between 0 and 65535.
* `<FirmwareVersion>` is the firmware version in `<Major>.<Minor>.<Patch>` format.
    * 🧶🧶🧶TODO: Is it actually a good idea to include the firmware version? Does having this string change over time cause undesirable effects? (Such as causing Windows to reassign COM ports) - It also would not work with the RP2350's OTP.
* 🧶🧶🧶TODO: Should we encode the serial number here too? That way we could get serial number when device is online without connecting to it.
    * (The serial number wouldn't be present in the firmware metadata)
* `<Description>` is an optional human-readable description for the device. (This may be the name of the device such as "Sound Card" or a user-defined string "NE Nose Poke")

Leading and trailing whitespace around each section is ignored.

Markers like `Harp` and `Fw` are case sensitive.

Some valid Harp device identifiers might be:

* `Harp0`
* `Harp123`
* `Harp123|Hobgoblin`
* `Harp123|Fw0.1.0|Hobgoblin`
* `Harp123 | Fw0.1.0 | Hobgoblin`
* `Harp1152|Clock Synchronizer`
* `Harp1401|Sniff Detector (Far corner)`

The metadata **must** be encoded in the string descriptor (IE: `iInterface`) of the USB interface corresponding to the seiral port (IE: the USB communications device class interface) which will be used for Harp communication.

The metadata **must** also be present in the USB device descriptor in the product field (IE: `iProduct`.)

*(Non-normative: The `iInterface` field of the CDC interface is what Harp Regulator uses, but both are required to enable easier Harp device identification with other enumeration strategies.)*

*(Non-normative: It is intentional that Harp Regulator does not try to expose the serial number or manufacturer from the USB descriptor as neither is exposed in Windows.)*
