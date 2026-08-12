@echo off
setlocal
set "GAME_ROOT=C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
set "GAME_EXE=%GAME_ROOT%\bin\Win64_Shipping_Client\Bannerlord.exe"
set "SHADER_CACHE_SWITCH=%GAME_ROOT%\Modules\CoopSpectator\CoopShaderCacheModeSwitch.ps1"
set "MODULES_ARG=_MODULES_*Native*SandBoxCore*Sandbox*Multiplayer*Bannerlord.Harmony*CoopSpectator*_MODULES_"

cd /d "%GAME_ROOT%\bin\Win64_Shipping_Client"
if exist "%SHADER_CACHE_SWITCH%" (
  powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%SHADER_CACHE_SWITCH%" -Phase RunMultiplayer -GameExecutable "%GAME_EXE%" -GameArguments "/multiplayer %MODULES_ARG%" -GameWorkingDirectory "%GAME_ROOT%\bin\Win64_Shipping_Client"
  set "EXIT_CODE=%ERRORLEVEL%"
) else (
  echo [WARN] Shader-cache mode-switch helper not found: "%SHADER_CACHE_SWITCH%"
  "%GAME_EXE%" /multiplayer %MODULES_ARG%
  set "EXIT_CODE=%ERRORLEVEL%"
)

pause
exit /b %EXIT_CODE%
