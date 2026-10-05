// Draws the game world the way the website's canvas does (flappycrix.html drawBackdrop,
// drawPipe, the coins, bird, hats, trails, Halloween pumpkins and cobwebs, and the witch /
// Santa fly-bys) into the play area of the native screen. The menus around it are NativeUi.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;

namespace FlappyCrix.Native
{
    /// <summary>Pictures the game draws, by name ("bird", "coin-still", "emoji/1f383"...; see tools/make_sprites.py).</summary>
    public sealed class Assets
    {
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        public void Add(string name, Sprite s) { if (s != null) sprites[name] = s; }
        public Sprite Get(string name) { Sprite s; return name != null && sprites.TryGetValue(name, out s) ? s : null; }
        public int Count => sprites.Count;
    }

    /// <summary>The colours the backdrop uses: a season's, or the site's dark/light default.</summary>
    public sealed class Scene
    {
        public uint SkyTop, SkyLow, City, Window, Bush, Ground, GroundTop, Stripe, Cloud, Moon, MotifFill;
        public float CityA, WindowA, StripeA, CloudA, MoonA, MotifA;
        public uint[] Pipe;
        public string Motif;

        public static readonly uint[] DefaultPipe = { Col.Hex("#0B8F56"), Col.Hex("#00CC7A"), Col.Hex("#4DE8A8"), Col.Hex("#00CC7A"), Col.Hex("#076B41") };

        public static Scene For(Season s, bool light)
        {
            if (s != null)
                return new Scene
                {
                    SkyTop = s.SkyTop, SkyLow = s.SkyLow, City = s.City, CityA = s.CityA, Window = Col.Rgb(255, 214, 120), WindowA = .22f,
                    Bush = s.Bush, Ground = s.Ground, GroundTop = s.GroundTop, Stripe = s.Stripe, StripeA = s.StripeA,
                    Pipe = s.Pipe, Motif = s.Motif, MotifFill = s.MotifFill, MotifA = s.MotifA, Moon = s.Moon, MoonA = s.MoonA,
                };
            return light
                ? new Scene
                {
                    SkyTop = Col.Hex("#4EC0CA"), SkyLow = Col.Hex("#8FD9E0"), City = Col.Rgb(120, 200, 205), CityA = .75f,
                    Window = Col.Rgb(255, 255, 255), WindowA = .22f, Bush = Col.Hex("#5BAF3E"), Ground = Col.Hex("#DED895"),
                    GroundTop = Col.Hex("#C9C177"), Stripe = Col.Rgb(180, 170, 90), StripeA = .5f, Cloud = Col.Rgb(255, 255, 255), CloudA = .85f,
                    Pipe = DefaultPipe, Motif = "cloud",
                }
                : new Scene
                {
                    SkyTop = Col.Hex("#12203A"), SkyLow = Col.Hex("#1B3352"), City = Col.Rgb(35, 60, 95), CityA = .7f,
                    Window = Col.Rgb(120, 160, 220), WindowA = .16f, Bush = Col.Hex("#1E4A2E"), Ground = Col.Hex("#2A2A22"),
                    GroundTop = Col.Hex("#22221C"), Stripe = Col.Rgb(255, 255, 255), StripeA = .05f, Cloud = Col.Rgb(180, 200, 235), CloudA = .10f,
                    Pipe = DefaultPipe, Motif = "cloud",
                };
        }
    }

    /// <summary>The witch (Halloween) or Santa's sleigh (Christmas) crossing the sky every so often, on the wall clock.</summary>
    public sealed class FlyBy
    {
        public string Kind;                        // "witch" / "santa"
        public bool Active;
        public float X, Y, BaseY, W, Hgt, Dir, Speed, T, NextAt = -1;
        private readonly Random rng = new Random();

        float Every0 => Kind == "witch" ? 14000 : 16000;
        float Every1 => Kind == "witch" ? 26000 : 30000;
        float Arc => Kind == "witch" ? 34 : 22;

        /// <summary>now / dt in milliseconds. Returns true when one starts (play its sound).</summary>
        public bool Update(string kind, float now, float dt, Sprite img)
        {
            if (kind != Kind) { Kind = kind; Active = false; NextAt = -1; }
            if (kind == null || img == null) { Active = false; return false; }
            dt = Math.Min(dt, 100);
            if (NextAt < 0) NextAt = now + 3000 + (float)rng.NextDouble() * Every0;
            if (!Active)
            {
                if (now < NextAt) return false;
                W = kind == "witch" ? 96 : 132;
                Hgt = W * img.H / (float)img.W;
                Dir = kind == "santa" ? 1 : -1;                         // the witch comes from the right, Santa from the left
                X = Dir > 0 ? -W : NativeSim.W + W;
                BaseY = NativeSim.H * (0.10f + (float)rng.NextDouble() * 0.16f);
                Speed = (NativeSim.W + 2 * W) / 4600f;
                T = 0; Active = true;
                return true;
            }
            T += dt;
            X += Dir * Speed * dt;
            Y = BaseY + (float)Math.Sin(T * 0.0016) * Arc;
            if ((Dir > 0 && X > NativeSim.W + W) || (Dir < 0 && X < -W))
            {
                Active = false;
                NextAt = now + Every0 + (float)rng.NextDouble() * (Every1 - Every0);
            }
            return false;
        }

        public float Rotation => (float)Math.Cos(T * 0.0016) * 0.07f * Dir;
    }

    public sealed class NativeRenderer
    {
        public const int W = NativeSim.W, H = NativeSim.H;
        const int SkyH = NativeSim.SkyH;

        static float Wrap(float v, float span) => ((v % span) + span) % span;

        // ------------------------------------------------------------------ backdrop

        /// <summary>Sky, moon, motif, skyline, bushes and ground (drawBackdrop). bgOff = scenery offset.</summary>
        public static void Backdrop(Canvas c, Scene s, float bgOff)
        {
            c.RectVGrad(0, 0, W, SkyH, s.SkyTop, s.SkyLow);

            if (s.Motif == "bat" || s.Motif == "snow")
                c.Circle(W * 0.78f, SkyH * 0.22f, Math.Min(38, W * 0.09f), s.Moon, s.MoonA);

            switch (s.Motif)
            {
                case "cloud":
                    for (int i = 0; i < 4; i++)
                    {
                        float x = Wrap(i * 150 - bgOff * 0.12f, W + 180) - 90, y = 26 + (i % 3) * 34;
                        Blob(c, x, y, 30, 12, s.Cloud, s.CloudA); Blob(c, x + 22, y - 7, 22, 10, s.Cloud, s.CloudA); Blob(c, x + 42, y, 26, 11, s.Cloud, s.CloudA);
                    }
                    break;
                case "bat":
                    for (int i = 0; i < 7; i++)
                    {
                        float x = Wrap(i * 96 - bgOff * 0.5f, W + 120) - 60;
                        float y = 30 + (i % 4) * 30 + (float)Math.Sin(bgOff * 0.04f + i) * 8;
                        float w = 11, flap = (float)Math.Sin(bgOff * 0.22f + i * 1.3f) * 5;
                        var p = new List<float> { x, y };
                        Canvas.Quad(p, x - w, y - 7 - flap, x - 2 * w, y + 1);
                        Canvas.Quad(p, x - w, y + 3, x, y + 6);
                        Canvas.Quad(p, x + w, y + 3, x + 2 * w, y + 1);
                        Canvas.Quad(p, x + w, y - 7 - flap, x, y);
                        c.Poly(p, s.MotifFill, s.MotifA);
                    }
                    break;
                case "snow":
                    for (int i = 0; i < 34; i++)
                    {
                        float sx = Wrap(i * 71 + (float)Math.Sin(i) * 40 - bgOff * 0.25f, W + 40) - 20;
                        float sy = Wrap(i * 53 + bgOff * (0.5f + (i % 3) * 0.25f), SkyH + 20) - 10;
                        c.Circle(sx, sy, 1.2f + (i % 3) * 0.8f, s.MotifFill, s.MotifA);
                    }
                    break;
                case "confetti":
                    for (int i = 0; i < 26; i++)
                    {
                        float x = Wrap(i * 83 + (float)Math.Sin(i) * 30 - bgOff * 0.2f, W + 30) - 15;
                        float y = Wrap(i * 47 + bgOff * (0.35f + (i % 4) * 0.12f), SkyH + 20) - 10;
                        float rot = bgOff * 0.03f * (i % 2 == 1 ? 1 : -1) + i;
                        c.Poly(RotRect(x, y, 6, 3, rot), Season.ConfettiColours[i % 5]);
                    }
                    break;
                case "egg":
                    for (int i = 0; i < 6; i++)
                    {
                        float x = Wrap(i * 128 - bgOff * 0.16f, W + 150) - 75;
                        float y = 34 + (i % 3) * 36 + (float)Math.Sin(bgOff * 0.03f + i) * 5;
                        c.Ellipse(x, y, 9, 12, Season.EggColours[i % 5], 1, (float)Math.Sin(i) * 0.3f);
                        c.Rect(x - 7, y - 1, 14, 2, Col.White, .55f);
                        c.Rect(x - 6, y + 4, 12, 2, Col.White, .55f);
                    }
                    break;
            }

            // skyline
            int cityY = SkyH - (int)Math.Round(H * 0.13);
            for (int i = 0; i < 14; i++)
            {
                float x = Wrap(i * 62 - bgOff * 0.2f, W + 140) - 70;
                int h = 26 + ((i * 37) % 46);
                c.Rect(x, cityY + 46 - h, 44, h, s.City, s.CityA);
                for (int wy = cityY + (46 - h) + 6; wy < cityY + 42; wy += 11)
                {
                    c.Rect(x + 8, wy, 6, 5, s.Window, s.WindowA);
                    c.Rect(x + 24, wy, 6, 5, s.Window, s.WindowA);
                }
            }

            // bushes
            int bushY = SkyH - (int)Math.Round(H * 0.045);
            for (int i = 0; i < 12; i++)
            {
                float x = Wrap(i * 74 - bgOff * 0.45f, W + 160) - 80;
                Blob(c, x, bushY, 34, 15, s.Bush, 1); Blob(c, x + 26, bushY - 5, 28, 14, s.Bush, 1);
            }
            c.Rect(0, bushY, W, SkyH - bushY, s.Bush);

            // ground and its stripes
            c.Rect(0, SkyH, W, NativeSim.GroundH, s.Ground);
            c.Rect(0, SkyH, W, 4, s.GroundTop);
            int cx0 = c.ClipX0, cy0 = c.ClipY0, cx1 = c.ClipX1, cy1 = c.ClipY1;
            c.ClipY0 = Math.Max(cy0, (int)(SkyH + 4 + c.OY));
            float off = Wrap(bgOff, 28);
            for (float x = -66; x < W + 66; x += 28)
            {
                float ox = x - off;
                c.Poly(new List<float> { ox, H, ox + 66, SkyH + 4, ox + 80, SkyH + 4, ox + 14, H }, s.Stripe, s.StripeA);
            }
            c.ClipX0 = cx0; c.ClipY0 = cy0; c.ClipX1 = cx1; c.ClipY1 = cy1;
        }

        static void Blob(Canvas c, float x, float y, float w, float h, uint col, float a) => c.Ellipse(x, y, w / 2, h, col, a);

        static List<float> RotRect(float cx, float cy, float w, float h, float rot)
        {
            float cr = (float)Math.Cos(rot), sr = (float)Math.Sin(rot), hw = w / 2, hh = h / 2;
            var p = new List<float>(8);
            foreach (var q in new[] { -1, -1, 1, -1, 1, 1, -1, 1 }.Pairs())
            {
                float lx = q.Key * hw, ly = q.Value * hh;
                p.Add(cx + lx * cr - ly * sr); p.Add(cy + lx * sr + ly * cr);
            }
            return p;
        }

        // ------------------------------------------------------------------ lane

        static readonly float[] PipeStops = { 0f, 0.18f, 0.42f, 0.72f, 1f };

        public static void Pipe(Canvas c, NativeSim.Pipe p, float x, uint[] cols)
        {
            const float w = NativeSim.PipeWidth, rimH = 16, over = 4.5f;
            c.RectHGrad(x, 0, w, p.Top, x, w, PipeStops, cols);
            c.RectHGrad(x, p.Bottom, w, H - p.Bottom, x, w, PipeStops, cols);
            foreach (float rimY in new[] { p.Top - rimH, p.Bottom })
            {
                c.RectHGrad(x - over, rimY, w + 2 * over, rimH, x, w + 2 * over, PipeStops, cols);   // the site's gradient starts at x, not x - 4.5
                c.Rect(x - over, rimY, w + 2 * over, 2, Col.White, .16f);
                c.Rect(x - over, rimY + rimH - 2, w + 2 * over, 2, Col.Black, .22f);
            }
        }

        /// <summary>Halloween cobwebs: half webs on the pipe walls near the opening (decoration only).</summary>
        public static void Webs(Canvas c, NativeSim.Pipe p, float x)
        {
            const float R = 27;
            uint col = Col.Rgb(232, 236, 250);
            for (int bit = 1; bit <= 8; bit <<= 1)
            {
                if ((p.Web & bit) == 0) continue;
                bool top = bit <= 2, left = bit == 1 || bit == 4;
                float hx = left ? x : x + NativeSim.PipeWidth;
                float hy = top ? p.Top - 16 - R : p.Bottom + 16 + R;
                if (top && hy <= R / 2) continue;
                float dir = left ? -1 : 1;
                var spokes = new List<float>();
                for (int k = 0; k < 8; k++)
                {
                    float a = (float)(-Math.PI / 2 + Math.PI * k / 7);
                    float ex = hx + dir * (float)Math.Cos(a) * R, ey = hy + (float)Math.Sin(a) * R;
                    c.Line(hx, hy, ex, ey, 1.1f, col, .4f);
                    spokes.Add(a);
                }
                for (int ring = 1; ring <= 5; ring++)
                {
                    float rr = R * ring / 5f;
                    for (int k = 0; k + 1 < spokes.Count; k++)
                    {
                        float a0 = spokes[k], a1 = spokes[k + 1], am = (a0 + a1) / 2;
                        float x0 = hx + dir * (float)Math.Cos(a0) * rr, y0 = hy + (float)Math.Sin(a0) * rr;
                        float x1 = hx + dir * (float)Math.Cos(a1) * rr, y1 = hy + (float)Math.Sin(a1) * rr;
                        float cxp = hx + dir * (float)Math.Cos(am) * rr * 0.84f, cyp = hy + (float)Math.Sin(am) * rr * 0.84f + rr * 0.06f;
                        float px = x0, py = y0;
                        for (int s = 1; s <= 4; s++)
                        {
                            float t = s / 4f, u = 1 - t;
                            float qx = u * u * x0 + 2 * u * t * cxp + t * t * x1, qy = u * u * y0 + 2 * u * t * cyp + t * t * y1;
                            c.Line(px, py, qx, qy, 1.1f, col, .4f);
                            px = qx; py = qy;
                        }
                    }
                }
            }
        }

        static readonly uint PumpkinHi = Col.Hex("#FFAE3D"), PumpkinMid = Col.Hex("#F0761A"), PumpkinLo = Col.Hex("#B8490A"),
                             PumpkinFace = Col.Hex("#FFE08A"), Stalk = Col.Hex("#3F7A2E"), ChunkCol = Col.Hex("#E8761A");

        /// <summary>A whole pumpkin (also used for the Pumpkin Head hat).</summary>
        public static void WholePumpkin(Canvas c, float x, float y, float r, float tilt, float alpha = 1f)
        {
            float ry = r * 0.86f;
            c.Ellipse(x, y, r + 1.2f, ry + 1.2f, Col.Rgb(12, 6, 20), .7f * alpha, tilt);
            float top = y - ry, span = 2 * ry, oy = c.OY;
            c.Poly(Canvas.EllipsePts(x, y, r, ry, tilt), 0, alpha, (px, py) =>
            {
                float f = (py + 0.5f - oy - top) / span;
                return f < 0.5f ? Col.Lerp(PumpkinHi, PumpkinMid, f * 2) : Col.Lerp(PumpkinMid, PumpkinLo, (f - 0.5f) * 2);
            });
            foreach (float k in new[] { -0.45f, 0f, 0.45f })
                c.Ellipse(x + k * r * (float)Math.Cos(tilt), y + k * r * (float)Math.Sin(tilt), r * 0.18f, ry * 0.92f, Col.Rgb(150, 62, 0), .42f * alpha, tilt);
            c.Poly(RotRect(x + (float)Math.Sin(tilt) * ry, y - (float)Math.Cos(tilt) * ry - r * 0.12f, r * 0.22f, r * 0.42f, tilt + 0.2f), Stalk, alpha);
            // glowing face
            Face(c, x, y, r, tilt, alpha);
        }

        static void Face(Canvas c, float x, float y, float r, float tilt, float alpha)
        {
            float cr = (float)Math.Cos(tilt), sr = (float)Math.Sin(tilt);
            Func<float, float, float[]> P = (lx, ly) => new[] { x + (lx * cr - ly * sr) * r, y + (lx * sr + ly * cr) * r };
            Action<float[][]> Tri = pts => { var l = new List<float>(); foreach (var p in pts) { l.Add(p[0]); l.Add(p[1]); } c.Poly(l, PumpkinFace, alpha); };
            Tri(new[] { P(-0.48f, -0.08f), P(-0.16f, -0.08f), P(-0.32f, -0.36f) });
            Tri(new[] { P(0.16f, -0.08f), P(0.48f, -0.08f), P(0.32f, -0.36f) });
            Tri(new[] { P(-0.5f, 0.14f), P(0.5f, 0.14f), P(0.3f, 0.42f), P(0.1f, 0.3f), P(-0.1f, 0.42f), P(-0.3f, 0.3f) });
        }

        public static void Pumpkin(Canvas c, NativeSim.Pumpkin p, float x)
        {
            if (p.Smashed <= 0) { WholePumpkin(c, x, p.Y, p.R, p.Tilt); return; }
            float a = Math.Min(1f, p.Smashed / 26f);
            float dxs = x - p.X;
            foreach (var ch in p.Chunks)
            {
                c.Poly(RotRect(ch.X + dxs, ch.Y, ch.R * 1.6f, ch.R * 1.2f, ch.Rot), ChunkCol, a);
                c.Poly(RotRect(ch.X + dxs, ch.Y, ch.R * 0.8f, ch.R * 0.5f, ch.Rot), Col.Rgb(255, 225, 150), .5f * a);
            }
        }

        public static void PrizeText(Canvas c, NativeSim.Pumpkin p, float x, Sprite coin)
        {
            if (p.Smashed <= 0 || p.Text == null) return;
            float y = p.Y - 30 - (46 - p.Smashed) * 0.6f;
            float a = Math.Min(1f, p.Smashed / 26f);
            uint col = p.CoinText ? Col.Hex("#FFD700") : Col.Hex("#7CF0B8");
            float w = c.Measure(p.Text, 16) + (p.CoinText ? 20 : 0);
            float left = x - w / 2;
            c.Text(p.Text, left, y - 9, 16, col, a, Align.Left, true);
            if (p.CoinText && coin != null) c.Sprite(coin, left + w - 8, y, 16, 16, 0, a);
        }

        public static void Coin(Canvas c, NativeSim.Coin co, float x, int frame, Sprite coin)
        {
            float t = frame * 0.08f + co.X * 0.05f, spin = Math.Abs((float)Math.Cos(t)), bob = (float)Math.Sin(t * 0.7f) * 2.5f;
            float w = 20 * Math.Max(0.18f, spin), cx = x + 10, cy = co.Y + 10 + bob;
            if (spin < 0.35f || coin == null) c.Rect(cx - w / 2, cy - 10, w, 20, Col.Hex("#B8860B"));
            else c.Sprite(coin, cx, cy, w, 20);
            c.Circle(cx, cy, 13, Col.Hex("#FFD700"), 0.18f * spin);
        }

        // ------------------------------------------------------------------ bird, hats, trails

        public static void Bird(Canvas c, float y, float rot, float size, Sprite body, Catalog.Cosmetic hat, float hatAngle, Assets assets, bool shieldGlow, int frame)
        {
            float cr = (float)Math.Cos(rot), sr = (float)Math.Sin(rot);
            float bx = NativeSim.BirdX;
            // shadow (inside the rotation)
            c.Ellipse(bx + 2 * cr - size * 0.5f * sr, y + 2 * sr + size * 0.5f * cr, size * 0.33f, size * 0.11f, Col.Black, 0.26f, rot);
            if (shieldGlow)
            {
                float pulse = 0.5f + 0.5f * (float)Math.Sin(frame * 0.15f);
                c.Circle(bx, y, size * 0.75f, Col.Hex("#7CF0B8"), 0.18f + 0.12f * pulse);
            }
            if (body != null) c.Sprite(body, bx, y, size, size, rot);
            else { c.Ellipse(bx, y, size / 2, size / 2, Col.Hex("#FF6B35"), 1, rot); c.Circle(bx + size * 0.2f, y - size * 0.15f, size * 0.12f, Col.White); }
            if (hat != null) Hat(c, bx, y, rot, size, hat, hatAngle, assets);
        }

        /// <summary>A hat on the bird: (bx, by) bird centre, rot the bird's tilt, wobble the hat's swing.</summary>
        public static void Hat(Canvas c, float bx, float by, float rot, float s, Catalog.Cosmetic hat, float wobble, Assets assets)
        {
            // base point on the head (cosmetic-art.js brimY(0.01) = -0.318) in the bird's rotated frame
            float cr = (float)Math.Cos(rot), sr = (float)Math.Sin(rot);
            float lx = 0.01f * s, ly = -0.318f * s;
            float baseX = bx + lx * cr - ly * sr, baseY = by + lx * sr + ly * cr;
            float a = rot + wobble * 1.1f;
            float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            // local hat coordinates: x right, y down, origin = base point
            Func<float, float, float[]> P = (hx, hy) => new[] { baseX + (hx * s) * ca - (hy * s) * sa, baseY + (hx * s) * sa + (hy * s) * ca };
            Action<uint, float, float[][]> Fill = (col, alpha, pts) => { var l = new List<float>(); foreach (var p in pts) { l.Add(p[0]); l.Add(p[1]); } c.Poly(l, col, alpha); };
            Action<float, float, float, float, uint> Ell = (hx, hy, rx, ry, col) => { var p = P(hx, hy); c.Ellipse(p[0], p[1], rx * s, ry * s, col, 1, a); };

            if (hat.Image != null)
            {
                var img = assets.Get(hat.Image);
                if (img == null) return;
                float w = s * hat.FitW, h = w / img.Aspect;
                var centre = P(0, hat.FitY - h / s / 2);
                c.Sprite(img, centre[0], centre[1], w, h, a);
                return;
            }
            switch (hat.Shape)
            {
                case "witch":
                    Fill(Col.Hex("#2A1B3D"), 1, new[] { P(-0.3f, 0.02f), P(0.3f, 0.02f), P(0.1f, -0.45f), P(0.28f, -0.78f), P(-0.02f, -0.5f) });
                    Fill(Col.Hex("#FF7A18"), 1, new[] { P(-0.25f, -0.06f), P(0.25f, -0.06f), P(0.22f, -0.15f), P(-0.21f, -0.15f) });
                    Ell(0, 0.02f, 0.55f, 0.09f, Col.Hex("#1B1224"));
                    break;
                case "santa":
                    Fill(Col.Hex("#C8102E"), 1, new[] { P(-0.3f, 0.02f), P(0.3f, 0.02f), P(0.15f, -0.4f), P(0.42f, -0.5f), P(-0.05f, -0.5f) });
                    Fill(Col.White, 1, new[] { P(-0.36f, 0.06f), P(0.36f, 0.06f), P(0.34f, -0.08f), P(-0.34f, -0.08f) });
                    Ell(0.45f, -0.5f, 0.09f, 0.09f, Col.White);
                    break;
                case "party":
                    Fill(Col.Hex("#FFC83D"), 1, new[] { P(-0.26f, 0.02f), P(0.26f, 0.02f), P(0f, -0.62f) });
                    Fill(Col.Hex("#FF5FA2"), 1, new[] { P(-0.16f, -0.22f), P(0.15f, -0.2f), P(0.11f, -0.31f), P(-0.12f, -0.33f) });
                    Fill(Col.Hex("#5BD2FF"), 1, new[] { P(-0.07f, -0.44f), P(0.07f, -0.43f), P(0.04f, -0.52f), P(-0.04f, -0.52f) });
                    Ell(0, -0.64f, 0.08f, 0.08f, Col.Hex("#FF5FA2"));
                    break;
                case "bunny":
                    Ell(-0.14f, -0.38f, 0.1f, 0.34f, Col.Hex("#F4F4F8")); Ell(0.16f, -0.36f, 0.1f, 0.34f, Col.Hex("#F4F4F8"));
                    Ell(-0.14f, -0.36f, 0.05f, 0.24f, Col.Hex("#FF9EC4")); Ell(0.16f, -0.34f, 0.05f, 0.24f, Col.Hex("#FF9EC4"));
                    break;
                case "reindeer":
                    {
                        uint antler = Col.Hex("#8B5A2B");
                        foreach (float side in new[] { -1f, 1f })
                        {
                            var b0 = P(side * 0.14f, 0f); var b1 = P(side * 0.26f, -0.42f); var t1 = P(side * 0.4f, -0.3f); var t2 = P(side * 0.16f, -0.32f);
                            c.Line(b0[0], b0[1], b1[0], b1[1], 0.08f * s, antler);
                            c.Line(P(side * 0.22f, -0.28f)[0], P(side * 0.22f, -0.28f)[1], t1[0], t1[1], 0.06f * s, antler);
                            c.Line(P(side * 0.2f, -0.18f)[0], P(side * 0.2f, -0.18f)[1], t2[0], t2[1], 0.06f * s, antler);
                            Ell(side * 0.3f, -0.02f, 0.1f, 0.05f, Col.Hex("#A0522D"));
                        }
                        break;
                    }
                case "pumpkin":
                    {
                        var p = P(0, 0.26f);
                        WholePumpkin(c, p[0], p[1], s * 0.56f, a);
                        break;
                    }
                case "skull":
                    {
                        var p = P(0.04f, 0.3f);
                        c.Ellipse(p[0], p[1], s * 0.36f, s * 0.34f, Col.Hex("#EDEDF2"), 0.96f, a);
                        var e1 = P(-0.1f, 0.26f); var e2 = P(0.18f, 0.26f);
                        c.Ellipse(e1[0], e1[1], s * 0.08f, s * 0.09f, Col.Hex("#17121F"), 1, a);
                        c.Ellipse(e2[0], e2[1], s * 0.08f, s * 0.09f, Col.Hex("#17121F"), 1, a);
                        var n = P(0.04f, 0.4f); c.Ellipse(n[0], n[1], s * 0.03f, s * 0.04f, Col.Hex("#17121F"), 1, a);
                        for (int k = -2; k <= 2; k++) { var tt = P(0.04f + k * 0.06f, 0.52f); var tb = P(0.04f + k * 0.06f, 0.58f); c.Line(tt[0], tt[1], tb[0], tb[1], 1, Col.Hex("#17121F")); }
                        break;
                    }
            }
        }

        /// <summary>The trail behind the bird: a fading ribbon in the trail's colours, plus a few sparks.</summary>
        public static void Trail(Canvas c, List<float> pts, float shift, Catalog.Cosmetic trail, int frame)
        {
            int n = pts.Count / 2;
            if (trail == null || n < 2) return;
            var cols = trail.Colours;
            for (int i = 1; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float ax = pts[2 * (i - 1)] + shift, ay = pts[2 * (i - 1) + 1], bx = pts[2 * i] + shift, by = pts[2 * i + 1];
                float width = 2 + 9 * t, alpha = 0.15f + 0.65f * t;
                if (trail.Fx == "bands")
                {
                    int k = cols.Length;
                    float dx = bx - ax, dy = by - ay, len = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (len < 1e-3f) continue;
                    float nx = -dy / len, ny = dx / len, bw = width * 1.3f / k;
                    for (int b = 0; b < k; b++)
                    {
                        float o = (b - (k - 1) / 2f) * bw;
                        c.Line(ax + nx * o, ay + ny * o, bx + nx * o, by + ny * o, bw + 0.4f, cols[b], alpha);
                    }
                }
                else
                {
                    uint col = Canvas.Grad(Stops(cols.Length), cols, 1 - t);
                    if (trail.Fx == "ghost") alpha *= 0.6f;
                    c.Line(ax, ay, bx, by, width, col, alpha);
                }
            }
            // sparks
            if (trail.Fx == "bands") return;
            for (int i = 0; i < n; i += 4)
            {
                int seed = (i * 7919 + (frame / 3) * 104729) & 0x7fffffff;
                float jx = (seed % 13) - 6, jy = ((seed / 13) % 13) - 6;
                float t = i / (float)(n - 1);
                uint col = cols[(seed / 169) % cols.Length];
                float r = trail.Fx == "ghost" ? 2.5f : 1.5f;
                c.Circle(pts[2 * i] + shift + jx, pts[2 * i + 1] + jy, r, col, 0.6f * t);
            }
        }

        static float[] Stops(int n)
        {
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = n == 1 ? 0 : i / (float)(n - 1);
            return s;
        }

        public static void Flyer(Canvas c, FlyBy f, Sprite img)
        {
            if (f == null || !f.Active || img == null) return;
            c.Sprite(img, f.X, f.Y, f.W, f.Hgt, f.Rotation, 0.95f);
        }
    }

    static class PairExt
    {
        public static IEnumerable<KeyValuePair<int, int>> Pairs(this int[] a)
        {
            for (int i = 0; i + 1 < a.Length; i += 2) yield return new KeyValuePair<int, int>(a[i], a[i + 1]);
        }
    }
}
