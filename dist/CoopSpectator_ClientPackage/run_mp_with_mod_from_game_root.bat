@echo off
setlocal

set "GAME_ROOT=%~dp0"
if "%GAME_ROOT:~-1%"=="\" set "GAME_ROOT=%GAME_ROOT:~0,-1%"

set "GAME_EXE=%GAME_ROOT%\bin\Win64_Shipping_Client\Bannerlord.exe"
set "COOP_MODULE=%GAME_ROOT%\Modules\CoopSpectator\SubModule.xml"
set "COOP_DLL=%GAME_ROOT%\Modules\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll"
set "COOP_HARMONY_DLL=%GAME_ROOT%\Modules\CoopSpectator\bin\Win64_Shipping_Client\0Harmony.dll"
set "HARMONY_MODULE=%GAME_ROOT%\Modules\Bannerlord.Harmony\SubModule.xml"
set "HARMONY_DLL=%GAME_ROOT%\Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client\Bannerlord.Harmony.dll"
set "HARMONY_CORE_DLL=%GAME_ROOT%\Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client\0Harmony.dll"
set "MODULES_ARG=_MODULES_*Native*SandBoxCore*Sandbox*Multiplayer*CoopSpectator*_MODULES_"
set "USE_EXTERNAL_HARMONY=0"
set "DIAG_FILE=%GAME_ROOT%\CoopSpectator_launch_diagnostic.txt"

> "%DIAG_FILE%" echo CoopSpectator launch diagnostic
>> "%DIAG_FILE%" echo GAME_ROOT=%GAME_ROOT%
>> "%DIAG_FILE%" echo GAME_EXE=%GAME_EXE%
>> "%DIAG_FILE%" echo COOP_MODULE=%COOP_MODULE%
>> "%DIAG_FILE%" echo COOP_DLL=%COOP_DLL%
>> "%DIAG_FILE%" echo COOP_HARMONY_DLL=%COOP_HARMONY_DLL%
>> "%DIAG_FILE%" echo HARMONY_MODULE=%HARMONY_MODULE%
>> "%DIAG_FILE%" echo HARMONY_DLL=%HARMONY_DLL%
>> "%DIAG_FILE%" echo HARMONY_CORE_DLL=%HARMONY_CORE_DLL%
if exist "%GAME_EXE%" (>> "%DIAG_FILE%" echo [OK] GAME_EXE) else (>> "%DIAG_FILE%" echo [MISSING] GAME_EXE)
if exist "%COOP_MODULE%" (>> "%DIAG_FILE%" echo [OK] COOP_MODULE) else (>> "%DIAG_FILE%" echo [MISSING] COOP_MODULE)
if exist "%COOP_DLL%" (>> "%DIAG_FILE%" echo [OK] COOP_DLL) else (>> "%DIAG_FILE%" echo [MISSING] COOP_DLL)
if exist "%COOP_HARMONY_DLL%" (>> "%DIAG_FILE%" echo [OK] COOP_HARMONY_DLL) else (>> "%DIAG_FILE%" echo [MISSING] COOP_HARMONY_DLL)
if exist "%HARMONY_MODULE%" (>> "%DIAG_FILE%" echo [OK] HARMONY_MODULE) else (>> "%DIAG_FILE%" echo [MISSING] HARMONY_MODULE)
if exist "%HARMONY_DLL%" (>> "%DIAG_FILE%" echo [OK] HARMONY_DLL) else (>> "%DIAG_FILE%" echo [MISSING] HARMONY_DLL)
if exist "%HARMONY_CORE_DLL%" (>> "%DIAG_FILE%" echo [OK] HARMONY_CORE_DLL) else (>> "%DIAG_FILE%" echo [MISSING] HARMONY_CORE_DLL)
>> "%DIAG_FILE%" echo.
>> "%DIAG_FILE%" echo Root listing:
dir /b "%GAME_ROOT%" >> "%DIAG_FILE%" 2>&1
>> "%DIAG_FILE%" echo.
>> "%DIAG_FILE%" echo Modules listing:
dir /b "%GAME_ROOT%\Modules" >> "%DIAG_FILE%" 2>&1
>> "%DIAG_FILE%" echo.
>> "%DIAG_FILE%" echo CoopSpectator matches:
dir /s /b "%GAME_ROOT%\CoopSpectator*" >> "%DIAG_FILE%" 2>&1
>> "%DIAG_FILE%" echo.
>> "%DIAG_FILE%" echo Harmony DLL matches:
dir /s /b "%GAME_ROOT%\0Harmony.dll" >> "%DIAG_FILE%" 2>&1

if not exist "%GAME_EXE%" (
  echo [ERROR] Bannerlord.exe not found.
  echo Put this .bat into the Mount ^& Blade II Bannerlord game root and run it from there.
  echo Expected path: "%GAME_EXE%"
  echo Diagnostic file: "%DIAG_FILE%"
  pause
  exit /b 1
)

if not exist "%COOP_MODULE%" (
  echo [ERROR] CoopSpectator module not found.
  echo Expected path: "%COOP_MODULE%"
  echo Copy the CoopSpectator folder into "%GAME_ROOT%\Modules" first.
  echo Diagnostic file: "%DIAG_FILE%"
  pause
  exit /b 1
)

if not exist "%COOP_DLL%" (
  echo [ERROR] CoopSpectator.dll not found.
  echo Expected path: "%COOP_DLL%"
  echo Re-copy the full CoopSpectator module, including bin\Win64_Shipping_Client.
  echo Diagnostic file: "%DIAG_FILE%"
  pause
  exit /b 1
)

if not exist "%COOP_HARMONY_DLL%" (
  echo [ERROR] CoopSpectator local 0Harmony.dll not found.
  echo Expected path: "%COOP_HARMONY_DLL%"
  echo Re-copy the full CoopSpectator module, including bundled dependencies.
  echo Diagnostic file: "%DIAG_FILE%"
  pause
  exit /b 1
)

if "%USE_EXTERNAL_HARMONY%"=="1" if exist "%HARMONY_MODULE%" if exist "%HARMONY_DLL%" if exist "%HARMONY_CORE_DLL%" (
  set "MODULES_ARG=_MODULES_*Native*SandBoxCore*Sandbox*Multiplayer*Bannerlord.Harmony*CoopSpectator*_MODULES_"
  echo [INFO] Using external Bannerlord.Harmony module.
) else (
  echo [INFO] Launching with bundled CoopSpectator Harmony runtime only.
)

pushd "%GAME_ROOT%\bin\Win64_Shipping_Client"
"%GAME_EXE%" /multiplayer %MODULES_ARG%
set "EXIT_CODE=%ERRORLEVEL%"
popd

if not "%EXIT_CODE%"=="0" (
  echo.
  echo Bannerlord exited with code %EXIT_CODE%.
)

pause
exit /b %EXIT_CODE%
