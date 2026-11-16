# SquareSnap

**Square (1:1) screenshots on Windows and Linux.** Drag on the screen and SquareSnap keeps the selection perfectly square, then copies it to the clipboard or saves it at an exact size such as 512 x 512 or 1080 x 1080. That's handy for profile pictures, thumbnails, icons, social posts and image tools that want square input.

It works from a window, a global hotkey (Ctrl+Alt+S), the tray, or a terminal command for scripts. On Windows it captures every monitor at its real resolution; on Linux it is made for KDE Plasma on Wayland; under WSL it captures the Windows desktop.

For AI assistants: see [AGENTS.md](AGENTS.md).

## Try it

Windows: download `SquareSnap-2.0.2-win-x64.zip` from [Releases](https://github.com/taosecurity/SquareSnap/releases), extract it anywhere, and double-click `SquareSnap.exe`. Click **Capture square**, drag a square, and paste it anywhere with Ctrl+V. Or, from PowerShell in that folder:

```powershell
.\SquareSnap --version
.\SquareSnap capture --size 512 --out $HOME\Desktop     # drag a square; prints the saved file
```

Linux:

```sh
tar -xzf SquareSnap-2.0.2-linux-x64.tar.gz
cd SquareSnap-2.0.2-linux-x64
./squaresnap --version
./install.sh          # adds it to your app menu (into ~/.local, no sudo); needed for the hotkey on Plasma
./squaresnap
```

Nothing else needs installing: each download carries its own .NET runtime.

## What it does

- **Always square.** The selection is held at 1:1 while you drag. A magnifier shows the exact pixel under the mouse. Right-click or Esc cancels.
- **Every monitor, real pixels.** On Windows the overlay covers all monitors, and mixed scaling (100%, 150%) is handled, so a 512-pixel square is 512 screen pixels.
- **Clipboard or file.** By default every capture goes to the clipboard. Ctrl+S saves it to `Pictures\SquareSnap` as `SquareCap-YYYYMMDD-001.png` (the number restarts each day), and a banner shows where, with an **Open folder** button. Options > Save every capture automatically skips the preview.
- **Exact output size:** native, 256, 512, 1024, 1080 or 2048 pixels (any size from 16 to 16384 from the terminal). PNG, JPEG or WebP.
- **Fixed-size square** (File > Capture fixed-size square): a square of a set size follows the mouse, the wheel resizes it, and a click captures.
- **Repeat last** (Ctrl+R, or the tray): the same square again, with no dragging. Useful for before-and-after shots.
- **Fine-tune** (Options): after the drag, the arrow keys move the square, Ctrl+arrows resize it, and Enter captures.
- **Delay** (Options): wait up to 60 seconds before freezing the screen, to catch menus and tooltips.
- **Global hotkey** Ctrl+Alt+S (changeable), a tray icon, and "keep running in the tray when closed".
- **Dark or light mode**, and an optional 1-pixel border like SquareSnap 1.0.
- **Terminal commands** for scripts, with exit codes (below).

Shortcuts in the window: Ctrl+N capture, Ctrl+R repeat last, Ctrl+S save, Ctrl+Shift+S save as, Ctrl+C copy.

## Install

| Where | How | Notes |
|---|---|---|
| Windows zip | Extract `SquareSnap-<version>-win-x64.zip` anywhere and run `SquareSnap.exe`. | Windows 10 (2004) or later, x64. The zip isn't signed, so the first start may show "Windows protected your PC": click **More info**, then **Run anyway**. |
| Microsoft Store | [SquareSnap in the Microsoft Store](https://apps.microsoft.com/detail/9P7RXW6FNH5D) (once Microsoft has certified it). | Signed by Microsoft; updates itself. Adds the `squaresnap` command to every terminal. |
| WinGet | `winget install taosecurity.SquareSnap` (once the package is accepted), or `winget install --id 9P7RXW6FNH5D --source msstore` for the Store version. | The WinGet package uses `SquareSnap-<version>-win-x64-winget.zip` and adds the `squaresnap` command. |
| Linux | Unpack `SquareSnap-<version>-linux-x64.tar.gz` (or `-linux-arm64`) and run `./squaresnap`. `./install.sh` puts `squaresnap` in `~/.local/bin` and adds the menu entry and icons under `~/.local/share` (no sudo); `./install.sh --uninstall` removes them. | Needs a desktop session. Made for KDE Plasma 6 on Wayland (tested on Debian 13 and CachyOS). GNOME and X11 work through fallbacks but are less tested; the arm64 build is untested. |

Every release lists SHA-256 hashes in `SHA256SUMS` and carries a GitHub build attestation. To check a download:

```sh
sha256sum -c SHA256SUMS --ignore-missing         # Linux
gh attestation verify SquareSnap-2.0.2-linux-x64.tar.gz -R taosecurity/SquareSnap
```

On Windows, compare `Get-FileHash SquareSnap-2.0.2-win-x64.zip` with the line in `SHA256SUMS`.

SquareSnap 2.0 replaces SquareSnap 1.0 (Windows) and BejSnap 1.0 (Linux). The first time it starts, it takes over 1.0's save folder (and on Linux BejSnap's save folder and theme).

**Windows 11 hides new tray icons.** Click the ^ next to the clock to find SquareSnap's, or turn it on in Settings > Personalization > Taskbar > Other system tray icons.

## From a terminal

On Windows, type `SquareSnap` without `.exe` (in PowerShell from its folder: `.\SquareSnap`). That runs `SquareSnap.com`, a small helper that waits for SquareSnap and passes its text and exit code back to the terminal. Keep `SquareSnap.com` next to `SquareSnap.exe`. The Store and WinGet versions give you `squaresnap` everywhere. On Linux, it's `./squaresnap` (or `squaresnap` after `install.sh`).

```text
squaresnap                                   open the window
squaresnap capture                           drag a square, save it, print the file's path
squaresnap capture --size 1080 --out D:\Shots
squaresnap capture --format jpg --no-clipboard
squaresnap capture --last                    the same square as last time, no dragging
squaresnap capture --region 100,100,512,512  this exact square, no overlay
squaresnap capture --fixed 256               click to place a 256 x 256 square
squaresnap capture --delay 3                 wait 3 seconds first
squaresnap paths                             where the settings, the log and captures go
squaresnap --help                            every option
```

More capture options: `--name TEMPLATE` (`{date} {time} {size} {n:000}`; default `SquareCap-{date}-{n:000}`), `--clipboard`, `--fine-tune` / `--no-fine-tune`. Each overrides the saved setting for that run only.

Exit codes:

| Code | Meaning |
|---|---|
| 0 | Saved or copied |
| 1 | Cancelled (Esc or right-click) |
| 2 | Bad arguments (the message names the option) |
| 3 | The capture failed |
| 4 | The file couldn't be saved |
| 5 | Couldn't start: `SquareSnap.com` couldn't find `SquareSnap.exe` (keep the two files together), or on Linux a system library is missing (the message names the package to install) |

## Linux details

**System libraries.** A desktop already has them. A minimal system, such as a new WSL distribution, may not; SquareSnap then stops with a message naming the missing library and its package (exit code 5). On Debian or Ubuntu:

```sh
sudo apt install libfontconfig1 libx11-6 libice6 libsm6 libxrandr2 libxi6 libxcursor1 libxkbcommon0 libwayland-client0 wl-clipboard
```

On Fedora: `fontconfig libX11 libICE libSM libXrandr libXi libXcursor libxkbcommon libwayland-client wl-clipboard`. On Arch and CachyOS: `fontconfig libx11 libice libsm libxrandr libxi libxcursor libxkbcommon wayland wl-clipboard`.

**How it captures.** On a Wayland desktop SquareSnap asks the screenshot portal (`xdg-desktop-portal-kde` on Plasma) for a picture of the screen. The first time, the desktop may ask whether SquareSnap may take screenshots: allow it. If the portal doesn't work, it tries `spectacle`, `grim` and `gnome-screenshot`, in that order. On an X11 session it reads the screen directly; X11 on a real Xorg desktop is untested. `SQUARESNAP_CAPTURE=portal|spectacle|grim|gnome-screenshot|x11` forces one method.

**Monitors on Wayland.** SquareSnap draws native Wayland windows (`SQUARESNAP_X11=1` uses X11 instead). Wayland doesn't let programs place their windows, so the capture overlay appears on one monitor: the one the desktop picks, normally the one with the mouse. To capture another monitor, move the mouse there and start the capture again. With `SQUARESNAP_X11=1` the overlay covers every monitor.

**Hotkey on KDE Plasma** (more in `KDE-SHORTCUTS.txt` in the Linux download). Run `./install.sh` first: Plasma gives hotkeys only to installed apps. Then start SquareSnap from the app menu. It asks Plasma for Ctrl+Alt+S through the global shortcuts portal, and Plasma shows a dialog: accept it without pressing a key. To change the keys, open System Settings > Keyboard > Shortcuts > SquareSnap. You can also bind any key to the command `squaresnap capture` there (Add New > Command or Script).

**Clipboard.** `squaresnap capture` copies with `wl-copy` (from wl-clipboard, or `xclip` on X11), so the picture stays on the clipboard after SquareSnap exits. Without either it still saves the file and says what to install.

**Tray icon.** Native on KDE Plasma. GNOME needs the AppIndicator extension (for example "AppIndicator and KStatusNotifierItem Support"); without a tray, closing the window exits SquareSnap.

## WSL

In a WSL shell, `squaresnap` runs the Windows `SquareSnap.exe`, because Linux under WSL can only see WSLg's own windows, not the Windows desktop. `squaresnap capture --out /mnt/d/Shots` saves through Windows and prints the file as a `/mnt/...` path. WSLg's Linux windows are on the Windows desktop, so they're captured too. Paths are translated with `wslpath`, and the arguments go to Windows as a list, never through a shell. The bridge needs no extra Linux packages.

SquareSnap looks for the Windows `SquareSnap.exe` in `$SQUARESNAP_WINDOWS_EXE` first, then next to the Linux `squaresnap`, then in the Windows folders on PATH. For example, in `~/.bashrc`:

```sh
export SQUARESNAP_WINDOWS_EXE="/mnt/c/Tools/SquareSnap/SquareSnap.exe"
```

`--wslg` keeps SquareSnap in Linux, where it can't capture (WSLg doesn't let programs read its screen). Unpack the `.tar.gz` inside WSL with `tar`, not with Windows Explorer, so `squaresnap` keeps its execute permission.

## Settings and the log

| | Windows (zip and WinGet) | Windows (Store) | Linux |
|---|---|---|---|
| Settings | `%APPDATA%\SquareSnap\settings.json` | the Store's private copy: `%LOCALAPPDATA%\Packages\RichardBejtlich.SquareSnap_kz4qzpv342p6y\LocalCache\Roaming\SquareSnap` | `~/.config/SquareSnap/settings.json` |
| Log | `%APPDATA%\SquareSnap\squaresnap.log` | the same private folder | `~/.config/SquareSnap/squaresnap.log` |
| Captures | `Pictures\SquareSnap` | `Pictures\SquareSnap` | `~/Pictures/SquareSnap` |

`squaresnap paths` prints the exact places on your computer, and Help > Open the log file and Help > Open the settings folder open them. The Store version also uses files already in `%APPDATA%\SquareSnap` (from a zip install or 1.0) in place. Options > Save folder changes where captures go.

## If something goes wrong

Every start and every problem is written to the log. For a full trace of every step (what SquareSnap found, what it decided, and why something failed), start it with `--verbose`:

```powershell
.\SquareSnap --verbose                       # Windows: the log also scrolls past in the terminal
.\SquareSnap --verbose capture
```

```sh
./squaresnap --verbose                       # Linux
```

`--log FILE` writes the log somewhere else. Please attach the log when you [report a problem](https://github.com/taosecurity/SquareSnap/issues).

## Building from source

See [DEVELOPMENT.md](DEVELOPMENT.md). In short: .NET 10 SDK, then `dotnet test` and `tools\build_release.ps1`.

## Changes

See [CHANGELOG.md](CHANGELOG.md).

## Privacy

SquareSnap collects nothing, sends nothing and connects to nothing: [PRIVACY.md](PRIVACY.md).

## Security

What SquareSnap promises and how to report a problem privately: [SECURITY.md](SECURITY.md).

## License

MIT; see [LICENSE](LICENSE).

## Credits

I am the developer, and Claude is the programmer. Version 1.0 was programmed by Claude Sonnet 3.7, version 2.0 by Claude Opus 5.5. SquareSnap 2.0 uses [Avalonia](https://avaloniaui.net/) for its window.
