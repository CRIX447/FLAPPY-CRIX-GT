// The arcade cabinet's half of the game: what makes the in-game Flappy Crix behave like a machine
// in a room full of players.
//
//   - attract mode: left alone on the menu, the screen cycles the logo and the high scores
//   - credits: FREE PLAY, or one coin in the slot per game
//   - a high-score table per cabinet, with three-letter initials entered on the joystick
//   - races with the players standing at the other cabinets of the same link group: the same
//     seed for everyone (same pipes and coins), a 3-2-1 countdown, the others as ghost birds
//   - watching someone else's race, and an "in use" card while another player has the cabinet
//
// The Unity side (ArcadeMachine + ArcadeLink) decides who is near and talks to the network;
// everything here only changes what the game shows and does.
// No UnityEngine references (tested outside Unity).
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Globalization;

namespace CrixArcade.Core
{
    public sealed partial class FlappyApp
    {
        // ------------------------------------------------------------------ settings (set by the cabinet)

        /// <summary>Arcade look and rules. False gives the plain in-game Flappy Crix.</summary>
        public bool ArcadeMode = true;
        /// <summary>No coins needed. When false, every game costs one credit.</summary>
        public bool FreePlay = true;
        /// <summary>Seconds alone on the main menu before attract mode starts (0 = never).</summary>
        public double AttractAfter = 40;
        /// <summary>Shows RACE on the main menu (the cabinet is in a link group).</summary>
        public bool RaceAvailable;
        /// <summary>How many other players are waiting in this cabinet group's race lobby.</summary>
        public int NearbyRacers;

        public int Credits { get; private set; }
        public bool NeedsCoin => ArcadeMode && !FreePlay && Credits <= 0;

        // ------------------------------------------------------------------ events for the cabinet

        /// <summary>The player pressed RACE on the menu.</summary>
        public event Action RaceRequested;
        /// <summary>The player left the race lobby.</summary>
        public event Action RaceLeft;
        /// <summary>The player's race run ended (final score).</summary>
        public event Action<int> RaceDied;
        /// <summary>A high score was entered (initials, score). The cabinet saves and shares it.</summary>
        public event Action<string, int> HighScoreEntered;
        /// <summary>A run started on this cabinet (seed, race or not).</summary>
        public event Action<int, bool> RunStarted;
        /// <summary>A run ended on this cabinet (final score).</summary>
        public event Action<int> RunEnded;

        // ------------------------------------------------------------------ high scores

        public sealed class HighScore { public string Name; public int Score; }
        public const int HighScoreSlots = 10;
        /// <summary>Best first. The cabinet fills and saves it; entries from other players arrive here too.</summary>
        public readonly List<HighScore> HighScores = new List<HighScore>();

        public bool Qualifies(int score)
        {
            if (score <= 0) return false;
            if (HighScores.Count < HighScoreSlots) return true;
            return score > HighScores[HighScores.Count - 1].Score;
        }

        /// <summary>Adds a score to the table (keeps it sorted and at most HighScoreSlots long).</summary>
        public void AddHighScore(string name, int score)
        {
            name = CleanInitials(name);
            int i = 0;
            while (i < HighScores.Count && HighScores[i].Score >= score) i++;
            if (i >= HighScoreSlots) return;
            HighScores.Insert(i, new HighScore { Name = name, Score = score });
            while (HighScores.Count > HighScoreSlots) HighScores.RemoveAt(HighScores.Count - 1);
            Changed();
        }

        public static string CleanInitials(string s)
        {
            var b = new System.Text.StringBuilder();
            foreach (char ch in (s ?? "").ToUpperInvariant())
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == ' ') { b.Append(ch); if (b.Length == 3) break; }
            while (b.Length < 3) b.Append(' ');
            return b.ToString();
        }

        // ------------------------------------------------------------------ race

        public sealed class Ghost
        {
            public string Id, Name;
            public float Y = 300, ShownY = 300;
            public bool Alive = true;
            public int Score;
            public uint Colour;
        }
        /// <summary>The other racers, by id. Set from the cabinet as their positions arrive.</summary>
        public readonly Dictionary<string, Ghost> Ghosts = new Dictionary<string, Ghost>();

        public bool InRace { get; private set; }
        public bool Spectating { get; private set; }
        public string SpectateName { get; private set; }
        public int RaceSeed { get; private set; }
        /// <summary>Lobby lines (who is in, what is happening). Null = not in a lobby.</summary>
        public string RaceStatus { get; private set; }
        public string[] RaceLobbyNames = new string[0];
        /// <summary>The final standings once everyone has crashed (name, score), shown on the game-over card.</summary>
        public List<KeyValuePair<string, int>> RaceResult;

        double countdownEnd = -1, countdownStarted;
        int lastCountdownSecond = -1;
        bool countdownForSpectate;

        static readonly uint[] GhostColours =
        {
            Col.Hex("#27A9F5"), Col.Hex("#FFD54A"), Col.Hex("#C77DFF"), Col.Hex("#5BE39A"),
            Col.Hex("#FF8FAB"), Col.Hex("#FFFFFF"), Col.Hex("#7CF0F0"), Col.Hex("#FF9F43"),
        };

        public void SetLobby(string status, string[] names, int others)
        {
            RaceStatus = status;
            RaceLobbyNames = names ?? new string[0];
            NearbyRacers = others;
            Changed();
        }

        public void ClearLobby() { RaceStatus = null; RaceLobbyNames = new string[0]; Changed(); }

        /// <summary>The race leader started the countdown: the run begins in `seconds`.</summary>
        public void BeginCountdown(int seed, double seconds, bool spectate = false, string spectateName = null)
        {
            RaceSeed = seed;
            countdownStarted = now;
            countdownEnd = now + Math.Max(0, seconds);
            lastCountdownSecond = -1;
            countdownForSpectate = spectate;
            SpectateName = spectateName;
            modal = null;
            RaceResult = null;
            if (!spectate && Sim.Screen != "menu") Sim.ToMenu();
            Changed();
        }

        public bool CountingDown => countdownEnd > now;

        void StartRaceRun()
        {
            countdownEnd = -1;
            modal = null;
            Ghosts.Clear();
            Sim.Reseed(RaceSeed);
            Sim.Powerups.Clear();
            Sim.Immortal = countdownForSpectate;
            Spectating = countdownForSpectate;
            InRace = !countdownForSpectate;
            Sim.Halloween = false;
            Sim.StartRun();
            acc = 0; newBest = false; rankPlayed = false; popup = null;
            if (InRace) { Save.Games++; Save.Dirty = true; }
            MusicRestarts++;
            Sfx("select", 1f);
            Changed();
            if (InRace) RunStarted?.Invoke(RaceSeed, true);
        }

        /// <summary>Watch another player's race on this cabinet's screen.</summary>
        public void Spectate(int seed, string name, double startedSecondsAgo)
        {
            BeginCountdown(seed, 0, true, name);
            spectateY = -1;
            StartRaceRun();
            // catch up with a race that is already going
            int steps = (int)Math.Min(60 * 60, Math.Max(0, startedSecondsAgo) * 60);
            for (int i = 0; i < steps && Sim.Screen == "playing"; i++) Sim.Step();
        }

        /// <summary>Where the watched player's bird is (from the network).</summary>
        public void SetSpectatedBird(float y, bool alive)
        {
            if (!Spectating) return;
            if (spectateY < 0) Sim.SetBird(y, 0);
            spectateY = y;
            if (!alive && Sim.Screen == "playing") { Sim.Immortal = false; Sim.SetBird(NativeSim.FloorY + 1, 1); }
        }
        float spectateY = -1;

        /// <summary>A short message on the screen (title, text).</summary>
        public void Notice(string title, string body) { Sfx("error", 0.8f); AddToast(null, title, body, Gold); }

        public void StopSpectating()
        {
            if (!Spectating) return;
            Spectating = false; SpectateName = null; Sim.Immortal = false;
            Sim.ToMenu();
            Changed();
        }

        public void SetGhost(string id, string name, float y, bool alive, int score)
        {
            Ghost g;
            if (!Ghosts.TryGetValue(id, out g))
            {
                g = new Ghost { Id = id, Colour = GhostColours[Ghosts.Count % GhostColours.Length], ShownY = y };
                Ghosts[id] = g;
            }
            g.Name = name; g.Y = y; g.Alive = alive; g.Score = score;
        }

        public void RemoveGhost(string id) { Ghosts.Remove(id); }

        public void EndRace(List<KeyValuePair<string, int>> result)
        {
            InRace = false;
            RaceResult = result;
            Ghosts.Clear();
            ClearLobby();
        }

        void RequestRace()
        {
            if (NeedsCoin) { Refuse("Insert a coin to race."); return; }
            RaceRequested?.Invoke();
        }

        // ------------------------------------------------------------------ credits / in use

        /// <summary>A coin went in the slot.</summary>
        public void InsertCoin()
        {
            Credits = Math.Min(99, Credits + 1);
            lastInputAt = now;
            Sfx("coin", 1f);
            Changed();
        }

        /// <summary>Someone else is playing this cabinet: their name and score (null = free).</summary>
        public string RemoteName { get; private set; }
        public int RemoteScore { get; private set; }
        public string RemoteScreen { get; private set; }

        public void SetRemote(string name, int score, string screen)
        {
            if (name != RemoteName || score != RemoteScore || screen != RemoteScreen) Changed();
            RemoteName = name; RemoteScore = score; RemoteScreen = screen;
        }

        bool ArcadeMayStart()
        {
            if (!ArcadeMode) return true;
            if (RemoteName != null || Spectating || CountingDown || entering) return false;
            if (!FreePlay)
            {
                if (Credits <= 0) { Sfx("error", 1f); AddToast("coin", "INSERT COIN", "Put a coin in the slot to play.", Gold); return false; }
                Credits--;
                Changed();
            }
            return true;
        }

        // ------------------------------------------------------------------ clock

        double lastInputAt;
        public bool Attract => ArcadeMode && AttractAfter > 0 && Sim.Screen == "menu" && modal == null && !entering &&
                               RemoteName == null && !Spectating && !CountingDown && RaceStatus == null && now - lastInputAt > AttractAfter;

        void ArcadeUpdate(float dt)
        {
            if (countdownEnd > 0)
            {
                int left = (int)Math.Ceiling(countdownEnd - now);
                if (left != lastCountdownSecond && left > 0) { lastCountdownSecond = left; Sfx("pop", 0.6f); }
                if (now >= countdownEnd) StartRaceRun();
            }
            // ghosts glide towards their newest position (they arrive a few times a second)
            float k = Math.Min(1f, dt * 14f);
            foreach (var g in Ghosts.Values) g.ShownY += (g.Y - g.ShownY) * k;
            if (Spectating && Sim.Screen == "playing" && spectateY >= 0)
                Sim.SetBird(Sim.BirdY + (spectateY - Sim.BirdY) * Math.Min(1f, dt * 12f), 0);
            if (Spectating && Sim.Screen == "dead" && now - diedAt > 4) StopSpectating();
        }

        void ArcadeDied(int score)
        {
            RunEnded?.Invoke(score);
            if (InRace) RaceDied?.Invoke(score);
            if (ArcadeMode && Qualifies(score))
            {
                entering = true;
                enteringScore = score;
                initials = (Save.LastInitials ?? "AAA").ToCharArray();
                if (initials.Length != 3) initials = "AAA".ToCharArray();
                initialPos = 0;
                Changed();
            }
        }

        // ------------------------------------------------------------------ initials

        bool entering;
        int enteringScore, initialPos;
        char[] initials = "AAA".ToCharArray();
        const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 ";

        public bool EnteringInitials => entering;

        void CycleLetter(int d)
        {
            int i = Letters.IndexOf(initials[initialPos]);
            if (i < 0) i = 0;
            initials[initialPos] = Letters[(i + d + Letters.Length) % Letters.Length];
            Sfx("pop", 0.4f);
            Changed();
        }

        void ConfirmLetter(bool all)
        {
            if (!all && initialPos < 2) { initialPos++; Sfx("select", 0.6f); Changed(); return; }
            string name = new string(initials);
            entering = false;
            Save.LastInitials = name; Save.Dirty = true;
            AddHighScore(name, enteringScore);
            HighScoreEntered?.Invoke(name, enteringScore);
            Sfx("rank", 0.8f);
            Changed();
        }

        // ------------------------------------------------------------------ input

        /// <summary>Arcade overlays take input first. True = handled.</summary>
        bool ArcadeInput(string what)
        {
            lastInputAt = now;
            if (!ArcadeMode) return false;
            if (entering)
            {
                if (what == "flap" || what == "select" || what == "pointer") ConfirmLetter(false);
                else if (what == "start") ConfirmLetter(true);
                else if (what == "back") { if (initialPos > 0) { initialPos--; Changed(); } }
                return true;
            }
            if (Attract) { Changed(); return true; }          // the first touch only wakes the screen
            if (CountingDown) return true;
            if (Spectating)
            {
                if (what == "back") StopSpectating();
                return true;
            }
            if (RemoteName != null) { if (what != "back") Sfx("error", 0.6f); return true; }
            if (RaceStatus != null && !InRace && Sim.Screen == "menu" && modal == null)
            {
                if (what == "back") { ClearLobby(); RaceLeft?.Invoke(); Sfx("woosh", 0.4f); }
                return true;
            }
            if (InRace && Sim.Screen == "dead")
            {
                // the run is over but the others are still flying: wait for the result, or BACK to leave
                if (what == "back") { EndRace(null); RaceLeft?.Invoke(); ToMenu(); }
                return true;
            }
            if (InRace && Sim.Screen == "paused") { Sim.Resume(); return true; }
            if (InRace && what == "back" && Sim.Screen == "playing") return true;     // no pausing a race
            return false;
        }

        bool ArcadeNavigate(int dx, int dy)
        {
            lastInputAt = now;
            if (!ArcadeMode) return false;
            if (entering)
            {
                if (dy != 0) CycleLetter(dy > 0 ? -1 : 1);
                if (dx != 0) { initialPos = Math.Max(0, Math.Min(2, initialPos + dx)); Sfx("pop", 0.3f); Changed(); }
                return true;
            }
            if (Attract) { Changed(); return true; }
            return CountingDown || Spectating || RemoteName != null;
        }

        // ------------------------------------------------------------------ drawing

        string ArcadeRightKey() =>
            (InRace ? "R" : "") + (RaceStatus ?? "") + "|" + Ghosts.Count + "|" + (RaceResult != null ? RaceResult.Count : -1) + "|" +
            string.Join(",", RaceLobbyNames) + "|" + Credits + "|" + FreePlay + "|" + HighScores.Count + "|" + RaceScoreKey();

        string RaceScoreKey()
        {
            if (!InRace) return "";
            var b = new System.Text.StringBuilder();
            foreach (var g in Ghosts.Values) b.Append(g.Score).Append(g.Alive ? 'a' : 'd');
            return b.ToString();
        }

        void DrawGhosts()
        {
            if (Ghosts.Count == 0) return;
            var body = Art.Get("bird");
            foreach (var g in Ghosts.Values)
            {
                float a = g.Alive ? 0.65f : 0.2f;
                if (body != null) C.Sprite(body, NativeSim.BirdX, g.ShownY, NativeSim.BirdSize, NativeSim.BirdSize, 0, a);
                else C.Circle(NativeSim.BirdX, g.ShownY, NativeSim.BirdSize / 2, g.Colour, a);
                C.Circle(NativeSim.BirdX - 14, g.ShownY - 14, 4, g.Colour, g.Alive ? 0.95f : 0.4f);
                string label = g.Name ?? "?";
                float w = C.Measure(label, 13) + 12;
                C.RoundRect(NativeSim.BirdX - w / 2, g.ShownY - 40, w, 18, 9, Ink, 0.55f * (g.Alive ? 1 : 0.5f));
                C.Text(label, NativeSim.BirdX, g.ShownY - 39, 13, g.Colour, g.Alive ? 1 : 0.5f, Align.Centre);
            }
        }

        void RaceCard(float x, float y, float w)
        {
            Card(x, y, w, 236);
            Icon("1f3c1", x + 28, y + 22, 24);
            C.Text(InRace ? "RACE" : "RACE LOBBY", x + 48, y + 10, 16, Accent);
            var rows = new List<KeyValuePair<string, KeyValuePair<int, bool>>>();
            if (InRace)
            {
                rows.Add(new KeyValuePair<string, KeyValuePair<int, bool>>("YOU", new KeyValuePair<int, bool>(Sim.Score, Sim.Screen == "playing")));
                foreach (var g in Ghosts.Values) rows.Add(new KeyValuePair<string, KeyValuePair<int, bool>>(g.Name, new KeyValuePair<int, bool>(g.Score, g.Alive)));
                rows.Sort((p, q) => q.Value.Key != p.Value.Key ? q.Value.Key.CompareTo(p.Value.Key) : q.Value.Value.CompareTo(p.Value.Value));
            }
            else foreach (var n in RaceLobbyNames) rows.Add(new KeyValuePair<string, KeyValuePair<int, bool>>(n, new KeyValuePair<int, bool>(-1, true)));
            float ry = y + 40;
            for (int i = 0; i < rows.Count && i < 6; i++)
            {
                var r = rows[i];
                C.RoundRect(x + 14, ry, w - 28, 26, 8, Panel2, r.Value.Value ? 1 : 0.5f);
                C.Text((i + 1) + ".", x + 22, ry + 4, 13, Muted);
                C.Text(r.Key ?? "?", x + 46, ry + 4, 16, r.Value.Value ? White : Dim);
                if (r.Value.Key >= 0) C.Text(r.Value.Key.ToString(CultureInfo.InvariantCulture), x + w - 24, ry + 4, 16, r.Value.Value ? Gold : Dim, 1, Align.Right);
                ry += 30;
            }
            if (!InRace && RaceStatus != null) C.TextWrapped(RaceStatus, x + 16, y + 210 - 16, w - 32, 13, Muted);
        }

        void ArcadeOverlay()
        {
            if (!ArcadeMode) return;
            if (Attract) { AttractScreen(); return; }
            if (RemoteName != null && !Spectating) { InUseCard(); return; }
            if (Spectating) WatchingBanner();
            if (CountingDown) { CountdownOverlay(); return; }
            if (RaceStatus != null && !InRace && Sim.Screen == "menu") LobbyCard();
            if (entering) InitialsCard();
            else if (RaceResult != null && Sim.Screen == "dead" && !InRace && now - diedAt > 0.25) RaceResultBadge();
            if (!FreePlay && Sim.Screen == "menu") CreditsLine();
        }

        void CreditsLine()
        {
            string s = Credits > 0 ? "CREDITS " + Credits : "INSERT COIN";
            float blink = Credits > 0 || Save.ReducedMotion ? 1 : (0.55f + 0.45f * (float)Math.Abs(Math.Sin(now * 3)));
            C.Text(s, PX + PW / 2f, PY + PH - 34, 16, Gold, blink, Align.Centre, true);
        }

        void AttractScreen()
        {
            C.Rect(PX, PY, PW, PH, Ink, 0.9f);
            float cx = PX + PW / 2f;
            double t = now - lastInputAt - AttractAfter;
            float bob = Save.ReducedMotion ? 0 : (float)Math.Sin(now * 2.4) * 8;
            DrawBird(cx, PY + 120 + bob, 96, (float)Math.Sin(now * 1.7) * 0.08f, Catalog.CosmeticById(Save.Hat));
            C.Text("FLAPPY", cx, PY + 186, 48, White, 1, Align.Centre, true);
            C.Text("CRIX", cx, PY + 236, 48, Accent, 1, Align.Centre, true);
            bool showScores = HighScores.Count > 0 && ((int)(t / 6)) % 2 == 1;
            if (showScores)
            {
                C.Rect(PX + 30, PY + 300, PW - 60, 250, Ink, 0.6f);
                C.Text("HIGH SCORES", cx, PY + 308, 20, Gold, 1, Align.Centre, true);
                for (int i = 0; i < HighScores.Count && i < 7; i++)
                {
                    float ry = PY + 342 + i * 28;
                    uint col = i == 0 ? Gold : i == 1 ? White : i == 2 ? Col.Hex("#FF9F43") : Muted;
                    C.Text((i + 1) + ".", PX + 60, ry, 16, col);
                    C.Text(HighScores[i].Name, PX + 110, ry, 20, col, 1, Align.Left, true);
                    C.Text(HighScores[i].Score.ToString(CultureInfo.InvariantCulture), PX + PW - 60, ry, 20, col, 1, Align.Right, true);
                }
            }
            else
            {
                C.Text("tap  -  collect  -  drip", cx, PY + 300, 16, Gold, 1, Align.Centre, true);
                if (HighScores.Count > 0)
                    C.Text("TOP SCORE  " + HighScores[0].Name + "  " + HighScores[0].Score, cx, PY + 340, 20, White, 1, Align.Centre, true);
            }
            float blink = Save.ReducedMotion ? 1 : (float)(0.35 + 0.65 * Math.Abs(Math.Sin(now * 2.6)));
            C.Text(NeedsCoin ? "INSERT COIN" : "PRESS START", cx, PY + PH - 70, 34, White, blink, Align.Centre, true);
            C.Text(FreePlay ? "FREE PLAY" : "CREDITS " + Credits, cx, PY + PH - 30, 16, Gold, 1, Align.Centre);
        }

        void InUseCard()
        {
            C.Rect(PX, PY, PW, PH, Ink, 0.93f);
            float cx = PX + PW / 2f;
            C.Text("IN USE", cx, PY + 150, 48, Accent, 1, Align.Centre, true);
            C.Text(RemoteName, cx, PY + 210, 34, White, 1, Align.Centre, true);
            string state = RemoteScreen == "playing" ? "is playing" : RemoteScreen == "dead" ? "crashed" : "is on the menu";
            C.Text(state, cx, PY + 256, 16, Muted, 1, Align.Centre);
            C.Text(RemoteScore.ToString(CultureInfo.InvariantCulture), cx, PY + 300, 48, Gold, 1, Align.Centre, true);
            C.TextWrapped("Wait for them to finish, or use another cabinet.", PX + 40, PY + 400, PW - 80, 16, Muted, 1, Align.Centre);
        }

        void WatchingBanner()
        {
            C.RoundRect(PX + 20, PY + PH - 54, PW - 40, 36, 18, Ink, 0.7f);
            C.Text("WATCHING  " + (SpectateName ?? "?"), PX + PW / 2f, PY + PH - 47, 16, Gold, 1, Align.Centre, true);
        }

        void CountdownOverlay()
        {
            double left = countdownEnd - now;
            int n = (int)Math.Ceiling(left);
            float frac = (float)(left - Math.Floor(left));
            float size = Save.ReducedMotion ? 1 : 0.8f + frac * 0.4f;
            C.Rect(PX, PY, PW, PH, Ink, 0.85f);
            float cx = PX + PW / 2f;
            C.Text(countdownForSpectate ? "WATCHING " + SpectateName : "GET READY", cx, PY + 160, 26, Gold, 1, Align.Centre, true);
            float r = 58 * size;
            C.Circle(cx, PY + 280, r + 6, Accent, 0.35f);
            C.Circle(cx, PY + 280, r, Accent);
            C.Text(n.ToString(CultureInfo.InvariantCulture), cx, PY + 254, 48, White, 1, Align.Centre, true);
            if (RaceLobbyNames.Length > 0)
                C.TextWrapped(string.Join("  vs  ", RaceLobbyNames), PX + 30, PY + 380, PW - 60, 16, Muted, 1, Align.Centre);
        }

        void LobbyCard()
        {
            C.Rect(PX, PY, PW, PH, Ink, 0.88f);
            float x = PX + 30, w = PW - 60, y = PY + 140;
            Card(x, y, w, 300, true);
            Icon("1f3c1", x + w / 2, y + 36, 40);
            C.Text("RACE LOBBY", x + w / 2, y + 62, 26, White, 1, Align.Centre, true);
            float ry = y + 104;
            foreach (var n in RaceLobbyNames)
            {
                C.Text(n, x + w / 2, ry, 20, White, 1, Align.Centre);
                ry += 28;
                if (ry > y + 220) break;
            }
            C.TextWrapped(RaceStatus ?? "", x + 20, y + 230, w - 40, 13, Muted, 1, Align.Centre);
            C.Text("BACK to leave", x + w / 2, y + 270, 13, Dim, 1, Align.Centre);
        }

        void InitialsCard()
        {
            C.Rect(PX, PY, PW, PH, Ink, 0.85f);
            float x = PX + 30, w = PW - 60, y = PY + 130;
            Card(x, y, w, 320, true);
            C.Text("NEW HIGH SCORE!", x + w / 2, y + 18, 26, Gold, 1, Align.Centre, true);
            C.Text(enteringScore.ToString(CultureInfo.InvariantCulture), x + w / 2, y + 56, 48, White, 1, Align.Centre, true);
            C.Text("ENTER YOUR INITIALS", x + w / 2, y + 118, 16, Muted, 1, Align.Centre);
            for (int i = 0; i < 3; i++)
            {
                float bx = x + w / 2 - 105 + i * 75;
                bool on = i == initialPos;
                C.RoundRect(bx, y + 146, 60, 76, 12, on ? Accent : Panel2);
                if (on && !Save.ReducedMotion && Math.Sin(now * 8) > 0) C.RoundRectStroke(bx - 4, y + 142, 68, 84, 14, 3, White);
                C.Text(initials[i] == ' ' ? "_" : initials[i].ToString(), bx + 30, y + 160, 48, White, 1, Align.Centre, true);
            }
            C.TextWrapped("JOYSTICK up/down: letter  -  FLAP: next  -  START: done", x + 16, y + 250, w - 32, 13, Dim, 1, Align.Centre);
        }

        void RaceResultBadge()
        {
            float x = PX + 40, w = PW - 80, y = PY + 20;
            C.RoundRect(x, y, w, 30 + RaceResult.Count * 22, 12, Ink, 0.75f);
            C.Text("RACE RESULT", x + w / 2, y + 6, 16, Gold, 1, Align.Centre);
            for (int i = 0; i < RaceResult.Count && i < 4; i++)
                C.Text((i + 1) + ". " + RaceResult[i].Key + "  " + RaceResult[i].Value, x + w / 2, y + 28 + i * 22, 13, i == 0 ? Gold : White, 1, Align.Centre);
        }
    }
}
