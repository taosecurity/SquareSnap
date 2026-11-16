using System.Diagnostics;
using System.Runtime.Versioning;
using SkiaSharp;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Logging;
using SquareSnap.Core.Platform;
using Tmds.DBus.Protocol;

namespace SquareSnap.Capture.Linux;

/// <summary>
/// Linux screen capture (step 4). Tries the methods <see cref="LinuxPlan.CaptureOrder"/> picks, in order, and logs
/// each decision: on KDE Plasma (Wayland) the screenshot portal (xdg-desktop-portal-kde) first, then spectacle, grim
/// and gnome-screenshot; on X11, XGetImage first. The image covers the whole desktop; <see cref="ScreenMapping"/>
/// fits the screens Avalonia reports onto it.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxCapture : ICaptureBackend
{
    private readonly Logger _log;
    private readonly IReadOnlyList<LinuxCaptureMethod> _order;
    private MonitorLayout? _last;

    /// <summary>How long the portal may take, including a permission dialog the desktop shows the first time.</summary>
    public static readonly TimeSpan PortalTimeout = TimeSpan.FromSeconds(90);

    public LinuxCapture(Logger log)
    {
        _log = log;
        var (order, reason) = LinuxPlan.CaptureOrder(Environment.GetEnvironmentVariable, Wsl.IsWslNow(), ProgramExists);
        _order = order;
        log.Info($"Linux capture: {reason} Order: {(order.Count == 0 ? "none" : string.Join(", ", order.Select(LinuxPlan.Name)))}.");
        if (order.Count == 0) throw new CaptureException(reason);
    }

    public string Name => "Linux (" + string.Join(", then ", _order.Select(LinuxPlan.Name)) + ")";

    /// <summary>The layout of the last capture (Linux needs the screenshot first to know it).</summary>
    public MonitorLayout GetLayout() =>
        _last ?? throw new InvalidOperationException("On Linux the layout comes with the capture (CaptureScreenAsync).");

    public ScreenImage CaptureAll(MonitorLayout layout) =>
        throw new InvalidOperationException("On Linux, use CaptureScreenAsync.");

    public async Task<(MonitorLayout Layout, ScreenImage Image)> CaptureScreenAsync(IReadOnlyList<ScreenInfo> screens, CancellationToken ct = default)
    {
        _log.Info($"Screens from the window system: {(screens.Count == 0 ? "none" : string.Join("; ", screens.Select(s => $"{s.Name} {s.Bounds.Width}x{s.Bounds.Height} at ({s.Bounds.X},{s.Bounds.Y}) scaling {s.Scaling:0.##}{(s.IsPrimary ? " primary" : "")}")))}.");
        var problems = new List<string>();
        foreach (var method in _order)
        {
            var started = Stopwatch.StartNew();
            try
            {
                var image = method switch
                {
                    LinuxCaptureMethod.Portal => await PortalAsync(ct).ConfigureAwait(false),
                    LinuxCaptureMethod.X11 => X11Capture.CaptureRoot(_log),
                    _ => await ToolAsync(method, ct).ConfigureAwait(false),
                };
                var (layout, note) = ScreenMapping.ToImageLayout(screens, image.Width, image.Height);
                _log.Info($"Captured with {LinuxPlan.Name(method)} in {started.ElapsedMilliseconds} ms. {note}");
                _last = layout;
                var whole = new ScreenImage(image.Width, image.Height, new PixelRect(0, 0, image.Width, image.Height), image.Bgra);
                return (layout, whole);
            }
            catch (CaptureCancelledException)
            {
                throw; // the user said no: don't try another way behind their back
            }
            catch (Exception ex) when (ex is CaptureException or IOException or InvalidOperationException or TimeoutException
                                           or DBusExceptionBase or DllNotFoundException or EntryPointNotFoundException)
            {
                string why = ex is DllNotFoundException ? "libX11 isn't installed (libx11-6 on Debian)" : ex.Message;
                _log.Info($"Capture with {LinuxPlan.Name(method)} didn't work ({why}); {(method == _order[^1] ? "no more methods to try" : "trying the next")}.");
                problems.Add($"{LinuxPlan.Name(method)}: {why}");
            }
        }
        throw new CaptureException("Couldn't capture the screen. Tried " + string.Join("; ", problems) +
            ". On KDE Plasma, check that xdg-desktop-portal-kde is installed (or install spectacle); run with --verbose for details.");
    }

    // ---------------- the portal ----------------

    private async Task<ScreenImage> PortalAsync(CancellationToken ct)
    {
        var portal = await Portal.GetAsync(_log).ConfigureAwait(false);
        var asked = DateTime.UtcNow;
        _log.Info("Portal: asking for a screenshot (non-interactive). The first time, the desktop may ask whether SquareSnap may take screenshots.");
        PortalResponse response;
        try
        {
            response = await portal.RequestAsync("org.freedesktop.portal.Screenshot", "Screenshot", "sa{sv}",
                static (ref MessageWriter w, string token) =>
                {
                    w.WriteString(""); // no parent window
                    Portal.WriteOptions(ref w, token, [("interactive", false)]);
                }, PortalTimeout, ct).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException ex)
        {
            throw new CaptureException($"the screenshot portal isn't available ({ex.ErrorName}: {ex.ErrorMessage})", ex);
        }
        _log.Info($"Portal: answer {response.CodeText} after {(DateTime.UtcNow - asked).TotalMilliseconds:0} ms.");
        if (response.Code == 1)
            throw new CaptureCancelledException("The screenshot was refused or cancelled at the desktop's prompt.");
        if (response.Code != 0)
            throw new CaptureException($"the screenshot portal answered {response.CodeText}");

        string? uri = Portal.StringResult(response.Results, "uri");
        if (string.IsNullOrEmpty(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var u) || !u.IsFile)
            throw new CaptureException($"the screenshot portal gave no file (uri '{uri}')");
        string file = u.LocalPath;
        _log.Info($"Portal: screenshot file {file}.");
        try
        {
            return PngImage.Load(File.ReadAllBytes(file));
        }
        finally
        {
            // The portal saved the screenshot for SquareSnap only (GNOME puts it in ~/Pictures); remove that copy once
            // it's read, but only a file made for this request.
            TryDeleteOwnFile(file, asked);
        }
    }

    private void TryDeleteOwnFile(string file, DateTime askedUtc)
    {
        try
        {
            var info = new FileInfo(file);
            if (info.Exists && info.LastWriteTimeUtc >= askedUtc.AddSeconds(-2))
            {
                info.Delete();
                _log.Debug($"Removed the portal's copy {file}.");
            }
            else if (info.Exists)
            {
                _log.Info($"Left {file} in place: it's older than this request, so it isn't SquareSnap's.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Info($"Couldn't remove the portal's copy {file} ({ex.Message}).");
        }
    }

    // ---------------- spectacle, grim, gnome-screenshot ----------------

    private async Task<ScreenImage> ToolAsync(LinuxCaptureMethod method, CancellationToken ct)
    {
        string dir = PrivateTempDir();
        string file = Path.Combine(dir, "screen.png");
        var cmd = LinuxPlan.ToolFor(method, file);
        _log.Info($"Running {cmd}.");
        try
        {
            // By full path: a bare name would also find a file of that name in the current folder.
            string exe = FindProgram(cmd.Program) ?? throw new CaptureException($"{cmd.Program} isn't installed (not found on PATH)");
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };
            foreach (string a in cmd.Arguments) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new CaptureException($"{cmd.Program} didn't start");
            p.StandardInput.Close();
            var stdout = p.StandardOutput.ReadToEndAsync(ct);
            var stderr = p.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw new CaptureException($"{cmd.Program} didn't finish within 30 seconds");
            }
            string err = (await stderr.ConfigureAwait(false)).Trim(), outText = (await stdout.ConfigureAwait(false)).Trim();
            if (err.Length > 0 || outText.Length > 0) _log.Debug($"{cmd.Program} said: {string.Join(" | ", new[] { outText, err }.Where(t => t.Length > 0))}");
            if (p.ExitCode != 0)
                throw new CaptureException($"{cmd.Program} exited with code {p.ExitCode}{(err.Length > 0 ? ": " + FirstLine(err) : "")}");
            if (!File.Exists(file) || new FileInfo(file).Length == 0)
                throw new CaptureException($"{cmd.Program} wrote no image");
            return PngImage.Load(await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false));
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new CaptureException($"{cmd.Program} couldn't be started ({ex.Message})", ex);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>A new folder only this user can read (the screenshot may show private things).</summary>
    private static string PrivateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "squaresnap-" + Portal.NewToken());
        Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return dir;
    }

    private static string FirstLine(string s) => s.Split('\n')[0].Trim();

    /// <summary>True when <paramref name="program"/> is an executable on PATH.</summary>
    public static bool ProgramExists(string program) => FindProgram(program) != null;

    /// <summary>The full path of <paramref name="program"/> on PATH (absolute folders only), or null. Start tools by this path.</summary>
    public static string? FindProgram(string program) =>
        LinuxPlan.FindOnPath(program, Environment.GetEnvironmentVariable("PATH"), f =>
        {
            try
            {
                return File.Exists(f) && (File.GetUnixFileMode(f) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        });
}

/// <summary>Decodes a screenshot PNG into SquareSnap's BGRA layout (alpha forced to 255).</summary>
public static class PngImage
{
    public static ScreenImage Load(byte[] png)
    {
        using var image = SKImage.FromEncodedData(png) ?? throw new CaptureException("the screenshot file isn't a readable image");
        int w = image.Width, h = image.Height;
        var bgra = new byte[w * h * 4];
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        unsafe
        {
            fixed (byte* p = bgra)
            {
                if (!image.ReadPixels(info, (IntPtr)p, w * 4, 0, 0))
                    throw new CaptureException("the screenshot couldn't be converted to pixels");
            }
        }
        for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
        return new ScreenImage(w, h, new PixelRect(0, 0, w, h), bgra);
    }
}
