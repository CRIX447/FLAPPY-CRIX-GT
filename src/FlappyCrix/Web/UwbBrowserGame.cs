// Website mode using UnityWebBrowser (out-of-process CEF) - optional alternative to the
// hidden Edge engine, for people who install UWB (see README). Every UWB type is confined
// to this file and FlappyCrixCefEngine, so without the UWB DLLs only this class fails to
// load and the controller moves on.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System.IO;
using System.Reflection;
using UnityEngine;
using VoltstroStudios.UnityWebBrowser.Communication;
using VoltstroStudios.UnityWebBrowser.Core;
using VoltstroStudios.UnityWebBrowser.Logging;
using VoltstroStudios.UnityWebBrowser.Shared;
using VoltstroStudios.UnityWebBrowser.Shared.Events;
using VoltstroStudios.UnityWebBrowser.Shared.Popups;

namespace FlappyCrix.Web
{
    public sealed class UwbBrowserGame : BrowserGame
    {
        private WebBrowserClient client;
        private bool connected, keyUpPending;
        private int textureChanges, lastHash;
        private float nextSample;

        public UwbBrowserGame(FlappyCrixConfig config, string modFolder) : base(config, modFolder) { }

        public override string EngineName => "UnityWebBrowser (CEF)";
        public override Texture PanelTexture => client?.BrowserTexture;
        // CEF paints top-down: flip V
        public override Vector2 TextureScale => new Vector2(1, -1);
        public override Vector2 TextureOffset => new Vector2(0, 1);
        protected override bool EngineUsable => client != null && connected && client.ReadySignalReceived && client.IsConnected;
        protected override int EngineFrames => textureChanges;

        private string EngineFolder => Path.Combine(modFolder, "UWB");

        public static bool IsInstalled(string modFolder) =>
            File.Exists(Path.Combine(modFolder, "VoltstroStudios.UnityWebBrowser.dll")) &&
            File.Exists(Path.Combine(modFolder, "UWB", FlappyCrixCefEngine.ExeName));

        protected override void StartEngine(string startUrl)
        {
            if (!File.Exists(Path.Combine(EngineFolder, FlappyCrixCefEngine.ExeName)))
            { Fail("UnityWebBrowser engine not found at " + EngineFolder); return; }
            UniTaskBootstrap.EnsureInitialised(Emit);

            var engine = ScriptableObject.CreateInstance<FlappyCrixCefEngine>();
            engine.EngineFolder = EngineFolder;
            engine.hideFlags = HideFlags.HideAndDontSave;
            var tcp = ScriptableObject.CreateInstance<TCPCommunicationLayer>();
            tcp.hideFlags = HideFlags.HideAndDontSave;
            tcp.inPort = server.Port + 1;
            tcp.outPort = server.Port + 2;
            tcp.connectionTimeout = config.EngineStartupTimeoutMs.Value;

            client = new WebBrowserClient
            {
                engine = engine,
                communicationLayer = tcp,
                initialUrl = startUrl,
                javascript = true,
                localStorage = true,
                cache = true,
                incognitoMode = false,
                popupAction = PopupAction.Ignore,
                backgroundColor = new Color32(0, 0, 0, 255),
                windowlessFrameRate = Mathf.Clamp(config.BrowserFrameRate.Value, 1, 60),
                engineStartupTimeout = config.EngineStartupTimeoutMs.Value,
                remoteDebugging = config.RemoteDebugging.Value,
                logSeverity = LogSeverity.Warn
            };
            client.Logger = new UwbLogger(s => OnMain(() => Emit(s)));
            client.jsMethodManager.jsMethodsEnable = true;
            // Resolution's public setter resizes a running browser and throws before Init.
            typeof(WebBrowserClient).GetField("resolution", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(client, new Resolution((uint)PageWidth, (uint)PageHeight));
            client.CachePath = new FileInfo(Path.Combine(DataFolder, "Profile"));
            client.LogPath = new FileInfo(Path.Combine(DataFolder, "engine.log"));

            client.RegisterJsMethod<string>("FlappyCrixEvent", BridgeMessage);
            client.OnClientConnected += () => { connected = true; EngineConnected(); };
            client.OnLoadFinish += url => OnMain(() =>
            {
                // The live site doesn't contain the bridge, so inject it after load.
                if (remoteActive && !url.StartsWith(server.BaseUrl, System.StringComparison.Ordinal))
                    Safe(() => client.ExecuteJs(InjectScript));
            });
            client.OnUrlChanged += url => OnMain(() => OnUrlChanged(url));
            Emit("Website engine: UnityWebBrowser at " + PageWidth + "x" + PageHeight + " -> " + startUrl);
            client.Init();
        }

        private void OnUrlChanged(string url)
        {
            if (url.StartsWith("about:") || url.StartsWith("data:")) return;
            if (url.StartsWith("chrome-error:")) { LoadFailed(url); return; }
            if (IsGamePage(url)) return;
            // UWB can't stop a navigation before it happens: open it on the desktop, go back.
            LeftTheGame(url);
            Safe(() => client.LoadUrl(remoteActive ? config.RemoteUrl.Value : server.EntryUrl));
        }

        protected override void EngineTick()
        {
            if (client == null) return;
            Safe(() => { client.UpdateFps(); client.LoadTextureData(); });
            if (keyUpPending && EngineUsable)
            {
                keyUpPending = false;
                Safe(() => client.SendKeyboardControls(new WindowsKey[0], new[] { WindowsKey.Space }, new char[0]));
            }
            // Count picture changes (proves frames arrive from CEF) for the self test.
            float now = Time.realtimeSinceStartup;
            if (now < nextSample || client.BrowserTexture == null || !connected) return;
            nextSample = now + 0.5f;
            try
            {
                var tex = client.BrowserTexture;
                int h = 17;
                for (int i = 1; i <= 8; i++)
                {
                    Color32 c = tex.GetPixel(tex.width * i / 9, tex.height * ((i * 5) % 9 + 1) / 10);
                    h = h * 31 + c.r * 7 + c.g * 13 + c.b;
                }
                if (lastHash != 0 && h != lastHash) textureChanges++;
                lastHash = h;
            }
            catch { }
        }

        protected override void EngineKeySpace()
        {
            client.SendKeyboardControls(new[] { WindowsKey.Space }, new WindowsKey[0], new char[0]);
            keyUpPending = true;     // released next frame
        }

        protected override void EngineMouse(int kind, Vector2 px)
        {
            if (kind == 0) client.SendMouseMove(px);
            else client.SendMouseClick(px, 1, MouseClickType.Left, kind == 1 ? MouseEventType.Down : MouseEventType.Up);
        }

        protected override void EngineWheel(Vector2 px, int delta) => client.SendMouseScroll(px, delta);
        protected override void EngineExec(string js) => client.ExecuteJs(js);
        protected override void EngineNavigate(string url) => client.LoadUrl(url);

        protected override void EngineDispose()
        {
            try { client?.Dispose(); } catch { }
            client = null;
        }

        private sealed class UwbLogger : IWebBrowserLogger
        {
            private readonly System.Action<string> log;
            public UwbLogger(System.Action<string> log) { this.log = log; }
            public void Debug(object message) { }
            public void Warn(object message) => log("[UWB] " + message);
            public void Error(object message) => log("[UWB ERROR] " + message);
        }
    }
}
