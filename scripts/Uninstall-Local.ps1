# Removes the user-local installation, shortcut, and launch-on-sign-in registration.
# Saved notes/tasks in %LOCALAPPDATA%\Floatly are intentionally preserved.
$ErrorActionPreference = 'Stop'
$destination = Join-Path $env:LOCALAPPDATA 'Programs\Floatly'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Floatly.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runEntry = (Get-ItemProperty -Path $runKey -Name Floatly -ErrorAction SilentlyContinue).Floatly
if ($runEntry -and ($runEntry -like "*$destination*")) {
    Remove-ItemProperty -Path $runKey -Name Floatly -ErrorAction SilentlyContinue
}
if (Test-Path $shortcut) { Remove-Item $shortcut -Force }
if (Get-Process Floatly -ErrorAction SilentlyContinue) {
    throw 'Exit Floatly from the system tray first, then run this uninstall script again.'
}
if (Test-Path $destination) { Remove-Item $destination -Recurse -Force }
Write-Host 'Floatly application removed. Your local notes and tasks were preserved.' -ForegroundColor Green
