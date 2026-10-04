using System;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Logging;
using FlappyCrix.Native;
using FlappyCrix.VR;
using UnityEngine;

namespace FlappyCrix
{
    /// <summary>
    /// Owns the "station" (screen + arcade deck), picks Website or Native mode, and turns
    /// every input into game actions:
    ///
    ///   Arcade deck: joystick -> menu navigation, FLAP / SELECT / START / PAUSE buttons
    ///   Controllers: Y = open in front of you, B = hide, X / A = flap, laser + trigger = click
    ///   Keyboard:    SPACE flap, arrows navigate, ENTER select, F5 start, P pause, F8 show/hide, F9 bring here
    ///        |
    ///   IFlappyGame = EdgeBrowserGame (the real website, PC's Edge/Chrome running hidden)
    ///              or UwbBrowserGame (the real website, optional UnityWebBrowser engine)
    ///              or NativeFlappyGame (offline fallback)
    ///
    /// Portable: it opens in front of you when you load in and every time you press Y,
    /// then stays put (it never drifts after you). B hides it.
    /// </summary>
    public sealed class FlappyCrixController : MonoBehaviour
    {
        public FlappyCrixConfig Config;
        public ManualLogSource Logger;
        public string ModFolder;

        private IFlappyGame game;
        private Transform station;
        private GamePanel panel;
        private ArcadeDeck deck;
        private LaserPointer laser;
        private readonly XRRig rig = new XRRig();
        private bool placed;
        private readonly System.Collections.Generic.List<string> engineQueue = new System.Collections.Generic.List<string>();

        // edge detection
        private bool prevLPrimary, prevRPrimary, prevLStickUp, prevRStickUp, prevRSecondary, prevLSecondary;

        // Shown on the screen while the website loads, or if nothing could start.
        private Texture2D statusTexture;
        private uint[] statusPixels;
        private string statusShown;
        private float engineStartedAt;
        private readonly System.Collections.Generic.HashSet<string> loggedErrors = new System.Collections.Generic.HashSet<string>();

        // The optional UnityWebBrowser engine's files. Checked by name only: touching any UWB
        // class here would make Mono fail to compile this whole class when UWB isn't installed
        // (it isn't, by default) - which is exactly what hid the screen in the 1.0.0 test build.
        private const string UwbDll = "VoltstroStudios.UnityWebBrowser.dll";
        private const string UwbEngineExe = "UnityWebBrowser.Engine.Cef.exe";

        private void Start()
        {
            Logger.LogInfo("Building the screen and deck");
            // Set before anything is drawn, so the shader choice is logged and the override applies.
            Visuals.Log = m => Logger.LogInfo(m);
            Visuals.ShaderOverride = Config.ShaderOverride.Value;
            if (Config.UpgradedFromOlderConfig)
                Logger.LogInfo("Display settings set to the current defaults (screen " + Config.Width.Value + " m wide, " + Config.Distance.Value + " m away).");

            // Each part on its own: if one can't be built, the others still appear.
            station = new GameObject("FlappyCrixStation").transform;
            DontDestroyOnLoad(station.gameObject);
            station.gameObject.SetActive(false);
            try
            {
                panel = new GamePanel(station);
                panel.Root.localPosition = new Vector3(0, Config.HeightOffset.Value, Config.Distance.Value);
            }
            catch (Exception e) { Logger.LogError("Could not build the screen: " + e); }
            if (Config.DeckEnabled.Value)
            {
                try
                {
                    deck = new ArcadeDeck(station);
                    deck.Root.localPosition = Config.DeckOffset.Value;
                    deck.Joystick.NavAngle = Config.JoystickNavAngle.Value;
                }
                catch (Exception e) { Logger.LogError("Could not build the arcade deck: " + e); deck = null; }
            }
            // Deck buttons are pressed with the index fingertip
            rig.TipOffset = Config.FingertipOffset.Value;
            if (Config.UseGameFingertips.Value) rig.GameTips = new GameFingertips { Log = m => Logger.LogInfo(m) };

            try { laser = new LaserPointer { PitchDegrees = Config.LaserPitch.Value }; }
            catch (Exception e) { Logger.LogError("Could not build the laser pointer: " + e); laser = null; }

            // Engines to try, in order. Each one that fails hands over to the next.
            if (Config.UseWebsite.Value)
            {
                if (Config.Engine.Value != WebEngine.UnityWebBrowser) engineQueue.Add("edge");
                if (Config.Engine.Value != WebEngine.SystemBrowser) engineQueue.Add("uwb");
            }
            engineQueue.Add("native");
            StartNextEngine(Config.UseWebsite.Value ? null : "UseWebsite = false");
        }

        // ------------------------------------------------------------------ modes

        private void StartNextEngine(string whyPreviousFailed)
        {
            if (game != null) { try { game.Dispose(); } catch { } game = null; }
            if (whyPreviousFailed != null) Logger.LogWarning("Switching engine: " + whyPreviousFailed);

            while (engineQueue.Count > 0)
            {
                string next = engineQueue[0];
                engineQueue.RemoveAt(0);
                try
                {
                    // Every engine is created in its own [NoInlining] method, so an engine whose
                    // code can't load only fails its own call - caught here - and never this method.
                    IFlappyGame g;
                    if (next == "edge") g = CreateEdgeGame();
                    else if (next == "uwb") g = UwbFilesPresent() ? CreateUwbGame() : null;
                    else
                    {
                        if (!Config.AutoFallbackToNative.Value && Config.UseWebsite.Value)
                        { Logger.LogError("No website engine could run and AutoFallbackToNative = false."); return; }
                        g = CreateNativeGame();
                    }
                    if (g == null) continue;
                    game = g;
                    engineStartedAt = Time.realtimeSinceStartup;
                    Logger.LogInfo("Mode: " + game.ModeName);
                    return;
                }
                catch (Exception e)
                {
                    // TypeLoadException / FileNotFoundException when an engine's DLLs are missing or mismatched
                    Logger.LogError(next + " engine unavailable: " + e.GetType().Name + ": " + e.Message);
                    if (game != null) { try { game.Dispose(); } catch { } }
                    game = null;
                }
            }
            Logger.LogError("Flappy Crix could not start any engine. The screen shows this message; details above.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private IFlappyGame CreateEdgeGame()
        {
            string exe = Web.EdgeBrowserGame.FindBrowser(Config, m => Logger.LogInfo(m));
            if (exe == null) { Logger.LogWarning("No Microsoft Edge or Chrome found on this PC (set BrowserPath in the config to use another Chromium browser)."); return null; }
            var web = new Web.EdgeBrowserGame(Config, ModFolder, exe);
            web.Log += m => Logger.LogInfo(m);
            web.Start();
            return web;
        }

        private bool UwbFilesPresent() =>
            File.Exists(Path.Combine(ModFolder, UwbDll)) && File.Exists(Path.Combine(ModFolder, "UWB", UwbEngineExe));

        [MethodImpl(MethodImplOptions.NoInlining)]
        private IFlappyGame CreateUwbGame()
        {
            var web = new Web.UwbBrowserGame(Config, ModFolder);   // UWB types are only JIT-loaded here
            web.Log += m => Logger.LogInfo(m);
            web.Start();
            return web;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private IFlappyGame CreateNativeGame()
        {
            var native = new NativeFlappyGame(panel != null ? panel.Root : station, ModFolder, this);
            native.Log += m => Logger.LogInfo(m);
            return native;
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            Guard("controllers", rig.Update);
            if (station == null) return;

            // Open in front of you as soon as the VR camera exists - before anything else, so
            // the screen shows up even if an engine is still starting or has failed.
            if (!placed && rig.Head != null)
            {
                placed = true;
                PlaceHere();
                station.gameObject.SetActive(Config.ShowOnStart.Value);
                Logger.LogInfo("Screen opened in front of you (B hides it, Y brings it back)");
            }
            Guard("system input", HandleSystemInput);

            if (game != null)
            {
                try { game.Tick(); }
                catch (Exception e) { LogOnce("tick", "Engine error: " + e); StartNextEngine(game.ModeName + " stopped with " + e.GetType().Name + ": " + e.Message); }
                if (game != null && game.HasFailed) StartNextEngine(game.FailureReason);
            }

            Guard("picture", ShowPicture);
            if (!Visible || game == null) return;
            Guard("game input", HandleGameInput);
        }

        /// <summary>The engine's picture, or a loading / error message while there is none.</summary>
        private void ShowPicture()
        {
            if (panel == null) return;
            Texture tex = game != null ? game.PanelTexture : null;
            if (tex != null)
            {
                panel.SetSize(Config.Width.Value, game.Aspect);
                panel.ShowTexture(tex, game.TextureScale, game.TextureOffset);
                return;
            }
            panel.SetSize(Config.Width.Value, StatusPicture.W / (float)StatusPicture.H);
            if (game != null)
            {
                int secs = Mathf.FloorToInt(Time.realtimeSinceStartup - engineStartedAt);
                ShowStatus("Loading...", "Starting the Flappy Crix website (" + secs + " s)", "B hides - Y opens");
            }
            else
                ShowStatus("Could not start", "See BepInEx/LogOutput.log", "B hides - Y opens");
        }

        private void ShowStatus(string title, string body, string footer)
        {
            string key = title + "|" + body + "|" + footer;
            if (statusTexture == null)
            {
                statusTexture = new Texture2D(StatusPicture.W, StatusPicture.H, TextureFormat.RGBA32, false)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
                statusPixels = new uint[StatusPicture.W * StatusPicture.H];
            }
            if (key != statusShown)
            {
                statusShown = key;
                StatusPicture.Draw(statusPixels, title, body, footer);
                Visuals.Upload(statusTexture, statusPixels, StatusPicture.W, StatusPicture.H, true);
            }
            panel.ShowTexture(statusTexture, Vector2.one, Vector2.zero);
        }

        /// <summary>Runs one part of the frame; an error is logged once instead of stopping the rest.</summary>
        private void Guard(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { LogOnce(what + e.GetType().Name, "Error in " + what + ": " + e); }
        }

        private void LogOnce(string key, string message)
        {
            if (loggedErrors.Add(key)) Logger.LogError(message);
        }

        private bool Visible => station != null && station.gameObject.activeSelf;

        /// <summary>Puts the screen and deck in front of you (yaw only). They stay there until the next open.</summary>
        private void PlaceHere()
        {
            var head = rig.Head;
            if (head == null) return;
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(head.up, Vector3.up);
            fwd.Normalize();
            station.position = head.position;
            station.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        /// <summary>Y / F9: open in front of you (or bring it to you if it's already open).</summary>
        private void Open()
        {
            PlaceHere();
            if (!Visible) { station.gameObject.SetActive(true); Logger.LogInfo("Screen opened"); }
        }

        /// <summary>B / F8: hide. A run in progress is paused so you don't die while it's away.</summary>
        private void Hide()
        {
            if (!Visible) return;
            if (game != null && game.Screen == "playing") game.TogglePause();
            station.gameObject.SetActive(false);
            Logger.LogInfo("Screen hidden");
        }

        private void HandleSystemInput()
        {
            if (UnityInput.Current.GetKeyDown(Config.ToggleKey.Value)) { if (Visible) Hide(); else Open(); }
            if (UnityInput.Current.GetKeyDown(Config.RecenterKey.Value)) Open();

            // Y (left secondary) = open in front of you, B (right secondary) = hide
            if (rig.Left.Secondary && !prevLSecondary) { Open(); XRRig.Buzz(rig.Left, 0.25f, 0.04f); }
            if (rig.Right.Secondary && !prevRSecondary && Visible) { Hide(); XRRig.Buzz(rig.Right, 0.25f, 0.04f); }
            prevLSecondary = rig.Left.Secondary;
            prevRSecondary = rig.Right.Secondary;
        }

        private void HandleGameInput()
        {
            bool flap = false;

            // Keyboard
            var k = UnityInput.Current;
            if (Config.KeyboardSpaceFlaps.Value && k.GetKeyDown(KeyCode.Space)) flap = true;
            if (k.GetKeyDown(KeyCode.UpArrow)) game.Navigate(0, 1);
            if (k.GetKeyDown(KeyCode.DownArrow)) game.Navigate(0, -1);
            if (k.GetKeyDown(KeyCode.LeftArrow)) game.Navigate(-1, 0);
            if (k.GetKeyDown(KeyCode.RightArrow)) game.Navigate(1, 0);
            if (k.GetKeyDown(KeyCode.Return)) game.Select();
            if (k.GetKeyDown(KeyCode.F5)) game.StartButton();
            if (k.GetKeyDown(KeyCode.P)) game.TogglePause();
            if (k.GetKeyDown(KeyCode.Backspace)) game.Back();
            if (k.GetKeyDown(KeyCode.Minus)) ResizeScreen(-1);
            if (k.GetKeyDown(KeyCode.Equals)) ResizeScreen(+1);

            // Controller buttons: left X, right A
            if (Config.PrimaryButtonsFlap.Value)
            {
                if (rig.Left.Primary && !prevLPrimary) flap = true;
                if (rig.Right.Primary && !prevRPrimary) flap = true;
            }
            prevLPrimary = rig.Left.Primary; prevRPrimary = rig.Right.Primary;

            // Thumbstick up (optional, off by default)
            float th = Config.ThumbstickThreshold.Value;
            bool lUp = rig.Left.Stick.y > th, rUp = rig.Right.Stick.y > th && !rig.Right.GripPressed;
            if (Config.ThumbstickUpFlaps.Value && ((lUp && !prevLStickUp) || (rUp && !prevRStickUp))) flap = true;
            prevLStickUp = lUp; prevRStickUp = rUp;

            // Arcade deck: joystick navigates, buttons press
            if (deck != null)
            {
                int dx, dy;
                if (deck.Joystick.Update(rig, out dx, out dy)) game.Navigate(dx, dy);
                deck.Update(rig, OnDeckButton);
            }

            if (flap) game.Flap();

            // Laser pointer (VR) or mouse (desktop testing)
            if (panel == null) return;
            if (Config.LaserPointer.Value && rig.AnyXR)
                laser?.Update(Config.LaserOnRightHand.Value ? rig.Right : rig.Left, panel, game);
            else
                MousePointer();
        }

        private void OnDeckButton(ArcadeDeck.DeckButton b)
        {
            switch (b)
            {
                case ArcadeDeck.DeckButton.Flap: game.Flap(); break;
                case ArcadeDeck.DeckButton.Select: game.Select(); break;
                case ArcadeDeck.DeckButton.Start: game.StartButton(); break;
                case ArcadeDeck.DeckButton.Pause: game.TogglePause(); break;
                case ArcadeDeck.DeckButton.SizeDown: ResizeScreen(-1); break;
                case ArcadeDeck.DeckButton.SizeUp: ResizeScreen(+1); break;
            }
        }

        /// <summary>Deck - / + (or keyboard - / =): screen 0.3 m to 2.5 m wide, saved in the config.</summary>
        private void ResizeScreen(int dir)
        {
            float w = Mathf.Clamp(Mathf.Round((Config.Width.Value + dir * 0.08f) * 100f) / 100f, 0.3f, 2.5f);
            if (Mathf.Abs(w - Config.Width.Value) < 0.001f) return;
            Config.Width.Value = w;
            Logger.LogInfo("Screen width " + w.ToString("0.00") + " m");
        }

        private bool mouseDown;

        /// <summary>Without a headset: the PC mouse over the panel works as the pointer.</summary>
        private void MousePointer()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector2 uv; Vector3 hit; float dist;
            if (!panel.Raycast(cam.ScreenPointToRay(UnityInput.Current.mousePosition), out uv, out hit, out dist)) { mouseDown = false; return; }
            game.PointerMove(uv);
            if (UnityInput.Current.GetMouseButtonDown(0)) { mouseDown = true; game.PointerDown(uv); }
            if (UnityInput.Current.GetMouseButtonUp(0) && mouseDown) { mouseDown = false; game.PointerUp(uv); }
            float wheel = UnityInput.Current.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f) game.Scroll(uv, (int)(wheel * 120));
        }

        private void OnDestroy()
        {
            game?.Dispose();
            if (station != null) Destroy(station.gameObject);
            laser?.Destroy();
        }

        private void OnApplicationQuit() => game?.Dispose();
    }
}
