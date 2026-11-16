using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using SquareSnap.Capture;
using SquareSnap.Capture.Windows;
using SquareSnap.Core;
using SquareSnap.Core.Cli;
using SquareSnap.Core.Logging;
using SquareSnap.Core.Platform;
using SquareSnap.Core.Settings;

namespace SquareSnap.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var parsed = CommandLine.Parse(args);
        var o = parsed.Options;

        // SquareSnap is a windowed app; borrow the terminal that started it when there is something to print.
        bool wantsConsole = !parsed.Ok || o!.Verbose || o.Command is Command.Help or Command.Version or Command.Paths or Command.Capture;
        if (wantsConsole && OperatingSystem.IsWindows()) WindowsDpi.AttachParentConsoleIfNeeded();

        if (!parsed.Ok)
        {
            Console.Error.WriteLine($"SquareSnap: {parsed.Error}");
            Console.Error.WriteLine("Run 'squaresnap --help' to see the options.");
            return ExitCodes.BadArguments;
        }

        var paths = AppPaths.ForCurrentOs();
        switch (o!.Command)
        {
            case Command.Help:
                Console.WriteLine(CommandLine.HelpText);
                return ExitCodes.Ok;
            case Command.Version:
                Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
                return ExitCodes.Ok;
            case Command.Paths:
                var s = new SettingsStore(paths, Logger.Null).Load();
                // Shown where other programs find them (the Store package may keep a private copy: AppPaths.RealLocation).
                Console.WriteLine($"Settings file: {AppPaths.RealLocation(paths.SettingsFile)}");
                Console.WriteLine($"Log file:      {AppPaths.RealLocation(o.LogFile ?? paths.DefaultLogFile)}");
                Console.WriteLine($"Save folder:   {AppPaths.RealLocation(new SettingsStore(paths, Logger.Null).SaveFolderFor(s))}");
                if (PackageIdentity.FamilyName is string family)
                    Console.WriteLine($"Installed from the Microsoft Store (package {family}).");
                return ExitCodes.Ok;
        }

        using var log = new Logger(o.LogFile ?? paths.DefaultLogFile, o.Verbose, Console.Error);
        log.Info($"{AppInfo.Name} {AppInfo.Version} starting ({RuntimeInformation.OSDescription},{RuntimeInformation.ProcessArchitecture}, .NET {Environment.Version}). " +
                 $"Arguments: {(args.Length == 0 ? "none" : string.Join(' ', args))}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Error($"Unexpected error: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => { log.Error("Unexpected background error", e.Exception); e.SetObserved(); };

        if (OperatingSystem.IsWindows()) log.Info(WindowsDpi.EnsurePerMonitorV2());

        // Under WSL the Windows SquareSnap.exe captures the Windows desktop (the bridge); --wslg stays in Linux.
        if (WslBridgeRunner.TryBridge(o, log) is int bridged)
            return bridged;

        var store = new SettingsStore(paths, log);
        var session = new AppSession(o, log, store, store.Load());
        try
        {
            session.Backend = PlatformServices.CreateCaptureBackend(log);
            log.Info($"Capture backend: {session.Backend.Name}.");
        }
        catch (CaptureException ex)
        {
            log.Error(ex.Message);
            if (!log.Verbose) Console.Error.WriteLine($"SquareSnap: {ex.Message}");
            return ExitCodes.CaptureFailed;
        }
        session.Clipboard = PlatformServices.CreateClipboard(log);
        AppSession.Current = session;

        try
        {
            bool wayland = UseWayland(Environment.GetEnvironmentVariable, log);
            // AppId matches squaresnap.desktop, so KDE Plasma (KWin) and the portals show SquareSnap's name and icon.
            var builder = wayland
                ? BuildAvaloniaApp().UseWaylandWithFallback().With(new WaylandPlatformOptions { AppId = DisplayChoice.AppId })
                : BuildAvaloniaApp();
            int code = builder.StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            log.Info($"Exiting with code {code}.");
            return code;
        }
        catch (Exception ex) when (MissingLibrary.Explain(ex) is string missing)
        {
            log.Error(missing);
            log.Debug($"Details: {ex}");
            if (!log.Verbose) Console.Error.WriteLine(missing); // --verbose already echoed it
            return ExitCodes.StartFailed;
        }
        catch (Exception ex) when (OperatingSystem.IsLinux() && ex.Message.Contains("XOpenDisplay", StringComparison.Ordinal))
        {
            // Avalonia's X11 start couldn't open the display (seen under WSLg from a session with no desktop, 2026-11-13).
            string msg = $"SquareSnap can't open a window: the X display '{Environment.GetEnvironmentVariable("DISPLAY") ?? "(DISPLAY not set)"}' " +
                         "didn't answer. Check that you're in a desktop session (under WSL, that WSLg is running: wsl --update, then wsl --shutdown).";
            log.Error(msg);
            log.Debug($"Details: {ex}");
            if (!log.Verbose) Console.Error.WriteLine(msg);
            return ExitCodes.CaptureFailed;
        }
        catch (Exception ex)
        {
            log.Error("SquareSnap stopped with an unexpected error", ex);
            Console.Error.WriteLine($"SquareSnap stopped with an unexpected error: {ex.Message}. Details are in {log.FilePath}.");
            return ExitCodes.CaptureFailed;
        }
    }

    /// <summary>
    /// Linux: native Wayland when the desktop offers it, with X11 (XWayland or Xorg) as Avalonia's own fallback.
    /// Under WSL it uses X11: on WSLg's Weston the native Wayland start hung before the first window (BejView,
    /// 2026-11-13), and Avalonia's fallback only covers a compositor it can't reach, not one that doesn't answer.
    /// SQUARESNAP_X11=1 forces X11 anywhere; SQUARESNAP_WAYLAND=1 forces native Wayland under WSL. Logged with --verbose.
    /// </summary>
    private static bool UseWayland(Func<string, string?> env, Logger log)
    {
        if (!OperatingSystem.IsLinux()) return false;
        var choice = DisplayChoice.Decide(env, Wsl.IsWslNow());
        log.Info($"Display: WAYLAND_DISPLAY={env("WAYLAND_DISPLAY") ?? "not set"}, DISPLAY={env("DISPLAY") ?? "not set"}, " +
                 $"XDG_CURRENT_DESKTOP={env("XDG_CURRENT_DESKTOP") ?? "not set"}, XDG_SESSION_TYPE={env("XDG_SESSION_TYPE") ?? "not set"}. {choice.Reason}");
        return choice.Wayland;
    }

    // Also used by the Avalonia previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
