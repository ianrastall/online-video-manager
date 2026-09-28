@echo off
setlocal
rem Run from any working directory, including by double-clicking in Explorer.
if not "%~1"=="" if /i not "%~1"=="--no-pause" (
    echo Usage: Build-Installer.bat [--no-pause]
    exit /b 2
)
where pwsh.exe >nul 2>&1
if errorlevel 1 (
    echo ERROR: PowerShell 7 is required. Install it and make pwsh.exe available on PATH.
    if /i not "%~1"=="--no-pause" pause
    exit /b 1
)
pushd "%~dp0"
if errorlevel 1 (
    echo ERROR: Cannot open the project folder.
    if /i not "%~1"=="--no-pause" pause
    exit /b 1
)
pwsh.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build-Installer.ps1"
set "buildExit=%ERRORLEVEL%"
popd
echo.
if not "%buildExit%"=="0" echo BUILD FAILED. See the error and log location above.
if /i not "%~1"=="--no-pause" pause
exit /b %buildExit%
