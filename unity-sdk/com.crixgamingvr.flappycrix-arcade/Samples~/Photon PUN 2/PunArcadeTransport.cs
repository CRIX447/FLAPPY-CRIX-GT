// Flappy Crix Arcade over Photon PUN 2 - the room your game is already in.
// It only sends events (RaiseEvent, event code 177) to players in that room: no second
// connection, no extra room, so your Photon CCU doesn't change. Racers send small messages
// about 8 times a second, only to the racers and the players standing near the cabinets.
//
// Setup: put PunArcadeSetup (below) on any object in your scene. That's all.
// Change EventCode if your game already uses 177.
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using CrixArcade;
using CrixArcade.Net;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public sealed class PunArcadeTransport : IArcadeTransport, IOnEventCallback, IInRoomCallbacks
{
    public const byte EventCode = 177;

    public bool Connected => PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.PlayerCount > 1;
    public string LocalId => PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber.ToString() : "0";
    public string LocalName => string.IsNullOrEmpty(PhotonNetwork.NickName) ? "PLAYER" : PhotonNetwork.NickName;

    public event Action<string, byte[]> Received;
    public event Action<string> PlayerLeft;

    readonly List<int> actors = new List<int>();

    public PunArcadeTransport() { PhotonNetwork.AddCallbackTarget(this); }
    public void Dispose() { PhotonNetwork.RemoveCallbackTarget(this); }

    public void Send(byte[] data, IList<string> targets, bool reliable)
    {
        if (!PhotonNetwork.InRoom) return;
        var options = new RaiseEventOptions();
        if (targets == null) options.Receivers = ReceiverGroup.Others;
        else
        {
            actors.Clear();
            foreach (var t in targets) { int a; if (int.TryParse(t, out a)) actors.Add(a); }
            if (actors.Count == 0) return;
            options.TargetActors = actors.ToArray();
        }
        PhotonNetwork.RaiseEvent(EventCode, data, options, reliable ? SendOptions.SendReliable : SendOptions.SendUnreliable);
    }

    public void OnEvent(EventData e)
    {
        if (e.Code != EventCode) return;
        var bytes = e.CustomData as byte[];
        if (bytes != null) Received?.Invoke(e.Sender.ToString(), bytes);
    }

    public void OnPlayerLeftRoom(Player other) => PlayerLeft?.Invoke(other.ActorNumber.ToString());
    public void OnPlayerEnteredRoom(Player newPlayer) { }
    public void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable changed) { }
    public void OnPlayerPropertiesUpdate(Player target, ExitGames.Client.Photon.Hashtable changed) { }
    public void OnMasterClientSwitched(Player newMaster) { }
}
