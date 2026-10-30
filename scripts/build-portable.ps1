# Creates dist\BogiHub-Portable-<version>-win-x64.zip, including the official ViGEmBus installer.
# Run after build-installer.ps1 with -UseExistingPublish to package the same app binaries.
param([switch]$UseExistingPublish, [switch]$SkipReadmeUpdate)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content (Join-Path $root 'src\BogiHub.App\BogiHub.App.csproj')
$version = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part app version.' }

if (-not $UseExistingPublish) { & (Join-Path $PSScriptRoot 'publish.ps1') }
$publish = Join-Path $root 'publish\BogiHub'
foreach ($file in @('BogiHub.exe', 'BogiHub.dll', 'BogiHub.deps.json', 'BogiHub.runtimeconfig.json',
    'SDL3.dll', 'coreclr.dll', 'hostfxr.dll', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $file))) { throw "Missing published file: $file. Run scripts/publish.ps1 first." }
}
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $publish 'BogiHub.dll')).Version
if ($assemblyVersion.ToString(3) -ne $version) { throw 'Published app version does not match the project. Rebuild before packaging.' }

$driverName = 'ViGEmBus_1.22.0_x64_x86_arm64.exe'
$driver = Join-Path $root "third_party\ViGEmBus\$driverName"
$driverHash = '89220A7865076B342892F98865F3499FB7C4CFD673159E89D352C360FD014C6A'
if (-not (Test-Path -LiteralPath $driver) -or (Get-FileHash -LiteralPath $driver).Hash -ne $driverHash) {
    throw 'ViGEmBus installer is missing or has the wrong checksum. Run scripts/fetch-deps.ps1.'
}
$driverSignature = Get-AuthenticodeSignature -LiteralPath $driver
if ($driverSignature.Status -ne 'Valid' -or $driverSignature.SignerCertificate.Subject -notlike '*Nefarius Software Solutions*') {
    throw 'ViGEmBus installer must have a valid Nefarius signature.'
}

# A fresh staging folder prevents leftover files from earlier ZIP builds entering this package.
$stage = Join-Path $root ('artifacts\portable-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object {
    $relative = $_.FullName.Substring($publish.Length + 1)
    $target = Join-Path $stage $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $target
}
New-Item -ItemType Directory -Path (Join-Path $stage 'drivers') | Out-Null
Copy-Item -LiteralPath $driver -Destination (Join-Path $stage "drivers\$driverName")
$instructions = [IO.File]::ReadAllText((Join-Path $root 'docs\portable-START-HERE.txt')).Replace('@VERSION@', $version)
[IO.File]::WriteAllText((Join-Path $stage 'START-HERE.txt'), $instructions, [Text.UTF8Encoding]::new($false))

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zipName = "BogiHub-Portable-$version-win-x64.zip"
$zip = Join-Path $dist $zipName
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporaryZip = Join-Path $dist ([Guid]::NewGuid().ToString('N') + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $false)
Move-Item -LiteralPath $temporaryZip -Destination $zip -Force
$hash = (Get-FileHash -LiteralPath $zip).Hash
$size = (Get-Item -LiteralPath $zip).Length / 1MB
Write-Host "Portable ZIP: $zip"
Write-Host ("Size: {0:N1} MB   SHA256: {1}" -f $size, $hash)

if (-not $SkipReadmeUpdate) {
    $readme = Join-Path $root 'README.md'
    $text = [IO.File]::ReadAllText($readme)
    $text = $text -replace 'releases/download/v[0-9.]+/BogiHub-Portable-[0-9.]+-win-x64\.zip', "releases/download/v$version/$zipName"
    $text = $text -replace 'BogiHub-Portable-[0-9.]+-win-x64\.zip', $zipName
    $text = $text -replace '<!--zip-size-->[^<]*<!--/zip-size-->', ("<!--zip-size-->{0:N0} MB<!--/zip-size-->" -f $size)
    $text = $text -replace '<!--zip-sha256-->`[^`]*`<!--/zip-sha256-->', "<!--zip-sha256-->``$hash``<!--/zip-sha256-->"
    [IO.File]::WriteAllText($readme, $text, [Text.UTF8Encoding]::new($false))
    Write-Host "README.md portable download metadata updated. Upload $zipName to v$version before publishing the page."
}
