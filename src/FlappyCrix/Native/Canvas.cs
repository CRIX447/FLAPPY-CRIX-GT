// A small software canvas for the native game: anti-aliased shapes, gradients, rotated
// sprites and text, drawn straight into one RGBA pixel buffer (rows top-down) that is
// uploaded as a single texture. Colours are packed like PixelFont: r | g<<8 | b<<16 | a<<24.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;

namespace FlappyCrix.Native
{
    public sealed class Sprite
    {
        public readonly int W, H;
        public readonly uint[] Px;
        public Sprite(uint[] px, int w, int h) { Px = px; W = w; H = h; }
        public float Aspect => H == 0 ? 1 : W / (float)H;
    }

    public enum Align { Left, Centre, Right }

    public static class Col
    {
        public static uint Hex(string hex)
        {
            hex = hex.TrimStart('#');
            byte r = Convert.ToByte(hex.Substring(0, 2), 16), g = Convert.ToByte(hex.Substring(2, 2), 16), b = Convert.ToByte(hex.Substring(4, 2), 16);
            return Rgb(r, g, b);
        }
        public static uint Rgb(int r, int g, int b) => (uint)(Clamp(r) | (Clamp(g) << 8) | (Clamp(b) << 16) | (255u << 24));
        public static int R(uint c) => (int)(c & 0xFF);
        public static int G(uint c) => (int)((c >> 8) & 0xFF);
        public static int B(uint c) => (int)((c >> 16) & 0xFF);
        public static uint Lerp(uint a, uint b, float t)
        {
            if (t <= 0) return a; if (t >= 1) return b;
            return Rgb((int)(R(a) + (R(b) - R(a)) * t), (int)(G(a) + (G(b) - G(a)) * t), (int)(B(a) + (B(b) - B(a)) * t));
        }
        public static uint Scale(uint c, float k) => Rgb((int)(R(c) * k), (int)(G(c) * k), (int)(B(c) * k));
        static uint Clamp(int v) => (uint)(v < 0 ? 0 : v > 255 ? 255 : v);

        public static readonly uint White = Rgb(255, 255, 255), Black = Rgb(0, 0, 0);
    }

    public sealed class Canvas
    {
        public readonly int W, H;
        public readonly uint[] Px;

        /// <summary>Everything drawn is moved by this (screen shake, the play area's position).</summary>
        public float OX, OY;
        /// <summary>Drawing is limited to this rectangle (in pixels, after OX/OY).</summary>
        public int ClipX0, ClipY0, ClipX1, ClipY1;

        private float[] cov;
        private BakedFont font;

        public Canvas(int w, int h)
        {
            W = w; H = h; Px = new uint[w * h];
            cov = new float[w + 2];
            ResetClip();
        }

        public BakedFont Font { get { return font ?? (font = BakedFont.Embedded()); } set { font = value; } }

        public void ResetClip() { ClipX0 = 0; ClipY0 = 0; ClipX1 = W; ClipY1 = H; }
        public void Clip(float x, float y, float w, float h)
        {
            ClipX0 = Math.Max(0, (int)(x + OX)); ClipY0 = Math.Max(0, (int)(y + OY));
            ClipX1 = Math.Min(W, (int)Math.Ceiling(x + w + OX)); ClipY1 = Math.Min(H, (int)Math.Ceiling(y + h + OY));
        }

        // ------------------------------------------------------------------ pixels

        private void Blend(int i, uint c, float a)
        {
            if (a >= 0.996f) { Px[i] = c | 0xFF000000; return; }
            if (a <= 0.002f) return;
            Px[i] = Mix(Px[i], c, (uint)(a * 256));
        }

        /// <summary>Blends c over d with k/256 (integer maths on packed channels).</summary>
        private static uint Mix(uint d, uint c, uint k)
        {
            uint ik = 256 - k;
            uint rb = (((d & 0xFF00FF) * ik + (c & 0xFF00FF) * k) >> 8) & 0xFF00FF;
            uint g = (((d & 0x00FF00) * ik + (c & 0x00FF00) * k) >> 8) & 0x00FF00;
            return rb | g | 0xFF000000;
        }

        public void Clear(uint c)
        {
            c |= 0xFF000000;
            for (int y = ClipY0; y < ClipY1; y++)
                for (int x = ClipX0, i = y * W + ClipX0; x < ClipX1; x++, i++) Px[i] = c;
        }

        /// <summary>A solid rectangle; fractional edges are blended.</summary>
        public void Rect(float x, float y, float w, float h, uint c, float a = 1f)
        {
            if (w <= 0 || h <= 0 || a <= 0) return;
            x += OX; y += OY;
            float x1 = x + w, y1 = y + h;
            int ix0 = Math.Max(ClipX0, (int)Math.Floor(x)), ix1 = Math.Min(ClipX1, (int)Math.Ceiling(x1));
            int iy0 = Math.Max(ClipY0, (int)Math.Floor(y)), iy1 = Math.Min(ClipY1, (int)Math.Ceiling(y1));
            if (ix1 <= ix0 || iy1 <= iy0) return;
            // columns fully inside
            int fx0 = Math.Max(ix0, (int)Math.Ceiling(x)), fx1 = Math.Min(ix1, (int)Math.Floor(x1));
            uint solid = c | 0xFF000000;
            for (int py = iy0; py < iy1; py++)
            {
                float cy = Math.Min(py + 1, y1) - Math.Max(py, y);
                float ar = a * cy;
                int row = py * W;
                if (ix0 < fx0) Blend(row + ix0, c, ar * (Math.Min(ix0 + 1, x1) - Math.Max(ix0, x)));
                if (ar >= 0.996f) for (int i = row + fx0, e = row + fx1; i < e; i++) Px[i] = solid;
                else if (ar > 0.002f)
                {
                    uint k = (uint)(ar * 256);
                    for (int i = row + fx0, e = row + fx1; i < e; i++) Px[i] = Mix(Px[i], c, k);
                }
                if (fx1 < ix1 && fx1 >= fx0) Blend(row + fx1, c, ar * (Math.Min(fx1 + 1, x1) - Math.Max(fx1, x)));
                else if (fx1 < fx0 && ix1 - 1 != ix0) Blend(row + ix1 - 1, c, ar * (Math.Min(ix1, x1) - Math.Max(ix1 - 1, x)));
            }
        }

        /// <summary>Horizontal gradient (stops 0..1) across [gx, gx+gw], filling the rectangle.</summary>
        public void RectHGrad(float x, float y, float w, float h, float gx, float gw, float[] stops, uint[] cols, float a = 1f)
        {
            int ix0 = Math.Max(ClipX0, (int)Math.Floor(x + OX)), ix1 = Math.Min(ClipX1, (int)Math.Ceiling(x + w + OX));
            int iy0 = Math.Max(ClipY0, (int)Math.Floor(y + OY)), iy1 = Math.Min(ClipY1, (int)Math.Ceiling(y + h + OY));
            if (ix1 <= ix0 || iy1 <= iy0) return;
            float top = y + OY, bottom = y + h + OY;
            for (int px = ix0; px < ix1; px++)
            {
                float lx = px + 0.5f - OX;
                uint c = Grad(stops, cols, (lx - gx) / gw);
                float cx = Math.Min(px + 1, x + w + OX) - Math.Max(px, x + OX);
                for (int py = iy0; py < iy1; py++)
                {
                    float cy = py < top || py + 1 > bottom ? Math.Min(py + 1, bottom) - Math.Max(py, top) : 1f;
                    Blend(py * W + px, c, a * cx * cy);
                }
            }
        }

        public void RectVGrad(float x, float y, float w, float h, uint top, uint bottom, float a = 1f)
        {
            int ix0 = Math.Max(ClipX0, (int)Math.Floor(x + OX)), ix1 = Math.Min(ClipX1, (int)Math.Ceiling(x + w + OX));
            int iy0 = Math.Max(ClipY0, (int)Math.Floor(y + OY)), iy1 = Math.Min(ClipY1, (int)Math.Ceiling(y + h + OY));
            for (int py = iy0; py < iy1; py++)
            {
                uint c = Col.Lerp(top, bottom, (py + 0.5f - OY - y) / h) | 0xFF000000;
                int row = py * W;
                if (a >= 0.996f) for (int i = row + ix0, e = row + ix1; i < e; i++) Px[i] = c;
                else for (int px = ix0; px < ix1; px++) Blend(row + px, c, a);
            }
        }

        /// <summary>Copies a block of pixels from another buffer of the same width (a cached layer).</summary>
        public void CopyFrom(uint[] src, int x, int y, int w, int h)
        {
            for (int py = y; py < y + h; py++) Array.Copy(src, py * W + x, Px, py * W + x, w);
        }

        public static uint Grad(float[] stops, uint[] cols, float f)
        {
            if (f <= stops[0]) return cols[0];
            for (int k = 0; k < stops.Length - 1; k++)
                if (f <= stops[k + 1]) return Col.Lerp(cols[k], cols[k + 1], (f - stops[k]) / (stops[k + 1] - stops[k]));
            return cols[cols.Length - 1];
        }

        // ------------------------------------------------------------------ polygons

        /// <summary>
        /// Fills a polygon (even-odd) with anti-aliasing: 4 sub-rows per pixel row, exact horizontal coverage.
        /// shade (optional) gives the colour per pixel.
        /// </summary>
        public void Poly(List<float> xy, uint c, float a = 1f, Func<int, int, uint> shade = null)
        {
            int n = xy.Count / 2;
            if (n < 3 || a <= 0) return;
            float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float px = xy[2 * i] + OX, py = xy[2 * i + 1] + OY;
                if (py < minY) minY = py; if (py > maxY) maxY = py;
                if (px < minX) minX = px; if (px > maxX) maxX = px;
            }
            int y0 = Math.Max(ClipY0, (int)Math.Floor(minY)), y1 = Math.Min(ClipY1, (int)Math.Ceiling(maxY));
            int x0 = Math.Max(ClipX0, (int)Math.Floor(minX)), x1 = Math.Min(ClipX1, (int)Math.Ceiling(maxX));
            if (y1 <= y0 || x1 <= x0) return;
            const int S = 4;
            var xs = this.xs;
            for (int py = y0; py < y1; py++)
            {
                int lo = int.MaxValue, hi = int.MinValue;
                for (int s = 0; s < S; s++)
                {
                    float sy = py + (s + 0.5f) / S;
                    xs.Clear();
                    for (int i = 0, j = n - 1; i < n; j = i++)
                    {
                        float ay = xy[2 * j + 1] + OY, by = xy[2 * i + 1] + OY;
                        if ((ay <= sy && by > sy) || (by <= sy && ay > sy))
                        {
                            float ax = xy[2 * j] + OX, bx = xy[2 * i] + OX;
                            xs.Add(ax + (sy - ay) / (by - ay) * (bx - ax));
                        }
                    }
                    if (xs.Count < 2) continue;
                    if (xs.Count == 2) { if (xs[0] > xs[1]) { float t = xs[0]; xs[0] = xs[1]; xs[1] = t; } }
                    else xs.Sort();
                    for (int k = 0; k + 1 < xs.Count; k += 2)
                    {
                        float l = Math.Max(x0, xs[k]), r = Math.Min(x1, xs[k + 1]);
                        if (r <= l) continue;
                        int il = (int)l, ir = (int)r;
                        if (il < lo) lo = il;
                        if (ir + 1 > hi) hi = ir + 1;
                        if (il == ir) { cov[il] += (r - l) / S; continue; }
                        cov[il] += (il + 1 - l) / S;
                        for (int q = il + 1; q < ir; q++) cov[q] += 1f / S;
                        if (ir < x1) cov[ir] += (r - ir) / S;
                    }
                }
                if (lo == int.MaxValue) continue;
                int row = py * W, end = Math.Min(hi, x1);
                for (int px = lo; px < end; px++)
                {
                    float k = cov[px];
                    if (k <= 0.002f) continue;
                    Blend(row + px, shade != null ? shade(px, py) : c, a * Math.Min(1f, k));
                }
                Array.Clear(cov, lo, Math.Min(cov.Length, hi + 1) - lo);       // leave it all zero for the next row
            }
        }

        private readonly List<float> xs = new List<float>(16);

        public static List<float> EllipsePts(float cx, float cy, float rx, float ry, float rot = 0, int seg = 0)
        {
            if (seg <= 0) seg = Math.Max(12, Math.Min(64, (int)((rx + ry) * 1.2f)));
            var p = new List<float>(seg * 2);
            float cr = (float)Math.Cos(rot), sr = (float)Math.Sin(rot);
            for (int i = 0; i < seg; i++)
            {
                double t = i * 2 * Math.PI / seg;
                float ex = (float)Math.Cos(t) * rx, ey = (float)Math.Sin(t) * ry;
                p.Add(cx + ex * cr - ey * sr); p.Add(cy + ex * sr + ey * cr);
            }
            return p;
        }

        public void Ellipse(float cx, float cy, float rx, float ry, uint c, float a = 1f, float rot = 0) =>
            Poly(EllipsePts(cx, cy, rx, ry, rot), c, a);

        public void Circle(float cx, float cy, float r, uint c, float a = 1f) => Poly(EllipsePts(cx, cy, r, r), c, a);

        public static List<float> RoundRectPts(float x, float y, float w, float h, float r)
        {
            r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2));
            var p = new List<float>(64);
            Corner(p, x + w - r, y + r, r, -90, 0);
            Corner(p, x + w - r, y + h - r, r, 0, 90);
            Corner(p, x + r, y + h - r, r, 90, 180);
            Corner(p, x + r, y + r, r, 180, 270);
            return p;
        }

        private static void Corner(List<float> p, float cx, float cy, float r, float a0, float a1)
        {
            int seg = r < 1 ? 1 : Math.Max(2, (int)(r / 1.5f));
            for (int i = 0; i <= seg; i++)
            {
                double t = (a0 + (a1 - a0) * i / seg) * Math.PI / 180;
                p.Add(cx + (float)Math.Cos(t) * r); p.Add(cy + (float)Math.Sin(t) * r);
            }
        }

        /// <summary>A rounded rectangle: the straight middle as a rectangle (fast), the curved bands as polygons.</summary>
        public void RoundRect(float x, float y, float w, float h, float r, uint c, float a = 1f)
        {
            r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2));
            if (r < 0.5f || w <= 0 || h <= 0) { Rect(x, y, w, h, c, a); return; }
            // whole pixel rows between the bands (after OY), so the parts meet without a seam
            float r0 = (float)Math.Ceiling(y + r + OY) - OY, r1 = (float)Math.Floor(y + h - r + OY) - OY;
            if (r1 < r0) { Poly(RoundRectPts(x, y, w, h, r), c, a); return; }
            Rect(x, r0, w, r1 - r0, c, a);
            var top = new List<float>(48);
            top.Add(x); top.Add(r0);
            Arc(top, x + r, y + r, r, 180, 270);
            Arc(top, x + w - r, y + r, r, 270, 360);
            top.Add(x + w); top.Add(r0);
            Poly(top, c, a);
            var bot = new List<float>(48);
            bot.Add(x + w); bot.Add(r1);
            Arc(bot, x + w - r, y + h - r, r, 0, 90);
            Arc(bot, x + r, y + h - r, r, 90, 180);
            bot.Add(x); bot.Add(r1);
            Poly(bot, c, a);
        }

        private static void Arc(List<float> p, float cx, float cy, float r, float a0, float a1)
        {
            int seg = Math.Max(2, Math.Min(16, (int)(r / 1.5f)));
            for (int i = 0; i <= seg; i++)
            {
                double t = (a0 + (a1 - a0) * i / seg) * Math.PI / 180;
                p.Add(cx + (float)Math.Cos(t) * r); p.Add(cy + (float)Math.Sin(t) * r);
            }
        }

        /// <summary>A rounded outline of thickness t: four straight strips and four corner pieces.</summary>
        public void RoundRectStroke(float x, float y, float w, float h, float r, float t, uint c, float a = 1f)
        {
            r = Math.Max(t, Math.Min(r, Math.Min(w, h) / 2));
            float r0 = (float)Math.Ceiling(y + r + OY) - OY, r1 = (float)Math.Floor(y + h - r + OY) - OY;
            float c0 = (float)Math.Ceiling(x + r + OX) - OX, c1 = (float)Math.Floor(x + w - r + OX) - OX;
            if (r1 < r0 || c1 < c0) return;
            Rect(x, r0, t, r1 - r0, c, a);
            Rect(x + w - t, r0, t, r1 - r0, c, a);
            Rect(c0, y, c1 - c0, t, c, a);
            Rect(c0, y + h - t, c1 - c0, t, c, a);
            float ri = Math.Max(0, r - t);
            CornerRing(x + r, y + r, r, ri, 180, 270, x, r0, c0, y, t, 1, 1, c, a);
            CornerRing(x + w - r, y + r, r, ri, 360, 270, x + w, r0, c1, y, t, -1, 1, c, a);
            CornerRing(x + w - r, y + h - r, r, ri, 0, 90, x + w, r1, c1, y + h, t, -1, -1, c, a);
            CornerRing(x + r, y + h - r, r, ri, 180, 90, x, r1, c0, y + h, t, 1, -1, c, a);
        }

        private void CornerRing(float cx, float cy, float r, float ri, float aV, float aH, float vx, float rowB, float colB, float hy, float t, int sx, int sy, uint c, float a)
        {
            var p = new List<float>(64);
            p.Add(vx); p.Add(rowB);
            Arc(p, cx, cy, r, aV, aH);
            p.Add(colB); p.Add(hy);
            p.Add(colB); p.Add(hy + sy * t);
            if (ri > 0.3f) Arc(p, cx, cy, ri, aH, aV);
            else { p.Add(vx + sx * t); p.Add(hy + sy * t); }
            p.Add(vx + sx * t); p.Add(rowB);
            Poly(p, c, a);
        }

        /// <summary>A line of the given width (a quad).</summary>
        public void Line(float ax, float ay, float bx, float by, float width, uint c, float a = 1f)
        {
            float dx = bx - ax, dy = by - ay, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f) return;
            float nx = -dy / len * width / 2, ny = dx / len * width / 2;
            Poly(new List<float> { ax + nx, ay + ny, bx + nx, by + ny, bx - nx, by - ny, ax - nx, ay - ny }, c, a);
        }

        /// <summary>Appends a quadratic curve (from the last point) to a path.</summary>
        public static void Quad(List<float> p, float cx, float cy, float x, float y, int seg = 8)
        {
            float sx = p[p.Count - 2], sy = p[p.Count - 1];
            for (int i = 1; i <= seg; i++)
            {
                float t = i / (float)seg, u = 1 - t;
                p.Add(u * u * sx + 2 * u * t * cx + t * t * x);
                p.Add(u * u * sy + 2 * u * t * cy + t * t * y);
            }
        }

        // ------------------------------------------------------------------ sprites

        /// <summary>Draws a sprite centred at (cx, cy), w x h, rotated by rot radians (bilinear).</summary>
        public void Sprite(Sprite s, float cx, float cy, float w, float h, float rot = 0, float a = 1f, uint tint = 0)
        {
            if (s == null || w <= 0 || h <= 0 || a <= 0) return;
            cx += OX; cy += OY;
            float cr = (float)Math.Cos(rot), sr = (float)Math.Sin(rot);
            float ex = Math.Abs(cr) * w / 2 + Math.Abs(sr) * h / 2, ey = Math.Abs(sr) * w / 2 + Math.Abs(cr) * h / 2;
            int x0 = Math.Max(ClipX0, (int)(cx - ex - 1)), x1 = Math.Min(ClipX1, (int)(cx + ex + 2));
            int y0 = Math.Max(ClipY0, (int)(cy - ey - 1)), y1 = Math.Min(ClipY1, (int)(cy + ey + 2));
            float kx = s.W / w, ky = s.H / h;
            for (int py = y0; py < y1; py++)
                for (int px = x0; px < x1; px++)
                {
                    float dx = px + 0.5f - cx, dy = py + 0.5f - cy;
                    float lx = dx * cr + dy * sr, ly = -dx * sr + dy * cr;      // undo the rotation
                    float u = (lx + w / 2) * kx - 0.5f, v = (ly + h / 2) * ky - 0.5f;
                    if (u < -1 || v < -1 || u > s.W || v > s.H) continue;
                    uint c; float sa;
                    Sample(s, u, v, out c, out sa);
                    if (sa <= 0.004f) continue;
                    if (tint != 0) c = tint;
                    Blend(py * W + px, c, sa * a);
                }
        }

        private static void Sample(Sprite s, float u, float v, out uint c, out float alpha)
        {
            int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
            float fx = u - x0, fy = v - y0;
            float r = 0, g = 0, b = 0, al = 0;
            for (int k = 0; k < 4; k++)
            {
                int x = x0 + (k & 1), y = y0 + (k >> 1);
                float wgt = ((k & 1) != 0 ? fx : 1 - fx) * ((k >> 1) != 0 ? fy : 1 - fy);
                if (x < 0 || y < 0 || x >= s.W || y >= s.H || wgt <= 0) continue;
                uint p = s.Px[y * s.W + x];
                float pa = (p >> 24) / 255f * wgt;
                r += (p & 0xFF) * pa; g += ((p >> 8) & 0xFF) * pa; b += ((p >> 16) & 0xFF) * pa; al += pa;
            }
            alpha = al;
            c = al > 0 ? Col.Rgb((int)(r / al), (int)(g / al), (int)(b / al)) : 0;
        }

        // ------------------------------------------------------------------ text

        public static readonly int[] Sizes = { 13, 16, 20, 26, 34, 48 };

        public float Measure(string s, int size) => Font.Measure(s, size);

        /// <summary>Text with its top at y. Returns the width drawn.</summary>
        public float Text(string s, float x, float y, int size, uint c, float a = 1f, Align align = Align.Left, bool shadow = false)
        {
            var f = Font.Get(size);
            float w = f.Measure(s);
            if (align == Align.Centre) x -= w / 2; else if (align == Align.Right) x -= w;
            if (shadow) DrawText(f, s, x + Math.Max(1, size / 14f), y + Math.Max(1, size / 12f), Col.Rgb(10, 6, 20), a * 0.55f);
            DrawText(f, s, x, y, c, a);
            return w;
        }

        /// <summary>Words wrapped to maxW; returns the height used.</summary>
        public float TextWrapped(string s, float x, float y, float maxW, int size, uint c, float a = 1f, Align align = Align.Left, float lineGap = 1.15f)
        {
            var f = Font.Get(size);
            float lh = f.LineHeight * lineGap, cy = y;
            foreach (var line in Wrap(s, maxW, size))
            {
                float lx = align == Align.Centre ? x + maxW / 2 : align == Align.Right ? x + maxW : x;
                Text(line, lx, cy, size, c, a, align);
                cy += lh;
            }
            return cy - y;
        }

        public List<string> Wrap(string s, float maxW, int size)
        {
            var lines = new List<string>();
            foreach (var para in s.Split('\n'))
            {
                string cur = "";
                foreach (var word in para.Split(' '))
                {
                    string t = cur.Length == 0 ? word : cur + " " + word;
                    if (cur.Length > 0 && Measure(t, size) > maxW) { lines.Add(cur); cur = word; }
                    else cur = t;
                }
                lines.Add(cur);
            }
            return lines;
        }

        private void DrawText(BakedFont.Face f, string s, float x, float y, uint c, float a)
        {
            float pen = x + OX;
            int baseY = (int)Math.Round(y + OY) + f.Ascent;
            foreach (char ch in s)
            {
                var g = f.Glyph(ch);
                if (g == null) { pen += f.Px * 0.5f; continue; }
                if (g.W > 0)
                {
                    int gx = (int)Math.Round(pen) + g.X, gy = baseY + g.Y;
                    for (int yy = 0; yy < g.H; yy++)
                    {
                        int py = gy + yy;
                        if (py < ClipY0 || py >= ClipY1) continue;
                        for (int xx = 0; xx < g.W; xx++)
                        {
                            int px = gx + xx;
                            if (px < ClipX0 || px >= ClipX1) continue;
                            byte al = g.A[yy * g.W + xx];
                            if (al != 0) Blend(py * W + px, c, a * al / 15f);
                        }
                    }
                }
                pen += g.Advance;
            }
        }
    }

    /// <summary>The bitmap UI font baked by tools/make_font.py (Bubble Sans, SIL OFL 1.1).</summary>
    public sealed class BakedFont
    {
        public sealed class Glyph { public int X, Y, W, H; public float Advance; public byte[] A; }

        public sealed class Face
        {
            public int Px, Ascent, LineHeight;
            public readonly Dictionary<char, Glyph> Glyphs = new Dictionary<char, Glyph>();
            public Glyph Glyph(char c)
            {
                Glyph g;
                if (Glyphs.TryGetValue(c, out g)) return g;
                if (c == '’' || c == '‘') return Glyphs.TryGetValue('\'', out g) ? g : null;
                return Glyphs.TryGetValue('?', out g) ? g : null;
            }
            public float Measure(string s)
            {
                float w = 0;
                foreach (char c in s) { var g = Glyph(c); w += g != null ? g.Advance : Px * 0.5f; }
                return w;
            }
        }

        private readonly List<Face> faces = new List<Face>();
        private static BakedFont embedded;

        public static BakedFont Embedded()
        {
            if (embedded != null) return embedded;
            using (var s = typeof(BakedFont).Assembly.GetManifestResourceStream("FlappyCrix.ui-font.bin"))
            {
                if (s == null) throw new InvalidOperationException("The UI font resource is missing from FlappyCrix.dll");
                var ms = new System.IO.MemoryStream();
                s.CopyTo(ms);
                return embedded = new BakedFont(ms.ToArray());
            }
        }

        public BakedFont(byte[] d)
        {
            int p = 0;
            Func<int> I32 = () => { int v = BitConverter.ToInt32(d, p); p += 4; return v; };
            Func<int> U16 = () => { int v = BitConverter.ToUInt16(d, p); p += 2; return v; };
            Func<int> S16 = () => { int v = BitConverter.ToInt16(d, p); p += 2; return v; };
            if (d.Length < 8 || d[0] != 'F' || d[1] != 'C' || d[2] != 'F' || d[3] != '1') throw new InvalidOperationException("Bad UI font data");
            p = 4;
            int sizes = I32();
            for (int si = 0; si < sizes; si++)
            {
                var f = new Face { Px = I32(), Ascent = I32(), LineHeight = I32() };
                int n = I32();
                for (int gi = 0; gi < n; gi++)
                {
                    char ch = (char)U16();
                    var g = new Glyph { X = S16(), Y = S16(), W = U16(), H = U16() };
                    g.Advance = I32() / 64f;
                    int count = g.W * g.H;
                    g.A = new byte[count];
                    for (int k = 0; k < count; k += 2)
                    {
                        byte b = d[p++];
                        g.A[k] = (byte)(b >> 4);
                        if (k + 1 < count) g.A[k + 1] = (byte)(b & 15);
                    }
                    f.Glyphs[ch] = g;
                }
                faces.Add(f);
            }
        }

        /// <summary>The baked size closest to px.</summary>
        public Face Get(int px)
        {
            Face best = faces[0];
            foreach (var f in faces) if (Math.Abs(f.Px - px) < Math.Abs(best.Px - px)) best = f;
            return best;
        }

        public float Measure(string s, int px) => Get(px).Measure(s);
    }
}
