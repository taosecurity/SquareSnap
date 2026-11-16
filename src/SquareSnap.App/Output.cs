using SquareSnap.Capture;
using SquareSnap.Capture.Imaging;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Naming;

namespace SquareSnap.App;

/// <summary>How a capture is written out: size, border, format, quality, where and under what name.</summary>
internal sealed record OutputOptions(OutputSize Size, bool Border, ImageFormat Format, int Quality, string Folder, string Template)
{
    public static OutputOptions FromSettings(AppSession s) => new(
        s.Settings.OutputSize, s.Settings.DrawBorder, s.Settings.Format, s.Settings.Quality,
        s.Store.SaveFolderFor(s.Settings), s.Settings.FileNameTemplate);
}

/// <summary>Saving and copying captures. Errors come back as messages that say what to do.</summary>
internal static class Output
{
    /// <summary>Saves into the folder with the next free name. Returns (path, null) or (null, error).</summary>
    public static (string? Path, string? Error) SaveToFolder(AppSession s, ScreenImage image, OutputOptions o)
    {
        try
        {
            Directory.CreateDirectory(o.Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            string msg = $"Can't create the save folder {o.Folder} ({ex.Message}). Pick another folder in Options > Save folder.";
            s.Log.Error(msg);
            return (null, msg);
        }

        byte[] bytes;
        try
        {
            bytes = ImageEncoder.Encode(image, o.Format, o.Quality, o.Size, o.Border);
        }
        catch (Exception ex)
        {
            s.Log.Error("Couldn't encode the image", ex);
            return (null, $"Couldn't make the {o.Format} file: {ex.Message}");
        }

        // CreateNew: if another SquareSnap grabbed the same name a moment ago, take the next one.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            List<string> existing;
            try
            {
                existing = Directory.EnumerateFiles(o.Folder).Select(Path.GetFileName).OfType<string>().ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A network folder that dropped, or one we may write to but not list: the save-folder message, not a crash.
                s.Log.Error($"Can't list the save folder {o.Folder}", ex);
                return (null, $"Can't read the save folder {o.Folder} ({ex.Message}). Pick another folder in Options, or with --out.");
            }
            string name = FileNamer.NextName(o.Template, DateTime.Now, o.Size.SideFor(image.Width), o.Format, existing);
            string path = Path.Combine(o.Folder, name);
            try
            {
                using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    f.Write(bytes);
                s.Log.Info($"Saved {path} ({bytes.Length:N0} bytes, {o.Format}, {o.Size.SideFor(image.Width)} px).");
                return (path, null);
            }
            catch (IOException) when (File.Exists(path))
            {
                s.Log.Debug($"{name} appeared while saving; trying the next name.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                string msg = $"Can't write {path} ({ex.Message}). Check the folder isn't read-only or full, or pick another in Options > Save folder.";
                s.Log.Error(msg);
                return (null, msg);
            }
        }
        return (null, $"Couldn't find a free file name in {o.Folder}.");
    }

    /// <summary>Saves to an exact path chosen in a Save As dialog; the format follows the extension.</summary>
    public static string? SaveAs(AppSession s, ScreenImage image, OutputOptions o, string path)
    {
        var format = ImageFormats.TryParse(Path.GetExtension(path), out var f) ? f : o.Format;
        try
        {
            File.WriteAllBytes(path, ImageEncoder.Encode(image, format, o.Quality, o.Size, o.Border));
            s.Log.Info($"Saved {path} ({format}).");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            string msg = $"Can't write {path} ({ex.Message}).";
            s.Log.Error(msg);
            return msg;
        }
    }

    /// <summary>Copies the final image (resized and bordered as set) to the clipboard.</summary>
    /// <summary>Copies the capture to the clipboard. <paramref name="rendered"/>: the same image already resized and
    /// bordered for these options (the window's preview), so it isn't rendered a second time.</summary>
    public static bool Copy(AppSession s, ScreenImage image, OutputOptions o, ScreenImage? rendered = null)
    {
        try
        {
            var final = rendered ?? ImageEncoder.Render(image, o.Size, o.Border);
            return s.Clipboard.SetImage(final, ImageEncoder.EncodePngPlain(final));
        }
        catch (Exception ex)
        {
            s.Log.Error("Copying to the clipboard failed", ex);
            return false;
        }
    }
}
