#!/usr/bin/env python3
"""Bakes the pictures the in-game (native) version draws into src/FlappyCrix/Native/sprites.bin,
embedded in FlappyCrix.dll as the resource FlappyCrix.sprites.bin, so the DLL works on its own.

Sources: the site's own images in mod/FlappyCrix/Web/img (bird, coin, hats, witch, Santa's
sleigh - Flappy Crix's own art) and the Noto emoji images in img/emoji/72 (Apache 2.0, see
THIRD_PARTY_NOTICES.md), shrunk to the sizes the game draws them at.

  python3 tools/make_sprites.py            (needs Pillow)

Format: "FCS2", int32 count; per sprite: uint8 nameLength, name (UTF-8),
uint16 w, uint16 h, w*h*4 bytes RGBA (rows top-down, straight alpha). Little-endian.
Not compressed on purpose: some Unity games strip .NET's DeflateStream, and this must load in all of them.
Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
"""
import os
import struct
import sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..")
IMG = os.path.join(ROOT, "mod", "FlappyCrix", "Web", "img")
OUT = os.path.join(ROOT, "src", "FlappyCrix", "Native", "sprites.bin")

# name -> longest side in pixels
PICTURES = {
    "bird": 160, "bird-bare": 160, "coin-still": 40,
    "hat-cap": 160, "hat-bucket": 160, "hat-crown": 160,
    "witch": 128, "santasley": 176,
}
EMOJI = """
26a1 1f6e1 1f9f2 1f423 1f3af 1f4af 1f4aa 2753 1f451 1f4b0 1f48e 1f579 1f3c5 1f576 2b50 1f31f
1f383 1f384 1f382 1f381 1f6d2 1f9e2 2699 1f3c6 1f50a 1f507 25b6 1f3a8 1f513 1f512 2705 1f3b5
1f3ae 2728 1f525 1f4a5 1f389 1f319 2600 1f3c1 1f5d3 1f47b 1f480 1f987 1f430 1f388 1f504
""".split()
EMOJI_PX = 48


def load(path, longest):
    im = Image.open(path).convert("RGBA")
    k = longest / max(im.size)
    size = (max(1, round(im.size[0] * k)), max(1, round(im.size[1] * k)))
    # shrink with premultiplied alpha so transparent edges don't go dark
    return im.convert("RGBa").resize(size, Image.LANCZOS).convert("RGBA")


def main():
    items = []
    for name, px in PICTURES.items():
        items.append((name, load(os.path.join(IMG, name + ".png"), px)))
    missing = []
    for code in EMOJI:
        p = os.path.join(IMG, "emoji", "72", code + ".png")
        if not os.path.exists(p):
            missing.append(code)
            continue
        items.append(("emoji/" + code, load(p, EMOJI_PX)))
    body = bytearray(struct.pack("<i", len(items)))
    for name, im in items:
        n = name.encode("utf-8")
        body += struct.pack("<B", len(n)) + n + struct.pack("<HH", im.size[0], im.size[1]) + im.tobytes()
    data = b"FCS2" + bytes(body)
    with open(OUT, "wb") as f:
        f.write(data)
    print("wrote %s: %d sprites, %d bytes" % (os.path.relpath(OUT, ROOT), len(items), len(data)))
    if missing:
        print("missing emoji:", " ".join(missing))
        sys.exit(1)


main()
