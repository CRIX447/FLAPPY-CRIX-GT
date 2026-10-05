// The in-game version of Flappy Crix: the website's game (NativeSim, drawn by NativeRenderer)
// with everything the site has offline - store, locker, daily rewards, the seasonal calendar,
// achievements, levels, settings, pause and game over - in the mod's own layout:
//
//   +-----------+-------------------+-----------+
//   | player    |                   | score /   |
//   | card,     |    the game       | controls, |
//   | power-ups,|    (400 x 600)    | messages  |
//   | season    |                   |           |
//   +-----------+-------------------+-----------+   960 x 640, one texture
//
// Left out on purpose (they need an account or don't work in VR): sign-in, multiplayer, voice
// and text chat. Nothing here opens the desktop browser.
// Every menu works with the arcade deck (joystick moves the highlight, SELECT chooses), the
// laser pointer, or the keyboard. SELECT never flaps.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Globalization;

namespace FlappyCrix.Native
{
    public sealed class FlappyApp
    {
        public const int W = 960, H = 640, PX = 280, PY = 20, PW = NativeSim.W, PH = NativeSim.H;
        public const string Version = "1.0.0";
        const float Tick = 1f / 60f;

        public readonly Canvas C = new Canvas(W, H);
        public readonly NativeSim Sim;
        public readonly SaveData Save;
        public readonly Assets Art;

        /// <summary>The wall clock (UTC). Replaceable for tests.</summary>
        public Func<DateTime> Clock = () => DateTime.UtcNow;
        /// <summary>FLAP on the main menu starts a run.</summary>
        public bool FlapStartsGame = true;
        /// <summary>False while the screen is hidden: no sounds, no fly-bys, nothing drawn.</summary>
        public bool Visible = true;

        /// <summary>A sound effect: file name in img/ (no .mp3) and volume 0..1 (settings already applied).</summary>
        public event Action<string, float> Sound;
        public event Action<string> Log;

        public Season Season { get; private set; }
        /// <summary>The music file (no .mp3) for the current season.</summary>
        public string MusicTrack => Season != null && Season.Music != null ? Season.Music : "music";
        /// <summary>Music plays on the menus and during a run; it stops for pause and game over.</summary>
        public bool MusicShouldPlay => Save.MusicOn && Save.MusicVol > 0 && (Sim.Screen == "menu" || Sim.Screen == "playing");
        public float MusicVolume => Save.MusicVol / 100f;
        /// <summary>Goes up by one each time a run starts (the music restarts from the top, as on the site).</summary>
        public int MusicRestarts { get; private set; }

        // ------------------------------------------------------------------ state

        string modal;                 // null, store, locker, daily, calendar, achievements, settings, help
        string storeTab = "powerups", lockerTab = "hats";
        double now, lastSave, seasonCheckedAt = -99, diedAt, popupUntil, flyerClockMs;
        float acc, alpha;
        bool newBest, has67, has1000, rankPlayed;
        string popup;                 // "67" / "1000"
        readonly FlyBy flyer = new FlyBy();
        Season.Day today;

        sealed class Toast { public string Icon, Title, Body; public double Born; public uint Col; }
        readonly List<Toast> toasts = new List<Toast>();

        // buttons laid out by the last frame, for the joystick, SELECT and the pointer
        sealed class Btn { public string Id; public float X, Y, W, H; public bool Enabled; public Action Press; }
        List<Btn> buttons = new List<Btn>(), building = new List<Btn>();
        string focus, screenKey, defaultFocus;

        public FlappyApp(SaveData save, Assets art, int seed = 0, Func<DateTime> clock = null)
        {
            if (clock != null) Clock = clock;
            Save = save;
            Art = art ?? new Assets();
            Sim = new NativeSim(seed);
            Sim.Sound += (n, v) => Sfx(n, v);
            Sim.CoinsGained += n => { Save.Coins += n; Save.Dirty = true; CheckAchievements(); };
            Sim.Scored += OnScored;
            Sim.Died += OnDied;
            Sim.PowerupGranted += (id, s) => Sfx("powerup", 1f);
            RefreshSeason(true);
        }

        public string Screen => Sim.Screen;
        public string Modal => modal;
        public string Focus => focus;

        // ------------------------------------------------------------------ time

        /// <summary>Advances the game by dt real seconds.</summary>
        public void Update(float dt)
        {
            dt = Math.Max(0, Math.Min(dt, 0.25f));
            now += dt;
            if (now - seasonCheckedAt >= 5) RefreshSeason(false);

            Sim.TickRealTime(dt);
            if (Sim.Screen == "playing")
            {
                acc += dt;
                int steps = 0;
                while (acc >= Tick && steps < 5) { Sim.Step(); acc -= Tick; steps++; if (Sim.Screen != "playing") break; }
                if (steps >= 5) acc = 0;
            }
            alpha = Sim.Screen == "playing" ? Math.Min(1f, acc / Tick) : 1f;

            // the witch / Santa's sleigh (wall clock, like the site); not while hidden
            if (Visible)
            {
                flyerClockMs += dt * 1000;
                string kind = Season != null ? Season.Flyer : null;
                if (flyer.Update(kind, (float)flyerClockMs, dt * 1000, kind == "witch" ? Art.Get("witch") : kind == "santa" ? Art.Get("santasley") : null))
                    Sfx(kind == "witch" ? "witch1" : "santahohoho", 0.7f);
            }

            toasts.RemoveAll(t => now - t.Born > 4.0);
            if (Sim.Screen == "dead" && !rankPlayed && now - diedAt > 0.25)
            {
                rankPlayed = true;
                if (newBest) Sfx("rank", 0.6f);
            }
            if (Save.Dirty && now - lastSave > 3) Flush();
        }

        public void Flush() { lastSave = now; if (Save.Dirty) Save.Save(); }

        void RefreshSeason(bool force)
        {
            seasonCheckedAt = now;
            today = Season.SydneyToday(Clock());
            var s = Season.Pick(Save.SeasonChoice, today);
            if (s != Season || force)
            {
                Season = s;
                Changed();
                Log?.Invoke("Season: " + (s != null ? s.Name : "none"));
            }
            Sim.Halloween = Season != null && Season.Motif == "bat";
            Sim.ReducedMotion = Save.ReducedMotion;
        }

        /// <summary>For the settings screen and tests: pick the season now.</summary>
        public void SetSeasonChoice(string choice) { Save.SeasonChoice = choice; Save.Dirty = true; RefreshSeason(true); }

        // ------------------------------------------------------------------ game events

        void StartRun()
        {
            modal = null;
            Sim.StartRun();
            acc = 0; newBest = false; rankPlayed = false; popup = null;
            Save.Games++; Save.Dirty = true;
            MusicRestarts++;
            Sfx("select", 1f);
            if (Season.IsPartyHatDay(today) && !Save.Owned.Contains("hat_party"))
            {
                Save.Owned.Add("hat_party");
                AddToast("1f382", "GOLDEN PARTY HAT", "Happy birthday Flappy Crix! It's in your Locker.", Gold);
                Sfx("unlock", 1f);
            }
            CheckAchievements();
        }

        void OnScored(int score)
        {
            // XP: one per pipe; level n -> n+1 takes 50 x n
            if (Save.Level < Catalog.MaxLevel)
            {
                Save.Xp++;
                while (Save.Level < Catalog.MaxLevel && Save.Xp >= Catalog.XpForLevel(Save.Level))
                {
                    Save.Xp -= Catalog.XpForLevel(Save.Level);
                    Save.Level++;
                    AddToast("2b50", "LEVEL " + Save.Level, "Keep flying!", Gold);
                    Sfx("ach", 1f);
                }
                if (Save.Level >= Catalog.MaxLevel) Save.Xp = 0;
            }
            Save.Dirty = true;
            if (score == 67 && !has67) { has67 = true; popup = "67"; popupUntil = now + 3; Sfx("67", 1f); Unlock("mystery"); }
            if (score == 1000 && !has1000) { has1000 = true; popup = "1000"; popupUntil = now + 1.6; Sfx("boom", 1f); Sfx("victory", 1f); Unlock("legend"); }
            CheckAchievements(score);
        }

        void OnDied(int score)
        {
            diedAt = now;
            // the site only saves the best after a floor death; here every death counts
            if (score > Save.Best) { Save.Best = score; newBest = true; }
            Save.Dirty = true;
            CheckAchievements(score);
            Flush();
        }

        // ------------------------------------------------------------------ achievements

        public int AchievementProgress(Catalog.Achievement a)
        {
            switch (a.Id)
            {
                case "first": case "games25": case "games100": return Save.Games;
                case "collector": case "coins1k": case "coins10k": return (int)Math.Min(int.MaxValue, Save.Coins);
                case "score25": case "score50": case "score100": case "score250": return Save.Best;
                case "dripped": return Save.Hat != "" && Save.Trail != "" ? 1 : 0;
                case "lvl10": case "lvl25": case "lvlmax": return Save.Level;
                default: return Save.Achievements.Contains(a.Id) ? 1 : 0;
            }
        }

        void CheckAchievements(int runScore = 0)
        {
            foreach (var a in Catalog.Achievements)
            {
                if (Save.Achievements.Contains(a.Id)) continue;
                int p = AchievementProgress(a);
                if (a.Id.StartsWith("score", StringComparison.Ordinal)) p = Math.Max(p, runScore);
                if (a.Id != "mystery" && a.Id != "legend" && p >= a.Target) Unlock(a.Id);
            }
        }

        void Unlock(string id)
        {
            if (!Save.Achievements.Add(id)) return;
            Save.Dirty = true;
            foreach (var a in Catalog.Achievements)
                if (a.Id == id) AddToast(a.Emoji, "AWARD UNLOCKED", a.Name, Gold);
            Sfx("ach", 1f);
        }

        // ------------------------------------------------------------------ sounds and messages

        void Sfx(string name, float vol)
        {
            if (!Visible || !Save.SfxOn || Save.SfxVol <= 0) return;
            Sound?.Invoke(name, vol * Save.SfxVol / 100f);
        }

        /// <summary>Menu taps click only if "Menu click sounds" is on (the site's default is off).</summary>
        void Click() { if (Save.MenuClicks) Sfx("select", 1f); }

        void AddToast(string icon, string title, string body, uint col)
        {
            toasts.Add(new Toast { Icon = icon, Title = title, Body = body, Born = now, Col = col });
            Changed();
            while (toasts.Count > 4) toasts.RemoveAt(0);
        }

        void Refuse(string why) { Sfx("error", 1f); AddToast(null, "Can't do that", why, Red); }

        // ------------------------------------------------------------------ input (IFlappyGame)

        public void Flap()
        {
            if (Sim.Screen == "playing") { Sim.Flap(); return; }
            if (Sim.Screen == "menu" && modal == null && FlapStartsGame) { StartRun(); return; }
            if (Sim.Screen == "dead" && now - diedAt > 0.5) StartRun();       // the site: a tap after game over plays again
        }

        public void StartButton()
        {
            switch (Sim.Screen)
            {
                case "menu": StartRun(); break;
                case "dead": StartRun(); break;
                case "paused": Sim.Resume(); break;
            }
        }

        public void TogglePause()
        {
            if (Sim.Screen == "playing") { Sim.Pause(); Flush(); }
            else if (Sim.Screen == "paused") Sim.Resume();
        }

        /// <summary>Close a menu / resume / back to the main menu.</summary>
        public void Back()
        {
            Changed();
            if (modal != null) { CloseModal(); return; }
            if (Sim.Screen == "paused") Sim.Resume();
            else if (Sim.Screen == "dead") ToMenu();
        }

        /// <summary>Joystick: dx -1/+1 = left/right, dy +1/-1 = up/down.</summary>
        public void Navigate(int dx, int dy)
        {
            if (buttons.Count == 0) return;
            var cur = buttons.Find(b => b.Id == focus);
            if (cur == null) { focus = (buttons.Find(b => b.Enabled) ?? buttons[0]).Id; return; }
            float cx = cur.X + cur.W / 2, cy = cur.Y + cur.H / 2;
            float vx = dx, vy = -dy;
            Btn best = null; float bestScore = float.MaxValue;
            foreach (var b in buttons)
            {
                if (b == cur) continue;
                float bx = b.X + b.W / 2 - cx, by = b.Y + b.H / 2 - cy;
                float along = bx * vx + by * vy;
                // overlapping rows/columns count as "in line"
                float across = Math.Abs(bx * vy - by * vx);
                if (along <= 4) continue;
                float score = along + across * 2.2f;
                if (score < bestScore) { bestScore = score; best = b; }
            }
            if (best != null) { focus = best.Id; Click(); Changed(); }
        }

        /// <summary>SELECT: presses the highlighted button. It never flaps.</summary>
        public void Select()
        {
            if (Sim.Screen == "playing") return;
            var b = buttons.Find(x => x.Id == focus);
            if (b == null) { if (buttons.Count > 0) focus = buttons[0].Id; return; }
            Press(b);
        }

        void Press(Btn b)
        {
            focus = b.Id;
            Changed();
            if (!b.Enabled) { Sfx("error", 1f); return; }
            Click();
            b.Press?.Invoke();
        }

        Btn HitButton(float u, float v)
        {
            float x = u * W, y = v * H;
            for (int i = buttons.Count - 1; i >= 0; i--)
            {
                var b = buttons[i];
                if (x >= b.X && x < b.X + b.W && y >= b.Y && y < b.Y + b.H) return b;
            }
            return null;
        }

        /// <summary>Pointer (laser / mouse) in 0..1, origin top-left.</summary>
        public void PointerMove(float u, float v)
        {
            var b = HitButton(u, v);
            if (b != null && b.Id != focus) { focus = b.Id; Changed(); }
        }

        public void PointerDown(float u, float v)
        {
            var b = HitButton(u, v);
            if (b != null) { Press(b); return; }
            float x = u * W, y = v * H;
            bool inGame = x >= PX && x < PX + PW && y >= PY && y < PY + PH;
            if (inGame && (Sim.Screen == "playing" || (Sim.Screen == "dead" && now - diedAt > 0.5))) Flap();
        }

        // ------------------------------------------------------------------ actions

        void Open(string m)
        {
            modal = m;
            Sfx("woosh", 0.5f);
        }

        void CloseModal() { modal = null; Sfx("woosh", 0.4f); }

        void ToMenu()
        {
            Sim.ToMenu();
            modal = null;
            Sfx("woosh", 0.5f);
            Flush();
        }

        void BuyPowerup(Catalog.Powerup p)
        {
            if (Sim.PowerupOn(p.Id)) { Refuse(p.Name + " is already on."); return; }
            if (Save.Coins < p.Price) { Refuse("You need " + Catalog.FormatCoins(p.Price - Save.Coins) + " more coins."); return; }
            Save.Coins -= p.Price; Save.Dirty = true;
            Sim.GrantPowerup(p.Id, p.Seconds);
            Sfx("purchase", 1f); Sfx("powerup", 1f);
            AddToast(p.Emoji, p.Name + " ON", p.Seconds + " seconds - the clock is running!", Green);
        }

        void BuyOrEquip(Catalog.Cosmetic c)
        {
            if (Save.Owned.Contains(c.Id)) { Equip(c); return; }
            if (c.Price <= 0) { Refuse(c.Source + " only."); return; }
            if (Save.Coins < c.Price) { Refuse("You need " + Catalog.FormatCoins(c.Price - Save.Coins) + " more coins."); return; }
            Save.Coins -= c.Price; Save.Owned.Add(c.Id); Save.Dirty = true;
            Sfx("purchase", 1f);
            AddToast(null, "BOUGHT " + c.Name, "Press it again to wear it.", Green);
            CheckAchievements();
        }

        void Equip(Catalog.Cosmetic c)
        {
            if (c.Type == "hat") Save.Hat = Save.Hat == c.Id ? "" : c.Id;
            else Save.Trail = Save.Trail == c.Id ? "" : c.Id;
            Save.Dirty = true;
            Sfx("select", 1f);
            CheckAchievements();
        }

        void Unequip(string type)
        {
            if (type == "hat") Save.Hat = ""; else Save.Trail = "";
            Save.Dirty = true;
            Sfx("select", 1f);
        }

        public bool DailyReady => Save.DailyLast != SaveData.DayNumber(LocalNow);
        DateTime LocalNow => Clock().ToLocalTime();

        int DailyStreakIfClaimedToday()
        {
            int day = SaveData.DayNumber(LocalNow);
            return Save.DailyLast == day - 1 ? Save.DailyStreak + 1 : 1;
        }

        void ClaimDaily()
        {
            if (!DailyReady) { Refuse("Come back tomorrow for the next reward."); return; }
            int streak = DailyStreakIfClaimedToday();
            int coins = Catalog.DailyRewards[(streak - 1) % 7];
            Save.DailyStreak = streak; Save.DailyLast = SaveData.DayNumber(LocalNow);
            Save.Coins += coins; Save.Dirty = true;
            Sfx("purchase", 1f);
            AddToast("1f381", "DAY " + streak + " REWARD", "+" + coins + " coins", Gold);
            CheckAchievements();
            Flush();
        }

        bool SeasonForced => !string.IsNullOrEmpty(Save.SeasonChoice) && Save.SeasonChoice != "auto";
        int CalendarDoorToday => Season != null ? Season.CalendarToday(today, SeasonForced) : 0;
        HashSet<int> CalendarClaimed => Save.CalendarDoors(Season.Id, LocalNow.Year);

        public bool CalendarReady
        {
            get
            {
                if (Season == null || Season.CalendarDoors == 0) return false;
                int open = CalendarDoorToday;
                var claimed = CalendarClaimed;
                for (int d = 1; d <= open; d++) if (!claimed.Contains(d)) return true;
                return false;
            }
        }

        void ClaimDoor(int door)
        {
            var claimed = CalendarClaimed;
            if (claimed.Contains(door)) { Refuse("Door " + door + " is already open."); return; }
            if (door > CalendarDoorToday) { Refuse("Door " + door + " opens on day " + door + "."); return; }
            int coins = Season.CalendarReward(door, Season.CalendarDoors);
            claimed.Add(door);
            Save.Coins += coins; Save.Dirty = true;
            string prize = Season.PrizeFor(door);
            var item = prize != null ? Catalog.CosmeticById(prize) : null;
            if (item != null && Save.Owned.Add(item.Id))
            {
                Sfx("unlock", 1f);
                AddToast(Season.Emoji, "DOOR " + door + ": " + item.Name, "+" + coins + " coins, and it's in your Locker!", Gold);
            }
            else
            {
                Sfx("purchase", 1f);
                AddToast(Season.Emoji, "DOOR " + door, "+" + coins + " coins", Gold);
            }
            CheckAchievements();
            Flush();
        }

        // ------------------------------------------------------------------ drawing

        static readonly uint White = Col.White, Muted = Col.Hex("#A9B6CC"), Dim = Col.Hex("#6B7890"), Gold = Col.Hex("#FFD54A"),
                             Green = Col.Hex("#5BE39A"), Red = Col.Hex("#FF5A6E"), Ink = Col.Hex("#0A0E18");
        uint Accent, Accent2, Panel, Panel2, Bg;

        void Theme()
        {
            if (Season != null) { Accent = Season.Accent; Accent2 = Season.Accent2; Panel = Season.Panel; Panel2 = Season.Panel2; }
            else { Accent = Col.Hex("#FF6B35"); Accent2 = Col.Hex("#00CC7A"); Panel = Col.Hex("#101B2D"); Panel2 = Col.Hex("#18273F"); }
            Bg = Col.Scale(Panel, 0.62f);
        }

        // Drawing is split into three columns so only what changed is drawn again: the game in
        // the middle every frame, the side columns when what they show changes, and the full-screen
        // menus only when something on them changes.
        const int LeftEnd = 272, RightStart = 688;
        uint[] bg; string bgKey, leftSig, rightSig, modalSig;
        bool lastModal;
        int uiVersion;

        void Changed() => uiVersion++;

        void BuildBackground()
        {
            C.OX = C.OY = 0; C.ResetClip();
            C.Clear(Bg);
            // a soft diagonal sheen so the background isn't flat
            C.Poly(new List<float> { 0, 0, 380, 0, 0, 380 }, Accent, 0.05f);
            C.Poly(new List<float> { W, H, W - 420, H, W, H - 420 }, Accent2, 0.05f);
            if (bg == null) bg = new uint[W * H];
            Array.Copy(C.Px, bg, bg.Length);
        }

        void Region(int x0, int x1)
        {
            C.OX = C.OY = 0;
            C.ClipX0 = x0; C.ClipX1 = x1; C.ClipY0 = 0; C.ClipY1 = H;
            C.CopyFrom(bg, x0, 0, x1 - x0, H);
        }

        string ToastKey()
        {
            var b = new System.Text.StringBuilder();
            foreach (var t in toasts)
            {
                double age = now - t.Born;
                b.Append(t.Title).Append(t.Body).Append(age < 0.25 || age > 3.55 ? ((int)(age * 30)).ToString(CultureInfo.InvariantCulture) : "s").Append(';');
            }
            return b.ToString();
        }

        string PowerupKey()
        {
            var b = new System.Text.StringBuilder();
            foreach (var kv in Sim.Powerups) b.Append(kv.Key).Append(Math.Ceiling(kv.Value)).Append(',');
            return b.ToString();
        }

        /// <summary>Draws the screen into C.Px (rows top-down). False = nothing changed since the last call.</summary>
        public bool Render()
        {
            Theme();
            string themeKey = Accent + "," + Accent2 + "," + Bg;
            if (bg == null || themeKey != bgKey) { BuildBackground(); bgKey = themeKey; leftSig = rightSig = modalSig = null; }

            string key = Sim.Screen + "/" + modal + "/" + (modal == "store" ? storeTab : modal == "locker" ? lockerTab : "");
            if (key != screenKey) { screenKey = key; focus = null; Changed(); }

            bool isModal = modal != null;
            if (isModal)
            {
                string sig = uiVersion + "|" + focus + "|" + Save.Coins + "|" + PowerupKey() + "|" + ToastKey() + "|" + DailyReady + "|" + today;
                if (lastModal && sig == modalSig) return false;
                modalSig = sig;
            }
            else if (lastModal) { leftSig = rightSig = null; }
            lastModal = isModal;

            building = new List<Btn>();
            defaultFocus = null;

            if (isModal)
            {
                Region(0, W);
                DrawGame();
                LeftColumn();
                RightColumn();
                C.Rect(0, 0, W, H, Ink, 0.55f);
                switch (modal)
                {
                    case "store": StoreScreen(); break;
                    case "locker": LockerScreen(); break;
                    case "daily": DailyScreen(); break;
                    case "calendar": CalendarScreen(); break;
                    case "achievements": AchievementsScreen(); break;
                    case "settings": SettingsScreen(); break;
                    case "help": HelpScreen(); break;
                }
                Toasts();
            }
            else
            {
                Region(LeftEnd, RightStart);
                DrawGame();
                C.ClipX0 = LeftEnd; C.ClipX1 = RightStart;
                if (Sim.Screen == "menu") MainMenu();
                else if (Sim.Screen == "paused") PauseCard();
                else if (Sim.Screen == "dead" && now - diedAt > 0.25) DeathCard();

                string ls = Save.Level + "," + Save.Xp + "," + Save.Coins + "," + Save.Best + "," + Save.Achievements.Count + "," + Save.Games + "," +
                            PowerupKey() + "," + (Season != null ? Season.Id : "-") + "," + SeasonForced + "," + today;
                if (ls != leftSig) { leftSig = ls; Region(0, LeftEnd); LeftColumn(); }

                string rs = Sim.Screen + "," + Sim.Score + "," + Save.Best + "," + Sim.RunCoins + "," + Sim.CoinMultiplier + "|" + ToastKey();
                if (rs != rightSig) { rightSig = rs; Region(RightStart, W); RightColumn(); Toasts(); }
            }
            C.OX = C.OY = 0; C.ResetClip();

            buttons = building;
            if (focus == null || buttons.Find(b => b.Id == focus) == null)
                focus = defaultFocus ?? (buttons.Count > 0 ? (buttons.Find(b => b.Enabled) ?? buttons[0]).Id : null);
            return true;
        }

        // ---- the game itself, in the middle

        void DrawGame()
        {
            // frame
            C.RoundRect(PX - 6, PY - 6, PW + 12, PH + 12, 14, Accent, 0.9f);
            C.RoundRect(PX - 3, PY - 3, PW + 6, PH + 6, 11, Ink);

            float shakeX = 0, shakeY = 0;
            if (Sim.ShakeLeft > 0 && !Save.ReducedMotion)
            {
                shakeX = (float)Math.Sin(now * 97) * Sim.ShakePx; shakeY = (float)Math.Cos(now * 83) * Sim.ShakePx * 0.6f;
            }
            int cx0 = C.ClipX0, cy0 = C.ClipY0, cx1 = C.ClipX1, cy1 = C.ClipY1;
            C.OX = PX + shakeX; C.OY = PY + shakeY;
            C.ClipX0 = PX; C.ClipY0 = PY; C.ClipX1 = PX + PW; C.ClipY1 = PY + PH;

            bool running = Sim.Screen == "playing";
            var scene = Scene.For(Season, Save.LightTheme);
            float lag = running ? 1 - alpha : 0;
            NativeRenderer.Backdrop(C, scene, Sim.BgOffset - NativeSim.SceneryStep * lag);
            NativeRenderer.Flyer(C, flyer, flyer.Kind == "witch" ? Art.Get("witch") : Art.Get("santasley"));

            float shift = NativeSim.Speed * lag;
            var coin = Art.Get("coin-still");
            foreach (var p in Sim.Pipes)
            {
                NativeRenderer.Pipe(C, p, p.X + shift, scene.Pipe);
                if (p.Web != 0) NativeRenderer.Webs(C, p, p.X + shift);
            }
            foreach (var p in Sim.Pumpkins) NativeRenderer.Pumpkin(C, p, p.X + (p.Smashed > 0 ? 0 : shift));
            foreach (var co in Sim.CoinList) NativeRenderer.Coin(C, co, co.X + shift, Sim.Frame, coin);
            foreach (var p in Sim.Pumpkins) NativeRenderer.PrizeText(C, p, p.X, coin);

            var hat = Catalog.CosmeticById(Save.Hat);
            var trail = Catalog.CosmeticById(Save.Trail);
            if (Sim.Screen != "menu")
            {
                NativeRenderer.Trail(C, Sim.Trail, shift, trail, Sim.Frame);
                float by = Sim.PrevBirdY + (Sim.BirdY - Sim.PrevBirdY) * alpha;
                NativeRenderer.Bird(C, by, Sim.BirdRotation, NativeSim.BirdSize, Art.Get(hat != null ? "bird-bare" : "bird"), hat, Sim.HatAngle, Art,
                                    Sim.PowerupOn("shield"), Sim.Frame);
                // the score, big, at the top of the play area
                if (Sim.Screen == "playing" || Sim.Screen == "paused")
                    C.Text(Sim.Score.ToString(CultureInfo.InvariantCulture), PW / 2f, 34, 48, White, 1, Align.Centre, true);
            }
            if (popup != null && now < popupUntil) Popup();

            C.OX = C.OY = 0; C.ClipX0 = cx0; C.ClipY0 = cy0; C.ClipX1 = cx1; C.ClipY1 = cy1;
        }

        void Popup()
        {
            float t = (float)(popupUntil - now);
            float a = Math.Min(1f, t * 2);
            if (popup == "67")
            {
                float pulse = 1 + 0.08f * (float)Math.Sin(now * 12);
                C.RoundRect(PW / 2f - 90 * pulse, 210, 180 * pulse, 110, 22, Accent2, 0.85f * a);
                C.Text("67", PW / 2f, 222, 48, White, a, Align.Centre, true);
                C.Text("MYSTERY LEVEL!", PW / 2f, 282, 20, Gold, a, Align.Centre, true);
            }
            else
            {
                Icon("1f4a5", PW / 2f, 240, 90, a);
                C.Text("1000!", PW / 2f, 292, 48, Gold, a, Align.Centre, true);
                C.Text("LEGEND", PW / 2f, 346, 26, White, a, Align.Centre, true);
            }
        }

        // ---- left: the player

        void LeftColumn()
        {
            float x = 16, w = 248, y = 20;
            // title
            C.Text("FLAPPY", x + 4, y - 2, 34, White, 1, Align.Left, true);
            C.Text("CRIX", x + 4 + C.Measure("FLAPPY ", 34), y - 2, 34, Accent, 1, Align.Left, true);
            C.Text("in Gorilla Tag  -  v" + Version, x + 6, y + 40, 13, Dim);
            y += 66;

            // level card
            Card(x, y, w, 86);
            Badge(x + 14, y + 14, 58, Save.Level.ToString(CultureInfo.InvariantCulture));
            C.Text("LEVEL " + Save.Level, x + 84, y + 14, 20, White);
            bool max = Save.Level >= Catalog.MaxLevel;
            int need = Catalog.XpForLevel(Save.Level);
            float f = max ? 1 : Math.Min(1f, Save.Xp / (float)need);
            C.RoundRect(x + 84, y + 44, w - 100, 12, 6, Ink, 0.8f);
            if (f > 0) C.RoundRect(x + 84, y + 44, Math.Max(12, (w - 100) * f), 12, 6, Gold);
            C.Text(max ? "MAX LEVEL" : Save.Xp + " / " + need + " XP", x + 84, y + 60, 13, Muted);
            y += 98;

            // stats
            Card(x, y, w, 162);
            Stat(x + 14, y + 10, "coin", "COINS", Catalog.FormatCoins(Save.Coins), Gold);
            Stat(x + 14, y + 47, "1f3c6", "BEST", Save.Best.ToString(CultureInfo.InvariantCulture), White);
            int got = 0; foreach (var a in Catalog.Achievements) if (Save.Achievements.Contains(a.Id)) got++;
            Stat(x + 14, y + 84, "1f3c5", "AWARDS", got + " / " + Catalog.Achievements.Length, White);
            Stat(x + 14, y + 121, "1f579", "GAMES", Save.Games.ToString(CultureInfo.InvariantCulture), White);
            y += 174;

            // power-ups that are running
            Card(x, y, w, 110);
            C.Text("POWER-UPS", x + 14, y + 10, 16, Muted);
            float py = y + 36;
            bool any = false;
            foreach (var p in Catalog.Powerups)
            {
                float left; if (!Sim.Powerups.TryGetValue(p.Id, out left) || left <= 0) continue;
                any = true;
                Icon(p.Emoji, x + 28, py + 13, 24);
                C.Text(p.Name, x + 48, py + 2, 16, White);
                C.Text(Math.Ceiling(left) + "s", x + w - 14, py + 2, 16, Green, 1, Align.Right);
                C.RoundRect(x + 48, py + 22, w - 62, 5, 2.5f, Ink, 0.8f);
                C.RoundRect(x + 48, py + 22, Math.Max(5, (w - 62) * Math.Min(1f, left / p.Seconds)), 5, 2.5f, Green);
                py += 32;
            }
            if (!any) C.TextWrapped("None on. Get them in the Store" + (Sim.Halloween ? ", or smash a pumpkin." : "."), x + 14, y + 38, w - 28, 13, Dim);
            y += 122;

            // season
            Card(x, y, w, 620 - y);
            if (Season != null)
            {
                Icon(Season.Emoji, x + 34, y + 32, 40);
                C.Text(Season.Name.ToUpperInvariant(), x + 62, y + 12, 20, Accent);
                C.Text(SeasonForced ? "Chosen in Settings" : "Seasonal theme is on", x + 62, y + 36, 13, Muted);
                if (Season.CalendarDoors > 0)
                {
                    int open = CalendarDoorToday, waiting = 0;
                    var claimed = CalendarClaimed;
                    for (int d = 1; d <= open; d++) if (!claimed.Contains(d)) waiting++;
                    string line = waiting > 0 ? waiting + (waiting == 1 ? " door" : " doors") + " to open!" : open > 0 ? "All open - next door tomorrow" : "Doors closed today";
                    C.Text(line, x + 14, y + 72, 16, waiting > 0 ? Gold : Muted);
                }
                else if (Season.Id == "birthday") C.Text("Play on 18 March: party hat!", x + 14, y + 72, 13, Gold);
            }
            else
            {
                Icon("1f319", x + 34, y + 32, 36);
                C.Text("NO EVENT", x + 62, y + 12, 20, White);
                C.Text("Next: " + NextSeasonText(), x + 62, y + 36, 13, Muted);
            }
        }

        string NextSeasonText()
        {
            var d = new DateTime(today.Y, today.M, today.D);
            for (int i = 1; i < 400; i++)
            {
                var t = d.AddDays(i);
                var s = Season.Auto(new Season.Day { Y = t.Year, M = t.Month, D = t.Day });
                if (s != null) return s.Name + ", " + t.ToString("d MMM", CultureInfo.InvariantCulture);
            }
            return "-";
        }

        void Stat(float x, float y, string icon, string label, string value, uint col)
        {
            Icon(icon, x + 13, y + 13, 26);
            C.Text(label, x + 34, y + 4, 13, Muted);
            C.Text(value, x + 220, y + 1, 20, col, 1, Align.Right);
        }

        void Badge(float x, float y, float s, string text)
        {
            C.Circle(x + s / 2, y + s / 2, s / 2, Accent);
            C.Circle(x + s / 2, y + s / 2, s / 2 - 4, Panel2);
            C.Text(text, x + s / 2, y + s / 2 - 14, 26, White, 1, Align.Centre);
        }

        // ---- right: score / controls / messages

        void RightColumn()
        {
            float x = 696, w = 248, y = 20;
            if (Sim.Screen == "playing" || Sim.Screen == "paused" || Sim.Screen == "dead")
            {
                Card(x, y, w, 150);
                C.Text("SCORE", x + 16, y + 12, 16, Muted);
                C.Text(Sim.Score.ToString(CultureInfo.InvariantCulture), x + 16, y + 30, 48, White, 1, Align.Left, true);
                C.Text("BEST " + Math.Max(Save.Best, Sim.Score), x + w - 16, y + 14, 16, Gold, 1, Align.Right);
                Icon("coin", x + 26, y + 116, 22);
                C.Text("+" + Sim.RunCoins + " this run", x + 44, y + 106, 16, Gold);
                if (Sim.CoinMultiplier > 1) C.Text("x2", x + w - 16, y + 106, 20, Green, 1, Align.Right);
                y += 162;
                // the next milestone
                Card(x, y, w, 70);
                int next = (Sim.Score / 10 + 1) * 10;
                C.Text("NEXT MILESTONE", x + 16, y + 10, 13, Muted);
                C.Text(next.ToString(CultureInfo.InvariantCulture), x + 16, y + 28, 26, Accent);
                float f = (Sim.Score % 10) / 10f;
                C.RoundRect(x + 90, y + 40, w - 106, 10, 5, Ink, 0.8f);
                if (f > 0) C.RoundRect(x + 90, y + 40, Math.Max(10, (w - 106) * f), 10, 5, Accent);
                y += 82;
            }
            Card(x, y, w, 236);
            C.Text("CONTROLS", x + 16, y + 10, 16, Muted);
            string[,] rows =
            {
                { "FLAP / A / X", Sim.Screen == "menu" ? "flap / play" : "flap" },
                { "JOYSTICK", "move" },
                { "SELECT", "choose" },
                { "START", "play / retry" },
                { "PAUSE", "pause" },
                { "TRIGGER", "click screen" },
                { "Y  /  B", "show / hide" },
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                float ry = y + 38 + i * 27;
                C.RoundRect(x + 14, ry, 104, 22, 6, Panel2);
                C.Text(rows[i, 0], x + 66, ry + 3, 13, White, 1, Align.Centre);
                C.Text(rows[i, 1], x + 126, ry + 3, 13, Muted);
            }
        }

        // ---- main menu, over the game

        void MainMenu()
        {
            float cx = PX + PW / 2f;
            // bird with its hat, bobbing
            float bob = Save.ReducedMotion ? 0 : (float)Math.Sin(now * 2.2) * 7;
            var hat = Catalog.CosmeticById(Save.Hat);
            DrawBird(cx, PY + 150 + bob, 92, (float)Math.Sin(now * 1.3) * 0.06f, hat);
            C.Text("FLAPPY CRIX", cx, PY + 28, 48, White, 1, Align.Centre, true);
            C.Text("tap  -  collect  -  drip", cx, PY + 84, 16, Gold, 1, Align.Centre, true);

            float y = PY + 222;
            Button("play", PX + 60, y, PW - 120, 60, "PLAY", "25b6", Style.Primary, true, StartRun, 34);
            defaultFocus = "play";
            y += 76;
            float bw = (PW - 120 - 12) / 2f, bh = 50;
            var items = new List<object[]>
            {
                new object[] { "daily", "DAILY", "1f381", (Action)(() => Open("daily")), DailyReady },
            };
            if (Season != null && Season.CalendarDoors > 0)
                items.Add(new object[] { "calendar", Season.Id == "spooky" ? "TREATS" : Season.Id == "christmas" ? "ADVENT" : "EGGS", Season.Emoji, (Action)(() => Open("calendar")), CalendarReady });
            items.Add(new object[] { "store", "STORE", "1f6d2", (Action)(() => { storeTab = "powerups"; Open("store"); }), false });
            items.Add(new object[] { "locker", "LOCKER", "1f9e2", (Action)(() => { lockerTab = "hats"; Open("locker"); }), false });
            items.Add(new object[] { "achievements", "AWARDS", "1f3c6", (Action)(() => Open("achievements")), false });
            items.Add(new object[] { "settings", "SETTINGS", "2699", (Action)(() => Open("settings")), false });
            items.Add(new object[] { "help", "HOW TO PLAY", "2753", (Action)(() => Open("help")), false });
            items.Add(new object[] { "music", Save.MusicOn ? "MUSIC ON" : "MUSIC OFF", Save.MusicOn ? "1f50a" : "1f507", (Action)(() => { Save.MusicOn = !Save.MusicOn; Save.Dirty = true; Sfx("pop", 0.4f); }), false });
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                float bx = PX + 60 + (i % 2) * (bw + 12), by = y + (i / 2) * (bh + 10);
                Button((string)it[0], bx, by, bw, bh, (string)it[1], (string)it[2], Style.Normal, true, (Action)it[3], 16);
                if ((bool)it[4]) Dot(bx + bw - 8, by + 8);
            }
        }

        void Dot(float x, float y)
        {
            C.Circle(x, y, 7, Ink); C.Circle(x, y, 5.5f, Red);
        }

        // ---- pause and game over cards, over the game

        void PauseCard()
        {
            C.Rect(PX, PY, PW, PH, Ink, 0.45f);
            float x = PX + 30, w = PW - 60, y = PY + 110;
            Card(x, y, w, 400, true);
            C.Text("PAUSED", x + w / 2, y + 16, 34, White, 1, Align.Centre, true);
            C.Text("Score " + Sim.Score + "   -   Coins " + Catalog.FormatCoins(Save.Coins), x + w / 2, y + 60, 16, Muted, 1, Align.Centre);
            C.Text("QUICK BUY", x + 20, y + 96, 13, Accent);
            float bw = (w - 40 - 16) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var p = Catalog.Powerups[i];
                bool ok = !Sim.PowerupOn(p.Id) && Save.Coins >= p.Price;
                var pp = p;
                Tile("buy_" + p.Id, x + 20 + i * (bw + 8), y + 116, bw, 96, ok, () => BuyPowerup(pp), (bx, by, bwid, bht) =>
                {
                    Icon(pp.Emoji, bx + bwid / 2, by + 26, 32);
                    C.Text(pp.Name, bx + bwid / 2, by + 46, 13, White, 1, Align.Centre);
                    PriceTag(bx + bwid / 2, by + 68, pp.Price, Sim.PowerupOn(pp.Id) ? "ON" : null);
                });
            }
            Button("resume", x + 20, y + 232, w - 40, 58, "RESUME", "25b6", Style.Primary, true, () => Sim.Resume(), 26);
            defaultFocus = "resume";
            Button("quit", x + 20, y + 302, w - 40, 50, "QUIT TO MENU", null, Style.Normal, true, ToMenu, 20);
        }

        void DeathCard()
        {
            C.Rect(PX, PY, PW, PH, Ink, 0.35f);
            float x = PX + 40, w = PW - 80, y = PY + 120;
            Card(x, y, w, 360, true);
            C.Text("GAME OVER", x + w / 2, y + 16, 34, White, 1, Align.Centre, true);
            float sy = y + 70;
            Row(x + 24, sy, w - 48, "SCORE", Sim.Score.ToString(CultureInfo.InvariantCulture), White);
            Row(x + 24, sy + 34, w - 48, "BEST", Math.Max(Save.Best, Sim.Score).ToString(CultureInfo.InvariantCulture), Gold);
            Row(x + 24, sy + 68, w - 48, "COINS", Catalog.FormatCoins(Save.Coins), Gold);
            if (newBest)
            {
                float pulse = Save.ReducedMotion ? 1 : 0.85f + 0.15f * (float)Math.Sin(now * 6);
                C.RoundRect(x + w / 2 - 92, sy + 106, 184, 32, 16, Accent2, pulse);
                C.Text("NEW BEST!", x + w / 2, sy + 110, 20, White, 1, Align.Centre);
            }
            Button("retry", x + 20, y + 230, w - 40, 56, "RETRY", "25b6", Style.Primary, true, StartRun, 26);
            defaultFocus = "retry";
            Button("menu", x + 20, y + 294, w - 40, 48, "MAIN MENU", null, Style.Normal, true, ToMenu, 20);
        }

        void Row(float x, float y, float w, string label, string value, uint col)
        {
            C.Text(label, x, y + 4, 16, Muted);
            C.Text(value, x + w, y, 20, col, 1, Align.Right);
        }

        // ---- full-screen menus

        const float MX = 24, MY = 20, MW = W - 48, MH = H - 40;

        void ModalFrame(string title, string icon, string subtitle = null)
        {
            Card(MX, MY, MW, MH, true);
            Icon(icon, MX + 44, MY + 38, 40);
            C.Text(title, MX + 74, MY + 14, 34, White, 1, Align.Left, true);
            if (subtitle != null) C.Text(subtitle, MX + 76, MY + 52, 13, Muted);
            // balance + back
            C.RoundRect(MX + MW - 330, MY + 18, 170, 40, 20, Panel2);
            Icon("coin", MX + MW - 306, MY + 38, 24);
            C.Text(Catalog.FormatCoins(Save.Coins), MX + MW - 170, MY + 26, 20, Gold, 1, Align.Right);
            Button("back", MX + MW - 150, MY + 16, 130, 44, "BACK", null, Style.Normal, true, CloseModal, 20);
        }

        void Tabs(string[] ids, string[] labels, string current, Action<string> pick)
        {
            float x = MX + 24, y = MY + 76;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                float w = C.Measure(labels[i], 16) + 40;
                Button("tab_" + id, x, y, w, 38, labels[i], null, id == current ? Style.Primary : Style.Normal, true, () => { pick(id); Sfx("pop", 0.35f); }, 16);
                x += w + 10;
            }
        }

        void StoreScreen()
        {
            ModalFrame("STORE", "1f6d2", "Spend the coins you collect");
            Tabs(new[] { "powerups", "cosmetics" }, new[] { "POWER-UPS", "COSMETICS" }, storeTab, t => storeTab = t);
            float top = MY + 132;
            if (storeTab == "powerups")
            {
                float cw = (MW - 48 - 32) / 3f;
                for (int i = 0; i < 3; i++)
                {
                    var p = Catalog.Powerups[i];
                    bool on = Sim.PowerupOn(p.Id);
                    var pp = p;
                    Tile("buy_" + p.Id, MX + 24 + i * (cw + 16), top + 10, cw, 300, !on && Save.Coins >= p.Price, () => BuyPowerup(pp), (x, y, w, h) =>
                    {
                        C.Circle(x + w / 2, y + 74, 52, Panel2);
                        Icon(pp.Emoji, x + w / 2, y + 74, 64);
                        C.Text(pp.Name, x + w / 2, y + 140, 26, White, 1, Align.Centre);
                        C.TextWrapped(pp.Desc, x + 20, y + 178, w - 40, 16, Muted, 1, Align.Centre);
                        float left; Sim.Powerups.TryGetValue(pp.Id, out left);
                        PriceTag(x + w / 2, y + 252, pp.Price, on ? "ON - " + Math.Ceiling(left) + "s" : null);
                    });
                    if (i == 0) defaultFocus = "buy_" + p.Id;
                }
                C.TextWrapped("Power-ups start counting down as soon as you buy them, like on the website. Pumpkins at Halloween can give you one for free.",
                              MX + 24, MY + MH - 70, MW - 48, 16, Dim, 1, Align.Centre);
            }
            else
            {
                var list = new List<Catalog.Cosmetic>();
                foreach (var c in Catalog.Cosmetics) if (c.Price > 0) list.Add(c);
                CosmeticGrid(list, top, 4, 210, true);
            }
        }

        void LockerScreen()
        {
            ModalFrame("LOCKER", "1f9e2", "Wear one hat and one trail");
            Tabs(new[] { "hats", "trails" }, new[] { "HATS", "TRAILS" }, lockerTab, t => lockerTab = t);
            var list = new List<Catalog.Cosmetic>(Catalog.OfType(lockerTab == "hats" ? "hat" : "trail"));
            CosmeticGrid(list, MY + 132, 6, 196, false);
        }

        /// <summary>Cards for hats and trails. store = show prices; else (Locker) a NONE card first and locked ones greyed.</summary>
        void CosmeticGrid(List<Catalog.Cosmetic> list, float top, int cols, float ch, bool store)
        {
            float gap = 12, cw = (MW - 48 - gap * (cols - 1)) / cols;
            int i = 0;
            if (!store)
            {
                string type = lockerTab == "hats" ? "hat" : "trail";
                bool none = (type == "hat" ? Save.Hat : Save.Trail) == "";
                Tile("none", MX + 24, top, cw, ch, true, () => Unequip(type), (x, y, w, h) =>
                {
                    DrawBird(x + w / 2, y + 70, 64, 0, null);
                    C.Text("NONE", x + w / 2, y + 122, 16, White, 1, Align.Centre);
                    Tag(x + w / 2, y + 152, none ? "WEARING" : "TAKE OFF", none ? Green : Muted);
                });
                defaultFocus = "none";
                i = 1;
            }
            foreach (var c in list)
            {
                var cc = c;
                bool owned = Save.Owned.Contains(c.Id);
                bool worn = Save.Hat == c.Id || Save.Trail == c.Id;
                float x0 = MX + 24 + (i % cols) * (cw + gap), y0 = top + (i / cols) * (ch + gap);
                bool enabled = owned || (store && Save.Coins >= c.Price);
                Tile("cos_" + c.Id, x0, y0, cw, ch, enabled, () => BuyOrEquip(cc), (x, y, w, h) =>
                {
                    if (cc.Type == "hat") DrawBird(x + w / 2, y + (store ? 80 : 74), store ? 76 : 64, 0, cc);
                    else TrailPreview(x + 14, y + (store ? 60 : 54), w - 28, cc);
                    float ty = y + h - (store ? 76 : 70);
                    int ns = C.Measure(cc.Name, 16) > w - 16 ? 13 : 16;
                    C.Text(cc.Name, x + w / 2, ty + (ns == 13 ? 2 : 0), ns, White, 1, Align.Centre);
                    if (worn) Tag(x + w / 2, ty + 34, "WEARING", Green);
                    else if (owned) Tag(x + w / 2, ty + 34, store ? "OWNED - WEAR" : "WEAR", Accent);
                    else if (store) PriceTag(x + w / 2, ty + 34, cc.Price, null);
                    else
                    {
                        C.RoundRect(x + 4, y + 4, w - 8, ty - y - 8, 10, Ink, 0.45f);
                        Icon("1f512", x + w / 2, y + (ty - y) / 2, 30);
                        string how = cc.Price > 0 ? "Store: " + cc.Price + " coins" : cc.Source;
                        C.Text(how, x + w / 2, ty + 34, C.Measure(how, 13) > w - 8 ? 13 : 13, Muted, 1, Align.Centre);
                    }
                });
                if (i == 0) defaultFocus = "cos_" + c.Id;
                i++;
            }
        }

        void DailyScreen()
        {
            ModalFrame("DAILY REWARD", "1f381", "Come back every day - the streak resets if you miss one");
            bool ready = DailyReady;
            int streak = ready ? DailyStreakIfClaimedToday() : Save.DailyStreak;
            int todayIdx = (Math.Max(1, streak) - 1) % 7;
            float cw = (MW - 48 - 6 * 12) / 7f, y = MY + 120;
            for (int d = 0; d < 7; d++)
            {
                float x = MX + 24 + d * (cw + 12);
                bool done = d < todayIdx || (d == todayIdx && !ready);
                bool isToday = d == todayIdx && ready;
                C.RoundRect(x, y, cw, 200, 14, isToday ? Accent : Panel2, isToday ? 1 : 0.95f);
                if (isToday) C.RoundRect(x + 3, y + 3, cw - 6, 194, 12, Panel2);
                C.Text("DAY " + (d + 1), x + cw / 2, y + 14, 16, isToday ? Accent : Muted, 1, Align.Centre);
                Icon(d == 6 ? "1f48e" : "coin", x + cw / 2, y + 86, d == 6 ? 56 : 44, done ? 0.45f : 1);
                C.Text("+" + Catalog.DailyRewards[d], x + cw / 2, y + 128, 26, done ? Dim : Gold, 1, Align.Centre);
                if (done) Icon("2705", x + cw / 2, y + 176, 28);
                else if (isToday) C.Text("TODAY", x + cw / 2, y + 166, 16, White, 1, Align.Centre);
            }
            C.Text("Streak: " + Save.DailyStreak + (Save.DailyStreak == 1 ? " day" : " days"), MX + MW / 2, y + 226, 20, White, 1, Align.Centre);
            Button("claim", MX + MW / 2 - 170, y + 270, 340, 70, ready ? "CLAIM +" + Catalog.DailyRewards[todayIdx] : "CLAIMED - SEE YOU TOMORROW",
                   ready ? "1f381" : null, ready ? Style.Primary : Style.Normal, ready, ClaimDaily, ready ? 26 : 16);
            defaultFocus = ready ? "claim" : "back";
            C.Text("Days change at midnight on this PC.", MX + MW / 2, MY + MH - 40, 13, Dim, 1, Align.Centre);
        }

        void CalendarScreen()
        {
            if (Season == null || Season.CalendarDoors == 0) { modal = null; return; }
            int open = CalendarDoorToday;
            var claimed = CalendarClaimed;
            ModalFrame(Season.CalendarTitle, Season.Emoji, open > 0 ? "A door opens every day (Sydney time). Missed doors can still be opened." : "The doors are closed today.");
            int doors = Season.CalendarDoors;
            int cols = doors > 24 ? 8 : doors > 7 ? 8 : 7;
            int rows = (doors + cols - 1) / cols;
            float gap = 10, top = MY + 90;
            float cw = (MW - 48 - gap * (cols - 1)) / cols, ch = Math.Min(118, (MH - 120 - gap * (rows - 1)) / rows);
            string firstOpen = null;
            for (int d = 1; d <= doors; d++)
            {
                int i = d - 1, dd = d;
                float x = MX + 24 + (i % cols) * (cw + gap), y = top + (i / cols) * (ch + gap);
                bool isClaimed = claimed.Contains(d), canOpen = !isClaimed && d <= open;
                string prize = Season.PrizeFor(d);
                if (canOpen && firstOpen == null) firstOpen = "door_" + d;
                Tile("door_" + d, x, y, cw, ch, canOpen, () => ClaimDoor(dd), (bx, by, bw, bh) =>
                {
                    if (canOpen) C.RoundRect(bx + 3, by + 3, bw - 6, bh - 6, 10, Accent, 0.25f);
                    C.Text(dd.ToString(CultureInfo.InvariantCulture), bx + 10, by + 6, 20, isClaimed ? Dim : White);
                    var item = prize != null ? Catalog.CosmeticById(prize) : null;
                    if (isClaimed) Icon("2705", bx + bw / 2, by + bh / 2 + 6, 30);
                    else if (dd > open) Icon("1f512", bx + bw / 2, by + bh / 2 + 6, 26, 0.6f);
                    else Icon(item != null ? "1f381" : Season.Emoji, bx + bw / 2, by + bh / 2 + 6, 34);
                    string reward = item != null ? item.Name : "+" + Season.CalendarReward(dd, Season.CalendarDoors);
                    C.Text(reward, bx + bw / 2, by + bh - 22, item != null && item.Name.Length > 11 ? 13 : 13, item != null ? Gold : Muted, 1, Align.Centre);
                    if (item != null) C.Circle(bx + bw - 12, by + 14, 5, Gold);
                });
            }
            defaultFocus = firstOpen ?? "back";
        }

        void AchievementsScreen()
        {
            int got = 0; foreach (var a in Catalog.Achievements) if (Save.Achievements.Contains(a.Id)) got++;
            ModalFrame("ACHIEVEMENTS", "1f3c6", got + " of " + Catalog.Achievements.Length + " unlocked");
            float gap = 10, cw = (MW - 48 - gap) / 2f, rh = 56, top = MY + 84;
            for (int i = 0; i < Catalog.Achievements.Length; i++)
            {
                var a = Catalog.Achievements[i];
                bool done = Save.Achievements.Contains(a.Id);
                float x = MX + 24 + (i % 2) * (cw + gap), y = top + (i / 2) * (rh + 8);
                C.RoundRect(x, y, cw, rh, 12, done ? Col.Lerp(Panel2, Gold, 0.12f) : Panel2);
                if (a.Emoji == "50") { C.Circle(x + 30, y + rh / 2, 18, Accent); C.Text("50", x + 30, y + rh / 2 - 11, 16, White, 1, Align.Centre); }
                else Icon(a.Emoji, x + 30, y + rh / 2, 32, done ? 1 : 0.4f);
                C.Text(a.Name, x + 58, y + 8, 16, done ? Gold : White);
                C.Text(a.Desc, x + 58, y + 30, 13, Muted);
                if (done) Icon("2705", x + cw - 26, y + rh / 2, 26);
                else if (a.Target > 1)
                {
                    int p = Math.Min(a.Target, AchievementProgress(a));
                    float bw = 110;
                    C.RoundRect(x + cw - bw - 16, y + 34, bw, 8, 4, Ink, 0.8f);
                    if (p > 0) C.RoundRect(x + cw - bw - 16, y + 34, Math.Max(8, bw * p / a.Target), 8, 4, Accent);
                    C.Text(Catalog.FormatCoins(p) + " / " + Catalog.FormatCoins(a.Target), x + cw - 16, y + 12, 13, Muted, 1, Align.Right);
                }
                else Icon("1f512", x + cw - 26, y + rh / 2, 20, 0.5f);
            }
            defaultFocus = "back";
        }

        static readonly string[] SeasonChoices = { "auto", "spooky", "christmas", "easter", "birthday", "off" };

        string SeasonChoiceName(string c)
        {
            if (c == "auto") { var s = Season.Auto(today); return "AUTOMATIC (" + (s != null ? s.Name.ToUpperInvariant() : "NONE TODAY") + ")"; }
            if (c == "off") return "OFF";
            var se = Season.ById(c); return se != null ? se.Name.ToUpperInvariant() : c;
        }

        void SettingsScreen()
        {
            ModalFrame("SETTINGS", "2699", "Saved on this PC");
            float x = MX + 24, w = (MW - 48 - 20) / 2f, y = MY + 92;

            // audio column
            SectionTitle(x, y, "AUDIO");
            SettingRow("music", x, y + 34, w, "Music", Save.MusicOn, v => { Save.MusicOn = v; });
            VolumeRow("musicvol", x, y + 92, w, "Music volume", () => Save.MusicVol, v => Save.MusicVol = v);
            SettingRow("sfx", x, y + 150, w, "Sound effects", Save.SfxOn, v => { Save.SfxOn = v; });
            VolumeRow("sfxvol", x, y + 208, w, "Effects volume", () => Save.SfxVol, v => { Save.SfxVol = v; Sfx("coin", 1f); });
            SettingRow("clicks", x, y + 266, w, "Menu click sounds", Save.MenuClicks, v => { Save.MenuClicks = v; });
            C.TextWrapped("Everything goes quiet while the screen is hidden (B), and comes back when you open it (Y).",
                          x, y + 330, w, 13, Dim);

            // game column
            float x2 = x + w + 20;
            SectionTitle(x2, y, "GAME");
            SettingRow("light", x2, y + 34, w, "Light theme (no event)", Save.LightTheme, v => { Save.LightTheme = v; });
            SettingRow("motion", x2, y + 92, w, "Reduced motion", Save.ReducedMotion, v => { Save.ReducedMotion = v; Sim.ReducedMotion = v; });
            C.RoundRect(x2, y + 150, w, 104, 12, Panel2);
            C.Text("Seasonal theme", x2 + 16, y + 160, 16, White);
            int idx = Math.Max(0, Array.IndexOf(SeasonChoices, Save.SeasonChoice));
            Button("season_prev", x2 + 12, y + 192, 50, 50, "<", null, Style.Normal, true, () => { SetSeasonChoice(SeasonChoices[(idx + SeasonChoices.Length - 1) % SeasonChoices.Length]); Sfx("pop", 0.4f); }, 26);
            Button("season_next", x2 + w - 62, y + 192, 50, 50, ">", null, Style.Normal, true, () => { SetSeasonChoice(SeasonChoices[(idx + 1) % SeasonChoices.Length]); Sfx("pop", 0.4f); }, 26);
            string name = SeasonChoiceName(Save.SeasonChoice);
            C.Text(name, x2 + w / 2, y + 206, name.Length > 22 ? 13 : 16, Accent, 1, Align.Centre);

            C.RoundRect(x2, y + 266, w, 130, 12, Panel2);
            C.Text("ABOUT", x2 + 16, y + 276, 16, Muted);
            C.TextWrapped("Flappy Crix for Gorilla Tag " + Version + ". The in-game version of crixgamingvr.com/flappycrix: the same game, coins, store, " +
                          "seasons and awards. Sign-in, multiplayer and chat aren't in VR. This mod was made with AI (Claude by Anthropic).",
                          x2 + 16, y + 300, w - 32, 13, Muted);
            defaultFocus = "music";
        }

        void SectionTitle(float x, float y, string t) => C.Text(t, x + 4, y, 16, Accent);

        void SettingRow(string id, float x, float y, float w, string label, bool value, Action<bool> set)
        {
            C.RoundRect(x, y, w, 50, 12, Panel2);
            C.Text(label, x + 16, y + 14, 16, White);
            Button(id, x + w - 120, y + 7, 108, 36, value ? "ON" : "OFF", null, value ? Style.On : Style.Normal, true,
                   () => { set(!value); Save.Dirty = true; Sfx("pop", 0.4f); }, 16);
        }

        void VolumeRow(string id, float x, float y, float w, string label, Func<int> get, Action<int> set)
        {
            C.RoundRect(x, y, w, 50, 12, Panel2);
            C.Text(label, x + 16, y + 14, 16, White);
            int v = get();
            Button(id + "_down", x + w - 190, y + 7, 44, 36, "-", null, Style.Normal, v > 0, () => { set(Math.Max(0, v - 10)); Save.Dirty = true; }, 20);
            C.Text(v + "%", x + w - 106, y + 14, 16, Gold, 1, Align.Centre);
            Button(id + "_up", x + w - 56, y + 7, 44, 36, "+", null, Style.Normal, v < 100, () => { set(Math.Min(100, v + 10)); Save.Dirty = true; }, 20);
        }

        void HelpScreen()
        {
            ModalFrame("HOW TO PLAY", "2753", "Same rules as the website");
            float x = MX + 40, w = MW - 80, y = MY + 96;
            string[,] steps =
            {
                { "25b6", "Flap through the gaps", "Press FLAP on the deck, A or X on your controllers, or pull the trigger while pointing at the game. Every pipe you pass is a point. The gaps get a little tighter as your score goes up." },
                { "coin", "Grab the coins", "Coins float between the pipes. Spend them in the Store on power-ups, hats and trails, and wear them from the Locker." },
                { "26a1", "Use power-ups", "2X COINS doubles every coin, SHIELD lets you bounce off pipes and the ground, MAGNET pulls coins in. They run on a real clock." },
                { "1f383", "Seasons", "Halloween (October), Christmas (1-26 December), Easter and the March birthday change the colours, music and sky, and bring a calendar with daily prizes and exclusive cosmetics." },
                { "1f3c6", "Level up", "Every pipe is 1 XP. Unlock 16 awards, claim the daily reward, and find the secret score..." },
            };
            for (int i = 0; i < steps.GetLength(0); i++)
            {
                float ry = y + i * 92;
                C.RoundRect(x, ry, w, 82, 14, Panel2);
                Icon(steps[i, 0], x + 42, ry + 41, 42);
                C.Text(steps[i, 1], x + 80, ry + 10, 20, White);
                C.TextWrapped(steps[i, 2], x + 80, ry + 38, w - 100, 13, Muted);
            }
            defaultFocus = "back";
        }

        // ---- messages

        void Toasts()
        {
            float x = 696, w = 248, y = H - 20;
            int first = 0;
            if (modal != null) { x = W / 2f - 170; w = 340; y = H - 26; first = toasts.Count - 1; }
            for (int i = toasts.Count - 1; i >= first && i >= 0; i--)
            {
                var t = toasts[i];
                float age = (float)(now - t.Born);
                float a = Math.Min(1f, Math.Min(age * 6, (4f - age) * 2.5f));
                if (a <= 0) continue;
                var lines = C.Wrap(t.Body ?? "", w - (t.Icon != null ? 70 : 28), 13);
                float h = 40 + lines.Count * 16;
                y -= h;
                float slide = Save.ReducedMotion ? 0 : (1 - Math.Min(1f, age * 6)) * 30;
                C.RoundRect(x + slide, y, w, h, 12, Ink, 0.92f * a);
                C.RoundRect(x + slide, y, 5, h, 2.5f, t.Col, a);
                float tx = x + slide + 16;
                if (t.Icon != null) { Icon(t.Icon, tx + 18, y + h / 2, 34, a); tx += 44; }
                C.Text(t.Title, tx, y + 8, 16, t.Col, a);
                for (int k = 0; k < lines.Count; k++) C.Text(lines[k], tx, y + 30 + k * 16, 13, Muted, a);
                y -= 8;
            }
        }

        // ------------------------------------------------------------------ widgets

        enum Style { Normal, Primary, On }

        void Card(float x, float y, float w, float h, bool strong = false)
        {
            C.RoundRect(x, y, w, h, 16, Panel, strong ? 0.98f : 0.92f);
            C.RoundRectStroke(x, y, w, h, 16, 1.5f, strong ? Accent : Col.Lerp(Panel, White, 0.12f), strong ? 0.8f : 1f);
        }

        void Register(string id, float x, float y, float w, float h, bool enabled, Action press)
        {
            building.Add(new Btn { Id = id, X = x, Y = y, W = w, H = h, Enabled = enabled, Press = press });
        }

        bool IsFocus(string id) => focus == id || (focus == null && defaultFocus == id);

        void FocusRing(float x, float y, float w, float h, float r)
        {
            C.RoundRectStroke(x - 4, y - 4, w + 8, h + 8, r + 4, 3, White);
        }

        void Button(string id, float x, float y, float w, float h, string label, string icon, Style st, bool enabled, Action press, int size)
        {
            Register(id, x, y, w, h, enabled, press);
            bool f = IsFocus(id);
            uint fill = st == Style.Primary ? Accent : st == Style.On ? Accent2 : Panel2;
            if (f && st == Style.Normal) fill = Col.Lerp(fill, White, 0.12f);
            float a = enabled ? 1 : 0.45f;
            C.RoundRect(x, y + 3, w, h, h / 3.2f, Ink, 0.35f * a);
            C.RoundRect(x, y, w, h, h / 3.2f, fill, a);
            if (st != Style.Normal) C.RoundRect(x + 3, y + 3, w - 6, h * 0.42f, h / 4, White, 0.10f * a);
            float iconW = icon != null ? size * 1.1f + 8 : 0;
            float tw = C.Measure(label, size) + iconW;
            float lx = x + (w - tw) / 2;
            if (icon != null) Icon(icon, lx + size * 0.55f, y + h / 2, size * 1.1f, a);
            C.Text(label, lx + iconW, y + h / 2 - TextHalf(size), size, White, a, Align.Left, st != Style.Normal);
            if (f) FocusRing(x, y, w, h, h / 3.2f);
        }

        /// <summary>A card that works as a button; draw fills in its contents.</summary>
        void Tile(string id, float x, float y, float w, float h, bool enabled, Action press, Action<float, float, float, float> draw)
        {
            Register(id, x, y, w, h, enabled, press);
            bool f = IsFocus(id);
            C.RoundRect(x, y, w, h, 14, f ? Col.Lerp(Panel2, White, 0.1f) : Panel2);
            draw(x, y, w, h);
            if (!enabled) C.RoundRect(x, y, w, h, 14, Ink, 0.25f);
            if (f) FocusRing(x, y, w, h, 14);
        }

        float TextHalf(int size) => C.Font.Get(size).LineHeight / 2f;

        void PriceTag(float cx, float y, int price, string instead)
        {
            if (instead != null) { Tag(cx, y, instead, Green); return; }
            string s = Catalog.FormatCoins(price);
            float w = C.Measure(s, 16) + 44;
            bool afford = Save.Coins >= price;
            C.RoundRect(cx - w / 2, y, w, 28, 14, Ink, 0.7f);
            Icon("coin", cx - w / 2 + 16, y + 14, 18);
            C.Text(s, cx - w / 2 + 30, y + 5, 16, afford ? Gold : Red);
        }

        void Tag(float cx, float y, string s, uint col)
        {
            float w = C.Measure(s, 13) + 24;
            C.RoundRect(cx - w / 2, y, w, 26, 13, col, 0.22f);
            C.Text(s, cx, y + 5, 13, col, 1, Align.Centre);
        }

        /// <summary>An emoji (by code), the coin, or nothing if the picture is missing.</summary>
        void Icon(string code, float cx, float cy, float size, float a = 1f)
        {
            if (code == null) return;
            var s = code == "coin" ? Art.Get("coin-still") : Art.Get("emoji/" + code);
            if (s == null)
            {
                if (code == "coin") { C.Circle(cx, cy, size * 0.45f, Gold, a); C.Circle(cx, cy, size * 0.3f, Col.Hex("#E0A800"), a); }
                return;
            }
            float k = size / Math.Max(s.W, s.H);
            C.Sprite(s, cx, cy, s.W * k, s.H * k, 0, a);
        }

        void DrawBird(float cx, float cy, float size, float rot, Catalog.Cosmetic hat)
        {
            var body = Art.Get(hat != null ? "bird-bare" : "bird");
            if (body != null) C.Sprite(body, cx, cy, size, size, rot);
            else C.Circle(cx, cy, size / 2, Accent);
            if (hat != null) NativeRenderer.Hat(C, cx, cy, rot, size, hat, 0, Art);
        }

        void TrailPreview(float x, float y, float w, Catalog.Cosmetic trail)
        {
            var pts = new List<float>();
            for (int i = 0; i <= 30; i++)
            {
                float t = i / 30f;
                pts.Add(x + t * w); pts.Add(y + (float)Math.Sin(t * 5 + 1.2f) * 14);
            }
            NativeRenderer.Trail(C, pts, 0, trail, 0);
        }

        // ------------------------------------------------------------------ for tests

        public void OpenForTest(string m, string tab = null)
        {
            modal = m;
            if (tab != null) { if (m == "store") storeTab = tab; else lockerTab = tab; }
        }

        public IEnumerable<string> ButtonIds { get { foreach (var b in buttons) yield return b.Id; } }
        public void PressForTest(string id) { var b = buttons.Find(x => x.Id == id); if (b != null) Press(b); }
        public void AdvanceClock(double seconds) => now += seconds;
    }
}
