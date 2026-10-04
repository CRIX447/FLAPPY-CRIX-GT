// Dev-only end-to-end test of the hidden-browser engine (CdpPage, HeadlessBrowser,
// CdpConnection, WebSocketClient, MiniJson, LocalWebServer) without Unity.
//   mcs -out:enginetest.exe -recurse:'../../src/FlappyCrix/Web/Cdp/*.cs' ../../src/FlappyCrix/Web/LocalWebServer.cs EngineTest.cs
//   mono enginetest.exe <browser exe> <mod/FlappyCrix/Web> <out dir> [extra browser args]
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FlappyCrix.Web;
using FlappyCrix.Web.Cdp;

class EngineTest
{
    static int pass, fail;
    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) pass++; else fail++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (detail != "" ? "  -- " + detail : ""));
    }

    static readonly BlockingCollection<string> messages = new BlockingCollection<string>();
    static string WaitFor(string prefix, int ms)
    {
        var sw = Stopwatch.StartNew();
        string m;
        while (sw.ElapsedMilliseconds < ms)
            if (messages.TryTake(out m, 100) && m.StartsWith(prefix, StringComparison.Ordinal)) return m;
        return null;
    }
    static void Drain() { string m; while (messages.TryTake(out m)) { } }

    static int Main(string[] a)
    {
        string exe = a[0], web = a[1], outDir = a[2], extra = a.Length > 3 ? a[3] : "";
        Directory.CreateDirectory(outDir);
        string bridge = File.ReadAllText(Path.Combine(web, "__flappycrix", "bridge.js"));
        string cfg = "{\"skipIntro\":true,\"disableServiceWorker\":true,\"flapStartsGame\":true,\"siteOrigin\":\"https://crixgamingvr.com\"}";

        var server = new LocalWebServer(web, "flappycrix.html", cfg, s => { });
        server.Start(47400);

        byte[] lastFrame = null; int frames = 0, lastDw = 0, lastDh = 0;
        var blocked = new ConcurrentQueue<string>();
        string failed = null;
        var connected = new ManualResetEvent(false);

        var page = new CdpPage(new CdpPage.Options
        {
            BrowserExe = exe,
            ProfileDir = Path.Combine(outDir, "profile"),
            Width = 768, Height = 960,
            ExtraArgs = extra,
            InjectScript = "window.__FLAPPYCRIX_CONFIG=" + cfg + ";\n" + bridge,
            AllowDocument = url => url.StartsWith(server.BaseUrl) || url.StartsWith("https://crixgamingvr.com/flappycrix"),
        });
        page.Log += s => Console.WriteLine("  [engine] " + s);
        page.Failed += s => { failed = s; Console.WriteLine("  [engine FAILED] " + s); };
        page.Connected += () => connected.Set();
        page.Frame += (jpeg, dw, dh) => { lastFrame = jpeg; lastDw = dw; lastDh = dh; Interlocked.Increment(ref frames); };
        page.BridgeMessage += m => messages.Add(m);
        page.NavigationBlocked += u => blocked.Enqueue(u);
        page.NavigationError += e => Console.WriteLine("  [nav error] " + e);

        var sw = Stopwatch.StartNew();
        page.StartAsync(server.EntryUrl);
        Check("hidden browser launched and DevTools connected", connected.WaitOne(30000) && failed == null, failed ?? (page.BrowserVersion + " in " + sw.ElapsedMilliseconds + " ms"));
        if (failed != null) { page.Dispose(); return 1; }

        string ready = WaitFor("BridgeReady:", 20000);
        Check("bridge injected before the site's scripts and reported ready", ready != null, (ready ?? "") + " at " + sw.ElapsedMilliseconds + " ms");
        Thread.Sleep(2500);
        Check("screencast frames arriving", frames > 0 && lastFrame != null && lastFrame.Length > 1000, frames + " frames, last " + (lastFrame?.Length ?? 0) + " bytes");
        int jw = 0, jh = 0;
        if (lastFrame != null)
            for (int i = 2; i + 8 < lastFrame.Length; i++)    // JPEG SOF0/SOF2 marker -> height, width
                if (lastFrame[i] == 0xFF && (lastFrame[i + 1] == 0xC0 || lastFrame[i + 1] == 0xC2))
                { jh = (lastFrame[i + 5] << 8) | lastFrame[i + 6]; jw = (lastFrame[i + 7] << 8) | lastFrame[i + 8]; break; }
        Check("frames are 1:1 and contain the whole 768x960 page at the top-left", jw == lastDw && jh == lastDh && jw >= 768 && jh >= 960,
              "frame " + jw + "x" + jh + ", visible area " + lastDw + "x" + lastDh + " -> screen shows the top-left 768x960");
        File.WriteAllText(Path.Combine(outDir, "frame-area.txt"), lastDw + "x" + lastDh);
        if (lastFrame != null) File.WriteAllBytes(Path.Combine(outDir, "frame-menu.jpg"), lastFrame);

        // State via the binding
        Drain();
        page.Evaluate("flappyCrixSend('St:'+FlappyCrixBridge.state())");
        string st = WaitFor("St:", 3000);
        Check("JS -> engine binding round trip", st != null, st);

        // Close the first-run tutorial with a real mouse click on its close button
        string tutOpen = null;
        for (int i = 0; i < 30 && tutOpen != "TutOpen:true"; i++)
        {
            Drain();
            page.Evaluate("flappyCrixSend('TutOpen:'+!!(document.getElementById('tutorialModal')&&document.getElementById('tutorialModal').classList.contains('active')))");
            tutOpen = WaitFor("TutOpen:", 2000);
            if (tutOpen != "TutOpen:true") Thread.Sleep(200);
        }
        Thread.Sleep(600);   // let its open animation finish

        // First-launch toasts sit over the tutorial's close button at this size; dismiss them
        // the way a player would - real clicks on their LATER/dismiss buttons.
        int toastClicks = 0;
        for (int i = 0; i < 6; i++)
        {
            Drain();
            page.Evaluate("(function(){var bs=[].slice.call(document.querySelectorAll('#toasts button')).filter(function(b){var r=b.getBoundingClientRect();return r.width>0&&/later|ok|dismiss|close|×|✕/i.test(b.textContent||'');});var b=bs.pop();var r=b&&b.getBoundingClientRect();flappyCrixSend('Toast:'+(r?[r.left+r.width/2,r.top+r.height/2].join(','):'none'));})()");
            string tb = WaitFor("Toast:", 2000);
            if (tb == null || tb == "Toast:none") break;
            var p2 = tb.Substring(6).Split(',').Select(double.Parse).ToArray();
            page.Mouse("mouseMoved", p2[0], p2[1]); page.Mouse("mousePressed", p2[0], p2[1]); page.Mouse("mouseReleased", p2[0], p2[1]);
            toastClicks++;
            Thread.Sleep(700);
        }
        Drain();
        page.Evaluate("flappyCrixSend('Toasts:'+[].slice.call(document.querySelectorAll('#toasts button')).filter(function(b){return b.getBoundingClientRect().width>0&&/later/i.test(b.textContent)}).length)");
        string left = WaitFor("Toasts:", 2000);
        Check("real clicks dismiss the site's toasts", toastClicks > 0 && left == "Toasts:0", toastClicks + " clicks, " + left);
        Drain();
        page.Evaluate("(function(){var b=document.getElementById('closeTutorialBtn');var r=b&&b.getBoundingClientRect();flappyCrixSend('Rect:'+(r&&r.width?[r.left+r.width/2,r.top+r.height/2].join(','):'none'));})()");
        string rect = WaitFor("Rect:", 3000);
        Console.WriteLine("    tutorial " + tutOpen + ", close button centre " + rect);
        if (rect != null && rect != "Rect:none")
        {
            Drain();
            var pt = rect.Substring(5).Split(',');
            page.Evaluate("(function(){var e=document.elementFromPoint(" + pt[0] + "," + pt[1] + ");flappyCrixSend('Top:'+(e?(e.id||e.className||e.tagName):'none')+' in '+(e&&e.closest('[id]')?e.closest('[id]').id:''));})()");
            Console.WriteLine("    element on top at that point: " + WaitFor("Top:", 2000));
        }
        if (rect != null && rect != "Rect:none")
        {
            var xy = rect.Substring(5).Split(',').Select(double.Parse).ToArray();
            page.Mouse("mouseMoved", xy[0], xy[1]);
            page.Mouse("mousePressed", xy[0], xy[1]);
            page.Mouse("mouseReleased", xy[0], xy[1]);
            Thread.Sleep(600);
            Drain();
            page.Evaluate("flappyCrixSend('Tut:'+document.getElementById('tutorialModal').classList.contains('active'))");
            Check("real mouse click closes the site's tutorial", WaitFor("Tut:", 3000) == "Tut:false");
        }
        else Check("tutorial close button found", false, rect);

        // Start a run, then flap with real Space key presses
        Drain();
        page.Evaluate("FlappyCrixBridge.start()");
        string scr = WaitFor("Screen:playing", 4000);
        Check("game started (Screen:playing event)", scr != null);
        Thread.Sleep(300);
        Drain();
        page.Evaluate("flappyCrixSend('V0:'+bird.velocity)");
        string v0 = WaitFor("V0:", 2000);
        page.KeySpace();
        Thread.Sleep(30);
        page.Evaluate("flappyCrixSend('V1:'+bird.velocity)");
        string v1 = WaitFor("V1:", 2000);
        double vel1 = v1 != null ? double.Parse(v1.Substring(3)) : 0;
        Check("real Space key (Input.dispatchKeyEvent) flaps via the site's own handler", vel1 < -5, v0 + " -> " + v1);
        page.Evaluate("flappyCrixSend('Music:'+(typeof bgMusic!=='undefined'&&bgMusic&&!bgMusic.paused))");
        Check("music playing", WaitFor("Music:", 2000) == "Music:true");

        // Keep it alive for a few seconds with the autopilot, count frames
        page.Evaluate("window.__ap=setInterval(function(){var p=pipes.find(function(p){return p.x+pipeWidth>bird.x-20});var t=p?(p.top+p.bottom)/2+18:300;if(bird.y>t&&bird.velocity>-1)FlappyCrixBridge.flap();},16)");
        int f0 = frames; var t0 = Stopwatch.StartNew();
        Thread.Sleep(5000);
        double fps = (frames - f0) / t0.Elapsed.TotalSeconds;
        Check("frame stream while playing", fps >= 15, fps.ToString("0.0") + " frames/s");
        if (lastFrame != null) { File.WriteAllBytes(Path.Combine(outDir, "frame-playing.jpg"), lastFrame); File.WriteAllText(Path.Combine(outDir, "frame-area.txt"), lastDw + "x" + lastDh); }
        Drain();
        page.Evaluate("flappyCrixSend('Score:'+score)");
        Check("scoring while flapping", (WaitFor("Score:", 2000) ?? "Score:0") != "Score:0");
        page.Evaluate("clearInterval(window.__ap)");

        // Gatekeeper: page tries to leave the game
        Drain();
        while (blocked.TryDequeue(out _)) { }
        page.Evaluate("location.href='https://discord.com/invite/MbQvJGDAst'");
        Thread.Sleep(1500);
        string b1; blocked.TryDequeue(out b1);
        Check("navigation away from the game is blocked and reported (to open on the desktop)", b1 != null && b1.Contains("discord.com"), b1);
        page.Evaluate("flappyCrixSend('Url:'+location.href)");
        string here = WaitFor("Url:", 2000);
        Check("panel still on the game", here != null && here.Contains("flappycrix"), here);

        // Bridge-level desktop routing (link + window.open)
        Drain();
        page.Evaluate("window.open('https://www.youtube.com/@CRIXGAMINGVR')");
        string ext = WaitFor("OpenExternal:", 3000);
        Check("window.open -> OpenExternal message", ext != null && ext.Contains("youtube.com"), ext);

        // Dialogs never block the page
        Drain();
        page.Evaluate("flappyCrixSend('Confirm:'+confirm('Remove friend?'))");
        Check("confirm() answered 'no' without freezing the page", WaitFor("Confirm:", 3000) == "Confirm:false");

        // In-page self test via the binding
        Drain();
        page.Evaluate("FlappyCrixBridge.reportSelfTest()");
        string self = WaitFor("SelfTest:", 3000);
        Check("self test report received", self != null && self.Contains("\"js\":true") && self.Contains("\"canvas\":true"), self);
        Console.WriteLine("    " + self);

        // Shutdown leaves nothing running
        int pid = -1;
        try { pid = int.Parse(File.ReadAllText(Path.Combine(outDir, "profile", "flappycrix.pid")).Trim()); } catch { }
        page.Dispose();
        Thread.Sleep(1500);
        bool gone = true;
        try { var p = Process.GetProcessById(pid); gone = p.HasExited; } catch { gone = true; }
        Check("browser engine closed on shutdown", gone, "pid " + pid);
        server.Dispose();

        Console.WriteLine();
        Console.WriteLine(pass + "/" + (pass + fail) + " passed");
        return fail == 0 ? 0 : 1;
    }
}
