param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
& "$PSScriptRoot/Build-Native.ps1" -Configuration $Configuration
Push-Location $root
try {
    dotnet publish src/OnlineVideoManager.App -c $Configuration -o .build/app
    if ($LASTEXITCODE) { throw 'WinUI app build failed.' }
    Copy-Item -LiteralPath .build/native/ovm_core.dll -Destination .build/app/ovm_core.dll
    $licenses = '.build/app/licenses'
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    Copy-Item .build/cpp/_deps/json-src/LICENSE.MIT "$licenses/nlohmann-json.txt"
    Copy-Item .build/cpp/_deps/miniz-src/LICENSE "$licenses/miniz.txt"
    Write-Output "Built $root\.build\app\OnlineVideoManager.exe"
} finally { Pop-Location }
