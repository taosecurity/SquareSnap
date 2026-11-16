using SquareSnap.Core.Geometry;

namespace SquareSnap.Capture;

/// <summary>
/// A frozen screen grab: 32-bit BGRA pixels, top row first, alpha always 255, rows packed (stride = Width * 4).
/// <see cref="Origin"/> is where the image sits on the virtual desktop, in physical pixels.
/// </summary>
public sealed class ScreenImage
{
    public int Width { get; }
    public int Height { get; }
    public PixelRect Origin { get; }
    public byte[] Bgra { get; }
    public int Stride => Width * 4;

    public ScreenImage(int width, int height, PixelRect origin, byte[] bgra)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException($"Image size {width}x{height} is empty.");
        if (bgra.Length != (long)width * height * 4)
            throw new ArgumentException($"Pixel buffer holds {bgra.Length} bytes; {width}x{height} needs {(long)width * height * 4}.");
        Width = width;
        Height = height;
        Origin = origin;
        Bgra = bgra;
    }

    /// <summary>Copies out the pixels under <paramref name="rect"/> (virtual-desktop coordinates). The rectangle must lie inside the image.</summary>
    public ScreenImage Crop(PixelRect rect)
    {
        if (!Origin.Contains(rect))
            throw new ArgumentException($"Square {rect} is not inside the captured screen {Origin}.", nameof(rect));
        int x0 = rect.X - Origin.X, y0 = rect.Y - Origin.Y;
        var dst = new byte[rect.Width * rect.Height * 4];
        int rowBytes = rect.Width * 4;
        for (int row = 0; row < rect.Height; row++)
            Buffer.BlockCopy(Bgra, ((y0 + row) * Width + x0) * 4, dst, row * rowBytes, rowBytes);
        return new ScreenImage(rect.Width, rect.Height, rect, dst);
    }

    /// <summary>The (B, G, R) of the pixel at virtual-desktop position (x, y).</summary>
    public (byte B, byte G, byte R) PixelAt(int x, int y)
    {
        int i = ((y - Origin.Y) * Width + (x - Origin.X)) * 4;
        return (Bgra[i], Bgra[i + 1], Bgra[i + 2]);
    }
}

/// <summary>A capture failed in a way the user can act on; the message says what to do.</summary>
public sealed class CaptureException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The user refused or cancelled the capture at the desktop's own prompt (the screenshot portal answered 1).</summary>
public sealed class CaptureCancelledException(string message) : Exception(message);
