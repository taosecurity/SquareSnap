#!/bin/bash
# Checks install.sh from the linux-x64 tar.gz with a throwaway HOME (nothing of the real account is touched):
#   wsl -d Debian -- bash /mnt/c/path/to/SquareSnap/tools/wsl_install_check.sh <version>
set -u
ver="$1"
repo="$(cd "$(dirname "$0")/.." && pwd)"
work=$(mktemp -d)
tar -xzf "$repo/dist/SquareSnap-$ver-linux-x64.tar.gz" -C "$work"
cd "$work/SquareSnap-$ver-linux-x64"
export HOME="$work/home" XDG_DATA_HOME= XDG_BIN_HOME=
mkdir -p "$HOME"
echo "### install"; ./install.sh; echo "exit $?"
echo "### Exec lines"; grep '^Exec=' "$HOME/.local/share/applications/squaresnap.desktop"
echo "### icons: $(find "$HOME/.local/share/icons" -name squaresnap.png | wc -l)"
echo "### installed program runs"; "$HOME/.local/bin/squaresnap" --version
echo "### bad option"; ./install.sh --nope; echo "exit $? (2 expected)"
echo "### uninstall"; ./install.sh --uninstall; echo "exit $?"
echo "### left behind: $(find "$HOME/.local" -type f | wc -l) files"
rm -rf "$work"
