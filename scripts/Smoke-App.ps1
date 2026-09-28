param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$smokeHome = Join-Path $root ('.build\smoke\app-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $smokeHome -Force | Out-Null
@{watchClipboard=$false; checkForToolUpdatesOnStartup=$false; installToolUpdatesAutomatically=$false} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $smokeHome 'settings.json')
$app = $null
try {
    $app = Start-Process -FilePath "$root\.build\app\OnlineVideoManager.exe" -WindowStyle Hidden `
        -Environment @{OVM_HOME=$smokeHome; OVM_SMOKE_TEST='1'} -PassThru
    if (-not $app.WaitForExit(20000)) { throw 'Application smoke test timed out.' }
    $log = Join-Path $smokeHome 'error.log'
    if (Test-Path -LiteralPath $log) { throw (Get-Content -LiteralPath $log -Raw) }
    if (-not (Test-Path -LiteralPath (Join-Path $smokeHome 'smoke-passed'))) { throw 'Application exited before loading all pages.' }
    Write-Output 'PASS: Published application launched and loaded Downloads, Tools, and Settings.'
} finally {
    if ($app) {
        $app.Refresh()
        if (-not $app.HasExited) { Stop-Process -Id $app.Id; $app.WaitForExit() }
    }
    & "$PSScriptRoot/Remove-BuildDirectory.ps1" -Path $smokeHome
}
