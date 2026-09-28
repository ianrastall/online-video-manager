# Double-click Build-Installer.bat at the repository root to run this workflow.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot
$out = Join-Path $root 'artifacts\installer'
$buildDirectory = Join-Path $root '.build'
$logDirectory = Join-Path $buildDirectory 'logs'
$log = Join-Path $logDirectory 'build-installer.log'
$buildLock = $null
$transcribing = $false
$result = 0
Push-Location $root
try {
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    try {
        $buildLock = [System.IO.File]::Open((Join-Path $buildDirectory 'installer.lock'),
            [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    } catch {
        throw "Cannot acquire the installer build lock. Another build may be running: $($_.Exception.Message)"
    }
    Start-Transcript -LiteralPath $log -Force | Out-Null
    $transcribing = $true
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Install the .NET 10 SDK and make dotnet available on PATH.'
    }

    [xml]$manifest = Get-Content -LiteralPath 'packaging\AppxManifest.xml' -Raw
    $identity = [string]$manifest.Package.Identity.Name
    $publisher = [string]$manifest.Package.Identity.Publisher
    [xml]$properties = Get-Content -LiteralPath 'Directory.Build.props' -Raw
    $sourceVersion = [version]$properties.Project.PropertyGroup.Version
    $version = [version]::new($sourceVersion.Major, $sourceVersion.Minor,
        [Math]::Max(0, $sourceVersion.Build), [Math]::Max(0, $sourceVersion.Revision))

    # MSIX updates require a greater four-part version and the same package identity.
    # Include installed versions even if local build artifacts have been removed.
    $previousVersions = @(
        Get-ChildItem -LiteralPath $out -Filter 'OnlineVideoManager_*_x64.msix' | ForEach-Object {
            if ($_.Name -match '^OnlineVideoManager_(\d+\.\d+\.\d+\.\d+)_x64\.msix$') {
                [version]$Matches[1]
            }
        }
        Get-AppxPackage -Name $identity | Where-Object Publisher -eq $publisher | ForEach-Object {
            [version]$_.Version
        }
    )
    $latest = $previousVersions | Sort-Object -Descending | Select-Object -First 1
    if ($latest -and $latest -ge $version) {
        $parts = @($latest.Major, $latest.Minor, $latest.Build, $latest.Revision)
        $index = 3
        while ($index -ge 0 -and $parts[$index] -eq 65535) {
            $parts[$index] = 0
            $index--
        }
        if ($index -lt 0) { throw 'The MSIX version cannot be increased beyond 65535.65535.65535.65535.' }
        $parts[$index]++
        $version = [version]($parts -join '.')
    }
    if (@($version.Major, $version.Minor, $version.Build, $version.Revision) |
        Where-Object { $_ -lt 0 -or $_ -gt 65535 }) {
        throw "Package version $version exceeds the MSIX version limits."
    }

    # Prefer the signer of the previous local installer. Never export a private key.
    $now = Get-Date
    $certificates = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object {
        $_.Subject -eq $publisher -and $_.HasPrivateKey -and $_.NotBefore -le $now -and $_.NotAfter -gt $now
    } | Sort-Object NotAfter -Descending)
    $certificate = $null
    $certificateFile = Join-Path $out 'OnlineVideoManager.cer'
    if (Test-Path -LiteralPath $certificateFile) {
        $previousSigner = Get-PfxCertificate -LiteralPath $certificateFile
        $certificate = $certificates | Where-Object Thumbprint -eq $previousSigner.Thumbprint | Select-Object -First 1
    }
    if (-not $certificate) { $certificate = $certificates | Select-Object -First 1 }

    Write-Host "Building Online Video Manager $version (Release, Windows x64)..."
    & "$PSScriptRoot\Build.ps1" -Configuration Release | Out-Host
    dotnet test --project tests/OnlineVideoManager.Tests | Out-Host
    if ($LASTEXITCODE) { throw 'Managed integration tests failed; no installer was created.' }

    if ($certificate) {
        Write-Host "Signing with existing certificate $($certificate.Thumbprint)"
        & "$PSScriptRoot\Package.ps1" -SkipBuild -Version $version.ToString() -Publisher $publisher -CertificateThumbprint $certificate.Thumbprint | Out-Host
        Export-Certificate -Cert $certificate -FilePath $certificateFile -Force | Out-Null
    } else {
        Write-Host 'Creating a development signing certificate in your Windows user certificate store.'
        & "$PSScriptRoot\Package.ps1" -SkipBuild -Version $version.ToString() -Publisher $publisher -DevelopmentCertificate | Out-Host
    }
    $package = Join-Path $out "OnlineVideoManager_$($version)_x64.msix"
    if (-not (Test-Path -LiteralPath $package)) { throw 'Packaging did not produce the expected MSIX.' }
    Write-Host ''
    Write-Host 'BUILD SUCCEEDED'
    Write-Host "Installer: $package"
    Write-Host 'Close OVM, then double-click the MSIX to install or update it.'
    Write-Host 'If Windows has not trusted this signing certificate yet, run the following once in an administrator PowerShell:'
    Write-Host "& '$out\Install.ps1' -TrustDevelopmentCertificate"
    Write-Host "Log: $log"
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($transcribing) { Write-Host "Log: $log" }
    $result = 1
} finally {
    if ($transcribing) { Stop-Transcript | Out-Null }
    if ($buildLock) { $buildLock.Dispose() }
    Pop-Location
}
exit $result
