// Runs the mod's real website engine (EdgeBrowserGame + BrowserGame, from FlappyCrix.dll)
// outside Unity against a real headless Chromium, with a stand-in for crixgamingvr.com
// (live_site.py) that can be slow, down, or stalled - the cases that put the in-game
// screen into the site's offline mode in CRIX's third test.
//
//   tools/live-harness/run.sh            (builds the DLL first; needs mono, python3, Chromium)
//
// Scenarios
//   1. slow live site: two images take 45 s (so the page's "load" event does too) and the site's
//      first online check (/robots.txt) takes 8 s, longer than its 5 s limit.
//      -> the live page is used (no fallback), ready in seconds, and the site goes from
//         "offline" back to "online" by itself.
//   2. live site down at start, back later
//      -> the packaged copy is shown at once, then the live site again once it can be reached.
//   3. live site stalls (accepts, never answers), then recovers
//      -> packaged copy after RemoteTimeoutSeconds, then the live site again.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using FlappyCrix;
using FlappyCrix.Web;

static class LiveTest
{
    static int pass, fail;
    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) pass++; else fail++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (detail != "" ? "  -- " + detail : ""));
    }

    static string chrome, webRoot, here, modFolder;
    const int LivePort = 47400;
    static readonly string LiveUrl = "http://localhost:" + LivePort + "/flappycrix";

    // ------------------------------------------------------------------ helpers

    static Process site;
    static void StartSite(string extra)
    {
        StopSite();
        var psi = new ProcessStartInfo("python3", Path.Combine(here, "live_site.py") + " --root \"" + webRoot + "\" --port " + LivePort + " " + extra)
        { UseShellExecute = false, RedirectStandardOutput = true };
        site = Process.Start(psi);
        string line = site.StandardOutput.ReadLine();
        if (line == null || !line.StartsWith("LIVE SITE READY")) throw new Exception("live_site.py did not start: " + line);
    }
    static void StopSite()
    {
        if (site == null) return;
        try { if (!site.HasExited) { site.Kill(); site.WaitForExit(3000); } } catch { }
        site = null;
    }

    sealed class Run : IDisposable
    {
        public EdgeBrowserGame Game;
        public readonly List<KeyValuePair<double, string>> Log = new List<KeyValuePair<double, string>>();
        readonly Stopwatch clock = Stopwatch.StartNew();

        public Run(FlappyCrixConfig cfg)
        {
            Game = new EdgeBrowserGame(cfg, modFolder, chrome);
            Game.Log += m => { lock (Log) Log.Add(new KeyValuePair<double, string>(clock.Elapsed.TotalSeconds, m)); Console.WriteLine("      [" + clock.Elapsed.TotalSeconds.ToString("0.0") + " s] " + m); };
            Game.Start();
        }
        public double Now => clock.Elapsed.TotalSeconds;

        /// <summary>Ticks the game like Unity's Update until cond() or the time runs out.</summary>
        public bool PumpUntil(Func<bool> cond, double seconds)
        {
            double end = Now + seconds;
            while (Now < end)
            {
                Game.Tick();
                if (Game.HasFailed) return false;
                if (cond()) return true;
                Thread.Sleep(16);
            }
            return cond();
        }
        public double? When(string contains, double after = 0)
        {
            lock (Log) foreach (var kv in Log) if (kv.Key >= after && kv.Value.Contains(contains)) return kv.Key;
            return null;
        }
        public bool LiveReady => Game.IsReady && Game.ModeName.Contains("localhost:" + LivePort);
        public bool LocalReady => Game.IsReady && Game.ModeName.Contains("packaged copy");
        public void Dispose() { try { Game.Dispose(); } catch { } }
    }

    static FlappyCrixConfig NewConfig()
    {
        string path = Path.Combine(Path.GetTempPath(), "flappycrix-livetest-" + Guid.NewGuid().ToString("N") + ".cfg");
        var cfg = new FlappyCrixConfig(new BepInEx.Configuration.ConfigFile(path, true));
        cfg.BrowserPath.Value = chrome;
        cfg.BrowserExtraArgs.Value = "--no-sandbox";
        cfg.RemoteUrl.Value = LiveUrl;
        cfg.UseRemoteWebsite.Value = true;
        cfg.RemoteTimeoutSeconds.Value = 8f;
        cfg.ReconnectSeconds.Value = 5f;
        cfg.RunSelfTest.Value = false;
        cfg.MuteWebAudio.Value = true;
        cfg.LocalPort.Value = 47391;
        return cfg;
    }

    // ------------------------------------------------------------------ main

    static int Main(string[] a)
    {
        chrome = a[0]; webRoot = Path.GetFullPath(a[1]); here = Path.GetFullPath(a[2]);
        string only = a.Length > 3 ? a[3] : "";

        // BepInEx's paths and Unity converters, as its preloader sets them up
        string game = Path.Combine(Path.GetTempPath(), "flappycrix-livetest-game");
        Directory.CreateDirectory(Path.Combine(game, "BepInEx", "config"));
        foreach (var kv in new Dictionary<string, string> {
            { "GameRootPath", game }, { "BepInExRootPath", Path.Combine(game, "BepInEx") },
            { "ConfigPath", Path.Combine(game, "BepInEx", "config") },
            { "BepInExConfigPath", Path.Combine(game, "BepInEx", "config", "BepInEx.cfg") } })
            typeof(BepInEx.Paths).GetProperty(kv.Key, BindingFlags.Static | BindingFlags.Public).GetSetMethod(true).Invoke(null, new object[] { kv.Value });
        typeof(BepInEx.Paths).Assembly.GetType("BepInEx.Configuration.LazyTomlConverterLoader")
            .GetMethod("AddUnityEngineConverters", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);

        // A mod folder whose Web/ is the shipped one
        modFolder = Path.Combine(Path.GetTempPath(), "flappycrix-livetest-mod");
        Directory.CreateDirectory(modFolder);
        string link = Path.Combine(modFolder, "Web");
        if (!Directory.Exists(link)) Process.Start("ln", "-s \"" + webRoot + "\" \"" + link + "\"").WaitForExit();

        try
        {
            if (only == "" || only == "1") SlowLiveSite();
            if (only == "" || only == "2") DownThenUp();
            if (only == "" || only == "3") StallThenRecover();
        }
        finally { StopSite(); }

        Console.WriteLine(pass + "/" + (pass + fail) + " passed");
        return fail == 0 ? 0 : 1;
    }

    static void SlowLiveSite()
    {
        Console.WriteLine("== 1. slow live site (two images 45 s, first online check 8 s)");
        StartSite("--slow-images 2:45 --slow-robots-first 8");
        using (var r = new Run(NewConfig()))
        {
            bool ready = r.PumpUntil(() => r.LiveReady, 40);
            Check("the live site is used, not the packaged copy", ready && r.When("Couldn't load the live site") == null, r.Game.ModeName);
            double? loading = r.When("Live site is loading"), readyAt = r.When("ready - Website (http://localhost");
            Check("the game is ready long before the page's load event (two images take 45 s)",
                  readyAt.HasValue && loading.HasValue && readyAt.Value - loading.Value < 20, "ready " + (readyAt - loading)?.ToString("0.0") + " s after the page started");
            bool offline = r.PumpUntil(() => r.When("says it is offline").HasValue, 25);
            Check("the site's first online check timed out (it said offline)", offline);
            double after = r.When("says it is offline") ?? 0;
            bool online = r.PumpUntil(() => r.When("says it is online", after).HasValue, 40);
            Check("...and the bridge got it to check again: back online by itself", online,
                  online ? "online " + (r.When("says it is online", after) - after)?.ToString("0.0") + " s later" : "");
            Check("still on the live site", r.LiveReady && r.When("Couldn't load the live site") == null);
        }
        StopSite();
    }

    static void DownThenUp()
    {
        Console.WriteLine("== 2. live site down at start, back later");
        StopSite();
        using (var r = new Run(NewConfig()))
        {
            bool local = r.PumpUntil(() => r.LocalReady, 40);
            Check("live site unreachable -> the packaged copy is shown", local && r.When("Couldn't load the live site").HasValue,
                  r.Log.Where(kv => kv.Value.Contains("Couldn't load")).Select(kv => kv.Value).FirstOrDefault() ?? "");
            StartSite("");
            double up = r.Now;
            bool back = r.PumpUntil(() => r.LiveReady, 40);
            Check("when it can be reached again, it switches back to the live site by itself", back,
                  back ? "live " + (r.Now - up).ToString("0.0") + " s after the site came back" : r.Game.ModeName);
        }
        StopSite();
    }

    static void StallThenRecover()
    {
        Console.WriteLine("== 3. live site stalls (connects, never answers), then recovers");
        StartSite("--hang");
        using (var r = new Run(NewConfig()))
        {
            bool local = r.PumpUntil(() => r.LocalReady, 45);
            Check("stalled live site -> packaged copy after RemoteTimeoutSeconds", local && r.When("didn't start loading within").HasValue,
                  r.Log.Where(kv => kv.Value.Contains("Couldn't load")).Select(kv => kv.Value).FirstOrDefault() ?? "");
            StartSite("");
            double up = r.Now;
            bool back = r.PumpUntil(() => r.LiveReady, 45);
            Check("...then back to the live site once it answers", back,
                  back ? "live " + (r.Now - up).ToString("0.0") + " s after it recovered" : r.Game.ModeName);
        }
        StopSite();
    }
}
