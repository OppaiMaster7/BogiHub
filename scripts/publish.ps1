# Builds a self-contained BogiHub (no .NET install needed) into publish\BogiHub
# and, with -Shortcuts, adds "BogiHub" to the Desktop and the Start menu.
# Usage: powershell -ExecutionPolicy Bypass -File scripts/publish.ps1 -Shortcuts
param([switch]$Shortcuts)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'publish\BogiHub'

& (Join-Path $PSScriptRoot 'fetch-deps.ps1')
if (-not (Test-Path (Join-Path $root 'assets\bogihub.ico'))) { & (Join-Path $PSScriptRoot 'make-icon.ps1') }

# Close a running copy the normal way first (it releases held keys and unplugs its virtual pad), force only as a fallback.
$running = Join-Path $out 'BogiHub.exe'
if ((Get-Process BogiHub -ErrorAction SilentlyContinue) -and (Test-Path $running)) { Start-Process $running -ArgumentList '--exit' -Wait }
Get-Process BogiHub -ErrorAction SilentlyContinue | Stop-Process
dotnet publish (Join-Path $root 'src\BogiHub.App\BogiHub.App.csproj') -c Release -r win-x64 --self-contained true -o $out -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$exe = Join-Path $out 'BogiHub.exe'
if (-not (Test-Path (Join-Path $out 'SDL3.dll'))) { throw "SDL3.dll missing from publish output" }
Write-Host "Published: $exe"

if ($Shortcuts) {
    $shell = New-Object -ComObject WScript.Shell
    $targets = @(
        (Join-Path ([Environment]::GetFolderPath('Desktop')) 'BogiHub.lnk'),
        (Join-Path ([Environment]::GetFolderPath('Programs')) 'BogiHub.lnk')
    )
    foreach ($lnk in $targets) {
        $s = $shell.CreateShortcut($lnk)
        $s.TargetPath = $exe
        $s.WorkingDirectory = $out
        $s.IconLocation = "$exe,0"
        $s.Description = 'BogiHub: companion app for the Steam Controller'
        $s.Save()
        Write-Host "Shortcut: $lnk"
    }
}
