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
    ///   IFlappyGame = WebFlappyGame (input bridge -> the website's own handlers)
    ///              or NativeFlappyGame (fallback)
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
        private bool placed, fellBack;

        // edge detection
        private bool prevLPrimary, prevRPrimary, prevLStickUp, prevRStickUp, prevRSecondary, prevLSecondary;

        private void Start()
        {
            station = new GameObject("FlappyCrixStation").transform;
            DontDestroyOnLoad(station.gameObject);
            station.gameObject.SetActive(false);

            panel = new GamePanel(station);
            panel.Root.localPosition = new Vector3(0, Config.HeightOffset.Value, Config.Distance.Value);
            if (Config.DeckEnabled.Value)
            {
                deck = new ArcadeDeck(station);
                deck.Root.localPosition = Config.DeckOffset.Value;
                deck.Joystick.NavAngle = Config.JoystickNavAngle.Value;
            }
            laser = new LaserPointer { PitchDegrees = Config.LaserPitch.Value };

            if (Config.UseWebsite.Value) StartWebsiteMode();
            if (game == null) StartNativeMode(Config.UseWebsite.Value ? "website mode could not start" : "UseWebsite = false");
        }

        // ------------------------------------------------------------------ modes

        private void StartWebsiteMode()
        {
            string uwbDll = Path.Combine(ModFolder, "VoltstroStudios.UnityWebBrowser.dll");
            if (!File.Exists(uwbDll))
            {
                Logger.LogWarning("UnityWebBrowser is not installed (" + uwbDll + " missing). See README.md -> 'Installing the browser engine'.");
                return;
            }
            try
            {
                game = CreateWebGame();     // separate method: UWB types are only JIT-loaded here
                Logger.LogInfo("Mode: " + game.ModeName);
            }
            catch (Exception e)
            {
                Logger.LogError("Website mode unavailable: " + e.GetType().Name + ": " + e.Message);
                game = null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private IFlappyGame CreateWebGame()
        {
            var web = new Web.WebFlappyGame(Config, ModFolder);
            web.Log += m => Logger.LogInfo(m);
            web.Start();
            return web;
        }

        private void StartNativeMode(string why)
        {
            game?.Dispose();
            Logger.LogInfo("Mode: native Unity Flappy Crix (" + why + ")");
            var native = new NativeFlappyGame(panel.Surface, ModFolder, this);
            native.Log += m => Logger.LogInfo(m);
            game = native;
            panel.ShowTexture(null);
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            rig.Update();
            if (game == null) return;

            game.Tick();
            if (game.HasFailed && !fellBack)
            {
                fellBack = true;
                if (Config.AutoFallbackToNative.Value) StartNativeMode(game.FailureReason);
                else Logger.LogError("Website mode failed and AutoFallbackToNative = false: " + game.FailureReason);
            }

            panel.SetSize(Config.Width.Value, game.Aspect);
            if (game.PanelTexture != null) panel.ShowTexture(game.PanelTexture);

            // Open in front of you as soon as the VR camera exists
            if (!placed && rig.Head != null)
            {
                placed = true;
                PlaceHere();
                station.gameObject.SetActive(Config.ShowOnStart.Value);
            }

            HandleSystemInput();
            if (!Visible) return;
            HandleGameInput();
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
            if (Config.LaserPointer.Value && rig.AnyXR)
                laser.Update(Config.LaserOnRightHand.Value ? rig.Right : rig.Left, panel, game);
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
            }
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
