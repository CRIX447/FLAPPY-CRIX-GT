// Multiplayer with the website's players: the site's rooms (crix_XXXX, the code is the last 4
// letters), its custom events and payloads (flappycrix.html MP_EV), and its modes - Freeplay,
// Race, Last One Standing, Coin Rush - built on PhotonClient. Ranked (competitive) and the
// seasonal room modes stay on the website; voice chat isn't possible here and nothing is typed,
// but other players' chat is shown (through the site's own filter).
// Runs on the game's thread (FlappyApp calls Update); PhotonClient does the networking.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Globalization;
using FlappyCrix.Native;
using FlappyCrix.Web.Cdp;

namespace FlappyCrix.Online
{
    public sealed class Multiplayer : IDisposable
    {
        // the site's event codes (MP_EV)
        public const int EvPosition = 1, EvDeath = 3, EvScore = 4, EvChat = 5, EvTyping = 6, EvKick = 7, EvMatchStart = 9, EvMatchEnd = 10,
                         EvLobbyChat = 11, EvModeChange = 12, EvColour = 13, EvNameUpdate = 14, EvHostEndGame = 17, EvHostChanged = 23,
                         EvRespawn = 24, EvCoinTake = 25, EvPing = 26;
        public static readonly string[] Colours = { "#FF6B35", "#FF4655", "#00CC7A", "#4285F4", "#FFD700", "#FF69B4", "#9B59B6", "#E67E22", "#1ABC9C", "#E74C3C", "#3498DB", "#2ECC71" };
        public static readonly string[] Modes = { "freemode", "race", "lastone", "coinrush" };
        public static readonly HashSet<string> SiteOnlyModes = new HashSet<string> { "candyhunt", "presents", "egghunt" };
        static readonly int[] Stagger = { -16, 16, -32, 32, -48, 48, -64, 64, -80, 80, -96 };
        const float TickMs = 1000f / 60f;

        public enum Phase { Off, Connecting, Lobby, Joining, Room, Playing, Results }

        public sealed class Player
        {
            public int Actor;
            public string Name = "Player", Colour = "#4285F4", Platform, Hat, Trail;
            public int Level = 1, Score, Coins, Seat;
            public bool Dead, Me;
            public List<string> Roles = new List<string>();
            // their bird, simulated between position messages (the site's mpRemoteStep)
            public float SimY = 300, SimVel, DrawY = 300, Travelled, TravTarget;
            public double LastPos = -99;
        }

        public sealed class Result { public int Actor; public string Name; public int Score; }
        public sealed class ChatLine { public string Name, Text, Colour; public double At; }

        public readonly PhotonClient Client;
        public readonly NativeSim Sim;
        public Phase State { get; private set; } = Phase.Off;
        public string Error;
        public readonly Dictionary<int, Player> Players = new Dictionary<int, Player>();
        public readonly List<ChatLine> Chat = new List<ChatLine>();
        public List<Result> Results;
        public int Winner;                     // 0 = none
        public string RoomCode, RoomTitle, Mode = "freemode";
        public bool MatchActive, Spectating, MatchInProgressOnJoin;
        public int RaceTarget = 100, RushSeconds = 300;
        public double MatchClock;              // seconds since the match started
        public int PingMs;
        public string Nickname = "Player";
        public Func<Dictionary<string, object>> Identity;   // the NAME_UPDATE payload (name, level, hat, trail...)

        /// <summary>Messages for the player: title, text.</summary>
        public event Action<string, string> Notice;
        public event Action<string, float> Sound;
        /// <summary>A match result paid out: coins (Coin Rush doubles your coins), xp (10 for a win).</summary>
        public event Action<int, int> Reward;

        private readonly ChatFilter filter;
        private uint seed;
        private double now, lastPosSent = -1, lastPing, deathAt = -1, matchStartedAt = -99, lastHostCheck;
        private bool forcePos, respawnPending, restartPending, endSent;
        private float tickAcc;
        private readonly Random rng = new Random();

        public Multiplayer(PhotonClient client, NativeSim sim)
        {
            Client = client; Sim = sim;
            filter = ChatFilter.Embedded();
            Sim.CoinTaken += id => { if (MatchActive) Send(EvCoinTake, Args("id", id), false); };
        }

        public bool IsHost => Client.Status == PhotonClient.State.InRoom && Client.Host == Client.MyActor;
        public int Me => Client.MyActor;
        public bool InRoom => State == Phase.Room || State == Phase.Playing || State == Phase.Results;

        // ------------------------------------------------------------------ actions

        public void Connect()
        {
            Error = null;
            Client.Nickname = Nickname;
            State = Phase.Connecting;
            Client.Connect();
        }

        public void Disconnect()
        {
            Client.Disconnect();
            ClearRoom();
            State = Phase.Off;
        }

        public void CreateRoom(string mode, int maxPlayers, bool isPublic, string title)
        {
            if (Client.Status != PhotonClient.State.InLobby) return;
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var code = new char[4];
            for (int i = 0; i < 4; i++) code[i] = alphabet[rng.Next(alphabet.Length)];
            string c = new string(code);
            var props = new Dictionary<string, object>
            {
                { "254", isPublic }, { "253", true }, { "255", maxPlayers },
                { "250", new List<object> { "roomName", "mode", "isPublic", "queue", "hostRank" } },
                { "roomName", title }, { "mode", mode }, { "isPublic", isPublic }, { "privacy", isPublic ? "public" : "invite" },
                { "queue", "casual" }, { "hostRank", null }, { "raceTarget", 100 }, { "rushTime", 300 }, { "allowSpectate", true }, { "code", c },
            };
            State = Phase.Joining;
            Client.CreateRoom("crix_" + c, props);
        }

        public void JoinByCode(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.StartsWith("CRIX_", StringComparison.Ordinal)) code = code.Substring(5);
            if (code.Length != 4) { Notice?.Invoke("Room codes have 4 letters", "Check the code and try again."); return; }
            JoinRoom("crix_" + code);
        }

        public void JoinRoom(string name)
        {
            if (Client.Status != PhotonClient.State.InLobby) return;
            State = Phase.Joining;
            Client.JoinRoom(name);
        }

        public void Leave()
        {
            if (MatchActive) Sim.EndMatch();
            Client.LeaveRoom();
            ClearRoom();
            State = Client.Status == PhotonClient.State.Error ? Phase.Off : Phase.Joining;   // back to the lobby when the server says so
        }

        /// <summary>The website's room-list rules: public, open, casual; ranked and seasonal rooms stay on the website.</summary>
        public List<PhotonClient.RoomInfo> ListRooms()
        {
            var list = new List<PhotonClient.RoomInfo>();
            foreach (var r in Client.RoomList())
            {
                if (!r.Bool("isPublic", true) || !r.IsOpen) continue;
                if ((r.Str("queue") ?? "casual") != "casual") continue;
                list.Add(r);
            }
            list.Sort((a, b) => b.PlayerCount.CompareTo(a.PlayerCount));
            return list;
        }

        public static bool CanPlay(PhotonClient.RoomInfo r) => !SiteOnlyModes.Contains(r.Str("mode") ?? "freemode") && r.PlayerCount < Math.Max(1, r.MaxPlayers);

        /// <summary>Host: start a match for everyone (the site's mpStartMatch).</summary>
        public void StartMatch()
        {
            if (!IsHost || MatchActive) return;
            uint s = (uint)rng.Next() ^ ((uint)rng.Next() << 16);
            if (s == 0) s = 12345;
            Send(EvMatchStart, Args("mode", Mode, "seed", (double)s), false);
            Client.SetRoomProps(Args("matchInProgress", true));
            BeginMatch(Mode, s);
        }

        /// <summary>Host: end a Freeplay match (the site's END button).</summary>
        public void EndMatchNow()
        {
            if (!IsHost || !MatchActive) return;
            var res = Standings();
            Send(EvHostEndGame, Args("results", ResultsJson(res)), false);
            ShowResults(res, 0, Mode);
        }

        public void SetMode(string mode)
        {
            if (!IsHost || MatchActive || Array.IndexOf(Modes, mode) < 0) return;
            Mode = mode;
            Client.SetRoomProps(Args("mode", mode));
            Send(EvModeChange, Args("mode", mode), false);
        }

        public void CloseResults() { if (State == Phase.Results) State = Phase.Room; Results = null; }

        // ---- from the game (my bird)

        public void OnFlap() { if (MatchActive) forcePos = true; }

        public void OnScored(int score)
        {
            if (!MatchActive || Mode == "coinrush") return;
            Send(EvScore, Args("score", score), false);
            if (Mode == "race" && score >= RaceTarget && !endSent)
            {
                endSent = true;
                var res = Standings();
                Send(EvMatchEnd, Args("results", ResultsJson(res), "winnerActor", Me), true);   // the echo shows the results
            }
        }

        public void OnCoin()
        {
            if (!MatchActive || Mode != "coinrush") return;
            int c = Sim.CoinsThisMatch;
            Players[Me].Coins = c; Players[Me].Score = c;
            Send(EvScore, Args("score", c, "coins", c), false);
        }

        public void OnDied(int score)
        {
            if (!MatchActive) return;
            var me = Players[Me];
            me.Dead = true;
            Send(EvDeath, Args("finalScore", Mode == "coinrush" ? Sim.CoinsThisMatch : score), false);
            forcePos = true;
            deathAt = now;
            if (Mode == "lastone") { Spectating = true; if (IsHost) CheckLastOne(); }
            else if (Mode == "race") respawnPending = true;
            else { restartPending = true; if (Mode == "coinrush") respawnPending = true; }
        }

        // ------------------------------------------------------------------ every frame

        public void Update(float dt)
        {
            now += dt;
            foreach (var e in Client.TakeEvents()) Handle(e);

            if (Client.Status == PhotonClient.State.InLobby && (State == Phase.Connecting || State == Phase.Joining)) State = Phase.Lobby;
            if (Client.Status == PhotonClient.State.Error && State != Phase.Off) { Error = Client.Error; ClearRoom(); State = Phase.Off; }
            if (!InRoom) return;

            // who's who (names can change through properties)
            lock (Client.Lock)
                foreach (var kv in Client.Actors)
                {
                    Player p;
                    if (!Players.TryGetValue(kv.Key, out p)) Players[kv.Key] = p = NewPlayer(kv.Key, kv.Value);
                    else if (!p.Me && !string.IsNullOrEmpty(kv.Value) && p.Name.StartsWith("Player", StringComparison.Ordinal)) p.Name = kv.Value;
                }

            // ping (the site measures its own echo of event 26 once a second)
            if (now - lastPing >= 1) { lastPing = now; Send(EvPing, Args("t", now * 1000.0), true); }

            if (MatchActive)
            {
                MatchClock = now - matchStartedAt;
                // crashed: back in the same world (Race, Coin Rush) or the lane from the start (Freeplay, Coin Rush)
                if (restartPending && now - deathAt >= 0.8)
                {
                    restartPending = false;
                    int coins = Sim.CoinsThisMatch;
                    Sim.StartMp(seed, Mode == "coinrush", false);
                    Sim.CoinsThisMatch = coins;
                    Players[Me].Score = Mode == "coinrush" ? coins : 0; Players[Me].Dead = false;
                }
                if (respawnPending && now - deathAt >= 1.2) { respawnPending = false; if (Sim.Screen != "playing") Sim.Respawn(); Players[Me].Dead = false; Send(EvRespawn, Args("actor", Me), false); }

                // my position: at most every 50 ms, and at once on a flap or crash
                if (!Spectating && (forcePos || now - lastPosSent >= 0.05)) SendPosition();

                // host clocks: Coin Rush ends after rushTime
                if (IsHost && !endSent && Mode == "coinrush" && MatchClock >= RushSeconds)
                {
                    endSent = true;
                    var res = Standings();
                    Send(EvMatchEnd, Args("results", ResultsJson(res), "winnerActor", res.Count > 0 && res[0].Score > 0 ? (object)res[0].Actor : null, "mode", "coinrush"), true);
                }
                if (IsHost && Mode == "lastone" && now - lastHostCheck > 0.5) { lastHostCheck = now; CheckLastOne(); }
            }

            // other birds, 60 ticks a second
            tickAcc += dt;
            int steps = 0;
            while (tickAcc >= TickMs / 1000f && steps < 5) { tickAcc -= TickMs / 1000f; steps++; foreach (var p in Players.Values) if (!p.Me) RemoteStep(p); }
            if (steps >= 5) tickAcc = 0;
            foreach (var p in Players.Values) if (!p.Me) p.DrawY += (p.SimY - p.DrawY) * Math.Min(1f, dt * 30f);
        }

        private void SendPosition()
        {
            forcePos = false;
            lastPosSent = now;
            Send(EvPosition, new Dictionary<string, object>
            {
                { "y", Math.Round(Sim.BirdY, 1) }, { "v", Math.Round(Sim.Velocity, 2) }, { "s", Mode == "coinrush" ? Sim.CoinsThisMatch : Sim.Score },
                { "t", Math.Round(Sim.Travelled) }, { "d", Sim.Screen == "dead" ? 1 : 0 },
            }, false);
        }

        private void RemoteStep(Player p)
        {
            if (p.Dead || !MatchActive) return;
            p.SimVel += p.SimVel < 0 ? NativeSim.Gravity : NativeSim.FallGrav;
            if (p.SimVel > NativeSim.MaxFall) p.SimVel = NativeSim.MaxFall;
            p.SimY = Math.Max(15, Math.Min(590, p.SimY + p.SimVel));
            p.Travelled += NativeSim.Speed;
            p.TravTarget += NativeSim.Speed;
            p.Travelled += (p.TravTarget - p.Travelled) * 0.1f;
        }

        /// <summary>Where to draw another player's bird on my screen (the site's mpVirtualX).</summary>
        public float ScreenX(Player p)
        {
            if (Mode == "coinrush") return 100 + ((p.Actor % 5) - 2) * 26;
            return 100 + (p.Travelled - Sim.Travelled) + Stagger[Math.Min(Stagger.Length - 1, p.Seat)];
        }

        public bool Visible(Player p) => !p.Me && (now - p.LastPos < 3 || p.Dead);

        // ------------------------------------------------------------------ events

        private void Handle(PhotonClient.Event e)
        {
            switch (e.Kind)
            {
                case "lobby": if (State == Phase.Connecting || State == Phase.Joining) State = Phase.Lobby; break;
                case "error":
                    if (State == Phase.Joining || State == Phase.Connecting) State = Client.Status == PhotonClient.State.InLobby ? Phase.Lobby : Phase.Off;
                    Notice?.Invoke("Multiplayer", e.Message);
                    break;
                case "joined": OnJoined((bool)e.Content); break;
                case "left": ClearRoom(); if (State != Phase.Off) State = Phase.Joining; if (e.Message != null) Notice?.Invoke("Left the room", e.Message); break;
                case "join":
                {
                    var p = Get(e.Actor); p.Name = e.Message ?? p.Name;
                    Seats();
                    SendIdentity();      // the site re-sends everyone's details to each newcomer
                    break;
                }
                case "leave":
                    Players.Remove(e.Actor);
                    Seats();
                    if (MatchActive && Mode == "lastone" && IsHost) CheckLastOne();
                    break;
                case "roomprops": ReadRoomProps(); break;
                case "custom": OnCustom(e.Code, e.Actor, MiniJson.Obj(e.Content)); break;
            }
        }

        private void OnJoined(bool created)
        {
            Players.Clear(); Chat.Clear(); Results = null;
            lock (Client.Lock) foreach (var kv in Client.Actors) Players[kv.Key] = NewPlayer(kv.Key, kv.Value);
            Get(Me).Me = true;
            Get(Me).Name = Nickname;
            ReadRoomProps();
            Seats();
            if ((Client.RoomProps.ContainsKey("queue") ? Client.RoomProps["queue"] as string : "casual") == "competitive")
            {
                Notice?.Invoke("Ranked rooms are website-only", "Pick a casual room instead.");
                Leave();
                return;
            }
            if (SiteOnlyModes.Contains(Mode))
            {
                Notice?.Invoke("That mode is website-only", "Candy Hunt, Presents and Egg Hunt are played on crixgamingvr.com.");
                Leave();
                return;
            }
            object mip;
            MatchInProgressOnJoin = !created && Client.RoomProps.TryGetValue("matchInProgress", out mip) && mip is bool && (bool)mip;
            State = Phase.Room;
            SendIdentity();
            Sound?.Invoke("unlock", 0.5f);
        }

        private void ReadRoomProps()
        {
            var rp = Client.RoomProps;
            object o;
            if (rp.TryGetValue("mode", out o) && o is string) Mode = (string)o;
            RoomCode = rp.TryGetValue("code", out o) && o is string ? (string)o : (Client.RoomName ?? "").Replace("crix_", "");
            RoomTitle = rp.TryGetValue("roomName", out o) && o is string ? (string)o : "Room " + RoomCode;
            if (rp.TryGetValue("raceTarget", out o)) RaceTarget = Math.Max(1, (int)PhotonClient.AsNum(o));
            if (rp.TryGetValue("rushTime", out o)) RushSeconds = Math.Max(30, (int)PhotonClient.AsNum(o));
        }

        private void OnCustom(int code, int actor, Dictionary<string, object> c)
        {
            if (c == null) c = new Dictionary<string, object>();
            if (code == EvPing) { if (actor == Me) PingMs = (int)Math.Max(0, now * 1000.0 - PhotonClient.Num(c, "t")); return; }
            if (actor == Me && code != EvMatchEnd) return;
            var p = Get(actor);
            switch (code)
            {
                case EvPosition:
                {
                    int lag = Math.Max(0, Math.Min(10, (int)Math.Round(((PingMs > 0 ? PingMs : 60) / 2.0) / TickMs)));
                    p.SimY = (float)PhotonClient.Num(c, "y");
                    p.SimVel = (float)PhotonClient.Num(c, "v");
                    p.Score = (int)PhotonClient.Num(c, "s");
                    p.Dead = PhotonClient.Num(c, "d") != 0;
                    if (c.ContainsKey("t"))
                    {
                        float target = (float)PhotonClient.Num(c, "t") + (p.Dead ? 0 : NativeSim.Speed * lag);
                        p.TravTarget = target;
                        if (Math.Abs(target - p.Travelled) > 60 || p.LastPos < 0) p.Travelled = target;
                        else p.Travelled += (target - p.Travelled) * 0.35f;
                    }
                    if (p.LastPos < 0) p.DrawY = p.SimY;
                    p.LastPos = now;
                    if (!p.Dead)
                    {
                        float keepT = p.Travelled, keepTarget = p.TravTarget;
                        for (int i = 0; i < lag; i++) RemoteStep(p);
                        p.Travelled = keepT; p.TravTarget = keepTarget;      // the distance was already carried
                    }
                    break;
                }
                case EvDeath:
                    p.Dead = true; p.Score = (int)PhotonClient.Num(c, "finalScore");
                    if (MatchActive && Mode == "lastone" && IsHost) CheckLastOne();
                    break;
                case EvScore:
                    p.Score = (int)PhotonClient.Num(c, "score");
                    if (c.ContainsKey("coins")) p.Coins = (int)PhotonClient.Num(c, "coins");
                    break;
                case EvRespawn: p.Dead = false; break;
                case EvCoinTake: Sim.MarkTaken((int)PhotonClient.Num(c, "id")); break;
                case EvChat:
                case EvLobbyChat:
                {
                    string text = MiniJson.Str(c, "text");
                    if (string.IsNullOrEmpty(text)) break;
                    if (text.Length > 120) text = text.Substring(0, 120);
                    Chat.Add(new ChatLine { Name = p.Name, Colour = p.Colour, Text = filter.Clean(text), At = now });
                    while (Chat.Count > 30) Chat.RemoveAt(0);
                    break;
                }
                case EvNameUpdate:
                {
                    string n = MiniJson.Str(c, "name"); if (!string.IsNullOrEmpty(n)) p.Name = n.Length > 24 ? n.Substring(0, 24) : n;
                    string col = MiniJson.Str(c, "colour"); if (col != null && col.StartsWith("#", StringComparison.Ordinal) && col.Length == 7) p.Colour = col;
                    if (c.ContainsKey("level")) p.Level = Math.Max(1, (int)PhotonClient.Num(c, "level"));
                    if (c.ContainsKey("hat")) p.Hat = Catalog.CosmeticById(MiniJson.Str(c, "hat") ?? "") != null ? MiniJson.Str(c, "hat") : null;
                    if (c.ContainsKey("trail")) p.Trail = Catalog.CosmeticById(MiniJson.Str(c, "trail") ?? "") != null ? MiniJson.Str(c, "trail") : null;
                    p.Platform = MiniJson.Str(c, "platform");
                    p.Roles.Clear();
                    object roles;
                    if (c.TryGetValue("roles", out roles) && roles is List<object>) foreach (var r in (List<object>)roles) if (r is string) p.Roles.Add((string)r);
                    break;
                }
                case EvKick:
                    if ((int)PhotonClient.Num(c, "targetActor") == Me) { Notice?.Invoke("Removed from the room", "The host removed you."); Leave(); }
                    break;
                case EvMatchStart:
                {
                    string mode = MiniJson.Str(c, "mode") ?? Mode;
                    if (now - matchStartedAt < 3 && MatchActive) break;            // the site's re-entry guard
                    if (Array.IndexOf(Modes, mode) < 0) { Notice?.Invoke("That mode is website-only", "Waiting for the next match."); break; }
                    BeginMatch(mode, (uint)(long)PhotonClient.Num(c, "seed"));
                    break;
                }
                case EvMatchEnd:
                {
                    var res = ParseResults(c);
                    object w; int winner = c.TryGetValue("winnerActor", out w) && w != null ? (int)PhotonClient.AsNum(w) : 0;
                    ShowResults(res, winner, MiniJson.Str(c, "mode") ?? Mode);
                    break;
                }
                case EvHostEndGame: ShowResults(ParseResults(c), 0, Mode); break;
                case EvModeChange: { string m = MiniJson.Str(c, "mode"); if (m != null && !MatchActive) Mode = m; break; }
                case EvHostChanged: break;    // the master client id arrives as a room property too
            }
        }

        private void BeginMatch(string mode, uint s)
        {
            Mode = mode; seed = s;
            MatchActive = true; Spectating = false; MatchInProgressOnJoin = false; Results = null; Winner = 0;
            matchStartedAt = now; MatchClock = 0;
            restartPending = respawnPending = endSent = false;
            foreach (var p in Players.Values) { p.Score = 0; p.Coins = 0; p.Dead = false; p.Travelled = p.TravTarget = 0; p.SimY = p.DrawY = 300; p.SimVel = 0; }
            Sim.StartMp(s, mode == "coinrush", true);
            State = Phase.Playing;
            forcePos = true;
            Sound?.Invoke("select", 1f);
        }

        private void ShowResults(List<Result> res, int winner, string mode)
        {
            if (State != Phase.Playing && State != Phase.Room) return;
            bool wasPlaying = MatchActive;
            MatchActive = false; Spectating = false;
            Sim.EndMatch();
            Results = res; Winner = winner;
            State = Phase.Results;
            if (IsHost) Client.SetRoomProps(Args("matchInProgress", false));
            if (!wasPlaying) return;
            if (winner == Me) Sound?.Invoke("victory", 1f);
            else if (winner != 0) Sound?.Invoke("fail", 1f);
            int coins = mode == "coinrush" ? Sim.CoinsThisMatch * 2 : 0;
            int xp = winner == Me ? 10 : 0;
            if (coins > 0 || xp > 0) Reward?.Invoke(coins, xp);
        }

        private void CheckLastOne()
        {
            if (!MatchActive || endSent) return;
            int alive = 0, last = 0;
            foreach (var p in Players.Values) if (!p.Dead) { alive++; last = p.Actor; }
            if (Players.Count < 2 && alive > 0) return;        // alone in the room: keep flying
            if (alive <= 1)
            {
                var res = Standings();
                endSent = true;
                Send(EvMatchEnd, Args("results", ResultsJson(res), "winnerActor", alive == 1 ? (object)last : null), true);   // the echo shows the results
            }
        }

        // ------------------------------------------------------------------ helpers

        private List<Result> Standings()
        {
            var l = new List<Result>();
            foreach (var p in Players.Values)
            {
                int score = p.Me ? (Mode == "coinrush" ? Sim.CoinsThisMatch : Sim.Score) : (Mode == "coinrush" ? Math.Max(p.Coins, p.Score) : p.Score);
                l.Add(new Result { Actor = p.Actor, Name = p.Name, Score = score });
            }
            l.Sort((a, b) => b.Score.CompareTo(a.Score));
            return l;
        }

        private static List<object> ResultsJson(List<Result> res)
        {
            var l = new List<object>();
            foreach (var r in res) l.Add(Args("actor", r.Actor, "name", r.Name, "score", r.Score));
            return l;
        }

        private static List<Result> ParseResults(Dictionary<string, object> c)
        {
            var l = new List<Result>();
            object o;
            if (c.TryGetValue("results", out o) && o is List<object>)
                foreach (var x in (List<object>)o)
                {
                    var d = MiniJson.Obj(x);
                    if (d == null) continue;
                    l.Add(new Result { Actor = (int)PhotonClient.Num(d, "actor"), Name = MiniJson.Str(d, "name") ?? "Player", Score = (int)PhotonClient.Num(d, "score") });
                }
            return l;
        }

        private Player NewPlayer(int actor, string name) =>
            new Player { Actor = actor, Name = string.IsNullOrEmpty(name) ? "Player" + actor : name, Colour = Colours[((actor % 12) + 12) % 12] };

        private Player Get(int actor)
        {
            Player p;
            if (!Players.TryGetValue(actor, out p)) { Players[actor] = p = NewPlayer(actor, null); Seats(); }
            return p;
        }

        private void Seats()
        {
            var ids = new List<int>();
            foreach (var p in Players.Values) if (!p.Me) ids.Add(p.Actor);
            ids.Sort();
            for (int i = 0; i < ids.Count; i++) Players[ids[i]].Seat = i;
        }

        private void SendIdentity()
        {
            var id = Identity != null ? Identity() : new Dictionary<string, object>();
            id["name"] = Nickname;
            id["platform"] = "windows";
            id["colour"] = Colours[((Me % 12) + 12) % 12];
            id["roles"] = new List<object>();          // never claims a staff role
            id["presence"] = "online";
            if (!id.ContainsKey("pfId")) id["pfId"] = null;
            id["photo"] = null;
            Send(EvNameUpdate, id, false);
            var me = Get(Me);
            me.Name = Nickname; me.Colour = (string)id["colour"];
        }

        private void Send(int code, Dictionary<string, object> content, bool toAll) => Client.RaiseEvent(code, content, toAll);

        private void ClearRoom()
        {
            if (MatchActive) Sim.EndMatch();
            MatchActive = false; Spectating = false;
            Players.Clear(); Results = null; RoomCode = null;
        }

        private static Dictionary<string, object> Args(params object[] kv) => MiniJson.Args(kv);

        public void Dispose() { Client.Dispose(); }
    }
}
