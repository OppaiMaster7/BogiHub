# Builds the Windows installer: dist\BogiHub-Setup-<version>.exe
#   1. publishes a self-contained BogiHub (scripts/publish.ps1, which also fetches SDL3 and ViGEmBus)
#   2. renders the installer artwork from the app's own drawing code (no window opens)
#   3. compiles installer\BogiHub.iss with Inno Setup 6.7+ (winget install JRSoftware.InnoSetup)
# Usage: powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
[xml]$csproj = Get-Content (Join-Path $root 'src\BogiHub.App\BogiHub.App.csproj')
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "No <Version> in BogiHub.App.csproj" }

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }

& (Join-Path $PSScriptRoot 'publish.ps1')
$publish = Join-Path $root 'publish\BogiHub'

$art = Join-Path $root 'artifacts\installer-art'
$env:BOGIHUB_INSTALLER_ART = $art
try { Start-Process (Join-Path $publish 'BogiHub.exe') -Wait } finally { Remove-Item Env:BOGIHUB_INSTALLER_ART }
if (-not (Test-Path (Join-Path $art 'wizard-200.png'))) { throw "installer artwork was not rendered" }

& $iscc /Q "/DAppVersion=$version" "/DPublishDir=$publish" "/DArtDir=$art" (Join-Path $root 'installer\BogiHub.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

$setup = Join-Path $root "dist\BogiHub-Setup-$version.exe"
Write-Host "Installer: $setup"
Write-Host ("Size: {0:N1} MB   SHA256: {1}" -f ((Get-Item $setup).Length / 1MB), (Get-FileHash $setup -Algorithm SHA256).Hash)
