using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace FlappyCrix.VR
{
    /// <summary>
    /// Controller buttons and poses straight from Unity's XR input, so the mod does not
    /// depend on Gorilla Tag's internal classes (which change between updates).
    /// Poses come back in tracking space and are converted to world space through the
    /// main camera's parent (the VR rig's tracking origin).
    /// </summary>
    public sealed class XRRig
    {
        public struct Hand
        {
            public bool Valid;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool Primary, Secondary, TriggerPressed, GripPressed;
            public float Trigger, Grip;
            public Vector2 Stick;
            public InputDevice Device;
            /// <summary>Index fingertip in world space - what presses the deck's buttons.</summary>
            public Vector3 Tip;
            /// <summary>True when Tip is Gorilla Tag's own fingertip point (not worked out from the controller).</summary>
            public bool TipFromGame;
            public Vector3 Forward => Rotation * Vector3.forward;
        }

        public Hand Left, Right;
        public Transform Head { get; private set; }
        public bool AnyXR { get; private set; }

        private readonly List<InputDevice> scratch = new List<InputDevice>();

        /// <summary>Gorilla Tag's fingertip points, when found (null = don't use them).</summary>
        public GameFingertips GameTips;
        /// <summary>Fingertip relative to the controller pose (right, up, forward), when the game's points aren't used.</summary>
        public Vector3 TipOffset = new Vector3(0f, -0.02f, 0.085f);

        public void Update()
        {
            var cam = Camera.main;
            Head = cam != null ? cam.transform : null;
            Transform origin = Head != null ? Head.parent : null;
            if (GameTips != null) GameTips.Refresh();
            Left = Read(XRNode.LeftHand, origin, GameTips != null ? GameTips.Left : null);
            Right = Read(XRNode.RightHand, origin, GameTips != null ? GameTips.Right : null);
            AnyXR = Left.Valid || Right.Valid;
        }

        private Hand Read(XRNode node, Transform origin, Transform gameTip)
        {
            var h = new Hand();
            scratch.Clear();
            InputDevices.GetDevicesAtXRNode(node, scratch);
            if (scratch.Count == 0) return h;
            var d = scratch[0];
            if (!d.isValid) return h;
            h.Device = d;

            Vector3 p; Quaternion r;
            if (d.TryGetFeatureValue(CommonUsages.devicePosition, out p) && d.TryGetFeatureValue(CommonUsages.deviceRotation, out r))
            {
                h.Valid = true;
                h.Position = origin != null ? origin.TransformPoint(p) : p;
                h.Rotation = origin != null ? origin.rotation * r : r;
                h.Tip = h.Position + h.Rotation * TipOffset;
                // The game's own fingertip point, if it's really on this hand (not left somewhere else)
                if (gameTip != null)
                {
                    Vector3 t = gameTip.position;
                    if (Vector3.Distance(t, h.Position) < 0.45f) { h.Tip = t; h.TipFromGame = true; }
                }
            }
            d.TryGetFeatureValue(CommonUsages.primaryButton, out h.Primary);
            d.TryGetFeatureValue(CommonUsages.secondaryButton, out h.Secondary);
            d.TryGetFeatureValue(CommonUsages.trigger, out h.Trigger);
            d.TryGetFeatureValue(CommonUsages.grip, out h.Grip);
            d.TryGetFeatureValue(CommonUsages.primary2DAxis, out h.Stick);
            h.TriggerPressed = h.Trigger > 0.7f;
            h.GripPressed = h.Grip > 0.7f;
            return h;
        }

        public static void Buzz(Hand h, float amplitude, float seconds)
        {
            try { if (h.Device.isValid) h.Device.SendHapticImpulse(0, amplitude, seconds); } catch { }
        }
    }
}
