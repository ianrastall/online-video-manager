param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Push-Location $root
try {
    dotnet publish src/OnlineVideoManager.Native -c $Configuration -o artifacts/native
    if ($LASTEXITCODE) { throw 'Native engine build failed.' }
    dotnet publish src/OnlineVideoManager.App -c $Configuration -o artifacts/app
    if ($LASTEXITCODE) { throw 'WinUI app build failed.' }
    Copy-Item -LiteralPath artifacts/native/ovm_core.dll -Destination artifacts/app/ovm_core.dll
    Write-Output "Built $root\artifacts\app\OnlineVideoManager.exe"
} finally { Pop-Location }
