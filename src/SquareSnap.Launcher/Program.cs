using System.Runtime.InteropServices;

namespace SquareSnap.Launcher;

/// <summary>
/// SquareSnap.com, the terminal helper. It starts SquareSnap.exe from its own folder with the same command line,
/// hands it this terminal's input and output, waits for it, and returns its exit code, so the prompt comes back
/// after SquareSnap's text instead of before it. With no arguments it only opens the window and returns at once.
/// </summary>
internal static partial class Program
{
    /// <summary>Same value as SquareSnap.Core.Cli.ExitCodes.StartFailed (a test checks they match).</summary>
    internal const int StartFailed = 5;
    internal const string TargetName = "SquareSnap.exe";

    private static int Main(string[] args)
    {
        string self = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SquareSnap.com");
        string? target = FindTarget(self, File.Exists, RealPath);
        if (target == null)
        {
            Console.Error.WriteLine($"SquareSnap: can't find {TargetName} in {Path.GetDirectoryName(self)}. Keep SquareSnap.com and SquareSnap.exe in the same folder.");
            return StartFailed;
        }

        string commandLine = $"\"{target}\"{ArgumentsPart(Environment.CommandLine)}";
        bool wait = args.Length > 0; // plain "SquareSnap" just opens the window; don't hold the terminal while it's open
        if (!Native.Start(target, commandLine, wait, out int exitCode, out int error))
        {
            Console.Error.WriteLine($"SquareSnap: couldn't start {target} (Windows error {error}).");
            return StartFailed;
        }
        return exitCode;
    }

    /// <summary>
    /// Where SquareSnap.exe is (step 6, 2026-11-14, from BejView's step 5). The helper may be started as itself (the
    /// zip: SquareSnap.com next to SquareSnap.exe), through a link (WinGet's Links\squaresnap.exe symlink, which points
    /// at cli\squaresnap.exe in the WinGet zip, so SquareSnap.exe is one folder up), or through the Store's app
    /// execution alias. So it looks next to the path it was started as, then next to the real file behind any link,
    /// then one folder up from each. A candidate that is really this program is skipped, so a broken install can't make
    /// the helper start itself over and over.
    /// </summary>
    internal static string? FindTarget(string self, Func<string, bool> exists, Func<string, string> realPath)
    {
        string realSelf = realPath(self);
        string?[] near = [Path.GetDirectoryName(self), Path.GetDirectoryName(realSelf)];
        string?[] up = [.. near.Select(d => string.IsNullOrEmpty(d) ? null : Path.GetDirectoryName(d))];
        foreach (string dir in near.Concat(up).OfType<string>().Where(d => d.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string candidate = Path.Combine(dir, TargetName);
            if (!exists(candidate)) continue;
            if (string.Equals(realPath(candidate), realSelf, StringComparison.OrdinalIgnoreCase)) continue;
            return candidate;
        }
        return null;
    }

    /// <summary>The file behind a symbolic link (or the path itself); never throws.</summary>
    private static string RealPath(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    /// <summary>
    /// The arguments part of a Windows command line, exactly as typed (with its leading space), so quoting survives
    /// unchanged. The program name ends at its closing quote if it starts with one, otherwise at the first space or tab.
    /// </summary>
    internal static string ArgumentsPart(string commandLine)
    {
        int i;
        if (commandLine.StartsWith('"'))
        {
            int close = commandLine.IndexOf('"', 1);
            i = close < 0 ? commandLine.Length : close + 1;
        }
        else
        {
            i = commandLine.IndexOfAny([' ', '\t']);
            if (i < 0) i = commandLine.Length;
        }
        string rest = commandLine[i..].TrimStart(' ', '\t');
        return rest.Length == 0 ? "" : " " + rest;
    }

    private static partial class Native
    {
        private const int StdInput = -10, StdOutput = -11, StdError = -12;
        private const uint StartfUseStdHandles = 0x100;
        private const uint Infinite = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfo
        {
            public int cb;
            public nint lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
            public uint dwFlags;
            public short wShowWindow, cbReserved2;
            public nint lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public nint hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CreateProcess(string application, char[] commandLine, nint processAttributes, nint threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, nint environment, string? currentDirectory,
            ref StartupInfo startupInfo, out ProcessInformation processInformation);

        [LibraryImport("kernel32.dll")]
        private static partial nint GetStdHandle(int which);

        [LibraryImport("kernel32.dll")]
        private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetExitCodeProcess(nint process, out int exitCode);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);

        /// <summary>Starts the program with this process's standard handles (terminal, file or pipe) and optionally waits.</summary>
        public static bool Start(string application, string commandLine, bool wait, out int exitCode, out int error)
        {
            exitCode = 0;
            var si = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                dwFlags = StartfUseStdHandles,
                hStdInput = GetStdHandle(StdInput),
                hStdOutput = GetStdHandle(StdOutput),
                hStdError = GetStdHandle(StdError),
            };
            char[] cmd = (commandLine + '\0').ToCharArray(); // CreateProcessW may write to this buffer
            if (!CreateProcess(application, cmd, 0, 0, true, 0, 0, null, ref si, out var pi))
            {
                error = Marshal.GetLastPInvokeError();
                return false;
            }
            error = 0;
            CloseHandle(pi.hThread);
            if (wait)
            {
                WaitForSingleObject(pi.hProcess, Infinite);
                GetExitCodeProcess(pi.hProcess, out exitCode);
            }
            CloseHandle(pi.hProcess);
            return true;
        }
    }
}
