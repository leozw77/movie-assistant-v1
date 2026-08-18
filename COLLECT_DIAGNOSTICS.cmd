@echo off
setlocal
chcp 65001 >nul
set "SCRIPT=%~dp0scripts\Collect-Diagnostics.ps1"
if not exist "%SCRIPT%" (
  echo Diagnostic script not found: %SCRIPT%
  pause
  exit /b 1
)
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" (
  echo.
  echo Diagnostic collection failed. Exit code: %EXITCODE%
)
pause
exit /b %EXITCODE%
