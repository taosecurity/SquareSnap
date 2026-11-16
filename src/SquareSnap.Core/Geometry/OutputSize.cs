namespace SquareSnap.Core.Geometry;

/// <summary>
/// The size of the saved image. <see cref="Native"/> keeps the captured pixels as they are;
/// any other value scales the square to exactly that many pixels per side.
/// </summary>
public readonly record struct OutputSize(int Pixels)
{
    public const int MaxPixels = 16384;

    public static readonly OutputSize Native = new(0);

    /// <summary>The presets offered in the UI.</summary>
    public static IReadOnlyList<OutputSize> Presets { get; } =
        [Native, new(256), new(512), new(1024), new(1080), new(2048)];

    public bool IsNative => Pixels == 0;

    /// <summary>Side length of the saved image for a capture of <paramref name="capturedSide"/> pixels.</summary>
    public int SideFor(int capturedSide) => IsNative ? capturedSide : Pixels;

    public override string ToString() => IsNative ? "native" : Pixels.ToString();

    /// <summary>Parses "native", "0" or a whole number from 16 to <see cref="MaxPixels"/>.</summary>
    public static bool TryParse(string? text, out OutputSize size, out string? error)
    {
        size = Native;
        error = null;
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("native", StringComparison.OrdinalIgnoreCase) || text.Trim() == "0")
            return true;
        if (!int.TryParse(text.Trim().TrimEnd('p', 'x', 'P', 'X'), out int px))
        {
            error = $"'{text}' is not a size. Use a number of pixels such as 1080, or 'native'.";
            return false;
        }
        if (px < 16 || px > MaxPixels)
        {
            error = $"Size {px} is out of range. Use 16 to {MaxPixels} pixels, or 'native'.";
            return false;
        }
        size = new OutputSize(px);
        return true;
    }
}
