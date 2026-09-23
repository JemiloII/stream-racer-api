| %SystemRoot%System32ind.exe /I "SteamPath"@echo off
| %SystemRoot%System32ind.exe /I "SteamPath"setlocal EnableDelayedExpansion
| %SystemRoot%System32ind.exe /I "SteamPath"title Stream Racer API installer
| %SystemRoot%System32ind.exe /I "SteamPath"echo.
| %SystemRoot%System32ind.exe /I "SteamPath"echo  Stream Racer API - installer
| %SystemRoot%System32ind.exe /I "SteamPath"echo  ============================
| %SystemRoot%System32ind.exe /I "SteamPath"echo.
| %SystemRoot%System32ind.exe /I "SteamPath"
| %SystemRoot%System32ind.exe /I "SteamPath"rem Where the zip was unpacked (this script sits next to winhttp.dll and the BepInEx folder).
| %SystemRoot%System32ind.exe /I "SteamPath"set "SRC=%~dp0"
| %SystemRoot%System32ind.exe /I "SteamPath"if not exist "%SRC%winhttp.dll" (
| %SystemRoot%System32ind.exe /I "SteamPath"  echo  This script must stay inside the unzipped folder, next to winhttp.dll and BepInEx\.
| %SystemRoot%System32ind.exe /I "SteamPath"  echo  Unzip the whole archive first, then double-click this file again.
| %SystemRoot%System32ind.exe /I "SteamPath"  goto :fail
| %SystemRoot%System32ind.exe /I "SteamPath")
| %SystemRoot%System32ind.exe /I "SteamPath"
| %SystemRoot%System32ind.exe /I "SteamPath"rem The game must be closed: Windows locks the DLLs while it runs.
| %SystemRoot%System32ind.exe /I "SteamPath"tasklist /FI "IMAGENAME eq StreamRacer.exe" 2>nul | %SystemRoot%System32ind.exe /I "StreamRacer.exe" >nul
| %SystemRoot%System32ind.exe /I "SteamPath"if not errorlevel 1 (
| %SystemRoot%System32ind.exe /I "SteamPath"  echo  Stream Racer is running. Close the game, then run this again.
| %SystemRoot%System32ind.exe /I "SteamPath"  goto :fail
| %SystemRoot%System32ind.exe /I "SteamPath")
| %SystemRoot%System32ind.exe /I "SteamPath"
| %SystemRoot%System32ind.exe /I "SteamPath"set "GAME="
| %SystemRoot%System32ind.exe /I "SteamPath"if defined SR_GAME_DIR if exist "%SR_GAME_DIR%\StreamRacer.exe" set "GAME=%SR_GAME_DIR%"
| %SystemRoot%System32ind.exe /I "SteamPath"
| %SystemRoot%System32ind.exe /I "SteamPath"rem 1. Steam's own install folder.
| %SystemRoot%System32ind.exe /I "SteamPath"if not defined GAME (
| %SystemRoot%System32ind.exe /I "SteamPath"  for /f "tokens=2,*" %%A in ('reg query "HKCU\Software\Valve\Steam" /v SteamPath 2^>nul ^| %SystemRoot%System32ind.exe /I "SteamPath"') do set "STEAM=%%B"
| %SystemRoot%System32ind.exe /I "SteamPath"  set "STEAM=!STEAM:/=\!"
| %SystemRoot%System32ind.exe /I "SteamPath"  if exist "!STEAM!\steamapps\common\Stream Racer\StreamRacer.exe" set "GAME=!STEAM!\steamapps\common\Stream Racer"
| %SystemRoot%System32ind.exe /I "SteamPath")
| %SystemRoot%System32ind.exe /I "SteamPath"
| %SystemRoot%System32ind.exe /I "SteamPath"rem 2. Every other Steam library listed in libraryfolders.vdf.
| %SystemRoot%System32ind.exe /I "SteamPath"if not defined GAME if defined STEAM if exist "!STEAM!\steamapps\libraryfolders.vdf" (
| %SystemRoot%System32ind.exe /I "SteamPath"  for /f "tokens=1,2,*" %%A in ('type "!STEAM!\steamapps\libraryfolders.vdf" ^| %SystemRoot%System32ind.exe /I "\"path\""') do (
| %SystemRoot%System32ind.exe /I "SteamPath"    set "LIB=%%~B"
| %SystemRoot%System32ind.exe /I "SteamPath"    set "LIB=!LIB:\\=\!"
| %SystemRoot%System32ind.exe /I "SteamPath"    if not defined GAME if exist "!LIB!\steamapps\common\Stream Racer\StreamRacer.exe" set "GAME=!LIB!\steamapps\common\Stream Racer"
| %SystemRoot%System32ind.exe /I "SteamPath"  )
| %SystemRoot%System32ind.exe /I "SteamPath")
| %SystemRoot%System32ind.exe /I "SteamPath"
| %SystemRoot%System32ind.exe /I "SteamPath"rem 3. The usual spots, in case the registry is unhelpful.
| %SystemRoot%System32ind.exe /I "SteamPath"if not defined GAME for %%D in ("C:\Program Files (x86)\Steam" "C:\Program Files\Steam" "D:\Steam" "D:\SteamLibrary" "E:\SteamLibrary") do (
| %SystemRoot%System32ind.exe /I "SteamPath"  if not defined GAME if exist "%%~D\steamapps\common\Stream Racer\StreamRacer.exe" set "GAME=%%~D\steamapps\common\Stream Racer"
| %SystemRoot%System32ind.exe /I "SteamPath")
| %SystemRoot%System32ind.exe /I "SteamPath"
| %SystemRoot%System32ind.exe /I "SteamPath"rem 4. Ask.
| %SystemRoot%System32ind.exe /I "SteamPath"if not defined GAME (
| %SystemRoot%System32ind.exe /I "SteamPath"  echo  Could not find Stream Racer on this PC.
| %SystemRoot%System32ind.exe /I "SteamPath"  echo  In Steam: right-click the game ^> Manage ^> Browse local files, then paste that folder here.
| %SystemRoot%System32ind.exe /I "SteamPath"  set /p "GAME=  Game folder: "
| %SystemRoot%System32ind.exe /I "SteamPath"  set "GAME=!GAME:"=!"
| %SystemRoot%System32ind.exe /I "SteamPath"  if not exist "!GAME!\StreamRacer.exe" (
| %SystemRoot%System32ind.exe /I "SteamPath"    echo  No StreamRacer.exe in "!GAME!".
| %SystemRoot%System32ind.exe /I "SteamPath"    goto :fail
| %SystemRoot%System32ind.exe /I "SteamPath"  )
| %SystemRoot%System32ind.exe /I "SteamPath")
| %SystemRoot%System32ind.exe /I "SteamPath"
| %SystemRoot%System32ind.exe /I "SteamPath"echo  Game folder: %GAME%
| %SystemRoot%System32ind.exe /I "SteamPath"echo  Installing...
| %SystemRoot%System32ind.exe /I "SteamPath"xcopy "%SRC%winhttp.dll" "%GAME%\" /Y /Q >nul || goto :copyfail
| %SystemRoot%System32ind.exe /I "SteamPath"xcopy "%SRC%doorstop_config.ini" "%GAME%\" /Y /Q >nul || goto :copyfail
| %SystemRoot%System32ind.exe /I "SteamPath"if exist "%SRC%.doorstop_version" xcopy "%SRC%.doorstop_version" "%GAME%\" /Y /Q /H >nul
| %SystemRoot%System32ind.exe /I "SteamPath"xcopy "%SRC%BepInEx" "%GAME%\BepInEx\" /E /I /Y /Q >nul || goto :copyfail
| %SystemRoot%System32ind.exe /I "SteamPath"
| %SystemRoot%System32ind.exe /I "SteamPath"echo.
| %SystemRoot%System32ind.exe /I "SteamPath"echo  Done. Launch Stream Racer, then open  http://localhost:8793  in your browser.
| %SystemRoot%System32ind.exe /I "SteamPath"echo  (Your antivirus may ask about winhttp.dll: that is BepInEx, the mod loader. See README.)
| %SystemRoot%System32ind.exe /I "SteamPath"echo.
| %SystemRoot%System32ind.exe /I "SteamPath"pause
| %SystemRoot%System32ind.exe /I "SteamPath"exit /b 0
| %SystemRoot%System32ind.exe /I "SteamPath"
| %SystemRoot%System32ind.exe /I "SteamPath":copyfail
| %SystemRoot%System32ind.exe /I "SteamPath"echo  Copy failed. Is the game folder read-only? Try running this as administrator.
| %SystemRoot%System32ind.exe /I "SteamPath":fail
| %SystemRoot%System32ind.exe /I "SteamPath"echo.
| %SystemRoot%System32ind.exe /I "SteamPath"pause
| %SystemRoot%System32ind.exe /I "SteamPath"exit /b 1
