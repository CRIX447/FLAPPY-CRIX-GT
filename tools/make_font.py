#!/usr/bin/env python3
"""Bakes the site's UI font (Bubble Sans, SIL Open Font License 1.1 - see THIRD_PARTY_NOTICES.md)
into the small bitmap font the native game draws its text with: src/FlappyCrix/Native/ui-font.bin,
embedded in FlappyCrix.dll as the resource FlappyCrix.ui-font.bin.

  python3 tools/make_font.py            (needs Pillow)

Format (little-endian): "FCF1", int32 sizeCount; per size: int32 px, ascent, lineHeight, glyphCount;
per glyph: uint16 char, int16 xoff, int16 yoff (from the baseline, down = +), uint16 w, h,
int32 advance in 1/64 px, then w*h 4-bit alpha values packed two per byte (high nibble first).
Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
"""
import os
import struct
from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..")
FONT = os.path.join(ROOT, "mod", "FlappyCrix", "Web", "img", "font.otf")
OUT = os.path.join(ROOT, "src", "FlappyCrix", "Native", "ui-font.bin")
SIZES = [13, 16, 20, 26, 34, 48]
EXTRA = "•×·–—…°©"

cmap = TTFont(FONT).getBestCmap()
chars = [chr(c) for c in range(32, 127)] + [c for c in EXTRA if ord(c) in cmap]

out = bytearray(b"FCF1")
out += struct.pack("<i", len(SIZES))
for px in SIZES:
    f = ImageFont.truetype(FONT, px)
    ascent, descent = f.getmetrics()
    glyphs = bytearray()
    for ch in chars:
        adv = int(round(f.getlength(ch) * 64))
        x0, y0, x1, y1 = f.getbbox(ch, anchor="ls")
        w, h = max(0, x1 - x0), max(0, y1 - y0)
        if ch == " " or w == 0 or h == 0:
            glyphs += struct.pack("<HhhHHi", ord(ch), 0, 0, 0, 0, adv)
            continue
        img = Image.new("L", (w, h), 0)
        ImageDraw.Draw(img).text((-x0, -y0), ch, font=f, fill=255, anchor="ls")
        data = img.tobytes()
        nib = bytearray()
        for i in range(0, len(data), 2):
            a = (data[i] + 8) // 17
            b = (data[i + 1] + 8) // 17 if i + 1 < len(data) else 0
            nib.append((min(15, a) << 4) | min(15, b))
        glyphs += struct.pack("<HhhHHi", ord(ch), x0, y0, w, h, adv) + nib
    out += struct.pack("<iiii", px, ascent, ascent + descent, len(chars)) + glyphs

with open(OUT, "wb") as fo:
    fo.write(out)
print("wrote", OUT, len(out), "bytes,", len(chars), "glyphs x", len(SIZES), "sizes")
