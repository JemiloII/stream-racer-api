#!/usr/bin/env bash
# Build a release: dist/stream-racer-api-<version>.zip (BepInEx + the plugin + INSTALL.txt, what the in-game updater
# downloads) and dist/StreamRacerApiSetup-<version>.exe (that zip inside a setup window: install, update, uninstall).
# <version> is read from src/Plugin.cs. Safe to re-run. Attach both to a GitHub release tagged v<version>.
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

cat > "$stage/INSTALL.txt" <<EOF
Stream Racer API $version

Easiest: download StreamRacerApiSetup-$version.exe from the same release page and run it. It finds the game,
installs, updates and uninstalls.

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

setup=dist/StreamRacerApiSetup-$version.exe
dotnet build installer -c Release -nologo -v q -p:PayloadZip="$PWD/$zip"
cp "installer/bin/Release/Stream Racer API Setup.exe" "$setup"

echo "$zip"
echo "$setup"
