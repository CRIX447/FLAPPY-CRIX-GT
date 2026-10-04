using BepInEx.Configuration;
using UnityEngine;

namespace FlappyCrix
{
    /// <summary>All settings, written to BepInEx/config/com.crix.flappycrix.cfg on first run.</summary>
    public sealed class FlappyCrixConfig
    {
        // General
        public readonly ConfigEntry<bool> UseWebsite;
        public readonly ConfigEntry<bool> UseRemoteWebsite;
        public readonly ConfigEntry<string> RemoteUrl;
        public readonly ConfigEntry<float> RemoteTimeoutSeconds;
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

        public readonly ConfigEntry<bool> OpenLinksOnDesktop;

        // Website
        public readonly ConfigEntry<int> LocalPort;
        public readonly ConfigEntry<bool> SkipIntro;
        public readonly ConfigEntry<bool> RunSelfTest;
        public readonly ConfigEntry<int> EngineStartupTimeoutMs;
        public readonly ConfigEntry<bool> RemoteDebugging;

        public FlappyCrixConfig(ConfigFile cfg)
        {
            const string G = "1. General", D = "2. Display", I = "3. Input", J = "4. Arcade deck", W = "5. Website";

            UseWebsite = cfg.Bind(G, "UseWebsite", true,
                "true = run the real Flappy Crix website (HTML/JS) in an embedded Chromium (UnityWebBrowser). false = native Unity version.");
            UseRemoteWebsite = cfg.Bind(G, "UseRemoteWebsite", true,
                "true = play the live page at RemoteUrl (crixgamingvr.com/flappycrix). false = the copy packaged with the mod (works offline). The live page falls back to the packaged copy if it can't load.");
            RemoteUrl = cfg.Bind(G, "RemoteUrl", "https://crixgamingvr.com/flappycrix", "Live site used when UseRemoteWebsite = true.");
            RemoteTimeoutSeconds = cfg.Bind(G, "RemoteTimeoutSeconds", 12f, "How long the live site gets to load before falling back to the local copy.");
            AutoFallbackToNative = cfg.Bind(G, "AutoFallbackToNative", true,
                "If the embedded browser is missing or fails its self test, switch to the native Unity version automatically.");
            ShowOnStart = cfg.Bind(G, "ShowOnStart", true, "Open the screen in front of you when you load in.");
            ToggleKey = cfg.Bind(G, "ToggleKey", KeyCode.F8, "Keyboard: show/hide. In VR: B hides, Y opens it in front of you.");
            RecenterKey = cfg.Bind(G, "RecenterKey", KeyCode.F9, "Keyboard: bring the screen and deck in front of you (same as Y in VR).");

            Distance = cfg.Bind(D, "Distance", 1.6f, new ConfigDescription("Metres in front of you. 1.6 fits inside the stump.", new AcceptableValueRange<float>(0.75f, 6f)));
            Width = cfg.Bind(D, "Width", 1.0f, new ConfigDescription("Panel width in metres. Height follows the resolution's aspect ratio.", new AcceptableValueRange<float>(0.5f, 4f)));
            HeightOffset = cfg.Bind(D, "HeightOffset", -0.15f, "Panel centre relative to eye height, metres.");
            ResolutionWidth = cfg.Bind(D, "ResolutionWidth", 768, new ConfigDescription("Browser pixels. 768x960 gives the site's tablet layout with text large enough for VR; 1024x1280 is sharper but smaller; below ~700 wide the site switches to its cramped phone layout.", new AcceptableValueRange<int>(320, 2560)));
            ResolutionHeight = cfg.Bind(D, "ResolutionHeight", 960, new ConfigDescription("Browser pixels.", new AcceptableValueRange<int>(320, 2560)));
            BrowserFrameRate = cfg.Bind(D, "BrowserFrameRate", 60, new ConfigDescription("Frames per second the browser renders at (UnityWebBrowser allows 1-60).", new AcceptableValueRange<int>(15, 60)));
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
                "Deck position relative to you when the screen opens, metres (right, up, forward). Push buttons by putting your hand down on them.");
            JoystickNavAngle = cfg.Bind(J, "JoystickNavAngle", 18f, new ConfigDescription("Degrees the joystick must tilt to move the menu highlight.", new AcceptableValueRange<float>(8f, 30f)));

            LocalPort = cfg.Bind(W, "LocalPort", 47321,
                "Loopback port for the packaged site (127.0.0.1 only). Keep it fixed: your saved progress belongs to this address. The browser IPC uses the next two ports.");
            OpenLinksOnDesktop = cfg.Bind(W, "OpenLinksOnDesktop", true,
                "Sign-in, Discord/YouTube/TikTok/shop and the site's other pages open in your normal PC browser. Only the Flappy Crix game stays in-game.");
            SkipIntro = cfg.Bind(W, "SkipIntro", true, "Skip the CRIX STUDIOS intro video.");
            RunSelfTest = cfg.Bind(W, "RunSelfTest", true, "Check the embedded site actually works (JS, CSS, images, audio, canvas, input, frame rate) and log the result.");
            EngineStartupTimeoutMs = cfg.Bind(W, "EngineStartupTimeoutMs", 15000, "How long to wait for the browser engine process to start.");
            RemoteDebugging = cfg.Bind(W, "RemoteDebugging", false, "Developer: Chrome DevTools at http://127.0.0.1:9022 while the game runs.");
        }
    }
}
