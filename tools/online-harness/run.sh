#!/bin/sh
# The mod's online features against stand-ins, and its multiplayer against the website's own
# multiplayer code (needs mono, python3 with websockets + playwright, Chromium, openssl).
#   tools/online-harness/run.sh [path to a crix-website checkout]
# Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
set -e
HERE=$(dirname "$(realpath "$0")")
ROOT="$HERE/../.."
W=$(mktemp -d)
SITE=${1:-$W/crix-website}
[ -d "$SITE" ] || git clone -q --depth 1 https://github.com/CRIX447/crix-website.git "$SITE"
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$W/key.pem" -out "$W/cert.pem" -days 2 -subj "/CN=ns.photonengine.io" \
    -addext "subjectAltName=DNS:ns.photonengine.io,DNS:localhost,IP:127.0.0.1" 2>/dev/null
N="$ROOT/src/FlappyCrix/Native"; O="$ROOT/src/FlappyCrix/Online"; C="$ROOT/src/FlappyCrix/Web/Cdp"
SRC="$N/Canvas.cs $N/Catalog.cs $N/SaveData.cs $N/Season.cs $N/SpriteSheet.cs $N/NativeSim.cs $N/NativeRenderer.cs $O/*.cs $C/MiniJson.cs $C/WebSocketClient.cs"
RES="-resource:$N/ui-font.bin,FlappyCrix.ui-font.bin -resource:$N/sprites.bin,FlappyCrix.sprites.bin -resource:$ROOT/mod/FlappyCrix/Web/filter.js,FlappyCrix.filter.js"
mcs -langversion:7.2 -nowarn:414,649 -out:"$W/ModBot.exe" $RES $SRC "$HERE/ModBot.cs"
mcs -langversion:7.2 -nowarn:414,649 -out:"$W/AccountTest.exe" $RES $SRC "$N/FlappyApp.cs" "$N/FlappyApp.Online.cs" "$HERE/AccountTest.cs"
mcs -langversion:7.2 -nowarn:414,649 -out:"$W/OnlineUiTest.exe" $RES $SRC "$N/FlappyApp.cs" "$N/FlappyApp.Online.cs" "$HERE/OnlineUiTest.cs"
SHOTS=${SHOTS:-$W/shots}; mkdir -p "$SHOTS"
R=0
python3 "$HERE/site_standin.py" --port 8091 > "$W/site.log" 2>&1 &
SITEPID=$!
sleep 1
echo "== accounts (stand-in crixgamingvr.com, Firebase, Firestore, PlayFab)"
mono "$W/AccountTest.exe" http://127.0.0.1:8091 "$W" || R=1
python3 - "$W" <<'PY' || R=1
import sys, cv2, numpy as np
w = sys.argv[1]
img = np.frombuffer(open(w + "/account-code.rgba", "rb").read(), np.uint8).reshape(640, 960, 4)[:, :, :3][:, :, ::-1]
text, pts, _ = cv2.QRCodeDetector().detectAndDecode(np.ascontiguousarray(img))
want = open(w + "/account-code.txt").read().strip()
print(("PASS" if text == want else "FAIL") + " the QR code on the account screen scans to the link page with the code  -- " + repr(text))
sys.exit(0 if text == want else 1)
PY
kill $SITEPID 2>/dev/null || true
echo "== multiplayer: two copies of the in-game version, through their screens (stand-in Photon)"
python3 "$HERE/photon_standin.py" --cert "$W/cert.pem" --key "$W/key.pem" > "$W/photon.log" 2>&1 &
PHOTONPID=$!
sleep 1
mono "$W/OnlineUiTest.exe" "$SHOTS" 2>/dev/null | grep -v "^     \[" || R=1
kill $PHOTONPID 2>/dev/null || true
sleep 0.5
echo "== multiplayer: the mod vs the website's own code (stand-in Photon)"
python3 "$HERE/interop.py" "$SITE" "$W/ModBot.exe" "$W/cert.pem" "$W/key.pem" || R=1
rm -rf "$W"
exit $R
