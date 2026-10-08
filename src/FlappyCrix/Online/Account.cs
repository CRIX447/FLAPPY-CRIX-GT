// A crixgamingvr.com account in the game, linked the way the site links consoles
// (api/device-link.js): the game asks for a code, the player opens crixgamingvr.com/link on a
// phone or PC, signs in there (Google, or email and password) and enters the code (or scans the
// QR code), and the game receives a sign-in token for that same account. Then:
//   Firebase Auth REST (custom token -> ID token, refreshed hourly; stays signed in next time),
//   Firestore users/{uid} (the website's save: coins, best, games, cosmetics, awards),
//   PlayFab (level / XP statistics and bans, signed in with the account id like the site).
// Everything runs on one background thread; the game reads Status and takes results.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using FlappyCrix.Web.Cdp;

namespace FlappyCrix.Online
{
    public sealed class Account : IDisposable
    {
        public enum State { SignedOut, Restoring, GettingCode, ShowingCode, Linking, SignedIn, Error }

        /// <summary>What the cloud had when the account was signed in (handed to the game once).</summary>
        public sealed class Loaded
        {
            public string Uid, Name;
            public Dictionary<string, object> Doc;        // users/{uid} as plain values, or null (no doc)
            public int PfLevel, PfXp, PfBest;             // PlayFab statistics (0 = none)
        }

        public readonly SiteConfig Config;
        public Action<string> Log = _ => { };

        // ---- read by the game (any thread)
        public State Status { get; private set; } = State.SignedOut;
        public string Code { get; private set; }
        public string LinkUrl { get; private set; }
        public DateTime CodeExpiresUtc { get; private set; }
        public string Message { get; private set; }
        public string Name { get; private set; }
        public string Uid { get; private set; }
        public bool Banned { get; private set; }
        public string BanReason { get; private set; }
        public int Version { get; private set; }      // goes up whenever something above changes

        private readonly string file;                 // %LOCALAPPDATA%\FlappyCrix\account.txt
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Thread thread;
        private volatile bool running = true;

        // requests from the game
        private bool wantLink, wantCancel, wantSignOut;
        private Dictionary<string, object> pendingSave;   // newest save to upload
        private bool flushNow;
        private Loaded loaded;

        // session
        private string idToken, refreshToken, playFabTicket, playFabId, linkToken;
        private DateTime idExpiresUtc, nextPollUtc, lastUploadUtc = DateTime.MinValue;

        public static TimeSpan PollEvery = TimeSpan.FromSeconds(3);
        public static TimeSpan UploadEvery = TimeSpan.FromSeconds(3);

        public Account(SiteConfig config, string dataRoot)
        {
            Config = config;
            file = dataRoot != null ? Path.Combine(dataRoot, "account.txt") : null;
            thread = new Thread(Loop) { IsBackground = true, Name = "FlappyCrix-Account" };
        }

        public void Start() => thread.Start();

        public void LinkDevice() { lock (gate) { wantLink = true; wantCancel = false; } wake.Set(); }
        public void Cancel() { lock (gate) wantCancel = true; wake.Set(); }
        public void SignOut() { lock (gate) wantSignOut = true; wake.Set(); }

        /// <summary>Upload this save (Firestore field values, see CloudSave). now = right away (death, quit).</summary>
        public void QueueSave(Dictionary<string, object> fields, bool now)
        {
            lock (gate) { pendingSave = fields; if (now) flushNow = true; }
            wake.Set();
        }

        public Loaded TakeLoaded() { lock (gate) { var l = loaded; loaded = null; return l; } }

        private void Set(State s, string message = null)
        {
            lock (gate) { Status = s; Message = message; Version++; }
            if (message != null) Log("Account: " + message);
        }

        // ------------------------------------------------------------------ the worker

        private void Loop()
        {
            try { Config.EnsureLoaded(Log); } catch (Exception e) { Log("Site settings: " + e.Message); }
            Restore();
            while (running)
            {
                wake.WaitOne(250);
                if (!running) break;
                try { Step(); }
                catch (Exception e) { Log("Account error: " + e); Set(State.Error, "Something went wrong (" + e.GetType().Name + "). Try again."); }
            }
            try { UploadPending(true); } catch { }
        }

        private void Step()
        {
            bool link, cancel, signOut;
            lock (gate) { link = wantLink; cancel = wantCancel; signOut = wantSignOut; wantLink = wantCancel = wantSignOut = false; }

            if (signOut) { DoSignOut(); return; }

            // Nothing below can work without crixgamingvr.com's settings (none are built in).
            // Keep asking for them; a link request that can't go anywhere says why.
            if (!Config.HasAccountSettings)
            {
                bool loaded = false;
                try { loaded = Config.EnsureLoaded(Log); } catch (Exception e) { Log("Site settings: " + e.Message); }
                if (!loaded || !Config.HasAccountSettings)
                {
                    if (link) Set(State.Error, "Can't reach crixgamingvr.com right now. Check your internet and try again.");
                    return;
                }
            }
            if (cancel && (Status == State.ShowingCode || Status == State.GettingCode || Status == State.Error))
            { linkToken = null; Code = null; Set(Uid != null ? State.SignedIn : State.SignedOut); }
            if (link && Status != State.SignedIn) CreateCode();

            if (Status == State.ShowingCode && DateTime.UtcNow >= nextPollUtc) Poll();
            if (Status == State.SignedIn)
            {
                if (DateTime.UtcNow > idExpiresUtc - TimeSpan.FromMinutes(5)) RefreshId();
                UploadPending(false);
            }
        }

        // ---- 1. linking (the site's "console" flow)

        private void CreateCode()
        {
            Set(State.GettingCode, null);
            var r = Http.PostJson(Config.Site + "/api/device-link", MiniJson.Args("action", "create"));
            var j = r.Json;
            string code = MiniJson.Str(j, "code"), token = MiniJson.Str(j, "token");
            if (!r.Ok || code == null || token == null)
            {
                Set(State.Error, r.Status == 404 ? "Linking isn't available on crixgamingvr.com right now." : "Couldn't get a code from crixgamingvr.com (" + r + ").");
                return;
            }
            double secs = MiniJson.Num(j, "expiresIn", 600);
            lock (gate)
            {
                Code = code; linkToken = token;
                LinkUrl = Config.LinkPage + "?code=" + Uri.EscapeDataString(code);
                CodeExpiresUtc = DateTime.UtcNow.AddSeconds(secs);
            }
            nextPollUtc = DateTime.UtcNow + PollEvery;
            Set(State.ShowingCode, null);
            Log("Account: link code ready (open " + Config.LinkPage + " and enter it)");
        }

        private void Poll()
        {
            nextPollUtc = DateTime.UtcNow + PollEvery;
            if (DateTime.UtcNow > CodeExpiresUtc) { Set(State.Error, "The code expired. Get a new one."); return; }
            var r = Http.PostJson(Config.Site + "/api/device-link", MiniJson.Args("action", "poll", "code", Code, "token", linkToken));
            var j = r.Json;
            if (r.Status == 0) return;                                          // no answer: keep trying
            if (r.Status == 404 || r.Status == 410) { Set(State.Error, "The code expired. Get a new one."); return; }
            if (r.Status == 403) { Set(State.Error, "That code belongs to another device. Get a new one."); return; }
            if (!r.Ok) { Log("Account: poll " + r); return; }
            string custom = MiniJson.Str(j, "customToken");
            if (custom == null) return;                                         // {"pending":true}
            Set(State.Linking, null);
            linkToken = null;
            var s = Http.PostJson(Config.IdentityToolkit + "/accounts:signInWithCustomToken?key=" + Http.Escape(Config.FirebaseApiKey),
                                  MiniJson.Args("token", custom, "returnSecureToken", true));
            if (!s.Ok) { Set(State.Error, "Sign-in was refused (" + FirebaseError(s) + ")."); return; }
            var sj = s.Json;
            TakeTokens(MiniJson.Str(sj, "idToken"), MiniJson.Str(sj, "refreshToken"), MiniJson.Num(sj, "expiresIn", 3600));
            Uid = JwtUid(idToken);
            Persist();
            FinishSignIn();
        }

        // ---- 2. tokens

        private void Restore()
        {
            if (file == null || !File.Exists(file)) return;
            string uid = null, rt = null;
            try
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    if (line.StartsWith("uid=", StringComparison.Ordinal)) uid = line.Substring(4).Trim();
                    if (line.StartsWith("refresh=", StringComparison.Ordinal)) rt = line.Substring(8).Trim();
                }
            }
            catch { }
            if (string.IsNullOrEmpty(rt)) return;
            Set(State.Restoring, null);
            refreshToken = rt;
            Uid = uid;
            if (!RefreshId())
            {
                if (Status == State.SignedOut) return;           // the account was signed out or removed
                Set(State.SignedOut, "Couldn't reach your account (offline?). Playing as a guest for now.");
                return;
            }
            FinishSignIn();
        }

        private bool RefreshId()
        {
            var r = Http.Send("POST", Config.SecureToken + "/token?key=" + Http.Escape(Config.FirebaseApiKey),
                              "grant_type=refresh_token&refresh_token=" + Http.Escape(refreshToken), "application/x-www-form-urlencoded");
            if (!r.Ok)
            {
                string err = FirebaseError(r);
                if (r.Status >= 400 && r.Status < 500 && (err.Contains("TOKEN_EXPIRED") || err.Contains("USER_DISABLED") || err.Contains("USER_NOT_FOUND") || err.Contains("INVALID_REFRESH_TOKEN")))
                { Forget(); Set(State.SignedOut, "You were signed out (" + err + "). Link the game again."); }
                else Log("Account: couldn't refresh the sign-in (" + err + ")");
                return false;
            }
            var j = r.Json;
            TakeTokens(MiniJson.Str(j, "id_token"), MiniJson.Str(j, "refresh_token"), Num(MiniJson.Str(j, "expires_in"), 3600));
            Uid = MiniJson.Str(j, "user_id") ?? JwtUid(idToken);
            Persist();
            return true;
        }

        private void TakeTokens(string id, string refresh, double expiresIn)
        {
            idToken = id;
            if (!string.IsNullOrEmpty(refresh)) refreshToken = refresh;
            idExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn);
        }

        private void Persist()
        {
            if (file == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, "# Flappy Crix for Gorilla Tag - the crixgamingvr.com account this PC is linked to. Delete this file to sign out.\n" +
                                        "uid=" + Uid + "\nrefresh=" + refreshToken + "\n");
            }
            catch (Exception e) { Log("Account: couldn't remember the sign-in (" + e.Message + ")"); }
        }

        private void Forget()
        {
            idToken = refreshToken = playFabTicket = playFabId = null;
            try { if (file != null && File.Exists(file)) File.Delete(file); } catch { }
        }

        // ---- 3. after sign-in: name, save, PlayFab

        private void FinishSignIn()
        {
            // name (the site's accountName: display name, else the part of the email before @, else "Player")
            var look = Http.PostJson(Config.IdentityToolkit + "/accounts:lookup?key=" + Http.Escape(Config.FirebaseApiKey), MiniJson.Args("idToken", idToken));
            string name = null;
            if (look.Ok)
            {
                var users = look.Json != null && look.Json.ContainsKey("users") ? look.Json["users"] as List<object> : null;
                var u = users != null && users.Count > 0 ? MiniJson.Obj(users[0]) : null;
                name = MiniJson.Str(u, "displayName");
                string email = MiniJson.Str(u, "email");
                if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(email)) name = email.Split('@')[0];
            }
            if (string.IsNullOrEmpty(name)) name = "Player";

            // the save
            var doc = Firestore_Get("users/" + Uid);
            if (doc == null && lastGetFailed) { Set(State.Error, "Couldn't load your progress from crixgamingvr.com. Try again."); return; }

            // PlayFab: the same account id the site signs in with; bans and level stats live there
            int pfLevel = 0, pfXp = 0, pfBest = 0;
            PlayFabLogin(name, ref pfLevel, ref pfXp, ref pfBest);
            if (!Banned && playFabId != null) CheckFirestoreBan();

            lock (gate)
            {
                Name = name;
                loaded = new Loaded { Uid = Uid, Name = name, Doc = doc, PfLevel = pfLevel, PfXp = pfXp, PfBest = pfBest };
            }
            lastUploadUtc = DateTime.UtcNow;
            Set(State.SignedIn, null);
            Log("Account: signed in as " + name + (Banned ? " (banned: " + BanReason + ")" : ""));
        }

        private bool lastGetFailed;

        /// <summary>GET a document as plain values; null if it doesn't exist (lastGetFailed tells errors apart).</summary>
        private Dictionary<string, object> Firestore_Get(string path)
        {
            lastGetFailed = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var r = Http.Send("GET", Config.FirestoreDocs + "/" + path, null, null, Auth());
                if (r.Status == 404) return null;
                if (r.Status == 401 && attempt == 0 && RefreshId()) continue;
                if (!r.Ok) { Log("Account: couldn't read " + path + " (" + r + ")"); lastGetFailed = true; return null; }
                return Firestore.PlainFields(MiniJson.Child(r.Json, "fields"));
            }
            lastGetFailed = true;
            return null;
        }

        private Dictionary<string, string> Auth() => new Dictionary<string, string> { { "Authorization", "Bearer " + idToken } };

        private void UploadPending(bool force)
        {
            Dictionary<string, object> fields;
            bool now;
            lock (gate) { fields = pendingSave; now = flushNow; }
            if (fields == null || idToken == null || Uid == null) return;
            if (!force && !now && DateTime.UtcNow - lastUploadUtc < UploadEvery) return;
            lock (gate) { if (pendingSave == fields) pendingSave = null; flushNow = false; }
            lastUploadUtc = DateTime.UtcNow;

            // update mask = exactly the fields sent (leaf paths for maps like stats, so the site's other keys survive)
            var mask = new StringBuilder();
            var body = new Dictionary<string, object>();
            foreach (var kv in fields)
            {
                var inner = kv.Value as Dictionary<string, object>;
                if (kv.Key == "stats" && inner != null)
                    foreach (var k in inner.Keys) mask.Append(mask.Length == 0 ? "?" : "&").Append("updateMask.fieldPaths=stats.").Append(k);
                else mask.Append(mask.Length == 0 ? "?" : "&").Append("updateMask.fieldPaths=").Append(Http.Escape(kv.Key));
                body[kv.Key] = kv.Value;
            }
            string json = MiniJson.Serialize(MiniJson.Args("fields", Firestore.Fields(body)));
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var r = Http.Send("PATCH", Config.FirestoreDocs + "/users/" + Uid + mask, json, "application/json", Auth());
                if (r.Status == 401 && attempt == 0 && RefreshId()) continue;
                if (!r.Ok) { Log("Account: couldn't save to crixgamingvr.com (" + r + "); will try again"); lock (gate) { if (pendingSave == null) pendingSave = fields; } }
                break;
            }
            PlayFabStats(fields);
        }

        private void PlayFabLogin(string name, ref int level, ref int xp, ref int best)
        {
            if (!Config.HasPlayFabSettings) { Log("Account: no PlayFab title from crixgamingvr.com; skipping PlayFab"); return; }
            // The website signs in to PlayFab for us (api/playfab-login.js), with an id only its
            // server knows — so knowing someone's account id is no longer enough to get into their
            // PlayFab account. It needs the Firebase sign-in, which only this player has.
            var data = (Dictionary<string, object>)null;
            var r = Http.PostJson(Config.Site + "/api/playfab-login", MiniJson.Args("idToken", idToken));
            if (r.Ok) data = r.Json;
            else if (r.Status == 403 && MiniJson.Str(r.Json, "error") == "AccountBanned")
            {
                var ban = MiniJson.Child(r.Json, "ban");
                Banned = true; BanReason = MiniJson.Str(ban, "reason") ?? "banned";
                Log("Account: this account is banned on PlayFab");
                return;
            }
            else if (r.Status == 503 || r.Status == 404)
            {
                // The website hasn't switched it on yet: the old way, which is all there is until then
                r = Http.PostJson(Config.PlayFab + "/Client/LoginWithCustomID",
                    MiniJson.Args("TitleId", Config.PlayFabTitle, "CustomId", Uid, "CreateAccount", true));
                var j = r.Json;
                if (!r.Ok)
                {
                    if (MiniJson.Str(j, "error") == "AccountBanned")
                    {
                        Banned = true; BanReason = MiniJson.Str(j, "errorMessage") ?? "banned";
                        Log("Account: this account is banned on PlayFab");
                    }
                    else Log("Account: PlayFab sign-in skipped (" + r + ")");
                    return;
                }
                data = MiniJson.Child(j, "data");
            }
            else
            {
                // Never fall back here: once an account has moved, the old way would make a new, empty one
                Log("Account: PlayFab sign-in skipped (" + r + ")");
                return;
            }
            playFabTicket = MiniJson.Str(data, "SessionTicket");
            playFabId = MiniJson.Str(data, "PlayFabId");
            var st = PlayFab("GetPlayerStatistics", MiniJson.Args("StatisticNames", new List<object> { "level", "xp", "highScore" }));
            var list = st != null && st.ContainsKey("Statistics") ? st["Statistics"] as List<object> : null;
            if (list != null)
                foreach (var o in list)
                {
                    var s = MiniJson.Obj(o);
                    int v = (int)MiniJson.Num(s, "Value");
                    switch (MiniJson.Str(s, "StatisticName")) { case "level": level = v; break; case "xp": xp = v; break; case "highScore": best = v; break; }
                }
        }

        /// <summary>
        /// A signed pass for one room (the website's api/identity.js). The website's games only believe
        /// a player is signed in — and so let them chat, and show their tags — when they have one.
        /// Blocking: call it off the game thread.
        /// </summary>
        public string RoomPass(string room, int actor)
        {
            string tok = idToken, pf = playFabTicket;
            if (tok == null || string.IsNullOrEmpty(room) || actor < 1) return null;
            var r = Http.PostJson(Config.Site + "/api/identity", MiniJson.Args("idToken", tok, "pfTicket", pf, "room", room, "actor", actor));
            if (!r.Ok) { Log("Account: no room pass (" + r + ")"); return null; }
            return MiniJson.Str(r.Json, "pass");
        }

        private Dictionary<string, object> PlayFab(string op, object body)
        {
            if (playFabTicket == null) return null;
            var r = Http.PostJson(Config.PlayFab + "/Client/" + op, body, new Dictionary<string, string> { { "X-Authorization", playFabTicket } });
            if (!r.Ok) { Log("Account: PlayFab " + op + " " + r); return null; }
            return MiniJson.Child(r.Json, "data");
        }

        private void PlayFabStats(Dictionary<string, object> fields)
        {
            var stats = fields.ContainsKey("stats") ? fields["stats"] as Dictionary<string, object> : null;
            if (stats == null || playFabTicket == null) return;
            var list = new List<object>();
            foreach (var k in new[] { "level", "xp", "highScore", "coins" })
                if (stats.ContainsKey(k)) list.Add(MiniJson.Args("StatisticName", k, "Value", stats[k]));
            PlayFab("UpdatePlayerStatistics", MiniJson.Args("Statistics", list));
        }

        private void CheckFirestoreBan()
        {
            var ban = Firestore_Get("bans/" + playFabId);
            if (ban == null) return;
            object active; ban.TryGetValue("active", out active);
            long exp = Firestore.Long(ban, "expiresAt", 0);
            long nowMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            if (active is bool && (bool)active && (exp == 0 || exp > nowMs))
            {
                Banned = true; BanReason = Firestore.Str(ban, "reason") ?? "banned";
            }
        }

        private void DoSignOut()
        {
            try { UploadPending(true); } catch { }
            Forget();
            lock (gate) { Uid = null; Name = null; Banned = false; BanReason = null; pendingSave = null; Code = null; }
            Set(State.SignedOut, null);
            Log("Account: signed out");
        }

        // ------------------------------------------------------------------ helpers

        private static string FirebaseError(HttpResult r)
        {
            var e = MiniJson.Child(r.Json, "error");
            string m = MiniJson.Str(e, "message");
            if (m == null) m = MiniJson.Str(r.Json, "error");
            return m != null ? m + (m.Contains("REFERRER") ? " - the site's Firebase key only allows the website itself" : "") : r.ToString();
        }

        private static double Num(string s, double fallback)
        {
            double d; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : fallback;
        }

        /// <summary>The user id inside a Firebase ID token (its payload's user_id / sub).</summary>
        public static string JwtUid(string jwt)
        {
            try
            {
                var parts = jwt.Split('.');
                string p = parts[1].Replace('-', '+').Replace('_', '/');
                while (p.Length % 4 != 0) p += "=";
                var j = MiniJson.Obj(MiniJson.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(p))));
                return MiniJson.Str(j, "user_id") ?? MiniJson.Str(j, "sub");
            }
            catch { return null; }
        }

        public void Dispose()
        {
            running = false;
            wake.Set();
            try { thread.Join(3000); } catch { }
        }
    }
}
