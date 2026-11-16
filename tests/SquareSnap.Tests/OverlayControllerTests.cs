using SquareSnap.Core.Cli;
using SquareSnap.Core.Geometry;

namespace SquareSnap.Tests;

public class OverlayControllerTests
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void DragAndRelease_CapturesAtOnce()
    {
        var c = new OverlayController(Screen, OverlayMode.Drag, fineTune: false);
        c.LeftPressed(new(100, 100));
        Assert.Equal(OverlayState.Dragging, c.State);
        c.PointerMoved(new(300, 150));
        Assert.Equal(new PixelRect(100, 100, 200, 200), c.Selection);
        c.LeftReleased(new(300, 150));
        Assert.Equal(OverlayState.Done, c.State);
        Assert.Equal(new PixelRect(100, 100, 200, 200), c.Selection);
    }

    [Fact]
    public void ClickWithoutDrag_KeepsWaiting()
    {
        var c = new OverlayController(Screen, OverlayMode.Drag, fineTune: false);
        c.LeftPressed(new(100, 100));
        c.LeftReleased(new(101, 101));
        Assert.Equal(OverlayState.Waiting, c.State);
        Assert.True(c.Selection.IsEmpty);
    }

    [Fact]
    public void FineTune_ArrowsMoveAndResize_EnterConfirms()
    {
        var c = new OverlayController(Screen, OverlayMode.Drag, fineTune: true);
        c.LeftPressed(new(100, 100));
        c.LeftReleased(new(200, 200));
        Assert.Equal(OverlayState.Adjusting, c.State);

        c.Key(OverlayKey.Right, shift: false, ctrl: false);
        c.Key(OverlayKey.Down, shift: true, ctrl: false);
        Assert.Equal(new PixelRect(101, 110, 100, 100), c.Selection);

        c.Key(OverlayKey.Up, shift: false, ctrl: true);   // grow 1
        c.Key(OverlayKey.Left, shift: true, ctrl: true);  // shrink 10
        Assert.Equal(new PixelRect(101, 110, 91, 91), c.Selection);

        c.Wheel(2, shift: false); // +20
        Assert.Equal(111, c.Selection.Width);

        c.Key(OverlayKey.Enter, false, false);
        Assert.Equal(OverlayState.Done, c.State);
    }

    [Fact]
    public void NewPressWhileAdjusting_StartsOver()
    {
        var c = new OverlayController(Screen, OverlayMode.Drag, fineTune: true);
        c.LeftPressed(new(100, 100));
        c.LeftReleased(new(200, 200));
        c.LeftPressed(new(500, 500));
        Assert.Equal(OverlayState.Dragging, c.State);
        c.LeftReleased(new(450, 400));
        Assert.Equal(new PixelRect(400, 400, 100, 100), c.Selection);
    }

    [Fact]
    public void EscapeAndRightClickCancel()
    {
        var a = new OverlayController(Screen, OverlayMode.Drag, false);
        a.LeftPressed(new(10, 10));
        a.Key(OverlayKey.Escape, false, false);
        Assert.Equal(OverlayState.Cancelled, a.State);

        var b = new OverlayController(Screen, OverlayMode.Drag, false);
        b.RightPressed();
        Assert.Equal(OverlayState.Cancelled, b.State);
        b.LeftPressed(new(10, 10)); // ignored once finished
        Assert.Equal(OverlayState.Cancelled, b.State);
    }

    [Fact]
    public void KeysBeforeAnyDragDoNothing()
    {
        var c = new OverlayController(Screen, OverlayMode.Drag, true);
        c.Key(OverlayKey.Enter, false, false);
        c.Key(OverlayKey.Left, false, false);
        Assert.Equal(OverlayState.Waiting, c.State);
        Assert.True(c.Selection.IsEmpty);
    }

    [Fact]
    public void FixedMode_FollowsMouse_WheelResizes_ClickPlaces()
    {
        var c = new OverlayController(Screen, OverlayMode.Fixed, fineTune: false, fixedSize: 512);
        c.PointerMoved(new(960, 540));
        Assert.Equal(new PixelRect(704, 284, 512, 512), c.Selection);
        c.Wheel(1, shift: false);
        Assert.Equal(528, c.Selection.Width);
        c.Wheel(-1, shift: true);
        Assert.Equal(527, c.Selection.Width);
        c.PointerMoved(new(0, 0));
        Assert.Equal(new PixelRect(0, 0, 527, 527), c.Selection);
        c.LeftPressed(new(1919, 1079));
        Assert.Equal(OverlayState.Done, c.State);
        Assert.Equal(new PixelRect(1393, 553, 527, 527), c.Selection);
    }

    [Fact]
    public void FixedMode_KeyboardLocksSquare()
    {
        var c = new OverlayController(Screen, OverlayMode.Fixed, fineTune: false, fixedSize: 100);
        c.PointerMoved(new(500, 500));
        c.Key(OverlayKey.Left, false, false);
        Assert.Equal(OverlayState.Adjusting, c.State);
        c.PointerMoved(new(10, 10)); // no longer follows
        Assert.Equal(new PixelRect(449, 450, 100, 100), c.Selection);
        c.Key(OverlayKey.Enter, false, false);
        Assert.Equal(OverlayState.Done, c.State);
    }

    [Fact]
    public void FixedSizeIsCappedByMonitor()
    {
        var c = new OverlayController(new PixelRect(0, 0, 800, 600), OverlayMode.Fixed, false, fixedSize: 4000);
        Assert.Equal(600, c.FixedSize);
        Assert.Equal(600, c.Selection.Width);
    }
}

public class HotkeySpecTests
{
    [Theory]
    [InlineData("Ctrl+Alt+S", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "S")]
    [InlineData("ctrl + shift + q", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Q")]
    [InlineData("Win+Shift+5", HotkeyModifiers.Win | HotkeyModifiers.Shift, "5")]
    [InlineData("Alt+F9", HotkeyModifiers.Alt, "F9")]
    [InlineData("F12", HotkeyModifiers.None, "F12")]
    [InlineData("Ctrl+PrtSc", HotkeyModifiers.Ctrl, "PrintScreen")]
    public void Parses(string text, HotkeyModifiers mods, string key)
    {
        Assert.True(HotkeySpec.TryParse(text, out var spec, out string? err), err);
        Assert.Equal(mods, spec!.Modifiers);
        Assert.Equal(key, spec.Key);
    }

    [Fact]
    public void ToStringIsCanonical()
    {
        Assert.True(HotkeySpec.TryParse("shift+alt+ctrl+s", out var spec, out _));
        Assert.Equal("Ctrl+Alt+Shift+S", spec!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("off")]
    public void EmptyMeansNone(string text)
    {
        Assert.True(HotkeySpec.TryParse(text, out var spec, out _));
        Assert.Null(spec);
    }

    [Theory]
    [InlineData("S")]
    [InlineData("Shift+S")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+S+T")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl++S")]
    [InlineData("Ctrl+F25")]
    public void RejectsWithMessage(string text)
    {
        Assert.False(HotkeySpec.TryParse(text, out _, out string? err));
        Assert.False(string.IsNullOrWhiteSpace(err));
    }
}

public class RegionOptionTests
{
    [Fact]
    public void ParsesSquareRegion()
    {
        var r = CommandLine.Parse(["capture", "--region", "-1920,100,512,512", "--fine-tune"]);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(new PixelRect(-1920, 100, 512, 512), r.Options!.Region);
        Assert.True(r.Options.FineTune);
    }

    [Theory]
    [InlineData("capture", "--region", "0,0,100,90")]
    [InlineData("capture", "--region", "0,0,2,2")]
    [InlineData("capture", "--region", "a,b,c,d")]
    [InlineData("capture", "--region", "0,0,100,100", "--last")]
    [InlineData("--region", "0,0,100,100")]
    public void Rejects(params string[] args) => Assert.False(CommandLine.Parse(args).Ok);
}
