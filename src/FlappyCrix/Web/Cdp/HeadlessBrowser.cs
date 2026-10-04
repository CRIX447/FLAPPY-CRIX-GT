// Starts a Chromium browser in headless mode: no window, nothing on the desktop, its own
// private profile. The browser is one already on the PC (Edge, Chrome, Brave, Vivaldi...,
// see BrowserFinder) or the engine the mod downloaded (EngineDownloader). The page is
// rendered off-screen and streamed into Gorilla Tag over the DevTools protocol.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FlappyCrix.Web.Cdp
{
    public sealed class HeadlessBrowser : IDisposable
    {
        public int Port { get; private set; }
        public string PageWebSocketPath { get; private set; }
        public string ExePath { get; private set; }
        public Process Process { get; private set; }

        private IntPtr job = IntPtr.Zero;
        private readonly Action<string> log;

        public HeadlessBrowser(Action<string> log) { this.log = log ?? (_ => { }); }

        // Which browser to start is decided by BrowserFinder (installed browsers that can run
        // hidden, or the engine the mod downloaded - EngineDownloader).

        // ------------------------------------------------------------------ launching

        public void Launch(string exe, string profileDir, int width, int height, bool mute, string extraArgs, int timeoutMs)
        {
            ExePath = exe;
            Directory.CreateDirectory(profileDir);
            KillStale(profileDir);

            string portFile = Path.Combine(profileDir, "DevToolsActivePort");
            try { File.Delete(portFile); } catch { }

            var args = new StringBuilder();
            args.Append("--headless=new ");
            args.Append("--remote-debugging-port=0 ");
            args.Append("--user-data-dir=\"").Append(profileDir).Append("\" ");
            args.Append("--no-first-run --no-default-browser-check --disable-extensions --disable-sync ");
            args.Append("--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows ");
            args.Append("--autoplay-policy=no-user-gesture-required ");
            args.Append("--hide-scrollbars ");
            // Even headless, the window reserves room for (invisible) toolbars, so it is made
            // taller than the page; the exact page size is then set by viewport emulation.
            args.Append("--window-size=").Append(width).Append(',').Append(height + 400).Append(' ');
            if (mute) args.Append("--mute-audio ");
            if (!string.IsNullOrEmpty(extraArgs)) args.Append(extraArgs).Append(' ');
            args.Append("about:blank");

            var psi = new ProcessStartInfo(exe, args.ToString())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };
            log("Starting hidden browser engine: " + exe);
            Process = Process.Start(psi);
            if (Process == null) throw new InvalidOperationException("The browser did not start.");
            TieToThisProcess(Process);
            try { File.WriteAllText(Path.Combine(profileDir, "flappycrix.pid"), Process.Id.ToString()); } catch { }

            // The browser writes "<port>\n<browser ws path>" once DevTools is listening.
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (Process.HasExited) throw new InvalidOperationException("The browser exited during start-up (code " + Process.ExitCode + ").");
                try
                {
                    if (File.Exists(portFile))
                    {
                        var lines = File.ReadAllLines(portFile);
                        int p;
                        if (lines.Length >= 1 && int.TryParse(lines[0].Trim(), out p) && p > 0) { Port = p; break; }
                    }
                }
                catch (IOException) { /* still being written */ }
                Thread.Sleep(50);
            }
            if (Port == 0) throw new TimeoutException("The browser's DevTools port did not appear within " + timeoutMs / 1000 + " s.");

            // Find the page target created for about:blank
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                string body = HttpGet(Port, "/json/list", 3000);
                var list = MiniJson.Parse(body) as List<object>;
                if (list != null)
                {
                    foreach (var o in list)
                    {
                        var t = MiniJson.Obj(o);
                        if (MiniJson.Str(t, "type") != "page") continue;
                        string ws = MiniJson.Str(t, "webSocketDebuggerUrl");
                        if (string.IsNullOrEmpty(ws)) continue;
                        PageWebSocketPath = new Uri(ws).PathAndQuery;
                        log("Browser engine ready on 127.0.0.1:" + Port + " after " + sw.ElapsedMilliseconds + " ms");
                        return;
                    }
                }
                Thread.Sleep(100);
            }
            throw new TimeoutException("The browser started but no page appeared.");
        }

        /// <summary>A browser left running by a crash would keep the profile locked; end it.</summary>
        private void KillStale(string profileDir)
        {
            string pidFile = Path.Combine(profileDir, "flappycrix.pid");
            try
            {
                if (!File.Exists(pidFile)) return;
                int pid;
                if (!int.TryParse(File.ReadAllText(pidFile).Trim(), out pid)) return;
                var p = Process.GetProcessById(pid);
                string name = p.ProcessName.ToLowerInvariant();
                if (name.Contains("msedge") || name.Contains("chrome") || name.Contains("brave") || name.Contains("chromium") ||
                    name.Contains("vivaldi") || name.Contains("thorium") || name.Contains("headless"))
                {
                    log("Closing a browser engine left over from last time (pid " + pid + ").");
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch { /* not running any more */ }
        }

        public static string HttpGet(int port, string path, int timeoutMs)
        {
            using (var c = new TcpClient())
            {
                var ar = c.BeginConnect("127.0.0.1", port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) throw new TimeoutException("DevTools HTTP connect timed out");
                c.EndConnect(ar);
                c.ReceiveTimeout = timeoutMs;
                var s = c.GetStream();
                byte[] req = Encoding.ASCII.GetBytes("GET " + path + " HTTP/1.1\r\nHost: 127.0.0.1:" + port + "\r\nConnection: close\r\n\r\n");
                s.Write(req, 0, req.Length);
                var ms = new MemoryStream();
                var buf = new byte[16384];
                int n;
                try { while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n); } catch (IOException) { }
                string resp = Encoding.UTF8.GetString(ms.ToArray());
                int body = resp.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                return body >= 0 ? resp.Substring(body + 4) : resp;
            }
        }

        // ------------------------------------------------------------------ lifetime

        /// <summary>
        /// Windows: put the browser in a job object that is closed when Gorilla Tag exits
        /// (even on a crash), so no hidden browser is ever left running.
        /// </summary>
        private void TieToThisProcess(Process p)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            try
            {
                job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) return;
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    SetInformationJobObject(job, 9 /* JobObjectExtendedLimitInformation */, ptr, (uint)size);
                }
                finally { Marshal.FreeHGlobal(ptr); }
                AssignProcessToJobObject(job, p.Handle);
            }
            catch (Exception e) { log("Could not tie the browser to the game process: " + e.Message); }
        }

        public void Dispose()
        {
            try { if (Process != null && !Process.HasExited) Process.Kill(); } catch { }
            if (job != IntPtr.Zero) { try { CloseHandle(job); } catch { } job = IntPtr.Zero; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll")]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

        [DllImport("kernel32.dll")]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
