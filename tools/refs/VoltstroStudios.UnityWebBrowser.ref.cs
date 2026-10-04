// Reference-only stand-in for VoltstroStudios.UnityWebBrowser.dll (UWB 2.2.8 runtime).
// Only what FlappyCrix uses; signatures copied from src/Packages/UnityWebBrowser/Runtime.
#pragma warning disable 67, 169, 414, 649
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VoltstroStudios.UnityWebBrowser.Shared;
using VoltstroStudios.UnityWebBrowser.Shared.Core;
using VoltstroStudios.UnityWebBrowser.Shared.Events;
using VoltstroStudios.UnityWebBrowser.Shared.Popups;

namespace VoltstroStudios.UnityWebBrowser.Logging
{
    public interface IWebBrowserLogger { void Debug(object message); void Warn(object message); void Error(object message); }
}
namespace VoltstroStudios.UnityWebBrowser.Events
{
    public delegate void OnClientConnected();
    public delegate void OnLoadFinishDelegate(string url);
    public delegate void OnUrlChangeDelegate(string url);
}
namespace VoltstroStudios.UnityWebBrowser.Communication
{
    public abstract class CommunicationLayer : ScriptableObject { public int connectionTimeout = 7000; }
    public sealed class TCPCommunicationLayer : CommunicationLayer { public int inPort = 5555; public int outPort = 5556; }
}
namespace VoltstroStudios.UnityWebBrowser.Core.Engines
{
    public abstract class Engine : ScriptableObject
    {
        public abstract string GetEngineExecutableName();
        public virtual string GetEngineWorkingPath(Platform platform) { return null; }
        public virtual string GetEngineAppPath(Platform platform) { return null; }
        public abstract IEnumerable<EnginePlatformFiles> EngineFiles { get; }
        [Serializable] public struct EnginePlatformFiles { public Platform platform; public string engineBaseAppLocation; public string engineRuntimeLocation; }
        [Obsolete] public abstract string EngineFilesNotFoundError { get; }
    }
}
namespace VoltstroStudios.UnityWebBrowser.Core.Js
{
    [Serializable] public sealed class JsMethodManager { public bool jsMethodsEnable; }
}
namespace VoltstroStudios.UnityWebBrowser.Core
{
    using VoltstroStudios.UnityWebBrowser.Communication;
    using VoltstroStudios.UnityWebBrowser.Core.Engines;
    using VoltstroStudios.UnityWebBrowser.Core.Js;
    using VoltstroStudios.UnityWebBrowser.Events;
    using VoltstroStudios.UnityWebBrowser.Logging;

    public class WebBrowserClient : IDisposable
    {
        public WebBrowserClient(bool headless = false) { }
        public Engine engine;
        public string initialUrl;
        private Resolution resolution;
        public Color32 backgroundColor;
        public bool javascript, cache, incognitoMode, localStorage;
        public PopupAction popupAction;
        public int windowlessFrameRate;
        public bool remoteDebugging;
        public JsMethodManager jsMethodManager;
        public CommunicationLayer communicationLayer;
        public int engineStartupTimeout;
        public LogSeverity logSeverity;
        public Texture2D BrowserTexture { get { return null; } }
        public bool IsConnected { get { return false; } }
        public bool ReadySignalReceived { get { return false; } }
        public int FPS { get { return 0; } }
        public FileInfo LogPath { get { return null; } set { } }
        public FileInfo CachePath { get { return null; } set { } }
        public IWebBrowserLogger Logger { get { return null; } set { } }
        public event OnClientConnected OnClientConnected;
        public event OnLoadFinishDelegate OnLoadFinish;
        public event OnUrlChangeDelegate OnUrlChanged;
        public void Init() { }
        public void LoadTextureData() { }
        public void UpdateFps() { }
        public void SendKeyboardControls(WindowsKey[] keysDown, WindowsKey[] keysUp, char[] chars) { }
        public void SendMouseMove(Vector2 mousePos) { }
        public void SendMouseClick(Vector2 mousePos, int clickCount, MouseClickType clickType, MouseEventType eventType) { }
        public void SendMouseScroll(Vector2 mousePos, int mouseScroll) { }
        public void LoadUrl(string url) { }
        public void ExecuteJs(string js) { }
        public void AudioMute(bool muted) { }
        public void RegisterJsMethod<T>(string name, Action<T> method) { }
        public void Dispose() { }
    }
}
