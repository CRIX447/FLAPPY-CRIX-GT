// What the website sells and rewards, with the site's prices and rules (flappycrix.html):
// power-ups, hats and trails, achievements, daily rewards and account levels.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System.Collections.Generic;
using System.Globalization;

namespace FlappyCrix.Native
{
    public static class Catalog
    {
        public sealed class Powerup
        {
            public string Id, Name, Desc, Emoji; public int Price, Seconds;
        }

        public static readonly Powerup[] Powerups =
        {
            new Powerup { Id = "x2coins", Name = "2X COINS", Desc = "Double coins for 30 seconds", Emoji = "26a1", Price = 1200, Seconds = 30 },
            new Powerup { Id = "shield", Name = "SHIELD", Desc = "Pipes can't hurt you for 20 seconds", Emoji = "1f6e1", Price = 900, Seconds = 20 },
            new Powerup { Id = "magnet", Name = "MAGNET", Desc = "Pulls in nearby coins for 15 seconds", Emoji = "1f9f2", Price = 750, Seconds = 15 },
        };

        public static Powerup PowerupById(string id)
        {
            foreach (var p in Powerups) if (p.Id == id) return p;
            return null;
        }

        public sealed class Cosmetic
        {
            public string Id, Name, Type, Image, Shape, Fx, Source;   // Type: hat / trail
            public int Price;                                         // 0 = not for sale (event reward)
            public float FitW, FitY;                                  // image hats: width and bottom offset (bird sizes)
            public uint[] Colours;
        }

        static uint[] C(params string[] hex) { var a = new uint[hex.Length]; for (int i = 0; i < hex.Length; i++) a[i] = Col.Hex(hex[i]); return a; }

        public static readonly Cosmetic[] Cosmetics =
        {
            new Cosmetic { Id = "cap", Name = "BASEBALL CAP", Type = "hat", Price = 500, Image = "hat-cap", FitW = 0.7f, FitY = 0.16f },
            new Cosmetic { Id = "bucket", Name = "BUCKET HAT", Type = "hat", Price = 600, Image = "hat-bucket", FitW = 0.9f, FitY = 0.15f },
            new Cosmetic { Id = "trail_ember", Name = "EMBER TRAIL", Type = "trail", Price = 500, Fx = "fire", Colours = C("#FFE08A", "#FF9A3C", "#FF4655") },
            new Cosmetic { Id = "trail_frost", Name = "FROST TRAIL", Type = "trail", Price = 500, Fx = "frost", Colours = C("#FFFFFF", "#9BE7FF", "#27A9F5") },
            new Cosmetic { Id = "trail_toxic", Name = "TOXIC TRAIL", Type = "trail", Price = 600, Fx = "toxic", Colours = C("#D4FF3C", "#00CC7A", "#0B6B3A") },
            new Cosmetic { Id = "trail_rainbow", Name = "RAINBOW TRAIL", Type = "trail", Price = 800, Fx = "bands", Colours = C("#FF0040", "#FF8A00", "#FFE800", "#00E05A", "#00B7FF", "#9B4DFF") },
            new Cosmetic { Id = "trail_void", Name = "VOID TRAIL", Type = "trail", Price = 900, Fx = "void", Colours = C("#E0C3FF", "#9B4DFF", "#2A0A4A") },
            // Event rewards (not sold)
            new Cosmetic { Id = "crown", Name = "CHAMPION'S CROWN", Type = "hat", Image = "hat-crown", FitW = 0.62f, FitY = 0.06f, Source = "Event prize" },
            new Cosmetic { Id = "hat_witch", Name = "WITCH HAT", Type = "hat", Shape = "witch", Source = "Halloween: door 7" },
            new Cosmetic { Id = "hat_skull", Name = "SKELETON MASK", Type = "hat", Shape = "skull", Source = "Halloween: door 14" },
            new Cosmetic { Id = "trail_ghost", Name = "GHOST TRAIL", Type = "trail", Fx = "ghost", Colours = C("#FFFFFF", "#D7C9FF", "#7A5AA8"), Source = "Halloween: door 21" },
            new Cosmetic { Id = "hat_pumpkin", Name = "PUMPKIN HEAD", Type = "hat", Shape = "pumpkin", Source = "Halloween: door 31" },
            new Cosmetic { Id = "hat_reindeer", Name = "REINDEER EARS", Type = "hat", Shape = "reindeer", Source = "Christmas: door 6" },
            new Cosmetic { Id = "trail_tinsel", Name = "TINSEL TRAIL", Type = "trail", Fx = "tinsel", Colours = C("#FFF6C9", "#FFD700", "#2FA85B", "#E8323F"), Source = "Christmas: door 12" },
            new Cosmetic { Id = "hat_santa", Name = "SANTA'S HAT", Type = "hat", Shape = "santa", Source = "Christmas: door 24" },
            new Cosmetic { Id = "hat_bunny", Name = "BUNNY EARS", Type = "hat", Shape = "bunny", Source = "Easter: door 4" },
            new Cosmetic { Id = "trail_pastel", Name = "PASTEL TRAIL", Type = "trail", Fx = "bands", Colours = C("#FFD1E8", "#FFF3B0", "#B8F2C8", "#B9D9FF"), Source = "Easter: door 7" },
            new Cosmetic { Id = "hat_party", Name = "GOLDEN PARTY HAT", Type = "hat", Shape = "party", Source = "Play on 18 March" },
        };

        public static Cosmetic CosmeticById(string id)
        {
            foreach (var c in Cosmetics) if (c.Id == id) return c;
            return null;
        }

        public sealed class Achievement
        {
            public string Id, Name, Desc, Emoji; public int Target;
        }

        public static readonly Achievement[] Achievements =
        {
            new Achievement { Id = "first", Name = "FIRST FLIGHT", Desc = "Play your first game", Emoji = "1f423", Target = 1 },
            new Achievement { Id = "collector", Name = "COIN COLLECTOR", Desc = "Collect 100 coins", Emoji = "coin", Target = 100 },
            new Achievement { Id = "score25", Name = "GETTING GOING", Desc = "Reach a score of 25", Emoji = "1f3af", Target = 25 },
            new Achievement { Id = "score50", Name = "SCORE 50", Desc = "Reach a score of 50", Emoji = "50", Target = 50 },
            new Achievement { Id = "score100", Name = "SCORE 100", Desc = "Reach a score of 100", Emoji = "1f4af", Target = 100 },
            new Achievement { Id = "score250", Name = "IRON WINGS", Desc = "Reach a score of 250", Emoji = "1f4aa", Target = 250 },
            new Achievement { Id = "mystery", Name = "MYSTERY LEVEL", Desc = "Find the secret", Emoji = "2753", Target = 1 },
            new Achievement { Id = "legend", Name = "LEGEND", Desc = "Reach a score of 1,000", Emoji = "1f451", Target = 1 },
            new Achievement { Id = "coins1k", Name = "COIN HOARDER", Desc = "Collect 1,000 coins total", Emoji = "1f4b0", Target = 1000 },
            new Achievement { Id = "coins10k", Name = "RICH BIRD", Desc = "Collect 10,000 coins total", Emoji = "1f48e", Target = 10000 },
            new Achievement { Id = "games25", Name = "REGULAR", Desc = "Play 25 games", Emoji = "1f579", Target = 25 },
            new Achievement { Id = "games100", Name = "DEDICATED", Desc = "Play 100 games", Emoji = "1f3c5", Target = 100 },
            new Achievement { Id = "dripped", Name = "DRIPPED OUT", Desc = "Equip a hat and a trail at once", Emoji = "1f576", Target = 1 },
            new Achievement { Id = "lvl10", Name = "LEVEL 10", Desc = "Reach account level 10", Emoji = "2b50", Target = 10 },
            new Achievement { Id = "lvl25", Name = "LEVEL 25", Desc = "Reach account level 25", Emoji = "1f31f", Target = 25 },
            new Achievement { Id = "lvlmax", Name = "MAXED OUT", Desc = "Reach account level 50", Emoji = "1f451", Target = 50 },
        };

        /// <summary>Daily reward for streak day 1..7 (then it repeats).</summary>
        public static readonly int[] DailyRewards = { 100, 160, 220, 280, 340, 400, 600 };

        public const int MaxLevel = 50;
        /// <summary>XP needed to go from level n to n+1.</summary>
        public static int XpForLevel(int n) => 50 * n;

        /// <summary>The site's coin format: 999, 1.23K, 4.50M, 7.00B.</summary>
        public static string FormatCoins(long n)
        {
            if (n < 1000) return n.ToString(CultureInfo.InvariantCulture);
            var inv = CultureInfo.InvariantCulture;
            if (n < 1000000) return (n / 1000.0).ToString("0.00", inv) + "K";
            if (n < 1000000000) return (n / 1000000.0).ToString("0.00", inv) + "M";
            return (n / 1000000000.0).ToString("0.00", inv) + "B";
        }

        public static IEnumerable<Cosmetic> OfType(string type)
        {
            foreach (var c in Cosmetics) if (c.Type == type) yield return c;
        }
    }
}
