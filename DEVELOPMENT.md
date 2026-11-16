# Building and changing SquareSnap

SquareSnap 2.0 is C# on .NET 10 with [Avalonia](https://avaloniaui.net/) 12.1.4 for the window, so one code base runs on Windows and Linux.

## What you need

- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Python 3.12 or later for the packaging scripts in `tools/`.
- For the Windows packages: Visual Studio 2022 Build Tools with "Desktop development with C++" (the terminal helper is built with Native AOT, which needs its linker).
- For the Store package only: the Windows SDK (`makeappx`, `signtool`) and Pillow (`pip install pillow`).
- On Linux, for the UI tests: `fontconfig` and a font such as `fonts-dejavu-core`.

## Layout

| Folder | What it holds |
|---|---|
| `src/SquareSnap.Core` | Everything that isn't UI or capture: square geometry and DPI mapping, file naming, settings (with the 1.0 and BejSnap import), logging, the command line, platform paths, the Store package's paths and the WSL bridge. |
| `src/SquareSnap.Capture` | Screen capture, clipboard and hotkey for each platform: Windows (GDI, Win32 hotkey) and Linux (screenshot portal, spectacle/grim/gnome-screenshot, X11, the GlobalShortcuts portal, wl-copy/xclip). |
| `src/SquareSnap.App` | The Avalonia window (exe `SquareSnap`), the capture overlay with its magnifier, the tray, and the `capture` and `paths` commands. |
| `src/SquareSnap.Launcher` | The small terminal helper, shipped as `SquareSnap.com` (and as `cli\squaresnap.exe` in the WinGet zip). It starts `SquareSnap.exe` and waits for its text and exit code. |
| `tests/SquareSnap.Tests` | xUnit tests, including headless UI tests of the window and overlay. |
| `packaging/` | The Linux `.desktop` file, icons, `install.sh` and `KDE-SHORTCUTS.txt`; the MSIX manifest, its test script and the Store identity. |
| `tools/` | Packaging scripts (below), the pixel check against 1.0, and the WSL test helpers. |
| `legacy/wpf-1.0` | The 1.0 WPF app, kept unchanged and not built. Tag `1.0.0` has it at its original paths. |

## Build and test

From the repository root:

```sh
dotnet test tests/SquareSnap.Tests/SquareSnap.Tests.csproj
dotnet run --project src/SquareSnap.App -- --version
```

On Linux, set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` first (SquareSnap builds with invariant globalization). `tools/wsl_test.sh` runs the tests in WSL on a copy of the source.

Packages are pinned with lock files (`packages.lock.json`); CI restores with `--locked-mode`. Warnings are errors.

## Packages

```powershell
& tools\build_release.ps1                         # dist\: the Windows zip, the WinGet zip, the Linux .tar.gz files, SHA256SUMS
python -I tools\make_msix.py [--test-sign]        # dist\msix\: the Store package (after build_release.ps1)
python -I tools\make_winget_manifest.py           # dist\winget\: the WinGet manifests, checked with winget validate
```

The version comes from `<Version>` in `Directory.Build.props`. `build_release.ps1` runs on Windows and checks each helper and app pair before zipping it. `make_msix.py --test-sign` makes a test certificate the first time, in `$SQUARESNAP_TEST_CERT_DIR` or `%LOCALAPPDATA%\SquareSnap-dev\msix-test-cert` (`--cert-dir` picks another place); `packaging/msix/msix-test.ps1` installs and checks the test package.

`tools\pixelcheck.ps1` (also in the Windows zip) captures the same region with 1.0's method and with 2.0 and prints MATCH or DIFFERENT.

## Releases

`.github/workflows/ci.yml` tests on Windows and Linux on every push. Pushing a tag `v<version>` (matching `Directory.Build.props`) runs `release.yml`: it tests, builds the packages, attests their build provenance, and makes a **draft** GitHub release with the packages and `SHA256SUMS`. A person publishes the draft. The unsigned Store package is kept as a workflow artifact for Partner Center.

## Rules the code keeps

- A capture is saved only under a new name (never over an existing file), inside the chosen folder.
- Every helper program is started by its full path, never a bare name; the WSL bridge passes paths to `SquareSnap.exe` as an argument list, never through a shell.
- The saved image is exactly the square that was selected, in physical pixels, at the requested size.
- Every failure is logged; `--verbose` logs every step.
