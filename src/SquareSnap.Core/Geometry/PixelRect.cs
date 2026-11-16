namespace SquareSnap.Core.Geometry;

/// <summary>A point in physical screen pixels (virtual-desktop coordinates; may be negative on multi-monitor setups).</summary>
public readonly record struct PixelPoint(int X, int Y)
{
    public override string ToString() => $"({X},{Y})";
}

/// <summary>
/// A rectangle in physical screen pixels. <see cref="Right"/> and <see cref="Bottom"/> are exclusive,
/// so a rectangle at X=0 with Width=10 covers pixels 0..9.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public bool IsSquare => Width == Height && Width > 0;
    public PixelPoint Center => new(X + Width / 2, Y + Height / 2);

    public bool Contains(PixelPoint p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    public bool Contains(PixelRect r) => !r.IsEmpty && r.X >= X && r.Y >= Y && r.Right <= Right && r.Bottom <= Bottom;

    public static PixelRect Union(PixelRect a, PixelRect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        int x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        return new PixelRect(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }

    /// <summary>Squared distance from a point to the nearest pixel of this rectangle (0 when inside).</summary>
    public long DistanceSquaredTo(PixelPoint p)
    {
        long dx = p.X < X ? X - p.X : p.X >= Right ? p.X - (Right - 1) : 0;
        long dy = p.Y < Y ? Y - p.Y : p.Y >= Bottom ? p.Y - (Bottom - 1) : 0;
        return dx * dx + dy * dy;
    }

    /// <summary>Format used in logs and settings: "x,y,w,h".</summary>
    public override string ToString() => $"{X},{Y},{Width},{Height}";

    public static bool TryParse(string? text, out PixelRect rect)
    {
        rect = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y) ||
            !int.TryParse(parts[2], out int w) || !int.TryParse(parts[3], out int h)) return false;
        if (w <= 0 || h <= 0) return false;
        rect = new PixelRect(x, y, w, h);
        return true;
    }
}
