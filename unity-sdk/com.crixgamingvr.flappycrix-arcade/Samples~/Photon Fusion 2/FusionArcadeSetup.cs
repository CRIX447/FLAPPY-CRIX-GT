// Flappy Crix Arcade over Photon Fusion 2 - the session your game is already in.
// Uses the runner's reliable data messages (SendReliableDataToPlayer), so there's no second
// connection and your Photon CCU doesn't change.
//
// Setup: put FusionArcadeSetup on the same object as your NetworkRunner (or drag the runner in).
// Written for Fusion 2.0: if your Fusion version names a callback differently, adjust the empty
// callbacks at the bottom - only OnReliableDataReceived and OnPlayerLeft do anything.
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using CrixArcade;
using CrixArcade.Net;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

public sealed class FusionArcadeSetup : MonoBehaviour, INetworkRunnerCallbacks, IArcadeTransport
{
    [Tooltip("Empty = the NetworkRunner on this object.")]
    public NetworkRunner runner;
    [Tooltip("The name other players see. Empty = PLAYER + number.")]
    public string playerName = "";

    static readonly ReliableKey Key = ReliableKey.FromInts(177, 0, 0, 0);

    public bool Connected => runner != null && runner.IsRunning && CountPlayers() > 1;
    public string LocalId => runner != null ? runner.LocalPlayer.PlayerId.ToString() : "0";
    public string LocalName => !string.IsNullOrEmpty(playerName) ? playerName : "PLAYER" + LocalId;
    public event Action<string, byte[]> Received;
    public event Action<string> PlayerLeft;

    void Start()
    {
        if (runner == null) runner = GetComponent<NetworkRunner>();
        if (runner == null) { Debug.LogWarning("[CrixArcade] FusionArcadeSetup needs a NetworkRunner."); return; }
        runner.AddCallbacks(this);
        ArcadeLink.Instance.SetTransport(this);
    }

    void OnDestroy() { if (runner != null) runner.RemoveCallbacks(this); }

    int CountPlayers() { int n = 0; foreach (var p in runner.ActivePlayers) n++; return n; }

    public void Send(byte[] data, IList<string> targets, bool reliable)
    {
        if (runner == null || !runner.IsRunning) return;
        foreach (var p in runner.ActivePlayers)
        {
            if (p == runner.LocalPlayer) continue;
            if (targets != null && !targets.Contains(p.PlayerId.ToString())) continue;
            runner.SendReliableDataToPlayer(p, Key, data);
        }
    }

    public void OnReliableDataReceived(NetworkRunner r, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
        if (!key.Equals(Key)) return;
        var copy = new byte[data.Count];
        Array.Copy(data.Array, data.Offset, copy, 0, data.Count);
        Received?.Invoke(player.PlayerId.ToString(), copy);
    }

    public void OnPlayerLeft(NetworkRunner r, PlayerRef player) => PlayerLeft?.Invoke(player.PlayerId.ToString());

    // not used
    public void OnPlayerJoined(NetworkRunner r, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner r, PlayerRef player, ReliableKey key, float progress) { }
    public void OnObjectExitAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
    public void OnInput(NetworkRunner r, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner r, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner r, ShutdownReason reason) { }
    public void OnConnectedToServer(NetworkRunner r) { }
    public void OnDisconnectedFromServer(NetworkRunner r, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner r, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner r, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner r, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner r, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner r, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner r, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner r) { }
    public void OnSceneLoadStart(NetworkRunner r) { }
}
