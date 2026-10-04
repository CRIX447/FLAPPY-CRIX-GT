# Testing

Two layers: what was verified during development (outside the game), and the self test the mod
runs **inside Gorilla Tag** on every start.

## 1. Verified during development — 2026-10-04, crix-website commit `57e902d`

### Website asset discovery (from reading the repo)

| Question | Finding |
|---|---|
| File that launches the game | `flappycrix.html` (1.0 MB, single page; all game code inline in classic `<script>` blocks) |
| Local JavaScript | `birthday.js`, `filter.js`, `ui.js`, `android-bridge.js`, `playfab-manager.js`, `cosmetic-art.js`, `photon-realtime-browser.js` |
| CSS | Inline `<style>` blocks; web font `img/font.woff2` (`CrixCustom`) |
| Images | `img/bird.png`, `coin-still.png`, gifs, cosmetics, `img/emoji/72/*.png` (paths built at runtime) |
| Audio | 46 MP3s in `img/`, loaded as `'/img/' + name + '.mp3'` |
| Canvas / WebGL | Canvas 2D, 400×600 logical (`#gameCanvas`). **No WebGL.** |
| External libraries | Firebase 10.8 compat (gstatic CDN), Photon Realtime (local file), Vercel analytics |
| External URLs at runtime | Firebase CDN, Photon websockets, `/api/*` serverless routes, Discord OAuth |
| Paths | Root-absolute (`/img/…`) → needs a web root, not `file://` (hence the loopback server) |
| Browser APIs | localStorage, sessionStorage, AudioContext/`<audio>`, `fetch`, Gamepad API, service worker, `matchMedia`, `performance`. None unavailable in CEF. |
| Input the game listens for | `keydown` Space → `jump()`; click on `#canvasWrapper` → `gameOver ? startGame() : jump()`; Gamepad A / d-pad up / stick < −0.55 |
| Existing hooks reused | `jump()`, `startGame()`, `togglePause()`, `resumeGame()`, `#startGameBtn`, `#dsRetry`, `crix:gamestart` event, globals `score`, `gameOver`, `gameRunning`, `isPaused` |
| Not in the repo (404 on the live site too) | `img/pumpkin-head.png`, `witch-hat.png`, `skeleton-mask.png`, `bunny-ears.png`, `santa-hat.png`, `reindeer-ears.png`, `eastermusic.mp3`, `witchl.mp3` |

### End-to-end browser test — `tools/test_web.py`

Setup: the packaged `mod/FlappyCrix/Web` served by **the mod's own `LocalWebServer.cs`** (compiled with
Mono, `tools/server-harness`), loaded in **headless Chromium 141** (CEF is Chromium), with **every
non-loopback request blocked** (offline). UnityWebBrowser's `uwb.ExecuteJsMethod` was replaced by a
recorder so JS→Unity events could be checked. Input used Playwright's real (trusted) keyboard and mouse,
which is what UWB's `SendKeyboardControls`/`SendMouseClick` produce.

Result: **26/26 passed** (also 3 repeated runs; one earlier flaky game-over wait was fixed by polling).

```
PASS no unexpected local 404s (offline)
PASS bridge reported ready to Unity
PASS intro video skipped by bridge
PASS JavaScript executed (game functions defined)
PASS CSS applied
PASS custom font loaded
PASS images loaded  -- 94 ok / 0 failed
PASS bird sprite decoded
PASS audio files fetched  -- 43
PASS MP3 playable by engine  -- probably
PASS canvas painted (multiple colours)  -- 8
PASS GameStarted event
PASS screen = playing
PASS native Space key flaps (website's own keydown handler)  -- velocity 5.50 -> -8.28
PASS Space reached page as a key event
PASS music playing after trusted input
PASS score increases while flapping via bridge  -- score 4
PASS ScoreChanged events sent to Unity
PASS page frame rate  -- 58 fps (headless, software rendering)
PASS pause freezes the game
PASS resume continues
PASS GameOver event
PASS restart starts a fresh run
PASS native mouse click on canvas flaps
PASS external link blocked, Unity notified
PASS no uncaught page errors
blocked (offline): the three Firebase CDN scripts — the site handles their absence
```

Also checked:
* **Live-site mode injection:** the *unmodified* site loaded, then config + `bridge.js` injected afterwards
  (as `ExecuteJs` does on `OnLoadFinish`): intro skipped, `BridgeReady` sent, flap from menu started a run,
  re-injection is a no-op. (crixgamingvr.com itself wasn't reachable from the test machine, so the repo copy
  stood in for it.)
* **Local server:** routing (`/`, `/flappycrix`), HTTP Range → 206 (needed for media), `../` and `%2e%2e`
  traversal → 404, bridge injected as the first thing in `<head>`.
* **Layout per resolution:** 640×800 hits the site's phone layout (title overlaps a banner);
  768×960 (default) and 1024×1280 lay out cleanly.
* **The shipped `FlappyCrix.dll`** (prebuilt, 72 KB) was built with Mono `mcs` against the **real
  BepInEx 5.4.23.2 `BepInEx.dll`** and reference stand-ins for Unity and UnityWebBrowser
  (`tools/refs/*.ref.cs`). The stand-ins declare only the members the mod uses, with the real kinds and
  signatures (properties as properties, enum values, etc.), and reference Unity through the `UnityEngine`
  facade assembly exactly as BepInEx itself does. Every Unity/UWB member the DLL calls was listed from its
  IL and checked against the real APIs (126 Unity members, 49 UWB members). The physics module is not
  referenced at all (colliders are removed without naming the type).
* The DLL loads under Mono, all 35 types resolve, `[BepInPlugin("com.crix.flappycrix", "Flappy Crix", "1.0.0")]`
  is present, and **the 26/26 browser suite was re-run with the web server running from inside this DLL.**

Run it yourself:
```
mcs -out:harness.exe src/FlappyCrix/Web/LocalWebServer.cs tools/server-harness/Harness.cs
mono harness.exe mod/FlappyCrix/Web 47321 &
pip install playwright && python -m playwright install chromium
python tools/test_web.py http://127.0.0.1:47321/flappycrix.html
```

### Arcade deck → website (`tools/test_deck.py`, 8/8 passed)

```
PASS joystick moves highlight (site's padMove)  -- highlighted: 'closeTutorialBtn'
PASS SELECT activates highlighted item (closed tutorial)
PASS joystick to START + SELECT starts a run
PASS SELECT while playing flaps
PASS joystick ignored while playing
PASS PAUSE pauses
PASS START resumes from pause
PASS START retries from game over
```
The 26/26 suite was re-run after the change (still 26/26).

### Live page + desktop links (`tools/test_links.py --inject`, 13/13 passed)

The unmodified site, with the bridge injected after load exactly as live-page mode does:
```
PASS SIGN IN opens crixgamingvr.com/flappycrix on the desktop
PASS ...and the in-panel sign-in dialog stays closed
PASS sign-in dialog opened by code -> desktop instead
PASS Discord link -> desktop          PASS YouTube link -> desktop        PASS Shop link -> desktop
PASS Other site page (/privacy) -> https://crixgamingvr.com/privacy on desktop
PASS window.open pop-up -> desktop    PASS Firebase sign-in pop-up -> game page on desktop
PASS non-web links ignored            PASS panel never navigated away from the game
PASS site's own toast tells the player it opened on the PC
PASS link to the game page itself stays in the panel
```
`tools/test_live_injection.py`: intro skipped, BridgeReady after late injection, flap starts a run, re-injection is a no-op.
Note: crixgamingvr.com itself wasn't reachable from the build machine, so the repo copy stood in for it.

### Not yet verified (needs the game)

* `Application.OpenURL` actually bringing up the desktop browser while Gorilla Tag is running in VR.
* Y / B open and hide with real controllers.

* Pressing the deck buttons and grabbing the joystick with real hands (hit sizes and the 15° deck tilt may need tuning).

* UnityWebBrowser actually starting inside Gorilla Tag under BepInEx (engine launch, IPC, UniTask bootstrap).
* Texture upload performance and Gorilla Tag frame rate with the panel open.
* XR controller mapping, laser alignment (`LaserPitch`), joystick feel, panel shader on Gorilla Tag's pipeline.
* The native fallback's look and feel (logic ported from the site; not run in Unity).
* That the facade `UnityEngine.dll` in Gorilla Tag forwards the XR module types (it is designed to forward every module; if not, rebuild with `dotnet build` against the game's DLLs).

## 2. In-game self test (automatic, every launch when `RunSelfTest = true`)

Three seconds after the page reports ready, the mod sends a real Space key and pointer move from Unity, then
asks the page for a report. Written to the BepInEx log and `BepInEx/plugins/FlappyCrix/BrowserData/selftest.txt`:

```
==== Flappy Crix WebView self test (Website (packaged copy)) ====
  [PASS] Engine started and page loaded  (4.2 s)
  [PASS] JavaScript executes (game functions present)
  [PASS] CSS applied
  [PASS] Website font loaded
  [PASS] Local images load  (94 ok, 0 failed)
  [PASS] Bird sprite decoded
  [PASS] Audio files load  (43 files)
  [PASS] Engine can decode MP3  ('probably')
  [PASS] Canvas renders  (8 colours sampled)
  [PASS] Unity texture receives frames  (58 fps)
  [PASS] Unity key input reaches page  (1 key events)
  [PASS] Page frame rate acceptable (>= 30)  (60 fps)
RESULT: PASS - website mode is working.
```
(illustrative — your numbers will differ)

Critical rows (JS, bird sprite, canvas, texture frames, key input) failing → website mode is reported as
failed and, with `AutoFallbackToNative = true`, the native game takes over. Other rows are warnings.
Separately, if the engine is missing, doesn't connect, or the page never reports ready within the
timeout, the mod falls back the same way. Nothing is claimed from files merely existing.
