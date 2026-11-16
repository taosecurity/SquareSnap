namespace SquareSnap.Core.Geometry;

/// <summary>One monitor, in physical pixels. <paramref name="Scale"/> is the display scaling (1.5 = 150%).</summary>
/// <remarks>
/// On Linux (step 4) <see cref="Bounds"/> are in the captured image's pixels and <see cref="Scale"/> is image pixels
/// per overlay unit (<see cref="ScreenMapping"/>); <see cref="FixedScale"/> then tells the overlay to use that scale
/// instead of the window's live render scaling, and <see cref="WindowPosition"/> is where the overlay window goes
/// in the windowing system's own coordinates (X11; native Wayland can't place windows).
/// </remarks>
public sealed record Display(string Name, PixelRect Bounds, double Scale, bool IsPrimary)
{
    /// <summary>The overlay's window position in the windowing system's coordinates, when those differ from <see cref="Bounds"/>.</summary>
    public PixelPoint? WindowPosition { get; init; }

    /// <summary>Use <see cref="Scale"/> as is in the overlay (Linux), instead of reading the window's render scaling (Windows).</summary>
    public bool FixedScale { get; init; }

    public override string ToString() =>
        $"{Name} {Bounds.Width}x{Bounds.Height} at ({Bounds.X},{Bounds.Y}) scale {Scale * 100:0}%{(IsPrimary ? " primary" : "")}";
}

/// <summary>The set of monitors the capture backend reported, plus helpers to map overlay coordinates to real pixels.</summary>
public sealed class MonitorLayout
{
    public IReadOnlyList<Display> Monitors { get; }

    /// <summary>The smallest rectangle covering every monitor (the "virtual desktop").</summary>
    public PixelRect VirtualBounds { get; }

    public MonitorLayout(IEnumerable<Display> monitors)
    {
        Monitors = monitors.Where(m => !m.Bounds.IsEmpty).ToList();
        if (Monitors.Count == 0)
            throw new ArgumentException("No monitors with a usable size were reported.", nameof(monitors));
        if (Monitors.Any(m => m.Scale <= 0 || double.IsNaN(m.Scale)))
            throw new ArgumentException("A monitor reported a scale of zero or less.", nameof(monitors));
        VirtualBounds = Monitors.Aggregate(default(PixelRect), (acc, m) => PixelRect.Union(acc, m.Bounds));
    }

    public Display Primary => Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors[0];

    /// <summary>The monitor containing <paramref name="p"/>, or the nearest one when the point is in a gap between monitors.</summary>
    public Display MonitorAt(PixelPoint p) =>
        Monitors.FirstOrDefault(m => m.Bounds.Contains(p)) ?? Monitors.MinBy(m => m.Bounds.DistanceSquaredTo(p))!;

    /// <summary>
    /// Converts a position inside a monitor's overlay window (in that window's logical units, as UI toolkits report
    /// mouse positions) to physical virtual-desktop pixels.
    /// </summary>
    public static PixelPoint ToPhysical(Display monitor, double logicalX, double logicalY)
    {
        int x = monitor.Bounds.X + (int)Math.Floor(logicalX * monitor.Scale);
        int y = monitor.Bounds.Y + (int)Math.Floor(logicalY * monitor.Scale);
        return new PixelPoint(
            Math.Clamp(x, monitor.Bounds.X, monitor.Bounds.Right - 1),
            Math.Clamp(y, monitor.Bounds.Y, monitor.Bounds.Bottom - 1));
    }

    /// <summary>
    /// True when a window whose client area is <paramref name="clientWidth"/> x <paramref name="clientHeight"/> logical
    /// units at <paramref name="scale"/> is at least as big as <paramref name="bounds"/> (within a pixel).
    /// Too big is harmless (the extra is clipped at the monitor edge; the picture is drawn 1:1 from the corner);
    /// too small leaves part of the monitor uncovered.
    /// Beta 4: client 1706.67 x 960 at scale 1 over 2560 x 1440 is NOT covered.
    /// Beta 6: client 2560 x 1440 at a stale scale 1.5 over 2560 x 1440 IS covered (Windows itself reported 144 DPI).
    /// </summary>
    public static bool Covers(double clientWidth, double clientHeight, double scale, PixelRect bounds) =>
        clientWidth * scale >= bounds.Width - 1 && clientHeight * scale >= bounds.Height - 1;

    /// <summary>Converts a physical rectangle on a monitor to that monitor's overlay logical units (for drawing the selection).</summary>
    public static (double X, double Y, double Width, double Height) ToLogical(Display monitor, PixelRect r) =>
        ((r.X - monitor.Bounds.X) / monitor.Scale, (r.Y - monitor.Bounds.Y) / monitor.Scale,
         r.Width / monitor.Scale, r.Height / monitor.Scale);
}
