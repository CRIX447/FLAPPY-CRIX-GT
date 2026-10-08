// The in-game version's online part: linking a crixgamingvr.com account (code + QR code, the
// site's /link page), keeping progress in that account, and multiplayer with website players
// (room list, create, join by code, the room, other birds, standings, chat, results).
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FlappyCrix.Online;

namespace FlappyCrix.Native
{
    public sealed partial class FlappyApp
    {
        public readonly OnlineServices Online;
        public Multiplayer Mp { get; private set; }
        /// <summary>Open this address in the PC's browser (the account link page, when the player asks).</summary>
        public event Action<string> OpenUrl;

        private readonly SaveData guestSave;
        private string pendingDisplayName, onlineSub, codeEntry = "", createMode = "freemode", lastQrText;
        private bool createPublic = true, wasInRoom, wasMatch, connectTried;
        private Account.State lastAccountState = Account.State.SignedOut;
        private QrCode qr;

        public bool InRoom => Mp != null && Mp.InRoom;
        public bool InMatch => InRoom && Mp.MatchActive;
        public bool SignedIn => Online != null && Online.Account.Status == Account.State.SignedIn;
        public string PlayerName => SignedIn && Online.Account.Name != null ? Online.Account.Name : "Guest " + guestSave.GuestNo;

        static readonly string KeyAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static string ModeName(string m)
        {
            switch (m)
            {
                case "race": return "RACE";
                case "lastone": return "LAST ONE STANDING";
                case "coinrush": return "COIN RUSH";
                case "candyhunt": return "CANDY HUNT";
                case "presents": return "PRESENTS";
                case "egghunt": return "EGG HUNT";
                default: return "FREE PLAY";
            }
        }

        void InitOnline()
        {
            if (guestSave.GuestNo < 1000) { guestSave.GuestNo = new Random().Next(1000, 10000); guestSave.Dirty = true; }
            if (Online == null) return;
            Mp = new Multiplayer(Online.Photon, Sim);
            Online.Mp = Mp;
            Mp.Identity = () => new Dictionary<string, object>
            {
                { "level", Save.Level }, { "hat", Save.Hat == "" ? null : Save.Hat }, { "trail", Save.Trail == "" ? null : Save.Trail },
            };
            Mp.PassFor = (room, actor) =>
                Online.Account != null && Online.Account.Status == Account.State.SignedIn ? Online.Account.RoomPass(room, actor) : null;
            Mp.Notice += (t, b) => AddToast(null, t, b, Accent);
            Mp.Sound += (n, v) => Sfx(n, v);
            Mp.Reward += (coins, xp) =>
            {
                if (coins > 0) { Save.Coins += coins; AddToast("coin", "COIN RUSH BONUS", "+" + coins + " coins (double your coins)", Gold); }
                if (xp > 0) { for (int i = 0; i < xp; i++) GiveXp(); AddToast("1f3c6", "YOU WIN!", "+" + xp + " XP", Gold); }
                Save.Dirty = true;
                CheckAchievements();
            };
            Sim.Scored += s => { if (InMatch) Mp.OnScored(s); };
            Sim.CoinsGained += n => { if (InMatch) Mp.OnCoin(); };
        }

        void GiveXp()
        {
            if (Save.Level >= Catalog.MaxLevel) return;
            Save.Xp++;
            while (Save.Level < Catalog.MaxLevel && Save.Xp >= Catalog.XpForLevel(Save.Level))
            {
                Save.Xp -= Catalog.XpForLevel(Save.Level);
                Save.Level++;
                AddToast("2b50", "LEVEL " + Save.Level, "Keep flying!", Gold);
                Sfx("ach", 1f);
            }
        }

        // ------------------------------------------------------------------ every frame

        void UpdateOnline(float dt)
        {
            if (Online == null) return;
            var acct = Online.Account;

            var loaded = acct.TakeLoaded();
            if (loaded != null) AdoptAccount(loaded);
            if (acct.Status != lastAccountState)
            {
                if (acct.Status == Account.State.SignedOut && Save != guestSave)
                {
                    Flush();
                    guestSave.CopySettingsFrom(Save);
                    SwitchSave(guestSave);
                    AddToast(null, "SIGNED OUT", "Playing as " + PlayerName + ". Your account progress is safe.", Muted);
                    if (acct.Message != null) AddToast(null, "Account", acct.Message, Red);
                }
                else if (acct.Status == Account.State.Error && modal != "account") AddToast(null, "Account", acct.Message ?? "Something went wrong", Red);
                lastAccountState = acct.Status;
                Changed();
            }

            Mp.Nickname = PlayerName;
            Mp.Update(dt);

            if (InRoom && acct.Banned) { Mp.Leave(); AddToast(null, "Multiplayer is off", "This account is banned (" + acct.BanReason + ").", Red); }
            if (Mp.MatchActive && !wasMatch) { modal = null; acc = 0; MusicRestarts++; Changed(); }
            if (!InRoom && wasInRoom) { Sim.LeaveMp(); Sim.ToMenu(); Changed(); }
            wasMatch = Mp.MatchActive;
            wasInRoom = InRoom;
        }

        void AdoptAccount(Account.Loaded l)
        {
            string dir = Online.DataRoot ?? Path.GetDirectoryName(guestSave.FilePath ?? "");
            string path = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "save-" + SafeName(l.Uid) + ".txt");
            SaveData local = path != null && File.Exists(path) ? SaveData.Load(path, m => Log?.Invoke(m)) : null;
            SaveData acct;
            if (CloudSave.HasSave(l.Doc))
            {
                acct = local ?? new SaveData(path);
                if (local == null || local.SavedAt <= Firestore.Long(l.Doc, "savedAt")) CloudSave.Apply(l.Doc, acct);
            }
            else
            {
                // first time on this account: it takes the progress made as a guest, as the website does
                acct = local ?? guestSave.CopyTo(path);
                pendingDisplayName = l.Name;
            }
            if (l.PfLevel > 0) CloudSave.TakeLevelIfHigher(acct, l.PfLevel, l.PfXp);
            if (l.PfBest > acct.Best) acct.Best = l.PfBest;
            acct.CopySettingsFrom(Save);
            acct.GuestNo = guestSave.GuestNo;
            acct.Dirty = true;
            SwitchSave(acct);
            Flush(true);
            AddToast("2705", "SIGNED IN", l.Name + " - your progress is now saved to your account.", Green);
            Sfx("unlock", 0.8f);
            CheckAchievements();
        }

        static string SafeName(string s)
        {
            var c = (s ?? "account").ToCharArray();
            for (int i = 0; i < c.Length; i++) if (!char.IsLetterOrDigit(c[i]) && c[i] != '-' && c[i] != '_') c[i] = '_';
            return new string(c);
        }

        void SwitchSave(SaveData s)
        {
            if (Save.Dirty) Save.Save();
            Save = s;
            Sim.ReducedMotion = s.ReducedMotion;
            leftSig = rightSig = modalSig = null;
            Changed();
        }

        void CloudSaveNow(bool now)
        {
            if (!SignedIn || Save == guestSave) return;
            Online.Account.QueueSave(CloudSave.Fields(Save, AchievementProgress, pendingDisplayName), now);
            pendingDisplayName = null;
        }

        string AccountKey() => Online == null ? "" : Online.Account.Status + "," + Online.Account.Name + "," + Online.Account.Banned;

        string OnlineSig()
        {
            if (Online == null) return "";
            var a = Online.Account;
            var b = new System.Text.StringBuilder();
            b.Append(a.Version).Append(a.Status).Append(a.Code);
            if (a.Status == Account.State.ShowingCode) b.Append((int)(a.CodeExpiresUtc - DateTime.UtcNow).TotalSeconds);
            b.Append('|').Append(Mp.State).Append(Mp.Error).Append(onlineSub).Append(codeEntry).Append(createMode).Append(createPublic).Append(Mp.Mode);
            if (Mp.State == Multiplayer.Phase.Lobby)
                foreach (var r in Mp.ListRooms()) b.Append(r.Name).Append(r.PlayerCount).Append(r.Str("mode"));
            if (InRoom)
            {
                b.Append(Mp.IsHost).Append(Mp.MatchActive);
                foreach (var p in Mp.Players.Values) b.Append(p.Actor).Append(p.Name).Append(p.Level);
                b.Append(Mp.Chat.Count);
            }
            return b.ToString();
        }

        string OnlineRightKey()
        {
            if (!InRoom) return "";
            var b = new System.Text.StringBuilder();
            b.Append(Mp.State).Append(Mp.Mode).Append(Mp.IsHost).Append(Mp.Chat.Count).Append(Mp.PingMs / 20);
            if (Mp.MatchActive && Mp.Mode == "coinrush") b.Append((int)Mp.MatchClock);
            foreach (var p in Mp.Players.Values) b.Append(p.Actor).Append(p.Name).Append(p.Me ? (Mp.Mode == "coinrush" ? Sim.CoinsThisMatch : Sim.Score) : p.Score).Append(p.Dead);
            return b.ToString();
        }

        void AccountLine(float x, float y)
        {
            if (Online == null) { C.Text("in Gorilla Tag  -  v" + Version, x, y, 13, Dim); return; }
            var a = Online.Account;
            if (SignedIn) C.Text("Signed in: " + Clip(a.Name ?? "", 22), x, y, 13, Green);
            else if (a.Status == Account.State.Restoring) C.Text("Signing in...", x, y, 13, Muted);
            else C.Text(PlayerName + " - link your account!", x, y, 13, Dim);
        }

        static string Clip(string s, int n) => s.Length > n ? s.Substring(0, n - 1) + "." : s;

        // ------------------------------------------------------------------ account screen

        void AccountScreen()
        {
            var a = Online.Account;
            ModalFrame("ACCOUNT", "1f513", "Your crixgamingvr.com account");
            float x = MX + 40, y = MY + 96, w = MW - 80;
            switch (a.Status)
            {
                case Account.State.SignedIn:
                {
                    C.Circle(x + 50, y + 54, 46, Accent);
                    string initial = (a.Name ?? "?").Substring(0, 1).ToUpperInvariant();
                    C.Text(initial, x + 50, y + 30, 48, White, 1, Align.Centre, true);
                    C.Text(a.Name ?? "Player", x + 116, y + 14, 34, White);
                    C.Text("Level " + Save.Level + "  -  " + Catalog.FormatCoins(Save.Coins) + " coins  -  best " + Save.Best, x + 118, y + 60, 16, Gold);
                    C.TextWrapped("Signed in. Your coins, best score, cosmetics, awards and level are saved to your account and shared with the website. " +
                                  "In multiplayer you play as " + (a.Name ?? "you") + ".", x, y + 130, w, 16, Muted);
                    if (a.Banned) C.TextWrapped("This account is banned (" + a.BanReason + "), so multiplayer is off.", x, y + 200, w, 16, Red);
                    Button("signout", x, y + 260, 260, 56, "SIGN OUT", null, Style.Normal, true, () => a.SignOut(), 20);
                    Button("acc_back", x + 280, y + 260, 260, 56, "DONE", null, Style.Primary, true, CloseModal, 20);
                    defaultFocus = "acc_back";
                    break;
                }
                case Account.State.ShowingCode:
                {
                    // the code, big, and a QR code of the link page with the code filled in
                    float qs = 300;
                    DrawQr(a.LinkUrl, MX + MW - 40 - qs, y - 6, qs);
                    float tw = w - qs - 30;
                    C.Text("1. On your phone or PC, open", x, y, 20, White);
                    C.Text(Online.Config.LinkPage.Replace("https://", ""), x + 20, y + 30, 26, Accent);
                    C.Text("   (or scan the QR code with your phone)", x, y + 66, 16, Muted);
                    C.Text("2. Sign in (Google, or email and password)", x, y + 100, 20, White);
                    C.Text("3. Enter this code:", x, y + 140, 20, White);
                    C.RoundRect(x + 20, y + 176, tw - 40, 96, 18, Ink, 0.85f);
                    C.Text(a.Code ?? "", x + 20 + (tw - 40) / 2, y + 196, 48, Gold, 1, Align.Centre, true);
                    int left = Math.Max(0, (int)(a.CodeExpiresUtc - DateTime.UtcNow).TotalSeconds);
                    C.Text("The game links by itself once you've done it.  Code expires in " + left / 60 + ":" + (left % 60).ToString("00", CultureInfo.InvariantCulture),
                           x, y + 290, 13, Dim);
                    Button("open_pc", x, y + 326, 230, 52, "OPEN ON THIS PC", null, Style.Normal, true, () => OpenUrl?.Invoke(a.LinkUrl), 16);
                    Button("new_code", x + 244, y + 326, 170, 52, "NEW CODE", null, Style.Normal, true, () => a.LinkDevice(), 16);
                    Button("cancel_code", x + 428, y + 326, 150, 52, "CANCEL", null, Style.Normal, true, () => a.Cancel(), 16);
                    defaultFocus = "open_pc";
                    break;
                }
                case Account.State.GettingCode:
                case Account.State.Linking:
                case Account.State.Restoring:
                    C.Text(a.Status == Account.State.GettingCode ? "Getting a code from crixgamingvr.com..." : "Signing in...", MX + MW / 2, y + 120, 26, White, 1, Align.Centre);
                    defaultFocus = "back";
                    break;
                default:
                {
                    C.Text("Play with your website account", x, y, 26, White);
                    C.TextWrapped("Link the game to your crixgamingvr.com account to keep the same coins, best score, cosmetics, awards and level " +
                                  "as on the website, and to use your name in multiplayer. You sign in on your phone or PC - nothing to type in VR.",
                                  x, y + 42, w, 16, Muted);
                    C.Text("1. Press LINK MY ACCOUNT - the game shows a code", x, y + 130, 20, White);
                    C.Text("2. On your phone or PC: crixgamingvr.com/link, sign in", x, y + 164, 20, White);
                    C.Text("3. Enter the code there (or scan the QR code)", x, y + 198, 20, White);
                    if (a.Message != null) C.TextWrapped(a.Message, x, y + 240, w, 16, a.Status == Account.State.Error ? Red : Muted);
                    Button("link", x, y + 300, 340, 64, a.Status == Account.State.Error ? "TRY AGAIN" : "LINK MY ACCOUNT", "1f513", Style.Primary, true, () => a.LinkDevice(), 20);
                    defaultFocus = "link";
                    break;
                }
            }
        }

        void DrawQr(string text, float x, float y, float size)
        {
            if (text == null) return;
            if (text != lastQrText) { lastQrText = text; try { qr = QrCode.Encode(text); } catch { qr = null; } }
            C.RoundRect(x, y, size, size, 12, White);
            if (qr == null) return;
            int n = qr.Size + 8;                          // quiet zone of 4 modules
            float m = (float)Math.Floor(size / n);
            float ox = (float)Math.Floor(x + (size - m * qr.Size) / 2), oy = (float)Math.Floor(y + (size - m * qr.Size) / 2);
            for (int yy = 0; yy < qr.Size; yy++)
                for (int xx = 0; xx < qr.Size; xx++)
                    if (qr[xx, yy]) C.Rect(ox + xx * m, oy + yy * m, m, m, Col.Black);
        }

        // ------------------------------------------------------------------ multiplayer screens

        void OnlineScreen()
        {
            var a = Online.Account;
            if (InRoom) { RoomScreen(); return; }
            ModalFrame("PLAY ONLINE", "1f3ae", "With players on crixgamingvr.com  -  you are " + PlayerName);
            float x = MX + 24, y = MY + 84, w = MW - 48;
            if (a.Banned)
            {
                C.TextWrapped("This account is banned (" + a.BanReason + "), so multiplayer is off.", x, y + 40, w, 20, Red);
                defaultFocus = "back";
                return;
            }
            if (Mp.State == Multiplayer.Phase.Off && !connectTried) { connectTried = true; Mp.Connect(); }
            switch (Mp.State)
            {
                case Multiplayer.Phase.Off:
                    C.Text("Not connected", MX + MW / 2, y + 90, 26, White, 1, Align.Centre);
                    if (Mp.Error != null) C.TextWrapped(Mp.Error, x + 80, y + 134, w - 160, 16, Red, 1, Align.Centre);
                    Button("connect", MX + MW / 2 - 140, y + 220, 280, 60, "CONNECT", "1f504", Style.Primary, true, () => Mp.Connect(), 20);
                    defaultFocus = "connect";
                    return;
                case Multiplayer.Phase.Connecting:
                case Multiplayer.Phase.Joining:
                    C.Text(Mp.State == Multiplayer.Phase.Connecting ? "Connecting to multiplayer..." : "Joining...", MX + MW / 2, y + 120, 26, White, 1, Align.Centre);
                    defaultFocus = "back";
                    return;
            }
            if (onlineSub == "create") { CreateRoomScreen(x, y, w); return; }

            // the room list (the website's: public, open, casual rooms)
            Button("create_room", x, y, 230, 50, "CREATE ROOM", "2728", Style.Primary, true, () => { onlineSub = "create"; Swoosh(); }, 16);
            Button("join_code", x + 244, y, 230, 50, "JOIN BY CODE", "1f513", Style.On, true, () => { codeEntry = ""; modal = "keyboard"; Swoosh(); }, 16);
            defaultFocus = "create_room";
            var rooms = Mp.ListRooms();
            float gy = y + 66, cw = (w - 12) / 2f, ch = 96;
            if (rooms.Count == 0)
            {
                C.Text("No open rooms right now.", MX + MW / 2, gy + 90, 26, White, 1, Align.Centre);
                C.Text("Create one - website players can join it too.", MX + MW / 2, gy + 130, 16, Muted, 1, Align.Centre);
            }
            for (int i = 0; i < Math.Min(8, rooms.Count); i++)
            {
                var r = rooms[i];
                bool can = Multiplayer.CanPlay(r);
                string name = r.Name;
                Tile("room_" + name, x + (i % 2) * (cw + 12), gy + (i / 2) * (ch + 10), cw, ch, can, () => Mp.JoinRoom(name), (tx, ty, tw, th) =>
                {
                    C.Text(Clip(r.Str("roomName") ?? name, 28), tx + 16, ty + 12, 20, White);
                    string mode = r.Str("mode") ?? "freemode";
                    C.Text(ModeName(mode) + (Multiplayer.SiteOnlyModes.Contains(mode) ? "  (website only)" : ""), tx + 16, ty + 44, 16, Multiplayer.SiteOnlyModes.Contains(mode) ? Dim : Accent);
                    C.Text(r.PlayerCount + " / " + Math.Max(r.PlayerCount, r.MaxPlayers) + " players", tx + 16, ty + 68, 13, Muted);
                    C.Text(can ? "JOIN" : "FULL", tx + tw - 16, ty + 36, 20, can ? Green : Dim, 1, Align.Right);
                });
            }
            C.Text("Ranked rooms and the seasonal room modes are on the website.", MX + MW / 2, MY + MH - 34, 13, Dim, 1, Align.Centre);
        }

        void CreateRoomScreen(float x, float y, float w)
        {
            C.Text("CREATE A ROOM", x, y, 26, White);
            string[] desc = { "Fly together, no winner - just for fun.", "First to 100 points wins.", "The last bird flying wins.", "Most coins in 5 minutes wins." };
            float cw = (w - 36) / 4f;
            for (int i = 0; i < 4; i++)
            {
                string m = Multiplayer.Modes[i];
                bool on = createMode == m;
                Tile("mode_" + m, x + i * (cw + 12), y + 44, cw, 150, true, () => { createMode = m; Sfx("pop", 0.35f); }, (tx, ty, tw, th) =>
                {
                    if (on) C.RoundRect(tx + 3, ty + 3, tw - 6, th - 6, 12, Accent, 0.3f);
                    C.Text(ModeName(m), tx + tw / 2, ty + 18, ModeName(m).Length > 12 ? 13 : 20, on ? Gold : White, 1, Align.Centre);
                    C.TextWrapped(desc[i], tx + 12, ty + 60, tw - 24, 13, Muted, 1, Align.Centre);
                    if (on) Icon("2705", tx + tw / 2, ty + th - 24, 26);
                });
            }
            C.Text("Who can join:", x, y + 222, 20, White);
            Button("pub", x + 170, y + 212, 200, 46, "EVERYONE", null, createPublic ? Style.On : Style.Normal, true, () => createPublic = true, 16);
            Button("priv", x + 384, y + 212, 260, 46, "ONLY WITH THE CODE", null, !createPublic ? Style.On : Style.Normal, true, () => createPublic = false, 16);
            Button("do_create", x, y + 290, 300, 62, "CREATE", "2728", Style.Primary, true, () =>
            {
                onlineSub = null;
                Mp.CreateRoom(createMode, 8, createPublic, PlayerName + "'s Lobby");
            }, 26);
            Button("create_back", x + 316, y + 290, 200, 62, "BACK", null, Style.Normal, true, () => onlineSub = null, 20);
            defaultFocus = "do_create";
        }

        void KeyboardScreen()
        {
            ModalFrame("JOIN BY CODE", "1f513", "The 4-letter code the room's host sees");
            float y = MY + 90;
            for (int i = 0; i < 4; i++)
            {
                float bx = MX + MW / 2 - 2 * 92 + i * 92;
                C.RoundRect(bx, y, 80, 92, 14, Ink, 0.85f);
                if (i < codeEntry.Length) C.Text(codeEntry[i].ToString(), bx + 40, y + 20, 48, Gold, 1, Align.Centre);
                else if (i == codeEntry.Length) C.Rect(bx + 20, y + 74, 40, 4, Accent);
            }
            float ky = y + 116, kw = 92, kh = 62;
            float kx0 = MX + (MW - 8 * kw - 7 * 8) / 2;
            for (int i = 0; i < KeyAlphabet.Length; i++)
            {
                char ch = KeyAlphabet[i];
                Button("key_" + ch, kx0 + (i % 8) * (kw + 8), ky + (i / 8) * (kh + 8), kw, kh, ch.ToString(), null, Style.Normal, codeEntry.Length < 4,
                       () => { if (codeEntry.Length < 4) codeEntry += ch; }, 26);
            }
            float by = ky + 4 * (kh + 8) + 6;
            Button("key_del", kx0, by, 2 * kw + 8, 56, "DELETE", null, Style.Normal, codeEntry.Length > 0, () => codeEntry = codeEntry.Substring(0, codeEntry.Length - 1), 20);
            Button("key_join", kx0 + 2 * (kw + 8), by, 4 * kw + 24, 56, "JOIN", "25b6", Style.Primary, codeEntry.Length == 4, () =>
            {
                modal = "online";
                Mp.JoinByCode(codeEntry);
            }, 26);
            Button("key_back", kx0 + 6 * (kw + 8), by, 2 * kw + 8, 56, "BACK", null, Style.Normal, true, () => modal = "online", 20);
            defaultFocus = codeEntry.Length == 4 ? "key_join" : "key_A";
        }

        /// <summary>The room, as a full menu (PAUSE in a room opens it): players, chat, start, mode, leave.</summary>
        void RoomScreen()
        {
            ModalFrame(Clip(Mp.RoomTitle ?? "ROOM", 22), "1f3ae", "Room code " + Mp.RoomCode + "  -  " + ModeName(Mp.Mode) + (Mp.IsHost ? "  -  you're the host" : ""));
            float x = MX + 24, y = MY + 84, w = MW - 48;
            float lw = w * 0.5f;
            C.Text("PLAYERS", x, y, 16, Muted);
            int i = 0;
            foreach (var p in Sorted())
            {
                float ry = y + 26 + i * 44;
                if (i >= 8) break;
                C.RoundRect(x, ry, lw, 38, 10, p.Me ? Col.Lerp(Panel2, Accent, 0.2f) : Panel2);
                C.Circle(x + 20, ry + 19, 9, Col.Hex(p.Colour));
                C.Text(Clip(p.Name, 20) + (p.Me ? "  (you)" : ""), x + 38, ry + 9, 16, White);
                C.Text("Lv " + (p.Me ? Save.Level : p.Level), x + lw - 14, ry + 10, 13, Gold, 1, Align.Right);
                if (p.Actor == Mp.Client.Host) Icon("1f451", x + lw - 70, ry + 19, 20);
                i++;
            }
            // chat (read-only)
            float cx = x + lw + 16, cw = w - lw - 16;
            C.Text("CHAT", cx, y, 16, Muted);
            C.RoundRect(cx, y + 26, cw, 250, 12, Ink, 0.6f);
            ChatLines(cx + 12, y + 36, cw - 24, 230, 16);
            float by = MY + MH - 84;
            if (Mp.MatchActive)
                Button("to_game", x, by, 300, 60, "BACK TO THE GAME", "25b6", Style.Primary, true, CloseModal, 20);
            else if (Mp.IsHost)
            {
                Button("start_match", x, by, 300, 60, "START MATCH", "25b6", Style.Primary, true, () => { Mp.StartMatch(); }, 20);
                for (int m = 0; m < 4; m++)
                {
                    string mode = Multiplayer.Modes[m];
                    Button("rmode_" + mode, x + 316 + m * 112, by + 8, 104, 44, ModeName(mode).Split(' ')[0], null, Mp.Mode == mode ? Style.On : Style.Normal, true, () => Mp.SetMode(mode), 13);
                }
            }
            else C.Text(Mp.MatchInProgressOnJoin ? "A match is on - you'll play the next one." : "Waiting for the host to start...", x, by + 18, 20, Muted);
            Button("leave_room", MX + MW - 24 - 180, by, 180, 60, "LEAVE", null, Style.Normal, true, () => { Mp.Leave(); modal = "online"; }, 20);
            defaultFocus = Mp.MatchActive ? "to_game" : Mp.IsHost ? "start_match" : "leave_room";
        }

        IEnumerable<Multiplayer.Player> Sorted()
        {
            var l = new List<Multiplayer.Player>(Mp.Players.Values);
            l.Sort((a, b) => Score(b).CompareTo(Score(a)) != 0 ? Score(b).CompareTo(Score(a)) : a.Actor.CompareTo(b.Actor));
            return l;
        }

        int Score(Multiplayer.Player p) => p.Me ? (Mp.Mode == "coinrush" ? Sim.CoinsThisMatch : Sim.Score) : p.Score;

        void ChatLines(float x, float y, float w, float h, int size)
        {
            if (Mp.Chat.Count == 0) { C.TextWrapped("No messages yet. Chat from website players shows here (typing isn't possible in VR).", x, y, w, 13, Dim); return; }
            var lines = new List<KeyValuePair<string, uint>>();
            foreach (var c in Mp.Chat)
                foreach (var l in C.Wrap(c.Name + ": " + c.Text, w, size)) lines.Add(new KeyValuePair<string, uint>(l, Col.Lerp(Col.Hex(c.Colour), White, 0.4f)));
            float lh = C.Font.Get(size).LineHeight;
            int max = Math.Max(1, (int)(h / lh));
            int start = Math.Max(0, lines.Count - max);
            for (int i = start; i < lines.Count; i++) C.Text(lines[i].Key, x, y + (i - start) * lh, size, lines[i].Value);
        }

        // ---- in the game area

        void RoomOverlay()
        {
            float x = PX + 30, w = PW - 60;
            switch (Mp.State)
            {
                case Multiplayer.Phase.Room:
                {
                    float y = PY + 150;
                    Card(x, y, w, 300, true);
                    C.Text(Clip(Mp.RoomTitle ?? "Room", 22), x + w / 2, y + 16, 26, White, 1, Align.Centre, true);
                    C.Text("ROOM CODE", x + w / 2, y + 56, 13, Muted, 1, Align.Centre);
                    C.Text(Mp.RoomCode ?? "", x + w / 2, y + 72, 48, Gold, 1, Align.Centre, true);
                    C.Text(ModeName(Mp.Mode) + "  -  " + Mp.Players.Count + (Mp.Players.Count == 1 ? " player" : " players"), x + w / 2, y + 132, 16, Accent, 1, Align.Centre);
                    if (Mp.IsHost)
                    {
                        Button("ov_start", x + 20, y + 166, w - 40, 56, "START MATCH", "25b6", Style.Primary, true, () => Mp.StartMatch(), 26);
                        defaultFocus = "ov_start";
                    }
                    else C.TextWrapped(Mp.MatchInProgressOnJoin ? "A match is on - you'll play the next one." : "Waiting for the host to start the match...", x + 20, y + 176, w - 40, 16, Muted, 1, Align.Centre);
                    Button("ov_menu", x + 20, y + 234, w - 40, 48, "ROOM MENU  (PAUSE)", null, Style.Normal, true, () => Open("online"), 16);
                    if (!Mp.IsHost) defaultFocus = "ov_menu";
                    break;
                }
                case Multiplayer.Phase.Results:
                {
                    var res = Mp.Results ?? new List<Multiplayer.Result>();
                    float y = PY + 110;
                    float h = 200 + Math.Min(6, res.Count) * 34;
                    Card(x, y, w, h, true);
                    string head = Mp.Winner == Mp.Me ? "YOU WIN!" : Mp.Winner != 0 ? Clip(NameOf(Mp.Winner, res), 14) + " WINS" : "MATCH OVER";
                    C.Text(head, x + w / 2, y + 16, 34, Mp.Winner == Mp.Me ? Gold : White, 1, Align.Centre, true);
                    for (int i = 0; i < Math.Min(6, res.Count); i++)
                    {
                        var r = res[i];
                        float ry = y + 70 + i * 34;
                        bool me = r.Actor == Mp.Me;
                        C.RoundRect(x + 16, ry, w - 32, 30, 8, me ? Col.Lerp(Panel2, Accent, 0.25f) : Panel2);
                        C.Text((i + 1) + ".  " + Clip(r.Name, 18), x + 28, ry + 6, 16, White);
                        C.Text(r.Score.ToString(CultureInfo.InvariantCulture), x + w - 28, ry + 6, 16, Gold, 1, Align.Right);
                    }
                    Button("ov_continue", x + 20, y + h - 76, w - 40, 56, "CONTINUE", "25b6", Style.Primary, true, () => Mp.CloseResults(), 26);
                    defaultFocus = "ov_continue";
                    break;
                }
                case Multiplayer.Phase.Playing:
                {
                    if (Mp.Spectating) { C.RoundRect(PX + 60, PY + 520, PW - 120, 40, 20, Ink, 0.75f); C.Text("You're out - watching", PX + PW / 2f, PY + 530, 16, White, 1, Align.Centre); }
                    if (Mp.Mode == "coinrush")
                    {
                        int left = Math.Max(0, Mp.RushSeconds - (int)Mp.MatchClock);
                        C.RoundRect(PX + PW / 2f - 60, PY + 96, 120, 34, 17, Ink, 0.7f);
                        C.Text(left / 60 + ":" + (left % 60).ToString("00", CultureInfo.InvariantCulture), PX + PW / 2f, PY + 102, 20, Gold, 1, Align.Centre);
                    }
                    break;
                }
            }
        }

        static string NameOf(int actor, List<Multiplayer.Result> res)
        {
            foreach (var r in res) if (r.Actor == actor) return r.Name;
            return "Player " + actor;
        }

        /// <summary>The other players' birds, where the website draws them (play-area coordinates).</summary>
        void DrawRemotes(float shift)
        {
            foreach (var p in Mp.Players.Values)
            {
                if (!Mp.Visible(p) || !Mp.MatchActive && Mp.State != Multiplayer.Phase.Results) continue;
                float x = Mp.ScreenX(p), y = p.DrawY;
                if (x < -40 || x > PW + 40) continue;
                float a = p.Dead ? 0.25f : 0.9f, size = 26;
                var hat = p.Hat != null ? Catalog.CosmeticById(p.Hat) : null;
                C.Circle(x, y, size * 0.62f, Col.Hex(p.Colour), 0.35f * a);
                var body = Art.Get(hat != null ? "bird-bare" : "bird");
                float rot = Math.Max(-0.30f, Math.Min(0.40f, p.SimVel * 0.035f));
                if (body != null) C.Sprite(body, x, y, size, size, rot, a);
                if (hat != null && !p.Dead) NativeRenderer.Hat(C, x, y, rot, size, hat, 0, Art);
                if (p.Dead) Icon("1f480", x, y, 18, 0.9f);
                string tag = Clip(p.Name, 14);
                float tw = C.Measure(tag, 13) + 14;
                C.RoundRect(x - tw / 2, y - 38, tw, 18, 9, Col.Hex(p.Colour), 0.85f * (p.Dead ? 0.5f : 1));
                C.Text(tag, x, y - 37, 13, White, p.Dead ? 0.6f : 1, Align.Centre);
            }
        }

        void OnlineRightColumn(float x, float y, float w)
        {
            Card(x, y, w, 64);
            C.Text("ROOM " + Mp.RoomCode, x + 16, y + 10, 20, Gold);
            C.Text(ModeName(Mp.Mode) + (Mp.Mode == "race" ? " - first to " + Mp.RaceTarget : "") + "   " + Mp.PingMs + " ms", x + 16, y + 38, 13, Muted);
            y += 76;
            var list = new List<Multiplayer.Player>(Sorted());
            float h = 40 + Math.Min(8, list.Count) * 26;
            Card(x, y, w, h);
            C.Text(Mp.Mode == "coinrush" ? "COINS" : "SCORES", x + 16, y + 10, 16, Muted);
            for (int i = 0; i < Math.Min(8, list.Count); i++)
            {
                var p = list[i];
                float ry = y + 34 + i * 26;
                C.Circle(x + 22, ry + 9, 6, Col.Hex(p.Colour));
                C.Text(Clip(p.Name, 16) + (p.Me ? " (you)" : ""), x + 36, ry + 1, 13, p.Dead ? Dim : White);
                C.Text(Score(p).ToString(CultureInfo.InvariantCulture), x + w - 16, ry, 16, p.Dead ? Dim : Gold, 1, Align.Right);
            }
            y += h + 12;
            float ch = 620 - y;
            if (ch > 80)
            {
                Card(x, y, w, ch);
                C.Text("CHAT", x + 16, y + 10, 16, Muted);
                ChatLines(x + 14, y + 34, w - 28, ch - 44, 13);
            }
        }
    }
}
