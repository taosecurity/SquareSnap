namespace SquareSnap.Core.Platform;

/// <summary>The ways SquareSnap can grab the screen on Linux.</summary>
public enum LinuxCaptureMethod
{
    /// <summary>xdg-desktop-portal Screenshot over D-Bus (xdg-desktop-portal-kde on Plasma). Works on any modern Wayland desktop.</summary>
    Portal,
    /// <summary>KDE's spectacle -b -n -f -o FILE.</summary>
    Spectacle,
    /// <summary>grim FILE (wlroots desktops: Sway, Hyprland, river).</summary>
    Grim,
    /// <summary>gnome-screenshot -f FILE.</summary>
    GnomeScreenshot,
    /// <summary>XGetImage on the X11 root window (an X11 session, or WSLg with --wslg).</summary>
    X11,
}

/// <summary>The portal host registry's answer for the app id (xdg-desktop-portal 1.19 and later).</summary>
public enum HostAppStatus { Registered, NotInstalled, NotSupported, Other }

/// <summary>One shortcut as the GlobalShortcuts portal reports it: its id and the key the desktop shows (empty: none).</summary>
public sealed record PortalShortcut(string Id, string? Trigger);

/// <summary>An external program to run, with its arguments as a list (never through a shell).</summary>
public sealed record ToolCommand(string Program, IReadOnlyList<string> Arguments)
{
    public override string ToString() => Program + " " + string.Join(' ', Arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}

/// <summary>
/// Linux decisions with no side effects, so they're tested on Windows too: which capture methods to try and in what
/// order, the command line for each screenshot tool, and which program keeps a capture on the clipboard after
/// SquareSnap exits. Every decision comes with a reason for the --verbose log.
/// </summary>
public static class LinuxPlan
{
    /// <summary>Forces one capture method: portal, spectacle, grim, gnome-screenshot or x11.</summary>
    public const string ForceCaptureVariable = "SQUARESNAP_CAPTURE";

    public static string Name(LinuxCaptureMethod m) => m switch
    {
        LinuxCaptureMethod.Portal => "portal",
        LinuxCaptureMethod.Spectacle => "spectacle",
        LinuxCaptureMethod.Grim => "grim",
        LinuxCaptureMethod.GnomeScreenshot => "gnome-screenshot",
        _ => "x11",
    };

    /// <summary>The program each tool method runs (null for the portal and X11, which are built in).</summary>
    public static string? Program(LinuxCaptureMethod m) => m switch
    {
        LinuxCaptureMethod.Spectacle => "spectacle",
        LinuxCaptureMethod.Grim => "grim",
        LinuxCaptureMethod.GnomeScreenshot => "gnome-screenshot",
        _ => null,
    };

    /// <summary>
    /// The capture methods to try, in order. A Wayland desktop: the portal, then spectacle, grim and gnome-screenshot
    /// (those that are installed). An X11 session: XGetImage first (exact and instant), then the portal and the tools.
    /// Under WSL (only reached with --wslg or when the bridge can't run): X11, because WSLg has no portal or tools.
    /// </summary>
    public static (IReadOnlyList<LinuxCaptureMethod> Order, string Reason) CaptureOrder(Func<string, string?> env, bool isWsl, Func<string, bool> programExists)
    {
        string? forced = env(ForceCaptureVariable)?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(forced))
        {
            var all = Enum.GetValues<LinuxCaptureMethod>();
            var match = all.Where(m => Name(m) == forced).ToList();
            if (match.Count == 1)
                return (match, $"{ForceCaptureVariable}={forced}, so only that method is tried.");
            // An unknown value is reported, and the normal order is used.
            var (order, reason) = CaptureOrder(n => n == ForceCaptureVariable ? null : env(n), isWsl, programExists);
            return (order, $"{ForceCaptureVariable}='{forced}' isn't one of {string.Join(", ", all.Select(Name))}; ignoring it. {reason}");
        }

        bool wayland = !string.IsNullOrEmpty(env("WAYLAND_DISPLAY"));
        bool x11 = !string.IsNullOrEmpty(env("DISPLAY"));
        var tools = new[] { LinuxCaptureMethod.Spectacle, LinuxCaptureMethod.Grim, LinuxCaptureMethod.GnomeScreenshot }
            .Where(m => programExists(Program(m)!)).ToList();
        string toolNote = tools.Count == 0 ? "no screenshot tool (spectacle, grim, gnome-screenshot) is installed" : $"installed: {string.Join(", ", tools.Select(Name))}";

        // WSLg's X server is rootless: its root window can't be read (XGetImage returned nothing in beta 13's test,
        // 2026-11-13), and it has no portal or screenshot tool. WSLg's windows sit on the Windows desktop, so the
        // bridge (without --wslg) captures them.
        if (isWsl)
            return ([], WslgCannotCapture);
        if (wayland)
            return ([LinuxCaptureMethod.Portal, .. tools],
                $"Wayland desktop ({env("XDG_CURRENT_DESKTOP") ?? "desktop unknown"}): the screenshot portal first, then the tools ({toolNote}).");
        if (x11)
            return ([LinuxCaptureMethod.X11, LinuxCaptureMethod.Portal, .. tools],
                $"X11 session: XGetImage first, then the portal and the tools ({toolNote}).");
        return ([], "Neither WAYLAND_DISPLAY nor DISPLAY is set: SquareSnap needs a graphical desktop session.");
    }

    /// <summary>The command a screenshot tool needs to write the whole desktop to <paramref name="file"/> without asking or notifying.</summary>
    public static ToolCommand ToolFor(LinuxCaptureMethod m, string file) => m switch
    {
        // -b background (no window), -n no notification, -f the full desktop (every monitor), -o the file.
        LinuxCaptureMethod.Spectacle => new("spectacle", ["-b", "-n", "-f", "-o", file]),
        LinuxCaptureMethod.Grim => new("grim", [file]),
        LinuxCaptureMethod.GnomeScreenshot => new("gnome-screenshot", ["-f", file]),
        _ => throw new ArgumentException($"{Name(m)} isn't an external tool.", nameof(m)),
    };

    /// <summary>
    /// Who keeps the PNG on the clipboard after "squaresnap capture" exits: wl-copy on Wayland, xclip on X11. Both stay
    /// in the background and serve the clipboard until something else is copied. Null when neither can run, with the hint.
    /// </summary>
    public static (ToolCommand? Command, string Reason) ClipboardTool(Func<string, string?> env, Func<string, bool> programExists)
    {
        bool wayland = !string.IsNullOrEmpty(env("WAYLAND_DISPLAY"));
        bool x11 = !string.IsNullOrEmpty(env("DISPLAY"));
        if (wayland && programExists("wl-copy"))
            return (new("wl-copy", ["--type", "image/png"]), "Wayland: wl-copy keeps the image on the clipboard after SquareSnap exits.");
        if (x11 && programExists("xclip"))
            return (new("xclip", ["-selection", "clipboard", "-t", "image/png", "-i"]),
                wayland ? "wl-copy isn't installed; using xclip through XWayland." : "X11: xclip keeps the image on the clipboard after SquareSnap exits.");
        return (null, wayland || !x11 ? WlClipboardHint : "xclip isn't installed. " + WlClipboardHint);
    }

    public const string WlClipboardHint =
        "To copy from the command line, install wl-clipboard: sudo apt install wl-clipboard (Debian, Ubuntu); " +
        "sudo dnf install wl-clipboard (Fedora); sudo pacman -S wl-clipboard (Arch, CachyOS).";

    /// <summary>
    /// What the portal's host registry said about the app id "squaresnap". Null error = registered. Beta 13's test
    /// (2026-11-13): started before install.sh, the portal answers "App info not found for 'squaresnap'", and a
    /// shortcut requested then gets no name on KDE Plasma (a stray launcher entry, no working hotkey).
    /// </summary>
    public static (HostAppStatus Status, string Explanation) HostAppRegistration(string? errorName, string? errorMessage)
    {
        if (errorName == null) return (HostAppStatus.Registered, "registered as app 'squaresnap' (matches squaresnap.desktop)");
        string all = $"{errorName}: {errorMessage}";
        if (all.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return (HostAppStatus.NotInstalled, $"the desktop doesn't know SquareSnap yet ({all}): squaresnap.desktop isn't installed; run ./install.sh");
        if (errorName.EndsWith(".UnknownMethod", StringComparison.Ordinal) || errorName.EndsWith(".UnknownInterface", StringComparison.Ordinal) ||
            errorName.EndsWith(".UnknownObject", StringComparison.Ordinal) || errorName.EndsWith(".ServiceUnknown", StringComparison.Ordinal))
            return (HostAppStatus.NotSupported, $"this portal has no host app registry ({all}; it came in xdg-desktop-portal 1.19)");
        return (HostAppStatus.Other, $"the portal refused the app id ({all}); an app started from its menu entry is already known by it");
    }

    public const string WslgCannotCapture =
        "WSLg doesn't let programs read its screen, so the Linux SquareSnap can't capture under WSL. " +
        "The Windows SquareSnap captures the whole Windows desktop, WSLg's Linux windows included: run squaresnap without --wslg, " +
        "with SquareSnap.exe next to it, on the Windows PATH or named in SQUARESNAP_WINDOWS_EXE.";

    /// <summary>The hint shown instead of asking for the hotkey before SquareSnap is installed.</summary>
    public const string InstallForHotkey =
        "The hotkey needs SquareSnap in the app menu: run ./install.sh in SquareSnap's folder, then start SquareSnap again " +
        "(KDE Plasma gives hotkeys only to installed apps; see KDE-SHORTCUTS.txt).";

    /// <summary>The status-bar text once the shortcut is known, and whether it's a problem (no key set).</summary>
    public static (string Message, bool Problem) ShortcutOutcome(IReadOnlyList<PortalShortcut> shortcuts, string id)
    {
        var mine = shortcuts.FirstOrDefault(s => s.Id == id);
        if (mine == null)
            return ("The desktop didn't report SquareSnap's shortcut. " + NoKeyForHotkey, true);
        if (string.IsNullOrWhiteSpace(mine.Trigger) || mine.Trigger.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            return (NoKeyForHotkey, true);
        return ($"Ready. Hotkey: {mine.Trigger.Trim()} (change it in System Settings > Keyboard > Shortcuts > SquareSnap).", false);
    }

    /// <summary>Shown when the desktop has SquareSnap's shortcut with no key (CachyOS, 2026-11-14: "capture=none" kept from beta 13).</summary>
    public const string NoKeyForHotkey =
        "The desktop has SquareSnap's shortcut with no key set, so the hotkey does nothing. Set one in System Settings > " +
        "Keyboard > Shortcuts > SquareSnap (tick the default Ctrl+Alt+S, then Apply); see KDE-SHORTCUTS.txt.";

    /// <summary>True when <paramref name="program"/> is an executable file in one of the PATH folders.</summary>
    public static bool OnPath(string program, string? path, Func<string, bool> isExecutableFile) =>
        FindOnPath(program, path, isExecutableFile) != null;

    /// <summary>
    /// The full path of <paramref name="program"/> in the first PATH folder that has it as an executable file, or null.
    /// Only absolute folders count: an empty or relative entry ("", ".") would mean the current folder. SquareSnap starts
    /// helper tools by this full path, never by bare name, because .NET on Unix looks in the program's folder and the
    /// current folder before PATH, so a file named wl-copy in the folder the user runs squaresnap from would run instead
    /// (security review, 2026-11-14).
    /// </summary>
    public static string? FindOnPath(string program, string? path, Func<string, bool> isExecutableFile)
    {
        if (program.Length == 0 || program.Contains('/')) return null;
        foreach (string dir in (path ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!dir.StartsWith('/')) continue;
            string candidate = dir.TrimEnd('/') + "/" + program;
            if (isExecutableFile(candidate)) return candidate;
        }
        return null;
    }
}
