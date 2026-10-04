// Website mode using the PC's own Microsoft Edge (or Chrome/Brave) as a hidden engine:
// started headless (no window, nothing on the desktop) with its own profile in the mod
// folder, and streamed onto the in-game screen over the DevTools protocol (CdpPage).
// Nothing to install - Edge ships with Windows 10 and 11.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FlappyCrix.Web.Cdp;
using UnityEngine;

namespace FlappyCrix.Web
{
    public sealed class EdgeBrowserGame : BrowserGame
    {
        private readonly string browserExe;
        private CdpPage page;
        private Texture2D texture;
        private volatile bool usable;

        // latest frame from the socket thread
        private readonly object frameLock = new object();
        private byte[] pendingJpeg;
        private int pendingW, pendingH, framesUploaded;
        private float nextUpload;
        private Vector2 uvScale = Vector2.one, uvOffset = Vector2.zero;
        private double decodeMsTotal; private int decodes; private float nextPerfLog;

        public EdgeBrowserGame(FlappyCrixConfig config, string modFolder, string browserExe) : base(config, modFolder)
        {
            this.browserExe = browserExe;
        }

        public override string EngineName => "hidden " + Path.GetFileNameWithoutExtension(browserExe) + " (" + browserExe + ")";
        public override Texture PanelTexture => framesUploaded > 0 ? texture : null;
        public override Vector2 TextureScale => uvScale;
        public override Vector2 TextureOffset => uvOffset;
        protected override bool EngineUsable => usable;
        protected override int EngineFrames => framesUploaded;

        /// <summary>The browser the website engine would use, or null (no Edge/Chrome found).</summary>
        public static string FindBrowser(FlappyCrixConfig config, Action<string> log) =>
            HeadlessBrowser.Find(config.BrowserPath.Value, log);

        protected override void StartEngine(string startUrl)
        {
            texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            page = new CdpPage(new CdpPage.Options
            {
                BrowserExe = browserExe,
                ProfileDir = Path.Combine(DataFolder, "EdgeProfile"),
                Width = PageWidth,
                Height = PageHeight,
                JpegQuality = config.JpegQuality.Value,
                Mute = config.MuteWebAudio.Value,
                ExtraArgs = config.BrowserExtraArgs.Value,
                StartupTimeoutMs = config.EngineStartupTimeoutMs.Value,
                InjectScript = InjectScript,
                AllowDocument = IsGamePage,              // only the game page may load in the panel
            });
            page.Log += s => OnMain(() => Emit(s));
            page.Connected += () => { usable = true; EngineConnected(); };
            page.Failed += why => { usable = false; EngineFailed(why); };
            page.BridgeMessage += BridgeMessage;
            page.NavigationBlocked += LeftTheGame;
            page.NavigationError += LoadFailed;
            page.Frame += (jpeg, w, h) => { lock (frameLock) { pendingJpeg = jpeg; pendingW = w; pendingH = h; } };
            Emit("Website engine: hidden " + browserExe + " (no window), page " + PageWidth + "x" + PageHeight + " -> " + startUrl);
            page.StartAsync(startUrl);
        }

        protected override void EngineTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextUpload) return;
            byte[] jpeg; int w, h;
            lock (frameLock) { jpeg = pendingJpeg; w = pendingW; h = pendingH; pendingJpeg = null; }
            if (jpeg == null) return;
            nextUpload = now + 1f / Mathf.Clamp(config.BrowserFrameRate.Value, 5, 60);

            var sw = Stopwatch.StartNew();
            if (!ImageConversion.LoadImage(texture, jpeg, false)) return;
            decodeMsTotal += sw.Elapsed.TotalMilliseconds; decodes++;
            framesUploaded++;

            // The page is the top-left PageWidth x PageHeight of the frame (see CdpPage.Frame).
            float sx = w > 0 ? Mathf.Min(1f, PageWidth / (float)w) : 1f;
            float sy = h > 0 ? Mathf.Min(1f, PageHeight / (float)h) : 1f;
            uvScale = new Vector2(sx, sy);
            uvOffset = new Vector2(0f, 1f - sy);       // texture is upright: the top is v = 1

            if (now >= nextPerfLog && decodes > 0)
            {
                if (nextPerfLog > 0) Emit("Screen stream: " + (decodes / 30f).ToString("0") + " frames/s, " + (decodeMsTotal / decodes).ToString("0.0") + " ms per frame to decode");
                nextPerfLog = now + 30f; decodeMsTotal = 0; decodes = 0;
            }
        }

        protected override void EngineKeySpace() => page?.KeySpace();

        protected override void EngineMouse(int kind, Vector2 px) =>
            page?.Mouse(kind == 1 ? "mousePressed" : kind == 2 ? "mouseReleased" : "mouseMoved", px.x, px.y);

        protected override void EngineWheel(Vector2 px, int delta) => page?.Wheel(px.x, px.y, -delta);
        protected override void EngineExec(string js) => page?.Evaluate(js);
        protected override void EngineNavigate(string url) => page?.Navigate(url);

        protected override void EngineDispose()
        {
            usable = false;
            page?.Dispose();
            page = null;
        }
    }
}
