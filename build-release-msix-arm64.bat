@echo off
setlocal
rem One-click arm64 release build: compiles the app (self-contained publish)
rem and produces .msix / .msixbundle under out\msix\artifacts.
rem Requires the arm64 native runtime (see docs\native-build.md).
rem Usage: build-release-msix-arm64.bat [version]   (default 1.0.0.0)
rem More options (publisher, certificate, ...): packaging\scripts\build-msix.ps1

set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=1.0.0.0"

pushd "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "packaging\scripts\build-msix-arm64.ps1" -Version %VERSION%
set "RESULT=%ERRORLEVEL%"
popd

if not "%RESULT%"=="0" echo. & echo build-release-msix-arm64 FAILED with exit code %RESULT%.

rem Keep the window open when launched by double-click (not from a terminal).
echo %CMDCMDLINE% | find /i "%~f0" >nul && pause

exit /b %RESULT%
