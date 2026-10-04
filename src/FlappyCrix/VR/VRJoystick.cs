using UnityEngine;

namespace FlappyCrix.VR
{
    /// <summary>
    /// Grabbable arcade joystick for navigating the website's menus. Hold grip near the knob
    /// to take it; push it (forward = up, back = down, left, right) past NavAngle to move the
    /// highlight one step, held = repeats. Releasing springs it back to centre.
    /// It sits on the arcade deck and never follows the player.
    /// </summary>
    public sealed class VRJoystick
    {
        const float StickLength = 0.14f, GrabRadius = 0.1f, MaxTilt = 35f;
        const float FirstRepeat = 0.45f, Repeat = 0.2f;

        public readonly Transform Root;
        private readonly Transform pivot, knob;
        private readonly Material knobMat;
        private readonly Color idle = Visuals.Hex("#E8283C"), held = Visuals.Hex("#FF6B6B");

        private int grabbingHand;           // 0 none, 1 left, 2 right
        private Quaternion tilt = Quaternion.identity;
        private int lastDx, lastDy;
        private float repeatAt;

        public float NavAngle = 18f;
        public bool Grabbed => grabbingHand != 0;

        public VRJoystick(Transform parent)
        {
            Root = new GameObject("Joystick").transform;
            Root.SetParent(parent, false);

            var plate = Visuals.Primitive(PrimitiveType.Cylinder, "Base", Root, Visuals.Hex("#111116"), 4).transform;
            plate.localScale = new Vector3(0.11f, 0.006f, 0.11f);
            var boot = Visuals.Primitive(PrimitiveType.Sphere, "Boot", Root, Visuals.Hex("#222228"), 5).transform;
            boot.localScale = new Vector3(0.05f, 0.02f, 0.05f);

            pivot = new GameObject("Pivot").transform;
            pivot.SetParent(Root, false);
            pivot.localPosition = new Vector3(0, 0.008f, 0);

            var shaft = Visuals.Primitive(PrimitiveType.Cylinder, "Shaft", pivot, Visuals.Hex("#C8C8D0"), 6).transform;
            shaft.localScale = new Vector3(0.014f, StickLength / 2, 0.014f);
            shaft.localPosition = new Vector3(0, StickLength / 2, 0);

            var k = Visuals.Primitive(PrimitiveType.Sphere, "Knob", pivot, idle, 7);
            knob = k.transform;
            knob.localScale = Vector3.one * 0.05f;
            knob.localPosition = new Vector3(0, StickLength, 0);
            knobMat = k.GetComponent<MeshRenderer>().sharedMaterial;
        }

        /// <summary>One navigation step this frame: dx (-1 left, +1 right), dy (+1 up, -1 down).</summary>
        public bool Update(XRRig rig, out int dx, out int dy)
        {
            dx = 0; dy = 0;

            if (grabbingHand == 0)
            {
                if (TryGrab(rig.Left)) grabbingHand = 1;
                else if (TryGrab(rig.Right)) grabbingHand = 2;
                if (grabbingHand != 0) XRRig.Buzz(grabbingHand == 1 ? rig.Left : rig.Right, 0.2f, 0.03f);
            }
            XRRig.Hand h = grabbingHand == 1 ? rig.Left : rig.Right;
            if (grabbingHand != 0 && (!h.Valid || !h.GripPressed)) grabbingHand = 0;

            if (grabbingHand != 0)
            {
                Vector3 local = Root.InverseTransformPoint(h.Position) - pivot.localPosition;
                if (local.y < 0.02f) local.y = 0.02f;
                Quaternion want = Quaternion.FromToRotation(Vector3.up, local.normalized);
                float angle = Quaternion.Angle(Quaternion.identity, want);
                tilt = angle > MaxTilt ? Quaternion.Slerp(Quaternion.identity, want, MaxTilt / angle) : want;
            }
            else tilt = Quaternion.Slerp(tilt, Quaternion.identity, Time.deltaTime * 14f);
            pivot.localRotation = tilt;
            knobMat.color = grabbingHand != 0 ? held : idle;

            // Direction in the deck's frame: +z = away from the player = "up"
            Vector3 dir = tilt * Vector3.up;
            float fwd = Mathf.Atan2(dir.z, dir.y) * Mathf.Rad2Deg;
            float side = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            int cx = 0, cy = 0;
            if (Mathf.Abs(fwd) >= Mathf.Abs(side)) { if (fwd > NavAngle) cy = 1; else if (fwd < -NavAngle) cy = -1; }
            else { if (side > NavAngle) cx = 1; else if (side < -NavAngle) cx = -1; }

            float now = Time.realtimeSinceStartup;
            bool fire = false;
            if ((cx != 0 || cy != 0) && (cx != lastDx || cy != lastDy)) { fire = true; repeatAt = now + FirstRepeat; }
            else if ((cx != 0 || cy != 0) && now >= repeatAt) { fire = true; repeatAt = now + Repeat; }
            lastDx = cx; lastDy = cy;

            if (fire)
            {
                dx = cx; dy = cy;
                XRRig.Buzz(h, 0.15f, 0.02f);
            }
            return fire;
        }

        private bool TryGrab(XRRig.Hand h) =>
            h.Valid && h.GripPressed && Vector3.Distance(h.Position, knob.position) < GrabRadius;
    }
}
