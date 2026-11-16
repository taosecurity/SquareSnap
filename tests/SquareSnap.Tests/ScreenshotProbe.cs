using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using SquareSnap.App;
using SquareSnap.Capture;
using SquareSnap.Core.Logging;
using SquareSnap.Core.Platform;
using SquareSnap.Core.Settings;

namespace SquareSnap.Tests;

/// <summary>Opt-in visual check: set SQS_PROBE_DIR and run this test to save the dark-theme main window as status.png.</summary>
public class ScreenshotProbe
{
    [AvaloniaFact]
    public void RenderStatus()
    {
        string? outDir = Environment.GetEnvironmentVariable("SQS_PROBE_DIR");
        if (outDir == null) return;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var s = new AppSession(new SquareSnap.Core.Cli.CliOptions(), Logger.Null,
            new SettingsStore(AppPaths.ForLinux(_ => "/nonexistent"), Logger.Null), new AppSettings { Hotkey = "" });
        s.Clipboard = PlatformServices.CreateClipboard(Logger.Null);
        var w = new MainWindow(s);
        w.Show();
        w.SetStatus("Ready. Press Ctrl+Alt+S anywhere to capture a square.");
        using var frame = w.CaptureRenderedFrame()!;
#pragma warning disable CS0618 // Bitmap.Save(string) is obsolete in Avalonia 12; fine for a probe
        frame.Save(Path.Combine(outDir, "status.png"));
#pragma warning restore CS0618
        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
    }

    /// <summary>Help > About with the Store package's long paths, saved as about.png (beta 19's height fix).</summary>
    [AvaloniaFact]
    public void RenderAbout()
    {
        string? outDir = Environment.GetEnvironmentVariable("SQS_PROBE_DIR");
        if (outDir == null) return;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        const string copy = @"C:\Users\alex\AppData\Local\Packages\RichardBejtlich.SquareSnap_kz4qzpv342p6y\LocalCache\Roaming\SquareSnap\";
        var w = MainWindow.AboutWindow(copy + "settings.json", copy + "squaresnap.log");
        w.Show();
        using var frame = w.CaptureRenderedFrame()!;
#pragma warning disable CS0618 // Bitmap.Save(string) is obsolete in Avalonia 12; fine for a probe
        frame.Save(Path.Combine(outDir, "about.png"));
#pragma warning restore CS0618
        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
    }
}
