# Builds every SquareSnap release package in dist\ (git ignores it):
#   SquareSnap-<version>-win-x64.zip         SquareSnap.exe, SquareSnap.com (the terminal helper), README.md, AGENTS.md,
#                                            CHANGELOG.md, LICENSE.txt, pixelcheck.ps1
#   SquareSnap-<version>-win-x64-winget.zip  the same, but the helper is cli\squaresnap.exe, for WinGet (step 6, below)
#   SquareSnap-<version>-linux-x64.tar.gz    and -linux-arm64.tar.gz (since beta 13, tools\make_linux_tar.py)
#   SHA256SUMS                               the SHA-256 of the four packages in the sha256sum format (step 6)
# The staged folder dist\SquareSnap-<version>-win-x64\ is kept: tools\make_msix.py builds the Store package from it.
# Usage (PowerShell 7, from anywhere):  & <repository>\tools\build_release.ps1
# The release workflow (.github\workflows\release.yml) runs the same script on windows-latest.
# Needs the .NET 10 SDK, Python 3 and Visual Studio 2022 (Build Tools) with the C++ workload (Native AOT links the helper).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version
$dist = Join-Path $root 'dist'
$pub = Join-Path $dist 'publish'

# Native AOT finds the linker with vswhere.exe, which isn't on PATH by default.
$vsInstaller = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if (Test-Path "$vsInstaller\vswhere.exe") { $env:PATH = "$vsInstaller;$env:PATH" }
else { throw "vswhere.exe not found in $vsInstaller. Install Visual Studio 2022 Build Tools with 'Desktop development with C++'." }

if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Force $dist | Out-Null

Write-Host "Building SquareSnap $version"
dotnet publish "$root\src\SquareSnap.App" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "$pub\app"
if ($LASTEXITCODE) { throw "Publishing SquareSnap.App failed (exit $LASTEXITCODE)." }
dotnet publish "$root\src\SquareSnap.Launcher" -c Release -r win-x64 -p:DebugType=none -o "$pub\launcher"
if ($LASTEXITCODE) { throw "Publishing SquareSnap.Launcher failed (exit $LASTEXITCODE)." }

# Stages the files, proves the pair works there (the helper must find the app and pass back its text and exit codes),
# and zips them with no folder inside: Explorer's "Extract All" already makes one named after the zip (since beta 13).
function New-WindowsZip([string]$name, [string]$helperName) {
    $stage = Join-Path $dist $name
    $zip = "$stage.zip"
    $helper = Join-Path $stage $helperName
    New-Item -ItemType Directory -Force (Split-Path $helper -Parent) | Out-Null
    Copy-Item "$pub\app\SquareSnap.exe" $stage
    Copy-Item "$pub\launcher\SquareSnap.Launcher.exe" $helper
    # README, AGENTS.md and CHANGELOG replace beta\README-beta.txt (step 7, 2026-11-16).
    foreach ($doc in 'README.md', 'AGENTS.md', 'CHANGELOG.md') { Copy-Item "$root\$doc" $stage }
    Copy-Item "$root\LICENSE" "$stage\LICENSE.txt"
    Copy-Item "$root\tools\pixelcheck.ps1" $stage

    $v = & $helper --version
    if ($LASTEXITCODE -ne 0 -or $v -ne "SquareSnap $version") { throw "$helperName --version gave '$v' (exit $LASTEXITCODE)." }
    $null = & $helper capture --size huge 2>$null   # (nothing may reach this function's output but the zip's path)
    if ($LASTEXITCODE -ne 2) { throw "$helperName capture --size huge returned $LASTEXITCODE, expected 2." }
    Write-Host "Checked in $name`: $v through $helperName, bad-argument exit code 2."

    # .NET's ZipFile writes entry names with '/', so cli/squaresnap.exe unpacks into a folder everywhere.
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $z = [IO.Compression.ZipFile]::OpenRead($zip)
    try { $z.Entries | ForEach-Object { Write-Host ("  {0,12:N0}  {1}" -f $_.Length, $_.FullName) } } finally { $z.Dispose() }
    Write-Host "Wrote $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
    $zip
}

$packages = @()
$packages += New-WindowsZip "SquareSnap-$version-win-x64" 'SquareSnap.com'
# The WinGet zip (step 6, 2026-11-14, from BejView's step 5): WinGet's portable install makes a hard link named after the
# command next to the file it points at. With the command "squaresnap" pointing at SquareSnap.com that link would be
# squaresnap.exe, the same name as SquareSnap.exe on Windows, and WinGet would silently replace the window program with
# the helper. So here the helper is cli\squaresnap.exe (file name = command, so no hard link) and finds SquareSnap.exe
# one folder up (SquareSnap.Launcher FindTarget).
$packages += New-WindowsZip "SquareSnap-$version-win-x64-winget" 'cli\squaresnap.exe'

# Linux (step 4, 2026-11-13): single-file, self-contained builds packed into .tar.gz files by make_linux_tar.py.
# They're checked in WSL by hand (NOTES.md, "Building and testing"); arm64 can't run here.
foreach ($arch in 'x64', 'arm64') {
    dotnet publish "$root\src\SquareSnap.App" -c Release -r "linux-$arch" --self-contained -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "$pub\linux-$arch"
    if ($LASTEXITCODE) { throw "Publishing SquareSnap.App for linux-$arch failed (exit $LASTEXITCODE)." }
    python -I "$root\tools\make_linux_tar.py" "$pub\linux-$arch" $arch
    if ($LASTEXITCODE) { throw "make_linux_tar.py failed for linux-$arch (exit $LASTEXITCODE)." }
    $packages += Join-Path $dist "SquareSnap-$version-linux-$arch.tar.gz"
}
Remove-Item -Recurse -Force $pub

# SHA256SUMS (step 6): hash, two spaces, name, LF line endings, so `sha256sum -c SHA256SUMS` works on Linux.
$sums = Join-Path $dist 'SHA256SUMS'
$lines = foreach ($p in $packages) { "$((Get-FileHash $p -Algorithm SHA256).Hash.ToLowerInvariant())  $(Split-Path $p -Leaf)" }
[IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"), [Text.Encoding]::ASCII)
Write-Host "Wrote $sums"
Get-Content $sums | ForEach-Object { Write-Host "  $_" }
exit 0
