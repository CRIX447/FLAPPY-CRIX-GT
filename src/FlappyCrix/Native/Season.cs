// The website's seasonal themes (flappycrix.html "SEASONS"): Halloween all of October,
// Christmas 1-26 December, Easter Palm Sunday to Easter Monday, Birthday in March from 2027
// (18 March always). Dates are read in Sydney time, exactly like the site.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;

namespace FlappyCrix.Native
{
    public sealed class Season
    {
        public string Id, Name, Music, Motif, CalendarTitle, Flyer;
        public uint SkyTop, SkyLow, City, Bush, Ground, GroundTop, Stripe, MotifFill;
        public float CityA, StripeA, MotifA;
        public uint[] Pipe;
        public uint Accent, Accent2, Panel, Panel2;
        public uint Moon; public float MoonA;
        public int CalendarDoors, CalendarMonth;          // month 1-12; 0 = Easter (moves)
        public int[] PrizeDoors = new int[0];
        public string[] PrizeItems = new string[0];

        public static readonly uint[] ConfettiColours = { Col.Hex("#FFC83D"), Col.Hex("#FF5FA2"), Col.Hex("#5BD2FF"), Col.Hex("#7CF07A"), Col.Hex("#C08BFF") };
        public static readonly uint[] EggColours = { Col.Hex("#FF9EC4"), Col.Hex("#9BE7FF"), Col.Hex("#FFE58A"), Col.Hex("#B8F2C8"), Col.Hex("#D7B3FF") };

        public static readonly Season Spooky = new Season
        {
            Id = "spooky", Name = "Halloween", Music = "halloweenmusic", Motif = "bat", Flyer = "witch",
            SkyTop = Col.Hex("#170A28"), SkyLow = Col.Hex("#3A1A4E"), City = Col.Rgb(58, 28, 78), CityA = .75f, Bush = Col.Hex("#2A1B3D"),
            Ground = Col.Hex("#241A2E"), GroundTop = Col.Hex("#1B1224"), Stripe = Col.Rgb(255, 140, 40), StripeA = .07f,
            Pipe = new[] { Col.Hex("#3A1F52"), Col.Hex("#7B3FA8"), Col.Hex("#B071E0"), Col.Hex("#7B3FA8"), Col.Hex("#2A1440") },
            MotifFill = Col.Rgb(20, 8, 32), MotifA = .85f,
            Accent = Col.Hex("#FF7A18"), Accent2 = Col.Hex("#8B3FD6"), Panel = Col.Hex("#1B0F2A"), Panel2 = Col.Hex("#241539"),
            Moon = Col.Rgb(255, 190, 90), MoonA = .9f,
            CalendarTitle = "TRICK OR TREAT", CalendarDoors = 31, CalendarMonth = 10,
            PrizeDoors = new[] { 7, 14, 21, 31 }, PrizeItems = new[] { "hat_witch", "hat_skull", "trail_ghost", "hat_pumpkin" },
        };

        public static readonly Season Christmas = new Season
        {
            Id = "christmas", Name = "Christmas", Music = "christmasmusic", Motif = "snow", Flyer = "santa",
            SkyTop = Col.Hex("#07121F"), SkyLow = Col.Hex("#123050"), City = Col.Rgb(30, 60, 100), CityA = .72f, Bush = Col.Hex("#14402A"),
            Ground = Col.Hex("#DCE6F2"), GroundTop = Col.Hex("#C2D2E6"), Stripe = Col.Rgb(255, 255, 255), StripeA = .35f,
            Pipe = new[] { Col.Hex("#5A0A16"), Col.Hex("#C8102E"), Col.Hex("#FF5566"), Col.Hex("#C8102E"), Col.Hex("#480810") },
            MotifFill = Col.Rgb(255, 255, 255), MotifA = .9f,
            Accent = Col.Hex("#E8323F"), Accent2 = Col.Hex("#2FA85B"), Panel = Col.Hex("#0C1A26"), Panel2 = Col.Hex("#122536"),
            Moon = Col.Rgb(226, 238, 255), MoonA = .85f,
            CalendarTitle = "ADVENT CALENDAR", CalendarDoors = 24, CalendarMonth = 12,
            PrizeDoors = new[] { 6, 12, 24 }, PrizeItems = new[] { "hat_reindeer", "trail_tinsel", "hat_santa" },
        };

        public static readonly Season Easter = new Season
        {
            Id = "easter", Name = "Easter", Music = null /* the site's eastermusic.mp3 doesn't exist, so it plays the normal track */, Motif = "egg",
            SkyTop = Col.Hex("#243C63"), SkyLow = Col.Hex("#5B7FB0"), City = Col.Rgb(120, 150, 200), CityA = .6f, Bush = Col.Hex("#3E7A4A"),
            Ground = Col.Hex("#C9B98A"), GroundTop = Col.Hex("#B3A375"), Stripe = Col.Rgb(255, 255, 255), StripeA = .16f,
            Pipe = new[] { Col.Hex("#3F6B2A"), Col.Hex("#7FC24A"), Col.Hex("#C4EE86"), Col.Hex("#7FC24A"), Col.Hex("#33571F") },
            Accent = Col.Hex("#FF8FB1"), Accent2 = Col.Hex("#8ED9C0"), Panel = Col.Hex("#1A2233"), Panel2 = Col.Hex("#232D42"),
            CalendarTitle = "EGG HUNT", CalendarDoors = 7, CalendarMonth = 0,
            PrizeDoors = new[] { 4, 7 }, PrizeItems = new[] { "hat_bunny", "trail_pastel" },
        };

        public static readonly Season Birthday = new Season
        {
            Id = "birthday", Name = "Birthday", Music = null, Motif = "confetti",
            SkyTop = Col.Hex("#1C1A4A"), SkyLow = Col.Hex("#5A3C8C"), City = Col.Rgb(90, 60, 140), CityA = .62f, Bush = Col.Hex("#2F5A3A"),
            Ground = Col.Hex("#DED895"), GroundTop = Col.Hex("#C9C177"), Stripe = Col.Rgb(255, 255, 255), StripeA = .18f,
            Pipe = new[] { Col.Hex("#2C7A1E"), Col.Hex("#5BC236"), Col.Hex("#A8F07A"), Col.Hex("#5BC236"), Col.Hex("#1F5A14") },
            Accent = Col.Hex("#FFC83D"), Accent2 = Col.Hex("#FF5FA2"), Panel = Col.Hex("#1E1430"), Panel2 = Col.Hex("#2A1B40"),
        };

        public static readonly Season[] All = { Spooky, Christmas, Easter, Birthday };

        public static Season ById(string id)
        {
            foreach (var s in All) if (s.Id == id) return s;
            return null;
        }

        public string Emoji => Id == "spooky" ? "1f383" : Id == "christmas" ? "1f384" : Id == "easter" ? "1f423" : "1f382";

        // ------------------------------------------------------------------ the calendar

        public struct Day { public int Y, M, D; public override string ToString() => Y + "-" + M + "-" + D; }

        private static TimeZoneInfo sydney;
        private static bool sydneyLooked;

        /// <summary>Today's date in Sydney (the site's clock), or local if the time zone isn't known.</summary>
        public static Day SydneyToday(DateTime utcNow)
        {
            if (!sydneyLooked)
            {
                sydneyLooked = true;
                foreach (var id in new[] { "AUS Eastern Standard Time", "Australia/Sydney" })
                    try { sydney = TimeZoneInfo.FindSystemTimeZoneById(id); break; } catch { }
            }
            DateTime t = sydney != null ? TimeZoneInfo.ConvertTimeFromUtc(utcNow, sydney) : utcNow.ToLocalTime();
            return new Day { Y = t.Year, M = t.Month, D = t.Day };
        }

        /// <summary>Anonymous Gregorian computus (the site's easterSunday).</summary>
        public static DateTime EasterSunday(int y)
        {
            int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
            int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
            int m = (a + 11 * h + 22 * l) / 451, month = (h + l - 7 * m + 114) / 31, day = ((h + l - 7 * m + 114) % 31) + 1;
            return new DateTime(y, month, day);
        }

        private static bool EasterWeek(Day t)
        {
            var e = EasterSunday(t.Y);
            var today = new DateTime(t.Y, t.M, t.D);
            return today >= e.AddDays(-7) && today <= e.AddDays(1);
        }

        /// <summary>computeAutoSeason: Birthday, then Halloween, then Christmas, then Easter.</summary>
        public static Season Auto(Day t)
        {
            const int Born = 2026;
            if (t.Y > Born && t.M == 3 && (t.D == 18 || !EasterWeek(t))) return Birthday;
            if (t.M == 10) return Spooky;
            if (t.M == 12 && t.D <= 26) return Christmas;
            if (EasterWeek(t)) return Easter;
            return null;
        }

        /// <summary>"auto" = the calendar, "off" = none, else a season id.</summary>
        public static Season Pick(string choice, Day today)
        {
            if (string.IsNullOrEmpty(choice) || choice == "auto") return Auto(today);
            if (choice == "off") return null;
            return ById(choice) ?? Auto(today);
        }

        public static bool IsPartyHatDay(Day t) => t.Y > 2026 && t.M == 3 && t.D == 18;

        /// <summary>Calendar door that is open today (0 = closed). Missed doors before it can still be claimed.</summary>
        public int CalendarToday(Day t, bool forced)
        {
            if (CalendarDoors == 0) return 0;
            if (CalendarMonth == 0)
            {
                // Easter: 7 doors ending on Easter Sunday
                var e = EasterSunday(t.Y);
                int door = (int)(new DateTime(t.Y, t.M, t.D) - e.AddDays(-6)).TotalDays + 1;
                if (door >= 1 && door <= CalendarDoors) return door;
                return forced ? CalendarDoors : 0;
            }
            if (t.M == CalendarMonth && t.D <= CalendarDoors) return t.D;
            return forced ? Math.Min(CalendarDoors, Math.Max(1, t.D)) : 0;
        }

        public static int CalendarReward(int door, int doors) => door == doors ? 750 : 60 + door * 12;

        public string PrizeFor(int door)
        {
            for (int i = 0; i < PrizeDoors.Length; i++) if (PrizeDoors[i] == door) return PrizeItems[i];
            return null;
        }
    }
}
