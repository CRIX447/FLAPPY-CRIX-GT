# Testing

What was verified while building the mod, how, and what still needs a real headset.
(This mod and these tests were made with AI — Claude by Anthropic.)

## Fixes after in-game tests (by CRIX)

| Report | Cause | Fix |
|---|---|---|
| "It isn't using the webpage" | The first build's website engine (UnityWebBrowser) wasn't in the release zip — it needs a separate Unity build — so the mod fell back to the native version. | New default engine: the PC's own Microsoft Edge/Chrome, started hidden and streamed in. Nothing to install. |
| "I can't see the UI unless I put my hand in front of it" | Draw-order bug: the screen's black backing was drawn *after* the game picture and covered it, while the menu text used Unity's text shader, which draws on top of everything — so text only showed where a hand blocked the backing. | The screen is now one solid picture (website stream or native image), drawn with a solid depth-tested shader; text is drawn into textures with a pixel font. No text shader is used anywhere. |
| "The screen is too big" | 1.0 m wide at 1.6 m ≈ 35° × 43° of your view. | 0.55 m at 1.3 m ≈ 24° × 30°, deck − / + buttons, and a one-time config upgrade so old configs get the new size. |
| Fourth test: "make it support any browser" — log: `No Microsoft Edge or Chrome found`, native version shown | The PC has no Edge or Chrome (Edge removed: only `EdgeCore`, `EdgeUpdate`, `EdgeWebView` left; Opera GX installed). The mod only knew Edge, Chrome and Brave. | `BrowserFinder` (all headless-capable Chromium browsers incl. EdgeCore; Opera/Firefox listed, not opened), next-browser on failure with memory, and `EngineDownloader` (Chrome for Testing headless shell from Google, once). Live harness scenarios 4–5; the headless shell passes the engine harness 18/18. |
| Fourth test: "select works as flap" | SELECT on the website's menu highlighted the first item instead of starting. | SELECT = FLAP (start / flap / retry) unless the joystick highlighted something or a window is open. `test_deck.py` 11/11. |
| Fourth test: "move the screen closer to the gamepad thing" | Screen 1.3 m away, deck at 0.42 m. | Screen 0.75 m away, 0.36 m wide, bottom edge just above the deck (`docs/screenshots/16-screen-behind-deck.png`); settings upgraded once. |
| Third test: "it's not connecting to the web" (the site's offline mode) | (1) The mod counted the live site as started only at the page's `load` event — every image/sound/script downloaded — and gave it 12 s from game start, then switched to the packaged copy, which is offline by design. (2) The site marks itself offline when its first `/robots.txt` check takes over 5 s (likely while the rest of the page is downloading) and only re-checks when a tab becomes visible, which never happens in a hidden browser. | Ready as soon as the game's code has run; 30 s to *start* loading (from browser start), then unlimited; the bridge re-triggers the site's own online check every 6 s while it says offline; the reason is shown on screen and logged, and the mod returns to the live site by itself when it can. `tools/live-harness` 9/9. |
| Third test: "I have to use my palm to press the buttons" | Presses were measured at the controller's tracked point, which is in your palm. | Presses use Gorilla Tag's own fingertip points (found by reflection, no hard link to the game's code), else a fingertip 8.5 cm ahead of the controller; tighter press zone at the cap, ring glows under a fingertip. |
| Second test: "it won't work" (no screen at all) | The start-up method called `UwbBrowserGame.IsInstalled`, a member of the optional UnityWebBrowser engine's class. Without that engine's DLLs (the normal install) Mono can't load the class, so it refused to compile the **whole** start-up method: no engine started, and `Update` returned before the screen was ever placed. The earlier checks loaded the DLL *with* the UWB stand-ins present, so they missed it. | The check now only looks for the engine's files; every engine is created in its own `[NoInlining]` method inside a try/catch; the screen is placed and shown before any engine code runs, with a Loading / Could-not-start picture. The new `tools/load-check` reproduces the bug on the old DLL (`TypeLoadException` at engine start-up) and passes on the new one. |

## 1. Hidden-browser engine, end to end — `tools/engine-harness` (18/18)

The mod's real engine code (`Web/Cdp/*`: launcher, WebSocket client, DevTools connection, page controller,
plus `LocalWebServer`) compiled with Mono and run against a real headless Chromium 141, outside Unity:

```
PASS hidden browser launched and DevTools connected  -- Chrome/141.0.7390.37 in 3519 ms
PASS bridge injected before the site's scripts and reported ready
PASS screencast frames arriving
PASS frames are 1:1 and contain the whole 768x960 page at the top-left
PASS JS -> engine binding round trip
PASS real clicks dismiss the site's toasts
PASS real mouse click closes the site's tutorial
PASS game started (Screen:playing event)
PASS real Space key (Input.dispatchKeyEvent) flaps via the site's own handler  -- velocity 4.75 -> -8.28
PASS music playing
PASS frame stream while playing  -- 58 frames/s
PASS scoring while flapping
PASS navigation away from the game is blocked and reported (to open on the desktop)
PASS panel still on the game
PASS window.open -> OpenExternal message
PASS confirm() answered 'no' without freezing the page
PASS self test report received
PASS browser engine closed on shutdown
```

Problems found and fixed during this testing:
- **Intermittent stall**: the WebSocket used one buffered stream for reading and writing from two threads. Reads and
  writes now use separate paths (three consecutive clean runs after the fix).
- **Bottom of the game cut off**: even headless, the browser window keeps room for invisible toolbars, and that room
  changes between pages. Frames are now taken at 1:1 with the visible-area size from each frame's metadata, and the
  screen shows exactly the page's 768×960 corner.
- **A click "missing" the tutorial's close button** was correct behaviour: at 768 px wide the site's own first-launch
  toasts sit on top of it (`elementFromPoint` confirmed). Dismissing the toasts with real clicks, then clicking close, works.

Run it: see the header of `tools/engine-harness/EngineTest.cs`.

## 2. Native fallback — `tools/native-harness` (5/5)

Game rules (`NativeSim`) and renderer (`NativeRenderer`) run outside Unity: FLAP starts a run, pipes score
(5 in 12 s of autopilot), falling ends the run, sounds fire, and drawing a 400×600 frame takes ~0.13 ms.
Frames were dumped and checked by eye (menu, playing, paused, game over).

## 3. Website + bridge in Chromium (Playwright)

| Suite | Result | Covers |
|---|---|---|
| `tools/test_web.py` | 26/26 | offline packaged copy: JS, CSS, font, images, audio, canvas, Space flaps, music, scoring, pause/resume, game over, retry, link routing, no page errors |
| `tools/test_deck.py` | 11/11 | joystick navigation (site's `padMove`), SELECT (as FLAP: start/flap/retry; presses a highlighted item), START (start/resume/retry), PAUSE |
| `tools/test_links.py --inject` | 13/13 | sign-in, Discord/YouTube/shop, other site pages and pop-ups → desktop; game page stays |
| `tools/test_live_injection.py` | pass | bridge injected after load on the unmodified site |

crixgamingvr.com itself wasn't reachable from the build machine, so the repo's copy of the site stood in for it.

## 4. The DLL

`FlappyCrix.dll` is built with Mono against the real BepInEx 5.4.23.2 DLL and reference stand-ins for Unity and
UnityWebBrowser (`tools/refs`). Two checks run on every build (locally and in GitHub Actions):

**`tools/load-check`** loads the DLL the way a normal install has it (BepInEx and Unity, **no** UnityWebBrowser
DLLs). It binds the settings against the real BepInEx `ConfigFile` (with BepInEx's own Vector3/Color converters),
runs the controller's engine start-up through the unavailable engines, then JIT-compiles every method:

```
DLL from the second in-game test:                         This build:
PASS settings bind against the real BepInEx ConfigFile    PASS settings bind against the real BepInEx ConfigFile
FAIL engine start-up ... TypeLoadException:               PASS engine start-up skips the unavailable engines
     Could not load ... 'VoltstroStudios.UnityWebBrowser' PASS every method outside the UWB-only classes compiles
1 FAILED                                                  ALL PASSED
```

**`tools/api-audit`** disassembles the DLL, lists every UnityEngine member it calls (169: type, name, parameter
types, field vs property vs method) and finds each one in Unity's own C# source
([UnityCsReference](https://github.com/Unity-Technologies/UnityCsReference)). All 169 are found in **2021.3, 2022.3,
Unity 6 (6000.0) and 6000.2 — the version Gorilla Tag runs (`Running under Unity v6000.2.9` in CRIX's log)**. The audit itself was tested by planting five wrong members (a missing overload, a missing
property, a missing field, a wrong parameter type, an extra parameter); it flagged all five.

The DLL references only `mscorlib`, `System` and `System.Core` (all shipped with every Unity game), BepInEx and
UnityEngine. Newer-runtime overloads such as `string.Trim(char)` / `Split(char)` were replaced with the classic
forms, so it doesn't depend on the game's .NET profile.

## 5. Live site and engine choice — `tools/live-harness` (22/22)

crixgamingvr.com can't be reached from the build machine, so `live_site.py` stands in for it (served like Vercel
serves the real site: clean URLs, `/robots.txt`), and the mod's real `EdgeBrowserGame`/`BrowserGame` from
`FlappyCrix.dll` run against it in a real Chromium, outside Unity:

```
== 1. slow live site (two images 45 s, first online check 8 s)
PASS the live site is used, not the packaged copy
PASS the game is ready long before the page's load event (two images take 45 s)  -- ready 0.4 s after the page started
PASS the site's first online check timed out (it said offline)
PASS ...and the bridge got it to check again: back online by itself  -- online 6.0 s later
PASS still on the live site
== 2. live site down at start, back later
PASS live site unreachable -> the packaged copy is shown  -- (net::ERR_CONNECTION_REFUSED)
PASS when it can be reached again, it switches back to the live site by itself  -- live 7.8 s after the site came back
== 3. live site stalls (connects, never answers), then recovers
PASS stalled live site -> packaged copy after RemoteTimeoutSeconds
PASS ...then back to the live site once it answers  -- live 8.1 s after it recovered
9/9 passed
```

```
== 4. finding browsers (CRIX's PC: no Edge/Chrome, EdgeCore left over, Opera GX)
PASS usable browsers found, best first (newest EdgeCore, Vivaldi, then the downloaded engine)
PASS Edge's WebView runtime is not used as a browser
PASS Opera GX is reported as installed but not usable hidden
PASS only Opera GX installed -> no usable browser (so the engine gets downloaded)
PASS a browser that failed to start is remembered
PASS ...and tried again once it's updated
PASS BrowserPath is still honoured first (with a warning for Opera)
== 5. no usable browser -> download the engine, unpack, run the website
PASS refuses to download from anywhere but Google's Chrome for Testing storage
PASS downloads and unpacks the engine
PASS progress reached 100%
PASS older engine versions are removed
PASS the finder picks up the downloaded engine
PASS the downloaded engine runs the website
22/22 passed
```

Scenario 5 serves a stand-in for Google's servers (same JSON format and zip layout) whose package launches the
real Chromium headless shell. Google's servers can't be reached from the build machine; the real download on
Windows (and Windows' `tar`) still needs a real PC. The headless shell itself was also run through the engine
harness: 18/18, frames exactly 768×960.

A first run found that the stand-in itself was unrealistic: slowing *every* image used up the browser's
6 connections per site, so the site's online check queued behind them. The real site is served over HTTP/2,
where that can't happen; the stand-in now slows only two images (enough to hold up the `load` event).

`tools/test_links.py --inject` (13/13) also runs against this stand-in: the site's sign-in/social links work
online and are sent to the desktop.

## Still needs a real headset

- The hidden engine on Windows: Edge's `EdgeCore` copy or the downloaded Chrome for Testing headless shell (tested
  here with Chromium and its headless shell on Linux; same protocol). The self test will say whether it worked.
- The stream's **frame decode time** inside Gorilla Tag (logged every 30 s as `Screen stream: ...`).
- Which **shader** Gorilla Tag provides for the screen/deck (logged as `Rendering with shader: ...`).
- **Audio** from the hidden browser (it plays through Windows' default output; the test machine had no sound card,
  but the browser did try to open the audio device).
- Deck buttons with real fingertips: whether Gorilla Tag's fingertip points are found (the log says
  `Deck buttons: pressed with Gorilla Tag's own fingertip points` or that it uses `FingertipOffset`), the joystick,
  laser alignment, Y/B, and `Application.OpenURL` opening the desktop browser.
- The real crixgamingvr.com over the player's own connection (the stand-in covers slow, down and stalled).

## In-game self test (every launch)

Three seconds after the page reports ready, the mod sends a real Space key and pointer move, then asks the page for a
report, written to the log and `%LOCALAPPDATA%\FlappyCrix\selftest.txt`. Critical failures (JavaScript, bird sprite, canvas, frames
reaching the screen, key input reaching the page) make the mod switch to the next engine.
