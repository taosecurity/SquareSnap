<#
.SYNOPSIS
  Checks that SquareSnap 2.0 captures exactly the same pixels as SquareSnap 1.0's method.

.DESCRIPTION
  1. Lists your monitors (so you can pick a square on any of them).
  2. Captures the square with Graphics.CopyFromScreen, the call SquareSnap 1.0 used, in a DPI-aware process,
     once before and once after SquareSnap (two 1.0-method captures show the screen's own noise).
  3. Captures the same square with "SquareSnap.exe capture --region ...".
  4. Compares them pixel by pixel and prints MATCH, MATCH (within screen noise) or DIFFERENT.

  Pick a square over something that doesn't move (wallpaper, a still window): a ticking clock or a video makes
  the two captures differ for reasons that have nothing to do with SquareSnap. Keep the mouse out of the square.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File pixelcheck.ps1
  powershell -ExecutionPolicy Bypass -File pixelcheck.ps1 -Region 2000,200,600,600
#>
param(
    [string]$Region = "",
    [string]$Exe = (Join-Path $PSScriptRoot "SquareSnap.exe")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class PcDpi { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v); }
"@
[void][PcDpi]::SetProcessDpiAwarenessContext([IntPtr]-4)   # per-monitor aware: real pixels
Add-Type -AssemblyName System.Windows.Forms

Write-Host "Monitors (real pixels):"
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
    Write-Host ("  {0,-12} x={1,6} y={2,6} {3}x{4}{5}" -f $s.DeviceName.Replace('\\.\', ''), $s.Bounds.X, $s.Bounds.Y, $s.Bounds.Width, $s.Bounds.Height, $(if ($s.Primary) { "  (primary)" } else { "" }))
}

if (-not $Region) {
    $p = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $size = [Math]::Min(400, [Math]::Min($p.Width, $p.Height) - 100)
    $Region = "{0},{1},{2},{2}" -f ($p.X + 50), ($p.Y + 50), $size
    Write-Host "No -Region given; using $Region on the primary monitor."
}
$parts = $Region.Split(",") | ForEach-Object { [int]$_.Trim() }
if ($parts.Count -ne 4 -or $parts[2] -ne $parts[3]) { throw "-Region must be x,y,size,size (a square), for example 100,100,400,400." }
$x, $y, $w, $h = $parts

if (-not (Test-Path $Exe)) { throw "SquareSnap.exe not found at $Exe. Put this script next to SquareSnap.exe or pass -Exe <path>." }

$work = Join-Path ([IO.Path]::GetTempPath()) ("squaresnap-pixelcheck-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null


# 1.0's method: Graphics.CopyFromScreen (plain SRCCOPY) in this DPI-aware process.
function Capture-Method10 {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $g.Dispose()
    return $bmp
}

# Diagnostic: 1.0's method copying the WHOLE monitor the square is on, then cutting the square out, which is
# what SquareSnap does (it freezes the whole screen first). On a monitor where Windows converts colours with a
# dither pattern, the pattern can depend on where the copy starts, so this can differ from the square-only copy
# while both are honest captures.
function Capture-WholeMonitorCropped {
    $mon = [System.Windows.Forms.Screen]::FromRectangle((New-Object System.Drawing.Rectangle $x, $y, $w, $h)).Bounds
    $full = New-Object System.Drawing.Bitmap $mon.Width, $mon.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($full)
    $g.CopyFromScreen($mon.X, $mon.Y, 0, 0, $mon.Size)
    $g.Dispose()
    $part = $full.Clone((New-Object System.Drawing.Rectangle ($x - $mon.X), ($y - $mon.Y), $w, $h), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $full.Dispose()
    return $part
}

# Diagnostic: the same copy plus the CAPTUREBLT flag betas 1-8 used. Called through Win32 BitBlt directly,
# because Windows PowerShell 5.1's System.Drawing rejects SourceCopy|CaptureBlt as "not a valid enum value".
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class PcGdi {
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
}
"@
function Capture-WithCaptureBlt {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dst = $g.GetHdc()
    $src = [PcGdi]::GetDC([IntPtr]::Zero)
    try {
        $ok = [PcGdi]::BitBlt($dst, 0, 0, $w, $h, $src, $x, $y, [uint32]0x40CC0020)   # SRCCOPY | CAPTUREBLT
        if (-not $ok) { throw "BitBlt with CAPTUREBLT failed (error $([Runtime.InteropServices.Marshal]::GetLastWin32Error()))." }
    }
    finally {
        [void][PcGdi]::ReleaseDC([IntPtr]::Zero, $src)
        $g.ReleaseHdc($dst)
        $g.Dispose()
    }
    return $bmp
}

function Get-Bytes([System.Drawing.Bitmap]$bmp) {
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    $d = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $n = $d.Stride * $bmp.Height
    $bytes = New-Object byte[] $n
    [Runtime.InteropServices.Marshal]::Copy($d.Scan0, $bytes, 0, $n)
    $bmp.UnlockBits($d)
    return ,$bytes
}

# Number of pixels that differ and the largest per-channel difference (alpha ignored).
function Compare-Pixels([byte[]]$p, [byte[]]$q) {
    $diff = 0; $max = 0
    for ($i = 0; $i -lt $p.Length; $i += 4) {
        $d = [Math]::Max([Math]::Abs($p[$i] - $q[$i]), [Math]::Max([Math]::Abs($p[$i + 1] - $q[$i + 1]), [Math]::Abs($p[$i + 2] - $q[$i + 2])))
        if ($d -gt 0) { $diff++; if ($d -gt $max) { $max = $d } }
    }
    return @($diff, $max)
}

# Two captures with 1.0's method, one before and one after SquareSnap's, measure the screen's own noise.
# With HDR or Windows 11 Auto Color Management on, every screen grab is converted from the desktop's wide-colour
# surface and can come out +-1 differently each time, whichever program takes it.
$a1 = Capture-Method10
$a1.Save((Join-Path $work "method-1.0-before.png"), [System.Drawing.Imaging.ImageFormat]::Png)

$proc = Start-Process -FilePath $Exe -ArgumentList @("capture", "--region", $Region, "--out", $work, "--name", "squaresnap-2.0", "--format", "png", "--size", "native", "--no-clipboard", "--log", (Join-Path $work "squaresnap.log")) -Wait -PassThru -WindowStyle Hidden
if ($proc.ExitCode -ne 0) { throw "SquareSnap exited with code $($proc.ExitCode). Its log is $work\squaresnap.log" }

$a2 = Capture-Method10
$a2.Save((Join-Path $work "method-1.0-after.png"), [System.Drawing.Imaging.ImageFormat]::Png)
# Diagnostic: 1.0's method copying the whole monitor, then cut to the square. Never stops the main check.
$am = $null
try {
    $am = Capture-WholeMonitorCropped
    $am.Save((Join-Path $work "method-1.0-whole-monitor.png"), [System.Drawing.Imaging.ImageFormat]::Png)
}
catch {
    Write-Host "Diagnostic whole-monitor capture skipped: $($_.Exception.Message)"
    $am = $null
}

# Diagnostic: 1.0's method plus the CAPTUREBLT flag betas 1-8 used. Never stops the main check.
$ac = $null
try {
    $ac = Capture-WithCaptureBlt
    $ac.Save((Join-Path $work "method-1.0-captureblt.png"), [System.Drawing.Imaging.ImageFormat]::Png)
}
catch {
    Write-Host "Diagnostic capture with CAPTUREBLT skipped: $($_.Exception.Message)"
    $ac = $null
}

$bPath = Join-Path $work "squaresnap-2.0.png"
if (-not (Test-Path $bPath)) { throw "SquareSnap didn't write $bPath. Its log is $work\squaresnap.log" }
# Load without colour management, so the file's pixels are compared exactly as stored.
$stream = [IO.File]::OpenRead($bPath)
$b = [System.Drawing.Image]::FromStream($stream, $false, $true)
if ($b.Width -ne $w -or $b.Height -ne $h) {
    Write-Host "DIFFERENT: SquareSnap 2.0 saved $($b.Width)x$($b.Height), expected ${w}x${h}." -ForegroundColor Red
    Write-Host "Files: $work"; exit 1
}
$pb = Get-Bytes ([System.Drawing.Bitmap]$b); $b.Dispose(); $stream.Dispose()
$p1 = Get-Bytes $a1; $a1.Dispose()
$p2 = Get-Bytes $a2; $a2.Dispose()
$pc = $null
if ($ac -ne $null) { $pc = Get-Bytes $ac; $ac.Dispose() }

$noise = Compare-Pixels $p1 $p2   # 1.0's method against itself
$vs1 = Compare-Pixels $p1 $pb
$vs2 = Compare-Pixels $p2 $pb
$total = $w * $h
Write-Host ("1.0 method, before vs after (screen noise): {0} of {1} pixels differ, largest difference {2}" -f $noise[0], $total, $noise[1])
Write-Host ("SquareSnap 2.0 vs 1.0 method (before):      {0} pixels differ, largest difference {1}" -f $vs1[0], $vs1[1])
Write-Host ("SquareSnap 2.0 vs 1.0 method (after):       {0} pixels differ, largest difference {1}" -f $vs2[0], $vs2[1])
if ($pc -ne $null) {
    $cb = Compare-Pixels $p1 $pc
    $cb2 = Compare-Pixels $pc $pb
    Write-Host ("Diagnostic: 1.0 method with CAPTUREBLT vs without: {0} pixels differ, largest {1}; vs SquareSnap 2.0: {2} differ, largest {3}" -f $cb[0], $cb[1], $cb2[0], $cb2[1])
}
$wm = $null
if ($am -ne $null) {
    $pm = Get-Bytes $am; $am.Dispose()
    $wm = Compare-Pixels $pm $pb
    $wq = Compare-Pixels $p1 $pm
    Write-Host ("Diagnostic: 1.0 method copying the whole monitor, then cut to the square: vs square-only copy {0} differ (largest {1}); vs SquareSnap 2.0: {2} differ (largest {3})" -f $wq[0], $wq[1], $wm[0], $wm[1])
}

if ($vs1[0] -eq 0 -or $vs2[0] -eq 0) {
    Write-Host "MATCH: ${w}x${h} at $x,$y - every pixel identical to SquareSnap 1.0's method." -ForegroundColor Green
    Remove-Item -Recurse -Force $work
    exit 0
}
if ($wm -ne $null -and $wm[0] -eq 0) {
    Write-Host "MATCH (same as 1.0's method copying the whole monitor): every pixel identical." -ForegroundColor Green
    Write-Host "On this monitor Windows' own result depends on where a copy starts (a colour-conversion dither pattern):"
    Write-Host "copying just the square gives slightly different values from copying the whole monitor. SquareSnap"
    Write-Host "freezes the whole screen first, so it matches the whole-monitor copy exactly."
    Remove-Item -Recurse -Force $work
    exit 0
}
# Same tiny differences as the screen shows against itself: as exact as this screen allows.
$noiseMax = [Math]::Max($noise[1], 1)
if ($noise[0] -gt 0 -and $vs1[1] -le $noiseMax -and $vs2[1] -le $noiseMax -and [Math]::Min($vs1[0], $vs2[0]) -le 2 * $noise[0] + 10) {
    Write-Host "MATCH (within screen noise): SquareSnap 2.0 differs from 1.0's method no more than 1.0's method differs from itself." -ForegroundColor Green
    Write-Host "Your screen gives slightly different values on every capture (HDR or Auto Color Management does this)."
    Remove-Item -Recurse -Force $work
    exit 0
}
Write-Host "DIFFERENT: SquareSnap 2.0's capture differs from 1.0's method by more than the screen's own noise." -ForegroundColor Red
Write-Host "If something on screen moved, pick a still area and run again. All three images and the log are in $work"
exit 1
