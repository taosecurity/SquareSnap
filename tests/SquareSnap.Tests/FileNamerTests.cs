using SquareSnap.Core.Naming;

namespace SquareSnap.Tests;

public class FileNamerTests
{
    private static readonly DateTime Nov11 = new(2026, 11, 11, 14, 30, 5);

    [Fact]
    public void FirstFileOfTheDay()
    {
        Assert.Equal("SquareCap-20261111-001.png",
            FileNamer.NextName(FileNamer.DefaultTemplate, Nov11, 500, ImageFormat.Png, []));
    }

    [Fact]
    public void CountsTodaysFilesInAnyFormat()
    {
        string[] existing = ["SquareCap-20261111-001.png", "SquareCap-20261111-004.jpg", "SquareCap-20261111-002.webp", "notes.txt"];
        Assert.Equal("SquareCap-20261111-005.png", FileNamer.NextName(FileNamer.DefaultTemplate, Nov11, 500, ImageFormat.Png, existing));
    }

    [Fact]
    public void CounterResetsOnANewDay()
    {
        // The 1.0 counter never reset; 2.0 ignores yesterday's files.
        string[] existing = ["SquareCap-20261110-047.png"];
        Assert.Equal("SquareCap-20261111-001.png", FileNamer.NextName(FileNamer.DefaultTemplate, Nov11, 500, ImageFormat.Png, existing));
    }

    [Fact]
    public void IgnoresOneZeroNamesAndCaseDifferences()
    {
        // 1.0 names (no dash before the counter) can never clash with 2.0 names.
        string[] existing = ["SquareCap-20261111003.png", "SQUARECAP-20261111-002.PNG"];
        Assert.Equal("SquareCap-20261111-003.png", FileNamer.NextName(FileNamer.DefaultTemplate, Nov11, 500, ImageFormat.Png, existing));
    }

    [Fact]
    public void CounterGrowsPastPadding()
    {
        string[] existing = ["SquareCap-20261111-999.png"];
        Assert.Equal("SquareCap-20261111-1000.png", FileNamer.NextName(FileNamer.DefaultTemplate, Nov11, 500, ImageFormat.Png, existing));
    }

    [Fact]
    public void AllTokens()
    {
        Assert.Equal("snap_20261111_143005_1080_07.webp",
            FileNamer.NextName("snap_{date}_{time}_{size}_{n:00}", Nov11, 1080, ImageFormat.Webp, ["snap_20261111_120000_512_06.png"]));
    }

    [Fact]
    public void NoCounterAddsSuffixOnlyWhenTaken()
    {
        Assert.Equal("cap-20261111.png", FileNamer.NextName("cap-{date}", Nov11, 1, ImageFormat.Png, []));
        Assert.Equal("cap-20261111-3.png",
            FileNamer.NextName("cap-{date}", Nov11, 1, ImageFormat.Png, ["cap-20261111.png", "cap-20261111-2.png"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("snap-{day}")]
    [InlineData("snap-{date:00}")]
    [InlineData("a/b-{n}")]
    [InlineData("a:b")]
    [InlineData("snap{")]
    [InlineData("snap.")]
    public void BadTemplatesAreExplained(string template)
    {
        Assert.NotNull(FileNamer.Validate(template));
        Assert.Throws<ArgumentException>(() => FileNamer.NextName(template, Nov11, 1, ImageFormat.Png, []));
    }

    [Theory]
    [InlineData("png", ImageFormat.Png, ".png")]
    [InlineData("JPEG", ImageFormat.Jpeg, ".jpg")]
    [InlineData(".jpg", ImageFormat.Jpeg, ".jpg")]
    [InlineData("webp", ImageFormat.Webp, ".webp")]
    public void Formats(string text, ImageFormat f, string ext)
    {
        Assert.True(ImageFormats.TryParse(text, out var parsed));
        Assert.Equal(f, parsed);
        Assert.Equal(ext, parsed.Extension());
        Assert.False(ImageFormats.TryParse("bmp", out _));
    }
}
