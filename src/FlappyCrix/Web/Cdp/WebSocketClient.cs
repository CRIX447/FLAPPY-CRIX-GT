// Minimal RFC 6455 WebSocket client (text messages) for the DevTools protocol on
// 127.0.0.1. Written directly on TcpClient so it behaves the same in Unity's Mono and
// outside Unity, sends no Origin header (Chromium only checks Origin when one is sent),
// and handles fragmented and large (~100 KB screencast) messages.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FlappyCrix.Web.Cdp
{
    public sealed class WebSocketClient : IDisposable
    {
        public event Action<string> OnText;
        public event Action<string> OnClosed;

        private TcpClient tcp;
        private Stream input;          // buffered, used only by the reader thread
        private NetworkStream output;  // unbuffered, writes serialised by sendLock
        private Thread reader;
        private readonly object sendLock = new object();
        private readonly RandomNumberGenerator rng = RandomNumberGenerator.Create();
        private volatile bool open;

        public bool IsOpen => open;

        public void Connect(string host, int port, string path, int timeoutMs)
        {
            tcp = new TcpClient { NoDelay = true, ReceiveBufferSize = 1 << 20, SendBufferSize = 1 << 16 };
            var ar = tcp.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) { tcp.Close(); throw new TimeoutException("WebSocket connect timed out"); }
            tcp.EndConnect(ar);
            // Separate read and write paths: a BufferedStream is not safe for a read on one
            // thread and a write on another at the same time.
            output = tcp.GetStream();
            input = new BufferedStream(output, 1 << 16);

            var keyBytes = new byte[16];
            rng.GetBytes(keyBytes);
            string key = Convert.ToBase64String(keyBytes);
            string req = "GET " + path + " HTTP/1.1\r\n" +
                         "Host: " + host + ":" + port + "\r\n" +
                         "Upgrade: websocket\r\n" +
                         "Connection: Upgrade\r\n" +
                         "Sec-WebSocket-Key: " + key + "\r\n" +
                         "Sec-WebSocket-Version: 13\r\n\r\n";
            byte[] rb = Encoding.ASCII.GetBytes(req);
            lock (sendLock) output.Write(rb, 0, rb.Length);

            tcp.ReceiveTimeout = timeoutMs;
            string status = ReadLine();
            if (status == null || status.IndexOf(" 101", StringComparison.Ordinal) < 0)
                throw new IOException("WebSocket handshake refused: " + status);
            while (true)
            {
                string line = ReadLine();
                if (line == null) throw new IOException("WebSocket handshake ended early");
                if (line.Length == 0) break;
            }
            tcp.ReceiveTimeout = 0;
            open = true;
            reader = new Thread(ReadLoop) { IsBackground = true, Name = "FlappyCrix-DevTools" };
            reader.Start();
        }

        private string ReadLine()
        {
            var sb = new StringBuilder();
            int b;
            while ((b = input.ReadByte()) != -1)
            {
                if (b == '\n') return sb.ToString().TrimEnd(new[] { '\r' });
                sb.Append((char)b);
                if (sb.Length > 8192) throw new IOException("Header line too long");
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        public void SendText(string text)
        {
            if (!open) throw new IOException("WebSocket is closed");
            SendFrame(0x1, Encoding.UTF8.GetBytes(text));
        }

        private void SendFrame(int opcode, byte[] payload)
        {
            var mask = new byte[4];
            lock (sendLock)
            {
                rng.GetBytes(mask);
                int len = payload.Length;
                int headerLen = 2 + (len < 126 ? 0 : len <= 0xFFFF ? 2 : 8) + 4;
                var frame = new byte[headerLen + len];
                int h = 0;
                frame[h++] = (byte)(0x80 | opcode);
                if (len < 126) frame[h++] = (byte)(0x80 | len);
                else if (len <= 0xFFFF) { frame[h++] = 0x80 | 126; frame[h++] = (byte)(len >> 8); frame[h++] = (byte)len; }
                else
                {
                    frame[h++] = 0x80 | 127;
                    long l = len;
                    for (int s = 56; s >= 0; s -= 8) frame[h++] = (byte)(l >> s);
                }
                Buffer.BlockCopy(mask, 0, frame, h, 4);
                h += 4;
                for (int i = 0; i < len; i++) frame[h + i] = (byte)(payload[i] ^ mask[i & 3]);
                output.Write(frame, 0, frame.Length);   // one write per frame
            }
        }

        private void ReadExact(byte[] buf, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = input.Read(buf, got, count - got);
                if (n <= 0) throw new EndOfStreamException();
                got += n;
            }
        }

        private void ReadLoop()
        {
            string why = "closed";
            var message = new MemoryStream();
            var hdr = new byte[8];
            try
            {
                while (open)
                {
                    ReadExact(hdr, 2);
                    bool fin = (hdr[0] & 0x80) != 0;
                    int opcode = hdr[0] & 0x0F;
                    bool masked = (hdr[1] & 0x80) != 0;
                    long len = hdr[1] & 0x7F;
                    if (len == 126) { ReadExact(hdr, 2); len = (hdr[0] << 8) | hdr[1]; }
                    else if (len == 127) { ReadExact(hdr, 8); len = 0; for (int i = 0; i < 8; i++) len = (len << 8) | hdr[i]; }
                    if (len > 64L * 1024 * 1024) throw new IOException("WebSocket message too large");
                    byte[] mk = null;
                    if (masked) { mk = new byte[4]; ReadExact(mk, 4); }
                    var payload = new byte[len];
                    ReadExact(payload, (int)len);
                    if (mk != null) for (int i = 0; i < payload.Length; i++) payload[i] ^= mk[i & 3];

                    switch (opcode)
                    {
                        case 0x0: // continuation
                        case 0x1: // text
                        case 0x2: // binary (not used by DevTools; treated as text)
                            message.Write(payload, 0, payload.Length);
                            if (fin)
                            {
                                string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                                message.SetLength(0);
                                try { OnText?.Invoke(text); } catch { /* handler errors must not kill the socket */ }
                            }
                            break;
                        case 0x8: // close
                            why = "closed by the browser";
                            try { SendFrame(0x8, new byte[0]); } catch { }
                            open = false;
                            break;
                        case 0x9: // ping
                            SendFrame(0xA, payload);
                            break;
                        case 0xA: // pong
                            break;
                    }
                }
            }
            catch (Exception e) { if (open) why = e.GetType().Name + ": " + e.Message; }
            open = false;
            try { OnClosed?.Invoke(why); } catch { }
        }

        public void Dispose()
        {
            if (open) { try { SendFrame(0x8, new byte[0]); } catch { } }
            open = false;
            try { tcp?.Close(); } catch { }
        }
    }
}
