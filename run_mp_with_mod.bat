@echo off
setlocal
set "GAME_ROOT=%~dp0"
if defined BANNERLORD_GAME_ROOT set "GAME_ROOT=%BANNERLORD_GAME_ROOT%"
if not exist "%GAME_ROOT%\bin\Win64_Shipping_Client\Bannerlord.exe" (
  echo [ERROR] Run run_mp_with_mod_from_game_root.bat from the game installation,
  echo or set BANNERLORD_GAME_ROOT to that installation. No fixed Steam drive is assumed.
  pause
  exit /b 1
)
if not exist "%GAME_ROOT%\run_mp_with_mod_from_game_root.bat" (
  echo [ERROR] The portable Warhost launcher is missing from the game installation.
  pause
  exit /b 1
)
call "%GAME_ROOT%\run_mp_with_mod_from_game_root.bat" %*
exit /b %ERRORLEVEL%
