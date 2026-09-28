param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Install Visual Studio with Desktop development with C++.' }
$cmake = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$ctest = Join-Path (Split-Path $cmake) 'ctest.exe'
$vsVersion = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationVersion
$generator = if ([version]$vsVersion -ge [version]'18.0') { 'Visual Studio 18 2026' } else { 'Visual Studio 17 2022' }
& $cmake -S $root -B "$root/artifacts/cpp" -G $generator -A x64
if ($LASTEXITCODE) { throw 'CMake configuration failed.' }
& $cmake --build "$root/artifacts/cpp" --config $Configuration --parallel
if ($LASTEXITCODE) { throw 'C++ build failed.' }
& $ctest --test-dir "$root/artifacts/cpp" -C $Configuration --output-on-failure
if ($LASTEXITCODE) { throw 'Native tests failed.' }
