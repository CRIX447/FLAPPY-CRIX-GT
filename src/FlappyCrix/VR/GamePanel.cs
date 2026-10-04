using UnityEngine;

namespace FlappyCrix.VR
{
    /// <summary>
    /// The world-space screen. A frame, a black backing and a surface quad that shows
    /// the browser texture (or hosts the native game's geometry). No colliders: hits are
    /// computed against the panel's plane, so nothing here can be climbed or bumped.
    /// </summary>
    public sealed class GamePanel
    {
        public readonly Transform Root;      // placed in the world, faces away from the player
        public readonly Transform Surface;   // unit quad scaled to width x height (metres)
        private readonly MeshRenderer surfaceRenderer;
        private readonly Transform backing, frame;
        private Texture shown;

        public float WidthM { get; private set; }
        public float HeightM { get; private set; }

        public GamePanel(Transform parent)
        {
            Root = new GameObject("FlappyCrixPanel").transform;
            Root.SetParent(parent, false);

            frame = Visuals.Quad("Frame", Root, Visuals.Material(null, Visuals.Hex("#FF6B35"), 0)).transform;
            frame.localPosition = new Vector3(0, 0, 0.004f);
            backing = Visuals.Quad("Backing", Root, Visuals.Material(null, Color.black, 1)).transform;
            backing.localPosition = new Vector3(0, 0, 0.002f);

            Surface = Visuals.Quad("Surface", Root, Visuals.Material(null, Color.black, 2)).transform;
            surfaceRenderer = Surface.GetComponent<MeshRenderer>();
        }

        public void SetSize(float widthM, float aspect)
        {
            WidthM = widthM;
            HeightM = widthM / Mathf.Max(0.1f, aspect);
            Surface.localScale = new Vector3(WidthM, HeightM, 1);
            backing.localScale = new Vector3(WidthM, HeightM, 1);
            float b = 0.025f;
            frame.localScale = new Vector3(WidthM + b * 2, HeightM + b * 2, 1);
        }

        /// <summary>Show a browser texture. CEF paints top-down, so V is flipped.</summary>
        public void ShowTexture(Texture tex)
        {
            if (tex == shown) return;
            shown = tex;
            var m = surfaceRenderer.sharedMaterial;
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.color = tex != null ? Color.white : Color.black;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color);
            m.mainTextureScale = new Vector2(1, -1);
            m.mainTextureOffset = new Vector2(0, 1);
            if (m.HasProperty("_BaseMap")) { m.SetTextureScale("_BaseMap", new Vector2(1, -1)); m.SetTextureOffset("_BaseMap", new Vector2(0, 1)); }
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
