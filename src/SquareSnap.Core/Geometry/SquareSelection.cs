namespace SquareSnap.Core.Geometry;

/// <summary>
/// The square rule shared by every platform: the square follows the longer side of the drag,
/// stays anchored at the corner where the drag started, and never leaves the given bounds
/// (normally the monitor the drag started on).
/// </summary>
public static class SquareSelection
{
    /// <summary>Selections smaller than this (a click, or a tiny wobble) count as "no selection".</summary>
    public const int MinimumSize = 4;

    public static bool IsUsable(PixelRect r) => r.IsSquare && r.Width >= MinimumSize;

    /// <summary>Square from a drag between <paramref name="start"/> and <paramref name="end"/>, clamped to <paramref name="bounds"/>.</summary>
    public static PixelRect FromDrag(PixelPoint start, PixelPoint end, PixelRect bounds)
    {
        if (bounds.IsEmpty) return default;
        start = ClampPoint(start, bounds);

        int dx = end.X - start.X, dy = end.Y - start.Y;
        bool right = dx >= 0, down = dy >= 0;
        int size = Math.Max(Math.Abs(dx), Math.Abs(dy));

        // Room left between the anchor corner and the bounds edge, in the drag direction.
        int roomX = right ? bounds.Right - start.X : start.X - bounds.X;
        int roomY = down ? bounds.Bottom - start.Y : start.Y - bounds.Y;
        size = Math.Min(size, Math.Min(roomX, roomY));
        if (size <= 0) return new PixelRect(start.X, start.Y, 0, 0);

        int x = right ? start.X : start.X - size;
        int y = down ? start.Y : start.Y - size;
        return new PixelRect(x, y, size, size);
    }

    /// <summary>Moves the square by (dx, dy), stopping at the bounds edges.</summary>
    public static PixelRect Move(PixelRect square, int dx, int dy, PixelRect bounds)
    {
        if (square.IsEmpty || bounds.IsEmpty) return square;
        int size = Math.Min(square.Width, Math.Min(bounds.Width, bounds.Height));
        int x = Math.Clamp(square.X + dx, bounds.X, bounds.Right - size);
        int y = Math.Clamp(square.Y + dy, bounds.Y, bounds.Bottom - size);
        return new PixelRect(x, y, size, size);
    }

    /// <summary>
    /// Grows or shrinks the square by <paramref name="delta"/> pixels, keeping its top-left corner where possible.
    /// Never smaller than <see cref="MinimumSize"/> or larger than the bounds allow.
    /// </summary>
    public static PixelRect Resize(PixelRect square, int delta, PixelRect bounds)
    {
        if (square.IsEmpty || bounds.IsEmpty) return square;
        int max = Math.Min(bounds.Width, bounds.Height);
        int size = Math.Clamp(square.Width + delta, Math.Min(MinimumSize, max), max);
        // Keep the top-left corner, but slide back inside if growing would cross an edge.
        int x = Math.Clamp(square.X, bounds.X, bounds.Right - size);
        int y = Math.Clamp(square.Y, bounds.Y, bounds.Bottom - size);
        return new PixelRect(x, y, size, size);
    }

    /// <summary>A square of a fixed size centred on <paramref name="center"/> (fixed-size mode), kept inside the bounds.</summary>
    public static PixelRect PlaceFixed(PixelPoint center, int size, PixelRect bounds)
    {
        if (bounds.IsEmpty || size <= 0) return default;
        size = Math.Min(size, Math.Min(bounds.Width, bounds.Height));
        int x = Math.Clamp(center.X - size / 2, bounds.X, bounds.Right - size);
        int y = Math.Clamp(center.Y - size / 2, bounds.Y, bounds.Bottom - size);
        return new PixelRect(x, y, size, size);
    }

    /// <summary>
    /// Fits a saved square (for "repeat last region") into the current bounds: same size if it fits,
    /// moved inside if it hangs over an edge, shrunk only when the bounds are smaller than the square.
    /// </summary>
    public static PixelRect FitInside(PixelRect square, PixelRect bounds)
    {
        if (square.IsEmpty || bounds.IsEmpty) return default;
        int size = Math.Min(Math.Min(square.Width, square.Height), Math.Min(bounds.Width, bounds.Height));
        int x = Math.Clamp(square.X, bounds.X, bounds.Right - size);
        int y = Math.Clamp(square.Y, bounds.Y, bounds.Bottom - size);
        return new PixelRect(x, y, size, size);
    }

    private static PixelPoint ClampPoint(PixelPoint p, PixelRect b) =>
        new(Math.Clamp(p.X, b.X, b.Right - 1), Math.Clamp(p.Y, b.Y, b.Bottom - 1));
}
