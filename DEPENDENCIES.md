# Website engine

## Default: the browser already on the PC

The mod runs the website with **the Chromium browser already installed on the player's PC** — Microsoft Edge,
which ships with Windows 10 and 11, or Google Chrome / Brave — started **headless** (no window, nothing on the
desktop, its own private profile in `BrowserData/EdgeProfile`). The page is rendered off-screen and streamed into
Gorilla Tag over the Chrome DevTools Protocol:

| Need | How |
|---|---|
| Frames for the in-game screen | `Page.startScreencast` (JPEG, 1:1) → `ImageConversion.LoadImage` into a `Texture2D` |
| Real input | `Input.dispatchKeyEvent` (Space), `Input.dispatchMouseEvent` (laser/mouse, wheel) |
| Bridge before the site's own scripts | `Page.addScriptToEvaluateOnNewDocument` |
| JS → Unity | `Runtime.addBinding("flappyCrixSend")` → `Runtime.bindingCalled` |
| Only the game page loads in the panel | `Fetch.enable` on main-frame documents; others are failed and opened on the desktop |
| Page size | `Emulation.setDeviceMetricsOverride` 768×960 |
| No leftover processes | Windows job object with kill-on-close, tied to Gorilla Tag's process |

**Nothing is downloaded or redistributed** for this: the protocol client (`src/FlappyCrix/Web/Cdp/`: launcher,
WebSocket, JSON, page controller) is part of the mod's own source, and the browser is the player's own.
Limitations: Windows (Edge/Chrome present); a work/school policy that disables browser remote debugging blocks it
(the mod then falls back); each frame is decoded on Unity's main thread (logged; `BrowserFrameRate` caps it at 30/s);
audio plays through Windows' default output from the hidden browser.

The rest of this file describes the **optional** UnityWebBrowser engine (`Engine = UnityWebBrowser`), which bundles
its own Chromium instead.

---

## Which library

**UnityWebBrowser (UWB) 2.2.x** by Voltstro-Studios, with its **CEF engine for Windows x64**.
<https://github.com/Voltstro-Studios/UnityWebBrowser>

It runs Chromium Embedded Framework (CEF — the same engine as Chrome) in a separate process
(`UnityWebBrowser.Engine.Cef.exe`), renders off-screen, and streams pixels into a Unity
`Texture2D`. Unity sends mouse/keyboard events and JavaScript to it over a local TCP/pipe connection.
The current 2.2.8 release bundles CEF 143.

## Why it's required

Gorilla Tag (Unity) has no built-in browser on Windows. To run the existing HTML/JS game unchanged,
the mod needs a real browser engine that can render into a texture inside the game. Requirements checked
against the website (see `TESTING.md`): JavaScript, Canvas 2D (no WebGL used), CSS + web fonts,
PNG/GIF images, MP3 audio, `localStorage`/`sessionStorage`, `fetch`, `performance` API, trusted
keyboard/mouse input, JS evaluation, and a JS→host callback. UWB provides all of them:

| Need | UWB API |
|---|---|
| Render into a texture | `WebBrowserClient.BrowserTexture` (BGRA32) |
| Unity → page input | `SendKeyboardControls`, `SendMouseMove`, `SendMouseClick`, `SendMouseScroll` |
| Unity → JS | `ExecuteJs(string)` |
| JS → Unity | `RegisterJsMethod<string>("FlappyCrixEvent", …)` ↔ `uwb.ExecuteJsMethod(...)` in the page |
| Page events | `OnLoadFinish`, `OnUrlChanged`, `OnClientConnected` |

### Alternatives considered

| Option | Why not |
|---|---|
| **Vuplex 3D WebView** | Commercial, per-developer licence; redistributing it inside a free public mod isn't covered without buying a licence. |
| **ZFBrowser (Embedded Browser)** | Commercial and no longer maintained. |
| **CefSharp.OffScreen** | Uses C++/CLI mixed-mode assemblies, which Unity's Mono runtime can't load. |
| **Microsoft WebView2** | No supported off-screen-to-texture mode; it renders into a window, which is exactly the "desktop browser window" the brief rules out. |
| Running the game in Chrome/Edge | The game must be inside Gorilla Tag; only sign-in, socials and other pages are sent to the desktop browser. |

## Where it comes from

* Source: GitHub, MIT licence — <https://github.com/Voltstro-Studios/UnityWebBrowser>
* Binaries: Voltstro's own Unity package registry, **VoltUPR** (<https://upr.voltstro.dev/-/web/about>),
  packages `dev.voltstro.unitywebbrowser` and `dev.voltstro.unitywebbrowser.engine.cef.win.x64`.
* ⚠️ The **npmjs.com** package named `dev.voltstro.unitywebbrowser` is *not* UWB — npm replaced it with a
  "security holding" placeholder (checked 2026-10-04). Never install from there.

No DLL is downloaded by this project automatically, and none were included from unverified sources:
`tools/collect_uwb.ps1` copies them out of a Unity build you make yourself from the official packages.

## How it's packaged

From a minimal Unity project built with **the same Unity version as Gorilla Tag** (Mono, Windows x64),
`collect_uwb.ps1` copies into `BepInEx/plugins/FlappyCrix/`:

| File | From | Licence |
|---|---|---|
| `VoltstroStudios.UnityWebBrowser.dll` | UWB | MIT |
| `VoltstroStudios.UnityWebBrowser.Shared.dll` | UWB | MIT |
| `VoltstroStudios.NativeArraySpanExtensions.dll` | Voltstro | MIT |
| `VoltRpc.dll` | Voltstro (IPC) | MIT |
| `UniTask.dll` (skipped if the game has it) | Cysharp | MIT |
| `Newtonsoft.Json.dll` (skipped if the game has it) | James Newton-King | MIT |
| `UWB/` — `UnityWebBrowser.Engine.Cef.exe`, `libcef.dll`, Chromium resources | UWB engine + CEF + Chromium | MIT (engine), BSD-3-Clause (CEF), Chromium's BSD-style licence plus its third-party component licences |

The engine is looked up in the mod folder (`FlappyCrixCefEngine` overrides UWB's default
`<Game>_Data/UWB/`), and the browser profile/logs go to `FlappyCrix/BrowserData/` — nothing is written
into Gorilla Tag's own folders.

Two runtime adjustments are needed because the DLLs are loaded by BepInEx rather than built into the game:

1. **UniTask** normally initialises from `[RuntimeInitializeOnLoadMethod]`, which Unity only runs for
   assemblies in the original build. `UniTaskBootstrap` calls its (idempotent) `PlayerLoopHelper.Init()` by
   reflection; without it UWB would connect and then hang.
2. UWB's `Resolution` setter resizes a running browser, so the initial size is set on the private field before `Init()`.

## Can it be redistributed with the mod?

**Yes, with notices.** Every component is under a permissive licence (MIT / BSD) that allows
redistribution in binary form provided the copyright and licence texts are included. `collect_uwb.ps1`
copies the licence files it finds into `LICENSES/`, and `THIRD_PARTY_NOTICES.md` lists everything.
CEF's standard builds use Chromium's open-source media stack without proprietary codecs (no H.264/AAC),
so no codec licensing applies — and this is why the site's MP3s and VP9 intro work, while `intro.mp4` (H.264)
isn't used (the site already falls back to `intro.webm`).

This is a summary of the licences, not legal advice.

## Limitations

* **Windows x64 only.** BepInEx PC Gorilla Tag (Steam / Oculus PC). Not Quest standalone.
* **Size:** the CEF engine is roughly 150–300 MB unpacked; the website itself is 14 MB.
* **Performance:** CEF renders on the CPU and every frame is copied into the texture (768×960×4 ≈ 3 MB per
  frame). Expect some CPU cost; lower `BrowserFrameRate` or the resolution if Gorilla Tag's frame rate drops.
* **Audio** plays through Windows' default output from the browser process — it is not positional and not
  affected by Gorilla Tag's volume slider (use `MuteWebAudio`).
* **Sign-in** happens in the desktop browser (the panel plays as a guest). In the packaged fallback, sign-in and
  multiplayer are off (offline by design).
* **Unity version coupling:** UWB DLLs must be rebuilt if Gorilla Tag moves to a new Unity major version.
* UWB's maintainers plan a 3.x release requiring Unity 6; stay on 2.2.x unless Gorilla Tag is on Unity 6.
* Antivirus software sometimes flags unsigned helper `.exe` files; the engine is unsigned upstream.
