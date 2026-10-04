// Downloads the website engine when the PC has no browser that can run hidden:
// Google's official "Chrome for Testing" headless shell - a build of Chromium made to be run
// by other programs, with no window at all. It comes straight from Google's servers
// (googlechromelabs.github.io lists the current version, storage.googleapis.com serves it);
// nothing else is accepted. One time, about 100 MB, into %LOCALAPPDATA%\FlappyCrix\engine.
// It is not shipped with the mod (see DEPENDENCIES.md).
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

namespace FlappyCrix.Web.Cdp
{
    public sealed class EngineDownloader
    {
        public const string VersionsJson = "https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json";
        public const string AllowedPrefix = "https://storage.googleapis.com/chrome-for-testing-public/";
        // Used only if the version list can't be read: the stable build when this mod was made.
        public const string FallbackVersion = "154.0.8037.92";

        // Overridable for tests (a local server standing in for Google's)
        public string JsonUrl = VersionsJson;
        public string UrlPrefix = AllowedPrefix;
        public string Platform = Environment.OSVersion.Platform == PlatformID.Win32NT ? "win64" : "linux64";

        public volatile bool Done;
        public volatile string Status = "Starting";
        public string ExePath { get; private set; }
        public string Error { get; private set; }
        public long BytesDone => Interlocked.Read(ref bytesDone);
        public long BytesTotal => Interlocked.Read(ref bytesTotal);
        public float Progress { get { long t = BytesTotal; return t > 0 ? Math.Min(1f, BytesDone / (float)t) : 0f; } }

        private long bytesDone, bytesTotal;
        private Action<string> log = _ => { };

        /// <summary>Downloads and unpacks on a background thread; Done is set when finished (ExePath or Error).</summary>
        public void StartAsync(string engineRoot, Action<string> logger)
        {
            if (logger != null) log = logger;
            var t = new Thread(() =>
            {
                try { ExePath = Run(engineRoot); }
                catch (Exception e) { Error = e.GetType().Name + ": " + e.Message; log("Engine download failed: " + Error); }
                finally { Done = true; }
            }) { IsBackground = true, Name = "FlappyCrix-EngineDownload" };
            t.Start();
        }

        public string Run(string engineRoot)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
            Directory.CreateDirectory(engineRoot);

            // 1. Which version, and where (only Google's Chrome for Testing storage is accepted)
            Status = "Finding the current version";
            string version = FallbackVersion, url = null;
            try
            {
                var root = MiniJson.Obj(MiniJson.Parse(GetString(JsonUrl)));
                var stable = MiniJson.Child(MiniJson.Child(root, "channels"), "Stable");
                version = MiniJson.Str(stable, "version") ?? FallbackVersion;
                var shells = MiniJson.Child(stable, "downloads");
                object list;
                if (shells != null && shells.TryGetValue("chrome-headless-shell", out list) && list is List<object>)
                    foreach (var o in (List<object>)list)
                    {
                        var d = MiniJson.Obj(o);
                        if (MiniJson.Str(d, "platform") == Platform) url = MiniJson.Str(d, "url");
                    }
            }
            catch (Exception e) { log("Couldn't read the engine version list (" + e.Message + "); using " + FallbackVersion + "."); }
            if (url == null) url = UrlPrefix + version + "/" + Platform + "/chrome-headless-shell-" + Platform + ".zip";
            if (!url.StartsWith(UrlPrefix, StringComparison.Ordinal) || !url.EndsWith("/chrome-headless-shell-" + Platform + ".zip", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to download the engine from an unexpected address: " + url);
            foreach (char c in version) if (!char.IsDigit(c) && c != '.') throw new InvalidOperationException("Unexpected engine version: " + version);

            string dir = Path.Combine(engineRoot, version);
            string exe = FindExe(dir);
            if (exe != null) { Status = "Ready"; return exe; }

            // 2. Download (to a .part file, renamed when complete)
            string zip = Path.Combine(engineRoot, version + ".zip");
            string part = zip + ".part";
            log("Downloading the website engine (Chrome for Testing headless shell " + version + ", one time): " + url);
            Status = "Downloading";
            Download(url, part);
            if (File.Exists(zip)) File.Delete(zip);
            File.Move(part, zip);

            // 3. Unpack
            Status = "Unpacking";
            string tmp = dir + ".unpacking";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            Unzip(zip, tmp);
            if (FindExe(tmp) == null) throw new InvalidOperationException("The download didn't contain chrome-headless-shell.");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.Move(tmp, dir);
            try { File.Delete(zip); } catch { }

            // 4. Older versions are no longer needed
            foreach (var old in Directory.GetDirectories(engineRoot))
                if (!string.Equals(old, dir, StringComparison.OrdinalIgnoreCase))
                    try { Directory.Delete(old, true); } catch { }

            exe = FindExe(dir);
            Status = "Ready";
            log("Website engine ready: " + exe);
            return exe;
        }

        private string FindExe(string dir)
        {
            if (!Directory.Exists(dir)) return null;
            string name = Platform == "win64" ? "chrome-headless-shell.exe" : "chrome-headless-shell";
            string p = Path.Combine(Path.Combine(dir, "chrome-headless-shell-" + Platform), name);
            return File.Exists(p) ? p : null;
        }

        private static string GetString(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 15000; req.ReadWriteTimeout = 15000;
            req.UserAgent = "FlappyCrix-GorillaTag-mod";
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream()))
                return r.ReadToEnd();
        }

        private void Download(string url, string path)
        {
            Exception first = null;
            try { DownloadWithDotNet(url, path); return; }
            catch (Exception e) { first = e; log("Download with .NET failed (" + e.Message + "); trying Windows' curl."); }
            // Windows 10/11 ship curl.exe; it uses Windows' own TLS.
            string curl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
            if (!File.Exists(curl)) curl = "curl";
            Interlocked.Exchange(ref bytesDone, 0);
            var p = Start(curl, "-L --fail --silent --show-error --retry 2 -o \"" + path + "\" \"" + url + "\"");
            while (!p.WaitForExit(250))
                try { if (File.Exists(path)) Interlocked.Exchange(ref bytesDone, new FileInfo(path).Length); } catch { }
            if (p.ExitCode != 0 || !File.Exists(path)) throw new IOException("Download failed (" + first.Message + "; curl exit " + p.ExitCode + ")");
        }

        private void DownloadWithDotNet(string url, string path)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 20000; req.ReadWriteTimeout = 30000;
            req.UserAgent = "FlappyCrix-GorillaTag-mod";
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                Interlocked.Exchange(ref bytesTotal, resp.ContentLength);
                using (var s = resp.GetResponseStream())
                using (var f = File.Create(path))
                {
                    var buf = new byte[1 << 16];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        f.Write(buf, 0, n);
                        Interlocked.Add(ref bytesDone, n);
                    }
                }
                long total = BytesTotal;
                if (total > 0 && new FileInfo(path).Length != total) throw new IOException("The download was cut short.");
            }
        }

        /// <summary>Windows 10/11's built-in tar (it reads zip files), else PowerShell; elsewhere unzip.</summary>
        private void Unzip(string zip, string into)
        {
            if (Platform != "win64") { Wait(Start("unzip", "-q -o \"" + zip + "\" -d \"" + into + "\""), "unzip"); return; }
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string tar = Path.Combine(sys, "tar.exe");
            if (File.Exists(tar))
            {
                try { Wait(Start(tar, "-xf \"" + zip + "\" -C \"" + into + "\""), "tar"); return; }
                catch (Exception e) { log("tar couldn't unpack the engine (" + e.Message + "); trying PowerShell."); }
            }
            string ps = Path.Combine(sys, @"WindowsPowerShell\v1.0\powershell.exe");
            Wait(Start(File.Exists(ps) ? ps : "powershell", "-NoProfile -NonInteractive -Command \"Expand-Archive -LiteralPath '" + zip.Replace("'", "''") +
                                                         "' -DestinationPath '" + into.Replace("'", "''") + "' -Force\""), "PowerShell");
        }

        private static Process Start(string exe, string args)
        {
            var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            if (p == null) throw new InvalidOperationException(exe + " did not start");
            return p;
        }

        private static void Wait(Process p, string what)
        {
            if (!p.WaitForExit(300000)) { try { p.Kill(); } catch { } throw new TimeoutException(what + " took too long"); }
            if (p.ExitCode != 0) throw new IOException(what + " exit code " + p.ExitCode);
        }
    }
}
