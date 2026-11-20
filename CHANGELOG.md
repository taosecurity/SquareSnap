# Changelog

## 2.0.2 (2026-11-16)

### What's new

SquareSnap 2.0.2 is a small fix release. Alt+F4 on the capture overlay now cancels the capture, as Esc does. Everything else works as in 2.0.1.

### Example

Press Ctrl+Alt+S (or click **Capture square**), drag a square and let go. The picture is on the clipboard, ready to paste with Ctrl+V. Press Alt+F4 or Esc instead to cancel. From a terminal:

    > .\SquareSnap --version
    SquareSnap 2.0.2

### Fixes

- Alt+F4 on the capture overlay cancels the capture, as Esc does. Before, the overlay ignored it.
- 2.0.1's fixes (from a code review) are listed in its release notes.

### Download

- Windows (x64): `SquareSnap-2.0.2-win-x64.zip`. Extract it anywhere and run `SquareSnap.exe`. The `-winget.zip` is the same build laid out for WinGet.
- Linux: `SquareSnap-2.0.2-linux-x64.tar.gz` or `-linux-arm64.tar.gz`, then run `./squaresnap`.

To check a download, run `sha256sum -c SHA256SUMS --ignore-missing` (Linux) or `Get-FileHash <file>` (Windows, then compare with SHA256SUMS), and `gh attestation verify <file> -R taosecurity/SquareSnap`. The Windows exe isn't code-signed, but the Microsoft Store version is.

## 2.0.1 (2026-11-16)

Bug fixes from a code review of the 2.0.0 source.

- Closing the capture overlay with Alt+F4 (or by the window manager) cancels the capture. Before, SquareSnap stayed busy and ignored every later capture until it was restarted, and `squaresnap capture` never exited.
- A hotkey of `F` or `Shift+F` is refused (it would fire on every capital F); F1-F24 alone still work.
- Under WSL, a Windows path for `--out` or `--log` (such as `C:\Users\me\Pictures`) is passed on as given instead of being joined onto the Linux folder.
- Two SquareSnaps (the tray and a `squaresnap capture`) no longer overwrite each other's settings: a capture run changes only the last square, and the tray keeps a newer last square saved by the other run.
- A log over 1 MB is moved aside to `squaresnap.log.1` instead of being rewritten, which broke the log of another running SquareSnap.
- A read-only settings folder holding an old or damaged settings.json no longer stops SquareSnap from starting.
- A save folder that can't be listed (a dropped network share) gives the save-folder message instead of "Capture failed".
- Each capture is resized once for the preview and the clipboard, not twice.
- The help now says the `{n}` counter restarts each day when the template has `{date}` (as the default does).

## 2.0.0 (2026-11-16)

A rewrite of SquareSnap on .NET 10 and Avalonia, for Windows and Linux. It replaces SquareSnap 1.0 (Windows) and BejSnap 1.0 (Linux), and takes over their save folder the first time it starts.

**Capture**
- Every monitor on Windows at its real resolution, with mixed scaling handled; a capture matches 1.0's pixel for pixel.
- A magnifier shows the exact pixel under the mouse; right-click or Esc cancels.
- Fixed-size squares that follow the mouse (the wheel resizes), Repeat last (Ctrl+R), fine-tune with the arrow keys, and a delay of up to 60 seconds for menus and tooltips.
- A global hotkey (Ctrl+Alt+S, changeable) and a tray icon; SquareSnap can keep running in the tray when its window is closed.

**Output**
- Every capture goes to the clipboard by default; Ctrl+S saves it, and a banner shows where with an Open folder button. Auto-save is an option.
- Exact output sizes (256, 512, 1024, 1080, 2048 or native; any size from 16 to 16384 from the terminal), PNG, JPEG or WebP, and file name templates. Files are never overwritten.
- 1.0's light-gray border is now optional and off by default.

**New: terminal commands**
- `squaresnap capture` with `--size`, `--out`, `--name`, `--format`, `--clipboard`/`--no-clipboard`, `--delay`, `--last`, `--fixed`, `--region` and `--fine-tune`; `squaresnap paths`. It prints the saved file's path and returns exit codes for scripts (README, "From a terminal").
- On Windows, the helper `SquareSnap.com` makes the terminal wait for the answer.

**New: Linux and WSL**
- A Linux version (x64 and arm64), made for KDE Plasma 6 on Wayland: native Wayland windows, capture through the desktop's screenshot portal (with spectacle, grim and gnome-screenshot as fallbacks), the hotkey through Plasma's global shortcuts, a tray icon, and a clipboard that keeps the picture after SquareSnap exits. `install.sh` adds a menu entry and icons without sudo.
- Under WSL, `squaresnap` runs the Windows SquareSnap, so it captures the Windows desktop and prints the file as a `/mnt/...` path.

**New: packages**
- Windows zip, a WinGet package, and a Microsoft Store version with the `squaresnap` command.
- Every release has SHA-256 hashes and a GitHub build attestation.

**Troubleshooting**
- Every start and every problem is logged; `--verbose` logs every step and `--log FILE` picks the file. Help > Open the log file and `squaresnap paths` show where it is.

## 1.0.0 (2025)

The first version: a WPF app for Windows on .NET 6 that captured a square region, previewed it and saved it as PNG, with dark mode and automatic file names. Its code is kept unchanged in `legacy/wpf-1.0`, and tag `1.0.0` has it at its original paths.
