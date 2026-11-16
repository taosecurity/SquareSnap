using SquareSnap.Core.Logging;
using SquareSnap.Core.Naming;
using SquareSnap.Core.Platform;
using SquareSnap.Core.Settings;

namespace SquareSnap.Tests;

/// <summary>Each test gets its own temp folder standing in for %APPDATA% / ~/.config.</summary>
public sealed class SettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("squaresnap-test-").FullName;
    private readonly StringWriter _console = new();
    private readonly Logger _log;
    private readonly AppPaths _paths;

    public SettingsTests()
    {
        _log = new Logger(Path.Combine(_root, "test.log"), verbose: true, console: _console);
        // Linux-style paths, rooted in the temp folder, work on both OSes for file I/O.
        var env = new Dictionary<string, string> { ["HOME"] = _root.Replace('\\', '/') };
        _paths = AppPaths.ForLinux(n => env.GetValueOrDefault(n));
    }

    public void Dispose()
    {
        _log.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string LogText => _console.ToString();

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var s = new SettingsStore(_paths, _log).Load();
        Assert.Equal(AppSettings.CurrentSchema, s.SchemaVersion);
        Assert.True(s.CopyToClipboard);
        Assert.False(s.DrawBorder);
        Assert.Equal(FileNamer.DefaultTemplate, s.FileNameTemplate);
        Assert.Contains("No settings file", LogText);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var store = new SettingsStore(_paths, _log);
        var s = new AppSettings { SaveFolder = "/x/y", Format = ImageFormat.Webp, OutputSizePixels = 1080, LastRegion = "1,2,300,300", AutoSave = true };
        Assert.True(store.Save(s));
        Assert.Contains("\"Webp\"", File.ReadAllText(_paths.SettingsFile)); // enums stored as words, readable by hand

        var back = store.Load();
        Assert.Equal("/x/y", back.SaveFolder);
        Assert.Equal(ImageFormat.Webp, back.Format);
        Assert.Equal(1080, back.OutputSize.Pixels);
        Assert.Equal(new SquareSnap.Core.Geometry.PixelRect(1, 2, 300, 300), back.LastRegionRect);
        Assert.True(back.AutoSave);
        Assert.False(File.Exists(_paths.SettingsFile + ".tmp"));
    }

    [Fact]
    public void ImportsVersion1File()
    {
        Directory.CreateDirectory(_paths.ConfigDir);
        File.WriteAllText(_paths.SettingsFile, """{"DefaultSaveLocation":"C:\\Users\\alex\\Shots","NextFileNumber":47}""");

        var s = new SettingsStore(_paths, _log).Load();

        Assert.Equal(@"C:\Users\alex\Shots", s.SaveFolder);
        Assert.Contains("Imported SquareSnap 1.0 settings", LogText);
        Assert.True(File.Exists(Path.Combine(_paths.ConfigDir, "settings.v1.json")));
        Assert.Contains("SchemaVersion", File.ReadAllText(_paths.SettingsFile)); // rewritten as 2.0, imported once
    }

    [Fact]
    public void ImportsVersion1WithoutFolder()
    {
        Directory.CreateDirectory(_paths.ConfigDir);
        File.WriteAllText(_paths.SettingsFile, """{"DefaultSaveLocation":null,"NextFileNumber":1}""");
        var store = new SettingsStore(_paths, _log);
        var s = store.Load();
        Assert.Null(s.SaveFolder);
        Assert.Equal(_paths.DefaultSaveFolder, store.SaveFolderFor(s));
    }

    [Fact]
    public void ImportsBejSnapOnFirstRun()
    {
        string conf = _paths.BejSnapSettingsFile!;
        Directory.CreateDirectory(Path.GetDirectoryName(conf)!);
        File.WriteAllText(conf, "[General]\nsavePath=/home/alex/Pictures/My Snaps\ndarkMode=false\n");

        var s = new SettingsStore(_paths, _log).Load();

        Assert.Equal("/home/alex/Pictures/My Snaps", s.SaveFolder);
        Assert.False(s.DarkMode);
        Assert.Contains("Imported BejSnap settings", LogText);
        Assert.True(File.Exists(_paths.SettingsFile));
    }

    [Fact]
    public void BrokenJsonIsKeptAndExplained()
    {
        Directory.CreateDirectory(_paths.ConfigDir);
        File.WriteAllText(_paths.SettingsFile, "{ this is not json");

        var s = new SettingsStore(_paths, _log).Load();

        Assert.True(s.CopyToClipboard); // defaults
        Assert.True(File.Exists(_paths.SettingsFile + ".bad"));
        Assert.Contains("is not valid JSON", LogText);
        Assert.Contains(_paths.SettingsFile, LogText);
    }

    [Fact]
    public void OutOfRangeValuesAreFixedAndLogged()
    {
        Directory.CreateDirectory(_paths.ConfigDir);
        File.WriteAllText(_paths.SettingsFile, """
            {
              // hand-edited
              "SchemaVersion": 2,
              "Quality": 500,
              "DelaySeconds": -4,
              "OutputSizePixels": 3,
              "FileNameTemplate": "bad/{n}",
              "LastRegion": "nonsense",
            }
            """);

        var s = new SettingsStore(_paths, _log).Load();

        Assert.Equal(90, s.Quality);
        Assert.Equal(0, s.DelaySeconds);
        Assert.True(s.OutputSize.IsNative);
        Assert.Equal(FileNamer.DefaultTemplate, s.FileNameTemplate);
        Assert.Null(s.LastRegion);
        Assert.Contains("Quality 500", LogText);
    }

    [Fact]
    public void QSettingsParsing()
    {
        var v = SettingsStore.ReadQSettingsGeneral([
            "[Other]", "savePath=/wrong",
            "[General]", "savePath=\"/home/alex/with \\\"quote\\\"\"", "darkMode=true", "; comment", "broken line",
        ]);
        Assert.Equal("/home/alex/with \"quote\"", v["savePath"]);
        Assert.Equal("true", v["darkMode"]);
        Assert.Equal(2, v.Count);
    }
}

public sealed class LoggerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("squaresnap-log-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void QuietModeLogsInfoAndProblemsToTheFileOnly()
    {
        string file = Path.Combine(_dir, "a.log");
        var console = new StringWriter();
        using (var log = new Logger(file, verbose: false, console: console, clock: () => new DateTime(2026, 11, 11, 9, 0, 0)))
        {
            log.Debug("hidden");
            log.Info("SquareSnap starting"); // since beta 16: a start from the app menu shows in the log
            log.Warning("disk nearly full");
            log.Error("capture failed", new InvalidOperationException("no display"));
        }
        string text = File.ReadAllText(file);
        Assert.DoesNotContain("hidden", text);
        Assert.Contains("INFO  SquareSnap starting", text);
        Assert.Contains("2026-11-11 09:00:00.000 WARN  disk nearly full", text);
        Assert.Contains("capture failed (InvalidOperationException: no display)", text);
        Assert.Equal("", console.ToString()); // nothing on the terminal unless --verbose
    }

    [Fact]
    public void VerboseEchoesEverything()
    {
        string file = Path.Combine(_dir, "sub", "b.log");
        var console = new StringWriter();
        using (var log = new Logger(file, verbose: true, console: console))
            log.Debug("backend chosen: portal");
        Assert.Contains("DEBUG backend chosen: portal", File.ReadAllText(file));
        Assert.Contains("backend chosen: portal", console.ToString());
    }

    [Fact]
    public void LargeLogIsTrimmed()
    {
        string file = Path.Combine(_dir, "big.log");
        File.WriteAllText(file, string.Concat(Enumerable.Repeat("0123456789012345678901234567890123456789\n", 40_000)));
        Logger.TrimIfLarge(file);
        long size = new FileInfo(file).Length;
        Assert.True(size < Logger.MaxBytes, $"size {size}");
        Assert.StartsWith("(older lines removed)", File.ReadAllText(file));
    }
}
