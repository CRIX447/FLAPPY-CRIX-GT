// One cabinet as the network sees it: where it is, which link group it belongs to, what its
// game is doing, and a queue of what the game asked for (RACE pressed, run over, high score).
// The game runs on its own thread, so commands are posted to it and its events are queued here
// until ArcadeNetwork reads them on the main thread.
// No UnityEngine references (tested outside Unity).
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using CrixArcade.Core;

namespace CrixArcade.Net
{
    public struct ArcadeVec
    {
        public float X, Y, Z;
        public ArcadeVec(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static float Distance(ArcadeVec a, ArcadeVec b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    /// <summary>A copy of the game's state, read on the main thread.</summary>
    public struct SeatState
    {
        public string Screen;
        public int Score;
        public float BirdY;
        public bool InRace, Spectating, InLobby, CountingDown, Attract, EnteringInitials;
    }

    public sealed class ArcadeSeat
    {
        public enum EventKind { RaceRequested, RaceLeft, RaceDied, HighScore, RunStarted, RunEnded }
        public struct Event { public EventKind Kind; public int Score; public string Name; }

        public readonly string CabinetId;
        /// <summary>Cabinets with the same group race each other ("" = this one is single player).</summary>
        public string Group;
        public ArcadeVec Position;
        /// <summary>Called on the game's thread after a high score from another player was added (to save it).</summary>
        public Action<FlappyApp> ScoresChanged;
        /// <summary>The last time the local player pressed something on this cabinet (seconds, network clock).</summary>
        public double TouchedAt = double.NegativeInfinity;

        readonly Action<Action<FlappyApp>> post;
        readonly Func<SeatState> read;
        readonly object gate = new object();
        readonly List<Event> events = new List<Event>();

        // what was last sent to the game, so it is only told about changes
        internal string shownRemote; internal int shownRemoteScore = -1; internal string shownRemoteScreen;
        internal bool shownRaceAvailable; internal int shownNearby = -1;
        internal string shownLobby;

        public ArcadeSeat(string cabinetId, FlappyApp app, Action<Action<FlappyApp>> post, Func<SeatState> read)
        {
            CabinetId = cabinetId;
            this.post = post;
            this.read = read;
            app.RaceRequested += () => Push(EventKind.RaceRequested, 0, null);
            app.RaceLeft += () => Push(EventKind.RaceLeft, 0, null);
            app.RaceDied += s => Push(EventKind.RaceDied, s, null);
            app.HighScoreEntered += (n, s) => Push(EventKind.HighScore, s, n);
            app.RunStarted += (seed, race) => Push(EventKind.RunStarted, 0, null);
            app.RunEnded += s => Push(EventKind.RunEnded, s, null);
        }

        /// <summary>A seat for a game run by an AppRunner (the normal case).</summary>
        public static ArcadeSeat For(string cabinetId, AppRunner r) =>
            new ArcadeSeat(cabinetId, r.App, r.Post, () => new SeatState
            {
                Screen = r.Screen, Score = r.Score, BirdY = r.BirdY, InRace = r.InRace, Spectating = r.Spectating,
                InLobby = r.InLobby, CountingDown = r.CountingDown, Attract = r.Attract, EnteringInitials = r.EnteringInitials,
            });

        void Push(EventKind k, int score, string name) { lock (gate) events.Add(new Event { Kind = k, Score = score, Name = name }); }

        internal void TakeEvents(List<Event> into) { lock (gate) { into.AddRange(events); events.Clear(); } }

        public SeatState State => read();
        public void Post(Action<FlappyApp> a) => post(a);
    }
}
