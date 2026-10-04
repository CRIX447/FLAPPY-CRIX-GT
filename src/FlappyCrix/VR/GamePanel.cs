using UnityEngine;

namespace FlappyCrix.VR
{
    /// <summary>
    /// The world-space screen: an orange frame, a black backing and the picture, all solid
    /// (depth-tested) so nothing draws through hands or walls. The picture is one texture -
    /// the website stream or the native game - so its parts can't draw out of order.
    /// No colliders: hits are computed against the panel's plane.
    /// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
    /// </summary>
    public sealed class GamePanel
    {
        public readonly Transform Root;      // placed in the world, faces away from the player
        public readonly Transform Surface;   // unit quad scaled to width x height (metres)
        private readonly MeshRenderer surfaceRenderer;
        private readonly Transform backing, frame;
        private Texture shown;
        private Vector2 shownScale, shownOffset;

        public float WidthM { get; private set; }
        public float HeightM { get; private set; }

        public GamePanel(Transform parent)
        {
            Root = new GameObject("FlappyCrixPanel").transform;
            Root.SetParent(parent, false);

            // Layers only matter for the fallback shaders; with the normal opaque shader the
            // 5 mm / 10 mm gaps behind the picture decide what's in front.
            frame = Visuals.Quad("Frame", Root, Visuals.Material(null, Visuals.Hex("#FF6B35"), 0)).transform;
            frame.localPosition = new Vector3(0, 0, 0.010f);
            backing = Visuals.Quad("Backing", Root, Visuals.Material(null, Color.black, 1)).transform;
            backing.localPosition = new Vector3(0, 0, 0.005f);

            Surface = Visuals.Quad("Surface", Root, Visuals.Material(null, Color.black, 2)).transform;
            surfaceRenderer = Surface.GetComponent<MeshRenderer>();
        }

        public void SetSize(float widthM, float aspect)
        {
            WidthM = widthM;
            HeightM = widthM / Mathf.Max(0.1f, aspect);
            Surface.localScale = new Vector3(WidthM, HeightM, 1);
            backing.localScale = new Vector3(WidthM, HeightM, 1);
            float b = Mathf.Clamp(widthM * 0.03f, 0.01f, 0.03f);
            frame.localScale = new Vector3(WidthM + b * 2, HeightM + b * 2, 1);
        }

        /// <summary>
        /// Show a texture. uvScale/uvOffset pick the part to show (for example the page area of
        /// a browser frame, or (1,-1)/(0,1) to flip a top-down texture).
        /// </summary>
        public void ShowTexture(Texture tex, Vector2 uvScale, Vector2 uvOffset)
        {
            if (tex == shown && uvScale == shownScale && uvOffset == shownOffset) return;
            shown = tex; shownScale = uvScale; shownOffset = uvOffset;
            var m = surfaceRenderer.sharedMaterial;
            m.mainTexture = tex != null ? tex : Visuals.White;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", m.mainTexture);
            m.color = tex != null ? Color.white : Color.black;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color);
            m.mainTextureScale = uvScale;
            m.mainTextureOffset = uvOffset;
            if (m.HasProperty("_BaseMap")) { m.SetTextureScale("_BaseMap", uvScale); m.SetTextureOffset("_BaseMap", uvOffset); }
        }

        public bool Visible
        {
            get => Root.gameObject.activeInHierarchy;
            set => Root.gameObject.SetActive(value);
        }

        /// <summary>
        /// Ray against the panel. Returns UV with the origin at the TOP-LEFT (browser convention).
        /// </summary>
        public bool Raycast(Ray ray, out Vector2 uv, out Vector3 hit, out float distance)
        {
            uv = default; hit = default; distance = 0;
            if (!Visible) return false;
            var plane = new Plane(-Surface.forward, Surface.position);
            if (!plane.Raycast(ray, out distance) || distance <= 0) return false;
            if (Vector3.Dot(ray.direction, Surface.forward) <= 0) return false;   // from behind
            hit = ray.GetPoint(distance);
            Vector3 local = Surface.InverseTransformPoint(hit);                    // -0.5..0.5
            if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f) return false;
            uv = new Vector2(local.x + 0.5f, 0.5f - local.y);
            return true;
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
        }
    }
}
