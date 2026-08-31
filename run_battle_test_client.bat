@echo off
setlocal

if "%~3"=="" (
  echo Usage: %~nx0 RunId ServerName ExpectedClientModuleSha256 [Port]
  echo.
  echo The server password is never accepted as a command-line argument.
  echo Set COOPSPECTATOR_AUTOMATION_SERVER_PASSWORD in this shell when needed.
  exit /b 2
)

set "RUN_ID=%~1"
set "SERVER_NAME=%~2"
set "EXPECTED_HASH=%~3"
set "SERVER_PORT=%~4"
if "%SERVER_PORT%"=="" set "SERVER_PORT=7210"

set "LAUNCH_SCRIPT=%~dp0scripts\Start-CoopBattleTestClient.ps1"
if not exist "%LAUNCH_SCRIPT%" (
  echo [ERROR] Launcher script not found: "%LAUNCH_SCRIPT%"
  exit /b 3
)

where pwsh.exe >nul 2>&1
if %ERRORLEVEL%==0 (
  set "POWERSHELL_EXE=pwsh.exe"
) else (
  set "POWERSHELL_EXE=powershell.exe"
)

"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%LAUNCH_SCRIPT%" -RunId "%RUN_ID%" -ServerName "%SERVER_NAME%" -ExpectedClientModuleSha256 "%EXPECTED_HASH%" -Port %SERVER_PORT%
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
  echo [ERROR] Battle-test client launcher failed with exit code %EXIT_CODE%.
)

exit /b %EXIT_CODE%
