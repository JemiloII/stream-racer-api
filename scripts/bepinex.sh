#!/usr/bin/env bash
# The one place that knows which BepInEx we ship. Sourced by install.sh and scripts/release.sh.
# bepinex_zip  -> downloads the release zip into .cache/ once (skipped when present) and prints its path.
BEPINEX_VERSION="5.4.23.3"
BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v${BEPINEX_VERSION}/BepInEx_win_x64_${BEPINEX_VERSION}.zip"

bepinex_zip() {
  local root cache zip
  root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
  cache="$root/.cache"
  zip="$cache/BepInEx_win_x64_${BEPINEX_VERSION}.zip"
  if [ ! -s "$zip" ]; then
    mkdir -p "$cache"
    echo "downloading BepInEx ${BEPINEX_VERSION}..." >&2
    curl -fsSL -o "$zip.part" "$BEPINEX_URL" && mv "$zip.part" "$zip"
  fi
  echo "$zip"
}
