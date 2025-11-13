using Harp.Devices;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HarpRegulator;

internal sealed class ListDevicesCommand : CommandBase
{
    public override string Verb => "list";
    public override string Description => "Displays information about Harp devices connected to this system.";
    public override string UsageHelp => "list [--json] [--all[!]] [--allow-connect[!]]";

    public override string ArgumentsHelp =>
        """
        --json
            Formats the output using JSON.

        --all
        --all!
            Include all devices, even ones that are not definitively Harp devices.
            Useful when interacting with older Harp devices which do not explicitly identify themselves.
            Adding a ! will include all serial ports, even ones that *really* don't seem like Harp devices.

        --allow-connect
        --allow-connect!
            Allow Harp Regulator to connect to the devices over the Harp protocol in ord to enumerate missing metadata.
            Adding a ! will attempt to connect to all serial ports, even ones that *really* don't seem like Harp devices.
        """;

    public override CommandResult Execute(Queue<string> arguments)
    {
        bool useJson = false;
        DeviceConfidence deviceFilter = DeviceConfidence.High;
        bool allowConnect = false;
        DeviceConfidence connectionFilter = DeviceConfidence.High;

        // Parse arguments
        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();
            switch (argument.ToLowerInvariant())
            {
                case "--json":
                    useJson = true;
                    break;
                case "--all":
                    deviceFilter = deviceFilter.DemoteTo(DeviceConfidence.Low);
                    break;
                case "--all!":
                    deviceFilter = deviceFilter.DemoteTo(DeviceConfidence.Zero);
                    break;
                case "--allow-connect":
                    connectionFilter = connectionFilter.DemoteTo(DeviceConfidence.Low);
                    allowConnect = true;
                    break;
                case "--allow-connect!":
                    connectionFilter = connectionFilter.DemoteTo(DeviceConfidence.Zero);
                    allowConnect = true;
                    break;
                default:
                    switch (TryHandleCommonArgument(argument))
                    {
                        case CommonArgumentResult.Handled:
                            break;
                        case CommonArgumentResult.NotHandled:
                            Console.Error.WriteLine($"Unknown argument '{argument}'");
                            return CommandResult.ShowHelp;
                        case CommonArgumentResult.ShowHelp:
                            return CommandResult.ShowHelp;
                        default:
                            throw new UnreachableException();
                    }
                    break;
            }
        }

        // Don't try to connect to devices that will be filtered out
        if (connectionFilter < deviceFilter)
            connectionFilter = deviceFilter;

        // List devices
        ImmutableArray<Device> devices = Device.EnumerateDevices(allowConnect ? connectionFilter : null);

        if (useJson)
        {
            string json = JsonSerializer.Serialize(devices.Where(d => d.Confidence >= deviceFilter), JsonOptions);
            Console.WriteLine(json);
            return CommandResult.Success;
        }
        else
        {
            ListDevices(devices, deviceFilter);
            return CommandResult.Success;
        }
    }

    public static void ListDevices(ImmutableArray<Device> devices, DeviceConfidence deviceFilter = DeviceConfidence.Zero, TextWriter? output = null)
    {
        output ??= Console.Out;
        List<string[]> rows = new(devices.Length + 1);
        rows.Add(["Port", "Serial", "Kind", "Status", "WhoAmI", "Description", "Hardware", "Firmware"]);

        foreach (Device device in devices)
        {
            if (device.Confidence < deviceFilter)
                continue;

            rows.Add
            (
                [
                    device.PortName ?? "N/A",
                    device.SerialNumber?.ToString("x") ?? "N/A",
                    device.Kind.ToString(),
                    device.State switch
                    {
                        DeviceState.DriverError => "Driver Error",
                        DeviceState.Unknown when device.Confidence == DeviceConfidence.Zero => "N/A",
                        _ => device.State.ToString()
                    },
                    device.WhoAmI?.ToString() ?? "N/A",
                    device.DeviceDescription ?? "N/A",
                    device.HardwareVersion?.ToString() ?? "N/A",
                    device.FirmwareVersion?.ToString() ?? "N/A",
                ]
            );
        }



        // Measure columns
        int[] columnWidths = new int[rows[0].Length];
        foreach (string[] row in rows)
        {
            if (row.Length != columnWidths.Length)
                throw new InvalidOperationException("Table is malformed.");

            for (int i = 0; i < row.Length; i++)
            {
                ref int columnWidth = ref columnWidths[i];
                columnWidth = Math.Max(columnWidth, row[i].Length);
            }
        }


        // No devices to list
        if (rows.Count == 1)
        {
            output.WriteLine("No Harp devices found.");
            int viableCount = devices.Count(d => d.Confidence > DeviceConfidence.Zero && d.Confidence < deviceFilter);
            if (viableCount > 0)
                output.WriteLine($"{viableCount} potential Harp device{(viableCount == 1 ? "" : "s")} were filtered out, try again with --all");
            else
                output.WriteLine($"None of the {devices.Length} serial port{(devices.Length == 1 ? "" : "s")} connected to this system appear like they could possibly be Harp devices.");
            return;
        }

        // Print table
        //TODO: Handle overflowing the width of the console
        ReadOnlySpan<char> fullSpace = new String(' ', columnWidths.Max());
        bool firstRow = true;
        foreach (string[] row in rows)
        {
            output.Write("|");
            int columnIndex = 0;
            foreach (string column in row)
            {
                output.Write($" {column}{fullSpace.Slice(0, columnWidths[columnIndex] - column.Length)} |");
                columnIndex++;
            }
            output.WriteLine();

            if (firstRow)
            {
                firstRow = false;
                output.Write("|");
                foreach (int width in columnWidths)
                    output.Write($"-{new String('-', width)}-|");
                output.WriteLine();
            }
        }
    }
}
