// The mod's multiplayer code (Multiplayer + PhotonClient + NativeSim), driven by commands on
// stdin, so tools/online-harness/interop.py can play it against the website's own multiplayer
// code running in Chromium. One JSON line of state per "state" command.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
using System;
using System.Collections.Generic;
using System.Threading;
using FlappyCrix.Native;
using FlappyCrix.Online;
using FlappyCrix.Web.Cdp;

class ModBot
{
    static Multiplayer mp;
    static readonly object gate = new object();
    static readonly List<double> pipeTops = new List<double>();
    static readonly List<string> notices = new List<string>();
    static readonly List<int> coinsTaken = new List<int>();
    static NativeSim.Pipe lastPipe;
    static bool god, autopilot;
    static int rewardsCoins, rewardsXp;

    static void Main(string[] a)
    {
        WebSocketClient.AcceptAnyCertificate = true;
        var cfg = new SiteConfig { PhotonNameServer = a.Length > 0 ? a[0] : "wss://127.0.0.1:19093", PhotonAppId = "test-photon-app" };
        var client = new PhotonClient(cfg) { Log = m => Console.Error.WriteLine("[mod] " + m) };
        var sim = new NativeSim(7);
        mp = new Multiplayer(client, sim) { Nickname = "VR Gorilla" };
        mp.Identity = () => new Dictionary<string, object> { { "level", 7 }, { "hat", "bucket" }, { "trail", "trail_rainbow" } };
        mp.Notice += (t, b) => { lock (gate) notices.Add(t + ": " + b); };
        mp.Reward += (c, x) => { lock (gate) { rewardsCoins += c; rewardsXp += x; } };
        sim.Scored += s => mp.OnScored(s);
        sim.Died += s => mp.OnDied(s);
        sim.CoinsGained += n => mp.OnCoin();
        sim.CoinTaken += id => { lock (gate) coinsTaken.Add(id); };

        var loop = new Thread(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double last = 0, acc = 0;
            while (true)
            {
                double t = sw.Elapsed.TotalSeconds, dt = t - last; last = t;
                lock (gate)
                {
                    mp.Update((float)dt);
                    acc += dt;
                    while (acc >= 1 / 60.0)
                    {
                        acc -= 1 / 60.0;
                        if (sim.Screen != "playing") continue;
                        if (god) sim.GrantPowerup("shield", 999);
                        if (autopilot)
                        {
                            NativeSim.Pipe next = null;
                            foreach (var p in sim.Pipes) if (p.X + NativeSim.PipeWidth > NativeSim.BirdX - 16) { next = p; break; }
                            float target = next != null ? (next.Top + next.Bottom) / 2 + 16 : 300;
                            if (sim.BirdY > target && sim.Velocity > -1) { sim.Flap(); mp.OnFlap(); }
                        }
                        sim.Step();
                        if (sim.Pipes.Count > 0 && sim.Pipes[sim.Pipes.Count - 1] != lastPipe) { lastPipe = sim.Pipes[sim.Pipes.Count - 1]; pipeTops.Add(Math.Round(lastPipe.Top, 2)); }
                    }
                }
                Thread.Sleep(5);
            }
        }) { IsBackground = true };
        loop.Start();

        string line;
        while ((line = Console.ReadLine()) != null)
        {
            var w = line.Trim().Split(' ');
            lock (gate)
            {
                switch (w[0])
                {
                    case "connect": mp.Connect(); break;
                    case "create": mp.CreateRoom(w[1], 8, w.Length < 3 || w[2] == "1", "VR Gorilla's Lobby"); break;
                    case "join": mp.JoinByCode(w[1]); break;
                    case "start": mp.StartMatch(); break;
                    case "end": mp.EndMatchNow(); break;
                    case "leave": mp.Leave(); break;
                    case "mode": mp.SetMode(w[1]); break;
                    case "god": god = w[1] == "on"; if (!god) sim.Powerups.Remove("shield"); break;
                    case "auto": autopilot = w[1] == "on"; break;
                    case "die": sim.Powerups.Remove("shield"); god = false; autopilot = false; sim.SetBird(589, 7); break;
                    case "results": mp.CloseResults(); break;
                    case "quit": return;
                    case "state": Console.WriteLine(State()); Console.Out.Flush(); break;
                }
            }
        }
    }

    static string State()
    {
        var players = new List<object>();
        foreach (var p in mp.Players.Values)
            players.Add(MiniJson.Args("actor", p.Actor, "name", p.Name, "dead", p.Dead, "score", p.Score, "me", p.Me, "hat", p.Hat, "trail", p.Trail,
                                      "level", p.Level, "seenPos", mp.Visible(p), "x", Math.Round(mp.ScreenX(p)), "y", Math.Round(p.DrawY)));
        var results = new List<object>();
        if (mp.Results != null) foreach (var r in mp.Results) results.Add(MiniJson.Args("actor", r.Actor, "name", r.Name, "score", r.Score));
        var chat = new List<object>();
        foreach (var c in mp.Chat) chat.Add(MiniJson.Args("name", c.Name, "text", c.Text));
        var rooms = new List<object>();
        foreach (var r in mp.ListRooms()) rooms.Add(MiniJson.Args("name", r.Name, "title", r.Str("roomName"), "mode", r.Str("mode"), "players", r.PlayerCount));
        return MiniJson.Serialize(MiniJson.Args(
            "phase", mp.State.ToString(), "code", mp.RoomCode, "mode", mp.Mode, "me", mp.Me, "host", mp.IsHost, "matchActive", mp.MatchActive,
            "players", players, "pipes", new List<double>(pipeTops), "results", results, "winner", mp.Winner, "chat", chat,
            "notices", new List<string>(notices), "rooms", rooms, "screen", mp.Sim.Screen, "score", mp.Sim.Score, "ping", mp.PingMs,
            "coinsTaken", new List<int>(coinsTaken), "rewardCoins", rewardsCoins, "rewardXp", rewardsXp, "error", mp.Error));
    }
}
