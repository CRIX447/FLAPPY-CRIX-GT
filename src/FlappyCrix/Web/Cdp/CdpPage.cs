// Drives one headless page over the DevTools protocol: injects the bridge before the
// site's own scripts, streams frames (screencast), sends real (trusted) keyboard/mouse
// input, receives bridge messages, and stops the page from ever leaving the game -
// any other document navigation is cancelled and reported, so it can be opened on
// the desktop instead.
// No UnityEngine references (tested outside Unity against headless Chromium).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Threading;

namespace FlappyCrix.Web.Cdp
{
    public sealed class CdpPage : IDisposable
    {
        public sealed class Options
        {
            public string BrowserExe;
            public string ProfileDir;
            public int Width = 768, Height = 960;
            public int JpegQuality = 80;
            public bool Mute;
            public string ExtraArgs = "";
            public int StartupTimeoutMs = 20000;
            /// <summary>Script that runs before the page's own scripts on every load (config + bridge).</summary>
            public string InjectScript;
            /// <summary>Name of the JS function the page calls to talk to us (string argument).</summary>
            public string BindingName = "flappyCrixSend";
            /// <summary>Main-frame documents allowed to load in the panel. Everything else is blocked.</summary>
            public Func<string, bool> AllowDocument = _ => true;
        }

        // All raised on background threads.
        public event Action<string> Log;
        public event Action Connected;
        public event Action<string> Failed;
        /// <summary>
        /// JPEG of the hidden window's visible area, at 1:1 scale, plus that area's size in page
        /// pixels. The page (Width x Height) is always its top-left corner: show only that part.
        /// Headless windows keep room for invisible toolbars, and that room changes between pages,
        /// so cropping here is more reliable than trying to size the window exactly.
        /// </summary>
        public event Action<byte[], int, int> Frame;
        public event Action<string> BridgeMessage;
        public event Action<string> NavigationBlocked;     // url
        public event Action<string> NavigationError;       // url + error, from Navigate()

        public bool IsConnected { get; private set; }
        public string BrowserVersion { get; private set; }
        public int FramesReceived => framesReceived;

        private readonly Options opt;
        private HeadlessBrowser browser;
        private CdpConnection cdp;
        private string mainFrameId;
        private int framesReceived;
        private volatile bool disposed;

        public CdpPage(Options options) { opt = options; }

        /// <summary>Launches and connects on a background thread; raises Connected or Failed.</summary>
        public void StartAsync(string firstUrl)
        {
            var t = new Thread(() =>
            {
                try { Start(firstUrl); }
                catch (Exception e) { if (!disposed) Failed?.Invoke(e.GetType().Name + ": " + e.Message); }
            }) { IsBackground = true, Name = "FlappyCrix-BrowserStart" };
            t.Start();
        }

        private void Start(string firstUrl)
        {
            browser = new HeadlessBrowser(s => Log?.Invoke(s));
            browser.Launch(opt.BrowserExe, opt.ProfileDir, opt.Width, opt.Height, opt.Mute, opt.ExtraArgs, opt.StartupTimeoutMs);
            if (disposed) return;

            cdp = new CdpConnection();
            cdp.OnEvent += OnEvent;
            cdp.OnClosed += why => { IsConnected = false; if (!disposed) Failed?.Invoke("The browser engine closed (" + why + ")."); };
            cdp.Connect(browser.Port, browser.PageWebSocketPath, 5000);

            const int T = 10000;
            var version = cdp.Call("Browser.getVersion", null, T);
            BrowserVersion = MiniJson.Str(version, "product");
            string ua = MiniJson.Str(version, "userAgent") ?? "";

            cdp.Call("Page.enable", null, T);
            cdp.Call("Runtime.enable", null, T);
            cdp.Call("Runtime.addBinding", MiniJson.Args("name", opt.BindingName), T);
            if (!string.IsNullOrEmpty(opt.InjectScript))
                cdp.Call("Page.addScriptToEvaluateOnNewDocument", MiniJson.Args("source", opt.InjectScript), T);
            cdp.Call("Emulation.setDeviceMetricsOverride", MiniJson.Args(
                "width", opt.Width, "height", opt.Height, "deviceScaleFactor", 1, "mobile", false), T);
            try { cdp.Call("Emulation.setFocusEmulationEnabled", MiniJson.Args("enabled", true), T); } catch { }
            // Present as the normal browser, not "HeadlessChrome"
            if (ua.Contains("Headless"))
                cdp.Call("Emulation.setUserAgentOverride", MiniJson.Args("userAgent", ua.Replace("HeadlessChrome", "Chrome")), T);

            var tree = MiniJson.Child(cdp.Call("Page.getFrameTree", null, T), "frameTree");
            mainFrameId = MiniJson.Str(MiniJson.Child(tree, "frame"), "id");

            // Gatekeeper: every main-frame document request is checked before it loads.
            cdp.Call("Fetch.enable", MiniJson.Args("patterns", new List<object> {
                MiniJson.Args("urlPattern", "*", "resourceType", "Document", "requestStage", "Request") }), T);

            // Large max size = frames at 1:1 (no downscaling); the receiver crops to the page.
            cdp.Call("Page.startScreencast", MiniJson.Args(
                "format", "jpeg", "quality", opt.JpegQuality, "maxWidth", 4096, "maxHeight", 4096, "everyNthFrame", 1), T);

            IsConnected = true;
            Log?.Invoke("Connected to " + BrowserVersion);
            Connected?.Invoke();
            if (!string.IsNullOrEmpty(firstUrl)) Navigate(firstUrl);
        }

        // ------------------------------------------------------------------ events

        private void OnEvent(string method, Dictionary<string, object> p)
        {
            switch (method)
            {
                case "Page.screencastFrame":
                {
                    object sid;
                    if (p.TryGetValue("sessionId", out sid))
                        cdp.Send("Page.screencastFrameAck", MiniJson.Args("sessionId", sid));
                    string data = MiniJson.Str(p, "data");
                    if (data == null) return;
                    byte[] jpeg;
                    try { jpeg = Convert.FromBase64String(data); } catch { return; }
                    var md = MiniJson.Child(p, "metadata");
                    int dw = (int)MiniJson.Num(md, "deviceWidth", opt.Width), dh = (int)MiniJson.Num(md, "deviceHeight", opt.Height);
                    Interlocked.Increment(ref framesReceived);
                    Frame?.Invoke(jpeg, dw, dh);
                    break;
                }
                case "Runtime.bindingCalled":
                    if (MiniJson.Str(p, "name") == opt.BindingName)
                        BridgeMessage?.Invoke(MiniJson.Str(p, "payload") ?? "");
                    break;

                case "Fetch.requestPaused":
                {
                    string requestId = MiniJson.Str(p, "requestId");
                    string url = MiniJson.Str(MiniJson.Child(p, "request"), "url") ?? "";
                    string frameId = MiniJson.Str(p, "frameId");
                    bool mainFrame = frameId == null || frameId == mainFrameId;
                    bool allow = !mainFrame || url.StartsWith("about:", StringComparison.Ordinal) ||
                                 url.StartsWith("data:", StringComparison.Ordinal) || SafeAllow(url);
                    if (allow)
                        cdp.Send("Fetch.continueRequest", MiniJson.Args("requestId", requestId));
                    else
                    {
                        cdp.Send("Fetch.failRequest", MiniJson.Args("requestId", requestId, "errorReason", "Aborted"));
                        NavigationBlocked?.Invoke(url);
                    }
                    break;
                }
                case "Page.javascriptDialogOpening":
                    // Nobody can answer a browser dialog in VR. The bridge replaces alert/confirm/prompt;
                    // this is the safety net so the page never freezes waiting for one.
                    cdp.Send("Page.handleJavaScriptDialog", MiniJson.Args("accept", false));
                    break;

                case "Inspector.targetCrashed":
                    Failed?.Invoke("The page crashed.");
                    break;
            }
        }

        private bool SafeAllow(string url)
        {
            try { return opt.AllowDocument(url); } catch { return false; }
        }

        // ------------------------------------------------------------------ commands

        public void Navigate(string url)
        {
            if (cdp == null) return;
            cdp.Send("Page.navigate", MiniJson.Args("url", url), (r, err) =>
            {
                string text = err ?? MiniJson.Str(r, "errorText");
                // net::ERR_ABORTED is our own gatekeeper cancelling a blocked page - not a failure
                if (!string.IsNullOrEmpty(text) && text != "net::ERR_ABORTED") NavigationError?.Invoke(url + " -> " + text);
            });
        }

        public void Evaluate(string js)
        {
            cdp?.Send("Runtime.evaluate", MiniJson.Args("expression", js, "userGesture", true));
        }

        /// <summary>Space bar, pressed and released, as a real key (the site's keydown handler sees it).</summary>
        public void KeySpace()
        {
            if (cdp == null) return;
            cdp.Send("Input.dispatchKeyEvent", MiniJson.Args("type", "keyDown", "key", " ", "code", "Space",
                "windowsVirtualKeyCode", 32, "nativeVirtualKeyCode", 32, "text", " ", "unmodifiedText", " "));
            cdp.Send("Input.dispatchKeyEvent", MiniJson.Args("type", "keyUp", "key", " ", "code", "Space",
                "windowsVirtualKeyCode", 32, "nativeVirtualKeyCode", 32));
        }

        /// <summary>kind: "mouseMoved", "mousePressed", "mouseReleased". CSS pixels from top-left.</summary>
        public void Mouse(string kind, double x, double y)
        {
            if (cdp == null) return;
            bool press = kind == "mousePressed", release = kind == "mouseReleased";
            cdp.Send("Input.dispatchMouseEvent", MiniJson.Args("type", kind, "x", x, "y", y,
                "button", press || release ? "left" : "none", "buttons", press ? 1 : 0, "clickCount", press || release ? 1 : 0));
        }

        public void Wheel(double x, double y, double deltaY)
        {
            cdp?.Send("Input.dispatchMouseEvent", MiniJson.Args("type", "mouseWheel", "x", x, "y", y, "deltaX", 0, "deltaY", deltaY));
        }

        public void Dispose()
        {
            disposed = true;
            IsConnected = false;
            try { cdp?.Send("Browser.close"); } catch { }
            try { cdp?.Dispose(); } catch { }
            try { browser?.Dispose(); } catch { }
        }
    }
}
