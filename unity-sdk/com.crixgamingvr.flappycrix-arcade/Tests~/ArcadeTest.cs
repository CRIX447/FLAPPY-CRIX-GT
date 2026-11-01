// Runs the arcade outside Unity: attract mode, coins, high scores with initials, and a race
// between two pretend players (plus a third watching) over LoopbackTransport. Saves a picture
// of each screen. See run.sh. (Unity ignores folders ending in ~.)
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).
using System;
using System.Collections.Generic;
using System.IO;
using CrixArcade.Core;
using CrixArcade.Net;

class ArcadeTest
{
    static int fails;
    static string outDir;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

    static void Shot(FlappyApp app, string name)
    {
        app.Render();
        var b = new byte[app.C.Px.Length * 4];
        Buffer.BlockCopy(app.C.Px, 0, b, 0, b.Length);
        File.WriteAllBytes(Path.Combine(outDir, name + ".rgba"), b);
    }

    /// <summary>One cabinet with a player: the game runs on the test's thread.</summary>
    sealed class Player
    {
        public FlappyApp App; public ArcadeSeat Seat; public ArcadeNetwork Net; public LoopbackTransport T;
        readonly List<Action<FlappyApp>> inbox = new List<Action<FlappyApp>>();
        int frames; public bool Autopilot = true; public int CrashAfterScore = int.MaxValue;
        public Player(string id, string name, string cab, float x, LoopbackTransport.Hub hub, Assets art, DateTime utc)
        {
            string save = Path.Combine(outDir, id + ".txt");
            if (File.Exists(save)) File.Delete(save);
            App = new FlappyApp(SaveData.Load(save), art, 7, () => utc);
            App.Save.MusicOn = false;
            T = new LoopbackTransport(hub, id, name);
            Seat = new ArcadeSeat(cab, App, a => inbox.Add(a), () => new SeatState
            {
                Screen = App.Screen, Score = App.Sim.Score, BirdY = App.Sim.BirdY, InRace = App.InRace, Spectating = App.Spectating,
                InLobby = App.RaceStatus != null, CountingDown = App.CountingDown, Attract = App.Attract, EnteringInitials = App.EnteringInitials,
            }) { Group = "room", Position = new ArcadeVec(x, 0, 0) };
            Net = new ArcadeNetwork(T) { LocalPosition = () => new ArcadeVec(x, 1.6f, 0.8f) };
            Net.Add(Seat);
        }
        public void Step(double t, float dt)
        {
            var q = inbox.ToArray(); inbox.Clear();
            foreach (var a in q) a(App);
            if (Autopilot && App.Screen == "playing" && !App.Spectating)
            {
                NativeSim.Pipe next = null;
                foreach (var p in App.Sim.Pipes) if (p.X + NativeSim.PipeWidth > NativeSim.BirdX - 16) { next = p; break; }
                float target = next != null ? (next.Top + next.Bottom) / 2 + 16 : 300;
                if (App.Sim.Score < CrashAfterScore && App.Sim.BirdY > target && App.Sim.Velocity > -1) App.Flap();
            }
            App.Update(dt);
            if (++frames % 6 == 0) App.Render();
            Net.Update(t);
        }
    }

    static int Main(string[] a)
    {
        outDir = a[0];
        Directory.CreateDirectory(outDir);
        CoreResources.Load = n => File.ReadAllBytes(Path.Combine(a[1], n + ".bytes"));
        var art = SpriteSheet.LoadEmbedded();
        var utc = new DateTime(2026, 6, 10, 3, 0, 0, DateTimeKind.Utc);   // no season
        Check(art.Get("bird") != null, "pictures load from Resources (" + art.Count + ")");

        // ---------------------------------------------------------------- single player arcade
        var hub0 = new LoopbackTransport.Hub();
        var solo = new Player("s", "SOLO", "cab-solo", 0, hub0, art, utc);
        var app = solo.App;
        app.FreePlay = false;
        app.AddHighScore("CRX", 41); app.AddHighScore("BOB", 12); app.AddHighScore("AMY", 30);
        Check(app.HighScores[0].Name == "CRX" && app.HighScores[2].Name == "BOB", "high scores are sorted");
        Shot(app, "01-menu-insert-coin");
        app.StartButton();
        Check(app.Screen == "menu", "no coin: START doesn't play");
        app.AdvanceClock(41); app.Update(0.1f);
        Check(app.Attract, "left alone, attract mode starts");
        Shot(app, "02-attract");
        app.AdvanceClock(6); app.Update(0.1f);
        Shot(app, "03-attract-scores");
        app.InsertCoin();
        Check(app.Credits == 1 && !app.Attract, "a coin gives a credit and wakes the screen");
        app.StartButton();
        Check(app.Screen == "playing" && app.Credits == 0, "START spends the credit and plays");
        double t = 0;
        solo.CrashAfterScore = 35;
        for (int i = 0; i < 60 * 90 && app.Screen == "playing"; i++) { t += 1 / 60.0; solo.Step(t, 1 / 60f); }
        Check(app.Screen == "dead" && app.EnteringInitials, "a high score asks for initials (score " + app.Sim.Score + ")");
        app.AdvanceClock(1); app.Update(0.05f);
        Shot(app, "04-initials");
        app.Navigate(0, -1); app.Navigate(0, -1); app.Flap();     // C
        app.Navigate(0, 1); app.Flap();                             // A -> 9? (up from A wraps to space)
        app.StartButton();
        Check(!app.EnteringInitials && app.HighScores.Exists(h => h.Score == app.Sim.Score), "initials go into the table: " + app.HighScores[0].Name + " " + app.HighScores[0].Score);
        Check(app.Save.LastInitials.Length == 3, "initials are remembered (" + app.Save.LastInitials + ")");
        Shot(app, "05-after-initials");

        // ---------------------------------------------------------------- race between two cabinets
        var hub = new LoopbackTransport.Hub();
        var p1 = new Player("1", "CRIX", "cab-a", 0, hub, art, utc);
        var p2 = new Player("2", "Bob", "cab-b", 1.2f, hub, art, utc);
        var p3 = new Player("3", "Watcher", "cab-c", 2.4f, hub, art, utc);
        var far = new Player("4", "FarAway", "cab-far", 80f, hub, art, utc);
        var all = new[] { p1, p2, p3, far };
        p2.CrashAfterScore = 1;
        Action<double> run = secs =>
        {
            for (int i = 0; i < secs * 60; i++) { t += 1 / 60.0; foreach (var p in all) p.Step(t, 1 / 60f); hub.Pump(); }
        };
        run(1.5);
        p1.App.Render();
        Check(p1.App.RaceAvailable && new List<string>(p1.App.ButtonIds).Contains("race"), "RACE shows on a linked cabinet");
        p1.App.PressForTest("race"); run(0.6);
        Check(p1.App.RaceStatus != null, "pressing RACE opens the lobby");
        Shot(p1.App, "06-lobby-alone");
        far.App.PressForTest("race");
        p2.App.PressForTest("race"); run(1.5);
        Check(p1.App.RaceLobbyNames.Length == 2 && p2.App.RaceLobbyNames.Length == 2, "both nearby players are in the lobby (" + string.Join(",", p1.App.RaceLobbyNames) + ")");
        Check(far.App.RaceLobbyNames.Length == 1, "a player far away isn't");
        Shot(p2.App, "07-lobby-two");
        run(4);
        Check(p1.App.CountingDown || p1.App.InRace, "the countdown starts");
        Check(p1.App.RaceSeed == p2.App.RaceSeed && p1.App.RaceSeed != 0, "both got the same seed");
        Shot(p1.App, "08-countdown");
        run(3);
        Check(p1.App.InRace && p2.App.InRace && p1.App.Screen == "playing", "the race is on");
        run(1.2);
        Check(p1.App.Ghosts.Count == 1 && p2.App.Ghosts.Count == 1, "each sees the other as a ghost");
        p3.App.PressForTest("race"); run(1);
        Check(p3.App.Spectating, "RACE during a race nearby watches it");
        // same pipes on both screens (matched by position: the starts can be a frame apart)
        int matched = 0; bool samePipes = true;
        foreach (var q1 in p1.App.Sim.Pipes)
            foreach (var q2 in p2.App.Sim.Pipes)
                if (Math.Abs(q1.X - q2.X) < 30f) { matched++; if (Math.Abs(q1.Top - q2.Top) > 0.01f) samePipes = false; }
        samePipes &= matched > 0;
        Check(samePipes, "same pipes for every racer (" + matched + " compared)");
        run(1);
        Shot(p1.App, "09-race-ghost");
        Shot(p3.App, "10-watching");
        Check(p2.App.Screen == "dead" || p2.App.Sim.Score < 5, "Bob crashes early (score " + p2.App.Sim.Score + ")");
        p1.CrashAfterScore = Math.Max(p1.App.Sim.Score + 2, 8);
        run(25);
        Check(p1.App.Screen == "dead", "CRIX crashes too (score " + p1.App.Sim.Score + ")");
        run(5);
        Check(!p1.App.InRace && p1.App.RaceResult != null && p1.App.RaceResult.Count == 2, "the race ends with a result for both");
        Check(p1.App.RaceResult != null && p1.App.RaceResult[0].Key == "CRIX", "CRIX wins (" + (p1.App.RaceResult == null ? "" : string.Join(", ", p1.App.RaceResult.ConvertAll(kv => kv.Key + " " + kv.Value).ToArray())) + ")");
        if (p1.App.EnteringInitials) { p1.App.StartButton(); run(0.5); }
        Shot(p1.App, "11-result");
        Check(!p3.App.Spectating, "the watcher's screen goes back after the crash");

        // ---------------------------------------------------------------- cabinet in use
        p1.App.Back(); run(0.5);
        p1.Seat.TouchedAt = t; p1.App.StartButton(); p1.Autopilot = true; p1.CrashAfterScore = int.MaxValue;
        var p1b = new ArcadeSeat("cab-a", p2.App, x => x(p2.App), () => new SeatState { Screen = "menu" }) { Group = "room", Position = new ArcadeVec(0, 0, 0) };
        p2.Net.Add(p1b);
        run(2.5);
        Check(p2.App.RemoteName == "CRIX", "another player's view of the cabinet shows IN USE by CRIX");
        Shot(p2.App, "12-in-use");
        p2.App.StartButton();
        Check(p2.App.Screen != "playing", "and can't start it");

        // ---------------------------------------------------------------- shared high scores
        int before = p3.App.HighScores.Count;
        var p3a = new ArcadeSeat("cab-a", p3.App, x => x(p3.App), () => new SeatState { Screen = "menu" }) { Group = "room", Position = new ArcadeVec(0, 0, 0) };
        p3.Net.Add(p3a);
        p1.App.AddHighScore("ZZZ", 1);
        bool got = false; p3a.ScoresChanged = x => got = true;
        // a high score entered on cab-a reaches the copies of cab-a on other players' games
        var t1 = p1.T; t1.Send(System.Text.Encoding.UTF8.GetBytes("FCA1|H|cab-a|QQQ|77"), null, true); hub.Pump(); run(0.1);
        Check(got && p3.App.HighScores.Exists(h => h.Name == "QQQ" && h.Score == 77), "high scores are shared");

        // ---------------------------------------------------------------- bad messages are ignored
        t1.Send(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, null, false);
        t1.Send(System.Text.Encoding.UTF8.GetBytes("FCA1|P|x"), null, false);
        t1.Send(System.Text.Encoding.UTF8.GetBytes("FCA1|H|cab-a|AAA|99999999"), null, true);
        hub.Pump(); run(0.1);
        Check(!p3.App.HighScores.Exists(h => h.Score == 99999999), "silly scores are refused");

        // ---------------------------------------------------------------- speed
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 60; i++) { p1.App.Update(1 / 30f); p1.App.Render(); }
        Console.WriteLine("render: " + (sw.Elapsed.TotalMilliseconds / 60).ToString("0.0") + " ms a frame");

        Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
