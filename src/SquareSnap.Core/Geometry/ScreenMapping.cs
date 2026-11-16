namespace SquareSnap.Core.Geometry;

/// <summary>One screen as the UI toolkit reports it (Avalonia's Screens): its bounds in the toolkit's pixels and its scaling.</summary>
/// <remarks>
/// On X11 the bounds are physical pixels and <see cref="Scaling"/> is the desktop scale. On native Wayland (Avalonia.Wayland)
/// the bounds are the compositor's logical layout and Scaling is always 1: the real scale belongs to each window.
/// </remarks>
public sealed record ScreenInfo(string Name, PixelRect Bounds, double Scaling, bool IsPrimary);

/// <summary>
/// Linux: fits the screens the toolkit reports onto the screenshot the desktop gave back. A Wayland screenshot
/// (the portal, spectacle, grim, gnome-screenshot) covers the whole desktop at one resolution, usually the highest
/// monitor scale: on KDE Plasma with one 2560x1440-logical monitor at 150%, the image is 3840x2160. So the image is
/// the screens' union times a factor k, and each monitor's slice is its rectangle times k. The overlay then shows
/// k image pixels per logical unit, which is 1:1 with the screen whenever k equals that monitor's scale.
/// </summary>
public static class ScreenMapping
{
    /// <summary>
    /// The monitor layout in image pixels, plus a sentence for the log. Never throws for odd input: with no screens,
    /// or an image whose shape doesn't match the screens, it falls back to one monitor covering the whole image.
    /// </summary>
    public static (MonitorLayout Layout, string Note) ToImageLayout(IReadOnlyList<ScreenInfo> screens, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            throw new ArgumentException($"The screenshot is empty ({imageWidth}x{imageHeight}).");
        var usable = screens.Where(s => !s.Bounds.IsEmpty && s.Scaling > 0).ToList();
        if (usable.Count == 0)
            return (Single("screen", imageWidth, imageHeight, 1, new PixelPoint(0, 0)),
                $"No screens were reported, so the {imageWidth}x{imageHeight} screenshot is treated as one monitor.");

        var union = usable.Aggregate(default(PixelRect), (acc, s) => PixelRect.Union(acc, s.Bounds));
        double kx = (double)imageWidth / union.Width, ky = (double)imageHeight / union.Height;
        if (Math.Abs(kx - ky) / Math.Max(kx, ky) > 0.02)
        {
            // The image isn't the whole desktop (some tools grab one monitor): match it to a monitor of the same shape.
            var match = usable
                .Where(s => Math.Abs((double)imageWidth / s.Bounds.Width - (double)imageHeight / s.Bounds.Height) / ((double)imageWidth / s.Bounds.Width) <= 0.02)
                .OrderByDescending(s => s.IsPrimary).FirstOrDefault();
            var pick = match ?? usable.FirstOrDefault(s => s.IsPrimary) ?? usable[0];
            double k1 = (double)imageWidth / pick.Bounds.Width;
            return (Single(pick.Name, imageWidth, imageHeight, k1 * pick.Scaling, new PixelPoint(pick.Bounds.X, pick.Bounds.Y)),
                $"The {imageWidth}x{imageHeight} screenshot doesn't match the desktop {union.Width}x{union.Height} (x{kx:0.###} wide, x{ky:0.###} high); " +
                $"treating it as monitor {pick.Name} only.");
        }

        double k = (kx + ky) / 2;
        var displays = new List<Display>();
        foreach (var s in usable)
        {
            int x = Edge(s.Bounds.X - union.X, k, imageWidth), right = Edge(s.Bounds.Right - union.X, k, imageWidth);
            int y = Edge(s.Bounds.Y - union.Y, k, imageHeight), bottom = Edge(s.Bounds.Bottom - union.Y, k, imageHeight);
            displays.Add(new Display(s.Name, new PixelRect(x, y, right - x, bottom - y), k * s.Scaling, s.IsPrimary)
            {
                WindowPosition = new PixelPoint(s.Bounds.X, s.Bounds.Y),
                FixedScale = true,
            });
        }
        string list = string.Join("; ", displays.Select(d => $"{d.Name} {d.Bounds.Width}x{d.Bounds.Height} at ({d.Bounds.X},{d.Bounds.Y}), {d.Scale:0.###} image px per unit"));
        return (new MonitorLayout(displays),
            $"Screenshot {imageWidth}x{imageHeight} covers the desktop {union.Width}x{union.Height} at x{k:0.###}: {list}.");
    }

    private static int Edge(int offset, double k, int limit) => Math.Clamp((int)Math.Round(offset * k), 0, limit);

    private static MonitorLayout Single(string name, int w, int h, double scale, PixelPoint windowAt) =>
        new([new Display(name, new PixelRect(0, 0, w, h), scale, true) { WindowPosition = windowAt, FixedScale = true }]);
}
