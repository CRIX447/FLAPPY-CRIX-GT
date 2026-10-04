using System.Collections.Generic;
using UnityEngine;

namespace FlappyCrix.VR
{
    /// <summary>
    /// The arcade control deck in front of the screen: a joystick for menu navigation, the
    /// FLAP, SELECT, START and PAUSE buttons, and small - / + buttons for the screen size.
    /// Press a button with your index fingertip (Gorilla Tag's own fingertip point when it can
    /// be found, else the fingertip worked out from the controller). A button's ring lights up
    /// while your fingertip is over it. No colliders: presses are measured from those points.
    /// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
    /// </summary>
    public sealed class ArcadeDeck
    {
        public enum DeckButton { Flap, Select, Start, Pause, SizeDown, SizeUp }

        private sealed class Button
        {
            public DeckButton Action;
            public Vector3 Local;          // centre of the cap's base, in surface space
            public float Radius;
            public Transform Cap;
            public Material CapMat;
            public Material RingMat;
            public Color Colour;
            public bool Down;
        }

        public readonly Transform Root;      // placed in the world (yaw only)
        public readonly VRJoystick Joystick;
        private readonly Transform surface;  // tilted top of the deck, scale 1
        private readonly List<Button> buttons = new List<Button>();

        const float CapHeight = 0.018f;
        static readonly Color RingIdle = Visuals.Hex("#0D0D12"), RingHover = Visuals.Hex("#F2F2FF");
        // Fingertip press zone: over the cap (a little wider than it) and down at the cap's top.
        const float PressMargin = 0.012f, PressAbove = 0.008f, PressBelow = 0.04f;
        // Re-arm once the fingertip has come away; ring lights up while the fingertip is over the button.
        const float NearMargin = 0.035f, NearAbove = 0.06f, HoverMargin = 0.02f, HoverAbove = 0.09f;
        const string Plate = "#2B2140";

        public ArcadeDeck(Transform parent)
        {
            Root = new GameObject("ArcadeDeck").transform;
            Root.SetParent(parent, false);

            surface = new GameObject("Surface").transform;
            surface.SetParent(Root, false);
            surface.localRotation = Quaternion.Euler(-15f, 0, 0);   // top tilts towards the player

            var body = Visuals.Primitive(PrimitiveType.Cube, "Body", surface, Visuals.Hex("#1A1A24"), 0).transform;
            body.localScale = new Vector3(0.6f, 0.08f, 0.28f);
            body.localPosition = new Vector3(0, -0.04f, 0.01f);
            var top = Visuals.Primitive(PrimitiveType.Cube, "Top", surface, Visuals.Hex(Plate), 1).transform;
            top.localScale = new Vector3(0.58f, 0.004f, 0.26f);
            top.localPosition = new Vector3(0, 0.002f, 0.01f);
            var trim = Visuals.Primitive(PrimitiveType.Cube, "Trim", surface, Visuals.Hex("#FF6B35"), 2).transform;
            trim.localScale = new Vector3(0.6f, 0.012f, 0.008f);
            trim.localPosition = new Vector3(0, -0.002f, -0.132f);
            var stand = Visuals.Primitive(PrimitiveType.Cube, "Stand", Root, Visuals.Hex("#111116"), 0).transform;
            stand.localScale = new Vector3(0.12f, 0.5f, 0.12f);
            stand.localPosition = new Vector3(0, -0.3f, 0.02f);

            Joystick = new VRJoystick(surface);
            Joystick.Root.localPosition = new Vector3(-0.19f, 0.004f, 0f);
            Visuals.Label("MENU", surface, new Vector3(-0.19f, 0.0045f, -0.085f), 0.016f, "#FFFFFF", Plate);

            AddButton(DeckButton.Select, "SELECT", new Vector3(-0.055f, 0, 0.0f), 0.026f, Visuals.Hex("#00CC7A"));
            AddButton(DeckButton.Start,  "START",  new Vector3( 0.025f, 0, 0.0f), 0.026f, Visuals.Hex("#3A8DFF"));
            AddButton(DeckButton.Pause,  "PAUSE",  new Vector3( 0.105f, 0, 0.0f), 0.026f, Visuals.Hex("#FFC107"));
            AddButton(DeckButton.Flap,   "FLAP",   new Vector3( 0.205f, 0, 0.0f), 0.045f, Visuals.Hex("#FF6B35"));

            // Screen size, in the back row
            AddButton(DeckButton.SizeDown, "-", new Vector3(0.0f, 0, 0.095f), 0.016f, Visuals.Hex("#8A8FB0"), labelBelow: false);
            AddButton(DeckButton.SizeUp,   "+", new Vector3(0.07f, 0, 0.095f), 0.016f, Visuals.Hex("#8A8FB0"), labelBelow: false);
            Visuals.Label("SIZE", surface, new Vector3(0.035f, 0.0045f, 0.062f), 0.011f, "#C8C8D8", Plate);
        }

        private void AddButton(DeckButton action, string label, Vector3 at, float radius, Color colour, bool labelBelow = true)
        {
            var ringGo = Visuals.Primitive(PrimitiveType.Cylinder, label + "Ring", surface, RingIdle, 3);
            var ring = ringGo.transform;
            ring.localScale = new Vector3(radius * 2.5f, 0.004f, radius * 2.5f);
            ring.localPosition = at + new Vector3(0, 0.004f, 0);

            var capGo = Visuals.Primitive(PrimitiveType.Cylinder, label + "Button", surface, colour, 4);
            var cap = capGo.transform;
            cap.localScale = new Vector3(radius * 2f, CapHeight / 2f, radius * 2f);
            cap.localPosition = at + new Vector3(0, CapHeight / 2f + 0.004f, 0);

            if (labelBelow)
                Visuals.Label(label, surface, at + new Vector3(0, 0.0045f, -radius - 0.026f), 0.012f, "#FFFFFF", Plate);
            else
            {
                // symbol printed on top of the small cap
                var sym = Visuals.Label(label, cap, new Vector3(0, 1.01f, 0), 0.6f, "#FFFFFF", "#8A8FB0");
                sym.localScale = new Vector3(sym.localScale.x * 0.5f, sym.localScale.y * 0.5f, 1);
            }
            buttons.Add(new Button { Action = action, Local = at, Radius = radius, Cap = cap,
                                     CapMat = capGo.GetComponent<MeshRenderer>().sharedMaterial,
                                     RingMat = ringGo.GetComponent<MeshRenderer>().sharedMaterial, Colour = colour });
        }

        public bool Visible
        {
            get => Root.gameObject.activeSelf;
            set => Root.gameObject.SetActive(value);
        }

        /// <summary>Checks both fingertips against every button; calls onPress once per press.</summary>
        public void Update(XRRig rig, System.Action<DeckButton> onPress)
        {
            foreach (var b in buttons)
            {
                bool leftIn = InZone(rig.Left, b, PressMargin, CapHeight + 0.004f + PressAbove, PressBelow);
                bool rightIn = InZone(rig.Right, b, PressMargin, CapHeight + 0.004f + PressAbove, PressBelow);
                bool releasedFar = !InZone(rig.Left, b, NearMargin, NearAbove, 0.08f) && !InZone(rig.Right, b, NearMargin, NearAbove, 0.08f);
                bool hover = InZone(rig.Left, b, HoverMargin, HoverAbove, 0.08f) || InZone(rig.Right, b, HoverMargin, HoverAbove, 0.08f);
                if ((leftIn || rightIn) && !b.Down)
                {
                    b.Down = true;
                    XRRig.Buzz(leftIn ? rig.Left : rig.Right, 0.4f, 0.04f);
                    onPress(b.Action);
                }
                else if (b.Down && releasedFar) b.Down = false;

                // Visual: cap sinks while held and brightens; the ring lights up under a fingertip
                b.Cap.localPosition = b.Local + new Vector3(0, (b.Down ? 0.004f : CapHeight / 2f + 0.004f), 0);
                SetColour(b.CapMat, b.Down ? Color.Lerp(b.Colour, Color.white, 0.45f) : b.Colour);
                SetColour(b.RingMat, hover || b.Down ? RingHover : RingIdle);
            }
        }

        private static void SetColour(Material m, Color c)
        {
            if (m == null) return;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        /// <summary>
        /// Fingertip within (radius + margin) of the button's centre line, and between `below` under
        /// and `above` over the deck surface at the button.
        /// </summary>
        private bool InZone(XRRig.Hand h, Button b, float margin, float above, float below)
        {
            if (!h.Valid) return false;
            Vector3 p = surface.InverseTransformPoint(h.Tip) - b.Local;
            float flat = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            return flat < b.Radius + margin && p.y < above && p.y > -below;
        }
    }
}
