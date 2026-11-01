// Hands the arcade a PUN transport. Put it on any object in the scene.
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using CrixArcade;
using UnityEngine;

/// <summary>Hands the arcade a PUN transport. Put it on any object in the scene.</summary>
public sealed class PunArcadeSetup : MonoBehaviour
{
    PunArcadeTransport transport;

    void Start()
    {
        transport = new PunArcadeTransport();
        ArcadeLink.Instance.SetTransport(transport);
    }

    void OnDestroy()
    {
        if (transport != null) transport.Dispose();
    }
}
