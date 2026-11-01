// A Flappy Crix arcade machine. Drop it in the scene (GameObject > CRIX > Flappy Crix Arcade
// Machine) and it builds the cabinet, runs the game on its screen and wires up the buttons,
// joystick, coin slot and touch screen for VR hands (put ArcadeHand on your fingertips).
//
//   Single player: leave Link Group empty. It's a normal arcade machine: attract mode, free play
//   or coins, and its own high-score table.
//   Multiplayer: give cabinets that stand together the same Link Group and give ArcadeLink a
//   transport (see Samples~). Players at those cabinets can race each other, watch races and
//   share the high scores - through your game's existing room, so no extra Photon CCU.
//
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using System.Collections.Generic;
using CrixArcade.Net;
using UnityEngine;

namespace CrixArcade
{
    [AddComponentMenu("CRIX/Flappy Crix Arcade Machine")]
    [DisallowMultipleComponent]
    public sealed class ArcadeMachine : MonoBehaviour
    {
        public static readonly List<ArcadeMachine> All = new List<ArcadeMachine>();

        [Header("Machine")]
        [Tooltip("Unique per cabinet in the scene: names its save file and high-score table, and must be the same for every player.")]
        public string cabinetId = "";
        [Tooltip("No coins needed. Off = touch the coin slot (or press C) before each game.")]
        public bool freePlay = true;
        [Tooltip("Seconds alone on the menu before the attract screen (0 = never).")]
        public float attractAfter = 40;
        [Range(0, 1)] public float volume = 1;

        [Header("Multiplayer (optional)")]
        [Tooltip("Cabinets with the same group race each other. Empty = single player.")]
        public string linkGroup = "";

        [Header("Model")]
        [Tooltip("Build the arcade cabinet model. Off = use your own model and set Screen Renderer (and add ArcadeButton / ArcadeJoystick / ArcadeCoinSlot to your parts).")]
        public bool buildCabinet = true;
        public ArcadeStyle style = new ArcadeStyle();
        [Tooltip("Add box colliders so players can't walk through it.")]
        public bool addColliders = true;
        [Tooltip("A lit material using your pipeline's shader (made by the editor menu). Empty = found at run time.")]
        public Material litMaterial;
        [Tooltip("An unlit material using your pipeline's shader (made by the editor menu). Empty = found at run time.")]
        public Material unlitMaterial;
        [Tooltip("Your own screen (only when Build Cabinet is off). The game is drawn on its main texture.")]
        public Renderer screenRenderer;

        [Header("Performance")]
        [Tooltip("Further than this (metres) from the camera, the screen stops drawing and the sound stops.")]
        public float activeDistance = 14f;
        [Tooltip("Pictures per second on the screen.")]
        [Range(10, 60)] public int frameRate = 30;

        [Header("Testing")]
        [Tooltip("Keyboard: Space flap, Enter start, Backspace/Esc back, arrows move, Tab select, C coin. Only for the nearest cabinet.")]
        public bool keyboard = true;
        [Tooltip("The mouse can click the screen and buttons (desktop testing).")]
        public bool mouse = true;

        public ArcadeGame Game { get; private set; }
        public ArcadeSeat Seat { get; private set; }
        public CabinetParts Parts { get; private set; }

        ArcadeCabinet.MaterialSet mats;
        Material screenMat;
        readonly Dictionary<ArcadeHand, bool> touchingScreen = new Dictionary<ArcadeHand, bool>();
        bool warned;

        void Reset() { cabinetId = "cabinet-" + Mathf.Abs(gameObject.GetInstanceID() % 10000); }

        void Awake()
        {
            if (string.IsNullOrEmpty(cabinetId)) cabinetId = gameObject.name;

            if (buildCabinet)
            {
                mats = new ArcadeCabinet.MaterialSet { Lit = litMaterial, Unlit = unlitMaterial };
                Parts = ArcadeCabinet.Build(this, style, mats, freePlay);
                if (addColliders) ArcadeCabinet.AddColliders(Parts.Root);
                screenMat = Parts.ScreenMaterial;
            }
            else if (screenRenderer != null) screenMat = screenRenderer.material;

            Game = new ArcadeGame(cabinetId, Parts != null ? Parts.ScreenFrame : transform);
            Game.Runner.FrameRate = frameRate;
            var app = Game.App;
            app.ArcadeMode = true;
            app.FreePlay = freePlay;
            app.AttractAfter = attractAfter;
            app.FlapStartsGame = true;

            Seat = ArcadeSeat.For(cabinetId, Game.Runner);
            Seat.Group = linkGroup;
            Seat.Position = Pos(transform.position);
            Seat.ScoresChanged = Game.SaveScoresFromGameThread;
            Game.Start();
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            if (Seat != null) ArcadeLink.Instance.Network.Add(Seat);
        }

        void OnDisable()
        {
            All.Remove(this);
            if (Seat != null && ArcadeLink.Exists) ArcadeLink.Instance.Network.Remove(Seat);
            if (Game != null) Game.Visible = false;
        }

        void OnDestroy()
        {
            if (Game != null) Game.Dispose();
            if (Parts != null) foreach (var o in Parts.Owned) if (o != null) Destroy(o);
        }

        void OnApplicationPause(bool paused) { if (paused && Game != null) Game.Post(a => a.Flush()); }

        static ArcadeVec Pos(Vector3 p) => new ArcadeVec(p.x, p.y, p.z);

        // ------------------------------------------------------------------ every frame

        void Update()
        {
            if (Game == null) return;
            if (Game.Error != null && !warned) { warned = true; Debug.LogError("[CrixArcade] " + cabinetId + ": " + Game.Error); }

            Seat.Group = linkGroup;
            Seat.Position = Pos(transform.position);
            Game.Volume = volume;
            Game.Runner.FrameRate = frameRate;

            var cam = Camera.main;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 0;
            Game.Visible = dist <= activeDistance;
            Game.Tick();

            if (screenMat != null && Game.HasPicture && screenMat.mainTexture != Game.Texture)
            {
                screenMat.mainTexture = Game.Texture;
                if (screenMat.HasProperty("_BaseMap")) screenMat.SetTexture("_BaseMap", Game.Texture);
                if (!buildCabinet)
                {
                    // the game's rows are top-down
                    screenMat.mainTextureScale = new Vector2(1, -1);
                    screenMat.mainTextureOffset = new Vector2(0, 1);
                }
            }

            if (Game.Visible)
            {
                ScreenTouch();
                if (keyboard && IsNearest(cam)) Keys();
                if (mouse && cam != null) Mouse(cam);
            }
        }

        bool IsNearest(Camera cam)
        {
            if (cam == null) return All.Count > 0 && All[0] == this;
            float mine = (cam.transform.position - transform.position).sqrMagnitude;
            foreach (var m in All) if (m != this && (cam.transform.position - m.transform.position).sqrMagnitude < mine) return false;
            return true;
        }

        // ------------------------------------------------------------------ input

        /// <summary>Press a control (from the buttons, the joystick, the keyboard or your own code).</summary>
        public void Press(ArcadeAction a)
        {
            if (Game == null) return;
            Seat.TouchedAt = Time.unscaledTimeAsDouble;
            switch (a)
            {
                case ArcadeAction.Flap: Game.Flap(); break;
                case ArcadeAction.Start: Game.StartButton(); break;
                case ArcadeAction.Back: Game.Back(); break;
                case ArcadeAction.Select: Game.Select(); break;
                case ArcadeAction.Coin: Game.InsertCoin(); break;
                case ArcadeAction.Left: Game.Navigate(-1, 0); break;
                case ArcadeAction.Right: Game.Navigate(1, 0); break;
                case ArcadeAction.Up: Game.Navigate(0, 1); break;
                case ArcadeAction.Down: Game.Navigate(0, -1); break;
            }
        }

        /// <summary>Where a world point is on the screen (0..1 from the top-left), and how far in front of it.</summary>
        bool ScreenPoint(Vector3 world, out Vector2 uv, out float front)
        {
            uv = default(Vector2); front = 0;
            if (Parts == null || Parts.ScreenFrame == null) return false;
            var f = Parts.ScreenFrame;
            Vector3 local = f.InverseTransformPoint(world);       // in the cabinet's own units: x = player's right, y = up, -z = towards the player
            Vector2 size = Parts.ScreenSize;
            float x = local.x, y = local.y;
            front = -local.z * style.scale;                        // metres
            uv = new Vector2(0.5f + x / size.x, 0.5f - y / size.y);
            return uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1;
        }

        void ScreenTouch()
        {
            foreach (var h in ArcadeHand.All)
            {
                Vector2 uv; float front;
                bool over = ScreenPoint(h.Tip, out uv, out front) && front > -0.03f && front < 0.06f;
                bool was;
                touchingScreen.TryGetValue(h, out was);
                if (over) Game.PointerMove(uv);
                bool touch = over && front < h.radius + 0.01f;
                if (touch && !was) { Seat.TouchedAt = Time.unscaledTimeAsDouble; Game.PointerDown(uv); h.Buzz(0.3f, 0.03f); }
                touchingScreen[h] = touch;
            }
        }

        // the old Input class throws in projects set to the new Input System only, so it's left out there
        void Keys()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Space)) Press(ArcadeAction.Flap);
            if (Input.GetKeyDown(KeyCode.Return)) Press(ArcadeAction.Start);
            if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Escape)) Press(ArcadeAction.Back);
            if (Input.GetKeyDown(KeyCode.Tab)) Press(ArcadeAction.Select);
            if (Input.GetKeyDown(KeyCode.C)) Press(ArcadeAction.Coin);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) Press(ArcadeAction.Left);
            if (Input.GetKeyDown(KeyCode.RightArrow)) Press(ArcadeAction.Right);
            if (Input.GetKeyDown(KeyCode.UpArrow)) Press(ArcadeAction.Up);
            if (Input.GetKeyDown(KeyCode.DownArrow)) Press(ArcadeAction.Down);
#endif
        }

        void Mouse(Camera cam)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!Input.GetMouseButtonDown(0)) return;
            Click(cam.ScreenPointToRay(Input.mousePosition));
#endif
        }

        /// <summary>A click along a ray (mouse, or a laser pointer from your own code).</summary>
        public void Click(Ray ray)
        {
            if (Parts != null)
            {
                // the buttons
                foreach (var b in Parts.Buttons.Values)
                {
                    Vector3 c = b.transform.position;
                    if (Vector3.Cross(ray.direction, c - ray.origin).magnitude < b.radius * style.scale * 1.3f) { b.PressFromCode(); return; }
                }
                if (Parts.CoinSlot != null && Vector3.Cross(ray.direction, Parts.CoinSlot.transform.position - ray.origin).magnitude < 0.05f * style.scale)
                { Press(ArcadeAction.Coin); return; }
                // the screen
                var f = Parts.ScreenFrame;
                var plane = new Plane(-f.forward, f.position);
                float d;
                if (plane.Raycast(ray, out d) && d < activeDistance)
                {
                    Vector2 uv; float front;
                    if (ScreenPoint(ray.GetPoint(d), out uv, out front)) { Seat.TouchedAt = Time.unscaledTimeAsDouble; Game.PointerMove(uv); Game.PointerDown(uv); }
                }
            }
        }
    }
}
