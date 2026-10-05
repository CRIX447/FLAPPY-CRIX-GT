// The website's chat filter (filter.js, built into the DLL): its word lists and its rules
// (leetspeak, repeated letters and separators collapse; short words only match whole), applied
// to every chat message shown in the game, as the website does for what it receives.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FlappyCrix.Online
{
    public sealed class ChatFilter
    {
        private readonly List<string> anywhere = new List<string>(), whole = new List<string>();
        private readonly HashSet<string> allow = new HashSet<string>();
        private readonly Dictionary<char, char> leet = new Dictionary<char, char>();
        private Regex anywhereRe, wholeRe;
        private static readonly Regex Separators = new Regex("[\\s._\\-*+~`'\"^,|/\\\\()\\[\\]{}<>:;!?]");
        private static ChatFilter embedded;

        public int WordCount => anywhere.Count + whole.Count;

        public static ChatFilter Embedded()
        {
            if (embedded != null) return embedded;
            using (var s = typeof(ChatFilter).Assembly.GetManifestResourceStream("FlappyCrix.filter.js"))
            {
                if (s == null) return embedded = new ChatFilter("");
                using (var r = new StreamReader(s, Encoding.UTF8)) return embedded = new ChatFilter(r.ReadToEnd());
            }
        }

        public ChatFilter(string js)
        {
            anywhere.AddRange(Strings(Block(js, "let ANYWHERE = [", "];")));
            whole.AddRange(Strings(Block(js, "let WHOLE_WORD = [", "];")));
            foreach (var w in Strings(Block(js, "const ALLOWLIST = new Set([", "]);"))) allow.Add(w.ToLowerInvariant());
            foreach (Match m in Regex.Matches(Block(js, "const LEET = {", "};"), "'(.)'\\s*:\\s*'(.)'"))
                leet[m.Groups[1].Value[0]] = m.Groups[2].Value[0];
            anywhereRe = Compile(anywhere, false);
            wholeRe = Compile(whole, true);
        }

        private static string Block(string js, string start, string end)
        {
            int a = js.IndexOf(start, StringComparison.Ordinal);
            if (a < 0) return "";
            int b = js.IndexOf(end, a + start.Length, StringComparison.Ordinal);
            return b < 0 ? "" : js.Substring(a + start.Length, b - a - start.Length);
        }

        private static IEnumerable<string> Strings(string block)
        {
            foreach (Match m in Regex.Matches(block, "'([^']*)'")) if (m.Groups[1].Value.Trim().Length > 0) yield return m.Groups[1].Value.Trim().ToLowerInvariant();
        }

        private static Regex Compile(List<string> words, bool wholeWord)
        {
            if (words.Count == 0) return null;
            const string sep = "[\\s._\\-*+~`'\"^,|/\\\\]{0,2}";
            var parts = new List<string>();
            foreach (var w in words)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < w.Length; i++) { if (i > 0) sb.Append(sep); sb.Append(Regex.Escape(w[i].ToString())); }
                parts.Add(sb.ToString());
            }
            string p = "(" + string.Join("|", parts.ToArray()) + ")";
            return new Regex(wholeWord ? "\\b" + p + "\\b" : p, RegexOptions.IgnoreCase);
        }

        private string Normalise(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s.ToLowerInvariant()) { char r; sb.Append(leet.TryGetValue(ch, out r) ? r : ch); }
            return Regex.Replace(sb.ToString(), "(.)\\1{2,}", "$1$1");
        }

        private string Squash(string s) => Regex.Replace(Separators.Replace(Normalise(s), ""), "(.)\\1+", "$1");

        private bool Allowed(string token) => allow.Contains(Separators.Replace(token.ToLowerInvariant(), "")) || allow.Contains(Squash(token));

        private static string Stars(int n) => new string('*', Math.Max(3, n));

        public string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var kept = new List<string>();
            string outText = Regex.Replace(text, "\\S+", m =>
            {
                string t = m.Value;
                if (Allowed(t)) { kept.Add(t); return t; }
                string sq = Squash(t);
                foreach (var w in anywhere) if (sq.Contains(w.Replace(" ", ""))) return Stars(t.Length);
                foreach (var w in whole) if (sq == w) return Stars(t.Length);
                return t;
            });
            // words split across spaces ("f u c k"); tokens judged fine above are left alone
            var stash = new List<string>();
            foreach (var t in kept)
            {
                int i = stash.Count; stash.Add(t);
                outText = new Regex("\\b" + Regex.Escape(t) + "\\b").Replace(outText, "\u0000" + i + "\u0000", 1);
            }
            if (anywhereRe != null) outText = anywhereRe.Replace(outText, m => Stars(m.Length));
            if (wholeRe != null) outText = wholeRe.Replace(outText, m => Allowed(m.Value) ? m.Value : Stars(m.Length));
            for (int i = 0; i < stash.Count; i++) outText = outText.Replace("\u0000" + i + "\u0000", stash[i]);
            return outText;
        }
    }
}
