# Code signing policy

## Application status

BogiHub has applied to the [SignPath Foundation](https://signpath.org/) open-source code signing program.
**Approval is pending.** The [v0.1.1 preview](https://github.com/OppaiMaster7/BogiHub/releases/tag/v0.1.1)
is unsigned, and this repository does not yet have a SignPath signing workflow.

After acceptance and release integration, BogiHub plans to publish signed Windows releases with this attribution:

> Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
> [SignPath Foundation](https://signpath.org/).

This describes the planned arrangement, not an existing approval or signature. Code signing does not change
BogiHub's [MIT License](../LICENSE) or the licences of its [dependencies](../THIRD_PARTY_NOTICES.md).

## Signing scope and release approval

The planned scope is BogiHub's own Windows executables, libraries and installer, built from the
[official repository](https://github.com/OppaiMaster7/BogiHub). Third-party binaries, including SDL,
Avalonia and ViGEmBus, retain their upstream signatures or remain unsigned inside the package; they will not
be signed with BogiHub's SignPath certificate.

For SignPath releases, the build must be traceable to its public source revision and each signing request
must receive manual approval before publication. Signing credentials belong in the build service's secret store.
Team members must use multi-factor authentication for GitHub and SignPath access.
These commitments follow the [SignPath Foundation conditions](https://signpath.org/terms).

| Responsibility | Maintainer |
| --- | --- |
| Author / committer | [OppaiMaster7](https://github.com/OppaiMaster7) |
| Reviewer of outside contributions | [OppaiMaster7](https://github.com/OppaiMaster7) |
| Approver of signing requests, once enabled | [OppaiMaster7](https://github.com/OppaiMaster7) |

## Privacy policy

BogiHub processes controller input and foreground application information locally to select profiles and produce
gamepad, keyboard and mouse output. It has no built-in telemetry, automatic crash uploads or automatic update checks.
Settings, profiles and diagnostic logs are stored under `%APPDATA%\BogiHub`.

Exporting a log saves a local file; it does not upload it. Logs can include profile names, application executable
names, device details and diagnostic events. Review a log before choosing to share it in a public issue.

BogiHub does not send information to network services unless the user requests an action that uses them.
The driver-download buttons open GitHub in the user's browser, where the
[GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement)
applies. The bundled ViGEmBus installer is a separate third-party component; its notices are listed in
[THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

## Signing BogiHub locally

`scripts/build-installer.ps1` supports an optional certificate in the Windows current-user certificate store.
This is separate from the planned SignPath service: applying to SignPath does not provide a local certificate
or enable this build option.

Install the certificate and its private-key provider, then find its thumbprint:

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Format-List Subject, Thumbprint, NotAfter
$env:BOGIHUB_SIGN_CERT = '<40-character SHA1 thumbprint>'
powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1 -SkipReadmeUpdate
```

The script finds Microsoft's Windows SDK signing tool, downloading the pinned Microsoft build-tools package
if necessary. It checks the tool's Microsoft signature, signs only BogiHub's own binaries, and checks their
signatures against the selected certificate. Inno Setup signs the installer, uninstaller and temporary setup
copies during compilation. The final installer signature is checked too. Third-party binaries are left intact.

`BOGIHUB_SIGN_TIMESTAMP` optionally selects an RFC 3161 timestamp service; the default is `http://time.certum.pl`.
Without `BOGIHUB_SIGN_CERT`, the build is unsigned. `-SkipReadmeUpdate` preserves the download metadata for the
published release while testing a local build. Omit it only when preparing the matching release and its new hash.

## Checking a release

Download official installers from [GitHub Releases](https://github.com/OppaiMaster7/BogiHub/releases).
Release notes will identify when SignPath signing is enabled. A policy statement alone does not mean a file is signed.
To inspect the downloaded installer in PowerShell, replace the example filename with the version you downloaded:

```powershell
$signature = Get-AuthenticodeSignature .\BogiHub-Setup-0.1.1.exe
$signature | Format-List Status, StatusMessage
$signature.SignerCertificate | Format-List Subject, Issuer, NotAfter
```

The current unsigned preview reports `NotSigned`. For a future SignPath-signed release, check for `Valid` and
the expected SignPath Foundation signer. Compare the file's SHA-256 hash with that release's published hash as well.
