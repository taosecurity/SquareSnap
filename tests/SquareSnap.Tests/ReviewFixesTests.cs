using SquareSnap.Core.Cli;
using SquareSnap.Core.Logging;
using SquareSnap.Core.Platform;
using SquareSnap.Core.Settings;

namespace SquareSnap.Tests;

/// <summary>2.0.1: fixes from the 2026-11-16 code review of the 2.0.0 source.</summary>
public class ReviewFixesTests
{
    [Theory]
    [InlineData("Shift+F")]
    [InlineData("F")]
    [InlineData("Shift+Fx")]
    public void LetterF_StillNeedsAModifier(string text) =>
        Assert.False(HotkeySpec.TryParse(text, out _, out _));

    [Theory]
    [InlineData("F5")]
    [InlineData("Shift+F12")]
    [InlineData("F24")]
    [InlineData("Ctrl+F")]
    public void FunctionKeysAndModifiedF_AreFine(string text) =>
        Assert.True(HotkeySpec.TryParse(text, out _, out string? error), error);

    private static readonly BridgeEnvironment Wsl = new()
    {
        IsWsl = true,
        InteropEnabled = true,
        CurrentDirectory = "/home/alex",
        Home = "/home/alex",
        WindowsExe = "/mnt/c/Tools/SquareSnap.exe",
        WindowsExeSource = "on PATH",
    };

    [Fact]
    public void WindowsPathForOut_IsPassedOnAsGiven()
    {
        var r = CommandLine.Parse(["capture", "--out", @"C:\Users\me\Pictures", "--log", @"\\nas\share\sq.log"]);
        Assert.True(r.Ok, r.Error);
        var d = WslBridge.Decide(r.Options!, Wsl, linux => Core.Platform.Wsl.ToWindowsPath(linux, "Debian"));
        Assert.Contains(@"C:\Users\me\Pictures", d.WindowsArgs);
        Assert.Contains(@"\\nas\share\sq.log", d.WindowsArgs);
        Assert.DoesNotContain(d.WindowsArgs, a => a.Contains("wsl.localhost"));
    }

    [Fact]
    public void PeekLastRegion_ReadsWhatAnotherRunSaved()
    {
        string dir = Path.Combine(Path.GetTempPath(), "squaresnap-review-" + Guid.NewGuid().ToString("N"));
        try
        {
            var env = new Dictionary<string, string> { ["HOME"] = dir.Replace('\\', '/') };
            using var log = new Logger(Path.Combine(dir, "test.log"), verbose: false, console: new StringWriter());
            var store = new SettingsStore(AppPaths.ForLinux(n => env.GetValueOrDefault(n)), log);
            Assert.Null(store.PeekLastRegion());
            var s = store.Load();
            s.LastRegion = "10,20,300,300";
            store.Save(s);
            Assert.Equal("10,20,300,300", store.PeekLastRegion());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
