#!/bin/sh
# Builds the in-game version's code (no Unity needed), runs NativeTest and saves a PNG of
# every screen into the folder given (default: a temp folder).
#   tools/native-harness/run.sh [output folder]
# Needs mono (mcs); python3 with Pillow turns the screens into PNGs.
# Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
set -e
HERE=$(dirname "$(realpath "$0")")
ROOT="$HERE/../.."
OUT=$(realpath -m "${1:-$(mktemp -d)}")
mkdir -p "$OUT"
N="$ROOT/src/FlappyCrix/Native"
mcs -langversion:7.2 -optimize+ -nowarn:414,649 -out:"$OUT/nativetest.exe" \
    -resource:"$N/ui-font.bin",FlappyCrix.ui-font.bin -resource:"$N/sprites.bin",FlappyCrix.sprites.bin \
    "$N/Canvas.cs" "$N/Catalog.cs" "$N/SaveData.cs" "$N/Season.cs" "$N/SpriteSheet.cs" \
    "$N/NativeSim.cs" "$N/NativeRenderer.cs" "$N/FlappyApp.cs" "$N/AppRunner.cs" "$HERE/NativeTest.cs"
R=0; mono "$OUT/nativetest.exe" "$OUT" || R=$?
python3 - "$OUT" <<'PY'
import glob, os, sys
try:
    from PIL import Image
except ImportError:
    sys.exit("(Pillow isn't installed: the screens stay as raw .rgba files, 960x640)")
for f in sorted(glob.glob(os.path.join(sys.argv[1], "*.rgba"))):
    Image.frombytes("RGBA", (960, 640), open(f, "rb").read()).convert("RGB").save(f[:-5] + ".png")
    os.remove(f)
PY
echo "Screens saved in $OUT"
exit $R
