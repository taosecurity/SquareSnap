using Avalonia.Controls;
using SquareSnap.Capture;
using SquareSnap.Core.Cli;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Logging;
using SquareSnap.Core.Settings;

namespace SquareSnap.App;

/// <summary>Everything one run of SquareSnap shares: options, log, settings and the platform services.</summary>
internal sealed class AppSession(CliOptions options, Logger log, SettingsStore store, AppSettings settings)
{
    public static AppSession Current { get; set; } = null!;

    public CliOptions Options { get; } = options;
    public Logger Log { get; } = log;
    public SettingsStore Store { get; } = store;
    public AppSettings Settings { get; } = settings;
    public ICaptureBackend Backend { get; set; } = null!;
    public IClipboardWriter Clipboard { get; set; } = null!;

    /// <summary>A window whose Screens list the monitors (Linux capture fits them onto the screenshot); set when the UI starts.</summary>
    public TopLevel? ScreensSource { get; set; }

    // The tray and a `squaresnap capture` run each hold their own copy of the settings. LastRegion is the one value
    // both write, so each save first takes the file's LastRegion when another run changed it since we last looked;
    // before 2.0.1 the tray wrote its old copy back and --last repeated the wrong square.
    private string? _knownLastRegion = settings.LastRegion;

    public void SaveSettings()
    {
        string? onDisk = Store.PeekLastRegion();
        if (onDisk != _knownLastRegion && Settings.LastRegion == _knownLastRegion)
        {
            Log.Debug($"Another SquareSnap saved LastRegion {onDisk}; keeping it.");
            Settings.LastRegion = onDisk;
        }
        Store.Save(Settings);
        _knownLastRegion = Settings.LastRegion;
    }

    /// <summary>Records the square just captured. Only LastRegion changes in the file: the other settings on disk stay
    /// as they are, so a capture run never puts back options the tray changed meanwhile.</summary>
    public void SaveLastRegion(string region)
    {
        Settings.LastRegion = region;
        var onDisk = Store.Load();   // the file as it is now, with whatever the other run saved
        onDisk.LastRegion = region;
        Store.Save(onDisk);
        _knownLastRegion = region;
    }

    /// <summary>The monitors as Avalonia reports them, for the Linux capture. Empty on Windows, which asks the system itself.</summary>
    public IReadOnlyList<ScreenInfo> CurrentScreens()
    {
        if (OperatingSystem.IsWindows() || ScreensSource?.Screens is not { } screens) return [];
        var list = new List<ScreenInfo>();
        int i = 1;
        foreach (var s in screens.All)
        {
            list.Add(new ScreenInfo(string.IsNullOrWhiteSpace(s.DisplayName) ? $"screen {i}" : s.DisplayName!,
                new PixelRect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height), s.Scaling, s.IsPrimary));
            i++;
        }
        return list;
    }
}
