# Builds the Windows installer: dist\BogiHub-Setup-<version>.exe
#   1. publishes a self-contained BogiHub (scripts/publish.ps1, which also fetches SDL3 and ViGEmBus)
#   2. renders the installer artwork from the app's own drawing code (no window opens)
#   3. compiles installer\BogiHub.iss with Inno Setup 6.7+ (winget install JRSoftware.InnoSetup)
#   4. points README.md's download button, install steps, size and SHA256 at this version (commit it with the release,
#      tagged v<version>, or the button links to a release that doesn't exist yet)
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
$sizeMb = (Get-Item $setup).Length / 1MB
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host "Installer: $setup"
Write-Host ("Size: {0:N1} MB   SHA256: {1}" -f $sizeMb, $hash)

# The README's big download button links straight to this version's file on its release (GitHub's "latest" link
# skips pre-releases), so every mention of the installer moves to the new version here.
$readme = Join-Path $root 'README.md'
$text = [IO.File]::ReadAllText($readme)
$text = $text -replace 'releases/download/v[0-9.]+/BogiHub-Setup-[0-9.]+\.exe', "releases/download/v$version/BogiHub-Setup-$version.exe"
$text = $text -replace 'BogiHub-Setup-[0-9.]+\.exe', "BogiHub-Setup-$version.exe"
$text = $text -replace '<!--version-->[^<]*<!--/version-->', "<!--version-->$version<!--/version-->"
$text = $text -replace '<!--size-->[^<]*<!--/size-->', ("<!--size-->{0:N0} MB<!--/size-->" -f $sizeMb)
$text = $text -replace '<!--sha256-->`[^`]*`<!--/sha256-->', "<!--sha256-->``$hash``<!--/sha256-->"
[IO.File]::WriteAllText($readme, $text, (New-Object System.Text.UTF8Encoding $false))
Write-Host "README.md now points at v$version. Publish the release with tag v$version before pushing README.md."
