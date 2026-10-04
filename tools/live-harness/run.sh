#!/bin/sh
# Live-site behaviour of the mod's website engine (see LiveTest.cs).
#   tools/live-harness/run.sh [chromium-or-chrome executable] [scenario 1|2|3]
# Needs mono (mcs), python3 and a Chromium/Chrome. Uses tools/refs/BepInEx.dll and the
# Unity stand-in (whose clock and maths behave like Unity's).
# Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
set -e
HERE=$(dirname "$(realpath "$0")")
ROOT="$HERE/../.."
CHROME=${1:-${CHROME:-/opt/pw-browsers/chromium-1194/chrome-linux/chrome}}
W=$(mktemp -d)
cp "$ROOT/tools/refs/BepInEx.dll" "$W/"
mcs -langversion:7.2 -target:library -nowarn:67,169,414,649 -out:"$W/UnityEngine.dll" "$ROOT/tools/refs/UnityEngine.ref.cs"
cp "$ROOT/mod/FlappyCrix/FlappyCrix.dll" "$W/"
mcs -langversion:7.2 -r:"$W/BepInEx.dll" -r:"$W/UnityEngine.dll" -r:"$W/FlappyCrix.dll" -out:"$W/LiveTest.exe" "$HERE/LiveTest.cs"
R=0; cd "$W" && mono LiveTest.exe "$CHROME" "$ROOT/mod/FlappyCrix/Web" "$HERE" "$2" > out.txt 2>&1 || R=$?
grep -v -E '^(\[[0-9]+:[0-9]+:[0-9]{4}/|ALSA lib )' out.txt     # hide Chromium's own console noise
rm -rf "$W"
exit $R
