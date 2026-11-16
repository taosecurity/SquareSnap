using SquareSnap.Core.Logging;

namespace SquareSnap.Capture.Linux;

/// <summary>Desktop facts the window needs on Linux.</summary>
public static class LinuxDesktop
{
    /// <summary>True when a tray host runs (KDE Plasma always; GNOME with the AppIndicator extension).</summary>
    public static Task<bool> TrayHostRunningAsync(Logger log) => Portal.TrayHostRunningAsync(log);
}
