"""Builds dist\\SquareSnap-<version>-linux-<arch>.tar.gz from a linux single-file publish (step 4, 2026-11-13).

Run by tools\\build_release.ps1; by hand:  python -I tools\\make_linux_tar.py <publish folder> <arch>
The archive holds a folder SquareSnap-<version>-linux-<arch>/ with:
  squaresnap (mode 755), install.sh (755), squaresnap.desktop, KDE-SHORTCUTS.txt,
  icons/hicolor/<n>x<n>/apps/squaresnap.png, README.md, AGENTS.md, CHANGELOG.md and LICENSE.txt.
It's written with Python's tarfile (BejView's make_tar), so the program and the script are executable once unpacked
on Linux even though the archive is made on Windows, and text files get LF line endings. Files are owned by 0/0.
"""
import hashlib
import io
import os
import re
import sys
import tarfile
import time

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))


def version() -> str:
    with open(os.path.join(ROOT, "Directory.Build.props"), encoding="utf-8") as f:
        m = re.search(r"<Version>([^<]+)</Version>", f.read())
    if not m:
        raise SystemExit("Directory.Build.props has no <Version>.")
    return m.group(1)


def make_tar(publish: str, arch: str) -> str:
    ver = version()
    folder = f"SquareSnap-{ver}-linux-{arch}"
    out = os.path.join(ROOT, "dist", folder + ".tar.gz")
    linux = os.path.join(ROOT, "packaging", "linux")
    files = [
        (os.path.join(publish, "SquareSnap"), "squaresnap", 0o755),
        (os.path.join(linux, "install.sh"), "install.sh", 0o755),
        (os.path.join(linux, "squaresnap.desktop"), "squaresnap.desktop", 0o644),
        (os.path.join(linux, "KDE-SHORTCUTS.txt"), "KDE-SHORTCUTS.txt", 0o644),  # since beta 14 (the user's request)
        (os.path.join(ROOT, "LICENSE"), "LICENSE.txt", 0o644),
    ]
    # The documents in every package (step 7, 2026-11-16: README, AGENTS.md and CHANGELOG replace beta/README-beta.txt).
    files += [(os.path.join(ROOT, n), n, 0o644) for n in ("README.md", "AGENTS.md", "CHANGELOG.md")]
    for dirpath, _, names in sorted(os.walk(os.path.join(linux, "icons"))):
        for n in sorted(names):
            src = os.path.join(dirpath, n)
            files.append((src, os.path.relpath(src, linux).replace(os.sep, "/"), 0o644))
    for src, _, _ in files:
        if not os.path.isfile(src):
            raise SystemExit(f"missing: {src}")
    now = int(time.time())
    if os.path.exists(out):
        os.remove(out)
    with tarfile.open(out, "w:gz", compresslevel=9) as t:
        made = set()

        def add_dir(path: str) -> None:
            if path in made:
                return
            parent = os.path.dirname(path)
            if parent:
                add_dir(parent)
            d = tarfile.TarInfo(path)
            d.type, d.mode, d.mtime = tarfile.DIRTYPE, 0o755, now
            t.addfile(d)
            made.add(path)

        add_dir(folder)
        for src, name, mode in files:
            add_dir(os.path.dirname(f"{folder}/{name}"))
            with open(src, "rb") as f:
                data = f.read()
            if name.endswith((".txt", ".sh", ".desktop")):
                data = data.replace(b"\r\n", b"\n")  # Linux line endings (a CRLF script won't run)
            info = tarfile.TarInfo(f"{folder}/{name}")
            info.size, info.mode, info.mtime = len(data), mode, now
            t.addfile(info, io.BytesIO(data))
    with tarfile.open(out) as t:
        for m in t.getmembers():
            if not m.isdir():
                print(f"  {m.size:>12,}  {oct(m.mode)[2:]}  {m.name}")
    with open(out, "rb") as f:
        sha = hashlib.sha256(f.read()).hexdigest()
    print(f"Wrote {out} ({os.path.getsize(out):,} bytes), SHA-256 {sha}")
    return out


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: make_linux_tar.py <publish folder> <arch>   (arch: x64 or arm64)")
        return 2
    make_tar(sys.argv[1], sys.argv[2])
    return 0


if __name__ == "__main__":
    sys.exit(main())
