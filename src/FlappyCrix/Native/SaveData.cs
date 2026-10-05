// The player's progress and settings for the native game, kept like the site's guest save
// (coins, best, games played, owned/equipped cosmetics, achievements, level, daily streak,
// calendar doors, audio and theme settings). One text file: %LOCALAPPDATA%\FlappyCrix\save.txt.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FlappyCrix.Native
{
    public sealed class SaveData
    {
        public long Coins;
        public int Best, Games, Xp, Level = 1;
        public HashSet<string> Owned = new HashSet<string>();
        public string Hat = "", Trail = "";
        public HashSet<string> Achievements = new HashSet<string>();
        public int DailyLast, DailyStreak;
        public Dictionary<string, HashSet<int>> Calendar = new Dictionary<string, HashSet<int>>();
        public Dictionary<string, double> PowerupEnds = new Dictionary<string, double>();   // not saved

        // Settings (the site's defaults)
        public bool MusicOn = true, SfxOn = true, MenuClicks = false, LightTheme = false, ReducedMotion = false;
        public int MusicVol = 30, SfxVol = 100;
        public string SeasonChoice = "auto";

        public bool Dirty;
        private readonly string path;

        public SaveData(string path) { this.path = path; }

        public static SaveData Load(string path, Action<string> log = null)
        {
            var s = new SaveData(path);
            try
            {
                if (path != null && File.Exists(path))
                    foreach (var raw in File.ReadAllLines(path))
                    {
                        int eq = raw.IndexOf('=');
                        if (eq <= 0 || raw.StartsWith("#")) continue;
                        s.Set(raw.Substring(0, eq).Trim(), raw.Substring(eq + 1).Trim());
                    }
            }
            catch (Exception e) { log?.Invoke("Couldn't read the save (" + e.Message + "); starting fresh."); }
            if (s.Level < 1) s.Level = 1;
            return s;
        }

        private void Set(string k, string v)
        {
            int i; long l;
            switch (k)
            {
                case "coins": if (long.TryParse(v, out l)) Coins = Math.Max(0, l); break;
                case "best": if (int.TryParse(v, out i)) Best = Math.Max(0, i); break;
                case "games": if (int.TryParse(v, out i)) Games = i; break;
                case "xp": if (int.TryParse(v, out i)) Xp = i; break;
                case "level": if (int.TryParse(v, out i)) Level = Math.Max(1, Math.Min(Catalog.MaxLevel, i)); break;
                case "owned": Owned = Set(v); break;
                case "hat": Hat = v; break;
                case "trail": Trail = v; break;
                case "achievements": Achievements = Set(v); break;
                case "dailyLast": if (int.TryParse(v, out i)) DailyLast = i; break;
                case "dailyStreak": if (int.TryParse(v, out i)) DailyStreak = i; break;
                case "music": MusicOn = v == "1"; break;
                case "sfx": SfxOn = v == "1"; break;
                case "menuClicks": MenuClicks = v == "1"; break;
                case "light": LightTheme = v == "1"; break;
                case "reducedMotion": ReducedMotion = v == "1"; break;
                case "musicVol": if (int.TryParse(v, out i)) MusicVol = Math.Max(0, Math.Min(100, i)); break;
                case "sfxVol": if (int.TryParse(v, out i)) SfxVol = Math.Max(0, Math.Min(100, i)); break;
                case "season": SeasonChoice = v; break;
                default:
                    if (k.StartsWith("cal_", StringComparison.Ordinal))
                    {
                        var doors = new HashSet<int>();
                        foreach (var d in v.Split(',')) if (int.TryParse(d, out i)) doors.Add(i);
                        Calendar[k.Substring(4)] = doors;
                    }
                    break;
            }
        }

        private static HashSet<string> Set(string v)
        {
            var h = new HashSet<string>();
            foreach (var p in v.Split(',')) if (p.Trim().Length > 0) h.Add(p.Trim());
            return h;
        }

        public void Save()
        {
            Dirty = false;
            if (path == null) return;
            var b = new StringBuilder();
            b.AppendLine("# Flappy Crix for Gorilla Tag - your progress in the in-game version. Made with AI (Claude by Anthropic).");
            b.Append("coins=").Append(Coins).AppendLine();
            b.Append("best=").Append(Best).AppendLine();
            b.Append("games=").Append(Games).AppendLine();
            b.Append("xp=").Append(Xp).AppendLine();
            b.Append("level=").Append(Level).AppendLine();
            b.Append("owned=").Append(string.Join(",", new List<string>(Owned).ToArray())).AppendLine();
            b.Append("hat=").Append(Hat).AppendLine();
            b.Append("trail=").Append(Trail).AppendLine();
            b.Append("achievements=").Append(string.Join(",", new List<string>(Achievements).ToArray())).AppendLine();
            b.Append("dailyLast=").Append(DailyLast).AppendLine();
            b.Append("dailyStreak=").Append(DailyStreak).AppendLine();
            b.Append("music=").Append(MusicOn ? 1 : 0).AppendLine();
            b.Append("musicVol=").Append(MusicVol).AppendLine();
            b.Append("sfx=").Append(SfxOn ? 1 : 0).AppendLine();
            b.Append("sfxVol=").Append(SfxVol).AppendLine();
            b.Append("menuClicks=").Append(MenuClicks ? 1 : 0).AppendLine();
            b.Append("light=").Append(LightTheme ? 1 : 0).AppendLine();
            b.Append("reducedMotion=").Append(ReducedMotion ? 1 : 0).AppendLine();
            b.Append("season=").Append(SeasonChoice).AppendLine();
            foreach (var kv in Calendar)
            {
                var doors = new List<int>(kv.Value); doors.Sort();
                b.Append("cal_").Append(kv.Key).Append('=').Append(string.Join(",", doors.ConvertAll(d => d.ToString(CultureInfo.InvariantCulture)).ToArray())).AppendLine();
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, b.ToString());
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            }
            catch
            {
                try { File.WriteAllText(path, b.ToString()); } catch { }
            }
        }

        public HashSet<int> CalendarDoors(string season, int year)
        {
            string key = season + "_" + year;
            HashSet<int> h;
            if (!Calendar.TryGetValue(key, out h)) Calendar[key] = h = new HashSet<int>();
            return h;
        }

        /// <summary>The local day number the site uses for daily rewards.</summary>
        public static int DayNumber(DateTime local) => (int)(local.Date - new DateTime(1970, 1, 1)).TotalDays;
    }
}
