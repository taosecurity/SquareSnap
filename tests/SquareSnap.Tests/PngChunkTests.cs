using System.Buffers.Binary;
using System.Text;
using SquareSnap.Capture.Imaging;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Naming;

namespace SquareSnap.Tests;

/// <summary>What the PNG files SquareSnap writes contain, chunk by chunk.</summary>
public class PngChunkTests
{
    internal static List<string> Chunks(byte[] png)
    {
        var names = new List<string>();
        int i = 8; // after the signature
        while (i + 8 <= png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(i, 4));
            names.Add(Encoding.ASCII.GetString(png, i + 4, 4));
            i += 12 + len;
        }
        return names;
    }

    [Fact]
    public void Png_HasNoColourTagsThatMakeViewersShiftColours()
    {
        // A gAMA, iCCP, sRGB or cHRM chunk makes some viewers (and GDI+) adjust the colours on load,
        // so a "pixel-exact" capture would look slightly different. SquareSnap writes plain pixels only.
        byte[] png = ImageEncoder.Encode(ImagingTests.Pattern(new PixelRect(0, 0, 32, 32)), ImageFormat.Png, 100, OutputSize.Native, false);
        var chunks = Chunks(png);
        Assert.Equal("IHDR", chunks[0]);
        Assert.Equal("IEND", chunks[^1]);
        foreach (string colourTag in new[] { "gAMA", "iCCP", "sRGB", "cHRM" })
            Assert.DoesNotContain(colourTag, chunks);
    }
}
