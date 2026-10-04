#!/bin/sh
# Downloads the official BepInEx 5.4.23.2 release and puts its core BepInEx.dll in tools/refs/
# (only needed to build without Gorilla Tag installed; it is not shipped with the mod).
set -e
cd "$(dirname "$0")"
URL=https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x64_5.4.23.2.zip
curl -sSL -o /tmp/bepinex.zip "$URL"
unzip -oqj /tmp/bepinex.zip BepInEx/core/BepInEx.dll -d refs/
echo "refs/BepInEx.dll ready"
