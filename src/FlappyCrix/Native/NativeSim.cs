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
        public sealed class Coin { public float X, Y; public bool Taken; public int Id; public long HiddenUntil; }
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

        private float Rnd() => Mp ? (float)RngStep(ref mpState) : (float)rng.NextDouble();
        /// <summary>Decoration (cobwebs, pumpkins): the site's separate "prop" stream in multiplayer.</summary>
        private float PropRnd() => Mp ? (float)RngStep(ref propState) : (float)rng.NextDouble();
        private int PropNext(int n) => Mp ? Math.Min(n - 1, (int)(RngStep(ref propState) * n)) : rng.Next(n);

        // ------------------------------------------------------------------ multiplayer (the website's rules)
        // In a room every player builds the same pipes and coins from the host's seed, with the
        // site's Mulberry32-style generator (flappycrix.html rngStep), one fixed gap of 170 and
        // numbered coins, so a coin someone else takes disappears for everyone.

        public bool Mp { get; private set; }
        public bool CoinRush;
        public float Travelled { get; private set; }
        public long Ticks { get; private set; }            // ticks since the match started (respawns don't reset it)
        public int CoinsThisMatch;
        private uint mpState, propState;
        private int coinSeq;
        private readonly Dictionary<int, long> taken = new Dictionary<int, long>();   // coin id -> tick it went
        public const int CoinRespawnTicks = 600;           // 10 s
        public event Action<int> CoinTaken;                // my pickup: the coin's id

        public static double RngStep(ref uint s)
        {
            unchecked
            {
                s += 0x6D2B79F5u;
                uint z = s;
                z = (z ^ (z >> 15)) * (z | 1u);
                z ^= z + ((z ^ (z >> 7)) * (z | 61u));
                return (z ^ (z >> 14)) / 4294967296.0;
            }
        }

        /// <summary>A multiplayer run from the shared seed. newMatch = forget the coins taken so far.</summary>
        public void StartMp(uint seed, bool coinRush, bool newMatch)
        {
            StartRun();
            Mp = true;
            CoinRush = coinRush;
            mpState = seed != 0 ? seed : 12345u;
            unchecked { propState = mpState + 0x9E3779B9u; }
            coinSeq = 0;
            Travelled = 0;
            if (newMatch) { taken.Clear(); Ticks = 0; CoinsThisMatch = 0; }
        }

        /// <summary>Back to single player.</summary>
        public void LeaveMp() { Mp = false; CoinRush = false; taken.Clear(); }

        /// <summary>Back in after a crash (Race, Coin Rush): same world, bird at the start height, grace ticks.</summary>
        public void Respawn()
        {
            BirdY = PrevBirdY = 300; Velocity = 0; grace = GraceTicks;
            Screen = "playing";
        }

        /// <summary>Someone took coin id: it vanishes (for 10 s, or for good in Coin Rush).</summary>
        public void MarkTaken(int id)
        {
            taken[id] = Ticks;
            foreach (var c in CoinList) if (c.Id == id) c.HiddenUntil = CoinRush ? long.MaxValue : Ticks + CoinRespawnTicks;
        }

        public void EndMatch() { if (Screen == "playing") Screen = "dead"; }

        public void StartRun()
        {
            Pipes.Clear(); CoinList.Clear(); Pumpkins.Clear(); Trail.Clear();
            BirdY = PrevBirdY = 300; Velocity = 0; Frame = 0; Score = 0; RunCoins = 0; grace = GraceTicks;
            lastTop = 0; pendingTop = -1; lastPipeBeat = 0; HatAngle = HatVel = 0;
            Travelled = 0; Mp = false; CoinRush = false;
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

        public float Gap() => Mp ? 170f : Math.Max(182f, 215f - Score * 1.2f);

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

        /// <summary>What arrives at the right-hand edge this tick (steps 5 and 6 of updateGame): drawn
        /// from the shared generator in a fixed order, so every player in a room lays the identical lane.</summary>
        private void SpawnLane()
        {
            // 5. pipes, the top chosen one beat early
            if (Frame % PipeTicks == 0)
            {
                float gap = Gap();
                float top = pendingTop >= 0 ? pendingTop : NextTop(gap);
                lastTop = top; lastPipeBeat = Frame;
                int web = 0;
                if (Halloween && PropRnd() < 0.45f)
                {
                    int[] corners = { 1, 2, 4, 8 };
                    web = corners[PropNext(4)];
                    if (PropRnd() < 0.35f) web |= corners[PropNext(4)];
                }
                Pipes.Add(new Pipe { X = W, Top = top, Bottom = top + Gap(), Web = web });
                pendingTop = NextTop(gap);
            }

            // 6. coins
            if (Frame % (CoinRush ? 18 : CoinTicks) == 0)
            {
                float gap = Gap();
                float spread = gap * 0.30f;
                float y = Pipes.Count > 0 ? LaneCentre() + (Rnd() - 0.5f) * spread * 2 : Rnd() * (H - 200) + 100;
                var coin = new Coin { X = W, Y = Math.Max(60, Math.Min(H - 90, y)) - 10, Id = ++coinSeq };
                long went;
                if (Mp && taken.TryGetValue(coin.Id, out went)) coin.HiddenUntil = CoinRush ? long.MaxValue : went + CoinRespawnTicks;
                CoinList.Add(coin);
            }
        }

        // ------------------------------------------------------------------ staying with the group
        // In a Freemode or Coin Rush room a crash used to restart the lane from the very start
        // while everyone else flew on, so from the first crash you were somewhere else in the
        // level - off their screens and they off yours. Now the lane carries on while you're
        // down, exactly as theirs does, and you come back where they are (the site does the same).
        private bool ghost;

        /// <summary>One tick of the lane with nobody in it: while I'm down in a room.</summary>
        public void LaneOnlyStep()
        {
            if (!Mp) return;
            ghost = true;
            try
            {
                BgOffset += SceneryStep;
                Travelled += Speed;
                Ticks++;
                SpawnLane();
                for (int i = 0; i < Pipes.Count; i++)
                {
                    var p = Pipes[i];
                    p.X -= Speed;
                    if (p.X + PipeWidth < BirdX) p.Scored = true;      // flown past while down: no points
                    if (p.X + PipeWidth < 0) Pipes.RemoveAt(i--);
                }
                for (int i = 0; i < CoinList.Count; i++)
                {
                    CoinList[i].X -= Speed;
                    if (CoinList[i].X + 20 < 0) CoinList.RemoveAt(i--);
                }
                Frame++;
                if (Halloween) StepPumpkins();
            }
            finally { ghost = false; }
        }

        /// <summary>The tick a bird crashes on stops short: its distance has moved on but the lane after
        /// the crash point hasn't. In a room the lane carries on, so it finishes the tick (with nobody in
        /// it) - otherwise every crash left this lane a step out from everyone else's.</summary>
        private void FinishCrashTick(int fromPipe, bool spawn)
        {
            if (!Mp) return;
            ghost = true;
            try
            {
                if (spawn) SpawnLane();
                for (int i = fromPipe; i < Pipes.Count; i++)
                {
                    var p = Pipes[i];
                    p.X -= Speed;
                    if (p.X + PipeWidth < BirdX) p.Scored = true;
                    if (p.X + PipeWidth < 0) Pipes.RemoveAt(i--);
                }
                for (int i = 0; i < CoinList.Count; i++)
                {
                    CoinList[i].X -= Speed;
                    if (CoinList[i].X + 20 < 0) CoinList.RemoveAt(i--);
                }
                Frame++;
                if (Halloween) StepPumpkins();
            }
            finally { ghost = false; }
        }

        /// <summary>Back in after a crash, in the same lane: a new run (score 0), bird at the start height.</summary>
        public void Rejoin()
        {
            Score = 0; RunCoins = 0; Trail.Clear(); HatAngle = HatVel = 0;
            Respawn();
        }

        /// <summary>One 60 Hz tick (updateGame).</summary>
        public void Step()
        {
            if (Screen != "playing") return;

            // 1. scenery and trail
            BgOffset += SceneryStep;
            Travelled += Speed;
            Ticks++;
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
                else { Die(); FinishCrashTick(0, true); return; }
            }

            // 5-6. pipes and coins arrive
            SpawnLane();

            // 7. pipes move, hit, score
            float rx = BirdSize * HitRX, ry = BirdSize * HitRY;
            for (int i = 0; i < Pipes.Count; i++)
            {
                var p = Pipes[i];
                p.X -= Speed;
                if (!shield && (HitsRectE(BirdX, BirdY, rx, ry, p.X, -1000, PipeWidth, p.Top + 1000) ||
                                HitsRectE(BirdX, BirdY, rx, ry, p.X, p.Bottom, PipeWidth, H - p.Bottom + 20)))
                { Die(); FinishCrashTick(i + 1, false); return; }
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
                if (c.HiddenUntil > Ticks) { if (c.X + 20 < 0) CoinList.RemoveAt(i--); continue; }
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
                    CoinsThisMatch++;
                    if (Mp) { taken[c.Id] = Ticks; CoinTaken?.Invoke(c.Id); }
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
            if (Frame % PipeTicks == 60 && PropRnd() < 0.42f)
                Pumpkins.Add(new Pumpkin
                {
                    X = W + 30, Y = Math.Max(90, Math.Min(H - 120, LaneCentre() + (PropRnd() - 0.5f) * 40)),
                    Tilt = (PropRnd() - 0.5f) * 0.4f, Prize = PropRnd(),
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
                if (!ghost && dx * dx / (brx * brx) + dy * dy / (bry * bry) < 1) Smash(p);
                else if (p.X < -40) Pumpkins.RemoveAt(i--);
            }
        }

        private void Smash(Pumpkin p)
        {
            p.Smashed = 46;
            p.Chunks = new Chunk[8];
            for (int i = 0; i < 8; i++)
            {
                double a = i / 8.0 * 2 * Math.PI + PropRnd() * 0.4;
                float sp = 1.6f + PropRnd() * 2.4f;
                p.Chunks[i] = new Chunk
                {
                    X = p.X, Y = p.Y, VX = (float)Math.Cos(a) * sp + 1.2f, VY = (float)Math.Sin(a) * (1.6f + PropRnd() * 2.4f) - 1.4f,
                    R = 4 + PropRnd() * 6, Spin = (PropRnd() - 0.5f) * 0.35f,
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
