# Testing

What was verified while building the mod, how, and what still needs a real headset.
(This mod and these tests were made with AI — Claude by Anthropic.)

## Fixes after the first in-game test (by CRIX)

| Report | Cause | Fix |
|---|---|---|
| "It isn't using the webpage" | The first build's website engine (UnityWebBrowser) wasn't in the release zip — it needs a separate Unity build — so the mod fell back to the native version. | New default engine: the PC's own Microsoft Edge/Chrome, started hidden and streamed in. Nothing to install. |
| "I can't see the UI unless I put my hand in front of it" | Draw-order bug: the screen's black backing was drawn *after* the game picture and covered it, while the menu text used Unity's text shader, which draws on top of everything — so text only showed where a hand blocked the backing. | The screen is now one solid picture (website stream or native image), drawn with a solid depth-tested shader; text is drawn into textures with a pixel font. No text shader is used anywhere. |
| "The screen is too big" | 1.0 m wide at 1.6 m ≈ 35° × 43° of your view. | 0.55 m at 1.3 m ≈ 24° × 30°, deck − / + buttons, and a one-time config upgrade so old configs get the new size. |

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
| `tools/test_deck.py` | 8/8 | joystick navigation (site's `padMove`), SELECT, START (start/resume/retry), PAUSE |
| `tools/test_links.py --inject` | 13/13 | sign-in, Discord/YouTube/shop, other site pages and pop-ups → desktop; game page stays |
| `tools/test_live_injection.py` | pass | bridge injected after load on the unmodified site |

crixgamingvr.com itself wasn't reachable from the build machine, so the repo's copy of the site stood in for it.

## 4. The DLL

`FlappyCrix.dll` is built with Mono against the real BepInEx 5.4.23.2 DLL and reference stand-ins for Unity and
UnityWebBrowser (`tools/refs`). Every Unity member the DLL calls was listed from its IL and checked against Unity's
real API (names, property vs field, overloads such as `LoadImage(Texture2D, byte[], bool)`), and it references
Unity through the `UnityEngine` facade exactly as BepInEx itself does. The DLL loads under Mono with all 62 types
resolving, and the browser suites above were run against the web server inside it.

## Still needs a real headset

- The hidden **Microsoft Edge** engine (tested here with Chromium on Linux; Edge is Chromium-based and uses the same
  protocol, but Edge on Windows hasn't been run). The self test will say whether it worked.
- The stream's **frame decode time** inside Gorilla Tag (logged every 30 s as `Screen stream: ...`).
- Which **shader** Gorilla Tag provides for the screen/deck (logged as `Rendering with shader: ...`).
- **Audio** from the hidden browser (it plays through Windows' default output; the test machine had no sound card,
  but the browser did try to open the audio device).
- Deck buttons/joystick with real hands, laser alignment, Y/B, and `Application.OpenURL` opening the desktop browser.

## In-game self test (every launch)

Three seconds after the page reports ready, the mod sends a real Space key and pointer move, then asks the page for a
report, written to the log and `BrowserData/selftest.txt`. Critical failures (JavaScript, bird sprite, canvas, frames
reaching the screen, key input reaching the page) make the mod switch to the next engine.
