// Runs the in-game version (FlappyApp) on its own thread, so its game rules and drawing never
// take time from Gorilla Tag's frames. The Unity side only hands over input, picks up the
// finished pictures and sounds, and uploads the picture.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace FlappyCrix.Native
{
    public sealed class AppRunner : IDisposable
    {
        public readonly FlappyApp App;

        /// <summary>Pictures per second while the screen is open.</summary>
        public int FrameRate = 30;

        // What the Unity side reads (copied under the lock after every loop)
        public string Screen { get; private set; } = "menu";
        public int Score { get; private set; }
        public string MusicTrack { get; private set; } = "music";
        public bool MusicShouldPlay { get; private set; }
        public float MusicVolume { get; private set; }
        public int MusicRestarts { get; private set; }
        public string Error { get; private set; }

        private readonly object gate = new object();
        private readonly List<Action<FlappyApp>> inbox = new List<Action<FlappyApp>>();
        private readonly List<KeyValuePair<string, float>> sounds = new List<KeyValuePair<string, float>>();
        private readonly List<string> logs = new List<string>();
        private uint[] ready;                     // the newest finished picture (null = none new)
        private uint[] spare;
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private volatile bool running, visible = true;
        private Thread thread;

        public AppRunner(FlappyApp app)
        {
            App = app;
            App.Sound += (n, v) => { lock (gate) { if (sounds.Count < 32) sounds.Add(new KeyValuePair<string, float>(n, v)); } };
            App.Log += m => { lock (gate) logs.Add(m); };
        }

        public void Start()
        {
            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "FlappyCrix-InGame", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        /// <summary>Queue something to do to the game (input). It runs on the game's thread, in order.</summary>
        public void Post(Action<FlappyApp> a)
        {
            lock (gate) inbox.Add(a);
            wake.Set();
        }

        /// <summary>Called every Unity frame: lets the game's thread run promptly.</summary>
        public void Nudge() => wake.Set();

        public bool Visible
        {
            get => visible;
            set { visible = value; wake.Set(); }
        }

        /// <summary>The newest picture since the last call, or null. The caller may keep it until its next call.</summary>
        public uint[] TakeFrame()
        {
            lock (gate)
            {
                var f = ready;
                ready = null;
                return f;
            }
        }

        public void TakeSounds(List<KeyValuePair<string, float>> into)
        {
            lock (gate) { into.AddRange(sounds); sounds.Clear(); }
        }

        /// <summary>A log line from any thread (the online services).</summary>
        public void AddLog(string m) { lock (gate) { if (logs.Count < 200) logs.Add(m); } }

        public void TakeLogs(List<string> into)
        {
            lock (gate) { into.AddRange(logs); logs.Clear(); }
        }

        private void Loop()
        {
            var clock = Stopwatch.StartNew();
            double last = 0, lastFrame = -1;
            bool wasVisible = true;
            var todo = new List<Action<FlappyApp>>();
            try
            {
                while (running)
                {
                    double t = clock.Elapsed.TotalSeconds;
                    float dt = (float)(t - last);
                    last = t;

                    lock (gate) { todo.AddRange(inbox); inbox.Clear(); }
                    bool vis = visible;
                    App.Visible = vis;
                    foreach (var a in todo) a(App);
                    todo.Clear();
                    App.Update(dt);
                    if (!vis && wasVisible) App.Flush();
                    wasVisible = vis;

                    if (vis && t - lastFrame >= 1.0 / Math.Max(5, FrameRate) - 0.002)
                    {
                        lastFrame = t;
                        if (App.Render())
                        {
                            lock (gate)
                            {
                                var buf = spare != null && spare.Length == App.C.Px.Length ? spare : new uint[App.C.Px.Length];
                                Array.Copy(App.C.Px, buf, buf.Length);
                                spare = ready;            // a picture nobody took is reused
                                ready = buf;
                            }
                        }
                    }
                    lock (gate)
                    {
                        Screen = App.Screen; Score = App.Sim.Score;
                        MusicTrack = App.MusicTrack; MusicShouldPlay = App.MusicShouldPlay && vis;
                        MusicVolume = App.MusicVolume; MusicRestarts = App.MusicRestarts;
                    }
                    // run again on the next Unity frame (or after 8 ms if none comes)
                    wake.WaitOne(App.Screen == "playing" || vis ? 8 : 100);
                }
            }
            catch (Exception e)
            {
                Error = e.GetType().Name + ": " + e.Message;
                lock (gate) logs.Add("The in-game version stopped: " + e);
            }
            try { App.Flush(); } catch { }
        }

        /// <summary>The picture buffer the Unity side gave back after uploading it, for reuse.</summary>
        public void GiveBack(uint[] frame)
        {
            lock (gate) { if (spare == null && frame != null && frame.Length == App.C.Px.Length) spare = frame; }
        }

        public void Dispose()
        {
            running = false;
            wake.Set();
            try { if (thread != null && !thread.Join(1000)) { } } catch { }
        }
    }
}
