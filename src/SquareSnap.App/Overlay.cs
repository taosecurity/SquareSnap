using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SquareSnap.Capture;
using SquareSnap.Core.Geometry;
using Display = SquareSnap.Core.Geometry.Display;
using PhysPoint = SquareSnap.Core.Geometry.PixelPoint;
using PhysRect = SquareSnap.Core.Geometry.PixelRect;

namespace SquareSnap.App;

/// <summary>Shows the frozen screen on every monitor and returns the square the user picks (null when cancelled).</summary>
internal static class Overlay
{
    public static Task<PhysRect?> PickAsync(AppSession s, MonitorLayout layout, ScreenImage screen, CaptureRequest req)
    {
        var done = new TaskCompletionSource<PhysRect?>();
        var windows = new List<OverlayWindow>();

        void Finish(OverlayController c)
        {
            if (done.Task.IsCompleted) return;
            done.SetResult(c.State == OverlayState.Done ? c.Selection : null);
            foreach (var w in windows) w.Close();
        }

        OverlayWindow Make(Display display) =>
            new(display, screen.Crop(display.Bounds), new OverlayController(display.Bounds, req.Mode, req.FineTune, req.FixedSize), s, Finish);

        if (NativeWayland(s))
        {
            // Native Wayland doesn't let a program place its windows, and Avalonia 12.1.4 goes full screen on the
            // output the compositor picks (KWin: the active one, under the mouse). So one overlay, on that output.
            var first = layout.Primary;
            var window = Make(first);
            windows.Add(window);
            window.Opened += async (_, _) =>
            {
                await Task.Delay(200); // the compositor reports which output the surface entered
                var on = window.Screens.ScreenFromWindow(window);
                var target = on == null ? null
                    : layout.Monitors.FirstOrDefault(m => m.WindowPosition == new PhysPoint(on.Bounds.X, on.Bounds.Y));
                s.Log.Info($"Overlay (native Wayland, one window): the compositor put it on {on?.DisplayName ?? "an unknown output"}" +
                           $"{(on == null ? "" : $" at ({on.Bounds.X},{on.Bounds.Y})")}; showing {(target ?? first).Name}" +
                           (layout.Monitors.Count > 1 ? $". To capture another of the {layout.Monitors.Count} monitors, move the mouse there and start the capture again." : "."));
                if (target != null && target != first && !done.Task.IsCompleted)
                    window.Retarget(target, screen.Crop(target.Bounds),
                        new OverlayController(target.Bounds, req.Mode, req.FineTune, req.FixedSize));
            };
        }
        else
        {
            foreach (var display in layout.Monitors) windows.Add(Make(display));
        }
        // An overlay closed by anything but Finish (Alt+F4, the compositor or window manager) ends the pick as
        // cancelled and closes the others; before 2.0.1 the pick never finished and SquareSnap stayed busy.
        foreach (var w in windows)
            w.Closed += (_, _) =>
            {
                if (!done.TrySetResult(null)) return;
                s.Log.Info("An overlay window was closed from outside SquareSnap (Alt+F4 or the window manager); capture cancelled.");
                foreach (var other in windows) if (other != w) other.Close();
            };
        foreach (var w in windows) w.Show();

        // Start on the monitor under the mouse so the keyboard works there straight away.
        windows.FirstOrDefault(w => w.IsPointerOver)?.Activate();
        return done.Task;
    }

    /// <summary>True when the windows are native Wayland (Avalonia.Wayland), not X11 or XWayland.</summary>
    private static bool NativeWayland(AppSession s) =>
        s.ScreensSource?.PlatformImpl?.GetType().FullName?.StartsWith("Avalonia.Wayland", StringComparison.Ordinal) == true;

    /// <summary>Copies BGRA pixels into an Avalonia bitmap.</summary>
    public static Bitmap ToBitmap(ScreenImage image)
    {
        var handle = GCHandle.Alloc(image.Bgra, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, handle.AddrOfPinnedObject(),
                new Avalonia.PixelSize(image.Width, image.Height), new Vector(96, 96), image.Stride);
        }
        finally
        {
            handle.Free();
        }
    }
}

/// <summary>A borderless, topmost window covering one monitor exactly.</summary>
internal sealed class OverlayWindow : Window
{
    public OverlayWindow(Display display, ScreenImage slice, OverlayController controller, AppSession s, Action<OverlayController> onFinished)
    {
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Linux: WindowPosition is in the window system's own coordinates (X11 places the window there; native
        // Wayland ignores it). Windows: the monitor's real pixels.
        var at = display.WindowPosition ?? new PhysPoint(display.Bounds.X, display.Bounds.Y);
        Position = new Avalonia.PixelPoint(at.X, at.Y);
        Width = display.Bounds.Width / display.Scale;
        Height = display.Bounds.Height / display.Scale;
        Background = Brushes.Black;
        Cursor = new Cursor(StandardCursorType.Cross);
        Title = "SquareSnap overlay";

        _display = display;
        _log = s.Log;
        _session = s;
        _onFinished = onFinished;
        _surface = NewSurface(display, slice, controller);

        Opened += (_, _) =>
        {
            Fit("opened");
            var surface = _surface;
            if (display.FixedScale)
                // Wayland reports the window's real scale a moment after it opens (100% first, then 125% or 200% in
                // beta 13's test), so judge 1:1 once it has settled, and on the monitor the window ended up on.
                DispatcherTimer.RunOnce(() =>
                {
                    if (!IsVisible) return;
                    s.Log.Info($"Overlay on {_display.Name}: window render scale {RenderScaling * 100:0}%, picture {_display.Scale:0.###} image px per unit" +
                               (Math.Abs(RenderScaling - _display.Scale) > 0.01 ? " (not 1:1 on screen; the capture keeps the screenshot's resolution)." : " (1:1)."));
                }, TimeSpan.FromMilliseconds(500));
            else if (Math.Abs(RenderScaling - display.Scale) > 0.01)
                s.Log.Debug($"Monitor {display.Name}: Windows reports {display.Scale * 100:0}% scaling; the overlay renders at {RenderScaling * 100:0}% right after opening (harmless: the picture is drawn 1:1).");
            surface.Focus();
            // Keep checking while the overlay is open: Avalonia's idea of this window's size and scaling can go
            // stale with mixed scaling (betas 3-5), at opening or later (beta 5: on later captures, when the mouse
            // arrived). The check is cheap; it only re-fits when something is actually off.
            Dispatcher.UIThread.Post(() => Verify("settled"), DispatcherPriority.Background);
            _watch = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Verify("watch"));
            _watch.Start();
        };
        ScalingChanged += (_, _) =>
        {
            _log.Debug($"Overlay on {display.Name}: render scale now {RenderScaling}, client {ClientSize}.");
            if (IsVisible) Fit("scaling changed");
        };
        Closed += (_, _) =>
        {
            _watch?.Stop();
            _surface.Dispose();
        };
    }

    private SelectionSurface NewSurface(Display display, ScreenImage slice, OverlayController controller)
    {
        var surface = new SelectionSurface(display, slice, controller, _session, () => _onFinished(controller));
        surface.PointerArrived += () => Verify("mouse arrived");
        Content = surface;
        return surface;
    }

    /// <summary>
    /// Native Wayland: shows another monitor's picture in this window, once the compositor has said which output
    /// it put the window on (the program can't choose).
    /// </summary>
    public void Retarget(Display display, ScreenImage slice, OverlayController controller)
    {
        var old = _surface;
        _display = display;
        _surface = NewSurface(display, slice, controller);
        old.Dispose();
        _surface.Focus();
    }

    private readonly AppSession _session;
    private readonly Action<OverlayController> _onFinished;

    private DispatcherTimer? _watch;
    private int _refits;

    private Display _display;
    private SelectionSurface _surface;
    private readonly SquareSnap.Core.Logging.Logger _log;

    /// <summary>
    /// Makes the window cover its monitor exactly at the window's current scaling. Beta 4 placed the Win32 window
    /// correctly, but Avalonia kept the size it had worked out on the 150% main monitor (1706.67 x 960 logical
    /// on a 2560 x 1440 monitor at 100%), so it drew into the top-left two-thirds only. Setting Width/Height at the
    /// current scaling tells Avalonia the right size; the real-pixel placement then pins the Win32 window.
    /// </summary>
    private bool _fitting;

    private void Fit(string why)
    {
        // Placing the window can itself change its scaling (ScalingChanged -> Fit); finish this pass first,
        // then pick up the new scale.
        if (_fitting)
        {
            Dispatcher.UIThread.Post(() => Fit(why + ", deferred"), DispatcherPriority.Background);
            return;
        }
        _fitting = true;
        try { FitNow(why); }
        finally { _fitting = false; }
    }

    private void FitNow(string why)
    {
        double scale = _display.FixedScale ? _display.Scale : RenderScaling;
        Width = _display.Bounds.Width / scale;
        Height = _display.Bounds.Height / scale;
        if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
            Capture.Windows.WindowsPlacement.CoverMonitor(handle.Handle, _display.Bounds, _display.Name, _log);
        else
            WindowState = WindowState.FullScreen;
        _surface.InvalidateVisual();
        _log.Debug($"Overlay on {_display.Name} ({why}): position {Position}, client {ClientSize}, render scale {RenderScaling}.");
    }

    /// <summary>Re-fits the window if Avalonia's size or the real Win32 rectangle no longer match the monitor.</summary>
    private void Verify(string when)
    {
        // Linux: full screen is the compositor's job, and the image-pixel bounds aren't window units.
        if (!OperatingSystem.IsWindows()) return;
        if (!IsVisible || _fitting) return;
        bool avaloniaOk = MonitorLayout.Covers(ClientSize.Width, ClientSize.Height, RenderScaling, _display.Bounds);
        PhysRect? win32 = null;
        if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
            win32 = Capture.Windows.WindowsPlacement.Actual(handle.Handle);
        bool win32Ok = win32 == null || win32 == _display.Bounds;
        if (avaloniaOk && win32Ok)
        {
            _sameFailures = 0;
            _lastBad = null;
            return;
        }
        if (_gaveUp) return;

        // A re-fit that changes nothing won't help the next time either (beta 6: Windows kept the window at 144 DPI
        // on the 100% monitor). After three identical failures, stop and say so once instead of looping.
        string state = $"client {ClientSize} at scale {RenderScaling}" + (win32 != null ? $", window {win32}" : "");
        _sameFailures = state == _lastBad ? _sameFailures + 1 : 1;
        _lastBad = state;
        if (_sameFailures > 3)
        {
            _gaveUp = true;
            _log.Warning($"Overlay on {_display.Name}: doesn't cover the monitor {_display.Bounds} ({state}) and re-fitting doesn't change it. " +
                         "Part of this monitor may show the live screen during the capture; please send this log.");
            return;
        }

        _refits++;
        _log.Info($"Overlay on {_display.Name} ({when}): {state}, monitor {_display.Bounds}; fitting again (#{_refits}).");
        Fit(when);
    }

    private string? _lastBad;
    private int _sameFailures;
    private bool _gaveUp;
}

/// <summary>Draws the frozen screen, the dimming, the square, its size, hints and a magnifier, and feeds input to the controller.</summary>
internal sealed class SelectionSurface : Control, IDisposable
{
    private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
    private static readonly IBrush LabelBack = new SolidColorBrush(Color.FromArgb(200, 20, 20, 24));
    private static readonly Typeface LabelFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private const int LoupePixels = 15;     // physical pixels shown in the magnifier
    private const double LoupeSize = 135;   // logical size of the magnifier

    private readonly Display _display;
    private readonly Bitmap _bitmap;
    private readonly OverlayController _c;
    private readonly AppSession _s;
    private readonly Action _finished;
    private bool _pointerInside;

    /// <summary>
    /// Logical-to-physical scale, read live from the window every time (never cached): drawing and mouse mapping
    /// then always use the same scale Avalonia itself is using, even when that scale is stale.
    /// </summary>
    public double Scale => _display.FixedScale ? _display.Scale : TopLevel.GetTopLevel(this)?.RenderScaling ?? _display.Scale;

    /// <summary>Raised when the mouse comes onto this monitor's overlay (beta 5's zoom started then).</summary>
    public event Action? PointerArrived;

    public SelectionSurface(Display display, ScreenImage slice, OverlayController controller, AppSession s, Action finished)
    {
        _display = display;
        _bitmap = Overlay.ToBitmap(slice);
        _c = controller;
        _s = s;
        _finished = finished;
        Focusable = true;
        ClipToBounds = true;
    }

    public void Dispose() => _bitmap.Dispose();

    private Display Mapped => _display with { Scale = Scale };

    private PhysPoint ToPhys(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        return MonitorLayout.ToPhysical(Mapped, p.X, p.Y);
    }

    private Rect ToLogical(PhysRect r)
    {
        var (x, y, w, h) = MonitorLayout.ToLogical(Mapped, r);
        return new Rect(x, y, w, h);
    }

    // ---------------- input ----------------

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        _pointerInside = true;
        if (TopLevel.GetTopLevel(this) is Window w && !w.IsActive) w.Activate();
        Focus();
        PointerArrived?.Invoke();
        base.OnPointerEntered(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _pointerInside = false;
        InvalidateVisual();
        base.OnPointerExited(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        _c.PointerMoved(ToPhys(e));
        After();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed)
        {
            _c.LeftPressed(ToPhys(e));
            e.Pointer.Capture(this);
        }
        else if (props.IsRightButtonPressed)
        {
            _c.RightPressed();
        }
        e.Handled = true;
        After();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            _c.LeftReleased(ToPhys(e));
            e.Pointer.Capture(null);
        }
        e.Handled = true;
        After();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        int notches = (int)Math.Round(e.Delta.Y);
        if (notches == 0 && e.Delta.Y != 0) notches = Math.Sign(e.Delta.Y);
        _c.Wheel(notches, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
        After();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.Left => OverlayKey.Left,
            Key.Right => OverlayKey.Right,
            Key.Up => OverlayKey.Up,
            Key.Down => OverlayKey.Down,
            Key.Enter or Key.Space => OverlayKey.Enter,
            Key.Escape => OverlayKey.Escape,
            _ => OverlayKey.Other,
        };
        _c.Key(key, e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Control));
        e.Handled = true;
        After();
    }

    private void After()
    {
        InvalidateVisual();
        if (_c.IsFinished) _finished();
    }

    // ---------------- drawing ----------------

    public override void Render(DrawingContext ctx)
    {
        // Draw the frozen screen one screenshot pixel per screen pixel, from the window's top-left corner (which
        // the real-pixel placement pins to the monitor's corner). Sizing it to Bounds instead made it shrink
        // (betas 3-4) or zoom (beta 5) whenever Avalonia's size or scale for the window was stale.
        double scale = Scale;
        var picture = new Rect(0, 0, _bitmap.PixelSize.Width / scale, _bitmap.PixelSize.Height / scale);
        var size = new Size(Math.Max(Bounds.Width, picture.Width), Math.Max(Bounds.Height, picture.Height));
        var whole = new Rect(size);
        ctx.FillRectangle(Brushes.Black, whole);
        ctx.DrawImage(_bitmap, new Rect(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height), picture);

        var sel = _c.Selection;
        bool hasSel = !sel.IsEmpty && _display.Bounds.Contains(sel);
        if (!hasSel)
        {
            ctx.FillRectangle(Dim, whole);
        }
        else
        {
            var r = ToLogical(sel);
            ctx.FillRectangle(Dim, new Rect(0, 0, size.Width, r.Top));
            ctx.FillRectangle(Dim, new Rect(0, r.Bottom, size.Width, Math.Max(0, size.Height - r.Bottom)));
            ctx.FillRectangle(Dim, new Rect(0, r.Top, r.Left, r.Height));
            ctx.FillRectangle(Dim, new Rect(r.Right, r.Top, Math.Max(0, size.Width - r.Right), r.Height));

            // Border drawn just outside the square, so every pixel inside is exactly what gets saved.
            double px = 1 / Scale;
            ctx.DrawRectangle(new Pen(Brushes.Black, px), r.Inflate(2.5 * px));
            ctx.DrawRectangle(new Pen(Brushes.White, 2 * px), r.Inflate(1 * px));
            DrawSizeLabel(ctx, r, sel, size);
        }

        DrawHint(ctx, size);
        if (_pointerInside && !_c.IsFinished) DrawLoupe(ctx, size);
    }

    private void DrawSizeLabel(DrawingContext ctx, Rect r, PhysRect sel, Size size)
    {
        string text = $"{sel.Width} × {sel.Height}";
        var ft = Text(text, 13);
        var box = new Rect(0, 0, ft.Width + 12, ft.Height + 6);
        double x = Math.Clamp(r.Right - box.Width, 4, size.Width - box.Width - 4);
        double y = r.Bottom + 6 + box.Height <= size.Height ? r.Bottom + 6 : r.Top - box.Height - 6;
        if (y < 0) y = r.Top + 6; // tall square: put it inside
        box = box.WithX(x).WithY(y);
        ctx.FillRectangle(LabelBack, box, 4);
        ctx.DrawText(ft, new Point(box.X + 6, box.Y + 3));
    }

    private void DrawHint(DrawingContext ctx, Size size)
    {
        string text = _c.State switch
        {
            OverlayState.Adjusting => "Arrows move · Shift = 10 px · Ctrl+arrows or wheel resize · Enter captures · Esc cancels",
            OverlayState.Dragging => "Release to " + (_c.FineTune ? "fine-tune" : "capture") + " · Esc cancels",
            _ when _c.Mode == OverlayMode.Fixed => $"Click to capture {_c.FixedSize} × {_c.FixedSize} · wheel resizes · arrows nudge · Esc cancels",
            _ => "Drag a square · right-click or Esc cancels",
        };
        var ft = Text(text, 14);
        var box = new Rect((size.Width - ft.Width) / 2 - 12, 16, ft.Width + 24, ft.Height + 12);
        ctx.FillRectangle(LabelBack, box, 6);
        ctx.DrawText(ft, new Point(box.X + 12, box.Y + 6));
    }

    private void DrawLoupe(DrawingContext ctx, Size size)
    {
        var p = _c.Pointer;
        if (!_display.Bounds.Contains(p)) return;
        int half = LoupePixels / 2;
        var src = new Rect(p.X - _display.Bounds.X - half, p.Y - _display.Bounds.Y - half, LoupePixels, LoupePixels);

        var (lx, ly, _, _) = MonitorLayout.ToLogical(Mapped, new PhysRect(p.X, p.Y, 1, 1));
        double x = lx + 24, y = ly + 24;
        if (x + LoupeSize > size.Width) x = lx - 24 - LoupeSize;
        if (y + LoupeSize + 24 > size.Height) y = ly - 24 - LoupeSize - 22;
        var dst = new Rect(x, y, LoupeSize, LoupeSize);

        ctx.FillRectangle(Brushes.Black, dst.Inflate(2));
        using (ctx.PushClip(dst))
        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            ctx.DrawImage(_bitmap, src, dst);

        double cell = LoupeSize / LoupePixels;
        var center = new Rect(dst.X + half * cell, dst.Y + half * cell, cell, cell);
        ctx.DrawRectangle(new Pen(Brushes.White, 1), center);
        ctx.DrawRectangle(new Pen(Brushes.White, 1), dst);

        var label = Text($"{p.X}, {p.Y}", 11);
        var box = new Rect(dst.X, dst.Bottom + 4, Math.Max(LoupeSize, label.Width + 10), label.Height + 4);
        ctx.FillRectangle(LabelBack, box, 3);
        ctx.DrawText(label, new Point(box.X + 5, box.Y + 2));
    }

    private static FormattedText Text(string s, double size) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, size, Brushes.White);
}
