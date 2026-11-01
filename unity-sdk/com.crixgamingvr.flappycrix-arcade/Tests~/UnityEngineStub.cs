// A small stand-in for UnityEngine, so the SDK's Unity scripts can be compiled (and the cabinet
// model built and exported) without Unity. Same names and signatures as Unity 2021-2022 for
// everything the SDK uses; the maths, transforms and meshes really work, the rest does nothing.
// Only for tests (Unity ignores folders ending in ~).
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).
#pragma warning disable 67, 169, 414, 649, 660, 661, 626
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
        public static void DontDestroyOnLoad(Object target) { }
        public int GetInstanceID() { return GetHashCode(); }
        public static T FindObjectOfType<T>() where T : Object { return null; }
        public static implicit operator bool(Object o) { return !ReferenceEquals(o, null); }
        public static bool operator ==(Object x, Object y) { return ReferenceEquals(x, y); }
        public static bool operator !=(Object x, Object y) { return !ReferenceEquals(x, y); }
        public override bool Equals(object o) { return ReferenceEquals(this, o); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    [Flags] public enum HideFlags { None = 0, DontSave = 52, HideAndDontSave = 61 }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector2 normalized { get { float m = magnitude; return m > 1e-6f ? new Vector2(x / m, y / m) : default(Vector2); } }
        public void Normalize() { this = normalized; }
        public static Vector2 zero => default(Vector2);
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public override bool Equals(object o) => o is Vector2 && (Vector2)o == this;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode();
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default(Vector3);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 left => new Vector3(-1, 0, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized { get { float m = magnitude; return m > 1e-6f ? this / m : default(Vector3); } }
        public void Normalize() { this = normalized; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => a * d;
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        public static Quaternion AngleAxis(float deg, Vector3 axis)
        {
            axis.Normalize(); float h = deg * Mathf.Deg2Rad / 2, s = (float)Math.Sin(h);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(h));
        }
        // Unity order: Z, then X, then Y
        public static Quaternion Euler(float x, float y, float z) => AngleAxis(y, Vector3.up) * AngleAxis(x, Vector3.right) * AngleAxis(z, Vector3.forward);
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            Vector3 f = forward.normalized, r = Vector3.Cross(up, f).normalized, u = Vector3.Cross(f, r);
            float m00 = r.x, m01 = u.x, m02 = f.x, m10 = r.y, m11 = u.y, m12 = f.y, m20 = r.z, m21 = u.z, m22 = f.z;
            float tr = m00 + m11 + m22; Quaternion q;
            if (tr > 0) { float s = (float)Math.Sqrt(tr + 1) * 2; q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s); }
            else if (m00 > m11 && m00 > m22) { float s = (float)Math.Sqrt(1 + m00 - m11 - m22) * 2; q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s); }
            else if (m11 > m22) { float s = (float)Math.Sqrt(1 + m11 - m00 - m22) * 2; q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s); }
            else { float s = (float)Math.Sqrt(1 + m22 - m00 - m11) * 2; q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s); }
            return q;
        }
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y, a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w, a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var u = new Vector3(q.x, q.y, q.z);
            return u * (2 * Vector3.Dot(u, v)) + v * (q.w * q.w - Vector3.Dot(u, u)) + Vector3.Cross(u, v) * (2 * q.w);
        }
        public Quaternion Inverse() => new Quaternion(-x, -y, -z, w);
        public static Quaternion Inverse(Quaternion q) => q.Inverse();
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) : this(r, g, b, 1) { }
        public static Color white => new Color(1, 1, 1);
        public static Color black => new Color(0, 0, 0);
        public static Color operator *(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a * k);
    }
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        static byte B(float f) => (byte)Math.Round(Mathf.Clamp01(f) * 255);
        public static implicit operator Color32(Color c) => new Color32(B(c.r), B(c.g), B(c.b), B(c.a));
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
    }
    public struct Ray
    {
        public Vector3 origin, direction;
        public Ray(Vector3 o, Vector3 d) { origin = o; direction = d.normalized; }
        public Vector3 GetPoint(float d) => origin + direction * d;
    }
    public struct Plane
    {
        Vector3 n; float d;
        public Plane(Vector3 inNormal, Vector3 inPoint) { n = inNormal.normalized; d = -Vector3.Dot(n, inPoint); }
        public bool Raycast(Ray ray, out float enter)
        {
            float vd = Vector3.Dot(ray.direction, n), vo = -Vector3.Dot(ray.origin, n) - d;
            if (Math.Abs(vd) < 1e-6f) { enter = 0; return false; }
            enter = vo / vd; return enter > 0;
        }
    }
    public struct Mathf
    {
        public const float Deg2Rad = 0.0174532924f, Rad2Deg = 57.29578f, PI = 3.14159274f;
        public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Abs(int f) => Math.Abs(f);
        public static float Abs(float f) => Math.Abs(f);
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    }
    public class Time
    {
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        public static float unscaledTime => (float)clock.Elapsed.TotalSeconds;
        public static double unscaledTimeAsDouble => clock.Elapsed.TotalSeconds;
        public static float unscaledDeltaTime => 1 / 60f;
        public static float deltaTime => 1 / 60f;
    }
    public class Application { public static string persistentDataPath => System.IO.Path.GetTempPath(); }
    public static class Debug
    {
        public static void Log(object m) { Console.WriteLine(m); }
        public static void LogWarning(object m) { Console.WriteLine("warning: " + m); }
        public static void LogError(object m) { Console.WriteLine("error: " + m); }
    }
    public enum KeyCode { None = 0, Backspace = 8, Tab = 9, Return = 13, Escape = 27, Space = 32, C = 99, UpArrow = 273, DownArrow = 274, RightArrow = 275, LeftArrow = 276 }
    public static class Input
    {
        public static bool GetKeyDown(KeyCode k) => false;
        public static bool GetMouseButtonDown(int b) => false;
        public static Vector3 mousePosition => default(Vector3);
    }

    public class Component : Object
    {
        internal GameObject go;
        public Transform transform => go.transform;
        public GameObject gameObject => go;
        public T GetComponent<T>() where T : Component => go.GetComponent<T>();
    }
    public class Behaviour : Component { public bool enabled { get; set; } = true; public bool isActiveAndEnabled => enabled; }
    public class YieldInstruction { }
    public sealed class Coroutine : YieldInstruction { }
    public class MonoBehaviour : Behaviour { public Coroutine StartCoroutine(IEnumerator r) => null; }

    public class Transform : Component, IEnumerable
    {
        Transform parentT;
        public readonly List<Transform> children = new List<Transform>();
        public Vector3 localPosition { get; set; }
        public Quaternion localRotation { get; set; } = Quaternion.identity;
        public Vector3 localScale { get; set; } = Vector3.one;
        public Transform parent { get => parentT; set => SetParent(value, true); }
        public void SetParent(Transform p, bool worldPositionStays)
        {
            if (parentT != null) parentT.children.Remove(this);
            parentT = p;
            if (p != null) p.children.Add(this);
        }
        public Vector3 TransformPoint(Vector3 p)
        {
            var v = localRotation * Vector3.Scale(p, localScale) + localPosition;
            return parentT != null ? parentT.TransformPoint(v) : v;
        }
        public Vector3 TransformDirection(Vector3 d) { var v = localRotation * d; return parentT != null ? parentT.TransformDirection(v) : v; }
        public Vector3 InverseTransformPoint(Vector3 p)
        {
            if (parentT != null) p = parentT.InverseTransformPoint(p);
            var v = localRotation.Inverse() * (p - localPosition);
            return new Vector3(v.x / localScale.x, v.y / localScale.y, v.z / localScale.z);
        }
        public Vector3 InverseTransformDirection(Vector3 d)
        {
            if (parentT != null) d = parentT.InverseTransformDirection(d);
            return localRotation.Inverse() * d;
        }
        public Quaternion rotation => parentT != null ? parentT.rotation * localRotation : localRotation;
        public Vector3 position { get => TransformPoint(Vector3.zero) - (parentT == null ? Vector3.zero : Vector3.zero); set { localPosition = parentT != null ? parentT.InverseTransformPoint(value) : value; } }
        public Vector3 forward => rotation * Vector3.forward;
        public Vector3 up => rotation * Vector3.up;
        public Vector3 right => rotation * Vector3.right;
        public IEnumerator GetEnumerator() => children.GetEnumerator();
        Vector3 TransformPointLocal(Vector3 p) => p;
    }

    public sealed class GameObject : Object
    {
        readonly List<Component> comps = new List<Component>();
        public readonly Transform transformT;
        public GameObject(string name) { this.name = name; transformT = new Transform(); transformT.go = this; comps.Add(transformT); }
        public GameObject() : this("GameObject") { }
        public Transform transform => transformT;
        public T AddComponent<T>() where T : Component
        {
            var c = (T)Activator.CreateInstance(typeof(T), true);
            c.go = this; comps.Add(c);
            var awake = typeof(T).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (awake != null) awake.Invoke(c, null);
            return c;
        }
        public T GetComponent<T>() where T : Component { foreach (var c in comps) if (c is T) return (T)c; return null; }
        public List<Component> Components => comps;
    }

    public sealed class Mesh : Object
    {
        public List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
        public List<Vector2> uv = new List<Vector2>();
        public List<List<int>> subs = new List<List<int>>();
        public Rendering.IndexFormat indexFormat { get; set; }
        public int subMeshCount { get => subs.Count; set { while (subs.Count < value) subs.Add(new List<int>()); } }
        public void SetVertices(List<Vector3> v) { vertices = new List<Vector3>(v); }
        public void SetNormals(List<Vector3> v) { normals = new List<Vector3>(v); }
        public void SetUVs(int ch, List<Vector2> v) { uv = new List<Vector2>(v); }
        public void SetTriangles(List<int> t, int sub) { subs[sub] = new List<int>(t); }
        public void RecalculateBounds() { }
        public void RecalculateTangents() { }
    }
    public sealed class MeshFilter : Component { public Mesh sharedMesh { get; set; } }
    public class Renderer : Component
    {
        public bool enabled { get; set; } = true;
        public Material sharedMaterial { get => sharedMaterials.Length > 0 ? sharedMaterials[0] : null; set => sharedMaterials = new[] { value }; }
        public Material[] sharedMaterials { get; set; } = new Material[0];
        public Material material { get => sharedMaterial; set => sharedMaterial = value; }
        public Rendering.ShadowCastingMode shadowCastingMode { get; set; }
    }
    public class MeshRenderer : Renderer { }
    public class Collider : Component { }
    public sealed class BoxCollider : Collider { public Vector3 center { get; set; } public Vector3 size { get; set; } }
    public sealed class Camera : Behaviour
    {
        public static Camera main => null;
        public Ray ScreenPointToRay(Vector3 p) => default(Ray);
    }
    public sealed class Shader : Object { public static Shader Find(string n) => new Shader { name = n }; }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1 }
    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureFormat { RGBA32 = 4 }
    public class Texture : Object
    {
        public TextureWrapMode wrapMode { get; set; }
        public FilterMode filterMode { get; set; }
        public int anisoLevel { get; set; }
    }
    public sealed class Texture2D : Texture
    {
        public int width, height; public uint[] pixels;
        public Texture2D(int w, int h, TextureFormat f, bool mip) { width = w; height = h; }
        public void SetPixelData<T>(T[] data, int mip, int start = 0) { pixels = (uint[])(object)((T[])data.Clone()); }
        public void Apply(bool updateMipmaps) { }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
    }
    public class Material : Object
    {
        public Material(Shader s) { shader = s; }
        public Material(Material m) { shader = m.shader; }
        public Shader shader { get; set; }
        public Texture mainTexture { get; set; }
        public Color color { get; set; }
        public Vector2 mainTextureScale { get; set; }
        public Vector2 mainTextureOffset { get; set; }
        public bool HasProperty(string n) => false;
        public void SetTexture(string n, Texture t) { }
        public void SetColor(string n, Color c) { }
        public void SetFloat(string n, float f) { }
    }
    public class TextAsset : Object { public byte[] bytes => null; }
    public sealed class AudioClip : Object { }
    public enum AudioRolloffMode { Logarithmic = 0, Linear = 1 }
    public sealed class AudioSource : Behaviour
    {
        public float spatialBlend { get; set; }
        public bool playOnAwake { get; set; }
        public AudioClip clip { get; set; }
        public bool loop { get; set; }
        public float volume { get; set; }
        public float minDistance { get; set; }
        public float maxDistance { get; set; }
        public float dopplerLevel { get; set; }
        public AudioRolloffMode rolloffMode { get; set; }
        public bool isPlaying => false;
        public void Play() { }
        public void Pause() { }
        public void UnPause() { }
        public void Stop() { }
        public void PlayOneShot(AudioClip c, float v) { }
    }
    public static class Resources { public static T Load<T>(string path) where T : Object => null; }

    // attributes used in the inspector
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string t) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string t) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string m) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
}

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off = 0, On = 1 }
    public enum IndexFormat { UInt16 = 0, UInt32 = 1 }
    public static class GraphicsSettings { public static Object currentRenderPipeline => null; }
}

namespace UnityEngine.XR
{
    public enum XRNode { LeftHand = 4, RightHand = 5 }
    public struct InputDevice
    {
        public bool isValid => false;
        public bool SendHapticImpulse(uint channel, float amplitude, float duration) => false;
    }
    public static class InputDevices { public static InputDevice GetDeviceAtXRNode(XRNode n) => default(InputDevice); }
}
