#!/bin/sh
# Builds mod/FlappyCrix/FlappyCrix.dll without Gorilla Tag installed (Mono mcs).
#   BepInEx.dll  - the real BepInEx 5.4.23.2 core DLL (github.com/BepInEx/BepInEx/releases)
#   *.ref.cs     - reference-only stand-ins for UnityEngine (facade) and UnityWebBrowser 2.2.8,
#                  declaring exactly the members the mod uses with the real signatures.
# The preferred build is still `dotnet build src/FlappyCrix` against the game's own DLLs.
set -e
cd "$(dirname "$0")"
L=-langversion:7.2
[ -f BepInEx.dll ] || { echo "Put BepInEx/core/BepInEx.dll from the BepInEx 5.4.23.2 release in tools/refs/ (tools/get_bepinex.sh does it)"; exit 1; }
mcs $L -target:library -out:UnityEngine.dll UnityEngine.ref.cs
mcs $L -target:library -out:VoltstroStudios.UnityWebBrowser.Shared.dll VoltstroStudios.UnityWebBrowser.Shared.ref.cs
mcs $L -target:library -r:UnityEngine.dll -r:VoltstroStudios.UnityWebBrowser.Shared.dll \
    -out:VoltstroStudios.UnityWebBrowser.dll VoltstroStudios.UnityWebBrowser.ref.cs
mcs $L -target:library -optimize+ -warn:4 -r:UnityEngine.dll -r:BepInEx.dll \
    -resource:../../mod/FlappyCrix/Web/__flappycrix/bridge.js,FlappyCrix.bridge.js \
    -resource:../../src/FlappyCrix/Native/ui-font.bin,FlappyCrix.ui-font.bin \
    -resource:../../src/FlappyCrix/Native/sprites.bin,FlappyCrix.sprites.bin \
    -resource:../../mod/FlappyCrix/Web/filter.js,FlappyCrix.filter.js \
    -r:VoltstroStudios.UnityWebBrowser.dll -r:VoltstroStudios.UnityWebBrowser.Shared.dll \
    -out:../../mod/FlappyCrix/FlappyCrix.dll -recurse:'../../src/FlappyCrix/*.cs'
rm -f UnityEngine.dll VoltstroStudios.UnityWebBrowser.dll VoltstroStudios.UnityWebBrowser.Shared.dll
echo "Built ../../mod/FlappyCrix/FlappyCrix.dll"
