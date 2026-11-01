// Gives a Gorilla Tag fan game's local player arcade hands: an ArcadeHand on each hand, with
// controller buzz, so the cabinet's buttons, joystick, coin slot and screen react to touch.
//
// Setup: put this on your local player rig (the GorillaPlayer / "Player" object) and drag in the
// two hand transforms the player touches things with (in most fan-game templates these are the
// "LeftHandTriggerCollider" / "RightHandTriggerCollider" objects, or the hand follower
// transforms). Left empty, it looks for those names under this object.
// Only the LOCAL player should have this - never the network copies of other players.
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using CrixArcade;
using UnityEngine;

public sealed class ArcadeFanGameHands : MonoBehaviour
{
    public Transform leftHand, rightHand;
    [Tooltip("Fingertip offset from the hand transform, in the hand's own space (metres).")]
    public Vector3 tipOffset = new Vector3(0, 0, 0.04f);
    [Tooltip("How big the fingertip is (metres). Gorilla hands are big: 0.03-0.05 works well.")]
    public float radius = 0.04f;
    [Tooltip("The head / camera, for 'nearby' (empty = main camera).")]
    public Transform head;

    static readonly string[] LeftNames = { "LeftHandTriggerCollider", "LeftHand Controller", "Left Hand", "LeftHand", "hand.L" };
    static readonly string[] RightNames = { "RightHandTriggerCollider", "RightHand Controller", "Right Hand", "RightHand", "hand.R" };

    void Start()
    {
        if (leftHand == null) leftHand = FindChild(transform, LeftNames);
        if (rightHand == null) rightHand = FindChild(transform, RightNames);
        Add(leftHand, 0);
        Add(rightHand, 1);
        if (leftHand == null || rightHand == null)
            Debug.LogWarning("[CrixArcade] ArcadeFanGameHands: drag your hand transforms into Left Hand / Right Hand.");
        ArcadeLink.Instance.localPlayer = head != null ? head : (Camera.main != null ? Camera.main.transform : transform);
    }

    void Add(Transform hand, int side)
    {
        if (hand == null) return;
        var tip = new GameObject(side == 0 ? "Arcade fingertip L" : "Arcade fingertip R").transform;
        tip.SetParent(hand, false);
        tip.localPosition = tipOffset;
        var h = tip.gameObject.AddComponent<ArcadeHand>();
        h.radius = radius;
        h.hapticHand = side;
    }

    static Transform FindChild(Transform root, string[] names)
    {
        foreach (var n in names)
        {
            var t = Find(root, n);
            if (t != null) return t;
        }
        return null;
    }

    static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = Find(c, name); if (r != null) return r; }
        return null;
    }
}
