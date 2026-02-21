using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarpRegulator;

internal abstract class CommandBase
{
    public abstract string Verb { get; }
    public abstract string Description { get; }
    public abstract string? UsageHelp { get; }
    public abstract string? ArgumentsHelp { get; }

    private static TraceListener? VerboseListener = null;
    public bool VerboseMode => VerboseListener is not null;

    private static int _DisplayWidth = 0;
    protected static int DisplayWidth
    {
        get
        {
            return _DisplayWidth > 0 ? _DisplayWidth : (_DisplayWidth = GetDisplayWidth());

            [MethodImpl(MethodImplOptions.NoInlining)]
            int GetDisplayWidth()
            {
                if (Console.IsOutputRedirected)
                    return int.MaxValue;

                int result = 80;
                try
                { result = Console.BufferWidth; }
                catch (PlatformNotSupportedException)
                { }

                if (result < 40)
                    result = 40;

                return result;
            }
        }
    }

    protected const string CommonArgumentHelp =
        """
        Common flags:
            --verbose
                Enables verbose logging.
        """;

    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    protected static readonly HarpRegulatorJsonSerializerContext JsonContext = new(JsonOptions);

    protected static CommonArgumentResult TryHandleCommonArgument(string argument)
    {
        switch (argument.ToLowerInvariant())
        {
            case "--verbose":
                if (VerboseListener is null)
                    // We use the error stream so that it can be separated from the JSON output
                    Trace.Listeners.Add(VerboseListener = new ColoredConsoleTraceListener(useErrorStream: true));
                return CommonArgumentResult.Handled;
            case "--help":
            case "-help":
            case "/help":
            case "-h":
            case "/h":
            case "-?":
            case "/?":
                return CommonArgumentResult.ShowHelp;
            default:
                return CommonArgumentResult.NotHandled;
        }
    }

    public static bool IsHelpArgument(string argument)
        => TryHandleCommonArgument(argument) == CommonArgumentResult.ShowHelp;

    public enum CommonArgumentResult
    {
        NotHandled,
        Handled,
        ShowHelp,
    }

    protected bool YesNo(string prompt, bool defaultChoice, bool? cancelChoice = null)
    {
        Console.WriteLine($"{prompt} ({(defaultChoice ? "Y/n" : "y/N")})");
        while (true)
        {
            switch (Console.ReadKey(intercept: true).Key)
            {
                case ConsoleKey.Escape:
                    return cancelChoice ?? defaultChoice;
                case ConsoleKey.Enter:
                    return defaultChoice;
                case ConsoleKey.Y:
                    return true;
                case ConsoleKey.N:
                    return false;
            }
        }
    }

    public abstract CommandResult Execute(Queue<string> arguments);
}
