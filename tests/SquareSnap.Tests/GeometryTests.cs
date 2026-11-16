using SquareSnap.Core.Geometry;

namespace SquareSnap.Tests;

public class SquareSelectionTests
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);

    [Theory]
    // start, end, expected square: the longer side wins and the start corner stays put, in all four directions.
    [InlineData(100, 100, 300, 150, 100, 100, 200, 200)] // down-right, wide drag
    [InlineData(100, 100, 150, 400, 100, 100, 300, 300)] // down-right, tall drag
    [InlineData(500, 500, 300, 450, 300, 300, 200, 200)] // up-left
    [InlineData(500, 500, 700, 450, 500, 300, 200, 200)] // up-right
    [InlineData(500, 500, 300, 550, 300, 500, 200, 200)] // down-left
    public void FromDrag_LongerSideAnchoredAtStart(int sx, int sy, int ex, int ey, int x, int y, int w, int h)
    {
        var r = SquareSelection.FromDrag(new(sx, sy), new(ex, ey), Screen);
        Assert.Equal(new PixelRect(x, y, w, h), r);
        Assert.True(r.IsSquare);
    }

    [Fact]
    public void FromDrag_StopsAtMonitorEdge()
    {
        // Dragging far past the bottom edge: the square can only be as tall as the room left below the start.
        var r = SquareSelection.FromDrag(new(1000, 900), new(1900, 5000), Screen);
        Assert.Equal(new PixelRect(1000, 900, 180, 180), r);
        Assert.True(Screen.Contains(r));
    }

    [Fact]
    public void FromDrag_UpLeftStopsAtOrigin()
    {
        var r = SquareSelection.FromDrag(new(50, 300), new(-400, -400), Screen);
        Assert.Equal(new PixelRect(0, 250, 50, 50), r);
    }

    [Fact]
    public void FromDrag_WorksOnMonitorWithNegativeCoordinates()
    {
        // A second monitor to the left of the primary one starts at a negative X.
        var left = new PixelRect(-2560, 0, 2560, 1440);
        var r = SquareSelection.FromDrag(new(-2000, 100), new(-1000, 300), left);
        Assert.Equal(new PixelRect(-2000, 100, 1000, 1000), r);
        Assert.True(left.Contains(r));
    }

    [Fact]
    public void FromDrag_StartOutsideBoundsIsPulledIn()
    {
        var r = SquareSelection.FromDrag(new(-50, -50), new(100, 100), Screen);
        Assert.Equal(0, r.X);
        Assert.Equal(0, r.Y);
        Assert.True(Screen.Contains(r));
    }

    [Fact]
    public void Click_IsNotUsable()
    {
        var r = SquareSelection.FromDrag(new(10, 10), new(12, 11), Screen);
        Assert.False(SquareSelection.IsUsable(r));
        Assert.True(SquareSelection.IsUsable(SquareSelection.FromDrag(new(10, 10), new(14, 10), Screen)));
    }

    [Fact]
    public void Move_ClampsToEdges()
    {
        var sq = new PixelRect(1800, 900, 100, 100);
        Assert.Equal(new PixelRect(1820, 980, 100, 100), SquareSelection.Move(sq, 500, 500, Screen));
        Assert.Equal(new PixelRect(0, 0, 100, 100), SquareSelection.Move(sq, -5000, -5000, Screen));
        Assert.Equal(new PixelRect(1801, 899, 100, 100), SquareSelection.Move(sq, 1, -1, Screen));
    }

    [Fact]
    public void Resize_KeepsCornerAndLimits()
    {
        var sq = new PixelRect(100, 100, 100, 100);
        Assert.Equal(new PixelRect(100, 100, 110, 110), SquareSelection.Resize(sq, 10, Screen));
        Assert.Equal(new PixelRect(100, 100, SquareSelection.MinimumSize, SquareSelection.MinimumSize), SquareSelection.Resize(sq, -1000, Screen));
        // Can't grow past the shorter side of the monitor; slides back to stay inside.
        var big = SquareSelection.Resize(sq, 5000, Screen);
        Assert.Equal(1080, big.Width);
        Assert.True(Screen.Contains(big));
    }

    [Fact]
    public void PlaceFixed_CentresAndClamps()
    {
        Assert.Equal(new PixelRect(704, 284, 512, 512), SquareSelection.PlaceFixed(new(960, 540), 512, Screen));
        Assert.Equal(new PixelRect(0, 0, 512, 512), SquareSelection.PlaceFixed(new(0, 0), 512, Screen));
        Assert.Equal(1080, SquareSelection.PlaceFixed(new(960, 540), 4000, Screen).Width);
    }

    [Fact]
    public void FitInside_KeepsSizeWhenPossible()
    {
        Assert.Equal(new PixelRect(1820, 0, 100, 100), SquareSelection.FitInside(new PixelRect(1900, -20, 100, 100), Screen));
        Assert.Equal(new PixelRect(0, 0, 1080, 1080), SquareSelection.FitInside(new PixelRect(0, 0, 2000, 2000), Screen));
    }
}

public class MonitorLayoutTests
{
    private static readonly Display Primary = new("DISPLAY1", new PixelRect(0, 0, 3840, 2160), 1.5, true);
    private static readonly Display Left = new("DISPLAY2", new PixelRect(-1920, 200, 1920, 1080), 1.0, false);

    [Fact]
    public void VirtualBounds_CoversAllMonitors()
    {
        var layout = new MonitorLayout([Primary, Left]);
        Assert.Equal(new PixelRect(-1920, 0, 5760, 2160), layout.VirtualBounds);
        Assert.Same(Primary, layout.Primary);
    }

    [Fact]
    public void MonitorAt_FindsContainingOrNearest()
    {
        var layout = new MonitorLayout([Primary, Left]);
        Assert.Same(Left, layout.MonitorAt(new(-100, 500)));
        Assert.Same(Primary, layout.MonitorAt(new(100, 500)));
        // (-100, 50) is in the gap above the left monitor: nearest is the left monitor (150 px) vs primary (100 px).
        Assert.Same(Primary, layout.MonitorAt(new(-100, 50)));
    }

    [Fact]
    public void ToPhysical_AppliesScaleAndOffset()
    {
        // 150%: logical 100,200 is physical 150,300 on the primary monitor.
        Assert.Equal(new PixelPoint(150, 300), MonitorLayout.ToPhysical(Primary, 100, 200));
        Assert.Equal(new PixelPoint(-1820, 400), MonitorLayout.ToPhysical(Left, 100, 200));
        // Past the far edge clamps to the last pixel.
        Assert.Equal(new PixelPoint(3839, 2159), MonitorLayout.ToPhysical(Primary, 5000, 5000));
    }

    [Fact]
    public void ToLogical_RoundTrips()
    {
        var (x, y, w, h) = MonitorLayout.ToLogical(Primary, new PixelRect(150, 300, 300, 300));
        Assert.Equal((100.0, 200.0, 200.0, 200.0), (x, y, w, h));
    }

    [Fact]
    public void Covers_UsesTheNumbersFromTheBeta4Log()
    {
        // DISPLAY1 3840x2160 at 150%: client 2560 x 1440 logical covers it.
        Assert.True(MonitorLayout.Covers(2560, 1440, 1.5, new PixelRect(0, 0, 3840, 2160)));
        // DISPLAY2 2560x1440 at 100%: Avalonia kept the client at 1706.67 x 960 (the bug).
        Assert.False(MonitorLayout.Covers(1706.6666666666667, 960, 1.0, new PixelRect(-2560, 0, 2560, 1440)));
        // After the fix it should be 2560 x 1440 at 1.0.
        Assert.True(MonitorLayout.Covers(2560, 1440, 1.0, new PixelRect(-2560, 0, 2560, 1440)));
        // Beta 6 log: Windows kept the window at 144 DPI on the 100% monitor; too big is fine (no endless re-fits).
        Assert.True(MonitorLayout.Covers(2560, 1440, 1.5, new PixelRect(-2560, 0, 2560, 1440)));
        // One pixel short is still covered; ten is not.
        Assert.True(MonitorLayout.Covers(2559, 1440, 1.0, new PixelRect(0, 0, 2560, 1440)));
        Assert.False(MonitorLayout.Covers(2550, 1440, 1.0, new PixelRect(0, 0, 2560, 1440)));
    }

    [Fact]
    public void RejectsEmptyAndBadScale()
    {
        Assert.Throws<ArgumentException>(() => new MonitorLayout([]));
        Assert.Throws<ArgumentException>(() => new MonitorLayout([Primary with { Scale = 0 }]));
    }
}

public class OutputSizeTests
{
    [Theory]
    [InlineData("native", 0)]
    [InlineData("", 0)]
    [InlineData("1080", 1080)]
    [InlineData(" 512px ", 512)]
    public void Parses(string text, int px)
    {
        Assert.True(OutputSize.TryParse(text, out var s, out _));
        Assert.Equal(px, s.Pixels);
    }

    [Theory]
    [InlineData("big")]
    [InlineData("8")]
    [InlineData("99999")]
    public void RejectsWithMessage(string text)
    {
        Assert.False(OutputSize.TryParse(text, out _, out string? err));
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void SideFor()
    {
        Assert.Equal(733, OutputSize.Native.SideFor(733));
        Assert.Equal(1080, new OutputSize(1080).SideFor(733));
    }
}

public class PixelRectTests
{
    [Fact]
    public void ParseRoundTrip()
    {
        var r = new PixelRect(-10, 20, 300, 300);
        Assert.True(PixelRect.TryParse(r.ToString(), out var back));
        Assert.Equal(r, back);
        Assert.False(PixelRect.TryParse("1,2,3", out _));
        Assert.False(PixelRect.TryParse("1,2,0,5", out _));
    }
}
