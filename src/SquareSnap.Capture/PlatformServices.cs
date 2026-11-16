using SquareSnap.Capture.Linux;
using SquareSnap.Capture.Windows;
using SquareSnap.Core.Cli;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Logging;

namespace SquareSnap.Capture;

/// <summary>Grabs the whole screen. One implementation per platform: Windows GDI, and on Linux the portal, the tools and X11 (step 4).</summary>
public interface ICaptureBackend
{
    string Name { get; }

    /// <summary>The monitors, in physical pixels, with their scaling.</summary>
    MonitorLayout GetLayout();

    /// <summary>A frozen image of the whole virtual desktop (every monitor).</summary>
    ScreenImage CaptureAll(MonitorLayout layout);

    /// <summary>
    /// The layout and the frozen image together. Windows works out the layout first and copies it; Linux needs the
    /// screenshot first, then fits <paramref name="screens"/> (from the UI toolkit) onto it, and may wait for the
    /// desktop's portal, hence async.
    /// </summary>
    Task<(MonitorLayout Layout, ScreenImage Image)> CaptureScreenAsync(IReadOnlyList<ScreenInfo> screens, CancellationToken ct = default)
    {
        var layout = GetLayout();
        return Task.FromResult((layout, CaptureAll(layout)));
    }
}

/// <summary>Puts an image on the system clipboard.</summary>
public interface IClipboardWriter
{
    /// <summary>Copies the pixels, plus a PNG copy for apps that prefer it. Returns false (and logs why) on failure.</summary>
    bool SetImage(ScreenImage image, byte[] png);
}

/// <summary>A system-wide hotkey. <see cref="Pressed"/> is raised on a background thread.</summary>
public interface IGlobalHotkey : IDisposable
{
    event Action? Pressed;

    /// <summary>Registers <paramref name="spec"/> (null turns the hotkey off). Returns an error message, or null on success.</summary>
    string? Register(HotkeySpec? spec);
}

/// <summary>Picks the platform pieces for the OS SquareSnap is running on.</summary>
public static class PlatformServices
{
    public static ICaptureBackend CreateCaptureBackend(Logger log)
    {
        if (OperatingSystem.IsWindows()) return new WindowsGdiCapture(log);
        if (OperatingSystem.IsLinux()) return new LinuxCapture(log);
        throw new CaptureException("SquareSnap captures on Windows and Linux only.");
    }

    /// <summary>
    /// The clipboard for "squaresnap capture" and the fallback for the window. On Linux that's wl-copy or xclip,
    /// which keep the image after SquareSnap exits; the window itself uses Avalonia's clipboard first.
    /// </summary>
    public static IClipboardWriter CreateClipboard(Logger log) =>
        OperatingSystem.IsWindows() ? new WindowsClipboard(log)
        : OperatingSystem.IsLinux() ? new CommandClipboard(log)
        : new UnsupportedClipboard(log);

    public static IGlobalHotkey CreateHotkey(Logger log) =>
        OperatingSystem.IsWindows() ? new WindowsHotkey(log)
        : OperatingSystem.IsLinux() ? new PortalHotkey(log)
        : new UnsupportedHotkey();

    private sealed class UnsupportedClipboard(Logger log) : IClipboardWriter
    {
        public bool SetImage(ScreenImage image, byte[] png)
        {
            log.Warning("Copying images to the clipboard isn't supported on this system; the capture was not copied.");
            return false;
        }
    }

    private sealed class UnsupportedHotkey : IGlobalHotkey
    {
        public event Action? Pressed { add { } remove { } }
        public string? Register(HotkeySpec? spec) =>
            spec == null ? null : "Global hotkeys aren't supported on this system. Bind 'squaresnap capture' to a key in your desktop's keyboard settings instead.";
        public void Dispose() { }
    }
}
