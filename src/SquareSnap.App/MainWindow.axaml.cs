using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using SquareSnap.Capture;
using SquareSnap.Capture.Imaging;
using SquareSnap.Capture.Linux;
using SquareSnap.Core;
using SquareSnap.Core.Cli;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Naming;
using SquareSnap.Core.Platform;
using PixelRect = SquareSnap.Core.Geometry.PixelRect;

namespace SquareSnap.App;

public partial class MainWindow : Window
{
    private const string ReadMeUrl = "https://github.com/taosecurity/SquareSnap#readme";

    /// <summary>The user's wording for Help > About (2026-11-13, the same sentence as BejView's).</summary>
    internal const string AboutCredits =
        "Developed by Richard Bejtlich. Version 1.0 programmed by Claude Sonnet 3.7. Version 2.0 programmed by Claude Opus 5.5.";

    private static readonly string[] HotkeyChoices = ["Ctrl+Alt+S", "Ctrl+Shift+S", "Ctrl+Alt+Q", "Ctrl+PrintScreen", ""];
    private static readonly int[] DelayChoices = [0, 3, 5, 10];
    private static readonly int[] FixedChoices = [256, 512, 1024, 1080];

    private readonly AppSession _s;
    private readonly IGlobalHotkey _hotkey;
    private TrayIcon? _tray;
    private ScreenImage? _capture;
    private Bitmap? _preview;
    private bool _busy;
    private bool _exiting;
    /// <summary>Linux: whether a tray host runs (checked at start); without one, closing the window exits.</summary>
    private bool _trayHost = true;

    // Needed by the XAML loader / previewer.
    public MainWindow() : this(AppSession.Current) { }

    internal MainWindow(AppSession session)
    {
        _s = session;
        InitializeComponent();
        Title = $"SquareSnap {AppInfo.Version}";

        LoadOptionsIntoMenu();
        BuildChoiceMenus();
        UpdateButtons();

        KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+N"), Command = new Relay(() => _ = CaptureAsync(OverlayMode.Drag)) });
        KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+R"), Command = new Relay(() => _ = RepeatAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+S"), Command = new Relay(Save) });
        KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+Shift+S"), Command = new Relay(() => _ = SaveAsAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+C"), Command = new Relay(Copy) });

        _hotkey = PlatformServices.CreateHotkey(_s.Log);
        _hotkey.Pressed += () => Dispatcher.UIThread.Post(() => _ = CaptureAsync(OverlayMode.Drag));
        if (_hotkey is PortalHotkey portalHotkey)
            portalHotkey.StatusChanged += (text, problem) => Dispatcher.UIThread.Post(() => SetStatus(text, problem));
        RegisterHotkey();
        SetUpTray();

        if (OperatingSystem.IsLinux())
        {
            if (_s.Clipboard is CommandClipboard command) _s.Clipboard = new LinuxWindowClipboard(_s.Log, this, command);
            _ = CheckTrayHostAsync();
        }

        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _hotkey.Dispose();
            if (_tray != null && Application.Current is { } app) TrayIcon.SetIcons(app, null); // removes and disposes it
            _tray?.Dispose();
        };
    }

    // ---------------- capture ----------------

    private async Task CaptureAsync(OverlayMode mode, PixelRect? region = null)
    {
        if (_busy) return;
        _busy = true;
        bool wasVisible = IsVisible;
        try
        {
            if (wasVisible)
            {
                Hide();
                await Task.Delay(250); // let Windows finish removing the window before the screen is frozen
            }
            var st = _s.Settings;
            var outcome = await CaptureFlow.RunAsync(_s, new CaptureRequest(mode, st.FineTune, st.FixedSize, region, st.DelaySeconds));
            if (outcome.Error != null) { ShowWindow(); SetStatus(outcome.Error, error: true); return; }
            if (outcome.Cancelled) { if (wasVisible) ShowWindow(); SetStatus("Capture cancelled."); return; }
            SetCapture(outcome.Image!);
            AfterCapture(showWindow: wasVisible || !(st.AutoSave || st.CopyToClipboard));
        }
        catch (Exception ex)
        {
            _s.Log.Error("Capture failed", ex);
            ShowWindow();
            SetStatus($"Capture failed: {ex.Message} (details in the log).", error: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private Task RepeatAsync()
    {
        var last = _s.Settings.LastRegionRect;
        if (last == null) { SetStatus("No previous square yet: capture one first."); return Task.CompletedTask; }
        return CaptureAsync(OverlayMode.Drag, last);
    }

    private void SetCapture(ScreenImage image)
    {
        _capture = image;
        var o = OutputOptions.FromSettings(_s);
        var final = ImageEncoder.Render(image, o.Size, o.Border);
        _preview?.Dispose();
        _preview = Overlay.ToBitmap(final);
        PreviewImage.Source = _preview;
        EmptyText.IsVisible = false;
        InfoText.Text = o.Size.IsNative || o.Size.Pixels == image.Width
            ? $"{image.Width} × {image.Height} · 1:1"
            : $"{image.Width} × {image.Height} → {o.Size.Pixels} × {o.Size.Pixels}";
        UpdateButtons();
    }

    private void AfterCapture(bool showWindow)
    {
        var parts = new List<string>();
        bool failed = false;
        if (_s.Settings.CopyToClipboard)
        {
            if (Output.Copy(_s, _capture!, OutputOptions.FromSettings(_s))) parts.Add("copied to the clipboard");
            else { parts.Add("copying failed (see the log)"); failed = true; }
        }
        if (_s.Settings.AutoSave)
        {
            var (path, error) = Output.SaveToFolder(_s, _capture!, OutputOptions.FromSettings(_s));
            if (path != null) { parts.Add($"saved as {path}"); ShowSaved(path); }
            else { parts.Add(error!); failed = true; }
        }
        SetStatus(parts.Count == 0 ? "Captured. Save (Ctrl+S) or copy (Ctrl+C) it." : "Captured: " + string.Join("; ", parts) + ".", failed);
        if (showWindow || failed) ShowWindow();
    }

    // ---------------- save / copy ----------------

    private void Save()
    {
        if (_capture == null) return;
        var (path, error) = Output.SaveToFolder(_s, _capture, OutputOptions.FromSettings(_s));
        SetStatus(path != null ? $"Saved as {path}" : error!, path == null);
        if (path != null) ShowSaved(path);
    }

    // ---------------- "Saved to" banner ----------------

    private static readonly TimeSpan BannerTime = TimeSpan.FromSeconds(8);
    private DispatcherTimer? _bannerTimer;
    private string? _lastSavedPath;

    /// <summary>Shows "Saved to &lt;folder&gt;" with the file name over the preview, then fades it after a few seconds.</summary>
    internal void ShowSaved(string path)
    {
        _lastSavedPath = path;
        SavedTitle.Text = $"Saved to {Path.GetDirectoryName(path)}";
        SavedFile.Text = Path.GetFileName(path);
        ToolTip.SetTip(SavedTitle, path);
        BannerHiding = false;
        SavedBanner.Opacity = 1;
        SavedBanner.IsVisible = true;

        _bannerTimer?.Stop();
        _bannerTimer = new DispatcherTimer { Interval = BannerTime };
        _bannerTimer.Tick += (_, _) =>
        {
            if (SavedBanner.IsPointerOver) return; // keep it while the mouse is on it; check again next tick
            _bannerTimer!.Stop();
            HideSaved();
        };
        _bannerTimer.Start();
    }

    /// <summary>True from the moment the banner starts fading until a new save shows it again.</summary>
    internal bool BannerHiding { get; private set; }

    private void HideSaved()
    {
        BannerHiding = true;
        SavedBanner.Opacity = 0; // fades (Opacity transition in the XAML), then goes away
        DispatcherTimer.RunOnce(() => { if (BannerHiding) SavedBanner.IsVisible = false; }, TimeSpan.FromMilliseconds(450));
    }

    private void SavedClose_Click(object? sender, RoutedEventArgs e)
    {
        _bannerTimer?.Stop();
        HideSaved();
    }

    private async void SavedOpen_Click(object? sender, RoutedEventArgs e)
    {
        if (_lastSavedPath == null) return;
        try
        {
            if (OperatingSystem.IsWindows() && File.Exists(_lastSavedPath))
            {
                // Opens Explorer with the new file selected. By full path: Windows looks in the current folder before
                // C:\Windows, so a bare "explorer.exe" could start a file of that name there (security review, 2026-11-14).
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                // Explorer runs outside the Store package, so it gets the real location (AppPaths.RealLocation, step 6).
                System.Diagnostics.Process.Start(explorer, $"/select,\"{AppPaths.RealLocation(_lastSavedPath)}\"");
                return;
            }
            string? folder = Path.GetDirectoryName(_lastSavedPath);
            if (folder != null) await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
        }
        catch (Exception ex)
        {
            _s.Log.Warning($"Couldn't open the folder for {_lastSavedPath} ({ex.Message}).");
            SetStatus($"Couldn't open the folder: {ex.Message}", error: true);
        }
    }

    private async Task SaveAsAsync()
    {
        if (_capture == null) return;
        var o = OutputOptions.FromSettings(_s);
        IStorageFolder? start = null;
        try { start = await StorageProvider.TryGetFolderFromPathAsync(o.Folder); } catch (Exception) { /* folder may not exist yet */ }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save capture as",
            SuggestedFileName = FileNamer.NextName(o.Template, DateTime.Now, o.Size.SideFor(_capture.Width), o.Format,
                Directory.Exists(o.Folder) ? Directory.EnumerateFiles(o.Folder).Select(Path.GetFileName).OfType<string>() : []),
            SuggestedStartLocation = start,
            DefaultExtension = o.Format.Extension().TrimStart('.'),
            FileTypeChoices =
            [
                new FilePickerFileType("PNG image") { Patterns = ["*.png"] },
                new FilePickerFileType("JPEG image") { Patterns = ["*.jpg", "*.jpeg"] },
                new FilePickerFileType("WebP image") { Patterns = ["*.webp"] },
            ],
        });
        string? path = file?.TryGetLocalPath();
        if (path == null) return;
        string? error = Output.SaveAs(_s, _capture, o, path);
        SetStatus(error ?? $"Saved as {path}", error != null);
        if (error == null) ShowSaved(path);
    }

    private void Copy()
    {
        if (_capture == null) return;
        bool ok = Output.Copy(_s, _capture, OutputOptions.FromSettings(_s));
        SetStatus(ok ? "Copied to the clipboard." : "Copying failed (see Help > Open the log file).", !ok);
    }

    // ---------------- menus ----------------

    private void LoadOptionsIntoMenu()
    {
        var st = _s.Settings;
        ClipboardOpt.IsChecked = st.CopyToClipboard;
        AutoSaveOpt.IsChecked = st.AutoSave;
        FineTuneOpt.IsChecked = st.FineTune;
        BorderOpt.IsChecked = st.DrawBorder;
        TrayOpt.IsChecked = st.CloseToTray;
        LightOpt.IsChecked = !st.DarkMode;
    }

    private void Toggle_Click(object? sender, RoutedEventArgs e)
    {
        var st = _s.Settings;
        st.CopyToClipboard = ClipboardOpt.IsChecked;
        st.AutoSave = AutoSaveOpt.IsChecked;
        st.FineTune = FineTuneOpt.IsChecked;
        st.DrawBorder = BorderOpt.IsChecked;
        st.CloseToTray = TrayOpt.IsChecked;
        st.DarkMode = !LightOpt.IsChecked;
        if (Application.Current != null) Application.Current.RequestedThemeVariant = st.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        _s.SaveSettings();
        if (_capture != null && ReferenceEquals(sender, BorderOpt)) SetCapture(_capture);
    }

    private void BuildChoiceMenus()
    {
        Fill(SizeMenu, OutputSize.Presets.Select(p => (p.IsNative ? "Captured size" : $"{p.Pixels} × {p.Pixels}", (object)p.Pixels)),
            () => _s.Settings.OutputSizePixels, v => { _s.Settings.OutputSizePixels = (int)v; if (_capture != null) SetCapture(_capture); });
        Fill(FormatMenu, [("PNG (exact pixels)", ImageFormat.Png), ("JPEG", ImageFormat.Jpeg), ("WebP", ImageFormat.Webp)],
            () => _s.Settings.Format, v => _s.Settings.Format = (ImageFormat)v);
        Fill(FixedMenu, FixedChoices.Select(n => ($"{n} × {n}", (object)n)),
            () => _s.Settings.FixedSize, v => _s.Settings.FixedSize = (int)v);
        Fill(DelayMenu, DelayChoices.Select(n => (n == 0 ? "None" : $"{n} seconds", (object)n)),
            () => _s.Settings.DelaySeconds, v => _s.Settings.DelaySeconds = (int)v);
        Fill(HotkeyMenu, HotkeyChoices.Select(h => (h.Length == 0 ? "Off" : h, (object)h)),
            () => _s.Settings.Hotkey, v => { _s.Settings.Hotkey = (string)v; RegisterHotkey(); });
    }

    /// <summary>Fills a submenu with radio items for a setting and keeps the check mark in step.</summary>
    private void Fill(MenuItem parent, IEnumerable<(string Label, object Value)> choices, Func<object> current, Action<object> set)
    {
        var items = new List<MenuItem>();
        foreach (var (label, value) in choices)
        {
            var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.Radio, GroupName = parent.Name, IsChecked = Equals(current(), value), Tag = value };
            item.Click += (_, _) =>
            {
                set(item.Tag!);
                _s.SaveSettings();
                foreach (var i in items) i.IsChecked = ReferenceEquals(i, item);
            };
            items.Add(item);
        }
        // A hand-edited value that isn't one of the choices still shows, so the menu never lies.
        if (!items.Any(i => i.IsChecked == true))
            items.Insert(0, new MenuItem { Header = $"{current()} (from settings file)", ToggleType = MenuItemToggleType.Radio, GroupName = parent.Name, IsChecked = true, IsEnabled = false });
        parent.ItemsSource = items;
    }

    private void RegisterHotkey()
    {
        if (!HotkeySpec.TryParse(_s.Settings.Hotkey, out var spec, out string? problem))
        {
            SetStatus(problem!, error: true);
            return;
        }
        string? error = _hotkey.Register(spec);
        if (error != null) SetStatus(error, error: true);
        else if (spec != null && _hotkey is PortalHotkey) SetStatus($"Asking the desktop for the hotkey {spec}; KDE Plasma may ask you to confirm it.");
        else if (spec != null) SetStatus($"Ready. Press {spec} anywhere to capture a square.");
    }

    private async Task CheckTrayHostAsync()
    {
        _trayHost = await LinuxDesktop.TrayHostRunningAsync(_s.Log);
    }

    private void SetUpTray()
    {
        try
        {
            var menu = new NativeMenu();
            menu.Items.Add(TrayItem("Capture square", () => _ = CaptureAsync(OverlayMode.Drag)));
            menu.Items.Add(TrayItem("Capture fixed-size square", () => _ = CaptureAsync(OverlayMode.Fixed)));
            menu.Items.Add(TrayItem("Repeat last square", () => _ = RepeatAsync()));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(TrayItem("Show SquareSnap", ShowWindow));
            menu.Items.Add(TrayItem("Exit", ExitApp));

            _tray = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SquareSnap/Assets/squaresnap.ico"))),
                ToolTipText = "SquareSnap",
                Menu = menu,
                IsVisible = true,
            };
            _tray.Clicked += (_, _) => ShowWindow();
            // In Avalonia 12 a TrayIcon only becomes a real system tray icon once it's in the application's
            // Icons list (TrayIcon.Attach); creating the object alone shows nothing (betas 1-7).
            if (Application.Current is { } app)
            {
                TrayIcon.SetIcons(app, new TrayIcons { _tray });
                _s.Log.Info("Tray icon added.");
            }
            else
            {
                _s.Log.Warning("No application object yet, so there is no tray icon.");
            }
        }
        catch (Exception ex)
        {
            _s.Log.Warning($"No tray icon on this desktop ({ex.Message}); SquareSnap works without it.");
        }
    }

    private static NativeMenuItem TrayItem(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    // ---------------- window ----------------

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_exiting && _s.Settings.CloseToTray && _tray != null && _trayHost)
        {
            e.Cancel = true;
            Hide();
            _s.Log.Info("Window closed to the tray.");
            return;
        }
        ExitApp();
    }

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(ExitCodes.Ok);
    }

    private void ShowWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    internal void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        // Errors in orange-red; otherwise go back to the theme's text colour.
        // (Setting Foreground to null would mean "no brush" and make the text invisible: the beta 2 bug.)
        if (error) StatusText.Foreground = Avalonia.Media.Brushes.OrangeRed;
        else StatusText.ClearValue(TextBlock.ForegroundProperty);
        ToolTip.SetTip(StatusText, text);
    }

    private void UpdateButtons()
    {
        bool has = _capture != null;
        SaveButton.IsEnabled = SaveMenu.IsEnabled = SaveAsMenu.IsEnabled = CopyButton.IsEnabled = CopyMenu.IsEnabled = has;
    }

    // ---------------- click handlers ----------------

    private void Capture_Click(object? sender, RoutedEventArgs e) => _ = CaptureAsync(OverlayMode.Drag);
    private void CaptureFixed_Click(object? sender, RoutedEventArgs e) => _ = CaptureAsync(OverlayMode.Fixed);
    private void Repeat_Click(object? sender, RoutedEventArgs e) => _ = RepeatAsync();
    private void Save_Click(object? sender, RoutedEventArgs e) => Save();
    private void SaveAs_Click(object? sender, RoutedEventArgs e) => _ = SaveAsAsync();
    private void Copy_Click(object? sender, RoutedEventArgs e) => Copy();
    private void Exit_Click(object? sender, RoutedEventArgs e) => ExitApp();

    private async void SaveFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Folder for captures", AllowMultiple = false });
        string? path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (path == null) return;
        _s.Settings.SaveFolder = path;
        _s.SaveSettings();
        SetStatus($"Captures will be saved in {path}");
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        string folder = _s.Store.SaveFolderFor(_s.Settings);
        Directory.CreateDirectory(folder);
        await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(AppPaths.RealLocation(folder)));
    }

    // Notepad and Explorer run outside the Store package, so they get the real location (AppPaths.RealLocation, step 6).
    private async void OpenLog_Click(object? sender, RoutedEventArgs e)
    {
        string? path = _s.Log.FilePath;
        if (path != null && File.Exists(path)) await Launcher.LaunchFileInfoAsync(new FileInfo(AppPaths.RealLocation(path)));
        else SetStatus($"No log file yet ({path ?? "logging is off"}).");
    }

    private async void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_s.Store.Paths.ConfigDir);
        await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(AppPaths.RealLocation(_s.Store.Paths.ConfigDir)));
    }

    private async void ReadMe_Click(object? sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(new Uri(ReadMeUrl));

    private async void About_Click(object? sender, RoutedEventArgs e) =>
        await AboutWindow(AppPaths.RealLocation(_s.Store.Paths.SettingsFile),
            _s.Log.FilePath is string f ? AppPaths.RealLocation(f) : null).ShowDialog(this);

    /// <summary>
    /// Help > About. Its height follows the text (beta 19): beta 18's fixed 380 x 250 window cut off the settings path
    /// and the whole Log line (the user's screenshot, 2026-11-14). The text can be selected, so the paths can be copied.
    /// </summary>
    internal static Window AboutWindow(string settingsFile, string? logFile) => new()
    {
        Title = "About SquareSnap",
        Width = 520, SizeToContent = SizeToContent.Height, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = new SelectableTextBlock
        {
            Name = "AboutText",
            Margin = new Thickness(20),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = $"SquareSnap {AppInfo.Version}\n\nSquare (1:1) screenshots for Windows and Linux.\n\n" +
                   $"{AboutCredits}\nMIT License.\nhttps://github.com/taosecurity/SquareSnap\n\n" +
                   $"Settings:\n{settingsFile}\n\nLog:\n{logFile ?? "none"}",   // each path on its own lines
        },
    };

    /// <summary>Minimal ICommand for key bindings.</summary>
    private sealed class Relay(Action action) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
