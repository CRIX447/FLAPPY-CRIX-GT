// Reference-only stand-in for UnityEngine.dll, used to build FlappyCrix.dll where the game's
// Managed folder isn't available. Contains ONLY the API the mod uses, declared with the same
// kind (field / property / method), signature and enum values as Unity 2021-2022, in the
// assembly name "UnityEngine" - the facade every Unity player ships, which forwards each type
// to its module (CoreModule, XRModule, ...). BepInEx.dll itself references Unity this way.
// Bodies are never executed; the real Unity assemblies are used at runtime.
// Built with: mcs -target:library -out:UnityEngine.dll UnityEngine.ref.cs
#pragma warning disable 67, 169, 414, 649, 660, 661, 626
using System;
using System.Collections;
using System.Collections.Generic;

[assembly: System.Reflection.AssemblyVersion("0.0.0.0")]

namespace UnityEngine
{
    public class Object
    {
        public string name { get { return null; } set { } }
        public HideFlags hideFlags { get { return 0; } set { } }
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
        public static void DontDestroyOnLoad(Object target) { }
        public static implicit operator bool(Object exists) { return false; }
        public static bool operator ==(Object x, Object y) { return false; }
        public static bool operator !=(Object x, Object y) { return false; }
    }
    [Flags] public enum HideFlags { None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8, DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61 }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float sqrMagnitude { get { return 0; } }
        public static Vector2 one { get { return default(Vector2); } }
        public static Vector2 zero { get { return default(Vector2); } }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return a; }
        public static bool operator ==(Vector2 lhs, Vector2 rhs) { return false; }
        public static bool operator !=(Vector2 lhs, Vector2 rhs) { return false; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up { get { return default(Vector3); } }
        public static Vector3 forward { get { return default(Vector3); } }
        public static Vector3 right { get { return default(Vector3); } }
        public static Vector3 one { get { return default(Vector3); } }
        public static Vector3 zero { get { return default(Vector3); } }
        public float sqrMagnitude { get { return 0; } }
        public Vector3 normalized { get { return this; } }
        public void Normalize() { }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a) { return a; }
        public static Vector3 operator *(Vector3 a, float d) { return a; }
        public static Vector3 operator *(float d, Vector3 a) { return a; }
        public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal) { return vector; }
        public static Vector3 Cross(Vector3 lhs, Vector3 rhs) { return lhs; }
        public static float Dot(Vector3 lhs, Vector3 rhs) { return 0; }
        public static float Distance(Vector3 a, Vector3 b) { return 0; }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { return a; }
    }
    // Only needed by BepInEx's own config converters (tools/load-check); the mod doesn't use it.
    public struct Vector4 { public float x, y, z, w; }
    public struct Rect { public float x { get; set; } public float y { get; set; } public float width { get; set; } public float height { get; set; } }
    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity { get { return default(Quaternion); } }
        public Vector3 eulerAngles { get { return default(Vector3); } set { } }
        public static Quaternion Euler(float x, float y, float z) { return default(Quaternion); }
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards) { return default(Quaternion); }
        public static Quaternion FromToRotation(Vector3 fromDirection, Vector3 toDirection) { return default(Quaternion); }
        public static float Angle(Quaternion a, Quaternion b) { return 0; }
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) { return a; }
        public static Quaternion operator *(Quaternion lhs, Quaternion rhs) { return lhs; }
        public static Vector3 operator *(Quaternion rotation, Vector3 point) { return point; }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1; }
        public static Color white { get { return default(Color); } }
        public static Color black { get { return default(Color); } }
        public static Color Lerp(Color a, Color b, float t) { return a; }
    }
    public struct Color32
    {
        public int rgba; public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { rgba = 0; this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color32(Color c) { return default(Color32); }
        public static implicit operator Color(Color32 c) { return default(Color); }
    }
    public struct Ray
    {
        public Ray(Vector3 origin, Vector3 direction) { }
        public Vector3 origin { get { return default(Vector3); } set { } }
        public Vector3 direction { get { return default(Vector3); } set { } }
        public Vector3 GetPoint(float distance) { return default(Vector3); }
    }
    public struct Plane
    {
        public Plane(Vector3 inNormal, Vector3 inPoint) { }
        public bool Raycast(Ray ray, out float enter) { enter = 0; return false; }
    }
    public struct Mathf
    {
        public const float Rad2Deg = 57.29578f;
        public static float Clamp01(float value) { return value; }
        public static float Clamp(float value, float min, float max) { return value; }
        public static int Clamp(int value, int min, int max) { return value; }
        public static float Max(float a, float b) { return a; }
        public static float Min(float a, float b) { return a; }
        public static float Abs(float f) { return f; }
        public static float Sqrt(float f) { return f; }
        public static float Round(float f) { return f; }
        public static int RoundToInt(float f) { return 0; }
        public static int FloorToInt(float f) { return 0; }
        public static float Lerp(float a, float b, float t) { return a; }
        public static float InverseLerp(float a, float b, float value) { return 0; }
        public static float Cos(float f) { return 0; }
        public static float Atan2(float y, float x) { return 0; }
    }
    public class Time
    {
        public static float deltaTime { get { return 0; } }
        public static float unscaledDeltaTime { get { return 0; } }
        public static float realtimeSinceStartup { get { return 0; } }
    }
    public class ColorUtility { public static bool TryParseHtmlString(string htmlString, out Color color) { color = default(Color); return false; } public static string ToHtmlStringRGBA(Color color) { return "000000FF"; } }
    public enum RuntimePlatform { OSXEditor = 0, OSXPlayer = 1, WindowsPlayer = 2, WindowsEditor = 7, Android = 11, LinuxPlayer = 13 }
    public class Application { public static RuntimePlatform platform { get { return 0; } } public static string dataPath { get { return null; } } public static void OpenURL(string url) { } }
    public enum KeyCode { None = 0, Backspace = 8, Return = 13, Escape = 27, Space = 32, Minus = 45, Equals = 61, P = 112, UpArrow = 273, DownArrow = 274, RightArrow = 275, LeftArrow = 276, F5 = 286, F8 = 289, F9 = 290 }

    public class Component : Object
    {
        public Transform transform { get { return null; } }
        public GameObject gameObject { get { return null; } }
        public T GetComponent<T>() { return default(T); }
        public T[] GetComponents<T>() { return null; }
    }
    public class Behaviour : Component { public bool enabled { get { return false; } set { } } }
    public class YieldInstruction { }
    public sealed class Coroutine : YieldInstruction { }
    public class MonoBehaviour : Behaviour { public Coroutine StartCoroutine(IEnumerator routine) { return null; } }
    public class Transform : Component, IEnumerable
    {
        public Vector3 position { get { return default(Vector3); } set { } }
        public Vector3 localPosition { get { return default(Vector3); } set { } }
        public Vector3 localScale { get { return default(Vector3); } set { } }
        public Vector3 forward { get { return default(Vector3); } set { } }
        public Vector3 up { get { return default(Vector3); } set { } }
        public Quaternion rotation { get { return default(Quaternion); } set { } }
        public Quaternion localRotation { get { return default(Quaternion); } set { } }
        public Transform parent { get { return null; } set { } }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public Vector3 TransformPoint(Vector3 position) { return position; }
        public Vector3 InverseTransformPoint(Vector3 position) { return position; }
        public IEnumerator GetEnumerator() { return null; }
    }
    public enum PrimitiveType { Sphere = 0, Capsule = 1, Cylinder = 2, Cube = 3, Plane = 4, Quad = 5 }
    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public GameObject() { }
        public Transform transform { get { return null; } }
        public bool activeSelf { get { return false; } }
        public bool activeInHierarchy { get { return false; } }
        public void SetActive(bool value) { }
        public T AddComponent<T>() where T : Component { return null; }
        public T GetComponent<T>() { return default(T); }
        public T[] GetComponents<T>() { return null; }
        public static GameObject CreatePrimitive(PrimitiveType type) { return null; }
    }
    public sealed class MeshFilter : Component { }
    public class Renderer : Component
    {
        public bool enabled { get { return false; } set { } }
        public Material sharedMaterial { get { return null; } set { } }
        public Rendering.ShadowCastingMode shadowCastingMode { get { return 0; } set { } }
        public bool receiveShadows { get { return false; } set { } }
    }
    public class MeshRenderer : Renderer { }
    public sealed class LineRenderer : Renderer
    {
        public int positionCount { get { return 0; } set { } }
        public float startWidth { get { return 0; } set { } }
        public float endWidth { get { return 0; } set { } }
        public bool useWorldSpace { get { return false; } set { } }
        public void SetPosition(int index, Vector3 position) { }
    }
    public sealed class Camera : Behaviour
    {
        public static Camera main { get { return null; } }
        public Ray ScreenPointToRay(Vector3 pos) { return default(Ray); }
    }
    public sealed class Shader : Object
    {
        public static Shader Find(string name) { return null; }
        public bool isSupported { get { return false; } }
    }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }
    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureFormat { RGB24 = 3, RGBA32 = 4, BGRA32 = 14 }
    public class Texture : Object
    {
        public virtual int width { get { return 0; } set { } }
        public virtual int height { get { return 0; } set { } }
        public TextureWrapMode wrapMode { get { return 0; } set { } }
        public FilterMode filterMode { get { return 0; } set { } }
    }
    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain, bool linear) { }
        public void SetPixel(int x, int y, Color color) { }
        public Color GetPixel(int x, int y) { return default(Color); }
        public Color32[] GetPixels32() { return null; }
        public void SetPixels32(Color32[] colors) { }
        public void Apply(bool updateMipmaps) { }
    }
    public static class ImageConversion { public static bool LoadImage(this Texture2D tex, byte[] data) { return false; } public static bool LoadImage(this Texture2D tex, byte[] data, bool markNonReadable) { return false; } }
    public class Material : Object
    {
        public Material(Shader shader) { }
        public Material(Material source) { }
        public Shader shader { get { return null; } set { } }
        public Texture mainTexture { get { return null; } set { } }
        public Color color { get { return default(Color); } set { } }
        public int renderQueue { get { return 0; } set { } }
        public Vector2 mainTextureScale { get { return default(Vector2); } set { } }
        public Vector2 mainTextureOffset { get { return default(Vector2); } set { } }
        public bool HasProperty(string name) { return false; }
        public void SetTexture(string name, Texture value) { }
        public void SetColor(string name, Color value) { }
        public void SetTextureScale(string name, Vector2 value) { }
        public void SetTextureOffset(string name, Vector2 value) { }
    }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject { return null; } }
    public sealed class Font : Object { public Material material { get { return null; } set { } } }
    public sealed class Resources { public static T GetBuiltinResource<T>(string path) where T : Object { return null; } }
    public enum TextAnchor { UpperLeft = 0, UpperCenter = 1, UpperRight = 2, MiddleLeft = 3, MiddleCenter = 4, MiddleRight = 5 }
    public enum TextAlignment { Left = 0, Center = 1, Right = 2 }
    public sealed class TextMesh : Component
    {
        public string text { get { return null; } set { } }
        public Font font { get { return null; } set { } }
        public int fontSize { get { return 0; } set { } }
        public float characterSize { get { return 0; } set { } }
        public TextAnchor anchor { get { return 0; } set { } }
        public TextAlignment alignment { get { return 0; } set { } }
        public Color color { get { return default(Color); } set { } }
    }
    public sealed class AudioClip : Object { }
    public sealed class AudioSource : Behaviour
    {
        public float spatialBlend { get { return 0; } set { } }
        public bool playOnAwake { get { return false; } set { } }
        public void PlayOneShot(AudioClip clip, float volumeScale) { }
    }
    public enum AudioType { UNKNOWN = 0, MPEG = 13, OGGVORBIS = 14, WAV = 20 }
    public static class JsonUtility
    {
        public static T FromJson<T>(string json) { return default(T); }
        // Used by BepInEx's config converters (Vector3 settings); bodies only matter to tools/load-check.
        public static string ToJson(object obj) { return "{}"; }
        public static object FromJson(string json, System.Type type) { return System.Activator.CreateInstance(type); }
    }
    public class AsyncOperation : YieldInstruction { }
}

namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off = 0, On = 1, TwoSided = 2, ShadowsOnly = 3 } }

namespace UnityEngine.Networking
{
    public class UnityWebRequestAsyncOperation : UnityEngine.AsyncOperation { }
    public class UnityWebRequest : IDisposable
    {
        public enum Result { InProgress = 0, Success = 1, ConnectionError = 2, ProtocolError = 3, DataProcessingError = 4 }
        public Result result { get { return 0; } }
        public UnityWebRequestAsyncOperation SendWebRequest() { return null; }
        public void Dispose() { }
    }
    public static class UnityWebRequestMultimedia { public static UnityWebRequest GetAudioClip(string uri, UnityEngine.AudioType audioType) { return null; } }
    public sealed class DownloadHandlerAudioClip { public static UnityEngine.AudioClip GetContent(UnityWebRequest www) { return null; } }
}

namespace UnityEngine.XR
{
    public enum XRNode { LeftEye = 0, RightEye = 1, CenterEye = 2, Head = 3, LeftHand = 4, RightHand = 5 }
    public struct InputFeatureUsage<T> { }
    public static class CommonUsages
    {
        public static InputFeatureUsage<bool> primaryButton;
        public static InputFeatureUsage<bool> secondaryButton;
        public static InputFeatureUsage<float> trigger;
        public static InputFeatureUsage<float> grip;
        public static InputFeatureUsage<Vector2> primary2DAxis;
        public static InputFeatureUsage<Vector3> devicePosition;
        public static InputFeatureUsage<Quaternion> deviceRotation;
    }
    public struct InputDevice
    {
        public bool isValid { get { return false; } }
        public bool SendHapticImpulse(uint channel, float amplitude, float duration) { return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<bool> usage, out bool value) { value = false; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<float> usage, out float value) { value = 0; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<Vector2> usage, out Vector2 value) { value = default(Vector2); return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<Vector3> usage, out Vector3 value) { value = default(Vector3); return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<Quaternion> usage, out Quaternion value) { value = default(Quaternion); return false; }
    }
    public class InputDevices { public static void GetDevicesAtXRNode(XRNode node, List<InputDevice> inputDevices) { } }
}
