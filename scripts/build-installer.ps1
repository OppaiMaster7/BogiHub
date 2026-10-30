# Builds the Windows installer: dist\BogiHub-Setup-<version>.exe
#   1. publishes a self-contained BogiHub (scripts/publish.ps1, which also fetches SDL3 and ViGEmBus)
#   2. renders the installer artwork from the app's own drawing code (no window opens)
#   3. compiles installer\BogiHub.iss with Inno Setup 6.7+ (winget install JRSoftware.InnoSetup)
#   4. points README.md's download button, install steps, size and SHA256 at this version (commit it with the release,
#      tagged v<version>, or the button links to a release that doesn't exist yet)
# Local signing: set BOGIHUB_SIGN_CERT to your code-signing certificate's SHA1 thumbprint (docs/signing.md).
# Only BogiHub's own binaries and installer components are signed. This is not the SignPath integration.
# BOGIHUB_SIGN_TIMESTAMP overrides the timestamp server (default: Certum's).
# Use -SkipReadmeUpdate for packaging checks that should not change the published download metadata.
# Usage: powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1
param([switch]$SkipReadmeUpdate)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$signCert = $env:BOGIHUB_SIGN_CERT
$timestamp = if ($env:BOGIHUB_SIGN_TIMESTAMP) { $env:BOGIHUB_SIGN_TIMESTAMP } else { 'http://time.certum.pl' }
$signtool = $null
if ($signCert) {
    $signCert = $signCert.Trim().ToUpperInvariant()
    if ($signCert -notmatch '^[0-9A-F]{40}$') { throw 'BOGIHUB_SIGN_CERT must be a 40-character SHA1 certificate thumbprint.' }
    $certificate = Get-Item "Cert:\CurrentUser\My\$signCert" -ErrorAction SilentlyContinue
    if (-not $certificate -or -not $certificate.HasPrivateKey -or
        $certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) {
        throw 'BOGIHUB_SIGN_CERT must identify a current certificate with a private key in Cert:\CurrentUser\My.'
    }
    # This URL is also embedded in Inno Setup's signing command; reject quoting and substitution characters.
    $timestampUri = $null
    if (-not [Uri]::TryCreate($timestamp, [UriKind]::Absolute, [ref]$timestampUri) -or
        $timestampUri.Scheme -notin @('http', 'https') -or $timestamp -match '[\s"$]') {
        throw 'BOGIHUB_SIGN_TIMESTAMP must be an HTTP(S) URL without whitespace, quotes or dollar signs.'
    }
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName | Select-Object -Last 1 -ExpandProperty FullName
    if (-not $signtool) {
        # No Windows SDK: take signtool from Microsoft's own build-tools package, fetched once into artifacts\.
        $tools = Join-Path $root 'artifacts\signtool'
        $signtool = Get-ChildItem $tools -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1 -ExpandProperty FullName
        if (-not $signtool) {
            New-Item -ItemType Directory -Force $tools | Out-Null
            $pkg = Join-Path $tools 'buildtools.zip'
            Invoke-WebRequest -UseBasicParsing 'https://www.nuget.org/api/v2/package/Microsoft.Windows.SDK.BuildTools/10.0.28000.2957' -OutFile $pkg
            Expand-Archive $pkg $tools -Force
            $signtool = Get-ChildItem $tools -Recurse -Filter signtool.exe | Where-Object { $_.FullName -match '\\x64\\' } |
                Select-Object -First 1 -ExpandProperty FullName
        }
    }
    if (-not $signtool) { throw 'Microsoft signtool.exe was not found in the Windows SDK or downloaded build tools.' }
    $toolSig = Get-AuthenticodeSignature $signtool
    if ($toolSig.Status -ne 'Valid' -or $toolSig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw "signtool at $signtool is not signed by Microsoft; not using it"
    }
}
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
try {
    $artProcess = Start-Process (Join-Path $publish 'BogiHub.exe') -WindowStyle Hidden -Wait -PassThru
    if ($artProcess.ExitCode -ne 0) { throw "Installer artwork rendering failed (exit $($artProcess.ExitCode))." }
} finally { Remove-Item Env:BOGIHUB_INSTALLER_ART }
if (-not (Test-Path (Join-Path $art 'wizard-200.png'))) { throw "installer artwork was not rendered" }

$isccArgs = @('/Q', "/DAppVersion=$version", "/DPublishDir=$publish", "/DArtDir=$art")
if ($signCert) {
    # Explicitly select our outputs. Do not re-sign upstream libraries, even if they are unsigned.
    $ownBinaries = @('BogiHub.exe', 'BogiHub.dll', 'BogiHub.Core.dll', 'BogiHub.Devices.Sdl.dll',
        'BogiHub.Output.dll', 'BogiHub.Platform.Windows.dll', 'BogiHub.Runtime.dll') |
        ForEach-Object { Get-Item -LiteralPath (Join-Path $publish $_) -ErrorAction Stop }
    Write-Host "Signing $($ownBinaries.Count) BogiHub files..."
    & $signtool sign /fd sha256 /tr $timestamp /td sha256 /sha1 $signCert /q @($ownBinaries.FullName)
    if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
    foreach ($binary in $ownBinaries) {
        $signature = Get-AuthenticodeSignature $binary.FullName
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $signCert) {
            throw "Signature verification failed for $($binary.Name): $($signature.StatusMessage)"
        }
    }
    # Inno Setup signs Setup, the uninstaller and Setup's temporary copy with this command ($q = quote, $f = the file).
    $isccArgs += @('/DSign=1', "/Sbogihub=`$q$signtool`$q sign /fd sha256 /tr `$q$timestamp`$q /td sha256 /sha1 $signCert `$f")
} else {
    Write-Warning "UNSIGNED build (BOGIHUB_SIGN_CERT isn't set). Windows may block this build; see docs/signing.md."
}
& $iscc @isccArgs (Join-Path $root 'installer\BogiHub.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

$setup = Join-Path $root "dist\BogiHub-Setup-$version.exe"
$sizeMb = (Get-Item $setup).Length / 1MB
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host "Installer: $setup"
Write-Host ("Size: {0:N1} MB   SHA256: {1}" -f $sizeMb, $hash)
if ($signCert) {
    $setupSig = Get-AuthenticodeSignature $setup
    if ($setupSig.Status -ne 'Valid' -or $setupSig.SignerCertificate.Thumbprint -ne $signCert) {
        throw "Installer signature verification failed: $($setupSig.StatusMessage)"
    }
    Write-Host "Signed by: $($setupSig.SignerCertificate.Subject)"
}

if ($SkipReadmeUpdate) {
    Write-Host 'README.md download metadata left unchanged (-SkipReadmeUpdate).'
    return
}

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
