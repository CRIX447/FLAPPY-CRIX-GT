// Where the online features connect: crixgamingvr.com's public client settings (the same ones
// every browser gets from /api.json - Firebase project, Photon app, PlayFab title). Built-in
// values are used until the live /api.json has been read; only these three sections are read
// from it (nothing else in that file is kept or logged).
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

        // Firebase (accounts + saves)
        public string FirebaseApiKey = "AIzaSyD7ld0RxU5n4yiMygv7qrwAxT11FMxaAzs";
        public string FirebaseProject = "flappy-crix";
        public string IdentityToolkit = "https://identitytoolkit.googleapis.com/v1";
        public string SecureToken = "https://securetoken.googleapis.com/v1";
        public string Firestore = "https://firestore.googleapis.com/v1";

        // PlayFab (level/XP stats, bans)
        public string PlayFabTitle = "17CF2A";
        public string PlayFabBase;       // null = https://<title>.playfabapi.com

        // Photon (multiplayer): must match the website exactly or you end up in different rooms
        public string PhotonAppId = "56ff5627-b073-412c-ac63-95fd3dc28484";
        public string PhotonAppVersion = "1.0";
        public string PhotonRegion = "us";
        public string PhotonNameServer = "wss://ns.photonengine.io:19093";

        public string FirestoreDocs => Firestore + "/projects/" + FirebaseProject + "/databases/(default)/documents";
        public string PlayFab => PlayFabBase ?? ("https://" + PlayFabTitle + ".playfabapi.com");

        public bool LoadedFromSite { get; private set; }

        /// <summary>Reads the live /api.json (blocking; call on a background thread). False = keep the built-in values.</summary>
        public bool Refresh(Action<string> log)
        {
            var r = Http.Send("GET", Site + "/api.json", null);
            if (!r.Ok) { log?.Invoke("Couldn't read the site's settings (" + r + "); using the built-in ones."); return false; }
            var root = r.Json;
            if (root == null) { log?.Invoke("The site's settings weren't JSON; using the built-in ones."); return false; }
            var fb = MiniJson.Child(root, "firebase");
            var ph = MiniJson.Child(root, "photon");
            var pf = MiniJson.Child(root, "playfab");
            FirebaseApiKey = Good(MiniJson.Str(fb, "apiKey")) ?? FirebaseApiKey;
            FirebaseProject = Good(MiniJson.Str(fb, "projectId")) ?? FirebaseProject;
            PhotonAppId = Good(MiniJson.Str(ph, "appId")) ?? PhotonAppId;
            PhotonAppVersion = Good(MiniJson.Str(ph, "appVersion")) ?? PhotonAppVersion;
            PhotonRegion = Good(MiniJson.Str(ph, "region")) ?? PhotonRegion;
            PlayFabTitle = Good(MiniJson.Str(pf, "titleId")) ?? PlayFabTitle;
            LoadedFromSite = true;
            log?.Invoke("Using crixgamingvr.com's online settings (Photon region " + PhotonRegion + ", app version " + PhotonAppVersion + ").");
            return true;
        }

        static string Good(string v) => string.IsNullOrEmpty(v) || v.StartsWith("PASTE_", StringComparison.Ordinal) ? null : v;
    }
}
