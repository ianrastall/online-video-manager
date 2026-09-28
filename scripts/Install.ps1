param([switch]$TrustDevelopmentCertificate)
$ErrorActionPreference = 'Stop'
$package = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.msix' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $package) { throw 'No MSIX found next to this script.' }
if ($TrustDevelopmentCertificate) {
    $certificate = Join-Path $PSScriptRoot 'OnlineVideoManager.cer'
    if (-not (Test-Path -LiteralPath $certificate)) { throw 'Development certificate is missing.' }
    # Explicit opt-in and elevation are required; never change certificate trust on app launch.
    Import-Certificate -FilePath $certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
}
Add-AppxPackage -Path $package.FullName
Write-Output 'Online Video Manager is installed. Launch it from Start.'
