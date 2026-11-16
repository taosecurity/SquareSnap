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

    public void SaveSettings() => Store.Save(Settings);

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
