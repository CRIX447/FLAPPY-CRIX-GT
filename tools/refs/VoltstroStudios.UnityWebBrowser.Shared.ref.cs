// Reference-only stand-in for VoltstroStudios.UnityWebBrowser.Shared.dll (UWB 2.2.8).
// Only what FlappyCrix uses; names, namespaces, kinds and enum values copied from
// github.com/Voltstro-Studios/UnityWebBrowser src/VoltstroStudios.UnityWebBrowser.Shared.
#pragma warning disable 649
namespace VoltstroStudios.UnityWebBrowser.Shared
{
    public struct Resolution { public Resolution(uint width, uint height) { Width = width; Height = height; } public uint Width; public uint Height; }
    public enum LogSeverity { Debug, Info, Warn, Error, Fatal }
    [System.Flags] public enum WindowsKey { Space = 0x20 }
}
namespace VoltstroStudios.UnityWebBrowser.Shared.Core { public enum Platform { Windows64, Linux64, MacOS, MacOSArm64 } }
namespace VoltstroStudios.UnityWebBrowser.Shared.Events
{
    public enum MouseClickType : byte { Left, Middle, Right }
    public enum MouseEventType : byte { Down, Up }
}
namespace VoltstroStudios.UnityWebBrowser.Shared.Popups { public enum PopupAction : byte { Ignore, OpenExternalWindow, Redirect } }
