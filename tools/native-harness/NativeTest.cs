// Dev-only: runs the native fallback's rules + renderer outside Unity and dumps frames.
//   mcs -out:nativetest.exe ../../src/FlappyCrix/PixelFont.cs ../../src/FlappyCrix/Native/NativeSim.cs ../../src/FlappyCrix/Native/NativeRenderer.cs NativeTest.cs
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
using System; using System.IO; using System.Diagnostics; using FlappyCrix.Native;
class NativeTest {
  static uint[] Load(string dir, string name, out int w, out int h) {
    var sz = File.ReadAllText(Path.Combine(dir, name + ".size")).Split(' '); w = int.Parse(sz[0]); h = int.Parse(sz[1]);
    var b = File.ReadAllBytes(Path.Combine(dir, name + ".rgba")); var px = new uint[w * h];
    Buffer.BlockCopy(b, 0, px, 0, b.Length); return px; }
  static void Dump(NativeRenderer r, string path) { var b = new byte[r.Pixels.Length * 4]; Buffer.BlockCopy(r.Pixels, 0, b, 0, b.Length); File.WriteAllBytes(path, b); }
  static int Main(string[] a) {
    string dir = a[0]; int bw, bh, cw, ch;
    var bird = Load(dir, "bird", out bw, out bh); var coin = Load(dir, "coin-still", out cw, out ch);
    var sim = new NativeSim(42); var r = new NativeRenderer(); r.SetSprites(bird, bw, bh, coin, cw, ch);
    int sounds = 0; sim.Sound += s => sounds++;
    r.Render(sim, 0); Dump(r, Path.Combine(dir, "menu.raw"));
    sim.Flap();                                     // FLAP on the menu starts a run
    int ok = sim.Screen == "playing" ? 1 : 0;
    var sw = Stopwatch.StartNew(); int renders = 0;
    for (int t = 0; t < 60 * 12 && sim.Screen == "playing"; t++) {     // 12 s autopilot
      NativeSim.Pipe next = null; foreach (var p in sim.Pipes) if (p.X + NativeSim.PipeWidth > NativeSim.BirdX - 20) { next = p; break; }
      float target = next != null ? (next.Top + next.Bottom) / 2 + 18 : 300;
      if (sim.BirdY > target && sim.Velocity > -1) sim.Flap();
      sim.Step();
      if (t % 2 == 0) { r.Render(sim, 0.5f); renders++; }
      if (t == 60 * 8) Dump(r, Path.Combine(dir, "playing.raw"));
    }
    double ms = sw.Elapsed.TotalMilliseconds / Math.Max(1, renders);
    int scored = sim.Score;
    sim.TogglePause(); r.Render(sim, 0); Dump(r, Path.Combine(dir, "paused.raw")); sim.TogglePause();
    for (int t = 0; t < 600 && sim.Screen == "playing"; t++) sim.Step();     // stop flapping -> fall -> game over
    r.Render(sim, 0); Dump(r, Path.Combine(dir, "dead.raw"));
    Console.WriteLine((ok == 1 ? "PASS" : "FAIL") + " FLAP on the menu starts a run");
    Console.WriteLine((scored >= 3 ? "PASS" : "FAIL") + " pipes score while flying (" + scored + " in 12 s)");
    Console.WriteLine((sim.Screen == "dead" ? "PASS" : "FAIL") + " falling ends the run (" + sim.Screen + ", best " + sim.Best + ")");
    Console.WriteLine((sounds > 5 ? "PASS" : "FAIL") + " sound events raised (" + sounds + ")");
    Console.WriteLine((ms < 4 ? "PASS" : "FAIL") + " render time " + ms.ToString("0.00") + " ms per frame (Mono, 400x600)");
    return 0; } }
