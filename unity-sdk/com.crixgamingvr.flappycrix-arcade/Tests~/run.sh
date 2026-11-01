#!/bin/sh
# Builds the arcade's game and network code without Unity, runs ArcadeTest and saves a PNG of
# each screen in the folder given (default: a temp folder). Needs mono (mcs); Pillow for PNGs.
# Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).
set -e
HERE=$(dirname "$(realpath "$0")")
PKG="$HERE/.."
OUT=$(realpath -m "${1:-$(mktemp -d)}")
mkdir -p "$OUT"
mcs -langversion:7.2 -optimize+ -nowarn:414,649 -out:"$OUT/arcadetest.exe" \
    "$PKG"/Runtime/Core/*.cs "$PKG"/Runtime/Net/*.cs "$HERE/ArcadeTest.cs"
R=0; mono "$OUT/arcadetest.exe" "$OUT" "$PKG/Runtime/Resources/CrixArcade" || R=$?
python3 - "$OUT" <<'PY'
import glob, os, sys
try:
    from PIL import Image
except ImportError:
    print("(Pillow isn't installed: the screens stay as raw .rgba files, 960x640)"); sys.exit(0)
for f in sorted(glob.glob(os.path.join(sys.argv[1], "*.rgba"))):
    Image.frombytes("RGBA", (960, 640), open(f, "rb").read()).convert("RGB").save(f[:-5] + ".png")
    os.remove(f)
PY
# the cabinet model, built with a stand-in UnityEngine and drawn by render.py (needs numpy)
mcs -langversion:7.2 -nowarn:414,649,67,169 -define:ENABLE_LEGACY_INPUT_MANAGER -out:"$OUT/cabinet.exe" \
    "$HERE/UnityEngineStub.cs" "$PKG"/Runtime/Core/*.cs "$PKG"/Runtime/Net/*.cs "$PKG"/Runtime/Unity/*.cs "$HERE/CabinetPreview.cs"
mono "$OUT/cabinet.exe" "$OUT/cabinet" "$PKG/Runtime/Resources/CrixArcade" || R=$?
python3 "$HERE/render.py" "$OUT/cabinet" || echo "(numpy + Pillow draw the cabinet: $OUT/cabinet/preview.png)"
echo "Screens saved in $OUT"
exit $R
