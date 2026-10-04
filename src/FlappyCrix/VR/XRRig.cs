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
            public Vector3 Forward => Rotation * Vector3.forward;
        }

        public Hand Left, Right;
        public Transform Head { get; private set; }
        public bool AnyXR { get; private set; }

        private readonly List<InputDevice> scratch = new List<InputDevice>();

        public void Update()
        {
            var cam = Camera.main;
            Head = cam != null ? cam.transform : null;
            Transform origin = Head != null ? Head.parent : null;
            Left = Read(XRNode.LeftHand, origin);
            Right = Read(XRNode.RightHand, origin);
            AnyXR = Left.Valid || Right.Valid;
        }

        private Hand Read(XRNode node, Transform origin)
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
