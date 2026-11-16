using System.Globalization;
using SquareSnap.Core.Cli;
using SquareSnap.Core.Naming;

namespace SquareSnap.Core.Platform;

/// <summary>What the Linux build does with a command line when it starts.</summary>
public enum BridgeAction
{
    /// <summary>Run here: help, version, paths, --wslg, or anything not under WSL.</summary>
    RunHere,
    /// <summary>Hand the run to the Windows SquareSnap.exe.</summary>
    UseWindowsExe,
}

/// <summary>Everything the bridge decision depends on, gathered once so the decision itself has no side effects.</summary>
public sealed record BridgeEnvironment
{
    public bool IsWsl { get; init; }
    /// <summary>WSL can run Windows programs (the WSLInterop handler is registered and enabled).</summary>
    public bool InteropEnabled { get; init; }
    /// <summary>Where the Windows drives are mounted: /mnt/ unless /etc/wsl.conf says otherwise.</summary>
    public string MountRoot { get; init; } = "/mnt/";
    public string CurrentDirectory { get; init; } = "/";
    /// <summary>The Linux home folder, for --out ~/... when the shell didn't expand it (--out=~/x).</summary>
    public string Home { get; init; } = "/root";
    /// <summary>The Windows SquareSnap.exe as a Linux path, or null if none was found.</summary>
    public string? WindowsExe { get; init; }
    /// <summary>How <see cref="WindowsExe"/> was found (for the log).</summary>
    public string? WindowsExeSource { get; init; }
}

/// <summary>The bridge's answer, with the reason in plain words (logged with --verbose).</summary>
public sealed record BridgeDecision(BridgeAction Action, string Reason)
{
    public string? WindowsExe { get; init; }
    /// <summary>The arguments for SquareSnap.exe, one per item (started as an argument list, never through a shell).</summary>
    public IReadOnlyList<string> WindowsArgs { get; init; } = [];
    /// <summary>Wait for the Windows exe and return its exit code (capture, or --verbose).</summary>
    public bool Wait { get; init; }
    /// <summary>Turn Windows paths in its output (the saved file) into /mnt/... paths.</summary>
    public bool TranslateOutput { get; init; }
    /// <summary>A problem the user should hear about even without --verbose (the bridge was wanted but couldn't run).</summary>
    public string? Warning { get; init; }
}

/// <summary>
/// WSL bridge mode (step 5). A Linux program under WSL only sees WSLg's own windows, not the Windows desktop, so
/// under WSL "squaresnap" and "squaresnap capture" run the Windows SquareSnap.exe through WSL interop: it captures
/// the real Windows desktop, and the saved file's path comes back as a /mnt/... path. --wslg keeps the run in Linux,
/// where capturing isn't possible (WSLg's screen can't be read; beta 13's test, 2026-11-13). help, version and paths always run in Linux. The same pattern as BejView's bridge.
/// </summary>
public static class WslBridge
{
    public const string ExeEnvironmentVariable = "SQUARESNAP_WINDOWS_EXE";
    public const string ExeName = "SquareSnap.exe";

    public static BridgeDecision Decide(CliOptions o, BridgeEnvironment env, Func<string, string> toWindowsPath)
    {
        if (!env.IsWsl)
            return Here(o.Wslg ? "Not running under WSL, so --wslg has nothing to change." : "Not running under WSL.");
        if (o.Command is Command.Help or Command.Version or Command.Paths)
            return Here($"'{o.Command.ToString().ToLowerInvariant()}' runs in Linux.");
        if (o.Wslg)
            return Here("--wslg given: staying in Linux, where WSLg's screen can't be captured.");

        const string fallback = " The Linux SquareSnap can't capture under WSL on its own (WSLg doesn't let programs read its screen).";
        if (!env.InteropEnabled)
            return Here("WSL can't start Windows programs here (interop is off).")
                with { Warning = "WSL interop is off ([interop] enabled=false in /etc/wsl.conf), so the Windows SquareSnap can't be started." + fallback };
        if (env.WindowsExe == null)
            return Here($"No Windows {ExeName} was found.")
                with
                {
                    Warning = $"No Windows {ExeName} found (looked in ${ExeEnvironmentVariable}, next to this program and on PATH)." + fallback +
                              $" To capture the Windows desktop, set {ExeEnvironmentVariable} to its path, for example: export {ExeEnvironmentVariable}=\"/mnt/c/Tools/SquareSnap/{ExeName}\"",
                };

        bool capture = o.Command == Command.Capture;
        return new BridgeDecision(BridgeAction.UseWindowsExe,
            $"Under WSL: running the Windows SquareSnap ({env.WindowsExe}, found {env.WindowsExeSource ?? "by search"}) so it captures the Windows desktop.")
        {
            WindowsExe = env.WindowsExe,
            WindowsArgs = WindowsArguments(o, env, toWindowsPath),
            Wait = capture || o.Verbose,
            TranslateOutput = capture,
        };
    }

    /// <summary>The options, rebuilt for SquareSnap.exe: --out and --log become Windows paths, --wslg is dropped.</summary>
    public static IReadOnlyList<string> WindowsArguments(CliOptions o, BridgeEnvironment env, Func<string, string> toWindowsPath)
    {
        var a = new List<string>();
        if (o.Command == Command.Capture) a.Add("capture");
        if (o.Size is { } size) { a.Add("--size"); a.Add(size.ToString()); }
        if (o.OutFolder != null) { a.Add("--out"); a.Add(ForWindows(o.OutFolder, env, toWindowsPath)); }
        if (o.NameTemplate != null) { a.Add("--name"); a.Add(o.NameTemplate); }
        if (o.Format is { } f) { a.Add("--format"); a.Add(f.Extension().TrimStart('.')); }
        if (o.Clipboard is bool clip) a.Add(clip ? "--clipboard" : "--no-clipboard");
        if (o.DelaySeconds is int d) { a.Add("--delay"); a.Add(d.ToString(CultureInfo.InvariantCulture)); }
        if (o.RepeatLast) a.Add("--last");
        if (o.FixedSize is int px) { a.Add("--fixed"); a.Add(px.ToString(CultureInfo.InvariantCulture)); }
        if (o.Region is { } r) { a.Add("--region"); a.Add(r.ToString()); }
        if (o.FineTune is bool ft) a.Add(ft ? "--fine-tune" : "--no-fine-tune");
        if (o.Verbose) a.Add("--verbose");
        if (o.LogFile != null) { a.Add("--log"); a.Add(ForWindows(o.LogFile, env, toWindowsPath)); }
        return a;
    }

    /// <summary>
    /// One line of the Windows exe's output, for the Linux terminal: a line that is a Windows path (the saved file)
    /// becomes a Linux path; anything else is passed on unchanged.
    /// </summary>
    public static string TranslateOutputLine(string line, Func<string, string> toLinuxPath)
    {
        string t = line.TrimEnd('\r');
        if (!Wsl.LooksLikeWindowsPath(t.Trim())) return t;
        try { return toLinuxPath(t.Trim()); }
        catch (ArgumentException) { return t; }
    }

    /// <summary>
    /// Looks for the Windows SquareSnap.exe, in this order: $SQUARESNAP_WINDOWS_EXE (a Linux or Windows path), the
    /// folder this program runs from, then each Windows folder on PATH (WSL adds the Windows PATH by default).
    /// Returns the Linux path and where it was found, or (null, null).
    /// </summary>
    public static (string? Path, string? Source) FindWindowsExe(Func<string, string?> env, Func<string, bool> fileExists,
        string? ownDirectory, string mountRoot)
    {
        string? configured = env(ExeEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            // A configured path that doesn't exist isn't silently replaced by a search: that would hide the typo.
            var (p, problem) = CheckConfigured(configured, fileExists, mountRoot);
            return problem == null ? (p, $"in ${ExeEnvironmentVariable}") : (null, null);
        }

        if (!string.IsNullOrEmpty(ownDirectory))
        {
            string p = ownDirectory.TrimEnd('/') + "/" + ExeName;
            if (fileExists(p)) return (p, "next to this program");
        }

        foreach (string dir in (env("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Wsl.IsOnDriveMount(dir, mountRoot)) continue; // Windows folders only
            string p = dir.TrimEnd('/') + "/" + ExeName;
            if (fileExists(p)) return (p, "on PATH");
        }
        return (null, null);
    }

    /// <summary>Why FindWindowsExe returned nothing when $SQUARESNAP_WINDOWS_EXE is set, so the warning can name the bad value.</summary>
    public static string? ConfiguredExeProblem(Func<string, string?> env, Func<string, bool> fileExists, string mountRoot)
    {
        string? configured = env(ExeEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured) ? null : CheckConfigured(configured, fileExists, mountRoot).Problem;
    }

    /// <summary>$SQUARESNAP_WINDOWS_EXE read once, for both <see cref="FindWindowsExe"/> and
    /// <see cref="ConfiguredExeProblem"/>: the Linux path, or why it can't be used.</summary>
    private static (string? Path, string? Problem) CheckConfigured(string configured, Func<string, bool> fileExists, string mountRoot)
    {
        string p = configured.Trim().Trim('"');
        if (!p.StartsWith('/'))
        {
            try { p = Wsl.ToLinuxPath(p, mountRoot); }
            catch (ArgumentException) { return (null, $"${ExeEnvironmentVariable} is '{configured}', which isn't a full Linux or Windows path."); }
        }
        return fileExists(p) ? (p, null) : (null, $"${ExeEnvironmentVariable} is '{configured}', but {p} doesn't exist.");
    }

    private static BridgeDecision Here(string reason) => new(BridgeAction.RunHere, reason);

    /// <summary>A path for SquareSnap.exe: a Windows path (C:\x, \\server\share) is passed on as given; a Linux
    /// path is made absolute and turned into a Windows one. Before 2.0.1 a Windows path was joined onto the Linux
    /// working folder.</summary>
    private static string ForWindows(string path, BridgeEnvironment env, Func<string, string> toWindowsPath) =>
        Wsl.LooksLikeWindowsPath(path) ? path : toWindowsPath(Absolute(path, env));

    private static string Absolute(string path, BridgeEnvironment env)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal)) path = env.Home.TrimEnd('/') + path[1..];
        return path.StartsWith('/') ? Normalize(path) : Normalize(env.CurrentDirectory.TrimEnd('/') + "/" + path);
    }

    /// <summary>Removes . and .. and doubled slashes, the Linux way (done by hand so tests on Windows give Linux answers).</summary>
    public static string Normalize(string linuxPath)
    {
        var parts = new List<string>();
        foreach (string part in linuxPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(part);
        }
        return "/" + string.Join('/', parts);
    }
}
