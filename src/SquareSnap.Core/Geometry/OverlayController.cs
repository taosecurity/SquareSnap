namespace SquareSnap.Core.Geometry;

public enum OverlayMode
{
    /// <summary>Drag a square with the mouse.</summary>
    Drag,
    /// <summary>A square of a set size follows the mouse; click to place it.</summary>
    Fixed,
}

public enum OverlayState
{
    /// <summary>Waiting for the first press (drag mode) or following the mouse (fixed mode).</summary>
    Waiting,
    Dragging,
    /// <summary>A square is placed and can be fine-tuned with the keyboard or wheel; Enter confirms.</summary>
    Adjusting,
    Done,
    Cancelled,
}

public enum OverlayKey
{
    Left, Right, Up, Down, Enter, Escape, Other,
}

/// <summary>
/// The overlay's behaviour, independent of any UI toolkit. The overlay window feeds it pointer, key and wheel events in
/// physical pixels and redraws from <see cref="Selection"/>. One controller covers one monitor.
/// <list type="bullet">
/// <item>Drag mode: press, drag, release. Without fine-tune, release captures at once (the 1.0 behaviour);
/// with fine-tune, the square stays for the arrow keys and Enter.</item>
/// <item>Fixed mode: the square follows the mouse, the wheel changes its size, a click places it.</item>
/// <item>Keys while a square exists: arrows move 1 px (Shift: 10), Ctrl+Up/Right grow and Ctrl+Down/Left shrink
/// (Shift: 10), Enter captures, Escape or a right click cancels.</item>
/// </list>
/// </summary>
public sealed class OverlayController
{
    public const int BigStep = 10;
    public const int WheelStepFixed = 16;

    private PixelPoint _dragStart;

    public PixelRect Bounds { get; }
    public OverlayMode Mode { get; }
    public bool FineTune { get; }
    public OverlayState State { get; private set; } = OverlayState.Waiting;
    public int FixedSize { get; private set; }

    /// <summary>The square to draw (empty when there's nothing to show yet).</summary>
    public PixelRect Selection { get; private set; }

    /// <summary>Where the mouse is, for the magnifier.</summary>
    public PixelPoint Pointer { get; private set; }

    public bool IsFinished => State is OverlayState.Done or OverlayState.Cancelled;

    public OverlayController(PixelRect bounds, OverlayMode mode, bool fineTune, int fixedSize = 512)
    {
        if (bounds.IsEmpty) throw new ArgumentException("The overlay bounds are empty.", nameof(bounds));
        Bounds = bounds;
        Mode = mode;
        FineTune = fineTune;
        FixedSize = Math.Clamp(fixedSize, SquareSelection.MinimumSize, Math.Min(bounds.Width, bounds.Height));
        Pointer = bounds.Center;
        if (mode == OverlayMode.Fixed) Selection = SquareSelection.PlaceFixed(Pointer, FixedSize, Bounds);
    }

    public void PointerMoved(PixelPoint p)
    {
        if (IsFinished) return;
        Pointer = p;
        switch (State)
        {
            case OverlayState.Dragging:
                Selection = SquareSelection.FromDrag(_dragStart, p, Bounds);
                break;
            case OverlayState.Waiting when Mode == OverlayMode.Fixed:
                Selection = SquareSelection.PlaceFixed(p, FixedSize, Bounds);
                break;
        }
    }

    public void LeftPressed(PixelPoint p)
    {
        if (IsFinished) return;
        Pointer = p;
        if (Mode == OverlayMode.Fixed)
        {
            // In fixed mode a click places the square wherever it is now.
            Selection = SquareSelection.PlaceFixed(p, FixedSize, Bounds);
            State = FineTune ? OverlayState.Adjusting : OverlayState.Done;
            return;
        }
        // A new press always starts a new drag, even while adjusting an earlier square.
        _dragStart = p;
        Selection = SquareSelection.FromDrag(p, p, Bounds);
        State = OverlayState.Dragging;
    }

    public void LeftReleased(PixelPoint p)
    {
        if (State != OverlayState.Dragging) return;
        Pointer = p;
        Selection = SquareSelection.FromDrag(_dragStart, p, Bounds);
        if (!SquareSelection.IsUsable(Selection))
        {
            // A click without a drag: forget it and wait for a real drag.
            Selection = default;
            State = OverlayState.Waiting;
            return;
        }
        State = FineTune ? OverlayState.Adjusting : OverlayState.Done;
    }

    public void RightPressed()
    {
        if (!IsFinished) State = OverlayState.Cancelled;
    }

    /// <summary>Wheel notches (positive = away from the user). Resizes the square.</summary>
    public void Wheel(int notches, bool shift)
    {
        if (IsFinished || notches == 0) return;
        if (Mode == OverlayMode.Fixed && State == OverlayState.Waiting)
        {
            int step = shift ? 1 : WheelStepFixed;
            FixedSize = Math.Clamp(FixedSize + notches * step, SquareSelection.MinimumSize, Math.Min(Bounds.Width, Bounds.Height));
            Selection = SquareSelection.PlaceFixed(Pointer, FixedSize, Bounds);
        }
        else if (State == OverlayState.Adjusting)
        {
            Selection = SquareSelection.Resize(Selection, notches * (shift ? 1 : BigStep), Bounds);
        }
    }

    public void Key(OverlayKey key, bool shift, bool ctrl)
    {
        if (IsFinished) return;
        if (key == OverlayKey.Escape) { State = OverlayState.Cancelled; return; }

        bool hasSquare = State == OverlayState.Adjusting || (Mode == OverlayMode.Fixed && State == OverlayState.Waiting);
        if (!hasSquare) return;

        if (key == OverlayKey.Enter)
        {
            if (SquareSelection.IsUsable(Selection)) State = OverlayState.Done;
            return;
        }

        int step = shift ? BigStep : 1;
        if (ctrl)
        {
            int delta = key switch { OverlayKey.Up or OverlayKey.Right => step, OverlayKey.Down or OverlayKey.Left => -step, _ => 0 };
            if (delta == 0) return;
            Selection = SquareSelection.Resize(Selection, delta, Bounds);
        }
        else
        {
            (int dx, int dy) = key switch
            {
                OverlayKey.Left => (-step, 0),
                OverlayKey.Right => (step, 0),
                OverlayKey.Up => (0, -step),
                OverlayKey.Down => (0, step),
                _ => (0, 0),
            };
            if (dx == 0 && dy == 0) return;
            Selection = SquareSelection.Move(Selection, dx, dy, Bounds);
        }

        // Keyboard changes in fixed mode lock the square in place (the mouse no longer drags it along).
        if (Mode == OverlayMode.Fixed) State = OverlayState.Adjusting;
        FixedSize = Selection.Width;
    }
}
