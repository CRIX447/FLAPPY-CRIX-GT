// Draws the native fallback game into one RGBA pixel buffer (400x600, rows top-down),
// which is uploaded as a single texture - the same way the website is shown. One
// opaque picture means no draw-order problems between pieces of the screen.
// Colours follow the site's night/Halloween palette. No UnityEngine references.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;

namespace FlappyCrix.Native
{
    public sealed class NativeRenderer
    {
        public const int W = NativeSim.W, H = NativeSim.H;
        public readonly uint[] Pixels = new uint[W * H];

        private readonly uint[] background = new uint[W * H];
        private readonly uint[] pipeRow, rimRow;
        private readonly int rimW;
        private uint[] bird; private int birdW, birdH;
        private uint[] coin; private int coinW, coinH;

        static readonly uint White = PixelFont.Rgba("#FFFFFF"), Orange = PixelFont.Rgba("#FF6B35"),
                             Gold = PixelFont.Rgba("#FFD700"), Shadow = PixelFont.Rgba("#14101E"),
                             Green = PixelFont.Rgba("#4DE8A8");

        public NativeRenderer()
        {
            // Sky (the site's dark theme: #12203A -> #1B3352), stars, a distant skyline
            var rnd = new Random(7);
            for (int y = 0; y < H; y++)
            {
                float t = y / (float)(H - NativeSim.GroundH);
                uint c = Lerp(PixelFont.Rgba("#12203A"), PixelFont.Rgba("#1B3352"), Math.Min(1, t));
                for (int x = 0; x < W; x++) background[y * W + x] = c;
            }
            for (int i = 0; i < 70; i++)
            {
                int x = rnd.Next(W), y = rnd.Next((int)(H * 0.6f));
                background[y * W + x] = PixelFont.Rgba("#C8D2F0");
            }
            // Moon
            FillCircle(background, 310, 110, 34, PixelFont.Rgba("#F2B65A"));
            // Skyline silhouettes
            uint city = PixelFont.Rgba("#0E1730"), window = PixelFont.Rgba("#33406A");
            int bx = 0;
            while (bx < W)
            {
                int bw = 26 + rnd.Next(34), bh = 50 + rnd.Next(90);
                int top = H - (int)NativeSim.GroundH - bh;
                FillRect(background, bx, top, bw, bh, city);
                for (int wy = top + 8; wy < top + bh - 8; wy += 12)
                    for (int wx = bx + 5; wx < bx + bw - 6; wx += 9)
                        if (rnd.Next(3) == 0) FillRect(background, wx, wy, 4, 5, window);
                bx += bw + 4;
            }
            // Ground band
            FillRect(background, 0, H - (int)NativeSim.GroundH, W, (int)NativeSim.GroundH, PixelFont.Rgba("#2A2A22"));
            FillRect(background, 0, H - (int)NativeSim.GroundH, W, 3, PixelFont.Rgba("#3C3C30"));

            // Pipes: the site's 5-stop green gradient (#0B8F56 #00CC7A #4DE8A8 #00CC7A #076B41)
            int pw = (int)NativeSim.PipeWidth;
            rimW = pw + 8;
            pipeRow = GradientRow(pw);
            rimRow = GradientRow(rimW);
        }

        /// <summary>Sprites decoded by the caller (RGBA, rows top-down). Optional.</summary>
        public void SetSprites(uint[] birdPx, int bw, int bh, uint[] coinPx, int cw, int ch)
        {
            bird = birdPx; birdW = bw; birdH = bh;
            coin = coinPx; coinW = cw; coinH = ch;
        }

        public void Render(NativeSim sim, float alpha)
        {
            bool live = sim.Screen == "playing";
            float shift = live ? NativeSim.Speed * alpha : 0f;
            Array.Copy(background, Pixels, Pixels.Length);

            // Ground stripes scroll with the world
            int groundY = H - (int)NativeSim.GroundH + 6;
            int scroll = (int)((sim.Frame + (live ? alpha : 0)) * NativeSim.Speed) % 24;
            uint stripe = PixelFont.Rgba("#33332A");
            for (int y = groundY; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (((x + scroll + (y - groundY)) % 24) < 10) Pixels[y * W + x] = stripe;

            // Pipes
            int ground = H - (int)NativeSim.GroundH;
            foreach (var p in sim.Pipes)
            {
                int x = (int)Math.Round(p.X - shift);
                int top = (int)p.Top, bottom = (int)p.Bottom;
                BlitRows(pipeRow, x, 0, top);
                BlitRows(pipeRow, x, bottom, ground - bottom);
                int rimH = 16, over = (rimW - pipeRow.Length) / 2;
                BlitRows(rimRow, x - over, top - rimH, rimH);
                BlitRows(rimRow, x - over, bottom, rimH);
                ShadeRow(x - over, top - rimH, rimW, 1.25f); ShadeRow(x - over, top - 1, rimW, 0.7f);
                ShadeRow(x - over, bottom, rimW, 1.25f); ShadeRow(x - over, bottom + rimH - 1, rimW, 0.7f);
            }

            // Coins (spinning = horizontal squash)
            foreach (var c in sim.CoinList)
            {
                if (c.Taken) continue;
                float spin = Math.Abs((float)Math.Cos(sim.Frame * 0.08f + c.X * 0.05f));
                int w = Math.Max(4, (int)(20 * Math.Max(0.18f, spin)));
                int cx = (int)(c.X - shift + 10), cy = (int)(c.Y + 10);
                if (coin != null) BlitScaled(coin, coinW, coinH, cx - w / 2, cy - 10, w, 20);
                else FillRect(Pixels, cx - w / 2, cy - 10, w, 20, Gold);
            }

            // Bird (on the menu it is shown large under the title instead)
            float by = live ? sim.PrevBirdY + (sim.BirdY - sim.PrevBirdY) * alpha : sim.BirdY;
            if (sim.Screen != "menu")
            {
            int bs = (int)NativeSim.BirdSize;
            int bxp = (int)NativeSim.BirdX - bs / 2, byp = (int)by - bs / 2;
            if (bird != null) BlitScaled(bird, birdW, birdH, bxp, byp, bs, bs);
            else { FillRect(Pixels, bxp, byp, bs, bs, Orange); FillRect(Pixels, bxp + 18, byp + 8, 6, 6, White); }
            }

            // HUD
            if (sim.Screen != "menu")
                PixelFont.DrawCentred(Pixels, W, H, sim.Score.ToString(), W / 2, 22, 5, White, Shadow);
            if (sim.Coins > 0)
                PixelFont.Draw(Pixels, W, H, sim.Coins + " COINS", W - 12 - PixelFont.Measure(sim.Coins + " COINS", 2), 14, 2, Gold, Shadow);

            // Overlays
            switch (sim.Screen)
            {
                case "menu":
                    Dim(0.55f);
                    PixelFont.DrawCentred(Pixels, W, H, "FLAPPY CRIX", W / 2, 130, 5, Orange, Shadow);
                    if (bird != null) BlitScaled(bird, birdW, birdH, W / 2 - 36, 190, 72, 72);
                    PixelFont.DrawCentred(Pixels, W, H, "PRESS FLAP TO START", W / 2, 290, 2, White, Shadow);
                    PixelFont.DrawCentred(Pixels, W, H, "FLAP, X / A OR SPACE", W / 2, 330, 2, Green, Shadow);
                    PixelFont.DrawCentred(Pixels, W, H, "BEST " + sim.Best, W / 2, 380, 2, Gold, Shadow);
                    PixelFont.DrawCentred(Pixels, W, H, "OFFLINE VERSION", W / 2, 520, 2, PixelFont.Rgba("#8A8FB0"), Shadow);
                    break;
                case "paused":
                    Dim(0.55f);
                    PixelFont.DrawCentred(Pixels, W, H, "PAUSED", W / 2, 230, 5, Orange, Shadow);
                    PixelFont.DrawCentred(Pixels, W, H, "PRESS START OR PAUSE", W / 2, 310, 2, White, Shadow);
                    break;
                case "dead":
                    Dim(0.55f);
                    PixelFont.DrawCentred(Pixels, W, H, "GAME OVER", W / 2, 190, 5, Orange, Shadow);
                    PixelFont.DrawCentred(Pixels, W, H, "SCORE " + sim.Score + "   BEST " + sim.Best, W / 2, 280, 2, White, Shadow);
                    PixelFont.DrawCentred(Pixels, W, H, "PRESS FLAP TO RETRY", W / 2, 340, 2, Green, Shadow);
                    break;
            }
        }

        // ------------------------------------------------------------------ drawing helpers

        private static uint[] GradientRow(int w)
        {
            float[] at = { 0f, 0.18f, 0.42f, 0.72f, 1f };
            uint[] cols = { PixelFont.Rgba("#0B8F56"), PixelFont.Rgba("#00CC7A"), PixelFont.Rgba("#4DE8A8"), PixelFont.Rgba("#00CC7A"), PixelFont.Rgba("#076B41") };
            var row = new uint[w];
            for (int i = 0; i < w; i++)
            {
                float f = w == 1 ? 0 : i / (float)(w - 1);
                for (int k = 0; k < at.Length - 1; k++)
                    if (f >= at[k] && f <= at[k + 1]) { row[i] = Lerp(cols[k], cols[k + 1], (f - at[k]) / (at[k + 1] - at[k])); break; }
            }
            return row;
        }

        private void BlitRows(uint[] row, int x, int y, int h)
        {
            int w = row.Length;
            int x0 = Math.Max(0, x), x1 = Math.Min(W, x + w);
            if (x1 <= x0) return;
            for (int yy = Math.Max(0, y); yy < Math.Min(H, y + h); yy++)
                Array.Copy(row, x0 - x, Pixels, yy * W + x0, x1 - x0);
        }

        private void ShadeRow(int x, int y, int w, float k)
        {
            if (y < 0 || y >= H) return;
            for (int xx = Math.Max(0, x); xx < Math.Min(W, x + w); xx++)
                Pixels[y * W + xx] = Scale(Pixels[y * W + xx], k);
        }

        private void BlitScaled(uint[] src, int sw, int sh, int x, int y, int w, int h)
        {
            for (int dy = 0; dy < h; dy++)
            {
                int py = y + dy;
                if (py < 0 || py >= H) continue;
                int sy = dy * sh / h;
                for (int dx = 0; dx < w; dx++)
                {
                    int px = x + dx;
                    if (px < 0 || px >= W) continue;
                    uint s = src[sy * sw + dx * sw / w];
                    uint a = s >> 24;
                    if (a == 0) continue;
                    int i = py * W + px;
                    Pixels[i] = a >= 250 ? (s | 0xFF000000) : Blend(Pixels[i], s, a / 255f);
                }
            }
        }

        private void Dim(float k)
        {
            for (int i = 0; i < Pixels.Length; i++) Pixels[i] = Scale(Pixels[i], 1 - k);
        }

        private static void FillRect(uint[] buf, int x, int y, int w, int h, uint c)
        {
            for (int yy = Math.Max(0, y); yy < Math.Min(H, y + h); yy++)
                for (int xx = Math.Max(0, x); xx < Math.Min(W, x + w); xx++)
                    buf[yy * W + xx] = c;
        }

        private static void FillCircle(uint[] buf, int cx, int cy, int r, uint c)
        {
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= r * r && cx + x >= 0 && cx + x < W && cy + y >= 0 && cy + y < H)
                        buf[(cy + y) * W + cx + x] = c;
        }

        private static uint Lerp(uint a, uint b, float t)
        {
            byte r = (byte)((a & 0xFF) + (((int)(b & 0xFF) - (int)(a & 0xFF)) * t));
            byte g = (byte)(((a >> 8) & 0xFF) + (((int)((b >> 8) & 0xFF) - (int)((a >> 8) & 0xFF)) * t));
            byte bl = (byte)(((a >> 16) & 0xFF) + (((int)((b >> 16) & 0xFF) - (int)((a >> 16) & 0xFF)) * t));
            return PixelFont.Pack(r, g, bl, 255);
        }

        private static uint Blend(uint dst, uint src, float a)
        {
            byte r = (byte)((dst & 0xFF) * (1 - a) + (src & 0xFF) * a);
            byte g = (byte)(((dst >> 8) & 0xFF) * (1 - a) + ((src >> 8) & 0xFF) * a);
            byte b = (byte)(((dst >> 16) & 0xFF) * (1 - a) + ((src >> 16) & 0xFF) * a);
            return PixelFont.Pack(r, g, b, 255);
        }

        private static uint Scale(uint c, float k)
        {
            int r = Math.Min(255, (int)((c & 0xFF) * k)), g = Math.Min(255, (int)(((c >> 8) & 0xFF) * k)), b = Math.Min(255, (int)(((c >> 16) & 0xFF) * k));
            return PixelFont.Pack((byte)r, (byte)g, (byte)b, 255);
        }
    }
}
