using UnityEngine;

namespace FlappyCrix.VR
{
    /// <summary>
    /// Point a controller at the panel; the trigger is a left mouse button. This is how the
    /// website's own menus (start, retry, store, settings, tutorial) are used in VR.
    /// </summary>
    public sealed class LaserPointer
    {
        private readonly LineRenderer line;
        private readonly Transform dot;
        private bool down;
        private Vector2 downUv;

        public bool IsOverPanel { get; private set; }

        /// <summary>Downward tilt of the ray from the controller's forward, degrees.</summary>
        public float PitchDegrees = 30f;

        public LaserPointer()
        {
            var go = new GameObject("FlappyCrixLaser");
            Object.DontDestroyOnLoad(go);
            line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = 0.004f;
            line.endWidth = 0.002f;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.sharedMaterial = Visuals.Material(null, new Color(1f, 0.42f, 0.21f, 0.8f), 10);
            dot = Visuals.Primitive(PrimitiveType.Sphere, "LaserDot", go.transform, Color.white, 11).transform;
            dot.localScale = Vector3.one * 0.015f;
            SetVisible(false);
        }

        private void SetVisible(bool v)
        {
            line.enabled = v;
            dot.gameObject.SetActive(v);
        }

        /// <param name="hand">Pose and trigger of the pointing hand.</param>
        public void Update(XRRig.Hand hand, GamePanel panel, IFlappyGame game)
        {
            IsOverPanel = false;
            if (!hand.Valid || game == null || !panel.Visible) { Release(game); SetVisible(false); return; }

            // Controllers point slightly down from their "forward"; tilt the ray to match how people hold them.
            Vector3 dir = hand.Rotation * Quaternion.Euler(PitchDegrees, 0, 0) * Vector3.forward;
            var ray = new Ray(hand.Position, dir);

            Vector2 uv; Vector3 hit; float dist;
            if (panel.Raycast(ray, out uv, out hit, out dist) && dist < 12f)
            {
                IsOverPanel = true;
                SetVisible(true);
                line.SetPosition(0, hand.Position);
                line.SetPosition(1, hit);
                dot.position = hit - panel.Surface.forward * 0.003f;

                game.PointerMove(uv);
                if (hand.TriggerPressed && !down)
                {
                    down = true; downUv = uv;
                    game.PointerDown(uv);
                    XRRig.Buzz(hand, 0.15f, 0.02f);
                }
                else if (!hand.TriggerPressed && down)
                {
                    down = false;
                    game.PointerUp(uv);
                }
                // Thumbstick on the pointing hand scrolls (store/locker lists) while held over the panel.
                if (Mathf.Abs(hand.Stick.y) > 0.6f && hand.GripPressed)
                    game.Scroll(uv, hand.Stick.y > 0 ? 60 : -60);
            }
            else
            {
                Release(game);
                SetVisible(false);
            }
        }

        private void Release(IFlappyGame game)
        {
            if (down) { down = false; game?.PointerUp(downUv); }
        }

        public void Destroy()
        {
            if (line != null) Object.Destroy(line.gameObject);
        }
    }
}
