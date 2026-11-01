// The cabinet's artwork, drawn with the game's own Canvas (so it needs no image files): the lit
// marquee on top, the side art, the control panel with its labels, the coin door and the
// screen bezel. The Unity side puts each picture on its part of the cabinet.
// No UnityEngine references (tested outside Unity).
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;

namespace CrixArcade.Core
{
    public static class CabinetArt
    {
        public sealed class Label { public string Text; public float X, Y; public float Ring; public uint Colour; }

        static readonly uint Ink = Col.Hex("#0A0A0F"), Night = Col.Hex("#141026"), Purple = Col.Hex("#2A1352"),
                             Gold = Col.Hex("#FFD54A"), Green = Col.Hex("#00CC7A"), GreenDark = Col.Hex("#076B41"),
                             White = Col.Hex("#FFFFFF"), Muted = Col.Hex("#A6A6B2"), Steel = Col.Hex("#8C93A6"), SteelDark = Col.Hex("#3A3F4D");

        /// <summary>The lit sign on top: 480 x 192.</summary>
        public static Canvas Marquee(Assets art, uint accent, uint accent2, string title = "FLAPPY CRIX")
        {
            var c = new Canvas(480, 192);
            c.RectVGrad(0, 0, 480, 192, Purple, Ink);
            // sun rays behind the title
            for (int i = 0; i < 18; i++)
            {
                double a0 = i * Math.PI * 2 / 18, a1 = a0 + Math.PI / 30;
                var p = new List<float> { 240, 120, 240 + (float)Math.Cos(a0) * 420, 120 + (float)Math.Sin(a0) * 420, 240 + (float)Math.Cos(a1) * 420, 120 + (float)Math.Sin(a1) * 420 };
                c.Poly(p, accent, 0.10f);
            }
            // a neon floor grid
            for (int i = 0; i <= 12; i++) c.Line(240 + (i - 6) * 26, 140, 240 + (i - 6) * 90, 192, 1.5f, accent, 0.35f);
            for (int i = 0; i < 4; i++) { float y = 140 + i * i * 4 + i * 6; c.Line(0, y, 480, y, 1.5f, accent, 0.35f); }
            c.RectVGrad(0, 120, 480, 22, Purple, Purple, 0.0f);
            // the title
            string[] words = title.Split(' ');
            string w1 = words.Length > 1 ? string.Join(" ", words, 0, words.Length - 1) : title, w2 = words.Length > 1 ? words[words.Length - 1] : "";
            float tw = c.Measure(w1 + " " + w2, 48);
            float x0 = 240 - tw / 2 + 40;
            float wW1 = c.Measure(w1 + " ", 48);
            for (int k = 3; k >= 1; k--) { c.Text(w1, x0, 58 - k, 48, accent, 0.18f); }
            c.Text(w1, x0, 56, 48, White, 1, Align.Left, true);
            c.Text(w2, x0 + wW1, 56, 48, accent2, 1, Align.Left, true);
            c.Text("A R C A D E", 240 + 40, 118, 16, Gold, 1, Align.Centre, true);
            var bird = art?.Get("bird");
            if (bird != null) c.Sprite(bird, x0 - 58, 92, 96, 96, -0.18f);
            var coin = art?.Get("coin");
            if (coin != null) { c.Sprite(coin, 440, 44, 30, 30); c.Sprite(coin, 456, 150, 22, 22, 0.4f); c.Sprite(coin, 30, 160, 20, 20); }
            // frame
            c.RoundRectStroke(4, 4, 472, 184, 14, 4, accent, 0.9f);
            c.RoundRectStroke(10, 10, 460, 172, 10, 1.5f, White, 0.35f);
            return c;
        }

        /// <summary>The side panels: 200 x 620, covering the side's whole outline (front of the cabinet on the right).</summary>
        public static Canvas Side(Assets art, uint body, uint accent, uint accent2)
        {
            var c = new Canvas(200, 620);
            c.RectVGrad(0, 0, 200, 620, Col.Scale(body, 1.4f), Col.Scale(body, 0.6f));
            // two diagonal racing stripes
            var s1 = new List<float> { -40, 620, 40, 620, 260, 180, 180, 180 };
            var s2 = new List<float> { 60, 620, 90, 620, 310, 180, 280, 180 };
            c.Poly(s1, accent, 0.9f);
            c.Poly(s2, accent2, 0.9f);
            // pipes along the bottom
            c.Rect(14, 470, 44, 150, GreenDark); c.Rect(18, 470, 36, 150, Green); c.Rect(10, 456, 52, 18, Green); c.Rect(10, 470, 52, 4, GreenDark);
            c.Rect(140, 520, 44, 100, GreenDark); c.Rect(144, 520, 36, 100, Green); c.Rect(136, 506, 52, 18, Green); c.Rect(136, 520, 52, 4, GreenDark);
            var bird = art?.Get("bird");
            if (bird != null) { c.Circle(100, 300, 74, White, 0.08f); c.Sprite(bird, 100, 300, 130, 130, -0.25f); }
            var coin = art?.Get("coin");
            if (coin != null) { c.Sprite(coin, 160, 230, 26, 26); c.Sprite(coin, 40, 380, 22, 22); }
            c.Text("FLAPPY", 100, 120, 34, White, 1, Align.Centre, true);
            c.Text("CRIX", 100, 158, 48, accent2, 1, Align.Centre, true);
            return c;
        }

        /// <summary>The control panel (512 x 248), with rings and labels at the given places.</summary>
        public static Canvas Panel(uint body, uint accent, IList<Label> labels)
        {
            var c = new Canvas(512, 248);
            c.RectVGrad(0, 0, 512, 248, Col.Scale(body, 0.7f), Col.Scale(body, 1.3f));
            for (int x = -248; x < 512; x += 16) c.Line(x, 248, x + 248, 0, 1, White, 0.025f);
            c.Rect(0, 0, 512, 6, accent);
            c.Rect(0, 242, 512, 6, accent, 0.8f);
            c.Text("1-2 PLAYERS", 16, 14, 13, Muted);
            c.Text("FLAPPY CRIX", 496, 14, 13, accent, 1, Align.Right);
            foreach (var l in labels)
            {
                if (l.Ring > 0)
                {
                    c.Circle(l.X, l.Y, l.Ring + 7, Ink, 0.9f);
                    c.Circle(l.X, l.Y, l.Ring + 4, l.Colour != 0 ? l.Colour : Steel, 0.35f);
                }
                if (!string.IsNullOrEmpty(l.Text)) c.Text(l.Text, l.X, l.Y + l.Ring + 9, 13, White, 0.9f, Align.Centre);
            }
            return c;
        }

        /// <summary>The front below the panel (256 x 352) with the coin door; slot = where the slot is drawn (0..1).</summary>
        public static Canvas Front(uint body, uint accent, bool freePlay, float slotX, float slotY)
        {
            var c = new Canvas(256, 352);
            c.RectVGrad(0, 0, 256, 352, Col.Scale(body, 1.2f), Col.Scale(body, 0.7f));
            float sx = slotX * 256, sy = slotY * 352;
            // the coin door
            float dw = 120, dh = 150, dx = sx - dw / 2, dy = sy - 50;
            c.RoundRect(dx - 4, dy - 4, dw + 8, dh + 8, 10, Ink);
            c.RoundRect(dx, dy, dw, dh, 8, SteelDark);
            c.RectVGrad(dx + 4, dy + 4, dw - 8, dh - 8, Steel, SteelDark);
            c.RoundRect(sx - 20, sy - 20, 40, 40, 6, Ink);                      // the slot plate
            c.RoundRect(sx - 3, sy - 14, 6, 28, 3, freePlay ? Green : accent);   // the lit slot
            c.Text(freePlay ? "FREE" : "25", sx, sy + 24, 13, Ink, 1, Align.Centre);
            c.RoundRect(sx - 26, dy + dh - 46, 52, 32, 6, Ink);                  // coin return
            c.Text("RETURN", sx, dy + dh - 38, 13, Muted, 0.8f, Align.Centre);
            c.RoundRect(dx + 6, dy - 34, dw - 12, 24, 12, freePlay ? Green : accent);
            c.Text(freePlay ? "FREE PLAY" : "INSERT COIN", sx, dy - 31, 16, White, 1, Align.Centre);
            // kick plate
            c.Rect(0, 300, 256, 4, accent);
            c.Text("CRIXGAMING VR", 128, 316, 16, Muted, 0.9f, Align.Centre);
            return c;
        }

        /// <summary>The black frame around the screen (512 x 416); hole = the screen's place (0..1).</summary>
        public static Canvas Bezel(uint accent, float holeX, float holeY, float holeW, float holeH)
        {
            var c = new Canvas(512, 416);
            c.RectVGrad(0, 0, 512, 416, Col.Hex("#1C1C24"), Col.Hex("#050508"));
            float x = holeX * 512, y = holeY * 416, w = holeW * 512, h = holeH * 416;
            c.RoundRect(x - 10, y - 10, w + 20, h + 20, 14, Col.Hex("#000000"));
            c.RoundRectStroke(x - 12, y - 12, w + 24, h + 24, 16, 2.5f, accent, 0.85f);
            c.Text("PRESS START", 120, y + h + 18, 13, Muted, 0.9f, Align.Centre);
            c.Text("RACE PLAYERS NEARBY", 392, y + h + 18, 13, Muted, 0.9f, Align.Centre);
            return c;
        }
    }
}
