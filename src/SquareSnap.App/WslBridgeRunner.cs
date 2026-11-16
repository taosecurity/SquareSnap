using System.Diagnostics;
using SquareSnap.Core.Cli;
using SquareSnap.Core.Logging;
using SquareSnap.Core.Platform;

namespace SquareSnap.App;

/// <summary>
/// The side-effect half of the WSL bridge (step 5), after BejView's: gathers the facts (WSL, interop, mount root,
/// where SquareSnap.exe is), asks <see cref="WslBridge.Decide"/>, and runs the Windows exe when that's the answer.
/// The exe is started with an argument list, never through a shell. Every step is logged (Info, so --verbose
/// shows it); a bridge that was wanted but can't run is a Warning, printed even without --verbose.
/// </summary>
internal static class WslBridgeRunner
{
    /// <summary>Returns an exit code when the Windows exe took over, or null to carry on in Linux.</summary>
    public static int? TryBridge(CliOptions o, Logger log)
    {
        if (!OperatingSystem.IsLinux()) return null;

        var env = Gather(log);
        var decision = WslBridge.Decide(o, env, p => ToWindowsPath(p, log));
        log.Info($"WSL bridge: {decision.Reason}");
        if (decision.Warning != null)
        {
            log.Warning($"WSL bridge: {decision.Warning}");
            if (!log.Verbose) Console.Error.WriteLine($"SquareSnap: {decision.Warning}");
        }
        if (decision.Action != BridgeAction.UseWindowsExe) return null;
        return Launch(decision, env, log);
    }

    private static BridgeEnvironment Gather(Logger log)
    {
        bool isWsl = Wsl.IsWslNow();
        if (!isWsl) return new BridgeEnvironment { IsWsl = false };

        string? conf = ReadOrNull("/etc/wsl.conf");
        string root = Wsl.MountRootFromConf(conf);
        bool interop = Wsl.InteropEnabled(f => ReadOrNull(f)?.Split('\n')[0]);
        string? ownDir = Path.GetDirectoryName(Environment.ProcessPath);
        var (exe, source) = WslBridge.FindWindowsExe(Environment.GetEnvironmentVariable, File.Exists, ownDir, root);
        string? problem = WslBridge.ConfiguredExeProblem(Environment.GetEnvironmentVariable, File.Exists, root);
        if (problem != null) log.Warning($"WSL bridge: {problem}");

        log.Info($"WSL bridge: WSL {Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") ?? "(distro unknown)"}, interop {(interop ? "on" : "off")}, " +
                 $"drive mounts under {root}{(conf == null ? " (no /etc/wsl.conf)" : "")}, Windows {WslBridge.ExeName}: {(exe == null ? "not found" : $"{exe} ({source})")}.");
        return new BridgeEnvironment
        {
            IsWsl = true,
            InteropEnabled = interop,
            MountRoot = root,
            CurrentDirectory = Environment.CurrentDirectory,
            Home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            WindowsExe = exe,
            WindowsExeSource = source,
        };
    }

    /// <summary>wslpath knows the real mounts; the tested rules in <see cref="Wsl"/> are the fallback.</summary>
    private static string ToWindowsPath(string linuxPath, Logger log) =>
        RunWslPath("-w", linuxPath, log) ?? Wsl.ToWindowsPath(linuxPath, Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") ?? "",
            MountRoot.Value);

    private static string ToLinuxPath(string windowsPath, Logger log) =>
        RunWslPath("-u", windowsPath, log) ?? Wsl.ToLinuxPath(windowsPath, MountRoot.Value);

    /// <summary>/etc/wsl.conf's drive mount root, read once per run (it was read again for every path).</summary>
    private static readonly Lazy<string> MountRoot = new(() => Wsl.MountRootFromConf(ReadOrNull("/etc/wsl.conf")));

    private static string? RunWslPath(string flag, string path, Logger log)
    {
        try
        {
            // By full path from PATH's absolute folders: a bare name would also find a file named wslpath in the current
            // folder (security review, 2026-11-14).
            string? exe = LinuxPlan.FindOnPath("wslpath", Environment.GetEnvironmentVariable("PATH"), File.Exists);
            if (exe == null)
            {
                log.Info("WSL bridge: wslpath isn't on PATH; using SquareSnap's own rules.");
                return null;
            }
            var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add(flag);
            psi.ArgumentList.Add(path);
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd().Trim();
            string error = p.StandardError.ReadToEnd().Trim();
            if (p.WaitForExit(5000) && p.ExitCode == 0 && output.Length > 0)
            {
                log.Debug($"WSL bridge: wslpath {flag} {path} gave {output}.");
                return output;
            }
            log.Info($"WSL bridge: wslpath {flag} {path} failed (exit {(p.HasExited ? p.ExitCode : -1)}{(error.Length > 0 ? ": " + error : "")}); using SquareSnap's own rules.");
        }
        catch (Exception ex)
        {
            log.Info($"WSL bridge: couldn't run wslpath ({ex.Message}); using SquareSnap's own rules.");
        }
        return null;
    }

    private static int Launch(BridgeDecision d, BridgeEnvironment env, Logger log)
    {
        var psi = new ProcessStartInfo(d.WindowsExe!) { UseShellExecute = false };
        foreach (string a in d.WindowsArgs) psi.ArgumentList.Add(a);
        // Start in a Windows folder so the Windows program doesn't start in a \\wsl.localhost folder.
        string? exeDir = Path.GetDirectoryName(d.WindowsExe);
        if (Wsl.IsOnDriveMount(env.CurrentDirectory, env.MountRoot)) psi.WorkingDirectory = env.CurrentDirectory;
        else if (exeDir != null && Directory.Exists(exeDir)) psi.WorkingDirectory = exeDir;
        if (d.TranslateOutput) psi.RedirectStandardOutput = true;
        log.Info($"WSL bridge: starting {d.WindowsExe} {string.Join(' ', d.WindowsArgs.Select(Quote))}" +
                 (d.Wait ? " and waiting for it." : "; this terminal is free again at once."));
        try
        {
            using var p = Process.Start(psi);
            if (p == null) throw new InvalidOperationException("Process.Start returned nothing.");
            if (!d.Wait) return ExitCodes.Ok;
            if (d.TranslateOutput)
            {
                // The saved file's path comes back as C:\...; print it as /mnt/c/... so it works in this shell.
                string? line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    string shown = WslBridge.TranslateOutputLine(line, w => ToLinuxPath(w, log));
                    if (shown != line.TrimEnd('\r')) log.Info($"WSL bridge: {line.TrimEnd('\r')} is {shown} here.");
                    Console.WriteLine(shown);
                }
            }
            p.WaitForExit();
            log.Info($"WSL bridge: the Windows SquareSnap exited with code {p.ExitCode}.");
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            log.Error($"WSL bridge: couldn't start {d.WindowsExe}", ex);
            Console.Error.WriteLine($"SquareSnap: couldn't start the Windows SquareSnap ({d.WindowsExe}): {ex.Message}. " +
                                    "Check that the file exists and that WSL interop is on, or add --wslg to capture WSLg's windows.");
            return ExitCodes.StartFailed;
        }
    }

    private static string Quote(string a) => a.Contains(' ') || a.Length == 0 ? $"\"{a}\"" : a;

    private static string? ReadOrNull(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception) { return null; }
    }
}
