# Copies the release to the current user's app directory; no administrator required.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'out\Floatly-win-x64'
$exe = Join-Path $source 'Floatly.exe'
if (-not (Test-Path $exe)) { throw 'First run 02-CREATE-PORTABLE-EXE.bat.' }
$destination = Join-Path $env:LOCALAPPDATA 'Programs\Floatly'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item (Join-Path $source '*') -Destination $destination -Force -Recurse
$exeDest = Join-Path $destination 'Floatly.exe'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path $startMenu 'Floatly.lnk'))
$link.TargetPath = $exeDest
$link.WorkingDirectory = $destination
$link.Description = 'Floatly - Offline AssistiveTouch for Windows'
$link.IconLocation = $exeDest
$link.Save()
Write-Host "Floatly installed to $destination" -ForegroundColor Green
Write-Host 'Launch Floatly from Start. Enable automatic startup in Floatly Settings if you want.'
