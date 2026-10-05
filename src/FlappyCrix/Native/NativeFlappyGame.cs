// The in-game version, Unity side. FlappyApp (the game, menus and seasons) runs on its own
// thread in AppRunner; this class hands it the controls, uploads its picture to one texture
// and plays its music and sounds (the site's own mp3 files in Web/img) from the screen.
// Everything goes quiet while the screen is hidden.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace FlappyCrix.Native
{
    public sealed class NativeFlappyGame : IFlappyGame
    {
        public event Action<string> Log;
        public string ModeName => "In-game version (Flappy Crix built into the mod)";
        public bool IsReady => true;
        public bool HasFailed => runner.Error != null;
        public string FailureReason => runner.Error;
        public Texture PanelTexture => uploaded ? texture : null;
        // the picture's rows are top-down, so it is shown flipped
        public Vector2 TextureScale => new Vector2(1, -1);
        public Vector2 TextureOffset => new Vector2(0, 1);
        public float Aspect => FlappyApp.W / (float)FlappyApp.H;
        public string Screen => runner.Screen;
        public int Score => runner.Score;

        // Sounds that restart instead of stacking (as on the site)
        static readonly HashSet<string> Mono = new HashSet<string> { "jump", "coin", "death", "ach", "powerup", "select", "error", "hover", "click", "fail", "victory", "67", "boom" };
        /// <summary>Every sound the game plays (the site's own mp3 files, built into the DLL).</summary>
        public static readonly string[] Effects =
        {
            "jump", "coin", "death", "milestone", "67", "boom", "victory", "ach", "powerup", "purchase", "unlock",
            "select", "rank", "woosh", "pop", "error", "smash", "witch1", "santahohoho", "fail",
            "hover", "click", "swoosh",                      // menu navigation
        };
        public static readonly string[] Music = { "music", "halloweenmusic", "christmasmusic" };

        private readonly AppRunner runner;
        private readonly Texture2D texture;
        private bool uploaded, visible = true;
        private readonly string soundFolder;
        private readonly List<string> early = new List<string>();      // log lines from before Log had listeners
        private bool soundsReported;
        private readonly MonoBehaviour host;
        private readonly GameObject audioRoot;
        private readonly AudioSource music, effects;
        private readonly Dictionary<string, AudioSource> monoSources = new Dictionary<string, AudioSource>();
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly HashSet<string> loading = new HashSet<string>();
        private int musicRestarts;
        private readonly List<KeyValuePair<string, float>> soundBuf = new List<KeyValuePair<string, float>>();
        private readonly List<string> logBuf = new List<string>();
        private Vector2 lastPointer = new Vector2(-1, -1);

        private readonly Online.OnlineServices online;
        private readonly List<string> urls = new List<string>();

        public NativeFlappyGame(Transform audioAnchor, string modFolder, MonoBehaviour host, bool flapStartsGame, bool onlineFeatures = true)
        {
            this.host = host;
            string data = ModPaths.DataRoot(modFolder);
            soundFolder = UnpackSounds(Path.Combine(data, "sounds"), Path.Combine(Path.Combine(modFolder, "Web"), "img"));
            string savePath = Path.Combine(data, "save.txt");
            var save = SaveData.Load(savePath, m => early.Add(m));
            if (!File.Exists(savePath)) ImportOldBest(save, data, modFolder);

            Assets art;
            try { art = SpriteSheet.LoadEmbedded(); }
            catch (Exception e) { art = new Assets(); early.Add("Pictures unavailable: " + e.Message); }

            // the crixgamingvr.com account and multiplayer (their own threads; log lines go through the runner)
            AppRunner r0 = null;
            if (onlineFeatures)
                online = new Online.OnlineServices(new Online.SiteConfig(), data, m => { if (r0 != null) r0.AddLog(m); });
            var app = new FlappyApp(save, art, 0, null, online) { FlapStartsGame = flapStartsGame };
            app.OpenUrl += u => { lock (urls) urls.Add(u); };
            runner = new AppRunner(app);
            r0 = runner;
            online?.Start();

            texture = new Texture2D(FlappyApp.W, FlappyApp.H, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

            // Sound comes from the screen
            audioRoot = new GameObject("FlappyCrixAudio");
            audioRoot.transform.SetParent(audioAnchor, false);
            music = audioRoot.AddComponent<AudioSource>();
            music.loop = true; music.playOnAwake = false; music.spatialBlend = 0.6f;
            effects = audioRoot.AddComponent<AudioSource>();
            effects.playOnAwake = false; effects.spatialBlend = 0.6f;
            if (soundFolder != null) { foreach (var n in Effects) LoadClip(n); foreach (var n in Music) LoadClip(n); }
            else early.Add("No sound files could be found or unpacked, so the in-game version is silent.");

            runner.Start();
        }

        /// <summary>The best score from earlier test builds (native-best.txt) carries over.</summary>
        private static void ImportOldBest(SaveData save, string data, string modFolder)
        {
            foreach (var f in new[] { Path.Combine(data, "native-best.txt"), Path.Combine(Path.Combine(modFolder, "BrowserData"), "native-best.txt") })
                try { int b; if (File.Exists(f) && int.TryParse(File.ReadAllText(f).Trim(), out b) && b > save.Best) save.Best = b; } catch { }
        }

        /// <summary>
        /// The sounds are built into the DLL; they're written once to %LOCALAPPDATA%\FlappyCrix\sounds (Unity
        /// loads mp3 from files), so they work with only FlappyCrix.dll installed. Falls back to Web/img.
        /// </summary>
        private string UnpackSounds(string folder, string webImg)
        {
            var asm = typeof(NativeFlappyGame).Assembly;
            int written = 0, found = 0;
            try
            {
                Directory.CreateDirectory(folder);
                foreach (var res in asm.GetManifestResourceNames())
                {
                    if (!res.StartsWith("FlappyCrix.sound.", StringComparison.Ordinal)) continue;
                    found++;
                    string path = Path.Combine(folder, res.Substring("FlappyCrix.sound.".Length));
                    using (var s = asm.GetManifestResourceStream(res))
                    {
                        if (File.Exists(path) && new FileInfo(path).Length == s.Length) continue;
                        using (var f = File.Create(path)) s.CopyTo(f);
                        written++;
                    }
                }
                if (found > 0)
                {
                    early.Add("Sounds: " + found + " built in" + (written > 0 ? ", " + written + " written to " + folder : "") + ".");
                    return folder;
                }
            }
            catch (Exception e) { early.Add("Couldn't unpack the sounds (" + e.Message + ")."); }
            if (Directory.Exists(webImg)) { early.Add("Sounds: using " + webImg); return webImg; }
            return null;
        }

        private void LoadClip(string name)
        {
            if (soundFolder == null || clips.ContainsKey(name) || loading.Contains(name)) return;
            string path = Path.Combine(soundFolder, name + ".mp3");
            if (!File.Exists(path)) return;
            loading.Add(name);
            host.StartCoroutine(LoadClipRoutine(name, path));
        }

        private IEnumerator LoadClipRoutine(string name, string path)
        {
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.MPEG))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip != null) clips[name] = clip;
                    else early.Add("Sound " + name + " didn't decode.");
                }
                else early.Add("Couldn't load sound " + name + ": " + req.error);
            }
            loading.Remove(name);
            if (loading.Count == 0 && !soundsReported)
            {
                soundsReported = true;
                early.Add("Sounds ready: " + clips.Count + " of " + (Effects.Length + Music.Length) + " loaded.");
            }
        }

        public void Tick()
        {
            // Silent whenever the screen isn't showing - whatever hid it - not only when SetVisible(false) was called.
            bool shown = visible && audioRoot != null && audioRoot.activeInHierarchy;
            if (shown != audible) SetAudible(shown);
            if (visible) runner.Nudge();
            if (early.Count > 0) { foreach (var m in early.ToArray()) Log?.Invoke(m); early.Clear(); }

            var frame = runner.TakeFrame();
            if (frame != null)
            {
                texture.SetPixelData(frame, 0);
                texture.Apply(false);
                uploaded = true;
                runner.GiveBack(frame);
            }

            runner.TakeLogs(logBuf);
            foreach (var m in logBuf) Log?.Invoke(m);
            logBuf.Clear();

            runner.TakeSounds(soundBuf);
            if (audible) foreach (var s in soundBuf) PlaySound(s.Key, s.Value);
            soundBuf.Clear();

            UpdateMusic();

            // "OPEN ON THIS PC" on the account screen: the site's link page in the PC's browser
            lock (urls)
            {
                foreach (var u in urls)
                    if (u.StartsWith("https://", StringComparison.Ordinal)) { Log?.Invoke("Opening " + u + " in the PC's browser"); Application.OpenURL(u); }
                urls.Clear();
            }
        }

        private void PlaySound(string name, float volume)
        {
            AudioClip clip;
            if (!clips.TryGetValue(name, out clip)) { LoadClip(name); return; }
            if (Mono.Contains(name))
            {
                AudioSource src;
                if (!monoSources.TryGetValue(name, out src))
                {
                    src = audioRoot.AddComponent<AudioSource>();
                    src.playOnAwake = false; src.spatialBlend = 0.6f; src.clip = clip; src.mute = !audible;
                    monoSources[name] = src;
                }
                src.Stop();
                src.volume = volume;
                src.Play();
            }
            else effects.PlayOneShot(clip, volume);
        }

        private void UpdateMusic()
        {
            string want = runner.MusicTrack;
            bool play = audible && runner.MusicShouldPlay;
            AudioClip clip = null;
            if (play && !clips.TryGetValue(want, out clip))
            {
                LoadClip(want);
                // until the season's track has loaded (or if it's missing) the normal one plays
                if (!clips.TryGetValue("music", out clip)) { LoadClip("music"); clip = null; }
            }
            if (!play || clip == null)
            {
                if (music.isPlaying) music.Pause();
                return;
            }
            music.volume = runner.MusicVolume;
            bool restart = runner.MusicRestarts != musicRestarts;
            musicRestarts = runner.MusicRestarts;
            if (music.clip != clip) { music.clip = clip; music.Play(); return; }
            if (restart) { music.Stop(); music.Play(); return; }
            if (!music.isPlaying) music.UnPause();
            if (!music.isPlaying) music.Play();
        }

        /// <summary>Screen hidden = silent (music paused, sounds cut) and the game stops drawing.</summary>
        public void SetVisible(bool v)
        {
            visible = v;
            SetAudible(v && audioRoot != null && audioRoot.activeInHierarchy);
        }

        private bool audible = true;

        /// <summary>Every sound source muted and stopped (or unmuted), and the game told not to make sounds.</summary>
        private void SetAudible(bool on)
        {
            bool changed = on != audible;
            audible = on;
            runner.Visible = on;
            foreach (var src in AllSources())
            {
                src.mute = !on;
                if (!on && src != music) src.Stop();
            }
            if (!on && music.isPlaying) music.Pause();
            if (changed) early.Add(on ? "Sound on (screen showing)." : "Sound off (screen hidden).");
        }

        private IEnumerable<AudioSource> AllSources()
        {
            if (music != null) yield return music;
            if (effects != null) yield return effects;
            foreach (var s in monoSources.Values) yield return s;
        }

        // ------------------------------------------------------------------ input

        public void Flap() => runner.Post(a => a.Flap());
        public void Restart() => runner.Post(a => a.StartButton());
        public void TogglePause() => runner.Post(a => a.TogglePause());
        public void Back() => runner.Post(a => a.Back());
        public void Navigate(int dx, int dy) => runner.Post(a => a.Navigate(dx, dy));
        /// <summary>SELECT: menus only - it never flaps.</summary>
        public void Select() => runner.Post(a => a.Select());
        public void StartButton() => runner.Post(a => a.StartButton());

        public void PointerMove(Vector2 uv)
        {
            if ((uv - lastPointer).sqrMagnitude < 1e-6f) return;
            lastPointer = uv;
            float u = uv.x, v = uv.y;
            runner.Post(a => a.PointerMove(u, v));
        }
        public void PointerDown(Vector2 uv) { float u = uv.x, v = uv.y; runner.Post(a => a.PointerDown(u, v)); }
        public void PointerUp(Vector2 uv) { }
        public void Scroll(Vector2 uv, int delta) { }

        public void Dispose()
        {
            runner.Dispose();
            try { online?.Dispose(); } catch { }
            if (audioRoot != null) UnityEngine.Object.Destroy(audioRoot);
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
    }
}
