// Small mesh helpers for the cabinet: extrude a side profile, flat quads, cylinders, spheres.
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using System.Collections.Generic;
using UnityEngine;

namespace CrixArcade
{
    public sealed class CabinetMesh
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<List<int>> Sub = new List<List<int>>();

        public CabinetMesh(int submeshes = 1) { for (int i = 0; i < submeshes; i++) Sub.Add(new List<int>()); }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V);
            m.SetNormals(N);
            m.SetUVs(0, UV);
            m.subMeshCount = Sub.Count;
            for (int i = 0; i < Sub.Count; i++) m.SetTriangles(Sub[i], i);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        int Vert(Vector3 p, Vector3 n, Vector2 uv) { V.Add(p); N.Add(n); UV.Add(uv); return V.Count - 1; }

        /// <summary>
        /// A quad a-b-c-d facing Cross(b - a, d - a); a-d is "up" for its picture. Unity is left-handed,
        /// so seen from the front a is bottom-RIGHT and b bottom-left. uv v is flipped for top-down pictures.
        /// </summary>
        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool flipV = true)
        {
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            float v0 = flipV ? 1 : 0, v1 = flipV ? 0 : 1;
            int i0 = Vert(a, n, new Vector2(1, v0)), i1 = Vert(b, n, new Vector2(0, v0)), i2 = Vert(c, n, new Vector2(0, v1)), i3 = Vert(d, n, new Vector2(1, v1));
            Sub[sub].AddRange(new[] { i0, i1, i2, i0, i2, i3 });
        }

        /// <summary>A box from min to max (all faces outward).</summary>
        public void Box(int sub, Vector3 min, Vector3 max)
        {
            Vector3 a = min, b = max;
            Quad(sub, new Vector3(a.x, a.y, b.z), new Vector3(b.x, a.y, b.z), new Vector3(b.x, b.y, b.z), new Vector3(a.x, b.y, b.z), false); // +z
            Quad(sub, new Vector3(b.x, a.y, a.z), new Vector3(a.x, a.y, a.z), new Vector3(a.x, b.y, a.z), new Vector3(b.x, b.y, a.z), false); // -z
            Quad(sub, new Vector3(b.x, a.y, b.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, b.y, a.z), new Vector3(b.x, b.y, b.z), false); // +x
            Quad(sub, new Vector3(a.x, a.y, a.z), new Vector3(a.x, a.y, b.z), new Vector3(a.x, b.y, b.z), new Vector3(a.x, b.y, a.z), false); // -x
            Quad(sub, new Vector3(a.x, b.y, b.z), new Vector3(b.x, b.y, b.z), new Vector3(b.x, b.y, a.z), new Vector3(a.x, b.y, a.z), false); // +y
            Quad(sub, new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, a.y, b.z), new Vector3(a.x, a.y, b.z), false); // -y
        }

        /// <summary>
        /// Extrudes a side profile (points are (z, y), counter-clockwise seen from +x) between x0 and x1.
        /// The two flat sides go in capSub (uv from the profile's bounds, for side art), the rim in rimSub.
        /// </summary>
        public void Extrude(IList<Vector2> profile, float x0, float x1, int capSub, int rimSub, bool flipV = true)
        {
            float zMin = float.MaxValue, zMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
            foreach (var p in profile) { zMin = Mathf.Min(zMin, p.x); zMax = Mathf.Max(zMax, p.x); yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y); }
            var tris = Triangulate(profile);
            foreach (float x in new[] { x0, x1 })
            {
                bool plus = x == x1;
                var n = plus ? Vector3.right : Vector3.left;
                int baseI = V.Count;
                foreach (var p in profile)
                {
                    // the art reads the right way round on both sides (not mirrored)
                    float u = (p.x - zMin) / (zMax - zMin);
                    if (!plus) u = 1 - u;
                    float v = (p.y - yMin) / (yMax - yMin);
                    Vert(new Vector3(x, p.y, p.x), n, new Vector2(u, flipV ? 1 - v : v));
                }
                for (int t = 0; t < tris.Count; t += 3)
                {
                    // seen from +x, (z, y) counter-clockwise needs reversing for Unity's clockwise front faces
                    if (plus) Sub[capSub].AddRange(new[] { baseI + tris[t], baseI + tris[t + 2], baseI + tris[t + 1] });
                    else Sub[capSub].AddRange(new[] { baseI + tris[t], baseI + tris[t + 1], baseI + tris[t + 2] });
                }
            }
            for (int i = 0; i < profile.Count; i++)
            {
                var p = profile[i]; var q = profile[(i + 1) % profile.Count];
                // rim faces point outwards from the profile
                Quad(rimSub, new Vector3(x0, p.y, p.x), new Vector3(x1, p.y, p.x), new Vector3(x1, q.y, q.x), new Vector3(x0, q.y, q.x), false);
            }
        }

        /// <summary>A cylinder standing on `bottom` along `up`, with a top cap.</summary>
        public void Cylinder(int sub, Vector3 bottom, Vector3 up, float radius, float height, int seg = 20, int capSub = -1)
        {
            if (capSub < 0) capSub = sub;
            up.Normalize();
            Vector3 a = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.forward).normalized, b = Vector3.Cross(up, a);
            Vector3 top = bottom + up * height;
            for (int i = 0; i < seg; i++)
            {
                float t0 = i * Mathf.PI * 2 / seg, t1 = (i + 1) * Mathf.PI * 2 / seg;
                Vector3 d0 = a * Mathf.Cos(t0) + b * Mathf.Sin(t0), d1 = a * Mathf.Cos(t1) + b * Mathf.Sin(t1);
                int s = V.Count;
                Vert(bottom + d0 * radius, d0, new Vector2(i / (float)seg, 0)); Vert(bottom + d1 * radius, d1, new Vector2((i + 1) / (float)seg, 0));
                Vert(top + d1 * radius, d1, new Vector2((i + 1) / (float)seg, 1)); Vert(top + d0 * radius, d0, new Vector2(i / (float)seg, 1));
                Sub[sub].AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                int c = V.Count;
                Vert(top, up, new Vector2(0.5f, 0.5f));
                Vert(top + d0 * radius, up, new Vector2(0.5f + Mathf.Cos(t0) / 2, 0.5f + Mathf.Sin(t0) / 2));
                Vert(top + d1 * radius, up, new Vector2(0.5f + Mathf.Cos(t1) / 2, 0.5f + Mathf.Sin(t1) / 2));
                Sub[capSub].AddRange(new[] { c, c + 1, c + 2 });
            }
        }

        public void Sphere(int sub, Vector3 centre, float r, int seg = 16)
        {
            int rings = seg / 2, s = V.Count;
            for (int y = 0; y <= rings; y++)
                for (int x = 0; x <= seg; x++)
                {
                    float th = y * Mathf.PI / rings, ph = x * Mathf.PI * 2 / seg;
                    var n = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    Vert(centre + n * r, n, new Vector2(x / (float)seg, 1 - y / (float)rings));
                }
            for (int y = 0; y < rings; y++)
                for (int x = 0; x < seg; x++)
                {
                    int i = s + y * (seg + 1) + x, j = i + seg + 1;
                    Sub[sub].AddRange(new[] { i, i + 1, j, i + 1, j + 1, j });
                }
        }

        /// <summary>Ear clipping for a simple polygon (counter-clockwise).</summary>
        public static List<int> Triangulate(IList<Vector2> p)
        {
            var res = new List<int>();
            var idx = new List<int>();
            for (int i = 0; i < p.Count; i++) idx.Add(i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 1000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = p[ia], b = p[ib], c = p[ic];
                    if (Cross(b - a, c - b) <= 0) continue;                // reflex corner
                    bool inside = false;
                    foreach (int k in idx)
                        if (k != ia && k != ib && k != ic && InTri(p[k], a, b, c)) { inside = true; break; }
                    if (inside) continue;
                    res.Add(ia); res.Add(ib); res.Add(ic);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { res.Add(idx[0]); res.Add(idx[1]); res.Add(idx[2]); }
            return res;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Cross(b - a, p - a) >= 0 && Cross(c - b, p - b) >= 0 && Cross(a - c, p - c) >= 0;
    }
}
