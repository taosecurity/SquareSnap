# SquareSnap: instructions for AI assistants

These steps let an assistant install SquareSnap and use it for a person. Every command below was run once before the 2.0.0 release and checked again for 2.0.1 and 2.0.2; the results are noted where a command needs a desktop.

## 1. What it is for

SquareSnap takes square (1:1) screenshots on Windows and Linux and saves them at an exact size (for example 512 x 512) as PNG, JPEG or WebP, or copies them to the clipboard. Use it when someone needs a square image of part of their screen: avatars, thumbnails, icons, input for image tools. It has a window, a global hotkey and terminal commands for scripts. Under WSL it captures the Windows desktop.

Don't use it for rectangular screenshots, screen recording, or on a machine with no desktop session: capturing needs a real, unlocked screen. A capture with a drag needs a person at the mouse; `--region` and `--last` capture without one.

## 2. Prerequisites

- Windows 10 version 2004 (build 19041) or later, x64; or Linux x64 or arm64 with a desktop session (made for KDE Plasma 6 on Wayland; GNOME and X11 work through fallbacks).
- Nothing else on Windows: each download carries its own .NET 10 runtime.
- Linux: the desktop's usual libraries, plus `wl-clipboard` (Wayland) or `xclip` (X11) for the clipboard. A minimal system needs the packages listed in README.md, "Linux details"; SquareSnap names any that is missing.
- To build from source instead: the .NET 10 SDK (see DEVELOPMENT.md).

## 3. Install

Windows, any one of these:

```powershell
# a) The release zip, extracted to a folder of your choice
Invoke-WebRequest https://github.com/taosecurity/SquareSnap/releases/download/v2.0.2/SquareSnap-2.0.2-win-x64.zip -OutFile SquareSnap.zip
Expand-Archive SquareSnap.zip -DestinationPath "$env:LOCALAPPDATA\SquareSnap-app"
# b) WinGet: the taosecurity.SquareSnap package is still waiting for review (checked 2026-11-20); once accepted it adds the "squaresnap" command to PATH
winget install taosecurity.SquareSnap
# c) The Microsoft Store version (2.0.2, live in the Store since 2026-11-17)
winget install --id 9P7RXW6FNH5D --source msstore
```

Linux:

```sh
curl -LO https://github.com/taosecurity/SquareSnap/releases/download/v2.0.2/SquareSnap-2.0.2-linux-x64.tar.gz
tar -xzf SquareSnap-2.0.2-linux-x64.tar.gz
cd SquareSnap-2.0.2-linux-x64
./install.sh            # optional: copies squaresnap to ~/.local/bin and adds a menu entry (no sudo); needed for the Plasma hotkey
```

On Windows, call the zip's terminal helper as `SquareSnap` (in PowerShell, `.\SquareSnap` or the full path to `SquareSnap.com`), never `SquareSnap.exe`: the helper waits for the answer and passes the exit code back; `SquareSnap.exe` returns at once and its text lands after the prompt. WinGet and the Store add `squaresnap`, which does the same.

## 4. Prove the install works

```powershell
& "$env:LOCALAPPDATA\SquareSnap-app\SquareSnap.com" --version     # Windows zip: prints "SquareSnap 2.0.2", exit 0
```

```sh
./squaresnap --version                                              # Linux: prints "SquareSnap 2.0.2", exit 0
```

## 5. Common tasks

Below, `squaresnap` stands for the command from step 3 (`.\SquareSnap` in the zip folder on Windows, `./squaresnap` on Linux).

```text
squaresnap                                         open the window (returns the prompt at once on Windows)
squaresnap capture                                 the person drags a square; prints the saved file's path
squaresnap capture --size 512 --out DIR            save at 512 x 512 into DIR (created if missing)
squaresnap capture --region 100,100,256,256        capture this exact square (screen pixels), no overlay, no person needed
squaresnap capture --last                          the same square as the last capture, no overlay
squaresnap capture --format jpg --no-clipboard     JPEG, and leave the clipboard alone
squaresnap capture --name "shot-{n:000}"           file name template: {date} {time} {size} {n:000}
squaresnap capture --delay 5                       wait 5 seconds before freezing the screen
squaresnap paths                                   where the settings, the log and captures go
squaresnap --help                                  every option
```

Each capture option overrides the saved setting for that run only. `--size` takes 16 to 16384 or `native`.

## 6. Inputs and outputs

- Input: the screen (a dragged square, or `--region X,Y,SIZE,SIZE` in physical screen pixels; the two sizes must be equal).
- `capture` always saves a file and prints its full path on standard output (under WSL, as a `/mnt/...` path); it also copies the image to the clipboard unless `--no-clipboard` (or the saved setting) says not to. Errors go to standard error, start with `SquareSnap:`, and say what to do. If saving works but copying fails, the exit code is still 0 and a `SquareSnap:` line on standard error says why.
- Files are written with a new name each time (the `{n:000}` counter restarts each day when the template has `{date}`, as the default does); SquareSnap never overwrites an existing file.
- SquareSnap writes only its captures, its settings file and its log (below).

## 7. Exit codes

| Code | Meaning |
|---|---|
| 0 | Saved or copied |
| 1 | Canceled (Esc or right-click in the overlay) |
| 2 | Bad arguments (the message names the option), or `--last` before any square was captured |
| 3 | The capture failed (for example, no desktop session; under WSL, no Windows `SquareSnap.exe` found) |
| 4 | The file couldn't be saved |
| 5 | Windows: `SquareSnap.com` couldn't find `SquareSnap.exe` (keep them in one folder). Linux: a system library is missing (the message names the package) |

## 8. The log

- Windows zip and WinGet: `%APPDATA%\SquareSnap\squaresnap.log`. Store version: under `%LOCALAPPDATA%\Packages\RichardBejtlich.SquareSnap_kz4qzpv342p6y\LocalCache\Roaming\SquareSnap`. Linux: `~/.config/SquareSnap/squaresnap.log`.
- `squaresnap paths` prints the exact location.
- `-v` / `--verbose` logs every step (what was found, what was decided, why something failed) and also prints it to the terminal. `--log FILE` writes the log to FILE instead.

## 9. Limits

- It never uploads or sends anything: no network access, no telemetry, no self-update. It never changes or deletes existing files.
- Capturing needs a desktop session. From a service, a scheduled task with no logged-on user, SSH without a desktop, or a locked screen, `capture` fails with exit code 3.
- On Linux under native Wayland the capture overlay appears on one monitor (the one the desktop picks, normally the one with the mouse); `SQUARESNAP_X11=1` covers every monitor. The first portal capture may show a permission dialog the person must accept.
- Under WSL, Linux can't read WSLg's screen, so `squaresnap` runs the Windows `SquareSnap.exe` (found through `$SQUARESNAP_WINDOWS_EXE`, next to `squaresnap`, or the Windows PATH). `--wslg` stays in Linux and can't capture.
- The global hotkey on KDE Plasma works only after `./install.sh`; Plasma owns the key binding (System Settings > Keyboard > Shortcuts > SquareSnap).
