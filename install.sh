#!/usr/bin/env bash
# Build the plugin and drop it (plus BepInEx if missing) into the Stream Racer folder.
set -e
cd "$(dirname "$0")"
. scripts/bepinex.sh
GAME="${STREAM_RACER_DIR:-/c/Program Files (x86)/Steam/steamapps/common/Stream Racer}"

[ -f "$GAME/StreamRacer.exe" ] || { echo "game not found at $GAME (set STREAM_RACER_DIR)"; exit 1; }

if [ ! -f "$GAME/winhttp.dll" ]; then
  echo "installing BepInEx $BEPINEX_VERSION..."
  unzip -q -o "$(bepinex_zip)" -d "$GAME"
fi

dotnet build -c Release -nologo -v q
mkdir -p "$GAME/BepInEx/plugins"
cp build/StreamRacerApi.dll "$GAME/BepInEx/plugins/"
echo "installed -> $GAME/BepInEx/plugins/StreamRacerApi.dll"
echo "config appears after first launch: $GAME/BepInEx/config/shibiko.streamracer.api.cfg"
