using System.Diagnostics;
using System.Runtime.Versioning;
using SquareSnap.Core.Logging;
using SquareSnap.Core.Platform;

namespace SquareSnap.Capture.Linux;

/// <summary>
/// Linux clipboard through wl-copy (Wayland) or xclip (X11). Both fork a small background process that serves the
/// clipboard until something else is copied, so the image stays pasteable after SquareSnap exits: the reason
/// "squaresnap capture" uses them. The PNG goes in on stdin; the programs run with an argument list, not a shell.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class CommandClipboard(Logger log) : IClipboardWriter
{
    /// <summary>Why the last copy failed, for the caller's message (for example the wl-clipboard install hint).</summary>
    public string? LastProblem { get; private set; }

    public bool SetImage(ScreenImage image, byte[] png)
    {
        var (cmd, reason) = LinuxPlan.ClipboardTool(Environment.GetEnvironmentVariable, LinuxCapture.ProgramExists);
        if (cmd == null)
        {
            // One line, not an INFO and a WARN with the same hint (beta 14's test, 2026-11-14).
            LastProblem = reason;
            log.Warning($"Clipboard: nothing to copy with. {reason}");
            return false;
        }
        log.Info($"Clipboard: {reason}");
        // By full path: a bare name would also find a file named wl-copy in the current folder (security review, 2026-11-14).
        string? exe = LinuxCapture.FindProgram(cmd.Program);
        if (exe == null)
        {
            LastProblem = $"{cmd.Program} isn't on PATH.";
            log.Warning($"Clipboard: {LastProblem}");
            return false;
        }
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                // Pipes nobody reads, rather than this terminal: the background copy keeps its output open, and a
                // shared stdout would make "path=$(squaresnap capture)" wait until the clipboard changes.
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string a in cmd.Arguments) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new InvalidOperationException($"{cmd.Program} didn't start.");
            p.StandardInput.BaseStream.Write(png);
            p.StandardInput.Close();
            // The foreground part exits once the background copy has the data.
            if (!p.WaitForExit(10_000))
            {
                LastProblem = $"{cmd.Program} didn't finish within 10 seconds.";
                log.Warning($"Clipboard: {LastProblem}");
                return false;
            }
            if (p.ExitCode != 0)
            {
                LastProblem = $"{cmd.Program} exited with code {p.ExitCode}.";
                log.Warning($"Clipboard: {LastProblem}");
                return false;
            }
            log.Info($"Clipboard: copied {png.Length:N0} bytes of PNG with {cmd}.");
            LastProblem = null;
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            LastProblem = $"{cmd.Program} couldn't run ({ex.Message}).";
            log.Warning($"Clipboard: {LastProblem}");
            return false;
        }
    }
}
