using SkiaSharp;
using SquareSnap.Capture;
using SquareSnap.Capture.Imaging;
using SquareSnap.Capture.Windows;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Naming;

namespace SquareSnap.Tests;

public class ImagingTests
{
    /// <summary>A test "screen": every pixel encodes its own position, so any shift or blur shows up.</summary>
    internal static ScreenImage Pattern(PixelRect origin)
    {
        var px = new byte[origin.Width * origin.Height * 4];
        for (int y = 0; y < origin.Height; y++)
        for (int x = 0; x < origin.Width; x++)
        {
            int i = (y * origin.Width + x) * 4;
            px[i] = (byte)(x * 7);
            px[i + 1] = (byte)(y * 5);
            px[i + 2] = (byte)((x + y) * 3);
            px[i + 3] = 255;
        }
        return new ScreenImage(origin.Width, origin.Height, origin, px);
    }

    [Fact]
    public void Crop_UsesVirtualDesktopCoordinates()
    {
        // Two monitors side by side, the left one at negative X.
        var screen = Pattern(new PixelRect(-100, 0, 300, 120));
        var sq = screen.Crop(new PixelRect(-10, 20, 50, 50));
        Assert.Equal(50, sq.Width);
        Assert.Equal(screen.PixelAt(-10, 20), sq.PixelAt(-10, 20));
        Assert.Equal(screen.PixelAt(39, 69), sq.PixelAt(39, 69));
        Assert.Throws<ArgumentException>(() => screen.Crop(new PixelRect(190, 0, 20, 20)));
    }

    [Fact]
    public void Png_IsPixelExact()
    {
        var img = Pattern(new PixelRect(0, 0, 64, 64));
        byte[] png = ImageEncoder.Encode(img, ImageFormat.Png, 100, OutputSize.Native, border: false);
        using var back = SKBitmap.Decode(png);
        Assert.Equal(64, back.Width);
        for (int y = 0; y < 64; y += 7)
        for (int x = 0; x < 64; x += 5)
        {
            var c = back.GetPixel(x, y);
            var (b, g, r) = img.PixelAt(x, y);
            Assert.Equal((r, g, b, (byte)255), (c.Red, c.Green, c.Blue, c.Alpha));
        }
    }

    [Theory]
    [InlineData(1080)]
    [InlineData(32)]
    public void Resize_GivesExactSide(int side)
    {
        var img = Pattern(new PixelRect(0, 0, 100, 100));
        using var back = SKBitmap.Decode(ImageEncoder.Encode(img, ImageFormat.Png, 100, new OutputSize(side), false));
        Assert.Equal((side, side), (back.Width, back.Height));
    }

    [Fact]
    public void Border_IsLightGrayOnTheEdgeOnly()
    {
        var img = Pattern(new PixelRect(0, 0, 40, 40));
        using var back = SKBitmap.Decode(ImageEncoder.Encode(img, ImageFormat.Png, 100, OutputSize.Native, border: true));
        foreach (var (x, y) in new[] { (0, 0), (39, 0), (0, 39), (39, 39), (20, 0), (0, 20), (39, 20), (20, 39) })
            Assert.Equal(ImageEncoder.BorderColor, back.GetPixel(x, y));
        var (b, g, r) = img.PixelAt(1, 1);
        Assert.Equal(new SKColor(r, g, b), back.GetPixel(1, 1)); // inside untouched
    }

    [Theory]
    [InlineData(ImageFormat.Jpeg, 0xFF, 0xD8)]
    [InlineData(ImageFormat.Webp, (byte)'R', (byte)'I')]
    [InlineData(ImageFormat.Png, 0x89, (byte)'P')]
    public void Formats_HaveTheRightSignature(ImageFormat f, byte first, byte second)
    {
        byte[] data = ImageEncoder.Encode(Pattern(new PixelRect(0, 0, 32, 32)), f, 85, OutputSize.Native, false);
        Assert.Equal((first, second), (data[0], data[1]));
    }

    [Fact]
    public void Render_MatchesEncode()
    {
        var img = Pattern(new PixelRect(5, 5, 20, 20));
        var final = ImageEncoder.Render(img, new OutputSize(40), border: true);
        Assert.Equal(40, final.Width);
        using var back = SKBitmap.Decode(ImageEncoder.EncodePngPlain(final));
        Assert.Equal(ImageEncoder.BorderColor, back.GetPixel(0, 0));
    }

    [Fact]
    public void ClipboardDib_IsBottomUpWithHeader()
    {
        if (!OperatingSystem.IsWindows()) return;
        var img = Pattern(new PixelRect(0, 0, 3, 2));
        byte[] dib = WindowsClipboard.BuildDib(img);
        Assert.Equal(40 + 3 * 2 * 4, dib.Length);
        Assert.Equal(40, BitConverter.ToInt32(dib, 0));   // biSize
        Assert.Equal(3, BitConverter.ToInt32(dib, 4));    // width
        Assert.Equal(2, BitConverter.ToInt32(dib, 8));    // positive height = bottom-up
        Assert.Equal(32, BitConverter.ToInt16(dib, 14));  // bits per pixel
        // First stored row is the image's bottom row (y = 1).
        Assert.Equal(img.Bgra.AsSpan(12, 12).ToArray(), dib.AsSpan(40, 12).ToArray());
    }

    [Theory]
    [InlineData("S", 0x53u)]
    [InlineData("5", 0x35u)]
    [InlineData("F1", 0x70u)]
    [InlineData("F24", 0x87u)]
    [InlineData("PrintScreen", 0x2Cu)]
    public void HotkeyVirtualKeys(string key, uint vk)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(vk, WindowsHotkey.VirtualKey(key));
    }
}
