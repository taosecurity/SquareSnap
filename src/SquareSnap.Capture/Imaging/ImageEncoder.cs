using System.Runtime.InteropServices;
using SkiaSharp;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Naming;

namespace SquareSnap.Capture.Imaging;

/// <summary>Turns a captured square into file bytes: optional resize to an exact size, optional 1-px border, then PNG, JPEG or WebP.</summary>
public static class ImageEncoder
{
    /// <summary>The light gray 1.0 drew around every capture (System.Drawing's LightGray).</summary>
    public static readonly SKColor BorderColor = new(211, 211, 211);

    public static byte[] Encode(ScreenImage image, ImageFormat format, int quality, OutputSize size, bool border)
    {
        using var bitmap = Process(image, size, border);
        using var skImage = SKImage.FromBitmap(bitmap);
        using var data = skImage.Encode(ToSkia(format), Math.Clamp(quality, 1, 100))
            ?? throw new InvalidOperationException($"The image library couldn't encode a {format} image.");
        return data.ToArray();
    }

    /// <summary>PNG at the captured size with no border (what goes on the clipboard).</summary>
    public static byte[] EncodePngPlain(ScreenImage image) => Encode(image, ImageFormat.Png, 100, OutputSize.Native, border: false);

    /// <summary>The final pixels (resized and bordered as asked), as BGRA, for the preview and the clipboard.</summary>
    public static ScreenImage Render(ScreenImage image, OutputSize size, bool border)
    {
        using var bitmap = Process(image, size, border);
        var pixels = new byte[bitmap.Width * bitmap.Height * 4];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return new ScreenImage(bitmap.Width, bitmap.Height, new PixelRect(image.Origin.X, image.Origin.Y, bitmap.Width, bitmap.Height), pixels);
    }

    private static SKBitmap Process(ScreenImage image, OutputSize size, bool border)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var source = new SKBitmap(info);
        Marshal.Copy(image.Bgra, 0, source.GetPixels(), image.Bgra.Length);

        int side = size.SideFor(image.Width);
        SKBitmap result = source;
        if (side != image.Width || image.Height != image.Width)
        {
            // Shrinking: mipmapped linear avoids shimmer on big reductions. Growing: Mitchell cubic stays sharp.
            var sampling = side < image.Width
                ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                : new SKSamplingOptions(SKCubicResampler.Mitchell);
            result = source.Resize(new SKImageInfo(side, side, SKColorType.Bgra8888, SKAlphaType.Opaque), sampling)
                     ?? throw new InvalidOperationException($"The image library couldn't resize the capture to {side}x{side}.");
            source.Dispose();
        }

        if (border)
        {
            using var canvas = new SKCanvas(result);
            using var paint = new SKPaint { Color = BorderColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
            canvas.DrawRect(0.5f, 0.5f, result.Width - 1, result.Height - 1, paint);
        }
        return result;
    }

    private static SKEncodedImageFormat ToSkia(ImageFormat f) => f switch
    {
        ImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
        ImageFormat.Webp => SKEncodedImageFormat.Webp,
        _ => SKEncodedImageFormat.Png,
    };
}
