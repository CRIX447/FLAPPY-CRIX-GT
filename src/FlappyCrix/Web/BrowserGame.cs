// Website mode, engine-independent part: the live page / packaged copy, the bridge
// (JS <-> Unity), the "only the game stays in the panel, everything else opens on the
// desktop" rule, live-to-packaged fallback, and the in-game self test.
// Engines (hidden Edge/Chrome, UnityWebBrowser) only move pixels and input.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using FlappyCrix.Web.Cdp;
using UnityEngine;

namespace FlappyCrix.Web
{
    public abstract class BrowserGame : IFlappyGame
    {
        public event Action<string> Log;

        public abstract string EngineName { get; }
        public string ModeName => "Website (" + (remoteActive ? config.RemoteUrl.Value : "packaged copy") + ") via " + EngineName;
        public bool IsReady { get; private set; }
        /// <summary>The engine itself started and connected (a later failure is the page's, not the browser's).</summary>
        public bool EngineEverConnected => engineConnected;
        public bool HasFailed { get; private set; }
        public string FailureReason { get; private set; }
        public abstract Texture PanelTexture { get; }
        public abstract Vector2 TextureScale { get; }
        public abstract Vector2 TextureOffset { get; }
        public float Aspect => (float)config.ResolutionWidth.Value / config.ResolutionHeight.Value;
        public string Screen { get; private set; } = "menu";
        public int Score { get; private set; }

        protected readonly FlappyCrixConfig config;
        protected readonly string modFolder;
        protected readonly string bridgeSource;
        protected readonly string configJson;
        protected LocalWebServer server;
        protected bool remoteActive;
        protected float startTime;

        private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
        private bool bridgeReady, engineConnected;
        private float remoteDeadline = -1f, readyDeadline;

        // Live site -> packaged copy -> live site again
        private bool remoteCommitted;          // the live page has started loading (it isn't stuck)
        private string fellBackBecause;        // why the packaged copy is showing (null = it isn't, or by choice)
        private bool fallbackNoticeShown;
        private float noticeAt = -1f, reconnectAt = -1f, goLiveAt = -1f;
        private int reconnects;
        private volatile bool probing;
        private string lastNet;
        private const int MaxReconnects = 5;

        protected BrowserGame(FlappyCrixConfig config, string modFolder)
        {
            this.config = config;
            this.modFolder = modFolder;
            string bridgeFile = Path.Combine(WebRoot, "__flappycrix", "bridge.js");
            bridgeSource = File.Exists(bridgeFile) ? File.ReadAllText(bridgeFile) : null;
            configJson = "{\"skipIntro\":" + Js(config.SkipIntro.Value) +
                         ",\"disableServiceWorker\":true" +
                         ",\"flapStartsGame\":" + Js(config.FlapStartsGame.Value) +
                         ",\"siteOrigin\":\"" + SiteOrigin(config.RemoteUrl.Value) + "\"}";
        }

        protected string WebRoot => Path.Combine(modFolder, "Web");
        /// <summary>%LOCALAPPDATA%\FlappyCrix: browser profiles and selftest.txt (see ModPaths).</summary>
        protected string DataFolder => dataFolder ?? (dataFolder = ModPaths.DataRoot(modFolder));
        private string dataFolder;
        protected int PageWidth => config.ResolutionWidth.Value;
        protected int PageHeight => config.ResolutionHeight.Value;
        private static string Js(bool b) => b ? "true" : "false";

        /// <summary>config + bridge, to run before the page's own scripts.</summary>
        protected string InjectScript => "window.__FLAPPYCRIX_CONFIG=" + configJson + ";\n" + bridgeSource;

        // ------------------------------------------------------------------ engine hooks

        /// <summary>Start the engine and load startUrl. Call EngineConnected() once it is up.</summary>
        protected abstract void StartEngine(string startUrl);
        protected abstract void EngineTick();
        protected abstract bool EngineUsable { get; }
        /// <summary>Press and release Space as a real key.</summary>
        protected abstract void EngineKeySpace();
        /// <summary>kind: 0 move, 1 down, 2 up. Page pixels from the top-left.</summary>
        protected abstract void EngineMouse(int kind, Vector2 px);
        protected abstract void EngineWheel(Vector2 px, int delta);
        protected abstract void EngineExec(string js);
        protected abstract void EngineNavigate(string url);
        /// <summary>Frames that reached Unity so far (self test).</summary>
        protected abstract int EngineFrames { get; }
        protected abstract void EngineDispose();

        // ------------------------------------------------------------------ start

        public void Start()
        {
            startTime = Time.realtimeSinceStartup;
            readyDeadline = startTime + config.EngineStartupTimeoutMs.Value / 1000f + 25f;
            if (bridgeSource == null) { Fail("Web/__flappycrix/bridge.js is missing from the mod folder."); return; }
            Directory.CreateDirectory(DataFolder);

            string startUrl;
            try
            {
                server = new LocalWebServer(WebRoot, "flappycrix.html", configJson, Emit);
                server.Start(config.LocalPort.Value);
                startUrl = server.EntryUrl;
            }
            catch (Exception e) { Fail("Could not serve the packaged website: " + e.Message); return; }

            if (config.UseRemoteWebsite.Value)
            {
                startUrl = config.RemoteUrl.Value;
                remoteActive = true;
                // The live site's clock starts when the hidden browser is up (EngineConnected), so a
                // slow browser start doesn't eat into it; until then readyDeadline covers the start.
                remoteDeadline = -1f;
            }

            try { StartEngine(startUrl); }
            catch (Exception e) { Fail(EngineName + " failed to start: " + e.GetType().Name + ": " + e.Message); }
        }

        // ------------------------------------------------------------------ called by engines (any thread)

        protected void OnMain(Action a) => mainThread.Enqueue(a);
        protected void EngineConnected() => OnMain(() =>
        {
            engineConnected = true;
            float now = Time.realtimeSinceStartup;
            Emit(EngineName + " connected after " + (now - startTime).ToString("0.0") + " s");
            if (remoteActive && !remoteCommitted)
            {
                remoteDeadline = now + config.RemoteTimeoutSeconds.Value;
                readyDeadline = Mathf.Max(readyDeadline, remoteDeadline + 30f);
            }
        });
        protected void EngineFailed(string why) => OnMain(() => Fail(why));
        protected void BridgeMessage(string msg) => OnMain(() => OnBridgeEvent(msg));
        /// <summary>A page the engine blocked (or saw) that isn't the game: open it on the desktop.</summary>
        protected void LeftTheGame(string url) => OnMain(() => OpenOnDesktop(DesktopUrl(url)));
        protected void LoadFailed(string what) => OnMain(() =>
        {
            Emit("Page failed to load: " + what);
            if (remoteActive) FallBackToLocal("the live site could not be reached");
        });

        // ------------------------------------------------------------------ the page rules

        /// <summary>The Flappy Crix game page, live or packaged. Anything else opens on the desktop.</summary>
        public bool IsGamePage(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            string path = u.AbsolutePath.TrimEnd(new[] { '/' }).ToLowerInvariant();
            bool gamePath = path == "/flappycrix" || path == "/flappycrix.html";
            if (server != null && url.StartsWith(server.BaseUrl, StringComparison.Ordinal)) return gamePath || path == "";
            return SameSite(url, config.RemoteUrl.Value) && gamePath;
        }

        /// <summary>Packaged-copy pages map to the same page on the live site.</summary>
        private string DesktopUrl(string url)
        {
            if (server != null && url.StartsWith(server.BaseUrl, StringComparison.Ordinal))
                return SiteOrigin(config.RemoteUrl.Value) + url.Substring(server.BaseUrl.Length);
            return url;
        }

        protected static string SiteOrigin(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Scheme + "://" + u.Authority : "https://crixgamingvr.com";
        }

        /// <summary>Same site, allowing www./apex redirects (crixgamingvr.com vs www.crixgamingvr.com).</summary>
        protected static bool SameSite(string a, string b)
        {
            Uri ua, ub;
            if (!Uri.TryCreate(a, UriKind.Absolute, out ua) || !Uri.TryCreate(b, UriKind.Absolute, out ub)) return false;
            string ha = ua.Host.ToLowerInvariant(), hb = ub.Host.ToLowerInvariant();
            if (ha.StartsWith("www.")) ha = ha.Substring(4);
            if (hb.StartsWith("www.")) hb = hb.Substring(4);
            return ha == hb;
        }

        private float lastDesktopOpen = -10f, minuteStart;
        private int desktopOpensThisMinute;

        /// <summary>
        /// Opens a link in the player's normal desktop browser (Application.OpenURL).
        /// http/https only, at most one per second and ten a minute, so a page can't spam tabs.
        /// </summary>
        protected void OpenOnDesktop(string url)
        {
            if (!config.OpenLinksOnDesktop.Value) { Emit("Link not opened (OpenLinksOnDesktop = false): " + url); return; }
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "https" && u.Scheme != "http"))
            { Emit("Ignored non-web link: " + url); return; }
            float now = Time.realtimeSinceStartup;
            if (now - minuteStart > 60f) { minuteStart = now; desktopOpensThisMinute = 0; }
            if (now - lastDesktopOpen < 1f || desktopOpensThisMinute >= 10) { Emit("Link ignored (too many at once): " + url); return; }
            lastDesktopOpen = now; desktopOpensThisMinute++;
            Emit("Opening on the desktop: " + u.AbsoluteUri);
            Application.OpenURL(u.AbsoluteUri);
        }

        private void OnBridgeEvent(string msg)
        {
            int colon = msg.IndexOf(':');
            string name = colon < 0 ? msg : msg.Substring(0, colon);
            string arg = colon < 0 ? "" : msg.Substring(colon + 1);
            switch (name)
            {
                case "BridgeReady":
                    bridgeReady = true;
                    IsReady = true;
                    remoteDeadline = -1f;
                    Emit("Bridge " + arg + " ready - " + ModeName + " - after " + (Time.realtimeSinceStartup - startTime).ToString("0.0") + " s");
                    if (config.RunSelfTest.Value && report == null) selfTestAt = Time.realtimeSinceStartup + 3f;
                    if (!remoteActive && fellBackBecause != null && !fallbackNoticeShown) noticeAt = Time.realtimeSinceStartup + 2.5f;
                    break;
                case "ScoreChanged": int s; if (int.TryParse(arg, out s)) Score = s; break;
                case "Screen": Screen = arg; break;
                case "GameStarted": Score = 0; break;
                case "GameOver": Emit("Game over, score " + arg); break;
                case "OpenExternal": OpenOnDesktop(arg); break;
                case "SelfTest": FinishSelfTest(arg); break;
                case "PageStart": PageStarted(arg); break;
                case "NetOffline":
                    string net = arg == "1" ? "offline" : "online";
                    if (net != lastNet)
                    {
                        lastNet = net;
                        Emit("The website says it is " + net + (remoteActive ? "" : " (the packaged copy is always offline)"));
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ per frame

        public void Tick()
        {
            Action a;
            while (mainThread.TryDequeue(out a)) Safe(a);
            if (HasFailed) return;
            Safe(EngineTick);

            float now = Time.realtimeSinceStartup;
            if (remoteActive && !remoteCommitted && remoteDeadline > 0 && now > remoteDeadline && !bridgeReady)
                FallBackToLocal("it didn't start loading within " + config.RemoteTimeoutSeconds.Value.ToString("0") + " s");
            if (!bridgeReady && now > readyDeadline)
            {
                if (remoteActive && engineConnected) FallBackToLocal("the page started loading but the game on it didn't start");
                else Fail(engineConnected ? "The page loaded but the game never reported ready (JavaScript/bridge problem)."
                                          : "The browser engine did not connect.");
            }
            if (HasFailed) return;
            TickReconnect(now);
            if (selfTestAt > 0 && now >= selfTestAt) BeginSelfTest();
            if (selfTestCollectAt > 0 && now >= selfTestCollectAt) { selfTestCollectAt = -1f; Exec("FlappyCrixBridge.reportSelfTest()"); }
        }

        private void FallBackToLocal(string why)
        {
            if (!remoteActive) return;
            Emit("Couldn't load the live site (" + why + "); playing the packaged copy (offline) instead.");
            remoteActive = false;
            remoteCommitted = false;
            remoteDeadline = -1f;
            bridgeReady = false;
            IsReady = false;
            fellBackBecause = why;
            fallbackNoticeShown = false;
            float now = Time.realtimeSinceStartup;
            readyDeadline = now + 25f;
            reconnectAt = config.ReconnectSeconds.Value > 0 && reconnects < MaxReconnects ? now + config.ReconnectSeconds.Value : -1f;
            Safe(() => EngineNavigate(server.EntryUrl));
        }

        /// <summary>A new page has started in the engine (sent by the bridge as the page begins).</summary>
        private void PageStarted(string url)
        {
            if (remoteActive && SameSite(url, config.RemoteUrl.Value))
            {
                if (!remoteCommitted) Emit("Live site is loading: " + url + " (after " + (Time.realtimeSinceStartup - startTime).ToString("0.0") + " s)");
                remoteCommitted = true;
                remoteDeadline = -1f;
                // It is coming: give the game on it as long as a slow connection needs.
                readyDeadline = Mathf.Max(readyDeadline, Time.realtimeSinceStartup + 90f);
            }
        }

        // ------------------------------------------------------------------ back to the live site

        /// <summary>
        /// While the packaged copy is showing because the live site couldn't be loaded, check now and
        /// then whether crixgamingvr.com can be reached, and switch back between runs.
        /// </summary>
        private void TickReconnect(float now)
        {
            if (noticeAt > 0 && now >= noticeAt)
            {
                noticeAt = -1f;
                fallbackNoticeShown = true;
                Notice("warn", "\U0001F310 Playing the offline copy",
                       "Couldn't load crixgamingvr.com (" + fellBackBecause + ")." +
                       (reconnectAt > 0 ? " It switches to the live site by itself when it can." : ""));
            }
            if (goLiveAt > 0 && now >= goLiveAt) { goLiveAt = -1f; GoLive(); return; }
            if (remoteActive || fellBackBecause == null || reconnectAt < 0 || probing || goLiveAt > 0) return;
            if (now < reconnectAt) return;
            if (Screen == "playing" || Screen == "paused") return;        // never in the middle of a run

            Uri u;
            if (!Uri.TryCreate(config.RemoteUrl.Value, UriKind.Absolute, out u)) { reconnectAt = -1f; return; }
            probing = true;
            string host = u.Host; int port = u.Port;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool reachable = false;
                try
                {
                    using (var c = new TcpClient())
                    {
                        var ar = c.BeginConnect(host, port, null, null);
                        if (ar.AsyncWaitHandle.WaitOne(4000)) { c.EndConnect(ar); reachable = true; }
                    }
                }
                catch { reachable = false; }
                OnMain(() =>
                {
                    probing = false;
                    if (remoteActive || fellBackBecause == null) return;
                    if (!reachable) { reconnectAt = Time.realtimeSinceStartup + config.ReconnectSeconds.Value; return; }
                    Emit(host + " can be reached again; switching back to the live site.");
                    Notice("info", "\U0001F4F6 Back online", "Switching to the live site at crixgamingvr.com...");
                    goLiveAt = Time.realtimeSinceStartup + 3f;
                });
            });
        }

        private void GoLive()
        {
            if (remoteActive) return;
            if (Screen == "playing" || Screen == "paused") { goLiveAt = Time.realtimeSinceStartup + 2f; return; }
            reconnects++;
            float now = Time.realtimeSinceStartup;
            remoteActive = true;
            remoteCommitted = false;
            bridgeReady = false;
            IsReady = false;
            fellBackBecause = null;
            reconnectAt = -1f;
            remoteDeadline = now + config.RemoteTimeoutSeconds.Value;
            readyDeadline = remoteDeadline + 30f;
            Emit("Loading the live site again (attempt " + reconnects + " of " + MaxReconnects + "): " + config.RemoteUrl.Value);
            Safe(() => EngineNavigate(config.RemoteUrl.Value));
        }

        /// <summary>Shows a message on the page with the site's own toast.</summary>
        private void Notice(string kind, string title, string body)
        {
            Emit("On screen: " + body);
            Exec("FlappyCrixBridge.notice(" + MiniJson.Quote(kind) + "," + MiniJson.Quote(title) + "," + MiniJson.Quote(body) + ")");
        }

        // ------------------------------------------------------------------ input (IFlappyGame)

        /// <summary>
        /// While playing, a real Space key (the site's own keydown handler -> jump()). Off the
        /// playfield, the bridge does what the site's tap-to-play does: start / retry.
        /// </summary>
        public void Flap()
        {
            if (!EngineUsable) return;
            Safe(EngineKeySpace);
            if (Screen != "playing") Exec("FlappyCrixBridge.flap()");
        }
        public void Restart() => Exec("FlappyCrixBridge.restart()");
        public void TogglePause() => Exec("FlappyCrixBridge.togglePause()");
        public void Back() => Exec("FlappyCrixBridge.back()");
        public void Navigate(int dx, int dy) => Exec("FlappyCrixBridge.navigate(" + dx + "," + dy + ")");
        public void StartButton() => Exec("FlappyCrixBridge.startButton()");
        public void Select()
        {
            if (Screen == "playing") { Flap(); return; }
            Exec("FlappyCrixBridge.select()");
        }

        private Vector2 lastPointer = new Vector2(-1, -1);
        public void PointerMove(Vector2 uv)
        {
            if (!EngineUsable) return;
            Vector2 p = ToPixels(uv);
            if ((p - lastPointer).sqrMagnitude < 1f) return;
            lastPointer = p;
            Safe(() => EngineMouse(0, p));
        }
        public void PointerDown(Vector2 uv) { if (EngineUsable) { Vector2 p = ToPixels(uv); Safe(() => { EngineMouse(0, p); EngineMouse(1, p); }); } }
        public void PointerUp(Vector2 uv) { if (EngineUsable) { Vector2 p = ToPixels(uv); Safe(() => EngineMouse(2, p)); } }
        public void Scroll(Vector2 uv, int delta) { if (EngineUsable) { Vector2 p = ToPixels(uv); Safe(() => EngineWheel(p, delta)); } }

        private Vector2 ToPixels(Vector2 uv) =>
            new Vector2(Mathf.Clamp01(uv.x) * PageWidth, Mathf.Clamp01(uv.y) * PageHeight);

        protected void Exec(string js)
        {
            if (!EngineUsable) return;
            Safe(() => EngineExec("window.FlappyCrixBridge && " + js + ";"));
        }

        // ------------------------------------------------------------------ self test

        private SelfTestReport report;
        private float selfTestAt = -1f, selfTestCollectAt = -1f;
        private int framesAtSelfTest;
        private float selfTestStarted;

        private void BeginSelfTest()
        {
            selfTestAt = -1f;
            report = new SelfTestReport { Mode = ModeName, EngineStartSeconds = Time.realtimeSinceStartup - startTime };
            framesAtSelfTest = EngineFrames;
            selfTestStarted = Time.realtimeSinceStartup;
            // Input path: a real key and pointer move from Unity (Space on the menu does nothing).
            Safe(EngineKeySpace);
            PointerMove(new Vector2(0.5f, 0.02f));
            selfTestCollectAt = Time.realtimeSinceStartup + 1.5f;
        }

        private void FinishSelfTest(string json)
        {
            if (report == null) return;
            report.RawJson = json;
            report.Parse(json);
            float secs = Mathf.Max(0.1f, Time.realtimeSinceStartup - selfTestStarted);
            report.UnityTextureFps = Mathf.RoundToInt((EngineFrames - framesAtSelfTest) / secs);
            report.TextureChanging = EngineFrames > 0;
            report.Evaluate();
            foreach (var line in report.Lines()) Emit(line);
            try { File.WriteAllText(Path.Combine(DataFolder, "selftest.txt"), string.Join(Environment.NewLine, report.Lines())); } catch { }
            if (!report.Passed && config.AutoFallbackToNative.Value) Fail("Self test failed: " + report.FailureSummary);
        }

        // ------------------------------------------------------------------ plumbing

        protected void Fail(string why)
        {
            if (HasFailed) return;
            HasFailed = true;
            IsReady = false;
            FailureReason = why;
            Emit("WEBSITE MODE (" + EngineName + ") FAILED: " + why);
        }

        protected void Safe(Action a)
        {
            try { a(); }
            catch (Exception e) { Emit("Browser call failed: " + e.GetType().Name + ": " + e.Message); }
        }

        protected void Emit(string s) => Log?.Invoke(s);

        public void Dispose()
        {
            try { EngineDispose(); } catch (Exception e) { Emit("Browser shutdown: " + e.Message); }
            server?.Dispose();
            server = null;
        }
    }
}
