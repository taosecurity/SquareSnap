using System.Text.Json.Serialization;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Naming;

namespace SquareSnap.Core.Settings;

/// <summary>Everything SquareSnap remembers between runs. Stored as JSON (see <see cref="SettingsStore"/>).</summary>
public sealed class AppSettings
{
    public const int CurrentSchema = 2;

    /// <summary>2 for SquareSnap 2.x. A file without it is a 1.0 file and gets imported.</summary>
    public int SchemaVersion { get; set; } = CurrentSchema;

    /// <summary>Where captures are saved. Null means the default (Pictures/SquareSnap).</summary>
    public string? SaveFolder { get; set; }

    public string FileNameTemplate { get; set; } = FileNamer.DefaultTemplate;

    public ImageFormat Format { get; set; } = ImageFormat.Png;

    /// <summary>JPEG and WebP quality, 1 to 100.</summary>
    public int Quality { get; set; } = 90;

    /// <summary>Side length of the saved image in pixels; 0 keeps the captured size.</summary>
    public int OutputSizePixels { get; set; }

    /// <summary>Put every capture on the clipboard.</summary>
    public bool CopyToClipboard { get; set; } = true;

    /// <summary>Save straight to the save folder with the next file name instead of showing a preview first.</summary>
    public bool AutoSave { get; set; }

    /// <summary>1.0 drew a 1-pixel light-gray border into every image; 2.0 makes it optional.</summary>
    public bool DrawBorder { get; set; }

    public bool DarkMode { get; set; } = true;

    /// <summary>Closing the window keeps SquareSnap running in the tray (so the hotkey keeps working).</summary>
    public bool CloseToTray { get; set; }

    /// <summary>Global capture hotkey, e.g. "Ctrl+Alt+S". Empty turns it off.</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+S";

    /// <summary>Seconds to wait before freezing the screen (for capturing menus), 0 to 60.</summary>
    public int DelaySeconds { get; set; }

    /// <summary>Keep the square after the drag so the arrow keys can adjust it; Enter captures. Off = release captures (1.0 behaviour).</summary>
    public bool FineTune { get; set; }

    /// <summary>Side length used by fixed-size mode.</summary>
    public int FixedSize { get; set; } = 512;

    /// <summary>The last square captured, as "x,y,w,h" in physical pixels, for "repeat last region".</summary>
    public string? LastRegion { get; set; }

    [JsonIgnore]
    public OutputSize OutputSize => OutputSizePixels <= 0 ? OutputSize.Native : new OutputSize(OutputSizePixels);

    [JsonIgnore]
    public PixelRect? LastRegionRect => PixelRect.TryParse(LastRegion, out var r) ? r : null;

    /// <summary>
    /// Puts out-of-range values back in range and returns a description of each change (for the log).
    /// Settings files are hand-editable, so this runs after every load.
    /// </summary>
    public List<string> Normalize()
    {
        var fixes = new List<string>();
        if (Quality is < 1 or > 100) { fixes.Add($"Quality {Quality} is outside 1-100; using 90."); Quality = 90; }
        if (DelaySeconds is < 0 or > 60) { fixes.Add($"DelaySeconds {DelaySeconds} is outside 0-60; using 0."); DelaySeconds = 0; }
        if (OutputSizePixels != 0 && (OutputSizePixels < 16 || OutputSizePixels > OutputSize.MaxPixels))
        { fixes.Add($"OutputSizePixels {OutputSizePixels} is outside 16-{OutputSize.MaxPixels}; using native size."); OutputSizePixels = 0; }
        if (FixedSize < SquareSelection.MinimumSize || FixedSize > OutputSize.MaxPixels)
        { fixes.Add($"FixedSize {FixedSize} is out of range; using 512."); FixedSize = 512; }
        if (!Enum.IsDefined(Format)) { fixes.Add($"Format {(int)Format} is unknown; using PNG."); Format = ImageFormat.Png; }
        string? templateProblem = FileNamer.Validate(FileNameTemplate);
        if (templateProblem != null)
        { fixes.Add($"{templateProblem} Using {FileNamer.DefaultTemplate}."); FileNameTemplate = FileNamer.DefaultTemplate; }
        if (LastRegion != null && LastRegionRect == null) { fixes.Add($"LastRegion '{LastRegion}' is not x,y,w,h; cleared."); LastRegion = null; }
        if (!Cli.HotkeySpec.TryParse(Hotkey, out _, out string? hotkeyProblem)) { fixes.Add($"{hotkeyProblem} The hotkey is off until you pick another."); Hotkey = ""; }
        Hotkey ??= "";
        SchemaVersion = CurrentSchema;
        return fixes;
    }
}
