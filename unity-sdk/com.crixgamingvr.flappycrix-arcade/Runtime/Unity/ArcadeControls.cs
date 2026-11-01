// The cabinet's physical controls for VR hands: big buttons, a joystick, a coin slot, and the
// screen itself (tap the menus). No colliders, layers or physics needed: put an ArcadeHand on
// each fingertip (or hand) of your player rig and the controls check the distance every frame,
// so it works the same in any Gorilla Tag fan game, any rig, any physics setup.
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using System.Collections.Generic;
using UnityEngine;

namespace CrixArcade
{
    /// <summary>Put this on a fingertip or hand of the LOCAL player (not on other players' copies).</summary>
    [AddComponentMenu("CRIX/Arcade Hand")]
    public sealed class ArcadeHand : MonoBehaviour
    {
        public static readonly List<ArcadeHand> All = new List<ArcadeHand>();
        [Tooltip("How big the fingertip is (metres).")]
        public float radius = 0.025f;
        [Tooltip("Optional: buzz this controller when a button is pressed (0 = left, 1 = right, -1 = none).")]
        public int hapticHand = -1;

        public Vector3 Tip => transform.position;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void Buzz(float strength = 0.4f, float seconds = 0.04f)
        {
            if (hapticHand < 0) return;
            var d = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(hapticHand == 0 ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand);
            if (d.isValid) d.SendHapticImpulse(0, strength, seconds);
        }
    }

    public enum ArcadeAction { Flap, Start, Back, Select, Coin, Left, Right, Up, Down }

    /// <summary>A push button. It sinks when a hand pushes it from the front (its local +Y is "up").</summary>
    [AddComponentMenu("CRIX/Arcade Button")]
    public sealed class ArcadeButton : MonoBehaviour
    {
        public ArcadeMachine machine;
        public ArcadeAction action = ArcadeAction.Flap;
        [Tooltip("The part that moves (the cap). Empty = this object.")]
        public Transform cap;
        public float radius = 0.045f;
        [Tooltip("How far the cap sinks when pressed (metres).")]
        public float travel = 0.012f;
        [Tooltip("Repeat while held (seconds). 0 = once per press.")]
        public float repeat = 0;

        Vector3 capRest;
        Transform restOf;
        bool down;
        float nextRepeat;

        void Update()
        {
            if (cap == null) cap = transform;
            if (restOf != cap) { restOf = cap; capRest = cap.localPosition; }     // set up on first use (fields are filled after AddComponent)
            if (machine == null) return;
            bool touching = false; ArcadeHand who = null;
            Vector3 up = transform.up;
            foreach (var h in ArcadeHand.All)
            {
                Vector3 d = h.Tip - transform.position;
                float height = Vector3.Dot(d, up);
                float side = (d - up * height).magnitude;
                if (side < radius + h.radius && height < h.radius + 0.02f && height > -0.05f) { touching = true; who = h; break; }
            }
            if (touching && !down)
            {
                down = true; nextRepeat = Time.unscaledTime + 0.4f;
                machine.Press(action);
                if (who != null) who.Buzz();
            }
            else if (touching && repeat > 0 && Time.unscaledTime >= nextRepeat)
            {
                nextRepeat = Time.unscaledTime + repeat;
                machine.Press(action);
            }
            else if (!touching) down = false;
            cap.localPosition = capRest - (down ? Vector3.up * travel : Vector3.zero);
        }

        /// <summary>For a mouse or a laser pointer.</summary>
        public void PressFromCode() { if (machine != null) machine.Press(action); }
    }

    /// <summary>
    /// A joystick: push the ball with your hand and the stick tilts; past half way it moves the
    /// highlight in the menus (and repeats while held). Its local +Y is "up".
    /// </summary>
    [AddComponentMenu("CRIX/Arcade Joystick")]
    public sealed class ArcadeJoystick : MonoBehaviour
    {
        public ArcadeMachine machine;
        [Tooltip("The stick (it tilts). Its pivot is the bottom of the stick.")]
        public Transform stick;
        [Tooltip("The ball on top (where hands push).")]
        public Transform ball;
        public float grabRadius = 0.06f;
        [Tooltip("How far (metres) the ball moves at full tilt.")]
        public float reach = 0.05f;
        public float maxTilt = 25f;
        public float repeatDelay = 0.4f, repeatEvery = 0.16f;

        Vector3 restBall;
        Vector2 tilt;
        int lastX, lastY;
        float nextX, nextY;
        ArcadeHand holder;

        Transform restOf;

        void Update()
        {
            if (stick == null) stick = transform;
            if (restOf != stick)
            {
                restOf = stick;
                restBall = ball != null ? stick.InverseTransformPoint(ball.position) : Vector3.up * 0.1f;
            }
            if (machine == null) return;
            Vector3 restWorld = stick.parent != null ? stick.parent.TransformPoint(stick.localPosition + stick.localRotation * restBall) : stick.TransformPoint(restBall);
            if (holder != null && (!holder.isActiveAndEnabled || (holder.Tip - restWorld).magnitude > grabRadius + reach * 1.5f)) holder = null;
            if (holder == null)
                foreach (var h in ArcadeHand.All)
                    if ((h.Tip - restWorld).magnitude < grabRadius + h.radius) { holder = h; break; }

            Vector2 want = Vector2.zero;
            if (holder != null)
            {
                Vector3 local = transform.InverseTransformDirection(holder.Tip - restWorld);
                want = new Vector2(local.x, local.z) / reach;
                if (want.magnitude > 1) want.Normalize();
            }
            tilt = Vector2.Lerp(tilt, want, Mathf.Clamp01(Time.unscaledDeltaTime * 20f));
            stick.localRotation = Quaternion.Euler(tilt.y * maxTilt, 0, -tilt.x * maxTilt);

            int x = tilt.x > 0.5f ? 1 : tilt.x < -0.5f ? -1 : 0;
            int y = tilt.y > 0.5f ? 1 : tilt.y < -0.5f ? -1 : 0;     // pushing away from you = up
            Step(ref lastX, ref nextX, x, true);
            Step(ref lastY, ref nextY, y, false);
        }

        void Step(ref int last, ref float next, int now, bool horizontal)
        {
            float t = Time.unscaledTime;
            if (now != 0 && (now != last || t >= next))
            {
                next = t + (now != last ? repeatDelay : repeatEvery);
                if (horizontal) machine.Press(now > 0 ? ArcadeAction.Right : ArcadeAction.Left);
                else machine.Press(now > 0 ? ArcadeAction.Up : ArcadeAction.Down);
                if (holder != null) holder.Buzz(0.2f, 0.02f);
            }
            last = now;
        }
    }

    /// <summary>Touch the slot to put a coin in (when the cabinet isn't on free play).</summary>
    [AddComponentMenu("CRIX/Arcade Coin Slot")]
    public sealed class ArcadeCoinSlot : MonoBehaviour
    {
        public ArcadeMachine machine;
        public float radius = 0.05f;
        public float cooldown = 0.8f;
        float readyAt;
        bool inside;

        void Update()
        {
            if (machine == null) return;
            bool touching = false; ArcadeHand who = null;
            foreach (var h in ArcadeHand.All)
                if ((h.Tip - transform.position).magnitude < radius + h.radius) { touching = true; who = h; break; }
            if (touching && !inside && Time.unscaledTime >= readyAt)
            {
                readyAt = Time.unscaledTime + cooldown;
                machine.Press(ArcadeAction.Coin);
                if (who != null) who.Buzz(0.6f, 0.06f);
            }
            inside = touching;
        }
    }
}
