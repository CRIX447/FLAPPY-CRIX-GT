// Runs the in-game version (FlappyApp: rules, menus, seasons, drawing) outside Unity, checks
// it, times the drawing and saves a picture of every screen. See run.sh.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FlappyCrix.Native;

class NativeTest
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

    static FlappyApp NewApp(string save, DateTime utc, Assets art, out List<string> sounds)
    {
        if (File.Exists(save)) File.Delete(save);
        var app = new FlappyApp(SaveData.Load(save), art, 42, () => utc);
        var s = new List<string>();
        app.Sound += (n, v) => s.Add(n);
        sounds = s;
        return app;
    }

    /// <summary>Flies through the gaps (a simple autopilot) for up to `seconds`.</summary>
    static void Fly(FlappyApp app, float seconds, bool autopilot = true)
    {
        for (int t = 0; t < seconds * 60 && app.Screen == "playing"; t++)
        {
            if (autopilot)
            {
                NativeSim.Pipe next = null;
                foreach (var p in app.Sim.Pipes) if (p.X + NativeSim.PipeWidth > NativeSim.BirdX - 16) { next = p; break; }
                float target = next != null ? (next.Top + next.Bottom) / 2 + 16 : 300;
                if (app.Sim.BirdY > target && app.Sim.Velocity > -1) app.Flap();
            }
            app.Update(1f / 60f);
        }
    }

    static int Main(string[] a)
    {
        outDir = a[0];
        Directory.CreateDirectory(outDir);
        string save = Path.Combine(outDir, "save.txt");
        var art = SpriteSheet.LoadEmbedded();
        Check(art.Get("bird") != null && art.Get("emoji/1f383") != null && art.Get("witch") != null, "pictures load from the DLL (" + art.Count + ")");

        // ---- Halloween (5 Oct 2026, Sydney)
        var oct5 = new DateTime(2026, 10, 4, 22, 0, 0, DateTimeKind.Utc);      // 5 Oct 09:00 in Sydney
        List<string> sounds;
        var app = NewApp(save, oct5, art, out sounds);
        Check(app.Season != null && app.Season.Id == "spooky", "5 October is Halloween (Sydney time)");
        app.Update(0.016f);
        Shot(app, "01-menu-halloween");
        Check(app.ButtonIds.Contains("play") && app.Focus == "play", "main menu: PLAY is highlighted");

        // SELECT on PLAY starts; SELECT while playing never flaps
        app.Select();
        Check(app.Screen == "playing", "SELECT on PLAY starts a run");
        app.Update(0.1f);
        float v0 = app.Sim.Velocity;
        app.Select();
        Check(app.Sim.Velocity == v0, "SELECT never flaps");
        Fly(app, 14);
        Check(app.Sim.Score >= 4, "autopilot scores (" + app.Sim.Score + ")");
        Check(sounds.Contains("jump") && sounds.Contains("coin"), "flap and coin sounds");
        // let a pumpkin show up if one is on screen
        Shot(app, "02-playing-halloween");

        // timing: draw the whole screen while playing
        var sw = Stopwatch.StartNew(); int frames = 0;
        for (int i = 0; i < 90 && app.Screen == "playing"; i++) { Fly(app, 1f / 30f); app.Render(); frames++; }
        double ms = sw.Elapsed.TotalMilliseconds / Math.Max(1, frames);
        Console.WriteLine("     render " + ms.ToString("0.00") + " ms per frame (960x640, Mono)");
        Check(ms < 12, "drawing is fast enough for 30 fps (" + ms.ToString("0.0") + " ms)");

        app.TogglePause();
        Check(app.Screen == "paused" && !app.MusicShouldPlay, "PAUSE pauses (and the music stops)");
        app.Update(0.016f);
        Shot(app, "03-paused");
        app.StartButton();
        Check(app.Screen == "playing", "START resumes");
        Fly(app, 20, false);                                   // stop flapping: fall
        Check(app.Screen == "dead", "falling ends the run");
        Check(app.Save.Best >= 4, "the best score is saved (" + app.Save.Best + ")");
        app.Update(0.2f); app.Update(0.2f);
        Shot(app, "04-game-over");
        Check(app.Focus == "retry", "game over: RETRY is highlighted");
        app.Navigate(0, -1);
        Check(app.Focus == "menu", "joystick down moves to MAIN MENU");
        app.Select();
        Check(app.Screen == "menu", "MAIN MENU goes back");

        // ---- store, locker, daily, calendar, awards, settings, help
        app.Save.Coins = 5000;
        app.Render();
        app.Navigate(0, -1);                                   // PLAY -> first row
        Check(app.Focus == "daily", "joystick down from PLAY reaches DAILY (" + app.Focus + ")");
        app.Select(); app.Render();
        Check(app.Modal == "daily", "SELECT opens Daily");
        Shot(app, "05-daily");
        long c0 = app.Save.Coins;
        app.Select(); app.Update(0.1f);
        Check(app.Save.Coins == c0 + 100 && !app.DailyReady, "daily reward claimed (+100)");
        Shot(app, "06-daily-claimed");
        app.Back();
        Check(app.Modal == null, "Back closes a menu");

        app.OpenForTest("calendar"); app.Render();
        Shot(app, "07-calendar-halloween");
        Check(app.Focus == "door_1", "calendar: first open door highlighted (" + app.Focus + ")");
        c0 = app.Save.Coins;
        for (int d = 1; d <= 5; d++) app.PressForTest("door_" + d);
        app.PressForTest("door_6");
        Check(app.Save.Coins == c0 + 72 + 84 + 96 + 108 + 120, "doors 1-5 open, door 6 stays locked");
        app.Update(0.2f);
        Shot(app, "08-calendar-opened");

        app.OpenForTest("store", "powerups"); app.Render();
        Shot(app, "09-store-powerups");
        app.PressForTest("buy_shield");
        Check(app.Sim.PowerupOn("shield"), "buying SHIELD turns it on");
        app.PressForTest("buy_shield");
        Check(sounds.Last() == "error", "can't buy SHIELD twice");
        app.OpenForTest("store", "cosmetics"); app.Render();
        app.PressForTest("cos_bucket"); app.PressForTest("cos_bucket");
        app.PressForTest("cos_trail_rainbow"); app.PressForTest("cos_trail_rainbow");
        Check(app.Save.Hat == "bucket" && app.Save.Trail == "trail_rainbow", "buy and wear a hat and a trail");
        Check(app.Save.Achievements.Contains("dripped"), "DRIPPED OUT unlocked");
        app.Update(0.3f);
        Shot(app, "10-store-cosmetics");
        app.OpenForTest("locker", "hats"); app.Render(); Shot(app, "11-locker-hats");
        app.OpenForTest("locker", "trails"); app.Render(); Shot(app, "12-locker-trails");
        app.OpenForTest("achievements"); app.Render(); Shot(app, "13-achievements");
        app.OpenForTest("settings"); app.Render(); Shot(app, "14-settings");
        app.OpenForTest("help"); app.Render(); Shot(app, "15-help");
        app.Back();

        // a run in the rainbow trail and bucket hat, with the shield on
        app.StartButton();
        Fly(app, 9);
        Shot(app, "16-playing-cosmetics");
        Fly(app, 60);

        // save file round trip
        app.Flush();
        var re = SaveData.Load(save);
        Check(re.Coins == app.Save.Coins && re.Hat == "bucket" && re.Owned.Contains("trail_rainbow") && re.Best == app.Save.Best, "progress is saved to a file and read back");

        // ---- the hidden screen is silent
        sounds.Clear();
        app.Visible = false;
        app.StartButton(); app.Flap(); app.Update(0.1f);
        Check(sounds.Count == 0, "no sounds while the screen is hidden");
        app.Visible = true;

        // ---- the other seasons
        var shots = new[] {
            new { When = new DateTime(2026, 12, 10, 1, 0, 0, DateTimeKind.Utc), Id = "christmas", Name = "17-christmas" },
            new { When = new DateTime(2027, 3, 26, 1, 0, 0, DateTimeKind.Utc), Id = "easter", Name = "18-easter" },
            new { When = new DateTime(2027, 3, 10, 1, 0, 0, DateTimeKind.Utc), Id = "birthday", Name = "19-birthday" },
            new { When = new DateTime(2026, 11, 10, 1, 0, 0, DateTimeKind.Utc), Id = (string)null, Name = "20-no-season" },
        };
        foreach (var s in shots)
        {
            List<string> snd;
            var b = NewApp(save, s.When, art, out snd);
            Check((b.Season == null ? null : b.Season.Id) == s.Id, s.When.ToString("d MMM yyyy") + " is " + (s.Id ?? "no season"));
            b.StartButton();
            Fly(b, 6);
            b.Update(0.008f);
            Shot(b, s.Name);
        }
        Check(Season.Auto(new Season.Day { Y = 2027, M = 3, D = 18 }) == Season.Birthday, "18 March 2027 is the birthday even in Easter week");
        Check(Season.Auto(new Season.Day { Y = 2026, M = 3, D = 18 }) == null, "no birthday theme in 2026");
        Check(Season.Auto(new Season.Day { Y = 2026, M = 12, D = 27 }) == null, "Christmas ends on 26 December");

        // calendar screens for Christmas
        {
            List<string> snd;
            var b = NewApp(save, new DateTime(2026, 12, 10, 1, 0, 0, DateTimeKind.Utc), art, out snd);
            b.OpenForTest("calendar"); b.Render(); Shot(b, "21-calendar-christmas");
            b.Back(); b.Render(); Shot(b, "22-menu-christmas");
        }
        // the witch fly-by
        {
            List<string> snd;
            var b = NewApp(save, oct5, art, out snd);
            for (int i = 0; i < 60 * 40 && !snd.Contains("witch1"); i++) b.Update(1f / 60f);
            Check(snd.Contains("witch1"), "the witch flies over (with her sound)");
            for (int i = 0; i < 60 * 2; i++) b.Update(1f / 60f);
            Shot(b, "23-witch");
        }

        // ---- the game's own thread (what the Unity side uses)
        {
            List<string> snd;
            var b = NewApp(save, oct5, art, out snd);
            var r = new AppRunner(b);
            r.Start();
            int got = 0;
            var sw2 = Stopwatch.StartNew();
            var heard = new List<KeyValuePair<string, float>>();
            while (sw2.ElapsedMilliseconds < 1500)
            {
                r.Nudge();
                var f = r.TakeFrame();
                if (f != null) { got++; r.GiveBack(f); }
                if (sw2.ElapsedMilliseconds > 300 && r.Screen == "menu") r.Post(x => x.Select());
                System.Threading.Thread.Sleep(11);                        // about 90 Unity frames a second
            }
            r.TakeSounds(heard);
            Check(b.Save.Games == 1 && r.Screen != "menu", "threaded: SELECT on PLAY started a run (" + r.Screen + ")");
            Check(got >= 30, "threaded: about 30 pictures a second (" + got + " in 1.5 s)");
            Check(heard.Exists(k => k.Key == "select"), "threaded: sounds reach the Unity side");
            r.Visible = false;
            System.Threading.Thread.Sleep(100);
            r.TakeFrame(); heard.Clear(); r.TakeSounds(heard);
            r.Post(x => x.Flap());
            for (int i = 0; i < 30; i++) { System.Threading.Thread.Sleep(10); r.Nudge(); }
            var none = r.TakeFrame(); r.TakeSounds(heard);
            Check(none == null && heard.Count == 0 && !r.MusicShouldPlay, "threaded: hidden = no pictures, no sounds, no music");
            r.Dispose();
        }

        Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
