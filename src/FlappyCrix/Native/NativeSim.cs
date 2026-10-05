// Flappy Crix game rules, ported tick for tick from the website (flappycrix.html: PHYS,
// resetGame, updateGame, nextPipeTop, laneCentreForSpawn, the Halloween pumpkins and cobwebs):
// 400x600 playfield, 60 Hz ticks, the ellipse hitbox, 120-tick pipe beat, the gap that narrows
// with score, coins every 50 ticks, power-ups, death below y = 590 (the bird sinks into the
// drawn ground first, as on the site) and no ceiling death.
// One deliberate fix: the site forgets to save the best score after a pipe death; here every
// death counts.
// No UnityEngine references (tested outside Unity).
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
        public const int GraceTicks = 24, PipeTicks = 120, CoinTicks = 50, TrailPoints = 44;
        public const float PipeWidth = 50f, Speed = 2.5f, FloorY = H - 10, SceneryStep = 0.4f;
        public const int GroundH = 66, SkyH = H - GroundH;          // drawn ground: round(600 * 0.11)

        public sealed class Pipe { public float X, Top, Bottom; public bool Scored; public int Web; }
        public sealed class Coin { public float X, Y; public bool Taken; }
        public sealed class Chunk { public float X, Y, VX, VY, R, Rot, Spin; }
        public sealed class Pumpkin
        {
            public float X, Y, R = 21, Tilt, Prize;
            public int Smashed;                      // ticks left of the smash animation (0 = whole)
            public Chunk[] Chunks;
            public string Text; public bool CoinText;
        }

        public string Screen { get; private set; } = "menu";   // menu, playing, paused, dead
        public int Score { get; private set; }
        public int Frame { get; private set; }
        public int RunCoins { get; private set; }
        public float BirdY { get; private set; } = 300;
        public float PrevBirdY = 300, Velocity, BgOffset, HatAngle, HatVel;
        public readonly List<Pipe> Pipes = new List<Pipe>();
        public readonly List<Coin> CoinList = new List<Coin>();
        public readonly List<Pumpkin> Pumpkins = new List<Pumpkin>();
        public readonly List<float> Trail = new List<float>();    // x, y pairs, oldest first

        /// <summary>Halloween lane props (pumpkins, cobwebs): set from the active season.</summary>
        public bool Halloween;
        public bool ReducedMotion;

        /// <summary>Power-ups: seconds left (real time, counted by the caller - they run on while paused, as on the site).</summary>
        public readonly Dictionary<string, float> Powerups = new Dictionary<string, float>();
        public bool PowerupOn(string id) { float t; return Powerups.TryGetValue(id, out t) && t > 0; }
        public int CoinMultiplier => PowerupOn("x2coins") ? 2 : 1;
        public bool Shielded => PowerupOn("shield") || grace > 0;

        /// <summary>Screen shake: pixels and seconds left.</summary>
        public float ShakePx, ShakeLeft;

        /// <summary>A sound to play: name (file in img/) and volume.</summary>
        public event Action<string, float> Sound;
        /// <summary>Coins picked up (already multiplied).</summary>
        public event Action<int> CoinsGained;
        /// <summary>A pipe was passed (the new score).</summary>
        public event Action<int> Scored;
        /// <summary>The run ended (final score).</summary>
        public event Action<int> Died;
        /// <summary>A power-up was granted for this many seconds (pumpkin prize).</summary>
        public event Action<string, int> PowerupGranted;

        private int grace, lastPipeBeat;
        private float lastTop, pendingTop = -1;
        private readonly Random rng;

        public NativeSim(int seed = 0) { rng = seed == 0 ? new Random() : new Random(seed); }

        private float Rnd() => (float)rng.NextDouble();

        public void StartRun()
        {
            Pipes.Clear(); CoinList.Clear(); Pumpkins.Clear(); Trail.Clear();
            BirdY = PrevBirdY = 300; Velocity = 0; Frame = 0; Score = 0; RunCoins = 0; grace = GraceTicks;
            lastTop = 0; pendingTop = -1; lastPipeBeat = 0; HatAngle = HatVel = 0;
            Screen = "playing";
        }

        public void ToMenu() { Screen = "menu"; Pipes.Clear(); CoinList.Clear(); Pumpkins.Clear(); Trail.Clear(); BirdY = PrevBirdY = 300; }

        /// <summary>Flap: only while a run is going (the site's jump()).</summary>
        public bool Flap()
        {
            if (Screen != "playing") return false;
            Velocity = FlapV;
            Sound?.Invoke("jump", 1f);
            return true;
        }

        public void Pause() { if (Screen == "playing") Screen = "paused"; }
        public void Resume() { if (Screen == "paused") Screen = "playing"; }

        public float Gap() => Math.Max(182f, 215f - Score * 1.2f);

        private float NextTop(float gap)
        {
            float top = Rnd() * (H - gap - 150) + 70;
            if (lastTop > 0) top = Math.Max(lastTop - 150, Math.Min(lastTop + 150, top));
            return Math.Max(70, Math.Min(H - gap - 80, top));
        }

        public float LaneCentre()
        {
            if (Pipes.Count == 0) return H / 2f;
            float gap = Gap();
            float f = Math.Max(0, Math.Min(120, Frame - lastPipeBeat)) / 120f;
            float a = lastTop + gap / 2, b = (pendingTop >= 0 ? pendingTop : lastTop) + gap / 2;
            return a + (b - a) * f;
        }

        public static bool HitsRectE(float cx, float cy, float erx, float ery, float rx, float ry, float rw, float rh)
        {
            float sx = 1 / erx, sy = 1 / ery;
            float nx = Math.Max(rx * sx, Math.Min(cx * sx, (rx + rw) * sx));
            float ny = Math.Max(ry * sy, Math.Min(cy * sy, (ry + rh) * sy));
            float dx = cx * sx - nx, dy = cy * sy - ny;
            return dx * dx + dy * dy < 1;
        }

        /// <summary>The bird's tilt in radians (the site: velocity x 0.035, clamped).</summary>
        public float BirdRotation => Math.Max(-0.30f, Math.Min(0.40f, Velocity * 0.035f));

        /// <summary>Real-time part: power-up timers and the screen shake.</summary>
        public void TickRealTime(float dt)
        {
            // like the site's setInterval timers: they count down from the moment they're bought,
            // in the menu and while paused too
            if (Powerups.Count > 0)
            {
                var keys = new List<string>(Powerups.Keys);
                foreach (var k in keys) { Powerups[k] = Math.Max(0, Powerups[k] - dt); if (Powerups[k] <= 0) Powerups.Remove(k); }
            }
            if (ShakeLeft > 0) ShakeLeft = Math.Max(0, ShakeLeft - dt);
        }

        public void GrantPowerup(string id, int seconds)
        {
            float left;
            Powerups.TryGetValue(id, out left);
            Powerups[id] = Math.Max(left, seconds);
        }

        /// <summary>One 60 Hz tick (updateGame).</summary>
        public void Step()
        {
            if (Screen != "playing") return;
            float gap;

            // 1. scenery and trail
            BgOffset += SceneryStep;
            for (int i = 0; i < Trail.Count; i += 2) Trail[i] -= Speed;
            Trail.Add(BirdX); Trail.Add(BirdY);
            while (Trail.Count > TrailPoints * 2) Trail.RemoveRange(0, 2);

            // 2. grace (counts as a shield)
            PrevBirdY = BirdY;
            if (grace > 0) grace--;
            bool shield = Shielded;

            // 3. gravity (v == 0 uses the falling gravity), terminal speed, no ceiling death
            Velocity += Velocity < 0 ? Gravity : FallGrav;
            if (Velocity > MaxFall) Velocity = MaxFall;
            BirdY += Velocity;
            if (BirdY < BirdSize / 2) { BirdY = BirdSize / 2; if (Velocity < 0) Velocity = 0; }

            // hat wobble spring (cosmetic-art.js)
            float target = Math.Max(-0.42f, Math.Min(0.42f, Velocity * 0.030f));
            HatVel += (target - HatAngle) * 0.16f; HatVel *= 0.70f; HatAngle = Math.Max(-0.52f, Math.Min(0.52f, HatAngle + HatVel));

            // 4. floor: a shield bounces, otherwise the run ends
            if (BirdY > FloorY)
            {
                if (shield) { BirdY = FloorY; Velocity = -Math.Abs(Velocity) * 0.5f; }
                else { Die(); return; }
            }

            // 5. pipes, the top chosen one beat early
            if (Frame % PipeTicks == 0)
            {
                gap = Gap();
                float top = pendingTop >= 0 ? pendingTop : NextTop(gap);
                lastTop = top; lastPipeBeat = Frame;
                int web = 0;
                if (Halloween && Rnd() < 0.45f)
                {
                    int[] corners = { 1, 2, 4, 8 };
                    web = corners[rng.Next(4)];
                    if (Rnd() < 0.35f) web |= corners[rng.Next(4)];
                }
                Pipes.Add(new Pipe { X = W, Top = top, Bottom = top + Gap(), Web = web });
                pendingTop = NextTop(gap);
            }

            // 6. coins
            if (Frame % CoinTicks == 0)
            {
                gap = Gap();
                float spread = gap * 0.30f;
                float y = Pipes.Count > 0 ? LaneCentre() + (Rnd() - 0.5f) * spread * 2 : Rnd() * (H - 200) + 100;
                CoinList.Add(new Coin { X = W, Y = Math.Max(60, Math.Min(H - 90, y)) - 10 });
            }

            // 7. pipes move, hit, score
            float rx = BirdSize * HitRX, ry = BirdSize * HitRY;
            for (int i = 0; i < Pipes.Count; i++)
            {
                var p = Pipes[i];
                p.X -= Speed;
                if (!shield && (HitsRectE(BirdX, BirdY, rx, ry, p.X, -1000, PipeWidth, p.Top + 1000) ||
                                HitsRectE(BirdX, BirdY, rx, ry, p.X, p.Bottom, PipeWidth, H - p.Bottom + 20)))
                { Die(); return; }
                if (p.X + PipeWidth < BirdX && !p.Scored)
                {
                    p.Scored = true; Score++;
                    if (Score % 10 == 0) Sound?.Invoke("milestone", 0.45f);
                    Scored?.Invoke(Score);
                }
                if (p.X + PipeWidth < 0) Pipes.RemoveAt(i--);
            }

            // 8. coins move, magnet, pick up (a fixed +-15 box, as on the site)
            bool magnet = PowerupOn("magnet");
            for (int i = 0; i < CoinList.Count; i++)
            {
                var c = CoinList[i];
                c.X -= Speed;
                if (magnet && !c.Taken)
                {
                    float dx = BirdX - (c.X + 10), dy = BirdY - (c.Y + 10);
                    if (dx * dx + dy * dy < 150 * 150) { c.X += dx * 0.08f; c.Y += dy * 0.08f; }
                }
                if (!c.Taken && BirdX + 15 > c.X && BirdX - 15 < c.X + 20 && BirdY + 15 > c.Y && BirdY - 15 < c.Y + 20)
                {
                    c.Taken = true;
                    int n = CoinMultiplier;
                    RunCoins += n;
                    Sound?.Invoke("coin", 1f);
                    CoinsGained?.Invoke(n);
                }
                if (c.Taken || c.X + 20 < 0) CoinList.RemoveAt(i--);
            }

            Frame++;

            // 10. Halloween pumpkins on the half-beat between pipes
            if (Halloween) StepPumpkins();
        }

        private void StepPumpkins()
        {
            if (Frame % PipeTicks == 60 && Rnd() < 0.42f)
                Pumpkins.Add(new Pumpkin
                {
                    X = W + 30, Y = Math.Max(90, Math.Min(H - 120, LaneCentre() + (Rnd() - 0.5f) * 40)),
                    Tilt = (Rnd() - 0.5f) * 0.4f, Prize = Rnd(),
                });
            float brx = BirdSize * HitRX + 21, bry = BirdSize * HitRY + 21 * 0.86f;
            for (int i = 0; i < Pumpkins.Count; i++)
            {
                var p = Pumpkins[i];
                if (p.Smashed > 0)
                {
                    p.Smashed--;
                    foreach (var c in p.Chunks) { c.X += c.VX - Speed; c.Y += c.VY; c.VY += 0.42f; c.Rot += c.Spin; }
                    if (p.Smashed == 0) { Pumpkins.RemoveAt(i--); }
                    continue;
                }
                p.X -= Speed;
                float dx = BirdX - p.X, dy = BirdY - p.Y;
                if (dx * dx / (brx * brx) + dy * dy / (bry * bry) < 1) Smash(p);
                else if (p.X < -40) Pumpkins.RemoveAt(i--);
            }
        }

        private void Smash(Pumpkin p)
        {
            p.Smashed = 46;
            p.Chunks = new Chunk[8];
            for (int i = 0; i < 8; i++)
            {
                double a = i / 8.0 * 2 * Math.PI + Rnd() * 0.4;
                float sp = 1.6f + Rnd() * 2.4f;
                p.Chunks[i] = new Chunk
                {
                    X = p.X, Y = p.Y, VX = (float)Math.Cos(a) * sp + 1.2f, VY = (float)Math.Sin(a) * (1.6f + Rnd() * 2.4f) - 1.4f,
                    R = 4 + Rnd() * 6, Spin = (Rnd() - 0.5f) * 0.35f,
                };
            }
            Sound?.Invoke("smash", 0.8f);
            if (!ReducedMotion) { ShakePx = 5; ShakeLeft = 0.14f; }
            float roll = p.Prize;
            if (roll < 0.55f)
            {
                int n = (25 + (int)Math.Floor(roll / 0.55f * 60)) * CoinMultiplier;
                RunCoins += n;
                p.Text = "+" + n; p.CoinText = true;
                Sound?.Invoke("coin", 1f);
                CoinsGained?.Invoke(n);
            }
            else
            {
                string[] ids = { "shield", "magnet", "x2coins" };
                string id = ids[Math.Min(2, (int)Math.Floor((roll - 0.55f) / 0.45f * 3))];
                GrantPowerup(id, 10);
                p.Text = (id == "x2coins" ? "2X COINS" : id.ToUpperInvariant()) + " 10s";
                Sound?.Invoke("unlock", 0.55f);
                PowerupGranted?.Invoke(id, 10);
            }
        }

        private void Die()
        {
            Screen = "dead";
            Sound?.Invoke("death", 1f);
            Died?.Invoke(Score);
        }

        /// <summary>For tests and the attract screen: put the bird somewhere.</summary>
        public void SetBird(float y, float v) { BirdY = PrevBirdY = y; Velocity = v; }
    }
}
