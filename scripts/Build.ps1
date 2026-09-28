param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
& "$PSScriptRoot/Build-Native.ps1" -Configuration $Configuration
Push-Location $root
try {
    dotnet publish src/OnlineVideoManager.App -c $Configuration -o artifacts/app-v2
    if ($LASTEXITCODE) { throw 'WinUI app build failed.' }
    Copy-Item -LiteralPath artifacts/native/ovm_core.dll -Destination artifacts/app-v2/ovm_core.dll
    $licenses = 'artifacts/app-v2/licenses'
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    Copy-Item artifacts/cpp/_deps/json-src/LICENSE.MIT "$licenses/nlohmann-json.txt"
    Copy-Item artifacts/cpp/_deps/miniz-src/LICENSE "$licenses/miniz.txt"
    Write-Output "Built $root\artifacts\app-v2\OnlineVideoManager.exe"
} finally { Pop-Location }
