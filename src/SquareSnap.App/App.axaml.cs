using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using SquareSnap.Core.Cli;

namespace SquareSnap.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var session = AppSession.Current;
        RequestedThemeVariant = session.Settings.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (session.Options.Command == Command.Capture)
            {
                // Command-line capture: no main window; the overlay appears, then the process exits with a code.
                // A window that's never shown lists the monitors for the Linux capture.
                if (!OperatingSystem.IsWindows())
                {
                    var probe = new Window { Title = "SquareSnap" };
                    session.ScreensSource = probe;
                    LogWindowSystem(session, probe);
                }
                Dispatcher.UIThread.Post(async () =>
                {
                    int code = await CliCapture.RunAsync(session);
                    desktop.Shutdown(code);
                });
            }
            else
            {
                var window = new MainWindow(session);
                session.ScreensSource = window;
                desktop.MainWindow = window;
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window.Show();
                LogWindowSystem(session, window);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Logs the window class that won (Avalonia.Wayland.WindowImpl on native Wayland, Avalonia.X11.X11Window on X11 or
    /// XWayland, Avalonia.Win32.WindowImpl on Windows): the one fact that says which path a Linux test exercised.
    /// </summary>
    private static void LogWindowSystem(AppSession session, Window window) =>
        session.Log.Info($"Display: the window uses {window.PlatformImpl?.GetType().FullName ?? "an unknown backend"}.");
}
