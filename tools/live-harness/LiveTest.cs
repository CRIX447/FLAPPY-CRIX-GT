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
//   4. finding browsers on a PC laid out like CRIX's (no Edge or Chrome; Edge's leftover
//      EdgeCore engine; Opera GX; a downloaded engine) -> usable ones in the right order,
//      Opera GX reported as not usable, failed browsers remembered.
//   5. no usable browser: the engine is downloaded (from a local stand-in for Google's
//      Chrome for Testing servers), unpacked, found, and runs the website.
//      Needs HEADLESS_SHELL (a headless shell executable) - skipped otherwise.
//   6. only FlappyCrix.dll installed (no Web folder, as found on CRIX's PC) -> the live site still
//      works (the bridge is built into the DLL); with the live site down it says the offline copy
//      is missing instead of showing an empty page.
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
using FlappyCrix.Web.Cdp;

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
        cfg.UseOfflineCopy.Value = true;          // scenarios 2-3 test the opt-in offline copy; 6-7 turn it off
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
            if (only == "" || only == "4") FindBrowsers();
            if (only == "" || only == "5") DownloadEngine();
            if (only == "" || only == "6") DllOnlyInstall();
            if (only == "" || only == "7") LiveOnly();
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

    // ------------------------------------------------------------------ 4. finding browsers

    static string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "");
        return path;
    }

    static void FindBrowsers()
    {
        Console.WriteLine("== 4. finding browsers (CRIX's PC: no Edge/Chrome, EdgeCore left over, Opera GX)");
        string t = Path.Combine(Path.GetTempPath(), "flappycrix-finder-" + Guid.NewGuid().ToString("N"));
        string pf86 = Path.Combine(t, "Program Files (x86)"), local = Path.Combine(t, "Local"), data = Path.Combine(t, "Data");
        string sep = Path.DirectorySeparatorChar.ToString();
        Touch(Path.Combine(pf86, "Microsoft/EdgeCore/99.0.1150.30/msedge.exe".Replace("/", sep)));
        string coreNew = Touch(Path.Combine(pf86, "Microsoft/EdgeCore/131.0.2903.112/msedge.exe".Replace("/", sep)));
        Touch(Path.Combine(pf86, "Microsoft/EdgeWebView/Application/131.0.2903.112/msedgewebview2.exe".Replace("/", sep)));
        Touch(Path.Combine(local, "Programs/Opera GX/launcher.exe".Replace("/", sep)));
        string vivaldi = Touch(Path.Combine(local, "Vivaldi/Application/vivaldi.exe".Replace("/", sep)));
        string engine = Touch(Path.Combine(data, "engine/154.0.8037.92/chrome-headless-shell-linux64/chrome-headless-shell".Replace("/", sep)));
        var log = new List<string>();
        var found = BrowserFinder.FindAll("", data, log.Add, new[] { pf86, local });
        string order = string.Join(" > ", found.Select(b => b.Name).ToArray());
        Check("usable browsers found, best first (newest EdgeCore, Vivaldi, then the downloaded engine)",
              found.Count == 3 && found[0].Exe == coreNew && found[1].Exe == vivaldi && found[2].Exe == engine && found[2].Downloaded, order);
        Check("Edge's WebView runtime is not used as a browser", found.All(b => !b.Exe.Contains("msedgewebview2")));
        Check("Opera GX is reported as installed but not usable hidden", log.Any(l => l.Contains("Opera GX") && l.Contains("can't run hidden")),
              log.FirstOrDefault(l => l.Contains("Opera")) ?? "");
        string operaOnly = Path.Combine(t, "OperaOnly");
        Touch(Path.Combine(operaOnly, "Programs/Opera GX/launcher.exe".Replace("/", sep)));
        var none = BrowserFinder.FindAll("", Path.Combine(t, "Empty"), log.Add, new[] { operaOnly });
        Check("only Opera GX installed -> no usable browser (so the engine gets downloaded)", none.Count == 0);
        BrowserFinder.RecordFailure(data, coreNew);
        Check("a browser that failed to start is remembered", BrowserFinder.FailedBefore(data, coreNew) && !BrowserFinder.FailedBefore(data, vivaldi));
        File.SetLastWriteTimeUtc(coreNew, DateTime.UtcNow.AddMinutes(5));
        Check("...and tried again once it's updated", !BrowserFinder.FailedBefore(data, coreNew));
        var chosen = BrowserFinder.FindAll(Touch(Path.Combine(local, "Programs/Opera GX/opera.exe".Replace("/", sep))), data, log.Add, new[] { pf86 });
        Check("BrowserPath is still honoured first (with a warning for Opera)", chosen.Count > 0 && chosen[0].UserChosen && log.Any(l => l.StartsWith("BrowserPath is Opera")));
        try { Directory.Delete(t, true); } catch { }
    }

    // ------------------------------------------------------------------ 5. downloading the engine

    static void DownloadEngine()
    {
        Console.WriteLine("== 5. no usable browser -> download the engine, unpack, run the website");
        string shell = Environment.GetEnvironmentVariable("HEADLESS_SHELL");
        if (string.IsNullOrEmpty(shell) || !File.Exists(shell)) { Console.WriteLine("SKIP (set HEADLESS_SHELL to a headless shell executable)"); return; }
        string t = Path.Combine(Path.GetTempPath(), "flappycrix-dl-" + Guid.NewGuid().ToString("N"));
        string site = Path.Combine(t, "google"), data = Path.Combine(t, "Data");
        const int Port = 47450; const string Ver = "141.0.7390.37";
        string prefix = "http://127.0.0.1:" + Port + "/chrome-for-testing-public/";
        // Google's layout: <version>/linux64/chrome-headless-shell-linux64.zip containing chrome-headless-shell-linux64/chrome-headless-shell
        string pkg = Path.Combine(t, "pkg", "chrome-headless-shell-linux64");
        Directory.CreateDirectory(pkg);
        File.WriteAllText(Path.Combine(pkg, "chrome-headless-shell"), "#!/bin/sh\nexec \"" + shell + "\" \"$@\"\n");
        File.WriteAllText(Path.Combine(pkg, "LICENSE.headless_shell"), "stand-in package for the test");
        Process.Start("chmod", "+x \"" + Path.Combine(pkg, "chrome-headless-shell") + "\"").WaitForExit();
        string zipDir = Path.Combine(site, "chrome-for-testing-public", Ver, "linux64");
        Directory.CreateDirectory(zipDir);
        var z = Process.Start(new ProcessStartInfo("zip", "-q -r \"" + Path.Combine(zipDir, "chrome-headless-shell-linux64.zip") + "\" chrome-headless-shell-linux64")
                              { WorkingDirectory = Path.Combine(t, "pkg"), UseShellExecute = false });
        z.WaitForExit();
        File.WriteAllText(Path.Combine(site, "cft.json"),
            "{\"channels\":{\"Stable\":{\"channel\":\"Stable\",\"version\":\"" + Ver + "\",\"downloads\":{\"chrome-headless-shell\":[" +
            "{\"platform\":\"win64\",\"url\":\"" + prefix + Ver + "/win64/chrome-headless-shell-win64.zip\"}," +
            "{\"platform\":\"linux64\",\"url\":\"" + prefix + Ver + "/linux64/chrome-headless-shell-linux64.zip\"}]}}}}");
        File.WriteAllText(Path.Combine(site, "evil.json"),
            "{\"channels\":{\"Stable\":{\"version\":\"1.2.3\",\"downloads\":{\"chrome-headless-shell\":[{\"platform\":\"linux64\",\"url\":\"http://evil.example/x/chrome-headless-shell-linux64.zip\"}]}}}}");
        // an older engine version already there (should be cleaned up)
        Touch(Path.Combine(data, "engine", "120.0.0.1", "chrome-headless-shell-linux64", "chrome-headless-shell"));
        var srv = Process.Start(new ProcessStartInfo("python3", "-m http.server " + Port + " --bind 127.0.0.1 --directory \"" + site + "\"")
                                { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
        Thread.Sleep(1200);
        try
        {
            var evil = new EngineDownloader { JsonUrl = "http://127.0.0.1:" + Port + "/evil.json", UrlPrefix = prefix, Platform = "linux64" };
            string refused = null;
            try { evil.Run(BrowserFinder.EngineRoot(data)); } catch (Exception e) { refused = e.Message; }
            Check("refuses to download from anywhere but Google's Chrome for Testing storage", refused != null && refused.Contains("unexpected address"), refused ?? "downloaded!");

            var logs = new List<string>();
            var dl = new EngineDownloader { JsonUrl = "http://127.0.0.1:" + Port + "/cft.json", UrlPrefix = prefix, Platform = "linux64" };
            dl.StartAsync(BrowserFinder.EngineRoot(data), m => { logs.Add(m); Console.WriteLine("      " + m); });
            var sw = Stopwatch.StartNew();
            while (!dl.Done && sw.ElapsedMilliseconds < 60000) Thread.Sleep(50);
            Check("downloads and unpacks the engine", dl.ExePath != null && File.Exists(dl.ExePath), dl.ExePath ?? dl.Error);
            Check("progress reached 100%", dl.Progress >= 0.999f, (dl.BytesDone) + " of " + dl.BytesTotal + " bytes");
            Check("older engine versions are removed", !Directory.Exists(Path.Combine(data, "engine", "120.0.0.1")));
            var found = BrowserFinder.FindAll("", data, null, new string[0]);
            Check("the finder picks up the downloaded engine", found.Count == 1 && found[0].Downloaded && found[0].Exe == dl.ExePath, found.Count > 0 ? found[0].Name : "none");

            if (dl.ExePath != null)
            {
                var cfg = NewConfig();
                cfg.UseRemoteWebsite.Value = false;               // packaged copy: this test is about the engine
                cfg.BrowserPath.Value = "";
                var game = new EdgeBrowserGame(cfg, modFolder, dl.ExePath);
                var glog = new List<string>();
                game.Log += m => { lock (glog) glog.Add(m); };
                game.Start();
                var w = Stopwatch.StartNew();
                while (!game.IsReady && !game.HasFailed && w.ElapsedMilliseconds < 40000) { game.Tick(); Thread.Sleep(16); }
                Check("the downloaded engine runs the website", game.IsReady, game.IsReady ? game.ModeName : (game.FailureReason ?? "timed out"));
                game.Dispose();
            }
        }
        finally
        {
            try { srv.Kill(); } catch { }
            try { Directory.Delete(t, true); } catch { }
        }
    }

    // ------------------------------------------------------------------ 6. DLL-only install

    static void DllOnlyInstall()
    {
        Console.WriteLine("== 6. only FlappyCrix.dll installed (no Web folder)");
        string bare = Path.Combine(Path.GetTempPath(), "flappycrix-dllonly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bare);
        string saved = modFolder;
        modFolder = bare;
        try
        {
            StartSite("");
            using (var r = new Run(NewConfig()))
            {
                bool live = r.PumpUntil(() => r.LiveReady, 40);
                Check("the live site works with just the DLL (bridge built in)", live, r.Game.ModeName);
            }
        }
        finally { modFolder = saved; StopSite(); try { Directory.Delete(bare, true); } catch { } }
    }

    static void LiveOnly()
    {
        Console.WriteLine("== 7. default: live site only (no offline copy, no remake); site down, then back");
        StopSite();
        var cfg = NewConfig();
        cfg.UseOfflineCopy.Value = false;
        using (var r = new Run(cfg))
        {
            bool waiting = r.PumpUntil(() => r.When("trying again shortly").HasValue, 40);
            Check("live site unreachable -> says so and keeps trying (no packaged copy, engine not failed)",
                  waiting && !r.Game.HasFailed && r.When("packaged copy") == null && !r.Game.IsReady);
            r.PumpUntil(() => false, 3);
            Check("...and is still waiting a few seconds later, not failed", !r.Game.HasFailed);
            StartSite("");
            double up = r.Now;
            bool back = r.PumpUntil(() => r.LiveReady, 40);
            Check("when the site is reachable the real page loads by itself", back,
                  back ? "live " + (r.Now - up).ToString("0.0") + " s after the site came back" : r.Game.ModeName);
        }
        StopSite();
    }
}
