@echo off
setlocal EnableDelayedExpansion
title Stream Racer API installer
set "FIND=%SystemRoot%\System32\find.exe"
echo.
echo  Stream Racer API - installer
echo  ============================
echo.

rem Where the zip was unpacked (this script sits next to winhttp.dll and the BepInEx folder).
set "SRC=%~dp0"
if not exist "%SRC%winhttp.dll" (
  echo  This script must stay inside the unzipped folder, next to winhttp.dll and BepInEx\.
  echo  Unzip the whole archive first, then double-click this file again.
  goto :fail
)

rem The game must be closed: Windows locks the DLLs while it runs.
tasklist /FI "IMAGENAME eq StreamRacer.exe" 2>nul | "%FIND%" /I "StreamRacer.exe" >nul
if not errorlevel 1 (
  echo  Stream Racer is running. Close the game, then run this again.
  goto :fail
)

set "GAME="
if defined SR_GAME_DIR if exist "%SR_GAME_DIR%\StreamRacer.exe" set "GAME=%SR_GAME_DIR%"

rem 1. Steam's own install folder.
if not defined GAME (
  for /f "tokens=2,*" %%A in ('reg query "HKCU\Software\Valve\Steam" /v SteamPath 2^>nul ^| "%FIND%" /I "SteamPath"') do set "STEAM=%%B"
  if defined STEAM set "STEAM=!STEAM:/=\!"
  if defined STEAM if exist "!STEAM!\steamapps\common\Stream Racer\StreamRacer.exe" set "GAME=!STEAM!\steamapps\common\Stream Racer"
)

rem 2. Every other Steam library listed in libraryfolders.vdf.
if not defined GAME if defined STEAM if exist "!STEAM!\steamapps\libraryfolders.vdf" (
  for /f "tokens=1,2,*" %%A in ('type "!STEAM!\steamapps\libraryfolders.vdf" ^| "%FIND%" /I "\"path\""') do (
    set "LIB=%%~B"
    set "LIB=!LIB:\\=\!"
    if not defined GAME if exist "!LIB!\steamapps\common\Stream Racer\StreamRacer.exe" set "GAME=!LIB!\steamapps\common\Stream Racer"
  )
)

rem 3. The usual spots, in case the registry is unhelpful.
if not defined GAME for %%D in ("C:\Program Files (x86)\Steam" "C:\Program Files\Steam" "D:\Steam" "D:\SteamLibrary" "E:\SteamLibrary") do (
  if not defined GAME if exist "%%~D\steamapps\common\Stream Racer\StreamRacer.exe" set "GAME=%%~D\steamapps\common\Stream Racer"
)

rem 4. Ask.
if not defined GAME (
  echo  Could not find Stream Racer on this PC.
  echo  In Steam: right-click the game ^> Manage ^> Browse local files, then paste that folder here.
  set /p "GAME=  Game folder: "
  set "GAME=!GAME:"=!"
  if not exist "!GAME!\StreamRacer.exe" (
    echo  No StreamRacer.exe in "!GAME!".
    goto :fail
  )
)

echo  Game folder: %GAME%
echo  Installing...
xcopy "%SRC%winhttp.dll" "%GAME%\" /Y /Q >nul || goto :copyfail
xcopy "%SRC%doorstop_config.ini" "%GAME%\" /Y /Q >nul || goto :copyfail
if exist "%SRC%.doorstop_version" xcopy "%SRC%.doorstop_version" "%GAME%\" /Y /Q /H >nul
xcopy "%SRC%BepInEx" "%GAME%\BepInEx\" /E /I /Y /Q >nul || goto :copyfail

echo.
echo  Done. Launch Stream Racer, then open  http://localhost:8793  in your browser.
echo  (Your antivirus may ask about winhttp.dll: that is BepInEx, the mod loader. See README.)
echo.
pause
exit /b 0

:copyfail
echo  Copy failed. Is the game folder read-only? Try running this as administrator.
:fail
echo.
pause
exit /b 1
