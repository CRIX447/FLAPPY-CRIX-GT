// The in-game version (FlappyApp) with a crixgamingvr.com account, against site_standin.py:
// linking with a code, the first sign-in adopting the guest progress, saves reaching the
// website's save (with update masks that keep the site's other fields), the website's newer
// save winning on the next start, staying signed in, signing out, bans, expired codes.
// Also saves the account screen (code + QR code) as account-code.rgba for a QR decode check.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FlappyCrix.Native;
using FlappyCrix.Online;
using FlappyCrix.Web.Cdp;

class AccountTest
{
    static int fails;
    static string Base;
    static void Check(bool ok, string what, string detail = "") { Console.WriteLine((ok ? "PASS " : "FAIL ") + what + (detail != "" ? "  -- " + detail : "")); if (!ok) fails++; }

    static SiteConfig Config() => new SiteConfig
    {
        Site = Base, IdentityToolkit = Base + "/v1", SecureToken = Base + "/v1", Firestore = Base + "/v1", PlayFabBase = Base,
        FirebaseApiKey = "WRONG-BUILT-IN-KEY",          // the live /api.json must correct it
    };

    static Dictionary<string, object> Doc(string uid)
    {
        var r = Http.Send("GET", Base + "/test/doc/" + uid);
        return Firestore.PlainFields(MiniJson.Child(r.Json, "fields"));
    }

    static void Post(string path, object body) => Http.PostJson(Base + path, body);
    static List<string> Calls()
    {
        var l = MiniJson.Parse(Http.PostJson(Base + "/test/calls", MiniJson.Args()).Body) as List<object>;
        return l == null ? new List<string>() : l.ConvertAll(o => o as string);
    }

    static bool Pump(FlappyApp app, Func<bool> until, double secs)
    {
        var end = DateTime.Now.AddSeconds(secs);
        while (DateTime.Now < end) { app.Update(0.02f); app.Render(); if (until()) return true; Thread.Sleep(20); }
        return false;
    }

    static FlappyApp NewApp(string dir, out OnlineServices online)
    {
        online = new OnlineServices(Config(), dir, m => Console.WriteLine("     " + m));
        var guest = SaveData.Load(Path.Combine(dir, "save.txt"));
        var app = new FlappyApp(guest, SpriteSheet.LoadEmbedded(), 3, null, online);
        online.Start();
        return app;
    }

    static int Main(string[] a)
    {
        Base = a.Length > 0 ? a[0] : "http://127.0.0.1:8091";
        Account.PollEvery = TimeSpan.FromMilliseconds(300);
        Account.UploadEvery = TimeSpan.FromMilliseconds(300);
        string dir = Path.Combine(Path.GetTempPath(), "fc-acct-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        // a guest with some progress
        var g = new SaveData(Path.Combine(dir, "save.txt")) { Coins = 500, Best = 12, Games = 3, Hat = "cap", Level = 2, Xp = 10 };
        g.Owned.Add("cap"); g.Achievements.Add("first");
        g.Save();

        OnlineServices online;
        var app = NewApp(dir, out online);
        var acct = online.Account;
        Pump(app, () => online.Config.LoadedFromSite, 5);
        Check(online.Config.FirebaseApiKey == "TESTKEY", "the site's live /api.json settings are used (Firebase key)");

        // 1. link: the game shows a code (and its QR code)
        app.OpenForTest("account"); app.Render();
        app.PressForTest("link");
        Check(Pump(app, () => acct.Status == Account.State.ShowingCode, 5), "LINK MY ACCOUNT shows a code", acct.Code ?? "");
        Check(acct.LinkUrl == Base + "/link?code=" + acct.Code, "the QR code / link opens the site's link page with the code", acct.LinkUrl);
        string opened = null; app.OpenUrl += u => opened = u;
        app.Update(0.02f); app.Render();
        app.PressForTest("open_pc");
        Check(opened == acct.LinkUrl, "OPEN ON THIS PC opens the link page in the PC's browser");
        File.WriteAllBytes(Path.Combine(a.Length > 1 ? a[1] : dir, "account-code.rgba"), ToBytes(app.C.Px));
        File.WriteAllText(Path.Combine(a.Length > 1 ? a[1] : dir, "account-code.txt"), acct.LinkUrl);

        // 2. the phone approves -> signed in; the first sign-in adopts the guest progress and uploads it
        Post("/test/claim", MiniJson.Args("code", acct.Code, "uid", "u1"));
        Check(Pump(app, () => app.SignedIn && app.Save.FilePath.EndsWith("save-u1.txt"), 10), "approving on the phone signs the game in", acct.Name ?? acct.Message ?? "");
        Check(app.PlayerName == "Crix Tester", "the account's name is used (and in multiplayer)", app.PlayerName);
        Pump(app, () => Doc("u1").ContainsKey("savedAt"), 5);
        var d = Doc("u1");
        Check(Firestore.Long(d, "coinCount") == 500 && Firestore.Long(d, "highScore") == 12 && Firestore.Str(d, "displayName") == "Crix Tester",
              "first sign-in: the guest progress goes up to the account", MiniJson.Serialize(d).Substring(0, Math.Min(200, MiniJson.Serialize(d).Length)));
        var eq = Firestore.Map(d, "equipped");
        Check(Firestore.Str(eq, "hat") == "cap" && Firestore.List(d, "owned").Contains("cap") && Firestore.List(d, "achievements").Count == 16,
              "owned, equipped and all 16 awards in the website's format");
        Check(Firestore.Long(Firestore.Map(d, "stats"), "level") == 2, "the stats mirror (level) is written");
        Check(acct.RoomPass("crix_TEST", 3) == "pass-u1-crix_TEST-3", "a signed-in game gets a room pass from the website (api/identity)");
        var calls = Calls();
        Check(calls.Contains("POST /api/playfab-login") && !calls.Contains("POST /Client/LoginWithCustomID"),
              "PlayFab sign-in goes through the website (api/playfab-login), not straight to PlayFab with the account id");

        // 3. the website has other fields; saves must not wipe them
        d = Doc("u1");
        var raw = Http.Send("GET", Base + "/test/doc/u1").Json;
        var fields = MiniJson.Child(raw, "fields");
        fields["discordId"] = MiniJson.Args("stringValue", "123456");
        MiniJson.Child(MiniJson.Child(MiniJson.Child(fields, "stats"), "mapValue"), "fields")["rp"] = MiniJson.Args("integerValue", "450");
        Post("/test/doc/u1", raw);
        // play a run: coins and games played change, the death saves straight away
        app.StartButton();
        long coinsBefore = app.Save.Coins;
        for (int t = 0; t < 60 * 8 && app.Screen == "playing"; t++)
        {
            NativeSim.Pipe next = null;
            foreach (var p in app.Sim.Pipes) if (p.X + NativeSim.PipeWidth > NativeSim.BirdX - 16) { next = p; break; }
            float target = next != null ? (next.Top + next.Bottom) / 2 + 16 : 300;
            if (app.Sim.BirdY > target && app.Sim.Velocity > -1) app.Flap();
            app.Update(1 / 60f);
        }
        for (int t = 0; t < 60 * 10 && app.Screen == "playing"; t++) app.Update(1 / 60f);
        Check(app.Screen == "dead" && app.Save.Coins > coinsBefore, "a run on the account earns coins", app.Save.Coins + " coins");
        Pump(app, () => Firestore.Long(Doc("u1"), "gamesPlayed") == 4, 5);
        d = Doc("u1");
        Check(Firestore.Long(d, "gamesPlayed") == 4 && Firestore.Long(d, "coinCount") == app.Save.Coins, "the run is saved to the account right after the crash");
        Check(Firestore.Str(d, "discordId") == "123456" && Firestore.Long(Firestore.Map(d, "stats"), "rp") == 450,
              "the website's other fields survive (update masks)");

        // 4. next start: still signed in; the website's newer save wins
        raw = Http.Send("GET", Base + "/test/doc/u1").Json;
        fields = MiniJson.Child(raw, "fields");
        fields["coinCount"] = MiniJson.Args("integerValue", "9999");
        fields["savedAt"] = MiniJson.Args("integerValue", (SaveData.NowMs() + 60000).ToString());
        var owned = MiniJson.Child(MiniJson.Child(fields, "owned"), "arrayValue")["values"] as List<object>;
        owned.Add(MiniJson.Args("stringValue", "trail_void"));
        Post("/test/doc/u1", raw);
        online.Dispose();
        var app2 = NewApp(dir, out online);
        acct = online.Account;
        Check(Pump(app2, () => app2.SignedIn && app2.Save.Coins == 9999, 10), "next start: still signed in, the website's newer save is loaded", app2.Save.Coins + "");
        Check(app2.Save.Owned.Contains("trail_void"), "cosmetics bought on the website show up in the game");

        // 5. sign out -> back to the guest save (untouched), sign-in forgotten
        app2.OpenForTest("account"); app2.Render();
        app2.PressForTest("signout");
        Check(Pump(app2, () => !app2.SignedIn && app2.Save.FilePath.EndsWith("save.txt"), 5), "SIGN OUT goes back to the guest progress");
        Check(app2.Save.Coins == 500 && !File.Exists(Path.Combine(dir, "account.txt")), "the guest save is as it was, and the sign-in is forgotten", app2.Save.Coins + "");

        // 6. an expired code says so
        app2.PressForTest("link");
        Pump(app2, () => acct.Status == Account.State.ShowingCode, 5);
        Post("/test/claim", MiniJson.Args("code", acct.Code, "expire", true));
        Check(Pump(app2, () => acct.Status == Account.State.Error, 5), "an expired code asks for a new one", acct.Message ?? "");

        // 7. a banned account: signed in, multiplayer off
        app2.Update(0.02f); app2.Render();
        app2.PressForTest("link");
        Pump(app2, () => acct.Status == Account.State.ShowingCode, 5);
        Post("/test/claim", MiniJson.Args("code", acct.Code, "uid", "banned"));
        Check(Pump(app2, () => app2.SignedIn, 10) && acct.Banned, "a banned account is recognised (PlayFab AccountBanned)", acct.BanReason ?? "");
        app2.OpenForTest("online"); app2.Render();
        Check(!new List<string>(app2.ButtonIds).Contains("create_room"), "a banned account can't use multiplayer");
        app2.PressForTest("back");

        // 8. a revoked sign-in (password changed on the website) signs out on the next start
        Post("/test/revoke", MiniJson.Args("uid", "banned"));
        online.Dispose();
        var app3 = NewApp(dir, out online);
        Pump(app3, () => online.Account.Status == Account.State.SignedOut && online.Account.Message != null, 8);
        Check(!app3.SignedIn && online.Account.Message != null && online.Account.Message.Contains("signed out"), "a revoked sign-in signs the game out", online.Account.Message ?? "");
        online.Dispose();

        // 9. a website that hasn't switched the safe sign-in on yet: the old way still works
        Post("/test/pfserver", MiniJson.Args("on", false));
        var app4 = NewApp(dir, out online);
        app4.OpenForTest("account"); app4.Render();
        app4.PressForTest("link");
        Pump(app4, () => online.Account.Status == Account.State.ShowingCode, 5);
        Post("/test/claim", MiniJson.Args("code", online.Account.Code, "uid", "u2"));
        Check(Pump(app4, () => app4.SignedIn, 10) && Calls().Contains("POST /Client/LoginWithCustomID"),
              "until the website switches it on, PlayFab sign-in falls back to the old way");
        online.Dispose();

        Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }

    static byte[] ToBytes(uint[] px) { var b = new byte[px.Length * 4]; Buffer.BlockCopy(px, 0, b, 0, b.Length); return b; }
}
