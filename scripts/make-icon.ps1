# Generates assets/bogihub.ico (PNG entries: 256, 64, 48, 32, 16 px) from the same drawing the tray icon uses:
# the Tabler "device-gamepad-2" icon on a dark rounded square (Theme/Icons.cs). Builds the app without an .exe icon
# first, so this also works when the .ico is missing. Opens no window and doesn't touch the controller.
# Usage: powershell -ExecutionPolicy Bypass -File scripts/make-icon.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'assets\bogihub.ico'
$bin = Join-Path $root 'artifacts\icon-tool'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

dotnet build (Join-Path $root 'src\BogiHub.App\BogiHub.App.csproj') -c Debug -o $bin -p:ApplicationIcon= -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$env:BOGIHUB_WRITE_ICO = $out
try { $p = Start-Process (Join-Path $bin 'BogiHub.exe') -PassThru -Wait } finally { Remove-Item Env:BOGIHUB_WRITE_ICO }
if (-not (Test-Path $out)) { throw "icon was not written" }
Write-Host "Wrote $out"
