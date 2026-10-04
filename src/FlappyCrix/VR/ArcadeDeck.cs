using System.Collections.Generic;
using UnityEngine;

namespace FlappyCrix.VR
{
    /// <summary>
    /// The arcade control deck that sits in front of the screen: a joystick for menu
    /// navigation and four push buttons - FLAP, SELECT, START, PAUSE. Press a button by
    /// pushing your hand down onto it. The deck stays where it was placed.
    /// No colliders: presses are measured from the controller positions.
    /// </summary>
    public sealed class ArcadeDeck
    {
        public enum DeckButton { Flap, Select, Start, Pause }

        private sealed class Button
        {
            public DeckButton Action;
            public Vector3 Local;          // centre of the cap's base, in surface space
            public float Radius;
            public Transform Cap;
            public Material CapMat;
            public Color Colour;
            public bool Down;
        }

        public readonly Transform Root;      // placed in the world (yaw only)
        public readonly VRJoystick Joystick;
        private readonly Transform surface;  // tilted top of the deck, scale 1
        private readonly List<Button> buttons = new List<Button>();

        const float CapHeight = 0.018f;

        public ArcadeDeck(Transform parent)
        {
            Root = new GameObject("ArcadeDeck").transform;
            Root.SetParent(parent, false);

            surface = new GameObject("Surface").transform;
            surface.SetParent(Root, false);
            surface.localRotation = Quaternion.Euler(-15f, 0, 0);   // top tilts towards the player

            // Cabinet body and its top plate
            var body = Visuals.Primitive(PrimitiveType.Cube, "Body", surface, Visuals.Hex("#1A1A24"), 0).transform;
            body.localScale = new Vector3(0.6f, 0.08f, 0.26f);
            body.localPosition = new Vector3(0, -0.04f, 0);
            var top = Visuals.Primitive(PrimitiveType.Cube, "Top", surface, Visuals.Hex("#2B2140"), 1).transform;
            top.localScale = new Vector3(0.58f, 0.004f, 0.24f);
            top.localPosition = new Vector3(0, 0.002f, 0);
            var trim = Visuals.Primitive(PrimitiveType.Cube, "Trim", surface, Visuals.Hex("#FF6B35"), 2).transform;
            trim.localScale = new Vector3(0.6f, 0.012f, 0.008f);
            trim.localPosition = new Vector3(0, -0.002f, -0.132f);
            var stand = Visuals.Primitive(PrimitiveType.Cube, "Stand", Root, Visuals.Hex("#111116"), 0).transform;
            stand.localScale = new Vector3(0.12f, 0.5f, 0.12f);
            stand.localPosition = new Vector3(0, -0.3f, 0.02f);

            Joystick = new VRJoystick(surface);
            Joystick.Root.localPosition = new Vector3(-0.19f, 0.004f, 0f);
            Label("MENU", new Vector3(-0.19f, 0.005f, -0.085f), Color.white);

            AddButton(DeckButton.Select, "SELECT", new Vector3(-0.055f, 0, 0.0f), 0.026f, Visuals.Hex("#00CC7A"));
            AddButton(DeckButton.Start,  "START",  new Vector3( 0.025f, 0, 0.0f), 0.026f, Visuals.Hex("#3A8DFF"));
            AddButton(DeckButton.Pause,  "PAUSE",  new Vector3( 0.105f, 0, 0.0f), 0.026f, Visuals.Hex("#FFC107"));
            AddButton(DeckButton.Flap,   "FLAP",   new Vector3( 0.205f, 0, 0.0f), 0.045f, Visuals.Hex("#FF6B35"));
        }

        private void AddButton(DeckButton action, string label, Vector3 at, float radius, Color colour)
        {
            var ring = Visuals.Primitive(PrimitiveType.Cylinder, label + "Ring", surface, Visuals.Hex("#0D0D12"), 3).transform;
            ring.localScale = new Vector3(radius * 2.5f, 0.004f, radius * 2.5f);
            ring.localPosition = at + new Vector3(0, 0.004f, 0);

            var capGo = Visuals.Primitive(PrimitiveType.Cylinder, label, surface, colour, 4);
            var cap = capGo.transform;
            cap.localScale = new Vector3(radius * 2f, CapHeight / 2f, radius * 2f);
            cap.localPosition = at + new Vector3(0, CapHeight / 2f + 0.004f, 0);

            Label(label, at + new Vector3(0, 0.005f, -radius - 0.03f), Color.white);
            buttons.Add(new Button { Action = action, Local = at, Radius = radius, Cap = cap,
                                     CapMat = capGo.GetComponent<MeshRenderer>().sharedMaterial, Colour = colour });
        }

        private void Label(string text, Vector3 at, Color colour)
        {
            var t = Visuals.Text(text + "Label", surface, 60, colour, TextAnchor.MiddleCenter);
            t.text = text;
            t.characterSize = 0.0035f;
            t.transform.localPosition = at;
            t.transform.localRotation = Quaternion.Euler(90f, 0, 0);   // lying on the deck, readable from the player's side
        }

        public bool Visible
        {
            get => Root.gameObject.activeSelf;
            set => Root.gameObject.SetActive(value);
        }

        /// <summary>Checks both hands against every button; calls onPress once per press.</summary>
        public void Update(XRRig rig, System.Action<DeckButton> onPress)
        {
            foreach (var b in buttons)
            {
                bool touching = Touching(rig.Left, b) || Touching(rig.Right, b);
                bool releasedFar = !Near(rig.Left, b) && !Near(rig.Right, b);
                if (touching && !b.Down)
                {
                    b.Down = true;
                    var hand = Touching(rig.Left, b) ? rig.Left : rig.Right;
                    XRRig.Buzz(hand, 0.4f, 0.04f);
                    onPress(b.Action);
                }
                else if (b.Down && releasedFar) b.Down = false;

                // Visual: cap sinks while held, brightens
                b.Cap.localPosition = b.Local + new Vector3(0, (b.Down ? 0.004f : CapHeight / 2f + 0.004f), 0);
                b.CapMat.color = b.Down ? Color.Lerp(b.Colour, Color.white, 0.45f) : b.Colour;
            }
        }

        // Hand pushed onto the cap: within the button's footprint and down at cap height.
        private bool Touching(XRRig.Hand h, Button b)
        {
            if (!h.Valid) return false;
            Vector3 p = surface.InverseTransformPoint(h.Position) - b.Local;
            float flat = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            return flat < b.Radius + 0.02f && p.y < CapHeight + 0.025f && p.y > -0.06f;
        }

        // Re-arm only once the hand has clearly come away.
        private bool Near(XRRig.Hand h, Button b)
        {
            if (!h.Valid) return false;
            Vector3 p = surface.InverseTransformPoint(h.Position) - b.Local;
            float flat = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            return flat < b.Radius + 0.045f && p.y < 0.07f && p.y > -0.08f;
        }
    }
}
