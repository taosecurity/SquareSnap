#!/bin/bash
# End-to-end check of the Linux build and the WSL bridge, run in WSL from Windows PowerShell:
#   wsl -d Debian -- bash /mnt/c/path/to/SquareSnap/tools/wsl_check.sh <version> <test-folder-on-mnt>
# It unpacks dist/SquareSnap-<version>-linux-x64.tar.gz into ~/sqs-check and runs the terminal commands, the
# missing-library message, a --wslg capture (X11, no overlay thanks to --region) and each bridge decision with
# --verbose. Needs dist/SquareSnap-<version>-win-x64/ (made by build_release.ps1). Windows SquareSnap processes it
# starts are left for the caller to stop (Get-Process SquareSnap | Stop-Process). From the ai account (session 0)
# the Windows capture itself fails with exit 3 (no desktop): the bridge's decisions and arguments are what's checked.
set -u
ver="$1"
testdir="$2"   # a /mnt/... folder, ideally with a space in its name
repo="$(cd "$(dirname "$0")/.." && pwd)"
tarball="$repo/dist/SquareSnap-$ver-linux-x64.tar.gz"
winexe="$repo/dist/SquareSnap-$ver-win-x64/SquareSnap.exe"

rm -rf ~/sqs-check && mkdir -p ~/sqs-check && tar -xzf "$tarball" -C ~/sqs-check
dir=~/sqs-check/SquareSnap-$ver-linux-x64
sq=$dir/squaresnap
ls -l "$dir"
mkdir -p "$testdir"
unset SQUARESNAP_WINDOWS_EXE
step() { echo; echo "### $*"; }

step "version, help (WSL and Linux lines), paths"
"$sq" --version; echo "exit $?"
"$sq" --help | sed -n '/WSL:/,/SQUARESNAP_CAPTURE/p'
"$sq" paths; echo "exit $?"
"$sq" --verbose version; echo "exit $? (options before the command)"
"$sq" capture --size huge; echo "exit $? (2 expected)"

# Since beta 14 --wslg stops at once with a plain message (WSLg's screen can't be read; beta 13's test 8.1), before any
# window or library is loaded, so the missing-library message can't be reached under WSL any more (unit tests cover it).
step "--wslg: one plain message, exit 3, no window"
mkdir -p ~/sqs-check/out
timeout 40 "$sq" capture --wslg --region 0,0,64,64 --out ~/sqs-check/out 2>&1 | head -3; echo "exit ${PIPESTATUS[0]} (3 expected)"

step "no SquareSnap.exe anywhere: warning, then the same plain message"
PATH=/usr/local/bin:/usr/bin:/bin timeout 40 "$sq" capture --region 0,0,32,32 --out ~/sqs-check/out 2>&1 | head -3; echo "exit ${PIPESTATUS[0]}"

step "a wrong SQUARESNAP_WINDOWS_EXE is reported"
SQUARESNAP_WINDOWS_EXE=/mnt/c/nope/SquareSnap.exe PATH=/usr/bin:/bin timeout 40 "$sq" capture --verbose --region 0,0,32,32 --out ~/sqs-check/out 2>&1 | grep -E "WSL bridge" | head -4

step "bridge: capture with paths goes to the Windows exe (session 0: the capture itself fails, exit 3)"
cd "$testdir"
SQUARESNAP_WINDOWS_EXE="$winexe" timeout 40 "$sq" capture --verbose --region 0,0,64,64 --out "$testdir/shots" --log "sq bridge.log" --no-clipboard 2>&1 \
    | grep -E "WSL bridge|starting|Arguments|rror" | head -10; echo "exit ${PIPESTATUS[0]}"
ls -l "$testdir" | head

step "bridge: the window returns at once"
start=$(date +%s%N)
SQUARESNAP_WINDOWS_EXE="$winexe" "$sq"; code=$?
end=$(date +%s%N)
echo "exit $code after $(( (end - start) / 1000000 )) ms"

step "Linux log file"
tail -n 4 ~/.config/SquareSnap/squaresnap.log
