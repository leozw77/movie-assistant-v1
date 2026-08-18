@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"

set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS%" (
  echo ERROR: Windows PowerShell was not found.
  echo Expected path: %PS%
  pause
  exit /b 9009
)

if not exist "%~dp0scripts\Build-Preview.ps1" (
  echo ERROR: scripts\Build-Preview.ps1 was not found.
  echo Please extract the complete ZIP before running this file.
  pause
  exit /b 2
)

echo Starting DoubanReview stable build...
"%PS%" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0scripts\Build-Preview.ps1"
set "BUILD_EXIT=%ERRORLEVEL%"

echo.
if not "%BUILD_EXIT%"=="0" (
  echo BUILD FAILED. Exit code: %BUILD_EXIT%
  echo See build-preview.log in this folder for details.
  pause
  exit /b %BUILD_EXIT%
)

echo BUILD SUCCEEDED.
echo Output folder: artifacts
pause
exit /b 0
