#!/bin/sh
# Builds and runs tools/load-check against mod/FlappyCrix/FlappyCrix.dll (or $1) with only
# BepInEx.dll and the Unity stand-in beside it - no UnityWebBrowser DLLs, like a normal install.
set -e
cd "$(dirname "$0")"
DLL=$(realpath "${1:-../../mod/FlappyCrix/FlappyCrix.dll}")
W=$(mktemp -d)
cp ../refs/BepInEx.dll "$W/"
mcs -langversion:7.2 -target:library -nowarn:67,169,414,649 -out:"$W/UnityEngine.dll" ../refs/UnityEngine.ref.cs
cp "$DLL" "$W/FlappyCrix.dll"
mcs -langversion:7.2 -r:"$W/BepInEx.dll" -out:"$W/LoadCheck.exe" LoadCheck.cs
cd "$W" && mono LoadCheck.exe "$W/FlappyCrix.dll"; R=$?
rm -rf "$W"
exit $R
