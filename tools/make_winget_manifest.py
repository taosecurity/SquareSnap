"""Writes the WinGet manifests for taosecurity.SquareSnap (step 6, 2026-11-14; BejView's script from its step 5): a
DRAFT for microsoft/winget-pkgs.

Run (after tools\\build_release.ps1):  python -I tools\\make_winget_manifest.py [--check-url]

It installs the GitHub release's SquareSnap-<version>-win-x64-winget.zip as a portable app with the command "squaresnap".
That zip holds SquareSnap.exe and the terminal helper as cli\\squaresnap.exe. The helper's file name equals the command on
purpose: WinGet makes a hard link named after the command next to the file it points at, so a command "squaresnap"
pointing at SquareSnap.com would be a link called squaresnap.exe, which on Windows is SquareSnap.exe, and WinGet would replace
the window program with the helper without a warning (winget-cli PortableFlow.cpp, read for BejView 2026-11-13).

The version comes from Directory.Build.props and the SHA-256 from dist\\SHA256SUMS. Output:
dist\\winget\\manifests\\t\\taosecurity\\SquareSnap\\<version>\\ (version, installer and en-US locale files), then it runs
`winget validate` on them. --check-url also downloads the release asset and checks its SHA-256 matches (only works
once the GitHub release is published).
Sending the manifests to microsoft/winget-pkgs (wingetcreate submit, a pull request) needs the user's OK.
"""
import datetime
import hashlib
import os
import re
import shutil
import subprocess
import sys
import urllib.request

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
ID = "taosecurity.SquareSnap"
REPO = "https://github.com/taosecurity/SquareSnap"
SCHEMA = "1.28.0"  # the current winget-pkgs manifest schema (wingetcreate 1.28.1.0 writes it, 2026-11-11)


def version() -> str:
    with open(os.path.join(ROOT, "Directory.Build.props"), encoding="utf-8") as f:
        m = re.search(r"<Version>([^<]+)</Version>", f.read())
    if not m:
        raise SystemExit("Directory.Build.props has no <Version>.")
    return m.group(1)


def header(kind: str) -> str:
    return (f"# Draft made by SquareSnap's tools/make_winget_manifest.py; check it with winget validate before a pull request.\n"
            f"# yaml-language-server: $schema=https://aka.ms/winget-manifest.{kind}.{SCHEMA}.schema.json\n\n")


def files(ver: str, sha: str, today: str) -> dict[str, str]:
    url = f"{REPO}/releases/download/v{ver}/SquareSnap-{ver}-win-x64-winget.zip"
    return {
        f"{ID}.yaml": header("version") + f"""PackageIdentifier: {ID}
PackageVersion: {ver}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: {SCHEMA}
""",
        f"{ID}.installer.yaml": header("installer") + f"""PackageIdentifier: {ID}
PackageVersion: {ver}
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
- RelativeFilePath: cli\\squaresnap.exe
  PortableCommandAlias: squaresnap
MinimumOSVersion: 10.0.19041.0
ReleaseDate: {today}
Installers:
- Architecture: x64
  InstallerUrl: {url}
  InstallerSha256: {sha.upper()}
ManifestType: installer
ManifestVersion: {SCHEMA}
""",
        f"{ID}.locale.en-US.yaml": header("defaultLocale") + f"""PackageIdentifier: {ID}
PackageVersion: {ver}
PackageLocale: en-US
Publisher: Richard Bejtlich
PublisherUrl: https://github.com/taosecurity
PublisherSupportUrl: {REPO}/issues
Author: Richard Bejtlich
PackageName: SquareSnap
PackageUrl: {REPO}
License: MIT
LicenseUrl: {REPO}/blob/main/LICENSE
Copyright: Copyright (c) Richard Bejtlich
ShortDescription: Square (1:1) screenshots on any monitor, copied and saved in one drag.
Description: |-
  SquareSnap takes square screenshots. Drag on any monitor and the square follows the longer side of the drag; the
  capture is copied to the clipboard and can be saved automatically, at its own size or scaled to an exact size
  (256, 512, 1024, 1080, 2048). It has a global hotkey (Ctrl+Alt+S), a tray icon, fine-tuning with the arrow keys,
  a fixed-size mode, "repeat last square", a delay timer, and PNG, JPEG or WebP output. The "squaresnap capture"
  command takes a capture from a terminal or script and prints the saved file's path.
Moniker: squaresnap
Tags:
- screenshot
- screen-capture
- square
- clipboard
- hotkey
ReleaseNotesUrl: {REPO}/releases/tag/v{ver}
ManifestType: defaultLocale
ManifestVersion: {SCHEMA}
""",
    }


def main() -> int:
    check_url = "--check-url" in sys.argv[1:]
    if any(a != "--check-url" for a in sys.argv[1:]):
        print(__doc__)
        return 2
    ver = version()
    zip_name = f"SquareSnap-{ver}-win-x64-winget.zip"
    sums = os.path.join(ROOT, "dist", "SHA256SUMS")
    sha = None
    if os.path.isfile(sums):
        for line in open(sums, encoding="ascii"):
            h, _, name = line.strip().partition("  ")
            if name == zip_name:
                sha = h
    if not sha:
        raise SystemExit(f"{zip_name} isn't in {sums}. Run tools\\build_release.ps1 first.")
    if check_url:
        url = f"{REPO}/releases/download/v{ver}/{zip_name}"
        print(f"downloading {url}")
        with urllib.request.urlopen(url) as r:
            got = hashlib.sha256(r.read()).hexdigest()
        if got != sha:
            raise SystemExit(f"The release asset's SHA-256 is {got}, but dist\\SHA256SUMS says {sha}.")
        print("the release asset matches dist\\SHA256SUMS")
    out = os.path.join(ROOT, "dist", "winget", "manifests", "t", "taosecurity", "SquareSnap", ver)
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out)
    for name, text in files(ver, sha, datetime.date.today().isoformat()).items():
        with open(os.path.join(out, name), "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        print(f"wrote {os.path.join(out, name)}")
    r = subprocess.run(["winget", "validate", "--manifest", out], capture_output=True, text=True)
    print((r.stdout + r.stderr).strip())
    return 0 if r.returncode == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
