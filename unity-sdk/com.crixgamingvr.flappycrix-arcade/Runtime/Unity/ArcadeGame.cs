// The game, Unity side: runs one cabinet's Flappy Crix on its own thread (AppRunner), uploads
// its picture to a texture and plays its music and sounds from the cabinet.
// Pictures and sounds come from Resources/CrixArcade, so nothing has to be installed next to
// the game. Progress and the high-score table are saved per cabinet in persistentDataPath.
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.IO;
using CrixArcade.Core;
using UnityEngine;

namespace CrixArcade
{
    public sealed class ArcadeGame : IDisposable
    {
        public event Action<string> Log;

        public readonly AppRunner Runner;
        public FlappyApp App => Runner.App;
        public Texture2D Texture { get; private set; }
        public bool HasPicture { get; private set; }
        public string Screen => Runner.Screen;
        public int Score => Runner.Score;
        public string Error => Runner.Error;

        // sounds that restart instead of stacking (as on the site)
        static readonly HashSet<string> Mono = new HashSet<string> { "jump", "coin", "death", "ach", "powerup", "select", "error" };
        static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();

        readonly AudioSource music, effects;
        readonly GameObject audioRoot;
        readonly Dictionary<string, AudioSource> monoSources = new Dictionary<string, AudioSource>();
        readonly List<KeyValuePair<string, float>> soundBuf = new List<KeyValuePair<string, float>>();
        readonly List<string> logBuf = new List<string>();
        readonly string scoresPath;
        bool visible = true;
        int musicRestarts;
        float volume = 1;
        Vector2 lastPointer = new Vector2(-1, -1);

        static bool resourcesHooked;

        public static void HookResources()
        {
            if (resourcesHooked) return;
            resourcesHooked = true;
            CoreResources.Load = name =>
            {
                var t = Resources.Load<TextAsset>("CrixArcade/" + name);
                return t != null ? t.bytes : null;
            };
        }

        /// <param name="saveId">Which save file (one per cabinet id; cabinets can share one).</param>
        /// <param name="audioAnchor">Where the sound comes from.</param>
        public ArcadeGame(string saveId, Transform audioAnchor, float spatialBlend = 0.85f, float maxDistance = 12f)
        {
            HookResources();
            string folder = Path.Combine(Application.persistentDataPath, "CrixArcade");
            string safe = Safe(saveId);
            var save = SaveData.Load(Path.Combine(folder, safe + ".txt"), m => Log?.Invoke(m));
            scoresPath = Path.Combine(folder, safe + ".scores.txt");

            Assets art;
            try { art = SpriteSheet.LoadEmbedded(); }
            catch (Exception e) { art = new Assets(); Debug.LogWarning("[CrixArcade] Pictures unavailable: " + e.Message); }

            var app = new FlappyApp(save, art);
            LoadScores(app);
            app.HighScoreEntered += (n, s) => SaveScoresFromGameThread(app);
            Runner = new AppRunner(app);

            Texture = new Texture2D(FlappyApp.W, FlappyApp.H, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Flappy Crix screen" };

            audioRoot = new GameObject("Flappy Crix sound");
            audioRoot.transform.SetParent(audioAnchor, false);
            music = NewSource(spatialBlend, maxDistance);
            music.loop = true;
            effects = NewSource(spatialBlend, maxDistance);
            this.spatialBlend = spatialBlend; this.maxDistance = maxDistance;
        }

        readonly float spatialBlend, maxDistance;

        AudioSource NewSource(float blend, float maxDist)
        {
            var s = audioRoot.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = blend;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 1f;
            s.maxDistance = maxDist;
            s.dopplerLevel = 0;
            return s;
        }

        static string Safe(string id)
        {
            var b = new System.Text.StringBuilder();
            foreach (char c in string.IsNullOrEmpty(id) ? "cabinet" : id)
                b.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return b.ToString();
        }

        public void Start() => Runner.Start();

        /// <summary>Master volume for this cabinet (0..1), on top of the game's own settings.</summary>
        public float Volume { get => volume; set => volume = Mathf.Clamp01(value); }

        // ------------------------------------------------------------------ high scores

        void LoadScores(FlappyApp app)
        {
            try
            {
                if (!File.Exists(scoresPath)) return;
                foreach (var line in File.ReadAllLines(scoresPath))
                {
                    int bar = line.LastIndexOf('|'), s;
                    if (bar > 0 && int.TryParse(line.Substring(bar + 1), out s)) app.AddHighScore(line.Substring(0, bar), s);
                }
            }
            catch (Exception e) { Debug.LogWarning("[CrixArcade] Couldn't read high scores: " + e.Message); }
        }

        /// <summary>Runs on the game's thread (HighScoreEntered), so it only touches the file.</summary>
        public void SaveScoresFromGameThread(FlappyApp app)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(scoresPath));
                var lines = new List<string>();
                foreach (var h in app.HighScores) lines.Add(h.Name + "|" + h.Score);
                File.WriteAllLines(scoresPath, lines.ToArray());
            }
            catch (Exception e) { Log?.Invoke("Couldn't save high scores: " + e.Message); }
        }

        // ------------------------------------------------------------------ every frame

        public void Tick()
        {
            if (visible) Runner.Nudge();

            var frame = Runner.TakeFrame();
            if (frame != null)
            {
                Texture.SetPixelData(frame, 0);
                Texture.Apply(false);
                HasPicture = true;
                Runner.GiveBack(frame);
            }

            Runner.TakeLogs(logBuf);
            foreach (var m in logBuf) { Debug.Log("[CrixArcade] " + m); Log?.Invoke(m); }
            logBuf.Clear();

            Runner.TakeSounds(soundBuf);
            if (visible) foreach (var s in soundBuf) PlaySound(s.Key, s.Value);
            soundBuf.Clear();

            UpdateMusic();
        }

        static AudioClip Clip(string name)
        {
            AudioClip c;
            if (Clips.TryGetValue(name, out c)) return c;
            c = Resources.Load<AudioClip>("CrixArcade/Sounds/" + name);
            Clips[name] = c;          // a missing sound is remembered as null (silent)
            return c;
        }

        void PlaySound(string name, float vol)
        {
            var clip = Clip(name);
            if (clip == null) return;
            vol *= volume;
            if (Mono.Contains(name))
            {
                AudioSource src;
                if (!monoSources.TryGetValue(name, out src))
                {
                    src = NewSource(spatialBlend, maxDistance);
                    src.clip = clip;
                    monoSources[name] = src;
                }
                src.Stop();
                src.volume = vol;
                src.Play();
            }
            else effects.PlayOneShot(clip, vol);
        }

        void UpdateMusic()
        {
            bool play = visible && Runner.MusicShouldPlay;
            var clip = play ? (Clip(Runner.MusicTrack) ?? Clip("music")) : null;
            if (clip == null)
            {
                if (music.isPlaying) music.Pause();
                return;
            }
            music.volume = Runner.MusicVolume * volume;
            bool restart = Runner.MusicRestarts != musicRestarts;
            musicRestarts = Runner.MusicRestarts;
            if (music.clip != clip) { music.clip = clip; music.Play(); return; }
            if (restart) { music.Stop(); music.Play(); return; }
            if (!music.isPlaying) music.UnPause();
            if (!music.isPlaying) music.Play();
        }

        /// <summary>Hidden or far away = silent, and the game stops drawing (it keeps its state).</summary>
        public bool Visible
        {
            get => visible;
            set
            {
                if (visible == value) return;
                visible = value;
                Runner.Visible = value;
                if (!value)
                {
                    if (music.isPlaying) music.Pause();
                    effects.Stop();
                    foreach (var s in monoSources.Values) s.Stop();
                }
            }
        }

        // ------------------------------------------------------------------ input (thread-safe: queued)

        public void Post(Action<FlappyApp> a) => Runner.Post(a);
        public void Flap() => Runner.Post(a => a.Flap());
        public void StartButton() => Runner.Post(a => a.StartButton());
        public void Back() => Runner.Post(a => a.Back());
        public void Select() => Runner.Post(a => a.Select());
        public void Navigate(int dx, int dy) => Runner.Post(a => a.Navigate(dx, dy));
        public void InsertCoin() => Runner.Post(a => a.InsertCoin());

        /// <summary>uv: 0..1 across the screen, origin top-left.</summary>
        public void PointerMove(Vector2 uv)
        {
            if ((uv - lastPointer).sqrMagnitude < 1e-6f) return;
            lastPointer = uv;
            float u = uv.x, v = uv.y;
            Runner.Post(a => a.PointerMove(u, v));
        }

        public void PointerDown(Vector2 uv) { float u = uv.x, v = uv.y; Runner.Post(a => a.PointerDown(u, v)); }

        public void Dispose()
        {
            Runner.Dispose();
            if (audioRoot != null) UnityEngine.Object.Destroy(audioRoot);
            if (Texture != null) UnityEngine.Object.Destroy(Texture);
        }
    }
}
