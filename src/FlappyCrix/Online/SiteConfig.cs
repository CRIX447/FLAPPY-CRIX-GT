// Where the online features connect: crixgamingvr.com's public client settings (the same ones
// every browser gets from /api.json - Firebase project, Photon app, PlayFab title). None of them
// are built into the mod: they are read from the live /api.json, and until that has worked the
// online features stay off (offline play is unaffected). Only these three sections are read from
// it (nothing else in that file is kept or logged).
// Every address can be pointed elsewhere for tests (tools/online-harness).
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using FlappyCrix.Web.Cdp;

namespace FlappyCrix.Online
{
    public sealed class SiteConfig
    {
        // crixgamingvr.com
        public string Site = "https://crixgamingvr.com";
        public string LinkPage => Site + "/link";

        // Firebase (accounts + saves) - from the site
        public string FirebaseApiKey = "";
        public string FirebaseProject = "";
        public string IdentityToolkit = "https://identitytoolkit.googleapis.com/v1";
        public string SecureToken = "https://securetoken.googleapis.com/v1";
        public string Firestore = "https://firestore.googleapis.com/v1";

        // PlayFab (level/XP stats, bans) - from the site
        public string PlayFabTitle = "";
        public string PlayFabBase;       // null = https://<title>.playfabapi.com

        // Photon (multiplayer): must match the website exactly or you end up in different rooms,
        // which is why it comes from the site rather than being typed in here
        public string PhotonAppId = "";
        public string PhotonAppVersion = "1.0";
        public string PhotonRegion = "us";
        public string PhotonNameServer = "wss://ns.photonengine.io:19093";

        public string FirestoreDocs => Firestore + "/projects/" + FirebaseProject + "/databases/(default)/documents";
        public string PlayFab => PlayFabBase ?? ("https://" + PlayFabTitle + ".playfabapi.com");

        public bool LoadedFromSite { get; private set; }

        public bool HasAccountSettings => !string.IsNullOrEmpty(FirebaseApiKey) && !string.IsNullOrEmpty(FirebaseProject);
        public bool HasPhotonSettings => !string.IsNullOrEmpty(PhotonAppId);
        public bool HasPlayFabSettings => !string.IsNullOrEmpty(PlayFabTitle);

        private readonly object refreshGate = new object();
        private DateTime nextTryUtc = DateTime.MinValue;
        private const int RetrySeconds = 15;

        /// <summary>
        /// True once the site's settings are in. Otherwise tries again (at most every few seconds, so a
        /// player with no internet isn't held up) - blocking; call on a background thread.
        /// </summary>
        public bool EnsureLoaded(Action<string> log)
        {
            lock (refreshGate)
            {
                if (LoadedFromSite) return true;
                if (DateTime.UtcNow < nextTryUtc) return false;
                nextTryUtc = DateTime.UtcNow.AddSeconds(RetrySeconds);
            }
            return Refresh(log);
        }

        /// <summary>Reads the live /api.json (blocking; call on a background thread). False = online stays off for now.</summary>
        public bool Refresh(Action<string> log)
        {
            var r = Http.Send("GET", Site + "/api.json", null);
            if (!r.Ok) { log?.Invoke("Couldn't read crixgamingvr.com's settings (" + r + "); online features wait until it can."); return false; }
            var root = r.Json;
            if (root == null) { log?.Invoke("crixgamingvr.com's settings weren't JSON; online features wait until they are."); return false; }
            var fb = MiniJson.Child(root, "firebase");
            var ph = MiniJson.Child(root, "photon");
            var pf = MiniJson.Child(root, "playfab");
            FirebaseApiKey = Good(MiniJson.Str(fb, "apiKey")) ?? FirebaseApiKey;
            FirebaseProject = Good(MiniJson.Str(fb, "projectId")) ?? FirebaseProject;
            PhotonAppId = Good(MiniJson.Str(ph, "appId")) ?? PhotonAppId;
            PhotonAppVersion = Good(MiniJson.Str(ph, "appVersion")) ?? PhotonAppVersion;
            PhotonRegion = Good(MiniJson.Str(ph, "region")) ?? PhotonRegion;
            PlayFabTitle = Good(MiniJson.Str(pf, "titleId")) ?? PlayFabTitle;
            lock (refreshGate) LoadedFromSite = true;
            log?.Invoke("Using crixgamingvr.com's online settings (Photon region " + PhotonRegion + ", app version " + PhotonAppVersion + ").");
            return true;
        }

        static string Good(string v) => string.IsNullOrEmpty(v) || v.StartsWith("PASTE_", StringComparison.Ordinal) ? null : v;
    }
}
