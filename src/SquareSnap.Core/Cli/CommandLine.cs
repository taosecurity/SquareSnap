using System.Globalization;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Naming;

namespace SquareSnap.Core.Cli;

public enum Command
{
    /// <summary>No command: open the main window.</summary>
    Gui,
    /// <summary>Capture one square, save and/or copy it, then exit.</summary>
    Capture,
    /// <summary>Print where the settings, log and save folder are, then exit.</summary>
    Paths,
    Help,
    Version,
}

/// <summary>Process exit codes, listed in the README and AGENTS.md.</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Cancelled = 1;
    public const int BadArguments = 2;
    public const int CaptureFailed = 3;
    public const int SaveFailed = 4;
    /// <summary>SquareSnap.com (the terminal helper) couldn't find or start SquareSnap.exe.</summary>
    public const int StartFailed = 5;
}

/// <summary>Parsed command line. Null values mean "use the saved setting".</summary>
public sealed record CliOptions
{
    public Command Command { get; init; } = Command.Gui;
    public OutputSize? Size { get; init; }
    public string? OutFolder { get; init; }
    public string? NameTemplate { get; init; }
    public ImageFormat? Format { get; init; }
    public bool? Clipboard { get; init; }
    public int? DelaySeconds { get; init; }
    public bool RepeatLast { get; init; }
    /// <summary>Capture this exact square (physical pixels) without showing the overlay.</summary>
    public PixelRect? Region { get; init; }
    /// <summary>Keep the square after the drag so the arrow keys can adjust it; Enter captures.</summary>
    public bool? FineTune { get; init; }
    public int? FixedSize { get; init; }
    public bool Verbose { get; init; }
    public string? LogFile { get; init; }
    /// <summary>Under WSL: stay in Linux instead of handing over to the Windows exe (which can't capture: WSLg's screen can't be read).</summary>
    public bool Wslg { get; init; }
}

public sealed record ParseResult(CliOptions? Options, string? Error)
{
    public bool Ok => Error == null;
}

/// <summary>A small, dependency-free parser for SquareSnap's few options. Accepts --opt value and --opt=value.</summary>
public static class CommandLine
{
    public static ParseResult Parse(IReadOnlyList<string> args)
    {
        var o = new CliOptions();

        // The command may follow options (squaresnap --verbose capture), as in BejView since its beta 9.
        int commandAt = CommandIndex(args);
        if (commandAt >= 0)
        {
            switch (args[commandAt].ToLowerInvariant())
            {
                case "capture": o = o with { Command = Command.Capture }; break;
                case "paths": o = o with { Command = Command.Paths }; break;
                case "help": o = o with { Command = Command.Help }; break;
                case "version": o = o with { Command = Command.Version }; break;
                default: return Fail($"Unknown command '{args[commandAt]}'. Commands: capture, paths, help, version.");
            }
        }

        for (int i = 0; i < args.Count; i++)
        {
            if (i == commandAt) continue;
            string arg = args[i];
            string name = arg;
            string? inline = null;
            int eq = arg.IndexOf('=');
            if (arg.StartsWith("--") && eq > 2) { name = arg[..eq]; inline = arg[(eq + 1)..]; }

            string? Value()
            {
                if (inline != null) return inline;
                if (i + 1 < args.Count && !args[i + 1].StartsWith("--")) return args[++i];
                return null;
            }

            switch (name)
            {
                case "-h" or "--help": o = o with { Command = Command.Help }; break;
                case "--version": o = o with { Command = Command.Version }; break;
                case "-v" or "--verbose": o = o with { Verbose = true }; break;
                case "--wslg": o = o with { Wslg = true }; break;
                case "--clipboard": o = o with { Clipboard = true }; break;
                case "--no-clipboard": o = o with { Clipboard = false }; break;
                case "--last": o = o with { RepeatLast = true }; break;
                case "--fine-tune": o = o with { FineTune = true }; break;
                case "--no-fine-tune": o = o with { FineTune = false }; break;

                case "--region":
                {
                    string? v = Value();
                    if (!PixelRect.TryParse(v, out var r))
                        return Fail($"--region needs x,y,size,size in screen pixels, as in --region 100,200,512,512 (got '{v}').");
                    if (!r.IsSquare)
                        return Fail($"--region {v} isn't square: the width and height must be equal.");
                    if (r.Width < SquareSelection.MinimumSize)
                        return Fail($"--region {v} is too small; the smallest square is {SquareSelection.MinimumSize} pixels.");
                    o = o with { Region = r };
                    break;
                }

                case "--log":
                {
                    string? v = Value();
                    if (string.IsNullOrWhiteSpace(v)) return Fail("--log needs a file path, as in --log squaresnap.log.");
                    o = o with { LogFile = v };
                    break;
                }
                case "--out":
                {
                    string? v = Value();
                    if (string.IsNullOrWhiteSpace(v)) return Fail("--out needs a folder, as in --out ~/Pictures.");
                    o = o with { OutFolder = v };
                    break;
                }
                case "--name":
                {
                    string? v = Value();
                    string? problem = FileNamer.Validate(v);
                    if (problem != null) return Fail(problem);
                    o = o with { NameTemplate = v };
                    break;
                }
                case "--size":
                {
                    string? v = Value();
                    if (v == null) return Fail("--size needs a number of pixels (such as 1080) or 'native'.");
                    if (!OutputSize.TryParse(v, out var size, out string? err)) return Fail(err!);
                    o = o with { Size = size };
                    break;
                }
                case "--format":
                {
                    string? v = Value();
                    if (!ImageFormats.TryParse(v, out var f)) return Fail($"--format must be png, jpg or webp (got '{v}').");
                    o = o with { Format = f };
                    break;
                }
                case "--delay":
                {
                    string? v = Value();
                    if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int d) || d > 60)
                        return Fail($"--delay must be a whole number of seconds from 0 to 60 (got '{v}').");
                    o = o with { DelaySeconds = d };
                    break;
                }
                case "--fixed":
                {
                    string? v = Value();
                    if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int px) ||
                        px < SquareSelection.MinimumSize || px > OutputSize.MaxPixels)
                        return Fail($"--fixed must be a square size in pixels from {SquareSelection.MinimumSize} to {OutputSize.MaxPixels} (got '{v}').");
                    o = o with { FixedSize = px };
                    break;
                }
                default:
                    return Fail($"Unknown option '{arg}'. Run 'squaresnap --help' for the list.");
            }
        }

        int modes = (o.RepeatLast ? 1 : 0) + (o.FixedSize != null ? 1 : 0) + (o.Region != null ? 1 : 0);
        if (modes > 1)
            return Fail("Use only one of --last, --fixed and --region: each picks the square a different way.");

        bool captureOnly = o.Size != null || o.OutFolder != null || o.NameTemplate != null || o.Format != null ||
                           o.Clipboard != null || o.DelaySeconds != null || modes > 0 || o.FineTune != null;
        if (captureOnly && o.Command is Command.Gui or Command.Paths)
            return Fail("Capture options (--size, --out, --name, --format, --clipboard, --delay, --last, --fixed, --region, --fine-tune) " +
                        "go with the capture command, as in: squaresnap capture --size 1080");

        return new ParseResult(o, null);
    }

    private static ParseResult Fail(string message) => new(null, message);

    /// <summary>Options followed by a value in the next argument (unless written --opt=value).</summary>
    private static readonly HashSet<string> TakesValue = ["--region", "--log", "--out", "--name", "--size", "--format", "--delay", "--fixed"];

    /// <summary>Where the command is: the first argument that isn't an option or an option's value; -1 for none.</summary>
    public static int CommandIndex(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            if (!a.StartsWith('-')) return i;
            // Same rule as Value(): the next argument is the value unless it starts with "--".
            if (TakesValue.Contains(a) && i + 1 < args.Count && !args[i + 1].StartsWith("--")) i++;
        }
        return -1;
    }

    public const string HelpText = """
        SquareSnap: take square (1:1) screenshots.

        Usage:
          squaresnap                         open the window
          squaresnap capture [options]       drag a square, then save and/or copy it, and exit
          squaresnap paths                   show where settings, the log and captures go
          squaresnap --version | --help

        Capture options (each overrides the saved setting for this run only):
          --size N|native     save at N x N pixels (16-16384), or at the captured size
          --out DIR           save into DIR (created if missing)
          --name TEMPLATE     file name template: {date} {time} {size} {n:000}
                              (default SquareCap-{date}-{n:000}; with {date} in it, the counter restarts each day)
          --format png|jpg|webp
          --clipboard / --no-clipboard   copy the capture to the clipboard, or don't
          --delay S           wait S seconds (0-60) before freezing the screen
          --last              capture the same square as last time, without dragging
          --fixed N           click to place an N x N square instead of dragging
          --region X,Y,S,S    capture this exact square (screen pixels), without the overlay
          --fine-tune / --no-fine-tune   after the drag, adjust with the arrow keys and press Enter

        Logging and troubleshooting:
          -v, --verbose       log every step, and also print it to the terminal
          --log FILE          write the log to FILE instead of the default log file

        WSL:
          Under WSL, SquareSnap runs the Windows SquareSnap.exe so it can capture the Windows desktop;
          the saved file's path comes back as a /mnt/... path. Set SQUARESNAP_WINDOWS_EXE to its path
          if it isn't next to this program or on the Windows PATH.
          WSLg's Linux windows are on the Windows desktop, so they're captured too.
          --wslg              stay in Linux (it can't capture: WSLg doesn't let programs read its screen)

        Linux:
          SQUARESNAP_X11=1        use X11 (XWayland) windows instead of native Wayland
          SQUARESNAP_CAPTURE=M    capture only with M: portal, spectacle, grim, gnome-screenshot or x11

        Windows terminals:
          Type "squaresnap" without ".exe": that runs SquareSnap.com, which waits for SquareSnap
          and passes its text and exit code back to the terminal. The Microsoft Store and
          WinGet versions add a "squaresnap" command that works the same way everywhere.

        Exit codes:
          0  saved or copied
          1  cancelled
          2  bad arguments
          3  capture failed
          4  save failed
          5  couldn't start: SquareSnap.com couldn't find SquareSnap.exe (keep the two files together),
             or on Linux a system library is missing (the message names the package)

        Enjoy SquareSnap!
        """;
}
