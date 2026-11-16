<#
  SquareSnap MSIX test (step 6, 2026-11-14; BejView's msix-test.ps1 from its step 5). Run in PowerShell from the folder
  that holds this script, the *-test.msix package and SquareSnap-test.cer. Steps, in order:

    1. trust      (admin window, once per machine)  trusts the test certificate, so Windows installs the package
    2. install    (your normal window)              installs the test package
    3. check      (a NEW normal window)             terminal checks through the "squaresnap" command, a real screen
                                                    capture, and where the log went
    4. import     (your normal window, optional)    1.0's settings: puts a 1.0 settings file in %APPDATA%\SquareSnap
                                                    and checks the package imports it; your own settings are moved
                                                    aside first and put back afterwards
    5. uninstall  (your normal window)              removes SquareSnap's package
    6. wack       (admin window, optional)          runs the Windows App Certification Kit on the package
    7. untrust    (admin window, at the end)        removes the test certificate again

  Example:  .\msix-test.ps1 install
  Each step appends what it saw to msix-test-results.txt next to this script; hand that file back.
  The package keeps Windows' default file-system virtualization: SquareSnap's NEW settings and log files go to the
  package's private copy of %APPDATA%\SquareSnap, which uninstall removes; files already in %APPDATA%\SquareSnap are
  used in place. Captures go to Pictures\SquareSnap (or your chosen folder), which is never redirected.
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('trust', 'install', 'check', 'import', 'uninstall', 'wack', 'untrust')]
    [string]$Step
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$results = Join-Path $here 'msix-test-results.txt'
$family = 'RichardBejtlich.SquareSnap_kz4qzpv342p6y'
$name = 'RichardBejtlich.SquareSnap'
$copyRoot = Join-Path $env:LOCALAPPDATA "Packages\$family\LocalCache\Roaming\SquareSnap"
$realRoot = Join-Path $env:APPDATA 'SquareSnap'

function Say([string]$text) {
    Write-Host $text
    Add-Content -Path $results -Value $text
}

function Mark([bool]$ok) { if ($ok) { 'OK  ' } else { 'FAIL' } }

function Admin { ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }

function Package {
    $p = Get-ChildItem -Path $here -Filter 'SquareSnap-*-x64-test.msix' | Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $p) { throw "No SquareSnap-*-x64-test.msix in $here." }
    $p.FullName
}

function Installed {
    $p = Get-AppxPackage -Name $name
    if (-not $p) { throw 'SquareSnap is not installed. Run: .\msix-test.ps1 install' }
    $p
}

function NotRunning {
    if (Get-Process SquareSnap -ErrorAction SilentlyContinue) {
        throw 'SquareSnap is running (check the tray). Close it (tray icon > Exit) and run this step again.'
    }
}

# Width and height from a PNG's IHDR chunk (bytes 16-23, big-endian).
function PngSize([string]$file) {
    $b = [IO.File]::ReadAllBytes($file)
    if ($b.Length -lt 24 -or $b[1] -ne 0x50 -or $b[2] -ne 0x4E -or $b[3] -ne 0x47) { return $null }
    $w = ([int]$b[16] -shl 24) -bor ([int]$b[17] -shl 16) -bor ([int]$b[18] -shl 8) -bor [int]$b[19]   # [int] first: -shl on a byte stays a byte
    $h = ([int]$b[20] -shl 24) -bor ([int]$b[21] -shl 16) -bor ([int]$b[22] -shl 8) -bor [int]$b[23]
    "${w}x${h}"
}

# Which copy of a file under %APPDATA%\SquareSnap Explorer and Notepad see: the package's private copy or the real one.
function WhereIs([string]$leaf) {
    $c = Join-Path $copyRoot $leaf; $r = Join-Path $realRoot $leaf
    $parts = @()
    if (Test-Path $c) { $parts += "private copy" }
    if (Test-Path $r) { $parts += "real %APPDATA%" }
    if ($parts.Count -eq 0) { 'nowhere' } else { $parts -join ' and ' }
}

Say ''
Say "=== $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  step $Step  user $env:USERNAME  $([Environment]::OSVersion.VersionString)"

switch ($Step) {
    'trust' {
        if (-not (Admin)) { throw 'Run "trust" in an admin PowerShell window (right-click > Run as administrator).' }
        $cer = Join-Path $here 'SquareSnap-test.cer'
        $c = Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
        Say "Trusted the test certificate $($c.Thumbprint) ($($c.Subject)) in Local Machine > Trusted People."
    }
    'install' {
        if (Admin) { Say 'Note: this window is an admin window. Use your normal window, so SquareSnap installs for your own account.' }
        if (Get-AppxPackage -Name $name) { throw "SquareSnap's package is already installed. Run: .\msix-test.ps1 uninstall" }
        $msix = Package
        try { Add-AppxPackage -Path $msix }
        catch {
            if ("$_" -match '0x800B0109') { throw 'Windows does not trust the test certificate yet. Run ".\msix-test.ps1 trust" in an admin window first.' }
            throw
        }
        $p = Get-AppxPackage -Name $name
        Say "Installed $([IO.Path]::GetFileName($msix)): $($p.PackageFullName)"
        Say "Install folder: $($p.InstallLocation)"
        Say 'Next: open a NEW terminal window (so it sees the "squaresnap" command), then run: .\msix-test.ps1 check'
    }
    'check' {
        $ErrorActionPreference = 'Continue'   # Windows PowerShell 5.1 turns a native program's stderr into errors under Stop
        $p = Installed
        Say "Package: $($p.PackageFullName)"
        $cmd = Get-Command squaresnap -ErrorAction SilentlyContinue
        if (-not $cmd) { Say 'FAIL  the "squaresnap" command is not found. Open a new terminal window and run check again.'; break }
        Say "$(Mark ($cmd.Source -like '*\Microsoft\WindowsApps\*'))  squaresnap is $($cmd.Source) (expected the Store alias in ...\Microsoft\WindowsApps)"
        Say '      (a zip copy of SquareSnap.com earlier on PATH would win over the alias; the line above shows which ran)'

        $v = & squaresnap --version
        Say "$(Mark ($LASTEXITCODE -eq 0 -and "$v" -like 'SquareSnap 2.0.0*'))  squaresnap --version -> '$v' (exit $LASTEXITCODE)"

        # Waits like .\SquareSnap: the whole help arrives before the next line runs, and the exit code comes back.
        $help = & squaresnap --help
        $last = (@($help) | Where-Object { $_.Trim() } | Select-Object -Last 1)
        Say "$(Mark ($LASTEXITCODE -eq 0 -and "$last".Trim() -eq 'Enjoy SquareSnap!'))  squaresnap --help ends with '$("$last".Trim())' (exit $LASTEXITCODE; $(@($help).Count) lines)"

        $null = & squaresnap capture --size huge 2>&1
        Say "$(Mark ($LASTEXITCODE -eq 2))  a bad size returns 2 (got $LASTEXITCODE)"

        # A real screen capture from inside the package: the top-left 64x64 of the main monitor, no overlay, no clipboard.
        $shots = Join-Path $here 'msix-test-shots'
        if (Test-Path $shots) { Remove-Item -Recurse -Force $shots }
        $saved = & squaresnap capture --region 0,0,64,64 --size native --no-clipboard --out $shots --verbose 2>$null
        $code = $LASTEXITCODE
        $file = @($saved) | Where-Object { $_ -like '*.png' } | Select-Object -Last 1
        $size = if ($file -and (Test-Path $file)) { PngSize $file } else { $null }
        Say "$(Mark ($code -eq 0 -and $size -eq '64x64'))  capture --region 0,0,64,64 (exit $code): $(if ($file) { "$file, $size" } else { 'no file' })"

        # Where the log really is, as SquareSnap reports it and as Explorer sees it.
        $paths = & squaresnap paths
        $paths | ForEach-Object { Say "      paths: $_" }
        # Windows puts NEW files in the package's private copy but changes files that already exist in %APPDATA% in place
        # (1903+), so either place can be right: on a PC that ran a zip build of SquareSnap before, the log stays in
        # %APPDATA%\SquareSnap. What matters is that "squaresnap paths" names the place where the log really is.
        $copy = Join-Path $copyRoot 'squaresnap.log'
        $shown = (($paths | Select-String 'Log file:').ToString() -replace '^\s*Log file:\s*', '').Trim()
        Say "      the log is in: $(WhereIs 'squaresnap.log'); settings.json is in: $(WhereIs 'settings.json')"
        Say "$(Mark ((Test-Path $shown) -and (($shown -eq $copy) -or (-not (Test-Path $copy)))))  ""squaresnap paths"" names the log where it really is: $shown"
        Say "$(Mark ([bool]($paths | Select-String 'Installed from the Microsoft Store')))  ""squaresnap paths"" says it runs from the Store package"
        Say 'Now the window checks by hand (the test plan''s Store package group): Start menu > SquareSnap; New capture (drag a'
        Say 'square on each monitor); Ctrl+Alt+S with the window hidden; the tray icon and its menu; Save, then the banner''s'
        Say 'Open folder; Help > Open the log file, Help > Open the settings folder, Help > About.'
    }
    'import' {
        $ErrorActionPreference = 'Continue'   # (as in check); the steps that must stop say -ErrorAction Stop
        $null = Installed
        NotRunning
        $backup = "$realRoot-msix-test-backup"
        if (Test-Path $backup) { throw "$backup already exists (an earlier import step didn't finish). Check it, put it back as $realRoot by hand, then run again." }
        $moved = $false
        if (Test-Path $realRoot) { Rename-Item $realRoot $backup -ErrorAction Stop; $moved = $true; Say "Moved your settings folder aside: $backup" }
        try {
            # Settings left in the private copy (from check or the window) would hide the test file: clear them first.
            foreach ($leaf in 'settings.json', 'settings.v1.json', 'settings.json.tmp') { Remove-Item (Join-Path $copyRoot $leaf) -ErrorAction SilentlyContinue }
            # A 1.0 settings file: no SchemaVersion, DefaultSaveLocation and NextFileNumber (1.0's two values).
            $target = Join-Path $here 'msix-test-v1-shots'
            if (Test-Path $target) { Remove-Item -Recurse -Force $target }
            New-Item -ItemType Directory -Force $realRoot | Out-Null
            $json = @{ DefaultSaveLocation = $target; NextFileNumber = 47 } | ConvertTo-Json -Compress
            [IO.File]::WriteAllText((Join-Path $realRoot 'settings.json'), $json)   # UTF-8 without a BOM, like 1.0
            Say "Wrote a 1.0 settings file (save folder $target)."

            $paths = & squaresnap paths      # the first run imports it
            $folder = (($paths | Select-String 'Save folder:').ToString() -replace '^\s*Save folder:\s*', '').Trim()
            Say "$(Mark ($folder -eq $target))  first run after 1.0: save folder '$folder'"
            $paths2 = & squaresnap paths     # the second run reads the 2.0 file the first one wrote
            $folder2 = (($paths2 | Select-String 'Save folder:').ToString() -replace '^\s*Save folder:\s*', '').Trim()
            Say "$(Mark ($folder2 -eq $target))  second run: save folder '$folder2' (the imported setting stuck)"

            $saved = & squaresnap capture --region 0,0,64,64 --size native --no-clipboard 2>$null
            $code = $LASTEXITCODE
            $file = @($saved) | Where-Object { $_ -like '*.png' } | Select-Object -Last 1
            Say "$(Mark ($code -eq 0 -and $file -and (Split-Path $file -Parent) -eq $target -and (Test-Path $file)))  a capture with no --out goes to the imported folder (exit $code): $file"

            $real = Join-Path $realRoot 'settings.json'
            $realKind = if ((Get-Content -Raw $real) -match 'SchemaVersion') { '2.0' } else { '1.0' }
            Say "      settings.json is in: $(WhereIs 'settings.json') (the real one now holds $realKind settings); settings.v1.json is in: $(WhereIs 'settings.v1.json')"
        }
        finally {
            Remove-Item -Recurse -Force $realRoot -ErrorAction SilentlyContinue
            if ($moved) { Rename-Item $backup $realRoot -ErrorAction Stop; Say "Put your settings folder back: $realRoot" }
            # The private copy's settings came from the test file: remove them, so the window starts with your own settings.
            foreach ($leaf in 'settings.json', 'settings.v1.json', 'settings.json.tmp') {
                Remove-Item (Join-Path $copyRoot $leaf) -ErrorAction SilentlyContinue
            }
        }
    }
    'uninstall' {
        $p = Get-AppxPackage -Name $name
        if (-not $p) { Say 'SquareSnap''s package is not installed.'; break }
        NotRunning
        Remove-AppxPackage -Package $p.PackageFullName
        $gone = -not (Test-Path (Join-Path $env:LOCALAPPDATA "Packages\$family"))
        Say "Removed $($p.PackageFullName). Its private copy is gone: $gone. %APPDATA%\SquareSnap (the zip's) is untouched."
    }
    'wack' {
        if (-not (Admin)) { throw 'Run "wack" in an admin PowerShell window.' }
        $kit = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit\appcert.exe'
        if (-not (Test-Path $kit)) { throw "The Windows App Certification Kit isn't installed ($kit). It comes with the Windows SDK." }
        if (Get-AppxPackage -Name $name -AllUsers) { throw 'Uninstall SquareSnap first (.\msix-test.ps1 uninstall in your normal window): the kit installs the package itself.' }
        $msix = Package
        $report = Join-Path $here 'wack.xml'
        & $kit reset | Out-Null
        Say "Running the certification kit on $([IO.Path]::GetFileName($msix)) (several minutes)..."
        & $kit test -appxpackagepath $msix -reportoutputpath $report | Out-Null
        if (Test-Path $report) {
            [xml]$x = Get-Content -Raw $report
            Say "      overall: $($x.DocumentElement.GetAttribute('OVERALL_RESULT'))  (report: $report)"
            foreach ($t in $x.SelectNodes('//TEST')) {
                $r = $t.SelectSingleNode('RESULT').InnerText.Trim()
                # Optional tests don't count towards the overall result (the kit's OPTIONAL="TRUE").
                $opt = if ($t.GetAttribute('OPTIONAL') -eq 'TRUE') { ' (optional: does not count)' } else { '' }
                if ($r -ne 'PASS') { Say "      not passed: $($t.GetAttribute('NAME')) -> $r$opt" }
            }
        } else {
            Say 'FAIL  no report'
        }
    }
    'untrust' {
        if (-not (Admin)) { throw 'Run "untrust" in an admin PowerShell window.' }
        $cer = New-Object Security.Cryptography.X509Certificates.X509Certificate2 (Join-Path $here 'SquareSnap-test.cer')
        $item = Join-Path 'Cert:\LocalMachine\TrustedPeople' $cer.Thumbprint
        if (Test-Path $item) { Remove-Item $item; Say "Removed the test certificate $($cer.Thumbprint)." }
        else { Say "The test certificate $($cer.Thumbprint) wasn't trusted on this machine." }
    }
}
