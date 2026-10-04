// Native fallback, Unity side: runs NativeSim at 60 Hz, draws it with NativeRenderer into
// one texture (shown exactly like the website stream) and plays the site's sounds.
// Used only when no website engine can run.
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
        const float TickSeconds = 1f / 60f, UploadInterval = 1f / 30f;

        public event Action<string> Log;
        public string ModeName => "Native Unity version (offline)";
        public bool IsReady => true;
        public bool HasFailed => false;
        public string FailureReason => null;
        public Texture PanelTexture => texture;
        public Vector2 TextureScale => Vector2.one;
        public Vector2 TextureOffset => Vector2.zero;
        public float Aspect => NativeSim.W / (float)NativeSim.H;
        public string Screen => sim.Screen;
        public int Score => sim.Score;

        private readonly NativeSim sim = new NativeSim();
        private readonly NativeRenderer renderer = new NativeRenderer();
        private readonly Texture2D texture;
        private readonly Color32[] scratch = new Color32[NativeSim.W * NativeSim.H];
        private readonly string webFolder, bestFile;
        private readonly AudioSource audio;
        private readonly Dictionary<string, AudioClip> sfx = new Dictionary<string, AudioClip>();
        private float acc, nextUpload;
        private bool dirty = true;

        public NativeFlappyGame(Transform audioAnchor, string modFolder, MonoBehaviour host)
        {
            webFolder = Path.Combine(modFolder, "Web");
            bestFile = Path.Combine(ModPaths.DataRoot(modFolder), "native-best.txt");
            string oldBest = Path.Combine(modFolder, "BrowserData", "native-best.txt");      // where earlier builds kept it
            foreach (var f in new[] { bestFile, oldBest })
                try { int b; if (File.Exists(f) && int.TryParse(File.ReadAllText(f).Trim(), out b) && b > sim.Best) sim.Best = b; } catch { }
            sim.NewBest += best =>
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(bestFile)); File.WriteAllText(bestFile, best.ToString()); } catch { }
            };

            // The site's own bird and coin pictures
            var birdTex = Visuals.LoadPng(Path.Combine(webFolder, "img", "bird.png"));
            var coinTex = Visuals.LoadPng(Path.Combine(webFolder, "img", "coin-still.png"));
            try
            {
                renderer.SetSprites(birdTex != null ? Visuals.ToTopDown(birdTex) : null, birdTex?.width ?? 0, birdTex?.height ?? 0,
                                    coinTex != null ? Visuals.ToTopDown(coinTex) : null, coinTex?.width ?? 0, coinTex?.height ?? 0);
            }
            catch (Exception e) { Log?.Invoke("Sprites unavailable: " + e.Message); }

            texture = new Texture2D(NativeSim.W, NativeSim.H, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

            audio = audioAnchor.gameObject.AddComponent<AudioSource>();
            audio.spatialBlend = 0.6f;
            audio.playOnAwake = false;
            foreach (var n in new[] { "jump", "death", "coin", "select", "milestone" })
                host.StartCoroutine(LoadClip(n));
            sim.Sound += Play;
        }

        private IEnumerator LoadClip(string name)
        {
            string path = Path.Combine(webFolder, "img", name + ".mp3");
            if (!File.Exists(path)) yield break;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.MPEG))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) sfx[name] = DownloadHandlerAudioClip.GetContent(req);
            }
        }

        private void Play(string name)
        {
            AudioClip c;
            if (audio != null && sfx.TryGetValue(name, out c)) audio.PlayOneShot(c, name == "milestone" ? 0.45f : 0.6f);
        }

        public void Tick()
        {
            if (sim.Screen == "playing")
            {
                acc += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
                int steps = 0;
                while (acc >= TickSeconds && steps < 5) { sim.Step(); acc -= TickSeconds; steps++; dirty = true; if (sim.Screen != "playing") break; }
                if (steps >= 5) acc = 0;
            }
            float now = Time.realtimeSinceStartup;
            if (dirty && now >= nextUpload)
            {
                renderer.Render(sim, Mathf.Clamp01(acc / TickSeconds));
                Visuals.Upload(texture, renderer.Pixels, NativeSim.W, NativeSim.H, true, scratch);
                nextUpload = now + UploadInterval;
                dirty = sim.Screen == "playing";
            }
        }

        public void Flap() { sim.Flap(); dirty = true; }
        public void Restart() { sim.StartRun(); dirty = true; }
        public void TogglePause() { sim.TogglePause(); dirty = true; }
        public void Back() { if (sim.Screen == "paused") TogglePause(); }

        // The native version has no menus: SELECT acts like FLAP, a click on the screen too.
        public void Navigate(int dx, int dy) { }
        public void Select() => Flap();
        public void StartButton()
        {
            if (sim.Screen == "paused") sim.TogglePause();
            else if (sim.Screen == "menu" || sim.Screen == "dead") sim.StartRun();
            dirty = true;
        }
        public void PointerDown(Vector2 uv) => Flap();
        public void PointerMove(Vector2 uv) { }
        public void PointerUp(Vector2 uv) { }
        public void Scroll(Vector2 uv, int delta) { }

        public void Dispose()
        {
            if (audio != null) UnityEngine.Object.Destroy(audio);
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
    }
}
