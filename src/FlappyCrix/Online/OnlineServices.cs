// Everything online the in-game version uses, created once: the site's settings, the linked
// account and multiplayer. Tests point SiteConfig at stand-ins.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using FlappyCrix.Native;

namespace FlappyCrix.Online
{
    public sealed class OnlineServices : IDisposable
    {
        public readonly SiteConfig Config;
        public readonly Account Account;
        public readonly PhotonClient Photon;
        public readonly string DataRoot;
        public Multiplayer Mp;                        // made by the game (it needs the game's rules)

        public OnlineServices(SiteConfig config, string dataRoot, Action<string> log)
        {
            Config = config;
            DataRoot = dataRoot;
            Account = new Account(config, dataRoot) { Log = log };
            Photon = new PhotonClient(config) { Log = log };
        }

        public void Start() => Account.Start();

        public void Dispose()
        {
            try { Mp?.Disconnect(); } catch { }
            try { Photon.Dispose(); } catch { }
            try { Account.Dispose(); } catch { }
        }
    }
}
