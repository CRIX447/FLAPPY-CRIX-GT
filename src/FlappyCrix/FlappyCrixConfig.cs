using BepInEx.Configuration;
using UnityEngine;

namespace FlappyCrix
{
    public enum WebEngine
    {
        /// <summary>Hidden Edge/Chrome if found, else UnityWebBrowser if installed, else the native version.</summary>
        Auto,
        /// <summary>The PC's own Microsoft Edge / Chrome, running hidden (no window).</summary>
        SystemBrowser,
        /// <summary>UnityWebBrowser's bundled CEF (needs the UWB folder, see README).</summary>
        UnityWebBrowser,
    }

    /// <summary>All settings, written to BepInEx/config/com.crix.flappycrix.cfg on first run.</summary>
    public sealed class FlappyCrixConfig
    {
        // General
        public readonly ConfigEntry<bool> UseWebsite;
        public readonly ConfigEntry<WebEngine> Engine;
        public readonly ConfigEntry<string> BrowserPath;
        public readonly ConfigEntry<string> BrowserExtraArgs;
        public readonly ConfigEntry<int> JpegQuality;
        public readonly ConfigEntry<string> ShaderOverride;
        public readonly ConfigEntry<bool> UseRemoteWebsite;
        public readonly ConfigEntry<string> RemoteUrl;
        public readonly ConfigEntry<float> RemoteTimeoutSeconds;
        public readonly ConfigEntry<float> ReconnectSeconds;
        public readonly ConfigEntry<bool> AutoFallbackToNative;
        public readonly ConfigEntry<bool> ShowOnStart;
        public readonly ConfigEntry<KeyCode> ToggleKey;
        public readonly ConfigEntry<KeyCode> RecenterKey;

        // Display
        public readonly ConfigEntry<float> Distance;
        public readonly ConfigEntry<float> Width;
        public readonly ConfigEntry<float> HeightOffset;
        public readonly ConfigEntry<int> ResolutionWidth;
        public readonly ConfigEntry<int> ResolutionHeight;
        public readonly ConfigEntry<int> BrowserFrameRate;
        public readonly ConfigEntry<bool> MuteWebAudio;

        // Input
        public readonly ConfigEntry<bool> KeyboardSpaceFlaps;
        public readonly ConfigEntry<bool> PrimaryButtonsFlap;
        public readonly ConfigEntry<bool> ThumbstickUpFlaps;
        public readonly ConfigEntry<float> ThumbstickThreshold;
        public readonly ConfigEntry<bool> LaserPointer;
        public readonly ConfigEntry<bool> LaserOnRightHand;
        public readonly ConfigEntry<bool> FlapStartsGame;
        public readonly ConfigEntry<float> LaserPitch;

        // Arcade deck
        public readonly ConfigEntry<bool> DeckEnabled;
        public readonly ConfigEntry<Vector3> DeckOffset;
        public readonly ConfigEntry<float> JoystickNavAngle;
        public readonly ConfigEntry<bool> UseGameFingertips;
        public readonly ConfigEntry<Vector3> FingertipOffset;

        public readonly ConfigEntry<bool> OpenLinksOnDesktop;

        // Website
        public readonly ConfigEntry<int> LocalPort;
        public readonly ConfigEntry<bool> SkipIntro;
        public readonly ConfigEntry<bool> RunSelfTest;
        public readonly ConfigEntry<int> EngineStartupTimeoutMs;
        public readonly ConfigEntry<bool> DownloadEngine;
        public readonly ConfigEntry<bool> RemoteDebugging;

        /// <summary>True when settings from the first test build were upgraded to the current display defaults this run.</summary>
        public readonly bool UpgradedFromOlderConfig;

        public FlappyCrixConfig(ConfigFile cfg)
        {
            const string G = "1. General", D = "2. Display", I = "3. Input", J = "4. Arcade deck", W = "5. Website";

            UseWebsite = cfg.Bind(G, "UseWebsite", true,
                "true = play the real Flappy Crix website. false = the native Unity version.");
            Engine = cfg.Bind(G, "Engine", WebEngine.Auto,
                "What runs the website. Auto/SystemBrowser = the Microsoft Edge (or Chrome) already on your PC, started hidden - no window opens, the page is streamed onto the in-game screen. UnityWebBrowser = optional bundled engine (see README).");
            UseRemoteWebsite = cfg.Bind(G, "UseRemoteWebsite", true,
                "true = play the live page at RemoteUrl (crixgamingvr.com/flappycrix). false = the copy packaged with the mod (works offline). The live page falls back to the packaged copy if it can't load.");
            RemoteUrl = cfg.Bind(G, "RemoteUrl", "https://crixgamingvr.com/flappycrix", "Live site used when UseRemoteWebsite = true.");
            RemoteTimeoutSeconds = cfg.Bind(G, "RemoteTimeoutSeconds", 30f, new ConfigDescription(
                "How long the live site gets to START loading (counted from when the hidden browser is up) before the packaged copy is used. Once the live page has started loading it gets as long as it needs.",
                new AcceptableValueRange<float>(5f, 120f)));
            ReconnectSeconds = cfg.Bind(G, "ReconnectSeconds", 45f, new ConfigDescription(
                "If the live site couldn't be reached and the packaged (offline) copy is showing, check this often whether crixgamingvr.com can be reached again, and switch back to it when you're not mid-run. 0 = never.",
                new AcceptableValueRange<float>(0f, 600f)));
            AutoFallbackToNative = cfg.Bind(G, "AutoFallbackToNative", true,
                "If the embedded browser is missing or fails its self test, switch to the native Unity version automatically.");
            ShowOnStart = cfg.Bind(G, "ShowOnStart", true, "Open the screen in front of you when you load in.");
            ToggleKey = cfg.Bind(G, "ToggleKey", KeyCode.F8, "Keyboard: show/hide. In VR: B hides, Y opens it in front of you.");
            RecenterKey = cfg.Bind(G, "RecenterKey", KeyCode.F9, "Keyboard: bring the screen and deck in front of you (same as Y in VR).");

            Distance = cfg.Bind(D, "Distance", 0.75f, new ConfigDescription("How far in front of you the screen stands, metres. 0.75 = just behind the deck, like an arcade cabinet.", new AcceptableValueRange<float>(0.4f, 6f)));
            Width = cfg.Bind(D, "Width", 0.36f, new ConfigDescription("Screen width in metres (height follows: 0.36 wide = 0.45 tall). Also changed in-game with the deck's - / + buttons.", new AcceptableValueRange<float>(0.2f, 2.5f)));
            HeightOffset = cfg.Bind(D, "HeightOffset", -0.43f, "Height of the screen's BOTTOM edge relative to your eyes, metres (-0.43 = just above the deck). Making the screen bigger grows it upwards.");
            ResolutionWidth = cfg.Bind(D, "ResolutionWidth", 768, new ConfigDescription("Browser pixels. 768x960 gives the site's tablet layout with text large enough for VR; 1024x1280 is sharper but smaller; below ~700 wide the site switches to its cramped phone layout.", new AcceptableValueRange<int>(320, 2560)));
            ResolutionHeight = cfg.Bind(D, "ResolutionHeight", 960, new ConfigDescription("Browser pixels.", new AcceptableValueRange<int>(320, 2560)));
            BrowserFrameRate = cfg.Bind(D, "BrowserFrameRate", 30, new ConfigDescription("Most screen updates per second from the website. 30 is smooth and light on VR performance; up to 60.", new AcceptableValueRange<int>(10, 60)));
            JpegQuality = cfg.Bind(D, "StreamQuality", 80, new ConfigDescription("Picture quality of the website stream (hidden Edge engine), 50-95.", new AcceptableValueRange<int>(50, 95)));
            ShaderOverride = cfg.Bind(D, "ShaderOverride", "", "Advanced: a shader name to draw the screen and deck with, if the default doesn't show up.");
            MuteWebAudio = cfg.Bind(D, "MuteWebAudio", false, "Mute the website's music and sound effects.");

            KeyboardSpaceFlaps = cfg.Bind(I, "KeyboardSpaceFlaps", true, "SPACE on the PC keyboard = flap.");
            PrimaryButtonsFlap = cfg.Bind(I, "PrimaryButtonsFlap", true, "Left X / right A = flap.");
            ThumbstickUpFlaps = cfg.Bind(I, "ThumbstickUpFlaps", false, "Pushing either controller thumbstick up = flap (same rule as the site's gamepad support).");
            ThumbstickThreshold = cfg.Bind(I, "ThumbstickThreshold", 0.55f, new ConfigDescription("How far up the thumbstick must go.", new AcceptableValueRange<float>(0.2f, 0.95f)));
            LaserPointer = cfg.Bind(I, "LaserPointer", true, "Point at the panel and pull the trigger to click the website's buttons (menus, store, settings).");
            LaserOnRightHand = cfg.Bind(I, "LaserOnRightHand", true, "false = laser on the left hand.");
            LaserPitch = cfg.Bind(I, "LaserPitch", 30f, new ConfigDescription("Downward tilt of the laser from the controller's forward axis, degrees. Adjust if the laser doesn't follow where you point.", new AcceptableValueRange<float>(-90f, 90f)));
            FlapStartsGame = cfg.Bind(I, "FlapStartsGame", true, "Flapping on the main menu starts a normal run (like the START button).");

            DeckEnabled = cfg.Bind(J, "Enabled", true,
                "Arcade control deck in front of the screen: joystick (menu navigation) + FLAP, SELECT, START, PAUSE buttons. It opens with the screen and stays put until you press Y again.");
            DeckOffset = cfg.Bind(J, "Offset", new Vector3(0f, -0.5f, 0.42f),
                "Deck position relative to you when the screen opens, metres (right, up, forward). Press buttons with your index fingertip.");
            JoystickNavAngle = cfg.Bind(J, "JoystickNavAngle", 18f, new ConfigDescription("Degrees the joystick must tilt to move the menu highlight.", new AcceptableValueRange<float>(8f, 30f)));
            UseGameFingertips = cfg.Bind(J, "UseGameFingertips", true,
                "Press deck buttons with the same fingertip points Gorilla Tag's own buttons use. false (or if the game's points can't be found) = FingertipOffset from the controller.");
            FingertipOffset = cfg.Bind(J, "FingertipOffset", new Vector3(0f, -0.02f, 0.085f),
                "Where your index fingertip is relative to the controller, metres (right, up, forward). Used when the game's own fingertip points aren't available.");

            LocalPort = cfg.Bind(W, "LocalPort", 47321,
                "Loopback port for the packaged site (127.0.0.1 only). Keep it fixed: your saved progress belongs to this address. The browser IPC uses the next two ports.");
            BrowserPath = cfg.Bind(W, "BrowserPath", "", "Leave empty to use Microsoft Edge (or Chrome/Brave) automatically. Or the full path to a Chromium browser's .exe.");
            BrowserExtraArgs = cfg.Bind(W, "BrowserExtraArgs", "", "Advanced: extra command-line switches for the hidden browser.");
            OpenLinksOnDesktop = cfg.Bind(W, "OpenLinksOnDesktop", true,
                "Sign-in, Discord/YouTube/TikTok/shop and the site's other pages open in your normal PC browser. Only the Flappy Crix game stays in-game.");
            SkipIntro = cfg.Bind(W, "SkipIntro", true, "Skip the CRIX STUDIOS intro video.");
            RunSelfTest = cfg.Bind(W, "RunSelfTest", true, "Check the embedded site actually works (JS, CSS, images, audio, canvas, input, frame rate) and log the result.");
            EngineStartupTimeoutMs = cfg.Bind(W, "EngineStartupTimeoutMs", 20000, "How long to wait for the browser engine to start.");
            DownloadEngine = cfg.Bind(W, "DownloadEngine", true,
                "If no browser on this PC can run the website hidden (Edge, Chrome, Brave, Vivaldi, Chromium... - Opera and Firefox can't), download Google's official Chrome for Testing headless shell once (about 100 MB, into %LOCALAPPDATA%\\FlappyCrix\\engine). It never opens a window. false = use the built-in version instead.");
            RemoteDebugging = cfg.Bind(W, "RemoteDebugging", false, "Developer: Chrome DevTools at http://127.0.0.1:9022 while the game runs.");

            // One-time upgrade: the first test build put a 1.0 m screen 1.6 m away, which was too big.
            // Settings files from that build get the new display defaults (and the lighter 30 fps stream) once.
            // (A settings-file revision number - not the mod's version.)
            var revision = cfg.Bind("0. About", "SettingsRevision", 0, "Set automatically; not the mod version. This mod was made with AI (Claude by Anthropic).");
            if (revision.Value < 2)
            {
                Width.Value = (float)Width.DefaultValue;
                Distance.Value = (float)Distance.DefaultValue;
                HeightOffset.Value = (float)HeightOffset.DefaultValue;
                BrowserFrameRate.Value = (int)BrowserFrameRate.DefaultValue;
                ResolutionWidth.Value = (int)ResolutionWidth.DefaultValue;
                ResolutionHeight.Value = (int)ResolutionHeight.DefaultValue;
                EngineStartupTimeoutMs.Value = (int)EngineStartupTimeoutMs.DefaultValue;
                revision.Value = 2;
                UpgradedFromOlderConfig = true;
            }
            // Revision 4: the screen moved to just behind the deck (0.36 m wide, 0.75 m away) and
            // HeightOffset now means the screen's bottom edge, so the old values no longer fit.
            if (revision.Value < 4)
            {
                Width.Value = (float)Width.DefaultValue;
                Distance.Value = (float)Distance.DefaultValue;
                HeightOffset.Value = (float)HeightOffset.DefaultValue;
                UpgradedFromOlderConfig = true;
            }
            // Revision 3: the live site got only 12 s (counted from game start) before the offline copy took over.
            if (revision.Value < 3)
            {
                if (RemoteTimeoutSeconds.Value < (float)RemoteTimeoutSeconds.DefaultValue) RemoteTimeoutSeconds.Value = (float)RemoteTimeoutSeconds.DefaultValue;
            }
            if (revision.Value < 4) revision.Value = 4;
        }
    }
}
