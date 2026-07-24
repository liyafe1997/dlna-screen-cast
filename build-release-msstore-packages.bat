@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Builds unsigned x64 and arm64 MSIX bundles for Microsoft Store submission.
rem The Publisher must exactly match Product identity management in Partner Center.

rem ---------------------------------------------------------------------------
rem Microsoft Store build settings -- edit these values before building.
rem VERSION must contain four numeric parts and end in .0.
rem Identity values must exactly match Partner Center > Product identity.
rem ---------------------------------------------------------------------------
set "VERSION=1.2.0.0"
set "PACKAGE_IDENTITY_NAME=Strawing.DLNAScreenCasting"
set "PUBLISHER=CN=949626B5-BA32-4EDA-B869-88BA07841C59"
set "PUBLISHER_DISPLAY_NAME=Strawing"

if "%PUBLISHER%"=="CN=00000000-0000-0000-0000-000000000000" (
  echo ERROR: Edit PUBLISHER at the top of %~nx0 before building.
  set "RESULT=2"
  goto :finish
)

rem Store package versions must have four numeric parts and Revision must be 0.
set "DDC_STORE_VERSION=%VERSION%"
powershell -NoProfile -Command ^
  "if ($env:DDC_STORE_VERSION -notmatch '^\d+\.\d+\.\d+\.0$') { exit 1 }"
if errorlevel 1 (
  echo ERROR: VERSION must contain four numeric parts and end in .0, for example 1.2.0.0.
  set "RESULT=2"
  goto :finish
)

pushd "%~dp0"

echo.
echo === Building Microsoft Store x64 package ===
powershell -NoProfile -ExecutionPolicy Bypass -File "packaging\scripts\build-msix-x64.ps1" ^
  -Version "%VERSION%" ^
  -PackageIdentityName "%PACKAGE_IDENTITY_NAME%" ^
  -Publisher "%PUBLISHER%" ^
  -PublisherDisplayName "%PUBLISHER_DISPLAY_NAME%" ^
  -NoUnsignedMarker
if errorlevel 1 (
  set "RESULT=%ERRORLEVEL%"
  goto :pop_and_finish
)

echo.
echo === Building Microsoft Store arm64 package ===
powershell -NoProfile -ExecutionPolicy Bypass -File "packaging\scripts\build-msix-arm64.ps1" ^
  -Version "%VERSION%" ^
  -PackageIdentityName "%PACKAGE_IDENTITY_NAME%" ^
  -Publisher "%PUBLISHER%" ^
  -PublisherDisplayName "%PUBLISHER_DISPLAY_NAME%" ^
  -NoUnsignedMarker
if errorlevel 1 (
  set "RESULT=%ERRORLEVEL%"
  goto :pop_and_finish
)

set "RESULT=0"
echo.
echo Microsoft Store packages are ready in:
echo   %CD%\out\msix\artifacts
echo.
echo Upload the unsigned *_x64.msixbundle and *_arm64.msixbundle files.
echo Microsoft Store signs them during certification.

:pop_and_finish
popd
goto :finish

:finish
if not "%RESULT%"=="0" (
  echo.
  echo build-release-msstore-packages FAILED with exit code %RESULT%.
)

rem Keep the window open when launched by double-click (not from a terminal).
echo %CMDCMDLINE% | find /i "%~f0" >nul && pause

exit /b %RESULT%
