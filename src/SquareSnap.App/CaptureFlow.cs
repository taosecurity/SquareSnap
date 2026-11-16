using SquareSnap.Capture;
using SquareSnap.Core.Geometry;

namespace SquareSnap.App;

/// <summary>How to pick the square for one capture.</summary>
internal sealed record CaptureRequest(OverlayMode Mode, bool FineTune, int FixedSize, PixelRect? Region, int DelaySeconds);

/// <summary>The captured square (raw pixels at screen size), or why there is none. Both null means the user cancelled.</summary>
internal sealed record CaptureOutcome(ScreenImage? Image, string? Error)
{
    public bool Cancelled => Image == null && Error == null;
}

/// <summary>Freeze the screen, let the user pick a square (or use a given one), and cut it out.</summary>
internal static class CaptureFlow
{
    public static async Task<CaptureOutcome> RunAsync(AppSession s, CaptureRequest req)
    {
        var log = s.Log;
        if (req.DelaySeconds > 0)
        {
            log.Info($"Waiting {req.DelaySeconds} s before capturing.");
            await Task.Delay(TimeSpan.FromSeconds(req.DelaySeconds));
        }

        MonitorLayout layout;
        ScreenImage screen;
        try
        {
            (layout, screen) = await s.Backend.CaptureScreenAsync(s.CurrentScreens());
        }
        catch (CaptureCancelledException ex)
        {
            log.Info(ex.Message);
            return new CaptureOutcome(null, null);
        }
        catch (CaptureException ex)
        {
            log.Error(ex.Message);
            return new CaptureOutcome(null, ex.Message);
        }

        PixelRect square;
        if (req.Region is { } wanted)
        {
            var monitor = layout.MonitorAt(wanted.Center);
            square = SquareSelection.FitInside(wanted, monitor.Bounds);
            if (square != wanted)
                log.Warning($"Square {wanted} doesn't fit on monitor {monitor.Name} {monitor.Bounds}; captured {square} instead.");
        }
        else
        {
            log.Info($"Showing the overlay ({req.Mode}{(req.FineTune ? ", fine-tune" : "")}{(req.Mode == OverlayMode.Fixed ? $", {req.FixedSize} px" : "")}).");
            var picked = await Overlay.PickAsync(s, layout, screen, req);
            if (picked == null)
            {
                log.Info("Capture cancelled.");
                return new CaptureOutcome(null, null);
            }
            square = picked.Value;
        }

        var image = screen.Crop(square);
        log.Info($"Captured square {square} ({square.Width}x{square.Height}) on {layout.MonitorAt(square.Center).Name}.");
        s.Settings.LastRegion = square.ToString();
        s.SaveSettings();
        return new CaptureOutcome(image, null);
    }
}
