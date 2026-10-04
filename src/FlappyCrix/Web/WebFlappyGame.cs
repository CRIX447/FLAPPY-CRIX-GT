// Website mode: the real Flappy Crix (HTML/JS) running in UnityWebBrowser's
// out-of-process CEF engine, painted into a Texture2D that the panel shows.
//
// Every UnityWebBrowser type is confined to this file (and FlappyCrixCefEngine),
// so if the UWB DLLs are missing only this class fails to load and the
// controller falls back to the native game.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using VoltstroStudios.UnityWebBrowser.Communication;
using VoltstroStudios.UnityWebBrowser.Core;
using VoltstroStudios.UnityWebBrowser.Logging;
using VoltstroStudios.UnityWebBrowser.Shared;
using VoltstroStudios.UnityWebBrowser.Shared.Events;
using VoltstroStudios.UnityWebBrowser.Shared.Popups;

namespace FlappyCrix.Web
{
    public sealed class WebFlappyGame : IFlappyGame
    {
        public event Action<string> Log;

        public string ModeName => remoteActive ? "Website (live: " + config.RemoteUrl.Value + ")" : "Website (packaged copy)";
        public bool IsReady { get; private set; }
        public bool HasFailed { get; private set; }
        public string FailureReason { get; private set; }
        public Texture PanelTexture => client?.BrowserTexture;
        public float Aspect => (float)config.ResolutionWidth.Value / config.ResolutionHeight.Value;
        public string Screen { get; private set; } = "menu";
        public int Score { get; private set; }

        /// <summary>Raised on the main thread with the self-test report.</summary>
        public event Action<SelfTestReport> SelfTestCompleted;

        private readonly FlappyCrixConfig config;
        private readonly string modFolder;
        private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
        private readonly string bridgeSource;
        private readonly string configJson;

        private LocalWebServer server;
        private WebBrowserClient client;
        private bool connected, remoteActive, bridgeReady;
        private float startTime, remoteDeadline = -1f, readyDeadline;
        private WindowsKey[] pendingKeyUp;
        private Vector2 lastPointer;

        // self test bookkeeping
        private SelfTestReport report;
        private float selfTestAt = -1f;
        private int textureHashChanges;
        private int lastTextureHash;
        private float textureSampleNext;

        public WebFlappyGame(FlappyCrixConfig config, string modFolder)
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

        private string WebRoot => Path.Combine(modFolder, "Web");
        private string EngineFolder => Path.Combine(modFolder, "UWB");
        private static string Js(bool b) => b ? "true" : "false";

        // ------------------------------------------------------------------ start

        public void Start()
        {
            startTime = Time.realtimeSinceStartup;
            readyDeadline = startTime + config.EngineStartupTimeoutMs.Value / 1000f + 20f;

            // 1. Pre-flight: never claim website support just because files exist,
            //    but do refuse early if they obviously don't.
            if (!File.Exists(Path.Combine(EngineFolder, FlappyCrixCefEngine.ExeName)))
            { Fail("Browser engine not found at " + Path.Combine(EngineFolder, FlappyCrixCefEngine.ExeName) + ". See INSTALL in README.md."); return; }
            if (bridgeSource == null)
            { Fail("Web/__flappycrix/bridge.js is missing from the mod folder."); return; }
            if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
            { Fail("The packaged browser engine is Windows x64 only."); return; }

            // 2. UniTask (used inside UnityWebBrowser) normally hooks Unity's player loop from
            //    [RuntimeInitializeOnLoadMethod], which never runs for assemblies BepInEx loads later.
            UniTaskBootstrap.EnsureInitialised(Emit);

            // 3. Local site server (default) - started even in remote mode, as the fallback.
            string startUrl;
            try
            {
                server = new LocalWebServer(WebRoot, "flappycrix.html", configJson, Emit);
                server.Start(config.LocalPort.Value);
                startUrl = server.EntryUrl;
            }
            catch (Exception e)
            {
                Fail("Could not serve the packaged website: " + e.Message);
                return;
            }

            if (config.UseRemoteWebsite.Value)
            {
                startUrl = config.RemoteUrl.Value;
                remoteActive = true;
                remoteDeadline = Time.realtimeSinceStartup + config.EngineStartupTimeoutMs.Value / 1000f + config.RemoteTimeoutSeconds.Value;
            }

            // 4. Browser client
            try
            {
                CreateClient(startUrl);
            }
            catch (Exception e)
            {
                Fail("UnityWebBrowser failed to start: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void CreateClient(string startUrl)
        {
            var engine = ScriptableObject.CreateInstance<FlappyCrixCefEngine>();
            engine.EngineFolder = EngineFolder;
            engine.hideFlags = HideFlags.HideAndDontSave;

            var tcp = ScriptableObject.CreateInstance<TCPCommunicationLayer>();
            tcp.hideFlags = HideFlags.HideAndDontSave;
            // Ports next to the web server, away from UWB's default 5555/5556
            tcp.inPort = server.Port + 1;
            tcp.outPort = server.Port + 2;
            tcp.connectionTimeout = config.EngineStartupTimeoutMs.Value;

            client = new WebBrowserClient
            {
                engine = engine,
                communicationLayer = tcp,
                initialUrl = startUrl,
                javascript = true,
                localStorage = true,           // the game's save lives here
                cache = true,
                incognitoMode = false,
                popupAction = PopupAction.Ignore,
                backgroundColor = new Color32(0, 0, 0, 255),
                windowlessFrameRate = Mathf.Clamp(config.BrowserFrameRate.Value, 1, 60),
                engineStartupTimeout = config.EngineStartupTimeoutMs.Value,
                remoteDebugging = config.RemoteDebugging.Value,
                logSeverity = LogSeverity.Warn
            };
            client.Logger = new BepInExUwbLogger(Emit);
            client.jsMethodManager.jsMethodsEnable = true;

            // Resolution's public setter resizes a running browser and throws before Init.
            var resField = typeof(WebBrowserClient).GetField("resolution", BindingFlags.Instance | BindingFlags.NonPublic);
            resField?.SetValue(client, new Resolution((uint)config.ResolutionWidth.Value, (uint)config.ResolutionHeight.Value));

            // Keep logs and the browser profile (with the game's save) inside the mod folder.
            Directory.CreateDirectory(Path.Combine(modFolder, "BrowserData"));
            client.CachePath = new FileInfo(Path.Combine(modFolder, "BrowserData", "Profile"));
            client.LogPath = new FileInfo(Path.Combine(modFolder, "BrowserData", "engine.log"));

            client.RegisterJsMethod<string>("FlappyCrixEvent", msg => mainThread.Enqueue(() => OnBridgeEvent(msg)));
            client.OnClientConnected += () => mainThread.Enqueue(OnConnected);
            client.OnLoadFinish += url => mainThread.Enqueue(() => OnLoadFinish(url));
            client.OnUrlChanged += url => mainThread.Enqueue(() => OnUrlChanged(url));

            Emit("Starting UnityWebBrowser (CEF) at " + config.ResolutionWidth.Value + "x" + config.ResolutionHeight.Value + " -> " + startUrl);
            client.Init();
        }

        // ------------------------------------------------------------------ events

        private void OnConnected()
        {
            connected = true;
            Emit("Browser engine connected after " + (Time.realtimeSinceStartup - startTime).ToString("0.0") + " s");
            if (config.MuteWebAudio.Value) Safe(() => client.AudioMute(true));
        }

        private void OnLoadFinish(string url)
        {
            Emit("Page loaded: " + url);
            // The live site doesn't contain the bridge, so inject it after load.
            if (remoteActive && !url.StartsWith(server.BaseUrl, StringComparison.Ordinal))
                Safe(() => client.ExecuteJs("window.__FLAPPYCRIX_CONFIG=" + configJson + ";\n" + bridgeSource));
        }

        private void OnUrlChanged(string url)
        {
            if (url.StartsWith("about:", StringComparison.Ordinal) || url.StartsWith("data:", StringComparison.Ordinal)) return;
            if (url.StartsWith("chrome-error:", StringComparison.Ordinal))
            {
                if (remoteActive) FallBackToLocal("the live site could not be reached");
                return;
            }

            // Only the game page stays in the panel. Anything else the page navigates to
            // (Discord sign-in, another site page, a social link that slipped past the bridge)
            // opens in the PC's desktop browser, and the panel goes back to the game.
            if (IsGamePage(url)) return;

            string desktop = url;
            if (server != null && url.StartsWith(server.BaseUrl, StringComparison.Ordinal))
                desktop = SiteOrigin(config.RemoteUrl.Value) + url.Substring(server.BaseUrl.Length);
            OpenOnDesktop(desktop);
            Safe(() => client.LoadUrl(remoteActive ? config.RemoteUrl.Value : server.EntryUrl));
        }

        /// <summary>The Flappy Crix game page, live or packaged. Everything else opens on the desktop.</summary>
        private bool IsGamePage(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            string path = u.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            bool gamePath = path == "/flappycrix" || path == "/flappycrix.html" || path == "";
            if (server != null && url.StartsWith(server.BaseUrl, StringComparison.Ordinal)) return gamePath;
            return SameSite(url, config.RemoteUrl.Value) && gamePath && path != "";
        }

        private static string SiteOrigin(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Scheme + "://" + u.Authority : "https://crixgamingvr.com";
        }

        private float lastDesktopOpen = -10f;
        private int desktopOpensThisMinute;
        private float minuteStart;

        /// <summary>
        /// Opens a link in the player's normal desktop browser (Application.OpenURL).
        /// http/https only, at most one per second and ten a minute, so a page can't spam tabs.
        /// </summary>
        private void OpenOnDesktop(string url)
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
                    Emit("Bridge " + arg + " ready (" + ModeName + ") after " + (Time.realtimeSinceStartup - startTime).ToString("0.0") + " s");
                    if (config.RunSelfTest.Value && report == null) selfTestAt = Time.realtimeSinceStartup + 3f;
                    break;
                case "ScoreChanged":
                    int s; if (int.TryParse(arg, out s)) Score = s;
                    break;
                case "Screen":
                    Screen = arg;
                    break;
                case "GameStarted":
                    Score = 0;
                    break;
                case "GameOver":
                    Emit("Game over, score " + arg);
                    break;
                case "OpenExternal":
                    OpenOnDesktop(arg);
                    break;
                case "SelfTest":
                    FinishSelfTest(arg);
                    break;
            }
        }

        // ------------------------------------------------------------------ per frame

        public void Tick()
        {
            Action a;
            while (mainThread.TryDequeue(out a)) Safe(a);
            if (HasFailed || client == null) return;

            // Same work UWB's own BaseUwbClientManager does every frame.
            Safe(() => { client.UpdateFps(); client.LoadTextureData(); });

            if (pendingKeyUp != null && Usable)
            {
                var up = pendingKeyUp; pendingKeyUp = null;
                Safe(() => client.SendKeyboardControls(new WindowsKey[0], up, new char[0]));
            }

            float now = Time.realtimeSinceStartup;
            if (remoteActive && remoteDeadline > 0 && now > remoteDeadline && !bridgeReady)
                FallBackToLocal("the live site did not load within " + config.RemoteTimeoutSeconds.Value + " s");

            if (!bridgeReady && now > readyDeadline)
                Fail(connected ? "The page loaded but the game never reported ready (JavaScript/bridge problem)."
                               : "The browser engine did not connect (check BrowserData/engine.log).");

            if (selfTestAt > 0 && now >= selfTestAt) BeginSelfTest();
            SampleTexture(now);
        }

        private bool Usable => client != null && connected && client.ReadySignalReceived && client.IsConnected;

        // ------------------------------------------------------------------ input bridge

        /// <summary>
        /// While playing, a real Space key goes to the page (the site's own keydown handler
        /// calls jump(), and a trusted key press also lets the music start). Off the
        /// playfield, the bridge does what the site's tap-to-play does: start / retry.
        /// </summary>
        public void Flap()
        {
            if (!Usable) return;
            SendKey(WindowsKey.Space);
            if (Screen != "playing") Exec("FlappyCrixBridge.flap()");
        }

        public void Restart() => Exec("FlappyCrixBridge.restart()");
        public void TogglePause() => Exec("FlappyCrixBridge.togglePause()");
        public void Back() => Exec("FlappyCrixBridge.back()");
        public void Navigate(int dx, int dy) => Exec("FlappyCrixBridge.navigate(" + dx + "," + dy + ")");
        public void StartButton() => Exec("FlappyCrixBridge.startButton()");

        /// <summary>While playing, SELECT is a flap (real Space key); otherwise it activates the highlight.</summary>
        public void Select()
        {
            if (Screen == "playing") { Flap(); return; }
            Exec("FlappyCrixBridge.select()");
        }

        public void PointerMove(Vector2 uv)
        {
            if (!Usable) return;
            Vector2 p = ToPixels(uv);
            if ((p - lastPointer).sqrMagnitude < 1f) return;
            lastPointer = p;
            Safe(() => client.SendMouseMove(p));
        }

        public void PointerDown(Vector2 uv)
        {
            if (!Usable) return;
            Vector2 p = ToPixels(uv);
            Safe(() => { client.SendMouseMove(p); client.SendMouseClick(p, 1, MouseClickType.Left, MouseEventType.Down); });
        }

        public void PointerUp(Vector2 uv)
        {
            if (!Usable) return;
            Vector2 p = ToPixels(uv);
            Safe(() => client.SendMouseClick(p, 1, MouseClickType.Left, MouseEventType.Up));
        }

        public void Scroll(Vector2 uv, int delta)
        {
            if (!Usable) return;
            Vector2 p = ToPixels(uv);
            Safe(() => client.SendMouseScroll(p, delta));
        }

        private Vector2 ToPixels(Vector2 uv) =>
            new Vector2(Mathf.Clamp01(uv.x) * config.ResolutionWidth.Value, Mathf.Clamp01(uv.y) * config.ResolutionHeight.Value);

        private void SendKey(WindowsKey key)
        {
            Safe(() => client.SendKeyboardControls(new[] { key }, new WindowsKey[0], new char[0]));
            pendingKeyUp = new[] { key };     // released next frame
        }

        private void Exec(string js)
        {
            if (!Usable) return;
            Safe(() => client.ExecuteJs("window.FlappyCrixBridge && " + js + ";"));
        }

        // ------------------------------------------------------------------ remote fallback

        private void FallBackToLocal(string why)
        {
            if (!remoteActive) return;
            Emit("Live site unavailable (" + why + "); loading the packaged copy instead.");
            remoteActive = false;
            remoteDeadline = -1f;
            bridgeReady = false;
            IsReady = false;
            readyDeadline = Time.realtimeSinceStartup + 25f;
            if (client != null && connected) Safe(() => client.LoadUrl(server.EntryUrl));
        }

        /// <summary>Same site, allowing www./apex redirects (crixgamingvr.com vs www.crixgamingvr.com).</summary>
        private static bool SameSite(string a, string b)
        {
            Uri ua, ub;
            if (!Uri.TryCreate(a, UriKind.Absolute, out ua) || !Uri.TryCreate(b, UriKind.Absolute, out ub)) return false;
            string ha = ua.Host.ToLowerInvariant(), hb = ub.Host.ToLowerInvariant();
            if (ha.StartsWith("www.")) ha = ha.Substring(4);
            if (hb.StartsWith("www.")) hb = hb.Substring(4);
            return ha == hb;
        }

        // ------------------------------------------------------------------ self test

        private void BeginSelfTest()
        {
            selfTestAt = -1f;
            report = new SelfTestReport { Mode = ModeName };
            report.EngineStartSeconds = Time.realtimeSinceStartup - startTime;
            // Input path: a real key event and pointer movement from Unity. Space on the menu is
            // harmless (the site's jump() ignores it when no run is active).
            SendKey(WindowsKey.Space);
            PointerMove(new Vector2(0.5f, 0.02f));
            // Collect results a moment later so the input events have arrived.
            selfTestCollectAt = Time.realtimeSinceStartup + 1.5f;
        }

        private float selfTestCollectAt = -1f;

        private void SampleTexture(float now)
        {
            if (selfTestCollectAt > 0 && now >= selfTestCollectAt)
            {
                selfTestCollectAt = -1f;
                Exec("uwb.ExecuteJsMethod('FlappyCrixEvent','SelfTest:'+FlappyCrixBridge.selfTest())");
            }

            // Is the texture actually changing? (proves frames arrive from CEF)
            if (now < textureSampleNext || client?.BrowserTexture == null || !connected) return;
            textureSampleNext = now + 0.5f;
            try
            {
                var tex = client.BrowserTexture;
                int h = 17;
                for (int i = 1; i <= 8; i++)
                {
                    Color32 c = tex.GetPixel(tex.width * i / 9, tex.height * ((i * 5) % 9 + 1) / 10);
                    h = h * 31 + c.r * 7 + c.g * 13 + c.b;
                }
                if (lastTextureHash != 0 && h != lastTextureHash) textureHashChanges++;
                lastTextureHash = h;
            }
            catch { /* texture not readable on this platform: skip */ }
        }

        private void FinishSelfTest(string json)
        {
            if (report == null) return;
            report.RawJson = json;
            report.Parse(json);
            report.UnityTextureFps = client != null ? client.FPS : 0;
            report.TextureChanging = textureHashChanges > 0;
            report.Evaluate();
            foreach (var line in report.Lines()) Emit(line);
            try { File.WriteAllText(Path.Combine(modFolder, "BrowserData", "selftest.txt"), string.Join(Environment.NewLine, report.Lines())); } catch { }
            SelfTestCompleted?.Invoke(report);
            if (!report.Passed && config.AutoFallbackToNative.Value)
                Fail("Self test failed: " + report.FailureSummary);
        }

        // ------------------------------------------------------------------ plumbing

        private void Fail(string why)
        {
            if (HasFailed) return;
            HasFailed = true;
            IsReady = false;
            FailureReason = why;
            Emit("WEBSITE MODE FAILED: " + why);
        }

        private void Safe(Action a)
        {
            try { a(); }
            catch (Exception e) { Emit("Browser call failed: " + e.GetType().Name + ": " + e.Message); }
        }

        private void Emit(string s) => Log?.Invoke(s);

        public void Dispose()
        {
            try { client?.Dispose(); } catch (Exception e) { Emit("Browser shutdown: " + e.Message); }
            client = null;
            server?.Dispose();
            server = null;
        }

        /// <summary>Routes UnityWebBrowser's own logging into the BepInEx log.</summary>
        private sealed class BepInExUwbLogger : IWebBrowserLogger
        {
            private readonly Action<string> log;
            public BepInExUwbLogger(Action<string> log) { this.log = log; }
            public void Debug(object message) { }
            public void Warn(object message) => log("[UWB] " + message);
            public void Error(object message) => log("[UWB ERROR] " + message);
        }
    }
}
