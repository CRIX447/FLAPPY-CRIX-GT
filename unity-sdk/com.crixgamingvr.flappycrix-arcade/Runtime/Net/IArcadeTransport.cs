// How the arcade talks to other players. It never opens a connection of its own: a transport
// sends small messages through the room the game is ALREADY in (Photon PUN, Photon Fusion,
// Normcore, Mirror, Netcode...), so the arcade adds no extra players to your Photon CCU.
// Samples~ has ready transports for PUN 2 and Fusion; OfflineTransport is the single-player one.
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;

namespace CrixArcade.Net
{
    public interface IArcadeTransport
    {
        /// <summary>True while in a room with other players.</summary>
        bool Connected { get; }
        /// <summary>This player's id in the room (e.g. the Photon actor number). Unique per room.</summary>
        string LocalId { get; }
        /// <summary>The name shown on other players' cabinets.</summary>
        string LocalName { get; }
        /// <summary>
        /// Send to the given players (ids from Received), or to everyone else in the room when targets is null.
        /// Unreliable is fine for positions; reliable is used for joins, countdowns and scores.
        /// </summary>
        void Send(byte[] data, IList<string> targets, bool reliable);
        /// <summary>A message arrived (sender id, data). Raise it on Unity's main thread.</summary>
        event Action<string, byte[]> Received;
        /// <summary>A player left the room (their id).</summary>
        event Action<string> PlayerLeft;
    }

    /// <summary>No network: every cabinet is single player. Used when no transport is set.</summary>
    public sealed class OfflineTransport : IArcadeTransport
    {
        public bool Connected => false;
        public string LocalId => "local";
        public string LocalName { get; set; } = "PLAYER";
        public void Send(byte[] data, IList<string> targets, bool reliable) { }
        public event Action<string, byte[]> Received { add { } remove { } }
        public event Action<string> PlayerLeft { add { } remove { } }
    }

    /// <summary>
    /// Several pretend players in one process, for testing races without a second headset:
    /// every LoopbackTransport made with the same hub hears the others.
    /// </summary>
    public sealed class LoopbackTransport : IArcadeTransport
    {
        public sealed class Hub
        {
            internal readonly List<LoopbackTransport> Members = new List<LoopbackTransport>();
            internal readonly List<KeyValuePair<LoopbackTransport, KeyValuePair<string, byte[]>>> Queue =
                new List<KeyValuePair<LoopbackTransport, KeyValuePair<string, byte[]>>>();

            /// <summary>Delivers everything sent since the last call (call it once a frame).</summary>
            public void Pump()
            {
                var q = Queue.ToArray();
                Queue.Clear();
                foreach (var m in q) m.Key.Received?.Invoke(m.Value.Key, m.Value.Value);
            }
        }

        readonly Hub hub;
        public LoopbackTransport(Hub hub, string id, string name)
        {
            this.hub = hub; LocalId = id; LocalName = name;
            hub.Members.Add(this);
        }

        public bool Connected => hub.Members.Count > 1;
        public string LocalId { get; }
        public string LocalName { get; }
        public event Action<string, byte[]> Received;
        public event Action<string> PlayerLeft;

        public void Send(byte[] data, IList<string> targets, bool reliable)
        {
            foreach (var m in hub.Members)
                if (m != this && (targets == null || targets.Contains(m.LocalId)))
                    hub.Queue.Add(new KeyValuePair<LoopbackTransport, KeyValuePair<string, byte[]>>(m, new KeyValuePair<string, byte[]>(LocalId, data)));
        }

        public void Leave()
        {
            hub.Members.Remove(this);
            foreach (var m in hub.Members) m.PlayerLeft?.Invoke(LocalId);
        }
    }
}
