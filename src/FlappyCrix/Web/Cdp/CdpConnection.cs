// Chrome DevTools Protocol connection: numbered commands with optional result
// callbacks, and events. Thread-safe; callbacks/events run on the socket thread.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace FlappyCrix.Web.Cdp
{
    public sealed class CdpConnection : IDisposable
    {
        public delegate void ResultHandler(Dictionary<string, object> result, string error);

        /// <summary>(method, params) for every protocol event. Socket thread.</summary>
        public event Action<string, Dictionary<string, object>> OnEvent;
        public event Action<string> OnClosed;

        private readonly WebSocketClient ws = new WebSocketClient();
        private readonly ConcurrentDictionary<int, ResultHandler> pending = new ConcurrentDictionary<int, ResultHandler>();
        private int nextId;

        public bool IsOpen => ws.IsOpen;

        public void Connect(int port, string path, int timeoutMs)
        {
            ws.OnText += Handle;
            ws.OnClosed += why =>
            {
                foreach (var kv in pending)
                {
                    ResultHandler h;
                    if (pending.TryRemove(kv.Key, out h)) { try { h(null, "connection closed"); } catch { } }
                }
                OnClosed?.Invoke(why);
            };
            ws.Connect("127.0.0.1", port, path, timeoutMs);
        }

        public int Send(string method, Dictionary<string, object> args = null, ResultHandler onResult = null)
        {
            int id = Interlocked.Increment(ref nextId);
            if (onResult != null) pending[id] = onResult;
            string msg = "{\"id\":" + id + ",\"method\":" + MiniJson.Quote(method) +
                         (args != null ? ",\"params\":" + MiniJson.Serialize(args) : "") + "}";
            try { ws.SendText(msg); }
            catch (Exception e)
            {
                ResultHandler h;
                if (pending.TryRemove(id, out h)) h(null, e.Message);
            }
            return id;
        }

        /// <summary>Blocking call for setup (never on Unity's main thread).</summary>
        public Dictionary<string, object> Call(string method, Dictionary<string, object> args, int timeoutMs)
        {
            Dictionary<string, object> result = null;
            string error = null;
            using (var done = new ManualResetEvent(false))
            {
                Send(method, args, (r, e) => { result = r; error = e; try { done.Set(); } catch { } });
                if (!done.WaitOne(timeoutMs)) throw new TimeoutException(method + " timed out");
            }
            if (error != null) throw new InvalidOperationException(method + ": " + error);
            return result ?? new Dictionary<string, object>();
        }

        private void Handle(string text)
        {
            Dictionary<string, object> msg;
            try { msg = MiniJson.Obj(MiniJson.Parse(text)); }
            catch { return; }
            if (msg == null) return;

            object idObj;
            if (msg.TryGetValue("id", out idObj) && idObj is double)
            {
                ResultHandler h;
                if (pending.TryRemove((int)(double)idObj, out h))
                {
                    var err = MiniJson.Child(msg, "error");
                    h(MiniJson.Child(msg, "result"), err != null ? (MiniJson.Str(err, "message") ?? "error") : null);
                }
                return;
            }
            string method = MiniJson.Str(msg, "method");
            if (method != null) OnEvent?.Invoke(method, MiniJson.Child(msg, "params") ?? new Dictionary<string, object>());
        }

        public void Dispose() => ws.Dispose();
    }
}
