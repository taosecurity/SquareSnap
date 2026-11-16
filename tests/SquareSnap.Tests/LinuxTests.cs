using SquareSnap.Core.Cli;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Platform;

namespace SquareSnap.Tests;

internal static class TestEnv
{
    public static Func<string, string?> Env(params (string Name, string Value)[] vars) =>
        n => vars.FirstOrDefault(v => v.Name == n).Value;

    /// <summary>The repo root: the folder above tests\ that holds Directory.Build.props.</summary>
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
        }
    }
}

/// <summary>Native Wayland or X11 on Linux (step 4): the user's desktops are KDE Plasma 6 on Wayland. Same rules as BejView.</summary>
public class DisplayChoiceTests
{
    [Fact]
    public void A_wayland_desktop_gets_native_wayland()
    {
        var c = DisplayChoice.Decide(TestEnv.Env(("WAYLAND_DISPLAY", "wayland-0"), ("DISPLAY", ":0")), isWsl: false);
        Assert.True(c.Wayland);
        Assert.Contains("falls back to X11", c.Reason);
    }

    [Fact]
    public void Without_a_wayland_compositor_it_uses_x11() =>
        Assert.False(DisplayChoice.Decide(TestEnv.Env(("DISPLAY", ":0")), isWsl: false).Wayland);

    [Fact]
    public void Wsl_uses_x11_unless_wayland_is_asked_for()
    {
        var c = DisplayChoice.Decide(TestEnv.Env(("WAYLAND_DISPLAY", "wayland-0"), ("DISPLAY", ":0")), isWsl: true);
        Assert.False(c.Wayland);
        Assert.Contains(DisplayChoice.ForceWaylandVariable, c.Reason);
        Assert.True(DisplayChoice.Decide(TestEnv.Env(("WAYLAND_DISPLAY", "wayland-0"), (DisplayChoice.ForceWaylandVariable, "1")), isWsl: true).Wayland);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData(" Yes ")]
    public void Squaresnap_x11_forces_x11_everywhere(string value)
    {
        var env = TestEnv.Env(("WAYLAND_DISPLAY", "wayland-0"), (DisplayChoice.ForceX11Variable, value), (DisplayChoice.ForceWaylandVariable, "1"));
        Assert.False(DisplayChoice.Decide(env, isWsl: false).Wayland);
        Assert.False(DisplayChoice.Decide(env, isWsl: true).Wayland);
    }

    [Fact]
    public void The_app_id_matches_the_desktop_file_and_the_icons()
    {
        string linux = Path.Combine(TestEnv.RepoRoot, "packaging", "linux");
        string desktop = File.ReadAllText(Path.Combine(linux, DisplayChoice.AppId + ".desktop"));
        Assert.Contains($"Icon={DisplayChoice.AppId}", desktop);
        Assert.Contains("Exec=squaresnap", desktop);
        Assert.DoesNotContain("\r", desktop); // .gitattributes keeps it LF
        Assert.True(File.Exists(Path.Combine(linux, "icons", "hicolor", "256x256", "apps", DisplayChoice.AppId + ".png")));
        string install = File.ReadAllText(Path.Combine(linux, "install.sh"));
        Assert.Contains($"APP_ID={DisplayChoice.AppId}\n", install);
        Assert.DoesNotContain("\r", install);
    }
}

public class MissingLibraryTests
{
    private static Exception AsThrown(string inner) =>
        new TypeInitializationException("SkiaSharp.SKImageInfo", new DllNotFoundException(inner));

    [Fact]
    public void A_missing_system_library_is_named_with_the_package_to_install()
    {
        var ex = AsThrown("Unable to load shared library 'libSkiaSharp' or one of its dependencies.\n" +
                          "libfontconfig.so.1: cannot open shared object file: No such file or directory\n" +
                          "/home/alex/.net/squaresnap/x/libSkiaSharp.so: cannot open shared object file: No such file or directory\n");
        Assert.Equal(["libfontconfig.so.1"], MissingLibrary.Find(ex));
        string msg = MissingLibrary.Explain(ex)!;
        Assert.StartsWith("SquareSnap can't start:", msg);
        Assert.Contains("sudo apt install libfontconfig1", msg);
        Assert.Contains("sudo dnf install fontconfig", msg);
        Assert.Contains("sudo pacman -S fontconfig", msg);
    }

    [Fact]
    public void Wayland_and_x11_libraries_are_named_once_each()
    {
        var ex = AsThrown("libwayland-client.so.0: cannot open shared object file\nlibX11.so.6: cannot open shared object file\nlibwayland-client.so.0: cannot open shared object file");
        Assert.Equal(["libwayland-client.so.0", "libX11.so.6"], MissingLibrary.Find(ex));
        Assert.Contains("sudo apt install libwayland-client0 libx11-6", MissingLibrary.Explain(ex));
        Assert.Contains("sudo pacman -S wayland libx11", MissingLibrary.Explain(ex));
    }

    [Fact]
    public void Other_errors_are_left_alone()
    {
        Assert.Null(MissingLibrary.Explain(new InvalidOperationException("something else")));
        Assert.Null(MissingLibrary.Explain(AsThrown("/x/libSkiaSharp.so: cannot open shared object file")));
    }

    [Fact]
    public void Exit_code_five_is_explained_in_the_help() =>
        Assert.Contains("a system library is missing", CommandLine.HelpText);
}

/// <summary>Fitting Avalonia's screens onto a Linux screenshot (the portal and the tools give one image of the whole desktop).</summary>
public class ScreenMappingTests
{
    [Fact]
    public void Plasma_at_150_percent_is_one_to_one()
    {
        // KDE Plasma on native Wayland: Avalonia reports the logical layout (2560x1440) with scaling 1;
        // the portal's screenshot is at the real resolution (3840x2160).
        var (layout, note) = ScreenMapping.ToImageLayout([new ScreenInfo("eDP-1", new PixelRect(0, 0, 2560, 1440), 1, true)], 3840, 2160);
        var d = Assert.Single(layout.Monitors);
        Assert.Equal(new PixelRect(0, 0, 3840, 2160), d.Bounds);
        Assert.Equal(1.5, d.Scale, 3);
        Assert.True(d.FixedScale);
        Assert.Equal(new PixelPoint(0, 0), d.WindowPosition);
        Assert.Contains("x1.5", note);
    }

    [Fact]
    public void Two_monitors_with_mixed_scaling_touch_and_dont_overlap()
    {
        // 150% laptop (2560x1440 logical) left of a 100% monitor (1920x1080), Plasma screenshot at the highest scale.
        var screens = new[]
        {
            new ScreenInfo("eDP-1", new PixelRect(0, 0, 2560, 1440), 1, true),
            new ScreenInfo("DP-2", new PixelRect(2560, 0, 1920, 1080), 1, false),
        };
        var (layout, _) = ScreenMapping.ToImageLayout(screens, 6720, 2160);
        Assert.Equal(new PixelRect(0, 0, 3840, 2160), layout.Monitors[0].Bounds);
        Assert.Equal(new PixelRect(3840, 0, 2880, 1620), layout.Monitors[1].Bounds);
        Assert.Equal(new PixelPoint(2560, 0), layout.Monitors[1].WindowPosition);
        Assert.Equal(1.5, layout.Monitors[1].Scale, 3);          // window units = logical, so 1.5 image px each
        Assert.Equal(1920, layout.Monitors[1].Bounds.Width / layout.Monitors[1].Scale, 3); // fills the window exactly
        Assert.Equal(new PixelRect(0, 0, 6720, 2160), layout.VirtualBounds);
    }

    [Fact]
    public void X11_screens_in_physical_pixels_with_scaling()
    {
        // X11 (XWayland or Xorg): bounds in real pixels, scaling 2; XGetImage is 1:1.
        var (layout, _) = ScreenMapping.ToImageLayout([new ScreenInfo(":0", new PixelRect(0, 0, 3840, 2160), 2, true)], 3840, 2160);
        var d = layout.Monitors[0];
        Assert.Equal(new PixelRect(0, 0, 3840, 2160), d.Bounds);
        Assert.Equal(2, d.Scale, 3); // the overlay window is 1920x1080 units, 2 image px each
    }

    [Fact]
    public void Negative_positions_are_moved_into_the_image()
    {
        var screens = new[]
        {
            new ScreenInfo("left", new PixelRect(-1920, 0, 1920, 1080), 1, false),
            new ScreenInfo("main", new PixelRect(0, 0, 1920, 1080), 1, true),
        };
        var (layout, _) = ScreenMapping.ToImageLayout(screens, 3840, 1080);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), layout.Monitors[0].Bounds);
        Assert.Equal(new PixelRect(1920, 0, 1920, 1080), layout.Monitors[1].Bounds);
        Assert.Equal(new PixelPoint(-1920, 0), layout.Monitors[0].WindowPosition);
    }

    [Fact]
    public void No_screens_means_one_monitor_covering_the_image()
    {
        var (layout, note) = ScreenMapping.ToImageLayout([], 1920, 1080);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), Assert.Single(layout.Monitors).Bounds);
        Assert.Contains("No screens", note);
    }

    [Fact]
    public void An_image_of_one_monitor_only_is_matched_to_that_monitor()
    {
        var screens = new[]
        {
            new ScreenInfo("main", new PixelRect(0, 0, 2560, 1440), 1, true),
            new ScreenInfo("side", new PixelRect(2560, 0, 1080, 1920), 1, false), // portrait
        };
        var (layout, note) = ScreenMapping.ToImageLayout(screens, 1620, 2880); // the portrait one at 150%
        var d = Assert.Single(layout.Monitors);
        Assert.Equal("side", d.Name);
        Assert.Equal(new PixelPoint(2560, 0), d.WindowPosition);
        Assert.Contains("treating it as monitor side", note);
    }
}

public class LinuxPlanTests
{
    private static Func<string, bool> Installed(params string[] programs) => p => programs.Contains(p);

    [Fact]
    public void Plasma_on_wayland_tries_the_portal_then_the_tools_that_exist()
    {
        var env = TestEnv.Env(("WAYLAND_DISPLAY", "wayland-0"), ("DISPLAY", ":1"), ("XDG_CURRENT_DESKTOP", "KDE"));
        var (order, reason) = LinuxPlan.CaptureOrder(env, isWsl: false, Installed("spectacle", "gnome-screenshot"));
        Assert.Equal([LinuxCaptureMethod.Portal, LinuxCaptureMethod.Spectacle, LinuxCaptureMethod.GnomeScreenshot], order);
        Assert.Contains("KDE", reason);
        Assert.DoesNotContain(LinuxCaptureMethod.X11, order); // XWayland's root window doesn't hold Wayland windows
    }

    [Fact]
    public void An_x11_session_uses_xgetimage_first() =>
        Assert.Equal([LinuxCaptureMethod.X11, LinuxCaptureMethod.Portal],
            LinuxPlan.CaptureOrder(TestEnv.Env(("DISPLAY", ":0")), false, Installed()).Order);

    [Fact]
    public void Wslg_cannot_capture_and_says_why()
    {
        var env = TestEnv.Env(("WAYLAND_DISPLAY", "wayland-0"), ("DISPLAY", ":0"));
        // WSLg's rootless X server can't be read (beta 13's test, 8.1): nothing to try, and the reason points to the bridge.
        var (order, reason) = LinuxPlan.CaptureOrder(env, true, Installed("grim"));
        Assert.Empty(order);
        Assert.Contains("without --wslg", reason);
    }

    [Fact]
    public void No_desktop_session_means_nothing_to_try()
    {
        var (order, reason) = LinuxPlan.CaptureOrder(TestEnv.Env(), false, Installed("spectacle"));
        Assert.Empty(order);
        Assert.Contains("graphical desktop", reason);
    }

    [Theory]
    [InlineData("spectacle", LinuxCaptureMethod.Spectacle)]
    [InlineData(" GRIM ", LinuxCaptureMethod.Grim)]
    [InlineData("x11", LinuxCaptureMethod.X11)]
    public void Squaresnap_capture_forces_one_method(string value, LinuxCaptureMethod method) =>
        Assert.Equal([method], LinuxPlan.CaptureOrder(TestEnv.Env((LinuxPlan.ForceCaptureVariable, value), ("WAYLAND_DISPLAY", "w")), false, Installed()).Order);

    [Fact]
    public void An_unknown_forced_method_is_reported_and_ignored()
    {
        var (order, reason) = LinuxPlan.CaptureOrder(TestEnv.Env((LinuxPlan.ForceCaptureVariable, "scrot"), ("WAYLAND_DISPLAY", "w")), false, Installed());
        Assert.Equal([LinuxCaptureMethod.Portal], order);
        Assert.Contains("'scrot' isn't one of", reason);
    }

    [Fact]
    public void Tool_commands_are_argument_lists()
    {
        string file = "/tmp/squaresnap-x/screen with space.png";
        Assert.Equal(["-b", "-n", "-f", "-o", file], LinuxPlan.ToolFor(LinuxCaptureMethod.Spectacle, file).Arguments);
        Assert.Equal([file], LinuxPlan.ToolFor(LinuxCaptureMethod.Grim, file).Arguments);
        Assert.Equal(["-f", file], LinuxPlan.ToolFor(LinuxCaptureMethod.GnomeScreenshot, file).Arguments);
        Assert.Throws<ArgumentException>(() => LinuxPlan.ToolFor(LinuxCaptureMethod.Portal, file));
    }

    [Fact]
    public void The_command_line_clipboard_prefers_wl_copy()
    {
        var wayland = TestEnv.Env(("WAYLAND_DISPLAY", "wayland-0"), ("DISPLAY", ":1"));
        var (cmd, _) = LinuxPlan.ClipboardTool(wayland, Installed("wl-copy", "xclip"));
        Assert.Equal("wl-copy", cmd!.Program);
        Assert.Equal(["--type", "image/png"], cmd.Arguments);
        Assert.Equal("xclip", LinuxPlan.ClipboardTool(wayland, Installed("xclip")).Command!.Program);
    }

    [Fact]
    public void Without_wl_copy_the_hint_names_wl_clipboard()
    {
        var (cmd, reason) = LinuxPlan.ClipboardTool(TestEnv.Env(("WAYLAND_DISPLAY", "wayland-0")), Installed());
        Assert.Null(cmd);
        Assert.Contains("sudo apt install wl-clipboard", reason);
        Assert.Contains("sudo pacman -S wl-clipboard", reason);
    }

    [Fact]
    public void On_path_checks_each_folder() =>
        Assert.True(LinuxPlan.OnPath("grim", "/usr/local/bin:/usr/bin/", f => f == "/usr/bin/grim"));

    [Fact]
    public void Find_on_path_gives_the_full_path_of_the_first_match() =>
        Assert.Equal("/usr/local/bin/wl-copy",
            LinuxPlan.FindOnPath("wl-copy", "/usr/local/bin:/usr/bin", f => f is "/usr/local/bin/wl-copy" or "/usr/bin/wl-copy"));

    [Theory]
    [InlineData(".:/usr/bin")]          // "." is the current folder
    [InlineData("::/usr/bin")]          // an empty entry is the current folder too
    [InlineData("bin:/usr/bin")]        // a relative folder depends on the current folder
    public void Find_on_path_never_uses_the_current_folder(string path)
    {
        // Security review, 2026-11-14: a planted ./wl-copy must never be the one that runs.
        Assert.Equal("/usr/bin/wl-copy", LinuxPlan.FindOnPath("wl-copy", path, f => true));
        Assert.Null(LinuxPlan.FindOnPath("wl-copy", ".:bin", f => true));
    }

    [Theory]
    [InlineData("./wl-copy")]
    [InlineData("../bin/wl-copy")]
    [InlineData("")]
    public void Find_on_path_takes_bare_names_only(string program) =>
        Assert.Null(LinuxPlan.FindOnPath(program, "/usr/bin", f => true));
}

public class HostAppTests
{
    [Fact]
    public void Before_install_sh_the_hotkey_waits()
    {
        // The answer every beta 13 run got (2026-11-13): SquareSnap started before install.sh.
        var (status, text) = LinuxPlan.HostAppRegistration("org.freedesktop.portal.Error.Failed", "Could not register app ID: App info not found for 'squaresnap'");
        Assert.Equal(HostAppStatus.NotInstalled, status);
        Assert.Contains("install.sh", text);
        Assert.Contains("./install.sh", LinuxPlan.InstallForHotkey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    public void A_shortcut_with_no_key_says_where_to_set_it(string? trigger)
    {
        // CachyOS (2026-11-14): kglobalshortcutsrc kept "capture=none,Ctrl+Alt+S,..." from beta 13.
        var (message, problem) = LinuxPlan.ShortcutOutcome([new("capture", trigger)], "capture");
        Assert.True(problem);
        Assert.Contains("System Settings > Keyboard > Shortcuts > SquareSnap", message);
    }

    [Fact]
    public void A_shortcut_with_a_key_is_ready()
    {
        var (message, problem) = LinuxPlan.ShortcutOutcome([new("capture", "Ctrl+Alt+S")], "capture");
        Assert.False(problem);
        Assert.StartsWith("Ready. Hotkey: Ctrl+Alt+S", message);
        Assert.True(LinuxPlan.ShortcutOutcome([], "capture").Problem);
    }

    [Theory]
    [InlineData(null, HostAppStatus.Registered)]
    [InlineData("org.freedesktop.DBus.Error.UnknownMethod", HostAppStatus.NotSupported)]
    [InlineData("org.freedesktop.DBus.Error.UnknownInterface", HostAppStatus.NotSupported)]
    [InlineData("org.freedesktop.portal.Error.Failed", HostAppStatus.Other)]
    public void Other_answers_let_the_hotkey_go_ahead(string? error, HostAppStatus expected) =>
        Assert.Equal(expected, LinuxPlan.HostAppRegistration(error, "Connection already associated with an application ID").Status);
}

public class PortalTriggerTests
{
    [Theory]
    [InlineData("Ctrl+Alt+S", "CTRL+ALT+s")]
    [InlineData("Ctrl+Shift+S", "CTRL+SHIFT+s")]
    [InlineData("Ctrl+PrintScreen", "CTRL+Print")]
    [InlineData("Win+Shift+F12", "SHIFT+LOGO+F12")]
    [InlineData("Alt+PageUp", "ALT+Prior")]
    [InlineData("Ctrl+Alt+1", "CTRL+ALT+1")]
    public void Hotkeys_become_freedesktop_triggers(string hotkey, string trigger)
    {
        Assert.True(HotkeySpec.TryParse(hotkey, out var spec, out string? error), error);
        Assert.Equal(trigger, spec!.ToPortalTrigger());
    }
}

/// <summary>BejView's beta 8 finding, carried over: the command may come after options.</summary>
public class CommandOrderTests
{
    [Theory]
    [InlineData("--verbose", "capture")]
    [InlineData("-v", "capture", "--size", "512")]
    [InlineData("--log", "x.log", "capture")]       // an option's value isn't taken for the command
    [InlineData("--out", "/tmp/shots", "capture")]
    [InlineData("capture", "--verbose")]
    public void The_command_may_come_after_options(params string[] args)
    {
        var r = CommandLine.Parse(args);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(Command.Capture, r.Options!.Command);
    }

    [Fact]
    public void A_stray_word_is_still_an_error()
    {
        Assert.False(CommandLine.Parse(["capture", "extra"]).Ok);
        Assert.Contains("Unknown command 'snap'", CommandLine.Parse(["--verbose", "snap"]).Error);
    }
}
