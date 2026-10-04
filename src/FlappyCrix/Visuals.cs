using System.IO;
using UnityEngine;

namespace FlappyCrix
{
    /// <summary>Small helpers for building unlit, collider-free geometry at runtime.</summary>
    internal static class Visuals
    {
        private static Shader cached;

        /// <summary>
        /// An unlit shader that is guaranteed to exist in the build. Gorilla Tag's own shaders
        /// are not used so the mod does not depend on them; UI/Default and Sprites/Default are
        /// always-included built-in shaders.
        /// </summary>
        public static Shader UnlitShader
        {
            get
            {
                if (cached != null) return cached;
                foreach (var name in new[] { "Universal Render Pipeline/Unlit", "Unlit/Texture", "UI/Default", "Sprites/Default", "Unlit/Transparent" })
                {
                    var s = Shader.Find(name);
                    if (s != null && s.isSupported) { cached = s; break; }
                }
                return cached;
            }
        }

        public static Material Material(Texture tex, Color color, int queueOffset = 0)
        {
            var m = new Material(UnlitShader);
            if (tex != null)
            {
                m.mainTexture = tex;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            }
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            m.renderQueue = 3000 + queueOffset;
            return m;
        }

        /// <summary>A quad with no collider - colliders on Default could be climbed in Gorilla Tag.</summary>
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

        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Color color, int queueOffset = 0)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            RemoveColliders(go);
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Material(null, color, queueOffset);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
                    Object.DestroyImmediate(c);
        }

        public static Texture2D LoadPng(string path)
        {
            if (!File.Exists(path)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(t, File.ReadAllBytes(path))) return null;
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            return t;
        }

        /// <summary>Horizontal gradient texture from colour stops (0..1).</summary>
        public static Texture2D Gradient(int width, int height, bool horizontal, float[] at, Color[] colors)
        {
            var t = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            int n = horizontal ? width : height;
            for (int i = 0; i < n; i++)
            {
                float f = n == 1 ? 0 : i / (float)(n - 1);
                Color c = colors[colors.Length - 1];
                for (int k = 0; k < at.Length - 1; k++)
                    if (f >= at[k] && f <= at[k + 1]) { c = Color.Lerp(colors[k], colors[k + 1], Mathf.InverseLerp(at[k], at[k + 1], f)); break; }
                for (int j = 0; j < (horizontal ? height : width); j++)
                    if (horizontal) t.SetPixel(i, j, c); else t.SetPixel(j, i, c);
            }
            t.Apply(false);
            return t;
        }

        public static Color Hex(string hex)
        {
            Color c; ColorUtility.TryParseHtmlString(hex, out c); return c;
        }

        public static Font DefaultFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f == null) try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            return f;
        }

        public static TextMesh Text(string name, Transform parent, int fontSize, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            var font = DefaultFont();
            if (font != null)
            {
                tm.font = font;
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = font.material;
            }
            tm.fontSize = fontSize;
            tm.characterSize = 1f;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            return tm;
        }
    }
}
