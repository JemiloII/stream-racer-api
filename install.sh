#!/usr/bin/env bash
# Build the plugin and drop it (plus BepInEx if missing) into the Stream Racer folder.
set -e
cd "$(dirname "$0")"
GAME="${STREAM_RACER_DIR:-/c/Program Files (x86)/Steam/steamapps/common/Stream Racer}"
BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x64_5.4.23.3.zip"

[ -f "$GAME/StreamRacer.exe" ] || { echo "game not found at $GAME (set STREAM_RACER_DIR)"; exit 1; }

if [ ! -f "$GAME/winhttp.dll" ]; then
  echo "installing BepInEx..."
  tmp=$(mktemp -d); curl -sL -o "$tmp/b.zip" "$BEPINEX_URL"; unzip -q -o "$tmp/b.zip" -d "$GAME"; rm -rf "$tmp"
fi

dotnet build -c Release -nologo -v q
mkdir -p "$GAME/BepInEx/plugins"
cp build/StreamRacerApi.dll "$GAME/BepInEx/plugins/"
echo "installed -> $GAME/BepInEx/plugins/StreamRacerApi.dll"
echo "config appears after first launch: $GAME/BepInEx/config/shibiko.streamracer.api.cfg"
