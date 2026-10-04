using System;
using System.Reflection;
using UnityEngine;

namespace FlappyCrix.VR
{
    /// <summary>
    /// Gorilla Tag's own fingertip points: the small trigger objects at the tip of each index
    /// finger that the game's buttons react to (GorillaTagger.Instance.leftHandTriggerCollider /
    /// rightHandTriggerCollider). Found by reflection, so the mod has no compile-time link to
    /// the game's code: if an update renames them, the deck simply uses the fingertip worked out
    /// from the controller pose instead (XRRig.TipOffset).
    /// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
    /// </summary>
    public sealed class GameFingertips
    {
        public Transform Left { get; private set; }
        public Transform Right { get; private set; }
        public Action<string> Log;

        private Type taggerType;
        private float nextLookup, firstLookup = -1f;
        private bool reportedFound, reportedMissing, typeSearched;

        /// <summary>Looks the points up (at most every 3 s) until both are found.</summary>
        public void Refresh()
        {
            float now = Time.realtimeSinceStartup;
            if (firstLookup < 0) firstLookup = now;
            if (Left != null && Right != null) return;
            if (now < nextLookup) return;
            nextLookup = now + 3f;
            try
            {
                if (!typeSearched) { taggerType = FindType("GorillaTagger"); typeSearched = taggerType != null || now - firstLookup > 60f; }
                object tagger = taggerType != null ? Static(taggerType, "Instance") ?? Static(taggerType, "_instance") : null;
                if (tagger != null)
                {
                    Left = AsTransform(Member(tagger, "leftHandTriggerCollider"));
                    Right = AsTransform(Member(tagger, "rightHandTriggerCollider"));
                }
            }
            catch (Exception e) { if (!reportedMissing) Log?.Invoke("Fingertip lookup failed: " + e.GetType().Name + ": " + e.Message); }

            if (Left != null && Right != null)
            {
                if (!reportedFound) { reportedFound = true; Log?.Invoke("Deck buttons: pressed with Gorilla Tag's own fingertip points"); }
            }
            else if (!reportedMissing && now - firstLookup > 20f)
            {
                reportedMissing = true;
                Log?.Invoke("Deck buttons: Gorilla Tag's fingertip points not found; using the fingertip from the controller (FingertipOffset)");
            }
        }

        private static Type FindType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(name, false); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic;

        private static object Static(Type t, string name)
        {
            var p = t.GetProperty(name, Any | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(null, null);
            var f = t.GetField(name, Any | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            return f != null ? f.GetValue(null) : null;
        }

        private static object Member(object o, string name)
        {
            var t = o.GetType();
            var f = t.GetField(name, Any | BindingFlags.Instance);
            if (f != null) return f.GetValue(o);
            var p = t.GetProperty(name, Any | BindingFlags.Instance);
            return p != null && p.GetIndexParameters().Length == 0 ? p.GetValue(o, null) : null;
        }

        private static Transform AsTransform(object o)
        {
            var go = o as GameObject;
            if (go != null) return go.transform;
            var c = o as Component;
            if (c != null) return c.transform;
            return null;
        }
    }
}
