# Downloads native dependencies that are not checked into git.
#   SDL3 3.4.18               -> third_party\SDL3 (bundled with the app)
#   ViGEmBus 1.22.0 setup     -> third_party\ViGEmBus (only bundled inside the installer, optional for the user)
# Usage: powershell -ExecutionPolicy Bypass -File scripts/fetch-deps.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

# ---- SDL3 ---------------------------------------------------------------------------------------
$sdlVersion = '3.4.18'
$sdlDir = Join-Path $root 'third_party\SDL3'
$sdlDll = Join-Path $sdlDir 'SDL3.dll'
$stamp = Join-Path $sdlDir 'VERSION'

if ((Test-Path $sdlDll) -and (Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq $sdlVersion)) {
    Write-Host "SDL3 $sdlVersion already present."
}
else {
    New-Item -ItemType Directory -Force $sdlDir | Out-Null
    $zip = Join-Path ([IO.Path]::GetTempPath()) "SDL3-$sdlVersion-win32-x64.zip"
    $url = "https://github.com/libsdl-org/SDL/releases/download/release-$sdlVersion/SDL3-$sdlVersion-win32-x64.zip"

    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing

    $extract = Join-Path ([IO.Path]::GetTempPath()) "SDL3-$sdlVersion-extract"
    if (Test-Path $extract) { Remove-Item -Recurse -Force $extract }
    Expand-Archive $zip -DestinationPath $extract

    Copy-Item (Join-Path $extract 'SDL3.dll') $sdlDll -Force
    Get-ChildItem $extract -Filter '*.txt' | Copy-Item -Destination $sdlDir -Force
    Get-ChildItem $extract -Filter '*.md' | Copy-Item -Destination $sdlDir -Force
    Set-Content -Path $stamp -Value $sdlVersion -Encoding ascii

    $hash = (Get-FileHash $sdlDll -Algorithm SHA256).Hash
    Write-Host "SDL3 $sdlVersion installed to $sdlDir"
    Write-Host "SHA256 SDL3.dll: $hash"

    Remove-Item -Recurse -Force $extract
    Remove-Item -Force $zip
}

# ---- ViGEmBus setup (official signed release, BSD-3-Clause) --------------------------------------
$vigemDir = Join-Path $root 'third_party\ViGEmBus'
$vigemExe = Join-Path $vigemDir 'ViGEmBus_1.22.0_x64_x86_arm64.exe'
$vigemSha = '89220A7865076B342892F98865F3499FB7C4CFD673159E89D352C360FD014C6A'

if ((Test-Path $vigemExe) -and (Get-FileHash $vigemExe -Algorithm SHA256).Hash -eq $vigemSha) {
    Write-Host "ViGEmBus 1.22.0 setup already present."
}
else {
    New-Item -ItemType Directory -Force $vigemDir | Out-Null
    $url = 'https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe'
    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $vigemExe -UseBasicParsing
    Invoke-WebRequest 'https://raw.githubusercontent.com/nefarius/ViGEmBus/v1.22.0/LICENSE' -OutFile (Join-Path $vigemDir 'LICENSE.txt') -UseBasicParsing
    $hash = (Get-FileHash $vigemExe -Algorithm SHA256).Hash
    if ($hash -ne $vigemSha) { Remove-Item $vigemExe; throw "ViGEmBus setup checksum mismatch ($hash)" }
    $sig = Get-AuthenticodeSignature $vigemExe
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike '*Nefarius Software Solutions*') { throw "ViGEmBus setup signature is not valid" }
    Write-Host "ViGEmBus 1.22.0 setup verified (SHA256 + Nefarius signature)."
}
