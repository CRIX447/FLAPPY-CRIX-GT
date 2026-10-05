// A Photon Realtime client that speaks exactly what the website's Photon JavaScript SDK
// (photon-realtime-browser.js, 4.4.0.0, pure-JS build) speaks: JSON over secure WebSockets,
// subprotocol "Json", frames "~m~<len>~m~~j~{json}", operations {"req":op,"vals":[k,v,...]}.
// Name server -> master server (lobby, room list) -> game server (the room), with the same
// app id, app version and region as the site, so it lands in the website's rooms.
// One worker thread owns all state; the game sends requests and takes events from queues.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using FlappyCrix.Web.Cdp;

namespace FlappyCrix.Online
{
    /// <summary>One WebSocket to one Photon server, with the JS SDK's framing, handshake and keep-alive.</summary>
    public sealed class PhotonPeer : IDisposable
    {
        public const string LibVersion = "4.4.0.0";
        public readonly string Url;
        public bool Connected { get; private set; }
        public bool Closed { get; private set; }
        public string CloseReason { get; private set; }

        private readonly WebSocketClient ws = new WebSocketClient { SubProtocol = "Json", Name = "FlappyCrix-Photon" };
        private readonly Action<PhotonPeer, Dictionary<string, object>> onMessage;
        private readonly Action<PhotonPeer> onConnect;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long lastSendMs;

        public PhotonPeer(string address, string appId, Action<PhotonPeer> onConnect, Action<PhotonPeer, Dictionary<string, object>> onMessage, string origin = null)
        {
            ws.Origin = origin;          // what a browser on the website sends
            if (!address.StartsWith("ws://", StringComparison.Ordinal) && !address.StartsWith("wss://", StringComparison.Ordinal)) address = "wss://" + address;
            Url = address.TrimEnd('/') + "/" + appId + "?libversion=" + LibVersion;
            this.onConnect = onConnect;
            this.onMessage = onMessage;
            ws.OnText += Receive;
            ws.OnClosed += why => { Closed = true; Connected = false; CloseReason = why; };
        }

        /// <summary>Blocking: TCP + TLS + WebSocket upgrade. The session starts when the server's first frame arrives.</summary>
        public void Open(int timeoutMs)
        {
            var u = new Uri(Url);
            ws.Tls = u.Scheme == "wss";
            ws.Connect(u.Host, u.Port, u.PathAndQuery, timeoutMs);
        }

        private void Receive(string text)
        {
            string payload = text.Replace("\0", "");
            if (payload.StartsWith("~m~", StringComparison.Ordinal))
            {
                int i = 3;
                while (i < payload.Length && char.IsDigit(payload[i])) i++;
                if (string.CompareOrdinal(payload, i, "~m~", 0, 3) == 0) payload = payload.Substring(i + 3);
            }
            if (payload.StartsWith("~j~", StringComparison.Ordinal))
            {
                Dictionary<string, object> msg;
                try { msg = MiniJson.Obj(MiniJson.Parse(payload.Substring(3))); } catch { return; }
                if (msg != null) onMessage(this, msg);
                return;
            }
            if (!Connected)
            {
                Connected = true;                 // the first plain frame is the session id
                Ping();
                onConnect(this);
            }
        }

        public void Send(Dictionary<string, object> msg)
        {
            if (!Connected || Closed) return;
            string p = "~j~" + MiniJson.Serialize(msg);
            try { ws.SendText("~m~" + p.Length + "~m~" + p); lastSendMs = clock.ElapsedMilliseconds; }
            catch (Exception e) { Closed = true; CloseReason = e.Message; }
        }

        public void Operation(int code, params object[] keysAndValues)
        {
            Send(new Dictionary<string, object> { { "req", code }, { "vals", new List<object>(keysAndValues) } });
        }

        public void Ping() => Send(new Dictionary<string, object> { { "irq", 1 }, { "vals", new List<object> { 1, clock.ElapsedMilliseconds } } });

        /// <summary>The SDK pings after 3 s without sending anything.</summary>
        public void KeepAlive() { if (Connected && clock.ElapsedMilliseconds - lastSendMs >= 3000) Ping(); }

        public void Dispose() { try { ws.Dispose(); } catch { } Closed = true; Connected = false; }
    }

    public sealed class PhotonClient : IDisposable
    {
        public enum State { Disconnected, ConnectingNameServer, ConnectingMaster, InLobby, JoiningGame, InRoom, Leaving, Error }

        public sealed class RoomInfo
        {
            public string Name;
            public Dictionary<string, object> Props = new Dictionary<string, object>();
            public int PlayerCount => (int)Num(Props, "252");
            public int MaxPlayers => (int)Num(Props, "255");
            public bool IsOpen => !(Props.ContainsKey("253") && Props["253"] is bool && !(bool)Props["253"]);
            public string Str(string k) { object v; return Props.TryGetValue(k, out v) ? v as string : null; }
            public bool Bool(string k, bool fallback) { object v; return Props.TryGetValue(k, out v) && v is bool ? (bool)v : fallback; }
        }

        /// <summary>Something that happened in the room, for the game.</summary>
        public sealed class Event
        {
            public string Kind;            // "joined", "join", "leave", "custom", "roomprops", "left", "error", "lobby"
            public int Code, Actor;
            public object Content;
            public string Message;
        }

        public readonly SiteConfig Config;
        public Action<string> Log = _ => { };
        public State Status { get; private set; } = State.Disconnected;
        public string Error { get; private set; }
        public string Nickname = "Player";

        // room state (read under Lock)
        public readonly object Lock = new object();
        public string RoomName { get; private set; }
        public int MyActor { get; private set; }
        public int MasterClientId { get; private set; }
        public readonly Dictionary<int, string> Actors = new Dictionary<int, string>();
        public Dictionary<string, object> RoomProps = new Dictionary<string, object>();
        public readonly Dictionary<string, RoomInfo> Rooms = new Dictionary<string, RoomInfo>();

        private PhotonPeer ns, master, game;
        private string secret, userId, masterAddress;
        private readonly List<Action> jobs = new List<Action>();
        private readonly List<Event> events = new List<Event>();
        private readonly List<KeyValuePair<PhotonPeer, Dictionary<string, object>>> inbox = new List<KeyValuePair<PhotonPeer, Dictionary<string, object>>>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private Thread thread;
        private volatile bool running;
        // the room we're creating/joining: re-sent to the game server
        private object[] pendingOp;
        private int pendingCode;

        public PhotonClient(SiteConfig config) { Config = config; }

        // ------------------------------------------------------------------ requests (any thread)

        public void Connect() => Do(() => { if (Status == State.Disconnected || Status == State.Error) ConnectNameServer(); });

        public void CreateRoom(string name, Dictionary<string, object> gameProps)
        {
            Do(() =>
            {
                if (Status != State.InLobby) return;
                pendingCode = 227;
                pendingOp = new object[] { 255, name, 248, gameProps, 241, true, 250, true, 232, true, 239, true };
                Status = State.JoiningGame;
                master.Operation(227, pendingOp);
            });
        }

        public void JoinRoom(string name)
        {
            Do(() =>
            {
                if (Status != State.InLobby) return;
                pendingCode = 226;
                pendingOp = new object[] { 255, name };
                Status = State.JoiningGame;
                master.Operation(226, pendingOp);
            });
        }

        public void LeaveRoom() => Do(() => { if (Status == State.InRoom) { Status = State.Leaving; game.Operation(254); } });

        /// <summary>Custom event. toAll = the sender gets it too (receiver group All), else Others.</summary>
        public void RaiseEvent(int code, object content, bool toAll)
        {
            Do(() =>
            {
                if (Status != State.InRoom) return;
                if (toAll) game.Operation(253, 244, code, 245, content, 246, 1);
                else game.Operation(253, 244, code, 245, content);
            });
        }

        public void SetRoomProps(Dictionary<string, object> props, Dictionary<string, object> expected = null)
        {
            Do(() =>
            {
                if (Status != State.InRoom) return;
                if (expected != null) game.Operation(252, 251, props, 250, true, 231, expected);
                else game.Operation(252, 251, props, 250, true);
                lock (Lock) foreach (var kv in props) RoomProps[kv.Key] = kv.Value;
            });
        }

        public void SetMyName(string name)
        {
            Nickname = name;
            Do(() => { if (Status == State.InRoom) game.Operation(252, 254, MyActor, 251, new Dictionary<string, object> { { "255", name } }, 250, true); });
        }

        public void Disconnect() => Do(() => { CloseAll(); Status = State.Disconnected; lock (Lock) { Rooms.Clear(); Actors.Clear(); RoomName = null; } });

        public List<Event> TakeEvents() { lock (events) { var l = new List<Event>(events); events.Clear(); return l; } }

        public List<RoomInfo> RoomList() { lock (Lock) return new List<RoomInfo>(Rooms.Values); }

        public int LowestActor() { lock (Lock) { int lo = int.MaxValue; foreach (var a in Actors.Keys) lo = Math.Min(lo, a); return lo == int.MaxValue ? MyActor : lo; } }

        /// <summary>The room's master client (the site's "host"): room masterClientId, else the lowest actor number.</summary>
        public int Host { get { lock (Lock) return MasterClientId > 0 && Actors.ContainsKey(MasterClientId) ? MasterClientId : LowestActor(); } }

        private void Do(Action a)
        {
            lock (jobs) jobs.Add(a);
            if (thread == null) { running = true; thread = new Thread(Loop) { IsBackground = true, Name = "FlappyCrix-Multiplayer" }; thread.Start(); }
            wake.Set();
        }

        private void Emit(Event e) { lock (events) events.Add(e); }

        // ------------------------------------------------------------------ the worker

        private void Loop()
        {
            var todo = new List<Action>();
            var msgs = new List<KeyValuePair<PhotonPeer, Dictionary<string, object>>>();
            while (running)
            {
                wake.WaitOne(50);
                lock (jobs) { todo.AddRange(jobs); jobs.Clear(); }
                foreach (var a in todo) Guard(a);
                todo.Clear();
                lock (inbox) { msgs.AddRange(inbox); inbox.Clear(); }
                foreach (var m in msgs) Guard(() => Handle(m.Key, m.Value));
                msgs.Clear();
                Guard(Watch);
            }
        }

        private void Guard(Action a)
        {
            try { a(); }
            catch (ThreadAbortException) { }                 // the game is closing
            catch (Exception e) { Fail("multiplayer error: " + e.GetType().Name + ": " + e.Message); Log("Multiplayer: " + e); }
        }

        private void Fail(string why)
        {
            Error = why;
            Log("Multiplayer: " + why);
            bool wasInRoom = Status == State.InRoom;
            CloseAll();
            Status = State.Error;
            if (wasInRoom) Emit(new Event { Kind = "left", Message = why });
            Emit(new Event { Kind = "error", Message = why });
        }

        private void Watch()
        {
            foreach (var p in new[] { ns, master, game })
                if (p != null) p.KeepAlive();
            // a socket that closed under us
            if (Status == State.InRoom && game != null && game.Closed) Fail("lost the connection to the room (" + game.CloseReason + ")");
            else if (Status == State.InLobby && master != null && master.Closed) Fail("lost the connection to multiplayer (" + master.CloseReason + ")");
        }

        private PhotonPeer Open(string address)
        {
            var p = new PhotonPeer(address, Config.PhotonAppId, OnPeerConnect, (peer, msg) => { lock (inbox) inbox.Add(new KeyValuePair<PhotonPeer, Dictionary<string, object>>(peer, msg)); wake.Set(); }, Config.Site);
            p.Open(10000);
            return p;
        }

        private void OnPeerConnect(PhotonPeer p)
        {
            // runs on the socket's reader thread: hand it to the worker
            lock (inbox) inbox.Add(new KeyValuePair<PhotonPeer, Dictionary<string, object>>(p, null));
            wake.Set();
        }

        private void ConnectNameServer()
        {
            Error = null;
            Status = State.ConnectingNameServer;
            Log("Multiplayer: connecting to Photon (" + Config.PhotonRegion + ")");
            try { ns = Open(Config.PhotonNameServer); }
            catch (Exception e) { Fail("couldn't reach Photon (" + e.Message + ")"); }
        }

        private void ConnectMaster()
        {
            Status = State.ConnectingMaster;
            try { master = Open(masterAddress); }
            catch (Exception e) { Fail("couldn't reach the lobby server (" + e.Message + ")"); }
        }

        private void Handle(PhotonPeer p, Dictionary<string, object> msg)
        {
            if (msg == null)            // session open: authenticate
            {
                if (p == ns) p.Operation(230, 224, Config.PhotonAppId, 220, Config.PhotonAppVersion, 210, Config.PhotonRegion);
                else if (p == master) p.Operation(230, 221, secret);
                else if (p == game)
                {
                    var vals = new List<object> { 224, Config.PhotonAppId, 220, Config.PhotonAppVersion, 221, secret };
                    if (!string.IsNullOrEmpty(userId)) { vals.Add(225); vals.Add(userId); }
                    p.Operation(230, vals.ToArray());
                }
                return;
            }
            if (msg.ContainsKey("irs")) return;                       // ping answer
            var v = Vals(msg);
            if (msg.ContainsKey("res"))
            {
                int op = (int)Num(msg, "res"), err = (int)Num(msg, "err");
                string text = MiniJson.Str(msg, "msg");
                if (p == ns) OnNameServer(op, err, text, v);
                else if (p == master) OnMaster(op, err, text, v);
                else if (p == game) OnGame(op, err, text, v);
                return;
            }
            if (msg.ContainsKey("evt"))
            {
                int code = (int)Num(msg, "evt");
                if (p == master) OnLobbyEvent(code, v);
                else if (p == game) OnRoomEvent(code, v);
            }
        }

        private void OnNameServer(int op, int err, string text, Dictionary<string, object> v)
        {
            if (op != 230) return;
            if (err != 0) { Fail(err == 32757 ? "multiplayer is full right now (too many players online)" : "Photon refused the connection (" + err + " " + text + ")"); return; }
            masterAddress = V(v, 230) as string;
            secret = V(v, 221) as string;
            userId = V(v, 225) as string ?? userId;
            ns.Dispose(); ns = null;
            ConnectMaster();
        }

        private void OnMaster(int op, int err, string text, Dictionary<string, object> v)
        {
            if (op == 230)
            {
                if (err != 0) { Fail("the lobby server refused the connection (" + err + " " + text + ")"); return; }
                if (V(v, 221) is string) secret = (string)V(v, 221);
                master.Operation(229);
                return;
            }
            if (op == 229)
            {
                Status = State.InLobby;
                Emit(new Event { Kind = "lobby" });
                Log("Multiplayer: in the lobby");
                return;
            }
            if (op == 227 || op == 226)
            {
                if (err != 0)
                {
                    Status = State.InLobby;
                    string why = err == 32758 ? "There's no room with that code." : err == 32765 ? "That room is full." : err == 32764 ? "That room is closed." :
                                 err == 32766 ? "That room code is taken - try again." : "Couldn't join (" + err + " " + text + ").";
                    Emit(new Event { Kind = "error", Message = why });
                    return;
                }
                if (V(v, 221) is string) secret = (string)V(v, 221);
                string addr = V(v, 230) as string;
                master.Dispose(); master = null;
                try { game = Open(addr); }
                catch (Exception e) { Fail("couldn't reach the room's server (" + e.Message + ")"); }
            }
        }

        private void OnGame(int op, int err, string text, Dictionary<string, object> v)
        {
            if (op == 230)
            {
                if (err != 0) { Fail("the room's server refused the connection (" + err + " " + text + ")"); return; }
                var vals = new List<object>(pendingOp);
                var me = new Dictionary<string, object> { { "255", Nickname } };
                if (pendingCode == 226) { vals.Add(250); vals.Add(true); }
                vals.Add(249); vals.Add(me);
                game.Operation(pendingCode, vals.ToArray());
                return;
            }
            if (op == 227 || op == 226)
            {
                if (err != 0)
                {
                    string why = err == 32758 ? "There's no room with that code." : err == 32765 ? "That room is full." : "Couldn't join (" + err + " " + text + ").";
                    game.Dispose(); game = null;
                    Emit(new Event { Kind = "error", Message = why });
                    BackToLobby();
                    return;
                }
                lock (Lock)
                {
                    MyActor = (int)AsNum(V(v, 254));
                    RoomName = V(pendingOp, 255) as string;
                    RoomProps = MiniJson.Obj(V(v, 248)) ?? new Dictionary<string, object>();
                    MasterClientId = (int)Num(RoomProps, "248");
                    Actors.Clear();
                    Actors[MyActor] = Nickname;
                    var actorProps = MiniJson.Obj(V(v, 249));
                    if (actorProps != null)
                        foreach (var kv in actorProps)
                        {
                            int nr; if (!int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out nr)) continue;
                            Actors[nr] = MiniJson.Str(MiniJson.Obj(kv.Value), "255") ?? ("Player" + nr);
                        }
                    var list = V(v, 252) as List<object>;
                    if (list != null) foreach (var o in list) { int nr = (int)AsNum(o); if (!Actors.ContainsKey(nr)) Actors[nr] = "Player" + nr; }
                }
                Status = State.InRoom;
                Log("Multiplayer: in room " + RoomName + " as player " + MyActor);
                Emit(new Event { Kind = "joined", Actor = MyActor, Content = pendingCode == 227 });
                return;
            }
            if (op == 254)
            {
                game.Dispose(); game = null;
                lock (Lock) { RoomName = null; Actors.Clear(); }
                Emit(new Event { Kind = "left" });
                BackToLobby();
                return;
            }
            if (op == 253 && err != 0) Log("Multiplayer: event refused (" + err + " " + text + ")");
        }

        private void BackToLobby()
        {
            if (masterAddress == null) { Status = State.Disconnected; return; }
            ConnectMaster();
        }

        private void OnLobbyEvent(int code, Dictionary<string, object> v)
        {
            if (code == 230 || code == 229)
            {
                var list = MiniJson.Obj(V(v, 222));
                if (list == null) return;
                lock (Lock)
                {
                    if (code == 230) Rooms.Clear();
                    foreach (var kv in list)
                    {
                        var props = MiniJson.Obj(kv.Value) ?? new Dictionary<string, object>();
                        object removed;
                        if (props.TryGetValue("251", out removed) && removed is bool && (bool)removed) { Rooms.Remove(kv.Key); continue; }
                        RoomInfo r;
                        if (!Rooms.TryGetValue(kv.Key, out r)) Rooms[kv.Key] = r = new RoomInfo { Name = kv.Key };
                        foreach (var p in props) r.Props[p.Key] = p.Value;
                    }
                }
            }
        }

        private void OnRoomEvent(int code, Dictionary<string, object> v)
        {
            switch (code)
            {
                case 255:   // join
                {
                    int nr = (int)AsNum(V(v, 254));
                    string name = MiniJson.Str(MiniJson.Obj(V(v, 249)), "255") ?? ("Player" + nr);
                    if (nr == MyActor) return;
                    lock (Lock) Actors[nr] = name;
                    Emit(new Event { Kind = "join", Actor = nr, Message = name });
                    return;
                }
                case 254:   // leave
                {
                    int nr = (int)AsNum(V(v, 254));
                    lock (Lock)
                    {
                        Actors.Remove(nr);
                        if (V(v, 203) != null) MasterClientId = (int)AsNum(V(v, 203));
                    }
                    Emit(new Event { Kind = "leave", Actor = nr });
                    return;
                }
                case 253:   // properties changed
                {
                    int target = (int)AsNum(V(v, 253));
                    var props = MiniJson.Obj(V(v, 251));
                    if (props == null) return;
                    lock (Lock)
                    {
                        if (target > 0) { object n; if (props.TryGetValue("255", out n) && n is string) Actors[target] = (string)n; }
                        else { foreach (var kv in props) RoomProps[kv.Key] = kv.Value; if (props.ContainsKey("248")) MasterClientId = (int)AsNum(props["248"]); }
                    }
                    Emit(new Event { Kind = target > 0 ? "actorprops" : "roomprops", Actor = target, Content = props });
                    return;
                }
                case 252: return;  // disconnect (suspended actor)
                case 223:          // new auth secret
                    if (V(v, 221) is string) secret = (string)V(v, 221);
                    return;
                case 251: Log("Multiplayer: server info " + MiniJson.Serialize(V(v, 218))); return;
                default:
                    Emit(new Event { Kind = "custom", Code = code, Actor = (int)AsNum(V(v, 254)), Content = V(v, 245) });
                    return;
            }
        }

        private void CloseAll()
        {
            foreach (var p in new[] { ns, master, game }) if (p != null) p.Dispose();
            ns = master = game = null;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Incoming "vals" [k1, v1, k2, v2...] as a dictionary keyed by the number's text.</summary>
        private static Dictionary<string, object> Vals(Dictionary<string, object> msg)
        {
            var d = new Dictionary<string, object>();
            object o;
            var list = msg.TryGetValue("vals", out o) ? o as List<object> : null;
            if (list != null)
                for (int i = 0; i + 1 < list.Count; i += 2)
                    d[Convert.ToString(list[i] is double ? (object)(long)(double)list[i] : list[i], CultureInfo.InvariantCulture)] = list[i + 1];
            return d;
        }

        private static object V(Dictionary<string, object> d, int key) { object o; return d.TryGetValue(key.ToString(CultureInfo.InvariantCulture), out o) ? o : null; }
        private static object V(object[] kv, int key) { for (int i = 0; i + 1 < kv.Length; i += 2) if (kv[i] is int && (int)kv[i] == key) return kv[i + 1]; return null; }
        public static double AsNum(object o) => o is double ? (double)o : o is int ? (int)o : o is long ? (long)o : 0;
        public static double Num(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? AsNum(o) : 0; }

        public void Dispose()
        {
            running = false;
            wake.Set();
            CloseAll();
        }
    }
}
