// Multiplayer for the arcade, on top of any IArcadeTransport (the room the game is already in).
//
//   presence  1 per second from each player: who they are, where they stand, which cabinet they
//             are using, whether they wait in a race lobby or are racing
//   races     players at cabinets of the same link group who press RACE wait in a lobby; once two
//             or more are in, the lowest id ("leader") sends one countdown with a shared seed, so
//             every racer gets the same pipes and coins. Positions go out 8 times a second, only
//             to the racers and the players standing near the cabinets (ghost birds, watching)
//   watching  pressing RACE while a race is on nearby shows one racer's run on your cabinet
//   in use    a cabinet someone else is playing shows their name and live score
//   scores    a new high score goes to everyone, so each cabinet's table is the same for all
//
// Nothing here opens a connection or joins a room, so it costs no Photon CCU: only a few small
// messages (about 1 a second per player, 8 a second per racer while a race is on).
// No UnityEngine references (tested outside Unity).
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CrixArcade.Core;

namespace CrixArcade.Net
{
    public sealed class ArcadeNetwork
    {
        const string Tag = "FCA1";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ settings

        /// <summary>Players further than this (metres) from a cabinet don't count as nearby.</summary>
        public float NearbyRadius = 12f;
        public double PresenceEvery = 1.0;
        public double BirdEvery = 1.0 / 8;
        /// <summary>Seconds the lobby waits after the last player joined before the countdown.</summary>
        public double LobbyWait = 4;
        public double CountdownSeconds = 3.5;
        /// <summary>A player not heard from for this long is gone.</summary>
        public double StaleAfter = 3.5;
        /// <summary>A cabinet stays yours this long after your last press while it's on the menu.</summary>
        public double ClaimFor = 25;

        public IArcadeTransport Transport { get; private set; }
        public Func<ArcadeVec> LocalPosition = () => default(ArcadeVec);
        public event Action<string> Log;

        // ------------------------------------------------------------------ state

        sealed class Remote
        {
            public string Id, Name = "?";
            public ArcadeVec Pos;
            public double Seen;
            public string Cab = "", CabScreen = "menu", Lobby = "", RaceGroup = "";
            public int CabScore, RaceSeed;
            public double ClaimStart, RaceStart;
            // the newest bird position (races)
            public float BirdY = 300; public bool BirdAlive = true; public int BirdScore; public double BirdAt = -1;
            public int BirdSeed;
        }

        sealed class Race
        {
            public string Group;
            public ArcadeSeat Seat;
            public int Seed;
            public double Start;
            public readonly Dictionary<string, string> Racers = new Dictionary<string, string>();   // id -> name (not us)
            public readonly Dictionary<string, int> Finals = new Dictionary<string, int>();
            public bool LocalDone; public int LocalScore;
            public double LastBird;
        }

        readonly List<ArcadeSeat> seats = new List<ArcadeSeat>();
        readonly Dictionary<string, Remote> remotes = new Dictionary<string, Remote>();
        readonly List<ArcadeSeat.Event> evBuf = new List<ArcadeSeat.Event>();
        readonly Random rng = new Random();
        double now, lastPresence = -99;
        bool presenceDirty;

        ArcadeSeat lobbySeat; string lobbyGroup; double lobbyChangedAt; string lobbyKey = "";
        Race race;
        ArcadeSeat watchSeat; string watchId; int watchSeed;

        public ArcadeNetwork(IArcadeTransport transport)
        {
            Transport = transport ?? new OfflineTransport();
            Transport.Received += OnReceived;
            Transport.PlayerLeft += id => remotes.Remove(id);
        }

        public void Add(ArcadeSeat s) { if (!seats.Contains(s)) seats.Add(s); }
        public void Remove(ArcadeSeat s)
        {
            seats.Remove(s);
            if (lobbySeat == s) LeaveLobby(false);
            if (race != null && race.Seat == s) race = null;
            if (watchSeat == s) watchSeat = null;
        }

        public bool Online => Transport.Connected;
        public string LocalName => Clean(Transport.LocalName, 12);

        // ------------------------------------------------------------------ every frame

        public void Update(double time)
        {
            now = time;
            foreach (var s in seats.ToArray())
            {
                s.TakeEvents(evBuf);
                foreach (var e in evBuf) Handle(s, e);
                evBuf.Clear();
            }
            Prune();
            UpdateLobby();
            UpdateRace();
            UpdateWatch();
            UpdateCabinets();
            if (Online && (presenceDirty || now - lastPresence >= PresenceEvery)) SendPresence();
        }

        void Handle(ArcadeSeat s, ArcadeSeat.Event e)
        {
            switch (e.Kind)
            {
                case ArcadeSeat.EventKind.RaceRequested:
                    s.TouchedAt = now;
                    if (!Online || string.IsNullOrEmpty(s.Group)) { s.Post(a => a.Notice("No one to race", "Racing needs other players in the room.")); break; }
                    var running = RunningRace(s);
                    if (running != null) { Watch(s, running); break; }
                    JoinLobby(s);
                    break;
                case ArcadeSeat.EventKind.RaceLeft:
                    if (lobbySeat == s) LeaveLobby(false);
                    if (race != null && race.Seat == s)
                    {
                        if (!race.LocalDone) SendFinal(race, s.State.Score);
                        race = null; presenceDirty = true;
                    }
                    break;
                case ArcadeSeat.EventKind.RaceDied:
                    if (race != null && race.Seat == s) SendFinal(race, e.Score);
                    break;
                case ArcadeSeat.EventKind.HighScore:
                    if (Online) Send(null, true, "H", s.CabinetId, Clean(e.Name, 3), e.Score.ToString(Inv));
                    break;
                case ArcadeSeat.EventKind.RunStarted:
                case ArcadeSeat.EventKind.RunEnded:
                    s.TouchedAt = now; presenceDirty = true;
                    break;
            }
        }

        void Prune()
        {
            List<string> gone = null;
            foreach (var r in remotes.Values) if (now - r.Seen > StaleAfter * 3) (gone ?? (gone = new List<string>())).Add(r.Id);
            if (gone != null) foreach (var id in gone) remotes.Remove(id);
        }

        bool Fresh(Remote r) => now - r.Seen <= StaleAfter;
        bool Near(Remote r, ArcadeSeat s) => ArcadeVec.Distance(r.Pos, s.Position) <= NearbyRadius;

        // ------------------------------------------------------------------ lobby

        void JoinLobby(ArcadeSeat s)
        {
            if (lobbySeat != null && lobbySeat != s) lobbySeat.Post(a => a.ClearLobby());
            lobbySeat = s; lobbyGroup = s.Group; lobbyChangedAt = now; lobbyKey = "";
            presenceDirty = true;
            UpdateLobby();
        }

        void LeaveLobby(bool tellGame)
        {
            if (lobbySeat != null && tellGame) lobbySeat.Post(a => a.ClearLobby());
            lobbySeat = null; lobbyGroup = null; presenceDirty = true;
        }

        List<Remote> LobbyMembers()
        {
            var list = new List<Remote>();
            if (lobbySeat == null) return list;
            foreach (var r in remotes.Values)
                if (Fresh(r) && r.Lobby == lobbyGroup && Near(r, lobbySeat)) list.Add(r);
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        void UpdateLobby()
        {
            if (lobbySeat == null) return;
            if (!Online) { LeaveLobby(true); return; }
            var members = LobbyMembers();
            var names = new List<string> { LocalName };
            foreach (var m in members) names.Add(m.Name);
            string key = string.Join(",", names.ToArray());
            if (key != lobbyKey) { lobbyKey = key; lobbyChangedAt = now; }
            bool leader = members.Count == 0 || string.CompareOrdinal(Transport.LocalId, members[0].Id) < 0;
            double left = LobbyWait - (now - lobbyChangedAt);
            string status = members.Count == 0
                ? "Waiting for someone nearby to press RACE on another cabinet..."
                : left > 0 ? "Starting in " + Math.Ceiling(left).ToString(Inv) + "..." : "Starting...";
            string show = status + "|" + key;
            if (show != lobbySeat.shownLobby)
            {
                lobbySeat.shownLobby = show;
                var arr = names.ToArray(); int others = members.Count;
                lobbySeat.Post(a => a.SetLobby(status, arr, others));
            }
            if (members.Count > 0 && leader && left <= 0)
            {
                int seed = rng.Next(1, int.MaxValue);
                var ids = new List<string>(); var ns = new List<string>();
                foreach (var m in members) { ids.Add(m.Id); ns.Add(m.Name); }
                ids.Add(Transport.LocalId); ns.Add(LocalName);
                Send(new List<string>(ids.GetRange(0, ids.Count - 1)), true, "C", lobbyGroup, seed.ToString(Inv),
                     ((int)(CountdownSeconds * 1000)).ToString(Inv), string.Join(",", ids.ToArray()), string.Join(",", ns.ToArray()));
                StartRace(lobbySeat, lobbyGroup, seed, CountdownSeconds, ids, ns);
            }
        }

        void StartRace(ArcadeSeat s, string group, int seed, double delay, List<string> ids, List<string> names)
        {
            race = new Race { Group = group, Seat = s, Seed = seed, Start = now + delay };
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] != Transport.LocalId) race.Racers[ids[i]] = i < names.Count ? names[i] : "?";
            lobbySeat = null; lobbyGroup = null; s.shownLobby = null;
            var arr = names.ToArray();
            s.Post(a => { a.ClearLobby(); a.RaceLobbyNames = arr; a.BeginCountdown(seed, delay); });
            presenceDirty = true;
            Log?.Invoke("Race " + seed + " with " + string.Join(", ", arr));
        }

        // ------------------------------------------------------------------ race

        void UpdateRace()
        {
            if (race == null) return;
            var r = race;
            if (now < r.Start) return;
            var st = r.Seat.State;
            if (!r.LocalDone && now - r.LastBird >= BirdEvery && Online)
            {
                r.LastBird = now;
                bool alive = st.InRace && st.Screen == "playing";
                Send(BirdTargets(r), false, "B", r.Group, r.Seed.ToString(Inv), st.BirdY.ToString("0.#", Inv), alive ? "1" : "0", st.Score.ToString(Inv));
            }
            // ghosts
            foreach (var kv in r.Racers)
            {
                Remote rem;
                if (!remotes.TryGetValue(kv.Key, out rem) || rem.BirdSeed != r.Seed || rem.BirdAt < 0) continue;
                string id = kv.Key, name = kv.Value; float y = rem.BirdY; bool alive = rem.BirdAlive && !r.Finals.ContainsKey(id); int sc = rem.BirdScore;
                r.Seat.Post(a => a.SetGhost(id, name, y, alive, sc));
            }
            // a racer we stopped hearing from is out with what they had
            foreach (var kv in r.Racers)
            {
                if (r.Finals.ContainsKey(kv.Key)) continue;
                Remote rem;
                bool gone = !remotes.TryGetValue(kv.Key, out rem) || !Fresh(rem) && (rem.BirdAt < 0 || now - rem.BirdAt > StaleAfter);
                if (gone && now - r.Start > StaleAfter) r.Finals[kv.Key] = rem != null ? rem.BirdScore : 0;
            }
            if (r.LocalDone && r.Finals.Count >= r.Racers.Count) FinishRace();
        }

        List<string> BirdTargets(Race r)
        {
            var t = new List<string>(r.Racers.Keys);
            foreach (var rem in remotes.Values)
                if (!r.Racers.ContainsKey(rem.Id) && Fresh(rem) && Near(rem, r.Seat)) t.Add(rem.Id);
            return t;
        }

        void SendFinal(Race r, int score)
        {
            r.LocalDone = true; r.LocalScore = score;
            if (Online) Send(new List<string>(r.Racers.Keys), true, "F", r.Group, r.Seed.ToString(Inv), score.ToString(Inv));
            // one last position so ghosts on other cabinets show we're out
            if (Online) Send(BirdTargets(r), false, "B", r.Group, r.Seed.ToString(Inv), "590", "0", score.ToString(Inv));
            presenceDirty = true;
        }

        void FinishRace()
        {
            var r = race; race = null;
            var result = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>(LocalName, r.LocalScore) };
            foreach (var kv in r.Finals) result.Add(new KeyValuePair<string, int>(r.Racers.ContainsKey(kv.Key) ? r.Racers[kv.Key] : "?", kv.Value));
            result.Sort((a, b) => b.Value.CompareTo(a.Value));
            r.Seat.Post(a => a.EndRace(result));
            presenceDirty = true;
            var sb = new StringBuilder("Race over: ");
            foreach (var p in result) sb.Append(p.Key).Append(' ').Append(p.Value).Append("  ");
            Log?.Invoke(sb.ToString());
        }

        // ------------------------------------------------------------------ watching

        Remote RunningRace(ArcadeSeat s)
        {
            Remote best = null;
            foreach (var r in remotes.Values)
                if (Fresh(r) && r.RaceGroup == s.Group && r.RaceGroup != "" && Near(r, s) && now >= r.RaceStart &&
                    (best == null || (r.BirdAlive && !best.BirdAlive) || (r.BirdAlive == best.BirdAlive && r.BirdScore > best.BirdScore)))
                    best = r;
            return best;
        }

        void Watch(ArcadeSeat s, Remote r)
        {
            watchSeat = s; watchId = r.Id; watchSeed = r.RaceSeed;
            int seed = r.RaceSeed; string name = r.Name; double ago = Math.Max(0, now - r.RaceStart);
            s.Post(a => a.Spectate(seed, name, ago));
            Log?.Invoke("Watching " + name);
        }

        void UpdateWatch()
        {
            if (watchSeat == null) return;
            Remote r;
            var st = watchSeat.State;
            if (!remotes.TryGetValue(watchId, out r) || (!Fresh(r) && now - r.BirdAt > StaleAfter))
            {
                watchSeat.Post(a => a.StopSpectating()); watchSeat = null; return;
            }
            if (r.BirdSeed == watchSeed && r.BirdAt >= 0)
            {
                float y = r.BirdY; bool alive = r.BirdAlive;
                watchSeat.Post(a => a.SetSpectatedBird(y, alive));
                if (!alive) watchSeat = null;          // the game shows the crash and goes back by itself
            }
        }

        // ------------------------------------------------------------------ cabinets in use

        ArcadeSeat LocalClaim()
        {
            ArcadeSeat best = null;
            foreach (var s in seats)
            {
                var st = s.State;
                bool busy = st.Screen != "menu" || st.InLobby || st.CountingDown || st.EnteringInitials;
                if (!busy && now - s.TouchedAt > ClaimFor) continue;
                if (st.Spectating) continue;
                if (best == null || s.TouchedAt > best.TouchedAt) best = s;
            }
            return best;
        }

        void UpdateCabinets()
        {
            var mine = LocalClaim();
            foreach (var s in seats)
            {
                Remote user = null;
                foreach (var r in remotes.Values)
                {
                    if (!Fresh(r) || r.Cab != s.CabinetId) continue;
                    if (s == mine)
                    {
                        // both think it's theirs: the one who started first keeps it
                        double myStart = s.TouchedAt;
                        bool theirs = r.ClaimStart < myStart - 0.25 || (Math.Abs(r.ClaimStart - myStart) <= 0.25 && string.CompareOrdinal(r.Id, Transport.LocalId) < 0);
                        if (!theirs) continue;
                    }
                    user = r; break;
                }
                string name = user?.Name; int score = user?.CabScore ?? 0; string screen = user?.CabScreen;
                if (name != s.shownRemote || score != s.shownRemoteScore || screen != s.shownRemoteScreen)
                {
                    s.shownRemote = name; s.shownRemoteScore = score; s.shownRemoteScreen = screen;
                    s.Post(a => a.SetRemote(name, score, screen));
                }
                bool avail = Online && !string.IsNullOrEmpty(s.Group);
                int nearby = 0;
                if (avail) foreach (var r in remotes.Values) if (Fresh(r) && r.Lobby == s.Group && Near(r, s)) nearby++;
                if (avail != s.shownRaceAvailable || nearby != s.shownNearby)
                {
                    s.shownRaceAvailable = avail; s.shownNearby = nearby;
                    s.Post(a => { a.RaceAvailable = avail; a.NearbyRacers = nearby; });
                }
            }
        }

        // ------------------------------------------------------------------ messages

        void SendPresence()
        {
            lastPresence = now; presenceDirty = false;
            var p = LocalPosition();
            var mine = LocalClaim();
            var st = mine != null ? mine.State : default(SeatState);
            bool racing = race != null;
            Send(null, false, "P", LocalName,
                 p.X.ToString("0.##", Inv), p.Y.ToString("0.##", Inv), p.Z.ToString("0.##", Inv),
                 mine != null ? mine.CabinetId : "", st.Screen ?? "menu", st.Score.ToString(Inv),
                 mine != null ? Math.Max(0, now - mine.TouchedAt).ToString("0.##", Inv) : "0",
                 lobbyGroup ?? "", racing ? race.Group : "", racing ? race.Seed.ToString(Inv) : "0",
                 racing ? (now - race.Start).ToString("0.##", Inv) : "0");
        }

        void Send(IList<string> targets, bool reliable, string kind, params string[] fields)
        {
            if (targets != null && targets.Count == 0) return;
            var b = new StringBuilder(Tag).Append('|').Append(kind);
            foreach (var f in fields) b.Append('|').Append((f ?? "").Replace("|", "/"));
            try { Transport.Send(Encoding.UTF8.GetBytes(b.ToString()), targets, reliable); }
            catch (Exception e) { Log?.Invoke("Send failed: " + e.Message); }
        }

        void OnReceived(string from, byte[] data)
        {
            if (data == null || data.Length < 6 || data.Length > 1024 || from == Transport.LocalId) return;
            string[] f;
            try { f = Encoding.UTF8.GetString(data).Split('|'); } catch { return; }
            if (f.Length < 2 || f[0] != Tag) return;
            Remote r;
            if (!remotes.TryGetValue(from, out r)) remotes[from] = r = new Remote { Id = from, Seen = double.NegativeInfinity };
            try
            {
                switch (f[1])
                {
                    case "P":
                        if (f.Length < 14) return;
                        r.Seen = now;
                        r.Name = Clean(f[2], 12);
                        r.Pos = new ArcadeVec(F(f[3]), F(f[4]), F(f[5]));
                        string cab = f[6];
                        double claimAge = D(f[9]);
                        if (cab != r.Cab || cab == "") r.ClaimStart = now - claimAge;
                        else r.ClaimStart = Math.Min(r.ClaimStart, now - claimAge);
                        r.Cab = cab; r.CabScreen = f[7]; r.CabScore = I(f[8]);
                        r.Lobby = f[10]; r.RaceGroup = f[11]; r.RaceSeed = I(f[12]);
                        if (r.RaceGroup != "") r.RaceStart = now - D(f[13]);
                        break;
                    case "C":
                        if (f.Length < 7 || lobbySeat == null || f[2] != lobbyGroup) return;
                        var ids = new List<string>(f[5].Split(','));
                        if (!ids.Contains(Transport.LocalId) || race != null) return;
                        var names = new List<string>(); foreach (var n in f[6].Split(',')) names.Add(Clean(n, 12));
                        StartRace(lobbySeat, f[2], I(f[3]), Math.Max(0.5, I(f[4]) / 1000.0), ids, names);
                        break;
                    case "B":
                        if (f.Length < 7) return;
                        r.BirdSeed = I(f[3]); r.BirdY = F(f[4]); r.BirdAlive = f[5] == "1"; r.BirdScore = I(f[6]); r.BirdAt = now;
                        if (r.Seen < now - StaleAfter) r.Seen = now - StaleAfter / 2;
                        break;
                    case "F":
                        if (f.Length < 5 || race == null || I(f[3]) != race.Seed || !race.Racers.ContainsKey(from)) return;
                        race.Finals[from] = I(f[4]);
                        break;
                    case "H":
                        if (f.Length < 5) return;
                        string initials = Clean(f[3], 3); int score = I(f[4]);
                        if (score <= 0 || score > 100000) return;
                        foreach (var s in seats)
                            if (s.CabinetId == f[2]) { var seat = s; seat.Post(a => { a.AddHighScore(initials, score); seat.ScoresChanged?.Invoke(a); }); }
                        break;
                }
            }
            catch (Exception e) { Log?.Invoke("Bad message from " + from + ": " + e.Message); }
        }

        static float F(string s) { float v; return float.TryParse(s, NumberStyles.Float, Inv, out v) && !float.IsNaN(v) && !float.IsInfinity(v) ? v : 0; }
        static double D(string s) { double v; return double.TryParse(s, NumberStyles.Float, Inv, out v) && !double.IsNaN(v) && !double.IsInfinity(v) ? Math.Max(0, Math.Min(v, 3600)) : 0; }
        static int I(string s) { int v; return int.TryParse(s, NumberStyles.Integer, Inv, out v) ? v : 0; }

        public static string Clean(string s, int max)
        {
            var b = new StringBuilder();
            foreach (char c in s ?? "")
            {
                if (b.Length >= max) break;
                if (c >= 32 && c < 127 && c != '|' && c != ',') b.Append(char.ToUpperInvariant(c));
            }
            return b.Length == 0 ? "PLAYER" : b.ToString();
        }
    }
}
