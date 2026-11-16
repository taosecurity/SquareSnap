using SquareSnap.Core.Platform;

namespace SquareSnap.Tests;

public class WslTests
{
    [Theory]
    [InlineData("/mnt/c/Users/alex/Pictures", @"C:\Users\alex\Pictures")]
    [InlineData("/mnt/d/My Shots/", @"D:\My Shots")]
    [InlineData("/mnt/c", @"C:\")]
    [InlineData("/home/alex/shots", @"\\wsl.localhost\Debian\home\alex\shots")]
    [InlineData("/mnt/wsl/shared", @"\\wsl.localhost\Debian\mnt\wsl\shared")]
    public void ToWindows(string linux, string windows) =>
        Assert.Equal(windows, Wsl.ToWindowsPath(linux, "Debian"));

    [Fact]
    public void ToWindows_CustomMountRoot() =>
        Assert.Equal(@"E:\x", Wsl.ToWindowsPath("/drives/e/x", "Ubuntu", "/drives"));

    [Theory]
    [InlineData(@"C:\Users\alex\Pictures\a.png", "/mnt/c/Users/alex/Pictures/a.png")]
    [InlineData(@"d:\", "/mnt/d")]
    [InlineData("C:/temp/x", "/mnt/c/temp/x")]
    [InlineData(@"\\wsl.localhost\Debian\home\alex", "/home/alex")]
    [InlineData(@"\\wsl$\Ubuntu\tmp\a.png", "/tmp/a.png")]
    public void ToLinux(string windows, string linux) =>
        Assert.Equal(linux, Wsl.ToLinuxPath(windows));

    [Fact]
    public void RoundTrip()
    {
        string p = "/mnt/c/Users/alex/My Pictures/SquareCap-20261111-001.png";
        Assert.Equal(p, Wsl.ToLinuxPath(Wsl.ToWindowsPath(p, "Debian")));
    }

    [Fact]
    public void BadPathsAreExplained()
    {
        Assert.Throws<ArgumentException>(() => Wsl.ToWindowsPath("relative/path", "Debian"));
        Assert.Throws<ArgumentException>(() => Wsl.ToWindowsPath("/home/alex", ""));
        Assert.Throws<ArgumentException>(() => Wsl.ToLinuxPath(@"relative\path"));
    }

    [Fact]
    public void Detection()
    {
        Assert.True(Wsl.IsWsl(n => n == "WSL_DISTRO_NAME" ? "Debian" : null, _ => false));
        Assert.True(Wsl.IsWsl(_ => null, f => f == Wsl.InteropFile));
        Assert.False(Wsl.IsWsl(_ => null, _ => false));
    }
}

public class AppPathsTests
{
    [Fact]
    public void Windows()
    {
        var p = AppPaths.ForWindows(n => n == "APPDATA" ? @"C:\Users\alex\AppData\Roaming" : null, @"D:\Pics");
        Assert.Equal(@"C:\Users\alex\AppData\Roaming\SquareSnap\settings.json", p.SettingsFile);
        Assert.Equal(@"C:\Users\alex\AppData\Roaming\SquareSnap\squaresnap.log", p.DefaultLogFile);
        Assert.Equal(@"D:\Pics\SquareSnap", p.DefaultSaveFolder);
        Assert.Null(p.BejSnapSettingsFile);
    }

    [Fact]
    public void Linux()
    {
        var p = AppPaths.ForLinux(n => n == "HOME" ? "/home/alex" : null);
        Assert.Equal("/home/alex/.config/SquareSnap/settings.json", p.SettingsFile);
        Assert.Equal("/home/alex/Pictures/SquareSnap", p.DefaultSaveFolder);
        Assert.Equal("/home/alex/.config/BejSnap/BejSnap.conf", p.BejSnapSettingsFile);
    }

    [Fact]
    public void LinuxHonoursXdg()
    {
        var env = new Dictionary<string, string> { ["HOME"] = "/home/alex", ["XDG_CONFIG_HOME"] = "/cfg/", ["XDG_PICTURES_DIR"] = "/data/pics" };
        var p = AppPaths.ForLinux(n => env.GetValueOrDefault(n));
        Assert.Equal("/cfg/SquareSnap/settings.json", p.SettingsFile);
        Assert.Equal("/data/pics/SquareSnap", p.DefaultSaveFolder);
    }
}
