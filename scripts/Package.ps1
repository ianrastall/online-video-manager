param([switch]$SkipBuild, [switch]$DevelopmentCertificate, [string]$CertificateThumbprint,
    [string]$Publisher = 'CN=Online Video Manager', [string]$Version = '0.1.0.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Use a four-part numeric package version.' }
if (-not $SkipBuild) { & "$PSScriptRoot/Build.ps1" }
$sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
$sdk = Get-ChildItem -LiteralPath $sdkRoot -Directory | Where-Object {
    Test-Path -LiteralPath (Join-Path $_.FullName 'x64\makeappx.exe')
} | Sort-Object { try { [version]$_.Name } catch { [version]'0.0' } } -Descending | Select-Object -First 1
if (-not $sdk) { throw 'Install the Windows SDK (MakeAppx and SignTool).' }
$stage = Join-Path $root ('artifacts\package-stage-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -Path "$root\artifacts\app\*" -Destination $stage -Recurse
if (-not (Test-Path -LiteralPath "$stage\ovm_core.dll")) { throw 'Native engine is missing; run Build.ps1.' }
Copy-Item -LiteralPath "$root\packaging\Assets" -Destination $stage -Recurse
[xml]$manifest = Get-Content -LiteralPath "$root\packaging\AppxManifest.xml" -Raw
$manifest.Package.Identity.Publisher = $Publisher
$manifest.Package.Identity.Version = $Version
$manifest.Save("$stage\AppxManifest.xml")
$out = Join-Path $root 'artifacts\installer'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$package = Join-Path $out "OnlineVideoManager_$($Version)_x64.msix"
& "$($sdk.FullName)\x64\makeappx.exe" pack /d $stage /p $package /o *> "$out\packaging.log"
if ($LASTEXITCODE) { throw 'MSIX packaging failed.' }
if ($DevelopmentCertificate) {
    $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -FriendlyName 'OVM development package signing' `
        -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(1) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
    $CertificateThumbprint = $cert.Thumbprint
    Export-Certificate -Cert $cert -FilePath "$out\OnlineVideoManager.cer" | Out-Null
}
if ($CertificateThumbprint) {
    & "$($sdk.FullName)\x64\signtool.exe" sign /fd SHA256 /sha1 $CertificateThumbprint $package
    if ($LASTEXITCODE) { throw 'Package signing failed. Certificate subject must match Publisher.' }
}
Copy-Item -LiteralPath "$PSScriptRoot\Install.ps1" -Destination $out
Write-Output "MSIX: $package"
if (-not $CertificateThumbprint) { Write-Warning 'Package is unsigned. Sign before installation or distribution.' }
elseif ($DevelopmentCertificate) { Write-Output 'Development-signed. Install.ps1 describes the certificate trust step; no trust was installed automatically.' }
