// Flappy Crix for Gorilla Tag
// A tiny static-file HTTP server bound to 127.0.0.1 only.
//
// Why a server instead of file:// ?
//   flappycrix.html uses root-absolute paths everywhere ("/img/bird.png",
//   "/ui.js", and paths built at runtime like "/img/emoji/72/<code>.png").
//   Under file:// those resolve to the root of the drive and every asset
//   breaks. Serving the packaged folder as the web root keeps every path
//   working without editing the website.
//
// No UnityEngine references here on purpose: this file is also compiled into
// a console test harness and exercised against headless Chromium.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace FlappyCrix.Web
{
    public sealed class LocalWebServer : IDisposable
    {
        public const string BridgePath = "/__flappycrix/bridge.js";

        private readonly string root;
        private readonly string entryPage;
        private readonly string configJson;
        private readonly Action<string> log;
        private TcpListener listener;
        private Thread acceptThread;
        private volatile bool running;

        public int Port { get; private set; }
        public string BaseUrl => "http://127.0.0.1:" + Port;
        public string EntryUrl => BaseUrl + "/" + entryPage;

        /// <param name="root">Folder containing flappycrix.html and its assets.</param>
        /// <param name="entryPage">Page that receives the bridge script, e.g. "flappycrix.html".</param>
        /// <param name="configJson">JSON object exposed to bridge.js as window.__FLAPPYCRIX_CONFIG.</param>
        public LocalWebServer(string root, string entryPage, string configJson, Action<string> log)
        {
            this.root = Path.GetFullPath(root);
            this.entryPage = entryPage.TrimStart('/');
            this.configJson = string.IsNullOrEmpty(configJson) ? "{}" : configJson;
            this.log = log ?? (_ => { });
        }

        /// <summary>
        /// Starts on <paramref name="preferredPort"/>. The port is part of the page's origin, and
        /// the browser keeps the game's save (localStorage) per origin, so a fixed port keeps
        /// progress between sessions. Falls back to a random free port if it is taken.
        /// </summary>
        public void Start(int preferredPort)
        {
            if (!File.Exists(Path.Combine(root, entryPage)))
                throw new FileNotFoundException("Packaged website not found", Path.Combine(root, entryPage));

            try
            {
                listener = new TcpListener(IPAddress.Loopback, preferredPort);
                listener.Start();
            }
            catch (SocketException)
            {
                log("Port " + preferredPort + " is busy; using a random port. Saved progress is per-port, so it will look empty this session.");
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
            }

            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            running = true;
            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "FlappyCrix-WebServer" };
            acceptThread.Start();
            log("Serving " + root + " at " + BaseUrl);
        }

        private void AcceptLoop()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch { if (!running) return; continue; }
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        private void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 10000;
                    var stream = client.GetStream();
                    var headers = ReadHeaders(stream);
                    if (headers == null) return;

                    string[] requestLine = headers[0].Split(' ');
                    if (requestLine.Length < 2) { Simple(stream, 400, "Bad Request"); return; }
                    string method = requestLine[0];
                    if (method != "GET" && method != "HEAD") { Simple(stream, 405, "Method Not Allowed"); return; }

                    string rawPath = requestLine[1];
                    int q = rawPath.IndexOfAny(new[] { '?', '#' });
                    if (q >= 0) rawPath = rawPath.Substring(0, q);
                    string path = Uri.UnescapeDataString(rawPath);

                    string range = null;
                    for (int i = 1; i < headers.Count; i++)
                        if (headers[i].StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                            range = headers[i].Substring(6).Trim();

                    Serve(stream, method == "HEAD", path, range);
                }
                catch (IOException) { /* browser closed the connection - normal */ }
                catch (Exception e) { log("Web server error: " + e.Message); }
            }
        }

        private void Serve(Stream s, bool headOnly, string path, string range)
        {
            if (path == BridgePath)
            {
                string bridgeFile = Path.Combine(root, "__flappycrix", "bridge.js");
                if (!File.Exists(bridgeFile)) { Simple(s, 404, "bridge.js missing"); return; }
                Send(s, 200, "application/javascript; charset=utf-8", File.ReadAllBytes(bridgeFile), headOnly, null);
                return;
            }

            // Root and the site's pretty URL both go to the game.
            if (path == "/" || path.Equals("/flappycrix", StringComparison.OrdinalIgnoreCase))
                path = "/" + entryPage;

            string full = Resolve(path);
            if (full == null || !File.Exists(full))
            {
                // Pretty URLs like /privacy -> /privacy.html, as the live site does
                if (full != null && File.Exists(full + ".html")) full += ".html";
                else { Simple(s, 404, "Not Found"); return; }
            }

            string mime = Mime(full);

            if (Path.GetFileName(full).Equals(entryPage, StringComparison.OrdinalIgnoreCase))
            {
                Send(s, 200, mime, InjectBridge(File.ReadAllText(full, Encoding.UTF8)), headOnly, null);
                return;
            }

            byte[] data = File.ReadAllBytes(full);

            // Range support: Chromium uses it for <audio>/<video> and needs a 206 to seek.
            if (range != null && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                string spec = range.Substring(6).Split(',')[0].Trim();
                int dash = spec.IndexOf('-');
                long start, end;
                if (dash > 0)
                {
                    start = long.Parse(spec.Substring(0, dash));
                    end = dash < spec.Length - 1 ? long.Parse(spec.Substring(dash + 1)) : data.Length - 1;
                }
                else if (dash == 0)
                {
                    long suffix = long.Parse(spec.Substring(1));
                    start = Math.Max(0, data.Length - suffix);
                    end = data.Length - 1;
                }
                else { start = 0; end = data.Length - 1; }

                if (start >= data.Length || start > end)
                {
                    WriteHead(s, 416, mime, 0, "Content-Range: bytes */" + data.Length);
                    return;
                }
                end = Math.Min(end, data.Length - 1);
                int len = (int)(end - start + 1);
                byte[] part = new byte[len];
                Buffer.BlockCopy(data, (int)start, part, 0, len);
                Send(s, 206, mime, part, headOnly, "Content-Range: bytes " + start + "-" + end + "/" + data.Length);
                return;
            }

            Send(s, 200, mime, data, headOnly, null);
        }

        /// <summary>Maps a URL path to a file under root, refusing anything that escapes it.</summary>
        private string Resolve(string urlPath)
        {
            string rel = urlPath.Replace('\\', '/').TrimStart('/');
            if (rel.Length == 0) return null;
            foreach (var part in rel.Split('/'))
                if (part == "..") return null;
            string full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar.ToString()) ? root : root + Path.DirectorySeparatorChar;
            return full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        /// <summary>Puts the config and bridge.js first in &lt;head&gt;, before any of the site's scripts.</summary>
        private byte[] InjectBridge(string html)
        {
            string tag = "<script>window.__FLAPPYCRIX_CONFIG=" + configJson + ";</script>" +
                         "<script src=\"" + BridgePath + "\"></script>";
            int head = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
            int insertAt = head >= 0 ? html.IndexOf('>', head) + 1 : 0;
            return Encoding.UTF8.GetBytes(html.Insert(insertAt, tag));
        }

        private static List<string> ReadHeaders(Stream s)
        {
            var lines = new List<string>();
            var sb = new StringBuilder();
            int b, total = 0;
            while ((b = s.ReadByte()) != -1)
            {
                if (++total > 16384) return null;
                if (b == '\n')
                {
                    string line = sb.ToString().TrimEnd('\r');
                    sb.Length = 0;
                    if (line.Length == 0) return lines.Count > 0 ? lines : null;
                    lines.Add(line);
                }
                else sb.Append((char)b);
            }
            return lines.Count > 0 ? lines : null;
        }

        private static void Simple(Stream s, int code, string text)
        {
            Send(s, code, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text), false, null);
        }

        private static void Send(Stream s, int code, string mime, byte[] body, bool headOnly, string extraHeader)
        {
            WriteHead(s, code, mime, body.Length, extraHeader);
            if (!headOnly) s.Write(body, 0, body.Length);
            s.Flush();
        }

        private static void WriteHead(Stream s, int code, string mime, long length, string extraHeader)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(code).Append(' ').Append(Reason(code)).Append("\r\n");
            sb.Append("Content-Type: ").Append(mime).Append("\r\n");
            sb.Append("Content-Length: ").Append(length).Append("\r\n");
            sb.Append("Accept-Ranges: bytes\r\n");
            sb.Append("Cache-Control: no-cache\r\n");
            sb.Append("Connection: close\r\n");
            if (extraHeader != null) sb.Append(extraHeader).Append("\r\n");
            sb.Append("\r\n");
            byte[] h = Encoding.ASCII.GetBytes(sb.ToString());
            s.Write(h, 0, h.Length);
        }

        private static string Reason(int code)
        {
            switch (code)
            {
                case 200: return "OK";
                case 206: return "Partial Content";
                case 400: return "Bad Request";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 416: return "Range Not Satisfiable";
                default: return "Status";
            }
        }

        private static readonly Dictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" }, { ".htm", "text/html; charset=utf-8" },
            { ".js", "application/javascript; charset=utf-8" }, { ".mjs", "application/javascript; charset=utf-8" },
            { ".css", "text/css; charset=utf-8" }, { ".json", "application/json; charset=utf-8" },
            { ".png", "image/png" }, { ".gif", "image/gif" }, { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" },
            { ".webp", "image/webp" }, { ".avif", "image/avif" }, { ".svg", "image/svg+xml" }, { ".ico", "image/x-icon" },
            { ".mp3", "audio/mpeg" }, { ".ogg", "audio/ogg" }, { ".wav", "audio/wav" }, { ".m4a", "audio/mp4" },
            { ".mp4", "video/mp4" }, { ".webm", "video/webm" },
            { ".woff2", "font/woff2" }, { ".woff", "font/woff" }, { ".otf", "font/otf" }, { ".ttf", "font/ttf" },
            { ".txt", "text/plain; charset=utf-8" }, { ".wasm", "application/wasm" },
        };

        private static string Mime(string file)
        {
            string t;
            return Types.TryGetValue(Path.GetExtension(file), out t) ? t : "application/octet-stream";
        }

        public void Dispose()
        {
            running = false;
            try { listener?.Stop(); } catch { }
        }
    }
}
