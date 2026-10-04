// Flappy Crix game rules for the native fallback, ported from flappycrix.html
// (CRIX447/crix-website): 400x600 playfield, 60 Hz ticks, PHYS constants, ellipse
// hitbox, 120-tick pipe cadence, gap that narrows with score, capped gap step,
// no ceiling death. No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;

namespace FlappyCrix.Native
{
    public sealed class NativeSim
    {
        public const int W = 400, H = 600;
        public const float Gravity = 0.52f, FallGrav = 0.25f, FlapV = -8.8f, MaxFall = 7.5f;
        public const float HitRX = 0.40f, HitRY = 0.467f, BirdSize = 30f, BirdX = 100f;
        public const int GraceTicks = 24, PipeTicks = 120, CoinTicks = 50;
        public const float PipeWidth = 50f, Speed = 2.5f, GroundH = 40f;

        public sealed class Pipe { public float X, Top, Bottom; public bool Scored; }
        public sealed class Coin { public float X, Y; public bool Taken; }

        public string Screen { get; private set; } = "menu";   // menu, playing, paused, dead
        public int Score { get; private set; }
        public int Best;
        public int Coins { get; private set; }
        public int Frame { get; private set; }
        public float BirdY { get; private set; } = 300;
        public float PrevBirdY = 300, Velocity;
        public readonly List<Pipe> Pipes = new List<Pipe>();
        public readonly List<Coin> CoinList = new List<Coin>();

        /// <summary>Sound names from the site's img/ folder: jump, death, coin, select, milestone.</summary>
        public event Action<string> Sound;
        /// <summary>Raised when a run ends with a new best.</summary>
        public event Action<int> NewBest;

        private int grace;
        private float lastTop;
        private readonly Random rng;

        public NativeSim(int seed = 0) { rng = seed == 0 ? new Random() : new Random(seed); }

        public void StartRun()
        {
            Pipes.Clear(); CoinList.Clear();
            BirdY = PrevBirdY = 300; Velocity = 0; Frame = 0; Score = 0; grace = GraceTicks; lastTop = 0;
            Screen = "playing";
            Sound?.Invoke("select");
        }

        public void Flap()
        {
            if (Screen == "playing") { Velocity = FlapV; Sound?.Invoke("jump"); }
            else if (Screen == "menu" || Screen == "dead") StartRun();
        }

        public void TogglePause()
        {
            if (Screen == "playing") Screen = "paused";
            else if (Screen == "paused") Screen = "playing";
        }

        private float Gap() => Math.Max(182f, 215f - Score * 1.2f);

        private float NextTop(float gap)
        {
            float top = (float)rng.NextDouble() * (H - gap - 150) + 70;
            if (lastTop > 0) top = Math.Max(lastTop - 150, Math.Min(lastTop + 150, top));
            return Math.Max(70, Math.Min(H - gap - 80, top));
        }

        private static bool HitsRectE(float cx, float cy, float erx, float ery, float rx, float ry, float rw, float rh)
        {
            float sx = 1 / erx, sy = 1 / ery;
            float nx = Math.Max(rx * sx, Math.Min(cx * sx, (rx + rw) * sx));
            float ny = Math.Max(ry * sy, Math.Min(cy * sy, (ry + rh) * sy));
            float dx = cx * sx - nx, dy = cy * sy - ny;
            return dx * dx + dy * dy < 1;
        }

        /// <summary>One 60 Hz tick.</summary>
        public void Step()
        {
            if (Screen != "playing") return;
            PrevBirdY = BirdY;
            if (grace > 0) grace--;
            Velocity += Velocity < 0 ? Gravity : FallGrav;
            if (Velocity > MaxFall) Velocity = MaxFall;
            BirdY += Velocity;
            if (BirdY < BirdSize / 2) { BirdY = BirdSize / 2; if (Velocity < 0) Velocity = 0; }   // no ceiling death, as on the site
            if (BirdY > H - 10)
            {
                if (grace > 0) { BirdY = H - 10; Velocity = -Math.Abs(Velocity) * 0.5f; }
                else { Die(); return; }
            }

            if (Frame % PipeTicks == 0)
            {
                float gap = Gap(), top = NextTop(gap);
                lastTop = top;
                Pipes.Add(new Pipe { X = W, Top = top, Bottom = top + gap });
            }
            if (Frame % CoinTicks == 0)
            {
                float y = Pipes.Count > 0 ? (lastTop + Gap() / 2) + ((float)rng.NextDouble() - 0.5f) * Gap() * 0.6f
                                          : (float)rng.NextDouble() * (H - 200) + 100;
                CoinList.Add(new Coin { X = W, Y = Math.Max(60, Math.Min(H - 90, y)) - 10 });
            }

            float rx = BirdSize * HitRX, ry = BirdSize * HitRY;
            for (int i = 0; i < Pipes.Count; i++)
            {
                var p = Pipes[i];
                p.X -= Speed;
                if (grace <= 0 && (HitsRectE(BirdX, BirdY, rx, ry, p.X, -1000, PipeWidth, p.Top + 1000) ||
                                   HitsRectE(BirdX, BirdY, rx, ry, p.X, p.Bottom, PipeWidth, H - p.Bottom + 20)))
                { Die(); return; }
                if (p.X + PipeWidth < BirdX && !p.Scored)
                {
                    p.Scored = true; Score++;
                    if (Score % 10 == 0) Sound?.Invoke("milestone");
                }
                if (p.X + PipeWidth < 0) Pipes.RemoveAt(i--);
            }
            for (int i = 0; i < CoinList.Count; i++)
            {
                var c = CoinList[i];
                c.X -= Speed;
                if (!c.Taken && Math.Abs(c.X + 10 - BirdX) < 22 && Math.Abs(c.Y + 10 - BirdY) < 24)
                { c.Taken = true; Coins++; Sound?.Invoke("coin"); }
                if (c.X + 20 < 0) CoinList.RemoveAt(i--);
            }
            Frame++;
        }

        private void Die()
        {
            Screen = "dead";
            Sound?.Invoke("death");
            if (Score > Best) { Best = Score; NewBest?.Invoke(Best); }
        }
    }
}
