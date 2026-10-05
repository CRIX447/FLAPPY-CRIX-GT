// Two copies of the in-game version (FlappyApp) play each other through photon_standin.py using
// only the screens' buttons: the room list, CREATE ROOM, joining from the list, the room,
// START MATCH, other birds on screen, standings and chat, Last One Standing results.
// Saves a picture of each screen (online-*.rgba) into the folder given.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FlappyCrix.Native;
using FlappyCrix.Online;
using FlappyCrix.Web.Cdp;

class OnlineUiTest
{
    static int fails;
    static string outDir;
    static void Check(bool ok, string what, string detail = "") { Console.WriteLine((ok ? "PASS " : "FAIL ") + what + (detail != "" ? "  -- " + detail : "")); if (!ok) fails++; }

    static FlappyApp Make(string name, DateTime when)
    {
        var cfg = new SiteConfig { PhotonNameServer = "wss://127.0.0.1:19093", Site = "http://127.0.0.1:9" };   // no account service: guests
        string dir = Path.Combine(Path.GetTempPath(), "fc-ui-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var online = new OnlineServices(cfg, dir, m => { if (m.Contains("Multiplayer")) Console.WriteLine("     [" + name + "] " + m); });
        var app = new FlappyApp(SaveData.Load(Path.Combine(dir, "save.txt")), SpriteSheet.LoadEmbedded(), name.Length, () => when, online);
        app.Save.Owned.Add(name == "A" ? "hat_witch" : "cap"); app.Save.Hat = name == "A" ? "hat_witch" : "cap";
        app.Save.Owned.Add("trail_ember"); app.Save.Trail = name == "A" ? "trail_ember" : "";
        return app;
    }

    static void Pump(FlappyApp[] apps, double secs, Func<bool> until = null, bool fly = false)
    {
        var end = DateTime.Now.AddSeconds(secs);
        while (DateTime.Now < end)
        {
            foreach (var a in apps)
            {
                if (fly && a.Screen == "playing" && a.InMatch)
                {
                    NativeSim.Pipe next = null;
                    foreach (var p in a.Sim.Pipes) if (p.X + NativeSim.PipeWidth > NativeSim.BirdX - 16) { next = p; break; }
                    float target = next != null ? (next.Top + next.Bottom) / 2 + 16 : 300;
                    if (a.Sim.BirdY > target && a.Sim.Velocity > -1) a.Flap();
                }
                a.Update(1 / 60f);
            }
            if (until != null && until()) return;
            Thread.Sleep(16);
        }
    }

    static void Shot(FlappyApp app, string name)
    {
        app.Render();
        var b = new byte[app.C.Px.Length * 4];
        Buffer.BlockCopy(app.C.Px, 0, b, 0, b.Length);
        File.WriteAllBytes(Path.Combine(outDir, name + ".rgba"), b);
    }

    static int Main(string[] args)
    {
        outDir = args.Length > 0 ? args[0] : ".";
        WebSocketClient.AcceptAnyCertificate = true;
        var when = new DateTime(2026, 10, 5, 1, 0, 0, DateTimeKind.Utc);
        var A = Make("A", when); var B = Make("B", when);
        var both = new[] { A, B };
        foreach (var a in both) a.Update(0.01f);
        Shot(A, "online-00-menu");
        Check(A.ButtonIds.Contains("online") && A.ButtonIds.Contains("account"), "the main menu has PLAY ONLINE and LINK ACCOUNT");

        A.OpenForTest("online"); A.Render();
        Pump(both, 10, () => A.Mp.State == Multiplayer.Phase.Lobby);
        Check(A.Mp.State == Multiplayer.Phase.Lobby, "PLAY ONLINE connects to the lobby by itself");
        Shot(A, "online-01-no-rooms");
        A.PressForTest("create_room"); A.Render();
        A.PressForTest("mode_lastone"); A.Render();
        Shot(A, "online-02-create");
        A.PressForTest("do_create");
        Pump(both, 10, () => A.InRoom);
        Check(A.InRoom && A.Mp.IsHost && A.Mp.Mode == "lastone", "CREATE makes a Last One Standing room, host", A.Mp.RoomCode ?? "");
        A.Render();
        Check(A.Modal == null || A.Modal == "online", "the room shows");
        if (A.Modal != null) A.Back();
        Shot(A, "online-03-room-host");

        B.OpenForTest("online"); B.Render();
        Pump(both, 10, () => B.Mp.State == Multiplayer.Phase.Lobby && B.Mp.ListRooms().Count > 0);
        B.Render();
        Shot(B, "online-04-room-list");
        string tile = B.ButtonIds.FirstOrDefault(id => id.StartsWith("room_"));
        Check(tile == "room_crix_" + A.Mp.RoomCode, "the room is in the other player's list", tile ?? "none");
        B.PressForTest("join_code"); B.Render();
        foreach (char ch in A.Mp.RoomCode) B.PressForTest("key_" + ch);
        B.Render();
        Shot(B, "online-05-join-by-code");
        B.PressForTest("key_join");
        Pump(both, 10, () => B.InRoom && A.Mp.Players.Count == 2);
        Check(B.InRoom && !B.Mp.IsHost, "JOIN BY CODE (on-screen keyboard) joins the room");
        Pump(both, 1);
        Check(A.Mp.Players.Values.Any(p => !p.Me && p.Name == B.PlayerName && p.Hat == "cap"), "the host sees the guest's name and hat", B.PlayerName);
        if (B.Modal != null) B.Back();
        B.Render();
        Shot(B, "online-06-room-guest");
        B.TogglePause(); B.Render();
        Check(B.Modal == "online", "PAUSE in a room opens the room menu");
        Shot(B, "online-07-room-menu");
        B.Back();

        // a message from "a website player" (the guest sends it the way the site does)
        B.Mp.Client.RaiseEvent(Multiplayer.EvChat, MiniJson.Args("text", "gl hf everyone!"), false);
        A.Render();
        A.PressForTest("ov_start");
        Pump(both, 5, () => A.InMatch && B.InMatch);
        Check(A.InMatch && B.InMatch && A.Modal == null && B.Modal == null, "START MATCH starts it for both, menus close");
        Pump(both, 6, null, true);
        var bOnA = A.Mp.Players.Values.First(p => !p.Me);
        Check(A.Mp.Visible(bOnA) && Math.Abs(A.Mp.ScreenX(bOnA) - 100) < 40, "the other bird is on screen next to yours", "x " + A.Mp.ScreenX(bOnA));
        Check(A.Mp.Chat.Count == 1 && A.Mp.Chat[0].Text == "gl hf everyone!", "chat shows");
        Shot(A, "online-08-playing");
        Shot(B, "online-09-playing-guest");
        // the guest stops flapping and crashes: the host is last one standing
        Pump(new[] { B }, 0.01);
        var endT = DateTime.Now.AddSeconds(15);
        while (DateTime.Now < endT && A.Mp.State != Multiplayer.Phase.Results)
        {
            Pump(new[] { A }, 0.02, null, true);
            B.Update(1 / 60f);
        }
        Pump(both, 0.5);
        Check(A.Mp.State == Multiplayer.Phase.Results && A.Mp.Winner == A.Mp.Me && B.Mp.Winner == A.Mp.Me, "Last One Standing: the host wins, both see it");
        Shot(A, "online-10-results-win");
        Shot(B, "online-11-results-lose");
        A.PressForTest("ov_continue");
        Check(A.Mp.State == Multiplayer.Phase.Room, "CONTINUE goes back to the room");
        B.TogglePause(); B.Render(); B.PressForTest("leave_room");
        Pump(both, 5, () => !B.InRoom && A.Mp.Players.Count == 1);
        Check(!B.InRoom && B.Screen == "menu" && A.Mp.Players.Count == 1, "LEAVE goes back to the lobby; the host sees it");
        foreach (var a in both) a.Online.Dispose();
        Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
