// The scene's connection between arcade cabinets and the network. Put one in the scene (the
// cabinet adds one by itself if there isn't one) and hand it a transport once your game has
// joined its room:
//
//     ArcadeLink.Instance.SetTransport(new PunArcadeTransport());    // Samples~/Photon PUN 2
//
// Without a transport every cabinet is a single-player machine. With one, cabinets that share a
// link group race each other, show who is using them and share high scores, through the room
// your game is already in (no extra CCU).
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using CrixArcade.Net;
using UnityEngine;

namespace CrixArcade
{
    [AddComponentMenu("CRIX/Flappy Crix Arcade Link")]
    [DisallowMultipleComponent]
    public sealed class ArcadeLink : MonoBehaviour
    {
        static ArcadeLink instance;

        [Tooltip("Where the local player is (their head or camera). Empty = the main camera.")]
        public Transform localPlayer;
        [Tooltip("Players further than this (metres) from a cabinet don't count as nearby: they can't join its races or watch them.")]
        public float nearbyRadius = 12f;
        [Tooltip("Positions sent per second while racing (ghost birds). Lower = fewer messages.")]
        [Range(2, 15)] public int racePositionsPerSecond = 8;
        [Tooltip("Write races and network notes to the console.")]
        public bool logToConsole = true;

        public ArcadeNetwork Network { get; private set; }

        public static bool Exists => instance != null;

        /// <summary>The scene's link (made on first use).</summary>
        public static ArcadeLink Instance
        {
            get
            {
                if (instance != null) return instance;
                instance = FindObjectOfType<ArcadeLink>();
                if (instance == null) instance = new GameObject("Flappy Crix Arcade Link").AddComponent<ArcadeLink>();
                instance.Init();
                return instance;
            }
        }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
            Init();
        }

        void Init()
        {
            if (Network != null) return;
            SetTransport(null);
        }

        /// <summary>Use this transport from now on (null = offline / single player).</summary>
        public void SetTransport(IArcadeTransport transport)
        {
            var old = Network;
            Network = new ArcadeNetwork(transport ?? new OfflineTransport())
            {
                LocalPosition = () =>
                {
                    var t = localPlayer != null ? localPlayer : (Camera.main != null ? Camera.main.transform : null);
                    var p = t != null ? t.position : Vector3.zero;
                    return new ArcadeVec(p.x, p.y, p.z);
                },
            };
            Network.Log += m => { if (logToConsole) Debug.Log("[CrixArcade] " + m); };
            if (old != null) foreach (var m in ArcadeMachine.All) { old.Remove(m.Seat); if (m.Seat != null) Network.Add(m.Seat); }
        }

        void Update()
        {
            if (Network == null) return;
            Network.NearbyRadius = nearbyRadius;
            Network.BirdEvery = 1.0 / Mathf.Max(2, racePositionsPerSecond);
            Network.Update(Time.unscaledTimeAsDouble);
        }

        void OnDestroy() { if (instance == this) instance = null; }
    }
}
