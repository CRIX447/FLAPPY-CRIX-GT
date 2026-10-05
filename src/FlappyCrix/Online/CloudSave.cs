// The in-game save <-> the website's account save (Firestore users/{uid}), with the site's
// field names and formats (flappycrix.html saveToCloud / applySave / mirrorStatsToFirestore):
// coinCount, highScore, gamesPlayed, owned[], equipped{hat,trail}, achievements[{id,unlocked,progress}],
// savedAt (ms; newer wins), and the stats.* mirror (level, xp, coins...). Daily rewards, calendar
// doors and settings stay on the PC, as on the site.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using FlappyCrix.Native;

namespace FlappyCrix.Online
{
    public static class CloudSave
    {
        public static long TotalXp(int level, int xp) => 50L * (level - 1) * level / 2 + xp;

        /// <summary>The fields the game writes (exactly these; the update mask follows them).</summary>
        public static Dictionary<string, object> Fields(SaveData s, Func<Catalog.Achievement, int> progress, string displayNameIfNew)
        {
            var owned = new List<object>();
            foreach (var id in s.Owned) owned.Add(id);
            var ach = new List<object>();
            int unlocked = 0;
            foreach (var a in Catalog.Achievements)
            {
                bool on = s.Achievements.Contains(a.Id);
                if (on) unlocked++;
                ach.Add(new Dictionary<string, object> { { "id", a.Id }, { "unlocked", on }, { "progress", Math.Min(a.Target, Math.Max(0, progress(a))) } });
            }
            long now = SaveData.NowMs();
            var f = new Dictionary<string, object>
            {
                { "coinCount", s.Coins },
                { "highScore", s.Best },
                { "gamesPlayed", s.Games },
                { "owned", owned },
                { "equipped", new Dictionary<string, object> { { "hat", s.Hat == "" ? null : s.Hat }, { "trail", s.Trail == "" ? null : s.Trail } } },
                { "achievements", ach },
                { "savedAt", s.SavedAt > 0 ? s.SavedAt : now },
                { "stats", new Dictionary<string, object>
                    {
                        { "level", s.Level }, { "xp", s.Xp }, { "coins", s.Coins }, { "highScore", s.Best }, { "games", s.Games },
                        { "achievements", unlocked }, { "achievementsTotal", Catalog.Achievements.Length }, { "updatedAt", now },
                    } },
                { "lastSeen", now },
            };
            if (displayNameIfNew != null) f["displayName"] = displayNameIfNew;
            return f;
        }

        /// <summary>True if the document is a real save (the site's doc can exist with only a Discord link or avatar).</summary>
        public static bool HasSave(Dictionary<string, object> doc) => doc != null && doc.ContainsKey("savedAt") && doc["savedAt"] != null;

        /// <summary>The website's save onto the game's (like the site's applySave). Settings, daily and calendar are kept.</summary>
        public static void Apply(Dictionary<string, object> doc, SaveData s)
        {
            s.Coins = Math.Max(0, Firestore.Long(doc, "coinCount"));
            s.Best = (int)Math.Max(0, Firestore.Long(doc, "highScore"));
            s.Games = (int)Math.Max(0, Firestore.Long(doc, "gamesPlayed"));
            s.SavedAt = Firestore.Long(doc, "savedAt");
            s.Owned = new HashSet<string>();
            var owned = Firestore.List(doc, "owned");
            if (owned != null)
                foreach (var o in owned)
                {
                    var id = o as string;
                    if (id == null) continue;
                    if (Catalog.CosmeticById(id) != null) s.Owned.Add(id);
                    else if (id.StartsWith("hat_", StringComparison.Ordinal) && Catalog.CosmeticById(id.Substring(4)) != null) s.Owned.Add(id.Substring(4));
                }
            var eq = Firestore.Map(doc, "equipped");
            string hat = Firestore.Str(eq, "hat"), trail = Firestore.Str(eq, "trail");
            s.Hat = hat != null && s.Owned.Contains(hat) ? hat : "";
            s.Trail = trail != null && s.Owned.Contains(trail) ? trail : "";
            s.Achievements = new HashSet<string>();
            var ach = Firestore.List(doc, "achievements");
            if (ach != null)
                foreach (var o in ach)
                {
                    var m = o as Dictionary<string, object>;
                    object un;
                    if (m != null && m.TryGetValue("unlocked", out un) && un is bool && (bool)un && Firestore.Str(m, "id") != null) s.Achievements.Add(Firestore.Str(m, "id"));
                }
            var st = Firestore.Map(doc, "stats");
            int lvl = (int)Firestore.Long(st, "level"), xp = (int)Firestore.Long(st, "xp");
            if (lvl >= 1) TakeLevelIfHigher(s, lvl, xp);
        }

        /// <summary>Level / XP: whichever has more total XP wins (the site's rule between the PC and PlayFab).</summary>
        public static void TakeLevelIfHigher(SaveData s, int level, int xp)
        {
            level = Math.Max(1, Math.Min(Catalog.MaxLevel, level));
            if (TotalXp(level, xp) > TotalXp(s.Level, s.Xp)) { s.Level = level; s.Xp = Math.Max(0, xp); }
        }
    }
}
