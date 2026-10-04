// Runtime-built geometry and materials for the screen, the arcade deck and the laser.
// Everything uses one solid (opaque, depth-tested) unlit material, so pieces
// draw in the right order and nothing shows through hands or walls. Text is drawn
// with PixelFont into textures - Unity's TextMesh shader draws on top of everything
// in Gorilla Tag, which is what made the old menu text only visible behind a hand.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.IO;
using UnityEngine;

namespace FlappyCrix
{
    internal static class Visuals
    {
        private static Shader shader;
        private static bool transparentFallback;
        private static Material primitiveDefault;
        private static Texture2D white;
        public static Action<string> Log;
        public static string ShaderOverride;

        /// <summary>
        /// An unlit shader that exists in this build. Opaque, depth-tested shaders first; the
        /// always-included UI/Sprites shaders are the last resort (they need explicit draw order).
        /// </summary>
        public static Shader UnlitShader
        {
            get
            {
                if (shader != null) return shader;
                var names = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(ShaderOverride)) names.Add(ShaderOverride);
                names.AddRange(new[] { "Universal Render Pipeline/Unlit", "Unlit/Texture", "UI/Default", "Sprites/Default" });
                foreach (var n in names)
                {
                    var s = Shader.Find(n);
                    if (s != null && s.isSupported) { shader = s; break; }
                }
                if (shader == null)
                {
                    // Last resort: whatever material the engine gives a new primitive.
                    var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    primitiveDefault = tmp.GetComponent<MeshRenderer>().sharedMaterial;
                    shader = primitiveDefault != null ? primitiveDefault.shader : null;
                    UnityEngine.Object.Destroy(tmp);
                }
                string sn = shader != null ? shader.name : "(none)";
                transparentFallback = sn.StartsWith("UI/") || sn.StartsWith("Sprites/");
                Log?.Invoke("Rendering with shader: " + sn + (transparentFallback ? " (fallback; draw order set manually)" : ""));
                return shader;
            }
        }

        public static Texture2D White
        {
            get
            {
                if (white != null) return white;
                white = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++) white.SetPixel(x, y, Color.white);
                white.Apply(false);
                return white;
            }
        }

        /// <param name="layer">Draw order among overlapping flat pieces, only used by the fallback shaders.</param>
        public static Material Material(Texture tex, Color color, int layer = 0)
        {
            var s = UnlitShader;
            var m = s != null ? new Material(s) : new Material(primitiveDefault);
            m.mainTexture = tex != null ? tex : White;
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", m.mainTexture);
            if (transparentFallback) m.renderQueue = 3000 + layer;
            return m;
        }

        /// <summary>A quad with no collider (colliders could be climbed in Gorilla Tag). Visible side faces -Z.</summary>
        public static GameObject Quad(string name, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            RemoveColliders(go);
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Color color, int layer = 0)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            RemoveColliders(go);
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Material(null, color, layer);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// CreatePrimitive adds a collider. Remove it without naming the Collider type, so this
        /// assembly doesn't depend on the physics module at all.
        /// </summary>
        private static void RemoveColliders(GameObject go)
        {
            foreach (var c in go.GetComponents<Component>())
                if (!(c is Transform) && !(c is MeshFilter) && !(c is Renderer))
                    UnityEngine.Object.DestroyImmediate(c);
        }

        /// <summary>
        /// A flat label (pixel-font text on a plate) lying in the parent's XZ plane, readable
        /// from the -Z side. heightM = text height in metres.
        /// </summary>
        public static Transform Label(string text, Transform parent, Vector3 at, float heightM, string textHex, string plateHex)
        {
            const int scale = 3, pad = 3;
            int tw = PixelFont.Measure(text, scale) + pad * 2, th = PixelFont.GlyphH * scale + pad * 2;
            var px = new uint[tw * th];
            uint plate = PixelFont.Rgba(plateHex);
            for (int i = 0; i < px.Length; i++) px[i] = plate;
            PixelFont.Draw(px, tw, th, text, pad, pad, scale, PixelFont.Rgba(textHex));
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            Upload(tex, px, tw, th, true);

            var q = Quad(text + "Label", parent, Material(tex, Color.white, 6)).transform;
            float hM = heightM * th / (PixelFont.GlyphH * scale);
            q.localScale = new Vector3(hM * tw / th, hM, 1);
            q.localPosition = at;
            q.localRotation = Quaternion.Euler(90f, 0, 0);   // lying flat, face up, text reading away from the player
            return q;
        }

        /// <summary>
        /// Uploads an RGBA buffer whose rows are top-down. flipRows = true writes it bottom-up
        /// (Unity's order) so the texture can be shown without flipping.
        /// </summary>
        public static void Upload(Texture2D tex, uint[] topDown, int w, int h, bool flipRows, Color32[] scratch = null)
        {
            var colors = scratch != null && scratch.Length == w * h ? scratch : new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int src = (flipRows ? (h - 1 - y) : y) * w, dst = y * w;
                for (int x = 0; x < w; x++)
                {
                    uint c = topDown[src + x];
                    colors[dst + x] = new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24));
                }
            }
            tex.SetPixels32(colors);
            tex.Apply(false);
        }

        public static Texture2D LoadPng(string path)
        {
            if (!File.Exists(path)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(t, File.ReadAllBytes(path))) return null;
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>Texture pixels as RGBA uints, rows top-down (for the native renderer's sprites).</summary>
        public static uint[] ToTopDown(Texture2D t)
        {
            var c = t.GetPixels32();
            int w = t.width, h = t.height;
            var px = new uint[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = c[(h - 1 - y) * w + x];
                    px[y * w + x] = PixelFont.Pack(p.r, p.g, p.b, p.a);
                }
            return px;
        }

        public static Color Hex(string hex)
        {
            Color c; ColorUtility.TryParseHtmlString(hex, out c); return c;
        }
    }
}
