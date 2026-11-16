using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using SquareSnap.App;
using SquareSnap.Capture;
using SquareSnap.Core.Cli;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Logging;
using SquareSnap.Core.Platform;
using SquareSnap.Core.Settings;
using Display = SquareSnap.Core.Geometry.Display;
using PixelRect = SquareSnap.Core.Geometry.PixelRect;

[assembly: AvaloniaTestApplication(typeof(SquareSnap.Tests.HeadlessApp))]

namespace SquareSnap.Tests;

/// <summary>Headless Avalonia with real Skia rendering, so frames can be inspected pixel by pixel.</summary>
public sealed class HeadlessApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HeadlessApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Drives the real overlay window (not just the controller) with simulated mouse and keys.</summary>
public class OverlayUiTests
{
    private static readonly Display Screen = new("TEST1", new PixelRect(0, 0, 400, 300), 1.0, true);

    private static AppSession Session()
    {
        var paths = AppPaths.ForLinux(_ => "/nonexistent");
        return new AppSession(new CliOptions(), Logger.Null, new SettingsStore(paths, Logger.Null), new AppSettings());
    }

    private static (OverlayWindow Window, OverlayController Controller, Func<int> Finished) Open(OverlayMode mode, bool fineTune, ScreenImage? shot = null)
    {
        shot ??= ImagingTests.Pattern(Screen.Bounds);
        var c = new OverlayController(Screen.Bounds, mode, fineTune, 64);
        int finished = 0;
        var w = new OverlayWindow(Screen, shot, c, Session(), _ => finished++);
        w.Show();
        return (w, c, () => finished);
    }

    [AvaloniaFact]
    public void MouseDrag_PicksTheSquare()
    {
        var (w, c, finished) = Open(OverlayMode.Drag, fineTune: false);
        w.MouseDown(new Point(50, 50), MouseButton.Left);
        w.MouseMove(new Point(150, 120));
        w.MouseUp(new Point(150, 120), MouseButton.Left);

        Assert.Equal(OverlayState.Done, c.State);
        Assert.Equal(new PixelRect(50, 50, 100, 100), c.Selection);
        Assert.Equal(1, finished());
    }

    [AvaloniaFact]
    public void FineTune_KeysThenEnter()
    {
        var (w, c, finished) = Open(OverlayMode.Drag, fineTune: true);
        w.MouseDown(new Point(10, 10), MouseButton.Left);
        w.MouseMove(new Point(60, 40));
        w.MouseUp(new Point(60, 40), MouseButton.Left);
        Assert.Equal(OverlayState.Adjusting, c.State);
        Assert.Equal(0, finished());

        w.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        w.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
        Assert.Equal(new PixelRect(11, 20, 50, 50), c.Selection);

        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(OverlayState.Done, c.State);
        Assert.Equal(1, finished());
    }

    [AvaloniaFact]
    public void EscapeCancels()
    {
        var (w, c, finished) = Open(OverlayMode.Drag, fineTune: false);
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Equal(OverlayState.Cancelled, c.State);
        Assert.Equal(1, finished());
    }

    [AvaloniaFact]
    public void AltF4Cancels()
    {
        // 2.0.2: the overlay handles every key, so Windows never closed it on Alt+F4 (the user's test of 2.0.1).
        var (w, c, finished) = Open(OverlayMode.Drag, fineTune: false);
        w.KeyPress(Key.F4, RawInputModifiers.Alt, PhysicalKey.F4, null);
        Assert.Equal(OverlayState.Cancelled, c.State);
        Assert.Equal(1, finished());
    }

    [AvaloniaFact]
    public void F4WithoutAltDoesNotCancel()
    {
        var (w, c, finished) = Open(OverlayMode.Drag, fineTune: false);
        w.KeyPress(Key.F4, RawInputModifiers.None, PhysicalKey.F4, null);
        Assert.NotEqual(OverlayState.Cancelled, c.State);
        Assert.Equal(0, finished());
    }

    [AvaloniaFact]
    public void Rendering_ShowsTheScreenOneToOneInsideTheSquare()
    {
        var shot = ImagingTests.Pattern(Screen.Bounds);
        var (w, c, _) = Open(OverlayMode.Drag, fineTune: true, shot);
        w.MouseDown(new Point(100, 100), MouseButton.Left);
        w.MouseMove(new Point(200, 200));
        w.MouseUp(new Point(200, 200), MouseButton.Left);
        w.MouseMove(new Point(390, 290)); // park the magnifier away from the probed pixels

        using var frame = w.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        unsafe
        {
            byte* p = (byte*)buffer.Address;
            bool rgba = buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888;
            (byte B, byte G, byte R) At(int x, int y)
            {
                byte* q = p + y * buffer.RowBytes + x * 4;
                return rgba ? (q[2], q[1], q[0]) : (q[0], q[1], q[2]);
            }

            // Inside the square the overlay shows the frozen screen exactly (no dimming, no shift).
            foreach (var (x, y) in new[] { (100, 100), (150, 170), (199, 199) })
                Assert.Equal(shot.PixelAt(x, y), At(x, y));

            // Outside it is dimmed (darker than the original).
            var orig = shot.PixelAt(60, 250);
            var dim = At(60, 250);
            Assert.True(dim.R < orig.R || orig.R == 0);
            Assert.True(dim.G < orig.G || orig.G == 0);
        }
        Assert.Equal(new PixelRect(100, 100, 100, 100), c.Selection);
    }

    [AvaloniaFact]
    public void MainWindow_OpensWithMenusFilled()
    {
        var s = Session();
        s.Settings.Hotkey = ""; // don't register a real system hotkey from a test
        s.Clipboard = PlatformServices.CreateClipboard(Logger.Null);
        var w = new MainWindow(s);
        w.Show();

        Assert.StartsWith("SquareSnap 2.0.2", w.Title);
        var size = w.FindControl<MenuItem>("SizeMenu")!;
        var items = ((IEnumerable<object>)size.ItemsSource!).Cast<MenuItem>().ToList();
        Assert.Equal(OutputSize.Presets.Count, items.Count);
        Assert.True(items[0].IsChecked); // native is the default
        Assert.False(w.FindControl<Button>("SaveButton")!.IsEnabled); // nothing to save yet
        Assert.True(w.FindControl<MenuItem>("ClipboardOpt")!.IsChecked);
        w.Close();
    }

    [AvaloniaFact]
    public void About_ShowsEveryLine_EvenWithTheLongStorePaths()
    {
        // Beta 18's fixed 250-pixel window cut off the settings path and the Log line (the user's screenshot, 2026-11-14).
        const string copy = @"C:\Users\alex\AppData\Local\Packages\RichardBejtlich.SquareSnap_kz4qzpv342p6y\LocalCache\Roaming\SquareSnap\";
        var w = MainWindow.AboutWindow(copy + "settings.json", copy + "squaresnap.log");
        w.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var text = (SelectableTextBlock)w.Content!;
        Assert.EndsWith("Log:\n" + copy + "squaresnap.log", text.Text);
        // The height every line needs at the window's width, against the height the window really has.
        text.Measure(new Size(w.ClientSize.Width, double.PositiveInfinity));
        double needed = text.DesiredSize.Height; // includes the margins
        Assert.True(needed <= w.ClientSize.Height + 0.5, $"the text needs {needed} but the window is {w.ClientSize.Height} high");
        w.Close();
    }

    [AvaloniaFact]
    public void TrayIcon_IsRegisteredWithTheApplication()
    {
        // Avalonia 12 only shows a tray icon that is in Application's Icons list; betas 1-7 never added it.
        var s = Session();
        s.Settings.Hotkey = "";
        s.Clipboard = PlatformServices.CreateClipboard(Logger.Null);
        var w = new MainWindow(s);
        w.Show();
        var icons = TrayIcon.GetIcons(Application.Current!);
        Assert.NotNull(icons);
        var tray = Assert.Single(icons!);
        Assert.Equal("SquareSnap", tray.ToolTipText);
        Assert.NotNull(tray.Menu);
        Assert.Equal(6, tray.Menu!.Items.Count); // 5 actions + separator
        w.Close();
        Assert.Null(TrayIcon.GetIcons(Application.Current!)); // removed again on close
    }

    [AvaloniaFact]
    public void StatusText_StaysVisibleAfterNormalAndErrorMessages()
    {
        var s = Session();
        s.Settings.Hotkey = "";
        s.Clipboard = PlatformServices.CreateClipboard(Logger.Null);
        var w = new MainWindow(s);
        w.Show();
        var status = w.FindControl<TextBlock>("StatusText")!;

        w.SetStatus("Ready. Press Ctrl+Alt+S anywhere to capture a square.");
        Assert.NotNull(status.Foreground); // beta 2 set it to null: invisible text
        var normal = status.Foreground;

        w.SetStatus("Something failed", error: true);
        Assert.Equal(Avalonia.Media.Brushes.OrangeRed, status.Foreground);

        w.SetStatus("Back to normal");
        Assert.NotNull(status.Foreground);
        Assert.Equal(normal, status.Foreground);
        Assert.Equal("Back to normal", status.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void SavedBanner_ShowsFolderAndFileThenCloses()
    {
        var s = Session();
        s.Settings.Hotkey = "";
        s.Clipboard = PlatformServices.CreateClipboard(Logger.Null);
        var w = new MainWindow(s);
        w.Show();

        var banner = w.FindControl<Border>("SavedBanner")!;
        Assert.False(banner.IsVisible);

        string path = Path.Combine(Path.GetTempPath(), "Shots", "SquareCap-20261111-003.png");
        w.ShowSaved(path);
        Assert.True(banner.IsVisible);
        Assert.Equal($"Saved to {Path.Combine(Path.GetTempPath(), "Shots")}", w.FindControl<TextBlock>("SavedTitle")!.Text);
        Assert.Equal("SquareCap-20261111-003.png", w.FindControl<TextBlock>("SavedFile")!.Text);
        Assert.True(w.FindControl<Button>("SavedOpen")!.IsVisible);

        // The close button starts the fade; a new save brings the banner back.
        w.FindControl<Button>("SavedClose")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(w.BannerHiding);
        w.ShowSaved(path);
        Assert.False(w.BannerHiding);
        Assert.True(banner.IsVisible);
        w.Close();
    }
}
