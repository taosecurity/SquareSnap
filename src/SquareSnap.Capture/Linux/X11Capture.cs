using System.Runtime.InteropServices;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Logging;

namespace SquareSnap.Capture.Linux;

/// <summary>
/// X11 capture: XGetImage on the root window, which holds the whole X screen (every monitor) on a real X11 session.
/// Untested on a real Xorg desktop (the user has none, 2026-11-11); used under WSLg with --wslg, where it sees WSLg's
/// X server. On a Wayland desktop it isn't tried: XWayland's root window doesn't hold the Wayland windows.
/// </summary>
internal static unsafe class X11Capture
{
    private const string Lib = "libX11.so.6";
    private const int ZPixmap = 2;

    [DllImport(Lib)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(Lib)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(Lib)] private static extern int XDefaultScreen(IntPtr display);
    [DllImport(Lib)] private static extern IntPtr XRootWindow(IntPtr display, int screen);
    [DllImport(Lib)] private static extern int XDisplayWidth(IntPtr display, int screen);
    [DllImport(Lib)] private static extern int XDisplayHeight(IntPtr display, int screen);
    [DllImport(Lib)] private static extern XImage* XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, nuint planeMask, int format);

    /// <summary>Xlib's XImage (64-bit layout). Only the fields read here matter; the functions table follows obdata.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct XImage
    {
        public int width, height, xoffset, format;
        public IntPtr data;
        public int byte_order, bitmap_unit, bitmap_bit_order, bitmap_pad, depth, bytes_per_line, bits_per_pixel;
        public nuint red_mask, green_mask, blue_mask;
        public IntPtr obdata;
        public IntPtr create_image;
        public delegate* unmanaged<XImage*, int> destroy_image;
        public IntPtr get_pixel, put_pixel, sub_image, add_pixel;
    }

    public static ScreenImage CaptureRoot(Logger log)
    {
        IntPtr display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            throw new CaptureException($"couldn't open the X display '{Environment.GetEnvironmentVariable("DISPLAY")}'");
        try
        {
            int screen = XDefaultScreen(display);
            int w = XDisplayWidth(display, screen), h = XDisplayHeight(display, screen);
            log.Info($"X11: root window {w}x{h} on display {Environment.GetEnvironmentVariable("DISPLAY")}.");
            XImage* img = XGetImage(display, XRootWindow(display, screen), 0, 0, (uint)w, (uint)h, nuint.MaxValue, ZPixmap);
            if (img == null) throw new CaptureException("XGetImage returned nothing for the root window");
            try
            {
                if (img->bits_per_pixel != 32 || img->byte_order != 0)
                    throw new CaptureException($"the X screen uses {img->bits_per_pixel}-bit pixels (byte order {img->byte_order}); only 32-bit little-endian is supported");
                var bgra = new byte[w * h * 4];
                byte* src = (byte*)img->data;
                fixed (byte* dst = bgra)
                {
                    for (int y = 0; y < h; y++)
                        Buffer.MemoryCopy(src + (long)y * img->bytes_per_line, dst + (long)y * w * 4, (long)w * 4, (long)w * 4);
                }
                for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255; // XGetImage leaves the X byte undefined
                return new ScreenImage(w, h, new PixelRect(0, 0, w, h), bgra);
            }
            finally
            {
                if (img->destroy_image != null) img->destroy_image(img);
            }
        }
        finally
        {
            XCloseDisplay(display);
        }
    }
}
