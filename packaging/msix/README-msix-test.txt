SquareSnap: Microsoft Store package (MSIX) test
===============================================

This is a TEST package, signed with a self-signed test certificate so Windows will install it. The real Store
package is signed by the Microsoft Store. Use it to check a Store build on your own PC before uploading it.

Like every Store app, SquareSnap writes NEW settings and log files into its own private folder
(...\AppData\Local\Packages\RichardBejtlich.SquareSnap_kz4qzpv342p6y\LocalCache\Roaming\SquareSnap), which
uninstalling removes. Files that already exist in %APPDATA%\SquareSnap (from a zip build, or SquareSnap 1.0) are used
in place. Either way, SquareSnap shows and opens the right place (squaresnap paths, Help > Open the log file).
Captures go to Pictures\SquareSnap or the folder you chose, which the package never redirects.

Files
  SquareSnap-<version>-x64-test.msix  the test package
  SquareSnap-test.cer                 the test certificate (public part only)
  msix-test.ps1                       the steps below; it writes msix-test-results.txt

In short (PowerShell, in this folder; first run: Set-ExecutionPolicy -Scope Process Bypass)
  Close any zip copy of SquareSnap first (tray icon > Exit), so the hotkey is free for the package.
  1. admin window:   .\msix-test.ps1 trust
  2. normal window:  .\msix-test.ps1 install
  3. NEW window:     .\msix-test.ps1 check        then the window: Start menu > SquareSnap, a capture on each
                                                  monitor, Ctrl+Alt+S, the tray icon, Save and Open folder,
                                                  Help > Open the log file, Help > About
  4. normal window:  .\msix-test.ps1 import       (optional) 1.0's settings are imported; your own settings
                                                  folder is moved aside and put back afterwards
  5. normal window:  .\msix-test.ps1 uninstall
  6. admin window:   .\msix-test.ps1 wack         (optional; needs the Windows SDK. "Blocked executables" is
                                                  optional and expected to fail for a desktop app)
  7. admin window:   .\msix-test.ps1 untrust
msix-test-results.txt (and wack.xml from step 6) record what happened.
