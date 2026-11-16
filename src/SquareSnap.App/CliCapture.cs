using SquareSnap.Core.Cli;
using SquareSnap.Core.Geometry;

namespace SquareSnap.App;

/// <summary>
/// <c>squaresnap capture</c>: one capture, saved (and copied if asked), path printed on stdout, then exit.
/// Command-line options override the saved settings for this run only.
/// </summary>
internal static class CliCapture
{
    public static async Task<int> RunAsync(AppSession s)
    {
        var o = s.Options;
        var st = s.Settings;

        PixelRect? region = o.Region;
        if (o.RepeatLast)
        {
            region = st.LastRegionRect;
            if (region == null)
            {
                Console.Error.WriteLine("SquareSnap: there is no previous square to repeat yet. Capture one first.");
                return ExitCodes.BadArguments;
            }
        }

        var request = new CaptureRequest(
            o.FixedSize != null ? OverlayMode.Fixed : OverlayMode.Drag,
            o.FineTune ?? st.FineTune,
            o.FixedSize ?? st.FixedSize,
            region,
            o.DelaySeconds ?? st.DelaySeconds);

        var outcome = await CaptureFlow.RunAsync(s, request);
        if (outcome.Error != null)
        {
            Console.Error.WriteLine($"SquareSnap: {outcome.Error}");
            return ExitCodes.CaptureFailed;
        }
        if (outcome.Cancelled) return ExitCodes.Cancelled;

        var defaults = OutputOptions.FromSettings(s);
        var output = defaults with
        {
            Size = o.Size ?? defaults.Size,
            Format = o.Format ?? defaults.Format,
            Folder = o.OutFolder != null ? Path.GetFullPath(Environment.ExpandEnvironmentVariables(ExpandHome(o.OutFolder))) : defaults.Folder,
            Template = o.NameTemplate ?? defaults.Template,
        };

        var (path, error) = Output.SaveToFolder(s, outcome.Image!, output);
        if (error != null)
        {
            Console.Error.WriteLine($"SquareSnap: {error}");
            return ExitCodes.SaveFailed;
        }
        Console.WriteLine(path);

        if (o.Clipboard ?? st.CopyToClipboard)
        {
            if (!Output.Copy(s, outcome.Image!, output))
            {
                // Linux: the reason, such as "install wl-clipboard", which the file still saved without.
                string? problem = OperatingSystem.IsLinux() && s.Clipboard is Capture.Linux.CommandClipboard c ? c.LastProblem : null;
                Console.Error.WriteLine(problem != null
                    ? $"SquareSnap: saved, but not copied to the clipboard. {problem}"
                    : "SquareSnap: saved, but copying to the clipboard failed (see the log).");
            }
        }
        return ExitCodes.Ok;
    }

    private static string ExpandHome(string p) =>
        p == "~" || p.StartsWith("~/") ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p[1..] : p;
}
