// Finds every browser on the PC that can run the website hidden (headless) and be driven
// over the DevTools protocol: all Chromium-based browsers that support headless mode,
// wherever they are installed - plus the engine the mod downloaded itself, if any.
//
// Browsers that can't run hidden (Opera / Opera GX, Firefox, ...) are listed in the log but
// never started: they would open a visible window on the desktop. On a PC with only those,
// the mod downloads Google's Chrome for Testing headless shell instead (EngineDownloader).
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FlappyCrix.Web.Cdp
{
    public sealed class BrowserCandidate
    {
        public string Name;          // "Microsoft Edge", "Downloaded engine (Chrome for Testing 154...)"
        public string Exe;
        public bool Downloaded;      // the engine the mod downloaded itself
        public bool UserChosen;      // BrowserPath from the settings
        public override string ToString() => Name + " (" + Exe + ")";
    }

    public static class BrowserFinder
    {
        // Executables of Chromium browsers that support headless mode.
        static readonly string[] HeadlessCapable = { "msedge.exe", "chrome.exe", "brave.exe", "vivaldi.exe", "thorium.exe", "chromium.exe", "chrome-headless-shell.exe" };
        // Recognised but not usable hidden (they open a window instead).
        static readonly Dictionary<string, string> NotHeadless = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "opera.exe", "Opera / Opera GX" }, { "launcher.exe", "Opera / Opera GX" }, { "firefox.exe", "Firefox" },
            { "browser.exe", "Yandex Browser" }, { "waterfox.exe", "Waterfox" }, { "librewolf.exe", "LibreWolf" },
            { "floorp.exe", "Floorp" }, { "zen.exe", "Zen Browser" }, { "iexplore.exe", "Internet Explorer" },
        };

        // name, path relative to a Program Files / LocalAppData root
        static readonly string[,] Known =
        {
            { "Microsoft Edge",        @"Microsoft\Edge\Application\msedge.exe" },
            { "Microsoft Edge Beta",   @"Microsoft\Edge Beta\Application\msedge.exe" },
            { "Microsoft Edge Dev",    @"Microsoft\Edge Dev\Application\msedge.exe" },
            { "Microsoft Edge Canary", @"Microsoft\Edge SxS\Application\msedge.exe" },
            { "Google Chrome",         @"Google\Chrome\Application\chrome.exe" },
            { "Google Chrome Beta",    @"Google\Chrome Beta\Application\chrome.exe" },
            { "Google Chrome Dev",     @"Google\Chrome Dev\Application\chrome.exe" },
            { "Google Chrome Canary",  @"Google\Chrome SxS\Application\chrome.exe" },
            { "Brave",                 @"BraveSoftware\Brave-Browser\Application\brave.exe" },
            { "Brave Beta",            @"BraveSoftware\Brave-Browser-Beta\Application\brave.exe" },
            { "Brave Nightly",         @"BraveSoftware\Brave-Browser-Nightly\Application\brave.exe" },
            { "Chromium",              @"Chromium\Application\chrome.exe" },
            { "Vivaldi",               @"Vivaldi\Application\vivaldi.exe" },
            { "Thorium",               @"Thorium\Application\thorium.exe" },
            { "Supermium",             @"Supermium\chrome.exe" },
        };

        /// <summary>Folder the mod's own downloaded engine lives in (one sub-folder per version).</summary>
        public static string EngineRoot(string dataRoot) => Path.Combine(dataRoot, "engine");

        /// <summary>
        /// Every usable browser, best first: BrowserPath (if set), installed browsers, then the
        /// downloaded engine. `roots` overrides the Program Files / LocalAppData folders (tests).
        /// </summary>
        public static List<BrowserCandidate> FindAll(string overridePath, string dataRoot, Action<string> log, string[] roots = null)
        {
            log = log ?? (_ => { });
            var list = new List<BrowserCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<BrowserCandidate> add = c =>
            {
                if (string.IsNullOrEmpty(c.Exe)) return;
                string full;
                try { full = Path.GetFullPath(c.Exe); } catch { return; }
                if (!File.Exists(full) || !seen.Add(full)) return;
                c.Exe = full;
                list.Add(c);
            };

            if (!string.IsNullOrEmpty(overridePath))
            {
                if (File.Exists(overridePath))
                {
                    string n = Path.GetFileName(overridePath);
                    if (NotHeadless.ContainsKey(n)) log("BrowserPath is " + NotHeadless[n] + ", which can't run hidden - it may open a window. Trying it because you chose it.");
                    add(new BrowserCandidate { Name = "BrowserPath", Exe = overridePath, UserChosen = true });
                }
                else log("BrowserPath '" + overridePath + "' not found; looking for other browsers.");
            }

            roots = roots ?? DefaultRoots();
            for (int i = 0; i < Known.GetLength(0); i++)
            {
                foreach (var root in roots) add(new BrowserCandidate { Name = Known[i, 0], Exe = Combine(root, Known[i, 1]) });
                // Edge's own engine copy: still there on many PCs where the Edge browser was removed
                if (i == 3)
                    foreach (var root in roots)
                    {
                        string core = Newest(Combine(root, @"Microsoft\EdgeCore"), "msedge.exe");
                        if (core != null) add(new BrowserCandidate { Name = "Microsoft Edge (EdgeCore)", Exe = core });
                    }
            }

            // Anything else Windows knows about: App Paths and the installed-browsers list
            foreach (var exe in HeadlessCapable)
                foreach (var p in RegistryAppPaths(exe)) add(new BrowserCandidate { Name = Path.GetFileNameWithoutExtension(exe), Exe = p });
            foreach (var kv in RegisteredBrowsers())
            {
                string file = Path.GetFileName(kv.Value);
                if (HeadlessCapable.Contains(file, StringComparer.OrdinalIgnoreCase))
                    add(new BrowserCandidate { Name = kv.Key, Exe = kv.Value });
            }

            // The engine the mod downloaded, newest version first
            string engine = DownloadedEngine(dataRoot);
            if (engine != null) add(new BrowserCandidate { Name = "Downloaded engine (Chrome for Testing " + Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(engine))) + ")", Exe = engine, Downloaded = true });

            // Report what can't be used, so the log explains why a download was needed
            var skipped = new List<string>();
            foreach (var kv in RegisteredBrowsers())
            {
                string file = Path.GetFileName(kv.Value);
                if (!HeadlessCapable.Contains(file, StringComparer.OrdinalIgnoreCase)) skipped.Add(kv.Key);
            }
            foreach (var root in roots)
                foreach (var opera in new[] { @"Programs\Opera GX\launcher.exe", @"Programs\Opera\launcher.exe", @"Opera GX\launcher.exe", @"Opera\launcher.exe" })
                    if (File.Exists(Combine(root, opera))) skipped.Add(opera.Contains("GX") ? "Opera GX" : "Opera");
            skipped = skipped.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (skipped.Count > 0) log("Also installed, but can't run hidden (so not used): " + string.Join(", ", skipped.ToArray()));

            return list;
        }

        // ------------------------------------------------------------------ browsers that didn't start

        static string FailuresFile(string dataRoot) => Path.Combine(dataRoot, "browsers-that-failed-v2.txt");
        static string Stamp(string exe) { try { return File.GetLastWriteTimeUtc(exe).Ticks.ToString(); } catch { return "0"; } }

        /// <summary>This exact browser (same file version on disk) failed to start before: skip it.</summary>
        public static bool FailedBefore(string dataRoot, string exe)
        {
            try
            {
                string f = FailuresFile(dataRoot);
                if (!File.Exists(f)) return false;
                string line = exe + "|" + Stamp(exe);
                return File.ReadAllLines(f).Any(l => string.Equals(l.Trim(), line, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        /// <summary>Remember that this browser couldn't start (tried again once it's updated).</summary>
        public static void RecordFailure(string dataRoot, string exe)
        {
            try
            {
                string f = FailuresFile(dataRoot);
                var lines = File.Exists(f) ? File.ReadAllLines(f).Where(l => !l.StartsWith(exe + "|", StringComparison.OrdinalIgnoreCase)).ToList() : new List<string>();
                lines.Add(exe + "|" + Stamp(exe));
                File.WriteAllLines(f, lines.ToArray());
            }
            catch { }
        }

        /// <summary>chrome-headless-shell.exe from the newest downloaded version, or null.</summary>
        public static string DownloadedEngine(string dataRoot)
        {
            if (string.IsNullOrEmpty(dataRoot)) return null;
            string root = EngineRoot(dataRoot);
            if (!Directory.Exists(root)) return null;
            foreach (var dir in Directory.GetDirectories(root).OrderByDescending(d => VersionKey(Path.GetFileName(d))))
            {
                foreach (var exe in new[] { @"chrome-headless-shell-win64\chrome-headless-shell.exe", "chrome-headless-shell-linux64/chrome-headless-shell" })
                {
                    string p = Combine(dir, exe);
                    if (File.Exists(p)) return p;
                }
            }
            return null;
        }

        static string[] DefaultRoots()
        {
            var r = new List<string>();
            foreach (var v in new[] { "ProgramFiles", "ProgramW6432", "ProgramFiles(x86)", "LOCALAPPDATA" })
            {
                string s = Environment.GetEnvironmentVariable(v);
                if (!string.IsNullOrEmpty(s) && !r.Contains(s, StringComparer.OrdinalIgnoreCase)) r.Add(s);
            }
            return r.ToArray();
        }

        /// <summary>Joins a relative Windows-style path onto a root, with this OS's separator.</summary>
        static string Combine(string root, string rel) =>
            Path.Combine(root, rel.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));

        /// <summary>exe inside the highest-version sub-folder of dir (EdgeCore\131.0.2903.112\msedge.exe).</summary>
        static string Newest(string dir, string exe)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;
                foreach (var d in Directory.GetDirectories(dir).OrderByDescending(x => VersionKey(Path.GetFileName(x))))
                {
                    string p = Path.Combine(d, exe);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        /// <summary>"131.0.2903.112" -> sortable key; anything else sorts last.</summary>
        public static string VersionKey(string name)
        {
            var parts = (name ?? "").Split(new[] { '.' });
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                int n;
                if (!int.TryParse(p, out n)) return "";
                sb.Append(n.ToString("D8"));
            }
            return sb.ToString();
        }

        static IEnumerable<string> RegistryAppPaths(string exe)
        {
            var found = new List<string>();
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return found;
            try
            {
                foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                    using (var k = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                    {
                        var v = k?.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(v)) found.Add(v.Trim(new[] { '"' }));
                    }
            }
            catch { /* registry not available */ }
            return found;
        }

        /// <summary>Windows' list of installed browsers (Default Apps): display name -> exe.</summary>
        static List<KeyValuePair<string, string>> RegisteredBrowsers()
        {
            var found = new List<KeyValuePair<string, string>>();
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return found;
            try
            {
                foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                    foreach (var basePath in new[] { @"SOFTWARE\Clients\StartMenuInternet", @"SOFTWARE\WOW6432Node\Clients\StartMenuInternet" })
                        using (var k = hive.OpenSubKey(basePath))
                        {
                            if (k == null) continue;
                            foreach (var name in k.GetSubKeyNames())
                                using (var cmd = k.OpenSubKey(name + @"\shell\open\command"))
                                {
                                    string exe = ExeFromCommand(cmd?.GetValue(null) as string);
                                    if (exe == null) continue;
                                    string display = null;
                                    using (var b = k.OpenSubKey(name)) display = b?.GetValue(null) as string;
                                    found.Add(new KeyValuePair<string, string>(string.IsNullOrEmpty(display) ? name : display, exe));
                                }
                        }
            }
            catch { /* registry not available */ }
            return found;
        }

        /// <summary>"\"C:\x\opera.exe\" --flag" or C:\x\chrome.exe -> the exe path.</summary>
        public static string ExeFromCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            cmd = cmd.Trim();
            if (cmd.StartsWith("\"")) { int e = cmd.IndexOf('"', 1); return e > 1 ? cmd.Substring(1, e - 1) : null; }
            int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe > 0 ? cmd.Substring(0, exe + 4) : null;
        }
    }
}
