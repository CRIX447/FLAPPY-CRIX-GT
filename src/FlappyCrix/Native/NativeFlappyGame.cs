// Native fallback: Flappy Crix rebuilt in Unity for when the embedded browser
// can't run. Mechanics are ported from flappycrix.html (CRIX447/crix-website):
// 400x600 playfield, 60 Hz fixed tick, PHYS constants, ellipse hitbox,
// pipe cadence, widening-to-narrowing gap and the capped gap step.
// Visuals use the site's own bird/coin sprites and colours from the package.

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
        // ---- constants from flappycrix.html ----
        const float W = 400f, H = 600f, TICK = 1f / 60f;
        const float Gravity = 0.52f, FallGrav = 0.25f, FlapV = -8.8f, MaxFall = 7.5f;
        const float HitRX = 0.40f, HitRY = 0.467f, BirdSize = 30f, BirdX = 100f;
        const int GraceTicks = 24, PipeTicks = 120, CoinTicks = 50;
        const float PipeWidth = 50f, Speed = 2.5f;

        public event Action<string> Log;
        public string ModeName => "Native Unity fallback";
        public bool IsReady => built;
        public bool HasFailed => false;
        public string FailureReason => null;
        public Texture PanelTexture => null;            // draws its own geometry
        public float Aspect => W / H;
        public string Screen { get; private set; } = "menu";
        public int Score { get; private set; }

        private class Pipe { public float x, top, bottom; public bool scored; public Transform topT, botT, topRim, botRim; }
        private class Coin { public float x, y; public bool taken; public Transform t; }

        private readonly Transform root;          // 1 local unit = 1 game pixel, origin at panel centre
        private readonly string webFolder;
        private readonly MonoBehaviour host;
        private readonly string bestFile;
        private bool built;

        private float birdY, birdPrevY, vel, acc;
        private int frame, grace, coins, best;
        private float lastTop;
        private bool paused;
        private readonly List<Pipe> pipes = new List<Pipe>();
        private readonly List<Coin> coinList = new List<Coin>();
        private readonly System.Random rng = new System.Random();

        private Transform bird, world;
        private Material pipeMat, coinMat;
        private TextMesh scoreText, titleText, infoText, coinText;
        private GameObject overlay;
        private AudioSource audio;
        private readonly Dictionary<string, AudioClip> sfx = new Dictionary<string, AudioClip>();

        public NativeFlappyGame(Transform panelSurface, string modFolder, MonoBehaviour host)
        {
            webFolder = Path.Combine(modFolder, "Web");
            bestFile = Path.Combine(modFolder, "BrowserData", "native-best.txt");
            this.host = host;

            var go = new GameObject("FlappyCrixNative");
            root = go.transform;
            root.SetParent(panelSurface, false);
            // panelSurface is a unit quad space (-0.5..0.5); map 400x600 px into it
            root.localScale = new Vector3(1f / W, 1f / H, 1f / W);
            root.localPosition = new Vector3(0, 0, -0.002f);
            Build();
        }

        // ---------------------------------------------------------------- scene

        private void Build()
        {
            try { int.TryParse(File.Exists(bestFile) ? File.ReadAllText(bestFile).Trim() : "0", out best); } catch { }

            // Sky (night theme from the site: #12203A -> #1B3352), ground #2A2A22
            var sky = Visuals.Gradient(1, 64, false, new[] { 0f, 1f }, new[] { Visuals.Hex("#1B3352"), Visuals.Hex("#12203A") });
            Place(Visuals.Quad("Sky", root, Visuals.Material(sky, Color.white, 0)).transform, W / 2, H / 2, W, H, 0f);
            Place(Visuals.Quad("Ground", root, Visuals.Material(null, Visuals.Hex("#2A2A22"), 1)).transform, W / 2, H - 20, W, 40, -1f);

            world = new GameObject("World").transform;
            world.SetParent(root, false);

            // Pipes: the site's 5-stop green gradient
            var pipeTex = Visuals.Gradient(64, 1, true, new[] { 0f, 0.18f, 0.42f, 0.72f, 1f },
                new[] { Visuals.Hex("#0B8F56"), Visuals.Hex("#00CC7A"), Visuals.Hex("#4DE8A8"), Visuals.Hex("#00CC7A"), Visuals.Hex("#076B41") });
            pipeMat = Visuals.Material(pipeTex, Color.white, 2);

            var coinTex = Visuals.LoadPng(Path.Combine(webFolder, "img", "coin-still.png"));
            coinMat = Visuals.Material(coinTex, coinTex != null ? Color.white : Visuals.Hex("#FFD700"), 3);

            var birdTex = Visuals.LoadPng(Path.Combine(webFolder, "img", "bird.png"));
            bird = Visuals.Quad("Bird", root, Visuals.Material(birdTex, birdTex != null ? Color.white : Visuals.Hex("#FF6B35"), 5)).transform;

            scoreText = Visuals.Text("Score", root, 100, Color.white, TextAnchor.UpperCenter);
            Place(scoreText.transform, W / 2, 24, 1, 1, -8f); scoreText.characterSize = 4.8f;
            coinText = Visuals.Text("Coins", root, 100, Visuals.Hex("#FFD700"), TextAnchor.UpperRight);
            Place(coinText.transform, W - 14, 14, 1, 1, -8f); coinText.characterSize = 2.2f;

            overlay = new GameObject("Overlay");
            overlay.transform.SetParent(root, false);
            Place(Visuals.Quad("Dim", overlay.transform, Visuals.Material(null, new Color(0, 0, 0, 0.55f), 6)).transform, W / 2, H / 2, W, H, -6f);
            titleText = Visuals.Text("Title", overlay.transform, 100, Visuals.Hex("#FF6B35"), TextAnchor.MiddleCenter);
            Place(titleText.transform, W / 2, 230, 1, 1, -9f); titleText.characterSize = 4.4f;
            infoText = Visuals.Text("Info", overlay.transform, 100, Color.white, TextAnchor.MiddleCenter);
            Place(infoText.transform, W / 2, 340, 1, 1, -9f); infoText.characterSize = 2.0f;

            audio = root.gameObject.AddComponent<AudioSource>();
            audio.spatialBlend = 0.6f;
            audio.playOnAwake = false;
            foreach (var n in new[] { "jump", "death", "coin", "select", "milestone" })
                host.StartCoroutine(LoadClip(n));

            ResetRun();
            ShowMenu();
            built = true;
            Log?.Invoke("Native Flappy Crix ready (bird sprite " + (birdTex != null ? "from package" : "missing - using a placeholder") + ").");
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

        private void Play(string name, float vol = 0.7f)
        {
            AudioClip c;
            if (audio != null && sfx.TryGetValue(name, out c)) audio.PlayOneShot(c, vol);
        }

        /// <summary>Game coordinates (top-left origin, px) to local root space.</summary>
        private static void Place(Transform t, float x, float y, float w, float h, float z)
        {
            t.localPosition = new Vector3(x - W / 2, H / 2 - y, z);
            t.localScale = new Vector3(w, h, 1);
        }

        // ---------------------------------------------------------------- game rules

        private void ResetRun()
        {
            foreach (var p in pipes) { UnityEngine.Object.Destroy(p.topT.gameObject); UnityEngine.Object.Destroy(p.botT.gameObject); UnityEngine.Object.Destroy(p.topRim.gameObject); UnityEngine.Object.Destroy(p.botRim.gameObject); }
            foreach (var c in coinList) UnityEngine.Object.Destroy(c.t.gameObject);
            pipes.Clear(); coinList.Clear();
            birdY = birdPrevY = 300; vel = 0; frame = 0; Score = 0; grace = GraceTicks; lastTop = 0; acc = 0;
        }

        private float Gap() => Mathf.Max(182f, 215f - Score * 1.2f);

        private float NextTop(float gap)
        {
            float top = (float)rng.NextDouble() * (H - gap - 150) + 70;
            if (lastTop > 0) top = Mathf.Clamp(top, lastTop - 150, lastTop + 150);
            return Mathf.Clamp(top, 70, H - gap - 80);
        }

        private static bool HitsRectE(float cx, float cy, float erx, float ery, float rx, float ry, float rw, float rh)
        {
            float sx = 1 / erx, sy = 1 / ery;
            float nx = Mathf.Max(rx * sx, Mathf.Min(cx * sx, (rx + rw) * sx));
            float ny = Mathf.Max(ry * sy, Mathf.Min(cy * sy, (ry + rh) * sy));
            float dx = cx * sx - nx, dy = cy * sy - ny;
            return dx * dx + dy * dy < 1;
        }

        private void Step()
        {
            birdPrevY = birdY;
            if (grace > 0) grace--;
            vel += vel < 0 ? Gravity : FallGrav;
            if (vel > MaxFall) vel = MaxFall;
            birdY += vel;
            if (birdY < BirdSize / 2) { birdY = BirdSize / 2; if (vel < 0) vel = 0; }   // no ceiling death, as on the site
            if (birdY > H - 10)
            {
                if (grace > 0) { birdY = H - 10; vel = -Mathf.Abs(vel) * 0.5f; }
                else { Die(); return; }
            }

            if (frame % PipeTicks == 0)
            {
                float gap = Gap(), top = NextTop(gap);
                lastTop = top;
                var p = new Pipe { x = W, top = top, bottom = top + gap };
                p.topT = Visuals.Quad("PipeTop", world, pipeMat).transform;
                p.botT = Visuals.Quad("PipeBottom", world, pipeMat).transform;
                p.topRim = Visuals.Quad("RimTop", world, pipeMat).transform;
                p.botRim = Visuals.Quad("RimBottom", world, pipeMat).transform;
                pipes.Add(p);
            }
            if (frame % CoinTicks == 0)
            {
                float y = pipes.Count > 0 ? (lastTop + Gap() / 2) + ((float)rng.NextDouble() - 0.5f) * Gap() * 0.6f : (float)rng.NextDouble() * (H - 200) + 100;
                y = Mathf.Clamp(y, 60, H - 90) - 10;
                var c = new Coin { x = W, y = y };
                c.t = Visuals.Quad("Coin", world, coinMat).transform;
                coinList.Add(c);
            }

            float rx = BirdSize * HitRX, ry = BirdSize * HitRY;
            for (int i = 0; i < pipes.Count; i++)
            {
                var p = pipes[i];
                p.x -= Speed;
                if (grace <= 0 && (HitsRectE(BirdX, birdY, rx, ry, p.x, -1000, PipeWidth, p.top + 1000) ||
                                   HitsRectE(BirdX, birdY, rx, ry, p.x, p.bottom, PipeWidth, H - p.bottom + 20)))
                { Die(); return; }
                if (p.x + PipeWidth < BirdX && !p.scored)
                {
                    p.scored = true; Score++;
                    if (Score % 10 == 0) Play("milestone", 0.45f);
                }
                if (p.x + PipeWidth < 0)
                {
                    UnityEngine.Object.Destroy(p.topT.gameObject); UnityEngine.Object.Destroy(p.botT.gameObject);
                    UnityEngine.Object.Destroy(p.topRim.gameObject); UnityEngine.Object.Destroy(p.botRim.gameObject);
                    pipes.RemoveAt(i--);
                }
            }
            for (int i = 0; i < coinList.Count; i++)
            {
                var c = coinList[i];
                c.x -= Speed;
                if (!c.taken && Mathf.Abs(c.x + 10 - BirdX) < 22 && Mathf.Abs(c.y + 10 - birdY) < 24)
                { c.taken = true; coins++; c.t.gameObject.SetActive(false); Play("coin", 0.5f); }
                if (c.x + 20 < 0) { UnityEngine.Object.Destroy(c.t.gameObject); coinList.RemoveAt(i--); }
            }
            frame++;
        }

        private void Die()
        {
            Screen = "dead";
            Play("death");
            if (Score > best)
            {
                best = Score;
                try { Directory.CreateDirectory(Path.GetDirectoryName(bestFile)); File.WriteAllText(bestFile, best.ToString()); } catch { }
            }
            overlay.SetActive(true);
            titleText.text = "GAME OVER";
            infoText.text = "Score " + Score + "     Best " + best + "\n\nFlap to retry";
        }

        private void ShowMenu()
        {
            Screen = "menu";
            overlay.SetActive(true);
            titleText.text = "FLAPPY CRIX";
            infoText.text = "Flap to start\n\nX / A, thumbstick up,\nthe joystick, or SPACE\n\nBest " + best;
        }

        private void StartRun()
        {
            ResetRun();
            Screen = "playing";
            paused = false;
            overlay.SetActive(false);
            Play("select", 0.5f);
        }

        // ---------------------------------------------------------------- IFlappyGame

        public void Flap()
        {
            if (Screen == "playing" && !paused) { vel = FlapV; Play("jump", 0.6f); }
            else if (Screen == "menu" || Screen == "dead") StartRun();
        }

        public void Restart() => StartRun();

        public void TogglePause()
        {
            if (Screen != "playing" && Screen != "paused") return;
            paused = !paused;
            Screen = paused ? "paused" : "playing";
            overlay.SetActive(paused);
            if (paused) { titleText.text = "PAUSED"; infoText.text = "Press B to resume"; }
        }

        public void Back() { if (paused) TogglePause(); }

        // The native version has no menus to navigate: SELECT acts like FLAP.
        public void Navigate(int dx, int dy) { }
        public void Select() => Flap();
        public void StartButton()
        {
            if (paused) TogglePause();
            else if (Screen == "menu" || Screen == "dead") StartRun();
        }

        // The native game has no menus to click: a click on the panel is a flap.
        public void PointerDown(Vector2 uv) => Flap();
        public void PointerMove(Vector2 uv) { }
        public void PointerUp(Vector2 uv) { }
        public void Scroll(Vector2 uv, int delta) { }

        public void Tick()
        {
            if (!built) return;
            if (Screen == "playing" && !paused)
            {
                acc += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
                int steps = 0;
                while (acc >= TICK && steps < 5) { Step(); acc -= TICK; steps++; if (Screen != "playing") break; }
                if (steps >= 5) acc = 0;
            }
            Render(Mathf.Clamp01(acc / TICK));
        }

        private void Render(float alpha)
        {
            float y = Mathf.Lerp(birdPrevY, birdY, Screen == "playing" ? alpha : 1f);
            Place(bird, BirdX, y, BirdSize, BirdSize, -5f);
            bird.localRotation = Quaternion.Euler(0, 0, -Mathf.Clamp(vel * 4f, -25f, 70f));
            float shift = Screen == "playing" && !paused ? Speed * alpha : 0f;
            foreach (var p in pipes)
            {
                float x = p.x - shift;
                float rimH = Mathf.Min(16, PipeWidth * 0.34f), over = PipeWidth * 0.09f;
                Place(p.topT, x + PipeWidth / 2, p.top / 2, PipeWidth, p.top, -2f);
                Place(p.botT, x + PipeWidth / 2, (p.bottom + H - 40) / 2, PipeWidth, H - 40 - p.bottom, -2f);
                Place(p.topRim, x + PipeWidth / 2, p.top - rimH / 2, PipeWidth + over * 2, rimH, -2.5f);
                Place(p.botRim, x + PipeWidth / 2, p.bottom + rimH / 2, PipeWidth + over * 2, rimH, -2.5f);
            }
            foreach (var c in coinList)
            {
                float spin = Mathf.Abs(Mathf.Cos(frame * 0.08f + c.x * 0.05f));
                Place(c.t, c.x - shift + 10, c.y + 10, 20 * Mathf.Max(0.18f, spin), 20, -3f);
            }
            scoreText.text = Screen == "menu" ? "" : Score.ToString();
            coinText.text = coins > 0 ? coins + " coins" : "";
        }

        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
