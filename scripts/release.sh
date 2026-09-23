#!/usr/bin/env bash
# Build the release zip: everything a user unzips over the game folder (BepInEx + the plugin + INSTALL.txt).
# Output: dist/stream-racer-api-<version>.zip, <version> read from src/Plugin.cs. Safe to re-run.
set -euo pipefail
cd "$(dirname "$0")/.."
. scripts/bepinex.sh

version=$(sed -n 's/.*Version = "\([0-9][0-9.]*\)".*/\1/p' src/Plugin.cs | head -1)
[ -n "$version" ] || { echo "could not read Version from src/Plugin.cs"; exit 1; }

dotnet build -c Release -nologo -v q
[ -f build/StreamRacerApi.dll ] || { echo "build/StreamRacerApi.dll missing"; exit 1; }

stage=dist/release
zip=dist/stream-racer-api-$version.zip
rm -rf "$stage" "$zip"
mkdir -p "$stage"

unzip -q -o "$(bepinex_zip)" -d "$stage"
mkdir -p "$stage/BepInEx/plugins"
cp build/StreamRacerApi.dll "$stage/BepInEx/plugins/"
cp "scripts/Install Stream Racer API.bat" "$stage/"   # double-click installer: finds the Steam folder, copies everything

cat > "$stage/INSTALL.txt" <<EOF
Stream Racer API $version

Easiest: unzip anywhere, close the game, double-click "Install Stream Racer API.bat". It finds your Steam
folder, copies everything in and tells you when it's done.

By hand:
1. Close Stream Racer.
2. Unzip everything in this archive into the game folder, next to StreamRacer.exe
   (usually C:\Program Files (x86)\Steam\steamapps\common\Stream Racer). Merge folders if asked.
3. Launch the game and open http://localhost:8793 in a browser.

What is inside: BepInEx $BEPINEX_VERSION (winhttp.dll, doorstop_config.ini, BepInEx/core)
and the plugin at BepInEx/plugins/StreamRacerApi.dll. No game file is modified.

Uninstall: delete winhttp.dll, doorstop_config.ini, .doorstop_version and the BepInEx folder.

Antivirus: some scanners flag winhttp.dll (the BepInEx loader) or the plugin's local web server.
See "Antivirus heads-up" in README.md for what each piece does and how to build it yourself.
EOF

# no `zip` binary in Git Bash: python's zipfile does the packing
python - "$stage" "$zip" <<'EOF'
import os, sys, zipfile
stage, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as archive:
    for folder, _, files in os.walk(stage):
        for name in sorted(files):
            path = os.path.join(folder, name)
            archive.write(path, os.path.relpath(path, stage).replace(os.sep, "/"))
EOF

echo "$zip"
