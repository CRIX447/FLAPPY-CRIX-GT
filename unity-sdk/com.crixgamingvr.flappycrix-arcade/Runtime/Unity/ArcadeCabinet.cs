// Builds the arcade cabinet model in code: a classic upright cabinet (sloped screen, lit
// marquee, control panel with a joystick and four buttons, coin door), with neon edges and the
// artwork from CabinetArt. No model files needed, so it works in any project and pipeline.
// The cabinet stands on its pivot and faces its blue arrow (+Z), where the player stands.
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using CrixArcade.Core;
using UnityEngine;

namespace CrixArcade
{
    [Serializable]
    public sealed class ArcadeStyle
    {
        [Tooltip("The cabinet's paint.")]
        public Color body = new Color32(0x16, 0x18, 0x26, 255);
        [Tooltip("Neon edges, stripes and the CRIX red.")]
        public Color accent = new Color32(0xFF, 0x46, 0x55, 255);
        [Tooltip("Second colour (the orange in the logo).")]
        public Color accent2 = new Color32(0xFF, 0x6B, 0x35, 255);
        public Color flapButton = new Color32(0xFF, 0x46, 0x55, 255);
        public Color startButton = new Color32(0x00, 0xCC, 0x7A, 255);
        public Color selectButton = new Color32(0x27, 0xA9, 0xF5, 255);
        public Color backButton = new Color32(0xFF, 0xD5, 0x4A, 255);
        [Tooltip("How bright the neon and the marquee glow (1 = normal). Needs bloom to really glow.")]
        [Range(0.5f, 4f)] public float glow = 1.4f;
        [Tooltip("Text on the marquee.")]
        public string title = "FLAPPY CRIX";
        [Tooltip("Size of the whole cabinet (1 = 1.94 m tall).")]
        [Range(0.6f, 1.5f)] public float scale = 1f;
    }

    public sealed class CabinetParts
    {
        public GameObject Root;
        public Renderer Screen;
        public Material ScreenMaterial;
        /// <summary>Centre of the screen, its right (player's right) and up directions, and size in metres.</summary>
        public Transform ScreenFrame;
        public Vector2 ScreenSize;
        public readonly Dictionary<ArcadeAction, ArcadeButton> Buttons = new Dictionary<ArcadeAction, ArcadeButton>();
        public ArcadeJoystick Joystick;
        public ArcadeCoinSlot CoinSlot;
        public readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
    }

    public static class ArcadeCabinet
    {
        // the side outline (z = towards the player, y = up), counter-clockwise seen from +x
        public static readonly Vector2[] Profile =
        {
            new Vector2(-0.38f, 0f), new Vector2(0.30f, 0f), new Vector2(0.30f, 0.88f), new Vector2(0.46f, 0.95f),
            new Vector2(0.46f, 1.00f), new Vector2(0.15f, 1.09f), new Vector2(0.03f, 1.60f), new Vector2(0.24f, 1.66f),
            new Vector2(0.24f, 1.94f), new Vector2(-0.38f, 1.94f),
        };
        const int EdgeFront = 1, EdgePanelUnder = 2, EdgeLip = 3, EdgePanel = 4, EdgeScreen = 5, EdgeMarqueeUnder = 6, EdgeMarquee = 7;
        public const float Width = 0.72f, SideThickness = 0.04f, Inset = 0.014f;
        const float HW = Width / 2 - SideThickness;

        // control panel layout: (metres to the player's right, fraction from the front edge to the back), radius
        struct Ctl { public ArcadeAction Action; public float R, F, Radius; public string Label; public Func<ArcadeStyle, Color> Colour; }
        static readonly Ctl[] Controls =
        {
            new Ctl { Action = ArcadeAction.Flap,   R = 0.05f, F = 0.46f, Radius = 0.048f, Label = "FLAP",   Colour = s => s.flapButton },
            new Ctl { Action = ArcadeAction.Select, R = 0.165f, F = 0.62f, Radius = 0.03f, Label = "SELECT", Colour = s => s.selectButton },
            new Ctl { Action = ArcadeAction.Back,   R = 0.255f, F = 0.62f, Radius = 0.03f, Label = "BACK",   Colour = s => s.backButton },
            new Ctl { Action = ArcadeAction.Start,  R = 0.21f, F = 0.24f, Radius = 0.026f, Label = "START",  Colour = s => s.startButton },
        };
        const float StickR = -0.17f, StickF = 0.5f;
        const float SlotX = 0.5f, SlotY = 0.6f;          // on the front picture, 0..1 from the top-left

        // ------------------------------------------------------------------ geometry helpers

        static Vector2[] InsetProfile(Vector2[] p, float d)
        {
            int n = p.Length;
            var res = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 a = p[(i + n - 1) % n], b = p[i], c = p[(i + 1) % n];
                Vector2 n0 = Out(a, b), n1 = Out(b, c);
                Vector2 bis = (n0 + n1).normalized;
                float k = Vector2.Dot(bis, n0);
                res[i] = b - bis * (d / Mathf.Max(0.3f, k));
            }
            return res;
        }

        /// <summary>Outward normal of edge a-b of a counter-clockwise (z, y) outline.</summary>
        static Vector2 Out(Vector2 a, Vector2 b) { var e = (b - a).normalized; return new Vector2(e.y, -e.x); }

        static Vector3 P3(float x, Vector2 zy) => new Vector3(x, zy.y, zy.x);

        /// <summary>A picture on edge `edge` of the outline, from fraction f0 to f1, across x = -hw..hw.</summary>
        static void Decal(CabinetMesh m, int sub, Vector2[] prof, int edge, float f0, float f1, float hw, float lift)
        {
            Vector2 p = prof[edge], q = prof[(edge + 1) % prof.Length], n = Out(p, q) * lift;
            Vector2 s = Vector2.Lerp(p, q, f0) + n, e = Vector2.Lerp(p, q, f1) + n;
            m.Quad(sub, P3(-hw, s), P3(hw, s), P3(hw, e), P3(-hw, e));
        }

        /// <summary>A point on the outline's edge: right = metres to the player's right, f = fraction along it.</summary>
        static void EdgeFrame(Vector2[] prof, int edge, float right, float f, float lift, out Vector3 pos, out Vector3 up, out Vector3 along)
        {
            Vector2 p = prof[edge], q = prof[(edge + 1) % prof.Length], n = Out(p, q);
            Vector2 at = Vector2.Lerp(p, q, f) + n * lift;
            pos = P3(-right, at);
            up = new Vector3(0, n.y, n.x);
            Vector2 d = (q - p).normalized;
            along = new Vector3(0, d.y, d.x);
        }

        static float EdgeLength(Vector2[] prof, int edge) => (prof[(edge + 1) % prof.Length] - prof[edge]).magnitude;

        // ------------------------------------------------------------------ materials

        public sealed class MaterialSet
        {
            public Material Lit, Unlit;   // templates (from the editor menu); null = find a shader at run time
            public readonly List<UnityEngine.Object> Made = new List<UnityEngine.Object>();

            static Shader Find(params string[] names)
            {
                foreach (var n in names) { var s = Shader.Find(n); if (s != null) return s; }
                return null;
            }

            static bool Urp => UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;

            public Material Make(bool lit, Color colour, Texture tex, float smooth = 0.35f, float glow = 1)
            {
                Material m;
                var template = lit ? Lit : Unlit;
                if (template != null) m = new Material(template);
                else
                {
                    var sh = lit
                        ? (Urp ? Find("Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit") : Find("Standard", "Legacy Shaders/Diffuse", "Mobile/Diffuse"))
                        : (Urp ? Find("Universal Render Pipeline/Unlit", "Unlit/Texture", "Unlit/Color") : Find("Unlit/Texture", "Unlit/Color", "Sprites/Default"));
                    if (sh == null) sh = Find("Sprites/Default", "Hidden/InternalErrorShader");
                    m = new Material(sh);
                }
                m.name = "Arcade " + (lit ? "lit" : "glow");
                Color c = lit ? colour : colour * glow;
                c.a = 1;
                m.color = c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (tex != null) { m.mainTexture = tex; if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); }
                if (lit)
                {
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
                }
                Made.Add(m);
                return m;
            }
        }

        static Texture2D Tex(Canvas c, string name, List<UnityEngine.Object> owned)
        {
            var t = new Texture2D(c.W, c.H, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixelData(c.Px, 0);
            t.Apply(true, false);
            owned.Add(t);
            return t;
        }

        // ------------------------------------------------------------------ build

        public static CabinetParts Build(ArcadeMachine owner, ArcadeStyle style, MaterialSet mats, bool freePlay)
        {
            ArcadeGame.HookResources();
            var parts = new CabinetParts();
            var root = new GameObject("Cabinet");
            root.transform.SetParent(owner.transform, false);
            root.transform.localScale = Vector3.one * style.scale;
            parts.Root = root;

            Assets art;
            try { art = SpriteSheet.LoadEmbedded(); } catch { art = new Assets(); }
            uint body = ToCol(style.body), accent = ToCol(style.accent), accent2 = ToCol(style.accent2);

            var inner = InsetProfile(Profile, Inset);
            float lift = 0.003f;

            // --- the body and the two side panels
            // submeshes: 0 body, 1 side art, 2 neon, 3 panel, 4 front, 5 bezel, 6 marquee, 7 light strip
            var m = new CabinetMesh(8);
            m.Extrude(inner, -HW, HW, 0, 0);
            m.Extrude(Profile, HW, HW + SideThickness, 1, 2);
            m.Extrude(Profile, -HW - SideThickness, -HW, 1, 2);

            // the control panel picture with rings and labels where the controls go
            float panelLen = EdgeLength(inner, EdgePanel);
            var labels = new List<CabinetArt.Label>();
            foreach (var ctl in Controls)
                labels.Add(new CabinetArt.Label { Text = ctl.Label, X = (ctl.R + HW) / (2 * HW) * 512, Y = (1 - ctl.F) * 248, Ring = ctl.Radius / (2 * HW) * 512, Colour = ToCol(ctl.Colour(style)) });
            labels.Add(new CabinetArt.Label { Text = "MOVE", X = (StickR + HW) / (2 * HW) * 512, Y = (1 - StickF) * 248, Ring = 0.04f / (2 * HW) * 512 });
            Decal(m, 3, inner, EdgePanel, 0, 1, HW, lift);
            Decal(m, 4, inner, EdgeFront, 0, 1, HW, lift);
            Decal(m, 5, inner, EdgeScreen, 0, 1, HW, lift);
            Decal(m, 6, inner, EdgeMarquee, 0.03f, 0.97f, HW, lift);
            Decal(m, 2, inner, EdgeLip, 0.15f, 0.85f, HW, lift);
            Decal(m, 7, inner, EdgeMarqueeUnder, 0.25f, 0.75f, HW - 0.02f, lift);
            Decal(m, 2, inner, EdgePanelUnder, 0.0f, 0.12f, HW, lift);

            // the screen: 3:2, as wide as fits
            float screenLen = EdgeLength(inner, EdgeScreen);
            float sw = Mathf.Min(2 * HW - 0.05f, (screenLen - 0.08f) * 1.5f), sh = sw / 1.5f;
            float sf0 = 0.5f - sh / screenLen / 2 + 0.02f, sf1 = sf0 + sh / screenLen;

            var mesh = m.ToMesh("Flappy Crix cabinet");
            parts.Owned.Add(mesh);
            var sideTex = Tex(CabinetArt.Side(art, body, accent, accent2), "Cabinet side", parts.Owned);
            var panelTex = Tex(CabinetArt.Panel(body, accent, labels), "Cabinet panel", parts.Owned);
            var frontTex = Tex(CabinetArt.Front(body, accent, freePlay, SlotX, SlotY), "Cabinet front", parts.Owned);
            var bezelTex = Tex(CabinetArt.Bezel(accent, (HW - sw / 2) / (2 * HW), 1 - sf1, sw / (2 * HW), sf1 - sf0), "Cabinet bezel", parts.Owned);
            var marqueeTex = Tex(CabinetArt.Marquee(art, accent, accent2, style.title), "Cabinet marquee", parts.Owned);

            var go = new GameObject("Body");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[]
            {
                mats.Make(true, style.body, null, 0.45f),
                mats.Make(true, Color.white, sideTex, 0.5f),
                mats.Make(false, style.accent, null, 0, style.glow),
                mats.Make(true, Color.white, panelTex, 0.55f),
                mats.Make(true, Color.white, frontTex, 0.4f),
                mats.Make(true, Color.white, bezelTex, 0.8f),
                mats.Make(false, Color.white, marqueeTex, 0, Mathf.Max(1, style.glow * 0.8f)),
                mats.Make(false, new Color(1f, 0.95f, 0.85f), null, 0, style.glow),
            };

            // --- the screen
            var sm = new CabinetMesh(1);
            Decal(sm, 0, inner, EdgeScreen, sf0, sf1, sw / 2, lift * 2);
            var smesh = sm.ToMesh("Flappy Crix screen");
            parts.Owned.Add(smesh);
            var sgo = new GameObject("Screen");
            sgo.transform.SetParent(root.transform, false);
            sgo.AddComponent<MeshFilter>().sharedMesh = smesh;
            var smr = sgo.AddComponent<MeshRenderer>();
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            parts.ScreenMaterial = mats.Make(false, Color.white, null, 0, 1);
            parts.ScreenMaterial.name = "Arcade screen";
            smr.sharedMaterial = parts.ScreenMaterial;
            parts.Screen = smr;

            Vector3 sp, sup, salong;
            EdgeFrame(inner, EdgeScreen, 0, (sf0 + sf1) / 2, lift * 2, out sp, out sup, out salong);
            var frame = new GameObject("Screen frame").transform;
            frame.SetParent(root.transform, false);
            frame.localPosition = sp;
            frame.localRotation = Quaternion.LookRotation(-sup, salong);   // forward into the screen, up = screen up
            parts.ScreenFrame = frame;
            parts.ScreenSize = new Vector2(sw, sh);

            // --- buttons
            foreach (var ctl in Controls)
            {
                Vector3 pos, up, along;
                EdgeFrame(inner, EdgePanel, ctl.R, ctl.F, lift, out pos, out up, out along);
                var b = new GameObject(ctl.Label + " button");
                b.transform.SetParent(root.transform, false);
                b.transform.localPosition = pos;
                b.transform.localRotation = Quaternion.LookRotation(along, up);
                var ring = new CabinetMesh(1);
                ring.Cylinder(0, Vector3.zero, Vector3.up, ctl.Radius + 0.008f, 0.008f, 24);
                AddMesh(b, ring, "ring", mats.Make(true, new Color(0.08f, 0.08f, 0.1f), null, 0.6f), parts);
                var capGo = new GameObject("Cap");
                capGo.transform.SetParent(b.transform, false);
                var cap = new CabinetMesh(2);
                cap.Cylinder(0, new Vector3(0, 0.004f, 0), Vector3.up, ctl.Radius, 0.018f, 28, 1);
                var col = ctl.Colour(style);
                var capR = capGo.AddComponent<MeshRenderer>();
                capGo.AddComponent<MeshFilter>().sharedMesh = Keep(cap.ToMesh("cap"), parts);
                capR.sharedMaterials = new[] { mats.Make(true, col * 0.8f, null, 0.8f), mats.Make(false, col, null, 0, style.glow * 0.9f) };
                var btn = b.AddComponent<ArcadeButton>();
                btn.machine = owner; btn.action = ctl.Action; btn.cap = capGo.transform; btn.radius = ctl.Radius;
                parts.Buttons[ctl.Action] = btn;
            }

            // --- joystick
            {
                Vector3 pos, up, along;
                EdgeFrame(inner, EdgePanel, StickR, StickF, lift, out pos, out up, out along);
                var j = new GameObject("Joystick");
                j.transform.SetParent(root.transform, false);
                j.transform.localPosition = pos;
                j.transform.localRotation = Quaternion.LookRotation(along, up);
                var plate = new CabinetMesh(1);
                plate.Cylinder(0, Vector3.zero, Vector3.up, 0.04f, 0.006f, 24);
                AddMesh(j, plate, "plate", mats.Make(true, new Color(0.05f, 0.05f, 0.07f), null, 0.7f), parts);
                var stickGo = new GameObject("Stick");
                stickGo.transform.SetParent(j.transform, false);
                var stick = new CabinetMesh(1);
                stick.Cylinder(0, Vector3.zero, Vector3.up, 0.007f, 0.085f, 12);
                AddMesh(stickGo, stick, "stick", mats.Make(true, new Color(0.75f, 0.77f, 0.8f), null, 0.9f), parts);
                var ballGo = new GameObject("Ball");
                ballGo.transform.SetParent(stickGo.transform, false);
                ballGo.transform.localPosition = new Vector3(0, 0.095f, 0);
                var ball = new CabinetMesh(1);
                ball.Sphere(0, Vector3.zero, 0.024f, 18);
                AddMesh(ballGo, ball, "ball", mats.Make(true, style.accent, null, 0.85f), parts);
                var js = j.AddComponent<ArcadeJoystick>();
                js.machine = owner; js.stick = stickGo.transform; js.ball = ballGo.transform;
                parts.Joystick = js;
            }

            // --- coin slot (on the front picture's slot)
            {
                Vector3 pos, up, along;
                EdgeFrame(inner, EdgeFront, (SlotX - 0.5f) * 2 * HW, 1 - SlotY, lift, out pos, out up, out along);
                var slot = new GameObject("Coin slot");
                slot.transform.SetParent(root.transform, false);
                slot.transform.localPosition = pos;
                slot.transform.localRotation = Quaternion.LookRotation(along, up);
                var cs = slot.AddComponent<ArcadeCoinSlot>();
                cs.machine = owner;
                parts.CoinSlot = cs;
            }

            parts.Owned.AddRange(mats.Made);
            return parts;
        }

        /// <summary>Simple solid boxes so players can't walk through the cabinet (the panel and screen stay reachable).</summary>
        public static void AddColliders(GameObject root)
        {
            var lower = root.AddComponent<BoxCollider>();
            lower.center = new Vector3(0, 0.44f, -0.04f); lower.size = new Vector3(Width, 0.88f, 0.68f);
            var upper = root.AddComponent<BoxCollider>();
            upper.center = new Vector3(0, 1.52f, -0.19f); upper.size = new Vector3(Width, 0.84f, 0.38f);
        }

        static Mesh Keep(Mesh m, CabinetParts p) { p.Owned.Add(m); return m; }

        static void AddMesh(GameObject go, CabinetMesh m, string name, Material mat, CabinetParts parts)
        {
            go.AddComponent<MeshFilter>().sharedMesh = Keep(m.ToMesh(name), parts);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static uint ToCol(Color c) { Color32 k = c; return Col.Rgb(k.r, k.g, k.b); }
    }
}
