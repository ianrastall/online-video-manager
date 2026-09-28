param([switch]$Live)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$extra = @()
if ($Live) { $extra += '--live' }
dotnet run --project "$root/tests/OnlineVideoManager.NativeSmoke" -c Release -- $root @extra
if ($LASTEXITCODE) { throw 'Native smoke test failed.' }
