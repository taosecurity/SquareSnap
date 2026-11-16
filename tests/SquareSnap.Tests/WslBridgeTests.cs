using SquareSnap.Core.Cli;
using SquareSnap.Core.Platform;

namespace SquareSnap.Tests;

/// <summary>The WSL bridge's decisions (step 5): pure Core code, so they're tested on Windows and Linux alike.</summary>
public class WslBridgeTests
{
    private const string Exe = "/mnt/c/Tools/SquareSnap/SquareSnap.exe";

    private static readonly BridgeEnvironment Wsl = new()
    {
        IsWsl = true,
        InteropEnabled = true,
        CurrentDirectory = "/home/alex/work",
        Home = "/home/alex",
        WindowsExe = Exe,
        WindowsExeSource = "on PATH",
    };

    private static string ToWin(string linux) => Core.Platform.Wsl.ToWindowsPath(linux, "Debian");

    private static BridgeDecision Decide(BridgeEnvironment env, params string[] args)
    {
        var r = CommandLine.Parse(args);
        Assert.True(r.Ok, r.Error);
        return WslBridge.Decide(r.Options!, env, ToWin);
    }

    [Fact]
    public void Outside_wsl_nothing_changes()
    {
        Assert.Equal(BridgeAction.RunHere, Decide(Wsl with { IsWsl = false }, "capture").Action);
        Assert.Equal(BridgeAction.RunHere, Decide(Wsl with { IsWsl = false }).Action);
    }

    [Fact]
    public void Capture_under_wsl_runs_the_windows_exe_and_waits()
    {
        var d = Decide(Wsl, "capture");
        Assert.Equal(BridgeAction.UseWindowsExe, d.Action);
        Assert.Equal(Exe, d.WindowsExe);
        Assert.Equal(["capture"], d.WindowsArgs);
        Assert.True(d.Wait);
        Assert.True(d.TranslateOutput);
        Assert.Null(d.Warning);
    }

    [Fact]
    public void The_window_under_wsl_returns_at_once_unless_verbose()
    {
        var d = Decide(Wsl);
        Assert.Equal(BridgeAction.UseWindowsExe, d.Action);
        Assert.Empty(d.WindowsArgs);
        Assert.False(d.Wait);
        Assert.True(Decide(Wsl, "--verbose").Wait);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("version")]
    [InlineData("paths")]
    public void Console_commands_stay_in_linux(string arg) =>
        Assert.Equal(BridgeAction.RunHere, Decide(Wsl, arg).Action);

    [Fact]
    public void Wslg_keeps_the_capture_in_linux()
    {
        var d = Decide(Wsl, "capture", "--wslg");
        Assert.Equal(BridgeAction.RunHere, d.Action);
        Assert.Contains("WSLg", d.Reason);
        Assert.Null(d.Warning);
    }

    [Fact]
    public void Paths_become_windows_paths_and_wslg_is_dropped()
    {
        var d = Decide(Wsl, "capture", "--out", "/mnt/d/My Shots/shots", "--log", "logs/sq.log", "--size", "1080",
            "--format", "jpg", "--no-clipboard", "--delay", "3", "--fine-tune", "--verbose");
        Assert.Equal(
            ["capture", "--size", "1080", "--out", @"D:\My Shots\shots", "--format", "jpg", "--no-clipboard", "--delay", "3",
             "--fine-tune", "--verbose", "--log", @"\\wsl.localhost\Debian\home\alex\work\logs\sq.log"],
            d.WindowsArgs);
    }

    [Fact]
    public void A_home_folder_out_is_expanded_and_relative_ones_are_resolved()
    {
        Assert.Contains(@"\\wsl.localhost\Debian\home\alex\Pictures", Decide(Wsl, "capture", "--out=~/Pictures").WindowsArgs);
        Assert.Contains(@"\\wsl.localhost\Debian\home\alex\shots", Decide(Wsl, "capture", "--out", "../shots/.").WindowsArgs);
    }

    [Fact]
    public void Square_options_pass_through()
    {
        Assert.Equal(["capture", "--region", "100,200,512,512"], Decide(Wsl, "capture", "--region", "100,200,512,512").WindowsArgs);
        Assert.Equal(["capture", "--fixed", "256"], Decide(Wsl, "capture", "--fixed", "256").WindowsArgs);
        Assert.Equal(["capture", "--name", "Mod-{date} {n:00}", "--last"], Decide(Wsl, "capture", "--last", "--name", "Mod-{date} {n:00}").WindowsArgs);
    }

    [Fact]
    public void The_arguments_round_trip_through_the_parser()
    {
        // What the Windows exe receives must parse to the same options (apart from the translated paths).
        var d = Decide(Wsl, "capture", "--size", "native", "--format", "webp", "--clipboard", "--no-fine-tune", "--delay", "0");
        var back = CommandLine.Parse(d.WindowsArgs);
        Assert.True(back.Ok, back.Error);
        var o = back.Options!;
        Assert.Equal(Command.Capture, o.Command);
        Assert.True(o.Size!.Value.IsNative);
        Assert.Equal(Core.Naming.ImageFormat.Webp, o.Format);
        Assert.True(o.Clipboard);
        Assert.False(o.FineTune);
        Assert.Equal(0, o.DelaySeconds);
    }

    [Fact]
    public void Interop_off_or_no_exe_falls_back_with_a_warning()
    {
        var off = Decide(Wsl with { InteropEnabled = false }, "capture");
        Assert.Equal(BridgeAction.RunHere, off.Action);
        Assert.Contains("interop is off", off.Warning);

        var none = Decide(Wsl with { WindowsExe = null }, "capture");
        Assert.Equal(BridgeAction.RunHere, none.Action);
        Assert.Contains("export SQUARESNAP_WINDOWS_EXE=", none.Warning);
        Assert.Contains("can't capture under WSL", none.Warning);
    }

    [Theory]
    [InlineData(@"C:\Users\alex\Pictures\SquareCap-20261113-001.png", "/mnt/c/Users/alex/Pictures/SquareCap-20261113-001.png")]
    [InlineData("C:\\Users\\alex\\Pictures\\a b.png\r", "/mnt/c/Users/alex/Pictures/a b.png")]
    [InlineData(@"\\wsl.localhost\Debian\home\alex\x.png", "/home/alex/x.png")]
    [InlineData("SquareSnap: saved, but copying failed", "SquareSnap: saved, but copying failed")]
    [InlineData("", "")]
    public void Saved_paths_come_back_as_linux_paths(string line, string shown) =>
        Assert.Equal(shown, WslBridge.TranslateOutputLine(line, w => Core.Platform.Wsl.ToLinuxPath(w)));

    [Fact]
    public void The_exe_is_found_in_the_documented_order()
    {
        var files = new HashSet<string> { "/opt/sq/SquareSnap.exe", "/mnt/c/Tools/SquareSnap/SquareSnap.exe", "/mnt/d/Other/SquareSnap.exe" };
        string path = "/usr/bin:/mnt/c/Tools/SquareSnap:/mnt/d/Other";

        // 1. The variable (a Windows path is translated); 2. next to this program; 3. Windows PATH folders only.
        Assert.Equal(("/mnt/d/Other/SquareSnap.exe", "in $SQUARESNAP_WINDOWS_EXE"),
            WslBridge.FindWindowsExe(TestEnv.Env((WslBridge.ExeEnvironmentVariable, @"D:\Other\SquareSnap.exe"), ("PATH", path)), files.Contains, "/opt/sq", "/mnt/"));
        Assert.Equal(("/opt/sq/SquareSnap.exe", "next to this program"),
            WslBridge.FindWindowsExe(TestEnv.Env(("PATH", path)), files.Contains, "/opt/sq", "/mnt/"));
        Assert.Equal(("/mnt/c/Tools/SquareSnap/SquareSnap.exe", "on PATH"),
            WslBridge.FindWindowsExe(TestEnv.Env(("PATH", path)), files.Contains, "/home/alex/bin", "/mnt/"));
        Assert.Equal(((string?)null, (string?)null),
            WslBridge.FindWindowsExe(TestEnv.Env(("PATH", "/usr/bin:/opt/sq")), files.Contains, null, "/mnt/")); // Linux folders on PATH don't count
    }

    [Fact]
    public void A_wrong_variable_is_reported_not_replaced_by_a_search()
    {
        var files = new HashSet<string> { "/opt/sq/SquareSnap.exe" };
        var env = TestEnv.Env((WslBridge.ExeEnvironmentVariable, "/mnt/c/Typo/SquareSnap.exe"));
        Assert.Equal(((string?)null, (string?)null), WslBridge.FindWindowsExe(env, files.Contains, "/opt/sq", "/mnt/"));
        Assert.Contains("doesn't exist", WslBridge.ConfiguredExeProblem(env, files.Contains, "/mnt/"));
        Assert.Contains("isn't a full", WslBridge.ConfiguredExeProblem(TestEnv.Env((WslBridge.ExeEnvironmentVariable, "relative.exe")), files.Contains, "/mnt/"));
    }

    [Theory]
    [InlineData("[automount]\nroot = /drives/\n", "/drives/")]
    [InlineData("[automount]\nroot=/win\n", "/win/")]
    [InlineData("[interop]\nroot = /x/\n", "/mnt/")]
    [InlineData(null, "/mnt/")]
    public void The_mount_root_comes_from_wsl_conf(string? conf, string root) =>
        Assert.Equal(root, Core.Platform.Wsl.MountRootFromConf(conf));

    [Fact]
    public void Interop_is_read_from_binfmt()
    {
        Assert.True(Core.Platform.Wsl.InteropEnabled(f => f == Core.Platform.Wsl.InteropFile ? "enabled" : null));
        Assert.False(Core.Platform.Wsl.InteropEnabled(f => f == Core.Platform.Wsl.InteropFile ? "disabled" : null));
        Assert.False(Core.Platform.Wsl.InteropEnabled(_ => null));
    }
}
