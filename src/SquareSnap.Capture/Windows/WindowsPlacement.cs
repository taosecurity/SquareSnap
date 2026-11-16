using System.Runtime.Versioning;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Logging;

namespace SquareSnap.Capture.Windows;

/// <summary>
/// Puts a window exactly over a monitor, in real pixels. UI toolkits size windows in scaled units, and when a
/// window crosses from a 150% monitor to a 100% one Windows rescales it on the way (WM_DPICHANGED), so an overlay
/// can end up two-thirds the size of its monitor (beta 3 on a mixed-scaling setup). Placing it twice with
/// SetWindowPos avoids that: the first move lands it on the monitor and lets the scale change happen, the second
/// sets the exact size on that monitor, where no further scale change can interfere.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsPlacement
{
    /// <summary>Returns the rectangle the window really occupies afterwards.</summary>
    public static PixelRect CoverMonitor(IntPtr hwnd, PixelRect bounds, string monitorName, Logger log)
    {
        const uint flags = Native.SWP_SHOWWINDOW | Native.SWP_NOOWNERZORDER;
        for (int pass = 1; pass <= 3; pass++)
        {
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);
            var actual = Actual(hwnd);
            if (actual == bounds && pass >= 2)
            {
                log.Debug($"Overlay on {monitorName}: placed at {actual} (window DPI {Native.GetDpiForWindow(hwnd)}) after {pass} passes.");
                return actual;
            }
        }
        var final = Actual(hwnd);
        if (final != bounds)
            log.Warning($"Overlay on {monitorName}: wanted {bounds} but Windows placed it at {final} " +
                        $"(window DPI {Native.GetDpiForWindow(hwnd)}). Captures there may be offset; please send this log.");
        return final;
    }

    /// <summary>Where the window really is, in real pixels (GetWindowRect).</summary>
    public static PixelRect Actual(IntPtr hwnd) =>
        Native.GetWindowRect(hwnd, out var r) ? new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : default;
}
