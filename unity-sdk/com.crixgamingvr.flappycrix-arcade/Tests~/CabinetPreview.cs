// Builds the cabinet model with the stand-in UnityEngine and writes it out as cabinet.obj (+ the
// pictures as .rgba) so render.py can draw it. Also checks the controls sit where they should.
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CrixArcade;
using CrixArcade.Core;
using UnityEngine;

class CabinetPreview
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
    static string F(float f) => f.ToString("0.#####", CultureInfo.InvariantCulture);

    static int Main(string[] a)
    {
        string outDir = a[0], res = a[1];
        Directory.CreateDirectory(outDir);
        ArcadeGame.HookResources();
        CoreResources.Load = n => File.ReadAllBytes(Path.Combine(res, n + ".bytes"));

        var go = new GameObject("Machine");
        var m = go.AddComponent<ArcadeMachine>();       // Awake builds the cabinet
        Check(m.Parts != null && m.Parts.Screen != null, "the cabinet builds");
        Check(m.Parts.Buttons.Count == 4 && m.Parts.Joystick != null && m.Parts.CoinSlot != null, "4 buttons, a joystick and a coin slot");

        // the screen faces the player (+z) and is upright-ish
        var f = m.Parts.ScreenFrame;
        Check(f.forward.z < -0.8f, "the screen faces the player (" + f.forward + ")");
        Check(f.up.y > 0.9f, "the screen's up is up");
        // the control panel is in front of the screen, at hand height
        foreach (var b in m.Parts.Buttons.Values)
            Check(b.transform.position.y > 0.9f && b.transform.position.y < 1.15f && b.transform.up.y > 0.9f, b.action + " button at hand height, facing up (" + b.transform.position + ")");
        var flap = m.Parts.Buttons[ArcadeAction.Flap].transform.position;
        var js = m.Parts.Joystick.transform.position;
        Check(js.x > flap.x, "the joystick is on the player's left of FLAP (player faces -z, so left = +x)");
        Check(m.Parts.CoinSlot.transform.position.z > 0.25f && m.Parts.CoinSlot.transform.position.y > 0.3f && m.Parts.CoinSlot.transform.position.y < 0.8f, "the coin slot is on the front, below the panel");

        // the screen maps 0..1 from the top-left as the player sees it
        var mi = typeof(ArcadeMachine).GetMethod("ScreenPoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Func<Vector3, Vector2> uvOf = p => { var args = new object[] { p, null, null }; mi.Invoke(m, args); return (Vector2)args[1]; };
        var size = m.Parts.ScreenSize;
        var topLeft = f.TransformPoint(new Vector3(-size.x / 2, size.y / 2, 0));     // frame right = player's right
        var c = uvOf(f.position); var tl = uvOf(topLeft);
        Check(Math.Abs(c.x - 0.5f) < 0.01f && Math.Abs(c.y - 0.5f) < 0.01f, "screen centre is uv 0.5, 0.5");
        Check(tl.x < 0.01f && tl.y < 0.01f, "the top-left corner as the player sees it is uv 0, 0 (" + tl.x + ", " + tl.y + ")");
        Check(f.TransformPoint(new Vector3(-size.x / 2, 0, 0)).x > f.position.x, "...and that corner is on the player's left (+x)");

        // export
        var obj = new StringBuilder("mtllib cabinet.mtl\n"); var mtl = new StringBuilder();
        int vbase = 1, matN = 0;
        var texIds = new Dictionary<Texture2D, string>();
        Action<Transform> walk = null;
        walk = t =>
        {
            var mf = t.gameObject.GetComponent<MeshFilter>(); var mr = t.gameObject.GetComponent<MeshRenderer>();
            if (mf != null && mr != null && mf.sharedMesh != null)
            {
                var mesh = mf.sharedMesh;
                foreach (var v in mesh.vertices) { var w = t.TransformPoint(v); obj.Append("v ").Append(F(w.x)).Append(' ').Append(F(w.y)).Append(' ').Append(F(w.z)).Append('\n'); }
                foreach (var uv in mesh.uv) obj.Append("vt ").Append(F(uv.x)).Append(' ').Append(F(uv.y)).Append('\n');
                foreach (var n in mesh.normals) { var w = t.TransformDirection(n); obj.Append("vn ").Append(F(w.x)).Append(' ').Append(F(w.y)).Append(' ').Append(F(w.z)).Append('\n'); }
                for (int s = 0; s < mesh.subs.Count; s++)
                {
                    var mat = mr.sharedMaterials[Math.Min(s, mr.sharedMaterials.Length - 1)];
                    string name = "m" + (matN++);
                    var col = mat.color;
                    mtl.Append("newmtl ").Append(name).Append("\nKd ").Append(F(col.r)).Append(' ').Append(F(col.g)).Append(' ').Append(F(col.b)).Append('\n');
                    mtl.Append("glow ").Append(mat.name.Contains("glow") || mat.name.Contains("screen") ? "1" : "0").Append('\n');
                    var tex = mat.mainTexture as Texture2D;
                    if (tex != null && tex.pixels != null)
                    {
                        string id;
                        if (!texIds.TryGetValue(tex, out id))
                        {
                            id = "tex" + texIds.Count + "_" + tex.width + "x" + tex.height + ".rgba";
                            var bytes = new byte[tex.pixels.Length * 4]; Buffer.BlockCopy(tex.pixels, 0, bytes, 0, bytes.Length);
                            File.WriteAllBytes(Path.Combine(outDir, id), bytes);
                            texIds[tex] = id;
                        }
                        mtl.Append("map_Kd ").Append(id).Append('\n');
                    }
                    if (mat.name.Contains("screen")) mtl.Append("map_Kd screen.rgba\n");
                    obj.Append("usemtl ").Append(name).Append('\n');
                    var tri = mesh.subs[s];
                    for (int i = 0; i < tri.Count; i += 3)
                    {
                        obj.Append('f');
                        for (int k = 0; k < 3; k++) { int vi = tri[i + k] + vbase; obj.Append(' ').Append(vi).Append('/').Append(vi).Append('/').Append(vi); }
                        obj.Append('\n');
                    }
                }
                vbase += mesh.vertices.Count;
            }
            foreach (Transform ch in t.children.ToArray()) walk(ch);
        };
        walk(go.transform);
        File.WriteAllText(Path.Combine(outDir, "cabinet.obj"), obj.ToString());
        File.WriteAllText(Path.Combine(outDir, "cabinet.mtl"), mtl.ToString());

        // a picture of the game for the screen
        m.Game.Dispose();
        System.Threading.Thread.Sleep(200);
        var app = m.Game.App;
        app.Render();
        var sb = new byte[app.C.Px.Length * 4]; Buffer.BlockCopy(app.C.Px, 0, sb, 0, sb.Length);
        File.WriteAllBytes(Path.Combine(outDir, "screen.rgba"), sb);
        Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
