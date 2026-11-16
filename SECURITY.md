# Security

## What SquareSnap promises

- **It captures only when you ask.** The screen is read only when you start a capture, and the picture goes only to the clipboard and/or a new file in the folder you chose.
- **It never overwrites a file.** Each capture is saved under a new name; a file name template can't write outside the save folder.
- **No network.** SquareSnap sends and downloads nothing and has no self-update. The only web link it opens is the README, from the Help menu, when you choose it.
- **No admin rights.** SquareSnap runs as you and needs no elevation; `install.sh` on Linux writes only under `~/.local`, with no sudo.
- **Helper programs are started by full path.** On Linux, `wl-copy`, `xclip`, `spectacle`, `grim`, `gnome-screenshot` and `wslpath` are found only in PATH's folders, never in the current folder. On Windows, Open folder runs `explorer.exe` from the Windows folder.
- **The WSL bridge never uses a shell.** Paths go to `wslpath` and to the Windows `SquareSnap.exe` as separate arguments, so a file name can't become a command.
- **The terminal helper starts only its own SquareSnap.exe**, by full path, from its install folder. It never searches the current folder.
- **Releases can be checked.** Every release lists SHA-256 hashes in `SHA256SUMS` and carries a GitHub build attestation (`gh attestation verify <file> -R taosecurity/SquareSnap`). The Microsoft Store version is signed by Microsoft.

## Supported versions

Only the latest release gets fixes. SquareSnap 1.0 is no longer supported: it ran on .NET 6, which Microsoft no longer supports, and 2.0 replaces it.

## Reporting a problem

Please report a security problem privately through GitHub: on the repository's **Security** tab, choose **Report a vulnerability**. Include the SquareSnap version (`squaresnap --version`), your system, and, if you can, the log from `--verbose` (README, "If something goes wrong").

Ordinary bugs go in [Issues](https://github.com/taosecurity/SquareSnap/issues).
