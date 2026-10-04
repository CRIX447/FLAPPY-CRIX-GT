# Flappy Crix for Gorilla Tag

> 🤖 **This mod was made with AI.** The code, tools and documentation in this repository were written
> with Claude (an AI model by Anthropic), directed and tested by CRIX. Please report anything that
> doesn't work in [Issues](../../issues).

Play the real **Flappy Crix** website game ([crixgamingvr.com/flappycrix](https://crixgamingvr.com/flappycrix))
on a screen inside Gorilla Tag, with an arcade control deck in front of it.

![Size before/after mock-up in the stump](docs/screenshots/13-size-before-after-mockup.png)
<sub>Mock-up: the screen and deck composited onto a stump screenshot at the old and new sizes, showing a real frame from the website engine. Not a capture of the running mod yet.</sub>

## Features

- **The actual website game, whatever browser you use.** The website runs in a **hidden engine** — headless:
  no window, nothing appears on your desktop — and the page is streamed onto the in-game screen. The engine is
  a browser already on your PC that can run hidden (Edge, Chrome, Brave, Vivaldi, Chromium..., even Edge's
  leftover engine on PCs where Edge was removed). If you only have browsers that can't (Opera / Opera GX,
  Firefox), the mod downloads Google's official Chrome for Testing headless shell once (about 100 MB).
  Plays the live page by default and falls back to a copy packaged with the mod if it can't load.
- **Arcade cabinet**: the screen stands just behind the control deck. Joystick to move through the menus,
  **FLAP**, **SELECT** (works as FLAP, or presses what the joystick highlighted), **START**, **PAUSE**, and
  **− / +** to resize the screen. Press the buttons with your fingertip.
- **Portable**: opens in front of you when you load in. **B** hides it, **Y** brings it back in front of you.
- **Sign-in, Discord, YouTube, TikTok, the shop and other site pages open in your PC's normal browser**, never in-game.
- Laser pointer (right hand + trigger) to click anything on the screen.
- **Native fallback**: if no website engine can run, a Unity version of Flappy Crix runs instead.
- Local-only, no colliders (nothing can be climbed), no dependency on Gorilla Tag's internal code.

More pictures in [`docs/screenshots`](docs/screenshots): a real frame from the website engine
(`15-edge-engine-stream-frame.png`), the native fallback (`14-native-fallback.png`), and mock-ups.

## Install

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (x64) into Gorilla Tag and run the game once.
2. Download `FlappyCrix-vX.Y.Z.zip` from [Releases](../../releases) and unzip **the whole `FlappyCrix` folder**
   into `Gorilla Tag/BepInEx/plugins/`, so you have `BepInEx/plugins/FlappyCrix/FlappyCrix.dll` **and**
   `BepInEx/plugins/FlappyCrix/Web/`. (With only the DLL the live site still works, but there's no offline copy.)
3. Start Gorilla Tag. The screen appears in front of you once you've loaded in.

That's it. If your PC has no browser that can run hidden, the first start shows **Getting the website engine**
with a percentage while it downloads (one time). Settings live in `BepInEx/config/com.crix.flappycrix.cfg`
(created on first run); browser profiles, the downloaded engine and `selftest.txt` live in
`%LOCALAPPDATA%\FlappyCrix`.

## Controls

| | VR | Keyboard |
|---|---|---|
| Open in front of you | **Y** | F9 |
| Hide (pauses a run) | **B** | F8 |
| Flap | Deck **FLAP**, or **X** / **A** | Space |
| Move the menu highlight | Deck **joystick** (hold grip on the red knob, push) | Arrow keys |
| Flap / start / retry, or press the highlighted item | Deck **SELECT** (like FLAP unless the joystick highlighted something) | Enter |
| Start / retry / resume | Deck **START** | F5 |
| Pause / resume | Deck **PAUSE** | P |
| Screen smaller / bigger | Deck **−** / **+** | − / = |
| Click on the screen | Laser + trigger | Mouse |

Press deck buttons with your **index fingertip**, like Gorilla Tag's own buttons. A button's ring lights up
while your fingertip is over it, and you feel a buzz when it presses. (The mod uses the game's own fingertip
points; if a game update moves them, it works the fingertip out from your controller instead.)

## How it works

```
Gorilla Tag ─ BepInEx ─ FlappyCrix.dll
                          ├─ hidden engine: a browser on the PC that can run headless, or the
                          │  downloaded Chrome for Testing headless shell (profile in %LOCALAPPDATA%\FlappyCrix)
                          │     ├─ loads crixgamingvr.com/flappycrix (or the packaged copy)
                          │     ├─ bridge.js injected before the site's scripts
                          │     └─ frames ──DevTools protocol──► texture on the in-game screen
                          ├─ input: real key/mouse events into the page (deck, X/A, laser, keyboard)
                          └─ anything that isn't the game ──► your desktop browser
```

- **Input reaches the website's own code.** A flap while playing is a real Space key press, which the site's
  own keydown handler turns into `jump()`. Laser clicks are real mouse clicks. The joystick drives the site's
  own controller navigation (`padMove` / `padActivate`). Nothing in the game's logic is rewritten.
- **Only the game page may load in the panel.** Every page load is checked before it happens; anything else
  (sign-in, Discord login, other site pages) is cancelled and opened in your desktop browser instead.
- **Browser dialogs** (`confirm()`, e.g. "Remove friend?") can't be answered in VR, so they answer "no" and
  show the site's own message; `alert()` becomes a site toast.
- **The hidden browser closes with the game**, even if the game crashes (it is tied to Gorilla Tag's process).
- **Save data** (coins, cosmetics, best score as a guest) lives in `%LOCALAPPDATA%\FlappyCrix\Profile-<browser>`.
  Signing in happens in your desktop browser, not in the panel.

## Settings worth knowing (`com.crix.flappycrix.cfg`)

| Setting | Default | |
|---|---|---|
| `Width` / `Distance` / `HeightOffset` | 0.36 m / 0.75 m / −0.43 m | Screen size, distance, and its **bottom edge** height (just above the deck, like an arcade cabinet; about 27° × 33° of your view). The deck's − / + change `Width`; the screen grows upwards. |
| `UseRemoteWebsite` | `true` | Live page; `false` = packaged copy (offline). |
| `RemoteTimeoutSeconds` | 30 | How long crixgamingvr.com gets to *start* loading before the packaged copy is shown. Once it has started, it gets as long as it needs. |
| `ReconnectSeconds` | 45 | While the packaged copy is showing because the live site couldn't be reached, how often to check again; it switches back by itself between runs. |
| `UseGameFingertips` / `FingertipOffset` | `true` / (0, −0.02, 0.085) | Deck buttons use Gorilla Tag's fingertip points; the offset (metres from the controller) is the backup. |
| `Engine` | `Auto` | `Auto` = a hidden browser from the PC → the downloaded engine → UnityWebBrowser (if installed) → native version. |
| `BrowserPath` | empty | Use a specific browser first. (Opera / Opera GX and Firefox can't run hidden, so they aren't picked automatically.) |
| `DownloadEngine` | `true` | If no browser on the PC can run hidden, download Google's Chrome for Testing headless shell once (~100 MB). `false` = the built-in version instead. |
| `BrowserFrameRate` / `StreamQuality` | 30 / 80 | Screen updates per second and picture quality. Lower these if Gorilla Tag's frame rate drops. |
| `OpenLinksOnDesktop` | `true` | Sign-in/socials/other pages open in your desktop browser. |
| `ShaderOverride` | empty | Advanced: if the screen or deck is invisible, a shader name to use instead (see the log). |

Older settings files are moved to the current screen placement once (and get the longer live-site time once).

## If something's wrong

Send `BepInEx/LogOutput.log` and `%LOCALAPPDATA%\FlappyCrix\selftest.txt`. The log says which
browsers were found and which engine was used (`Mode: Website (...) via hidden msedge ...`), which shader draws the screen, the stream's frame
rate and decode time, and why an engine was skipped. The self test checks JavaScript, CSS, images, audio, the
canvas, frames reaching the screen, key input reaching the page, and the page's frame rate.

When the mod starts, the screen opens in front of you straight away and shows **Loading...** until the website
is ready (usually a few seconds). If no screen appears at all, open `BepInEx/LogOutput.log` and search for
`Flappy Crix`. No lines means BepInEx didn't load the mod (check the folder is `BepInEx/plugins/FlappyCrix/` with
`FlappyCrix.dll` inside); otherwise the lines after it say what went wrong.

| Log says | What to do |
|---|---|
| `Still waiting for the player's camera` | The game hasn't created the player yet; the screen opens as soon as it does. |
| `Couldn't load the live site (...)` | The reason is in brackets, and the screen shows it too. The packaged copy (the site's offline mode) plays meanwhile, and the mod switches back to crixgamingvr.com by itself when it can. |
| `The website says it is offline` while on the live site | The site's own connection check failed; the mod asks it to check again every 6 s. If it never says `online`, something on the PC is blocking crixgamingvr.com for the hidden browser (firewall, antivirus, VPN). |
| `No browser on this PC can run the website hidden` | Normal with only Opera / Opera GX / Firefox: the engine is downloaded once. |
| `Engine download failed: ...` | The PC couldn't reach Google's download servers (firewall/antivirus?). The built-in version runs; it tries again next start. |
| `This browser couldn't run hidden; it won't be tried again` | That browser is skipped from now on (until it's updated); the next one, or the downloaded engine, is used. |
| `DevTools port did not appear` | Something blocked the hidden browser (antivirus, or a work/school policy that disables browser debugging). |
| `Screen stream: ... ms per frame to decode` is high | Lower `BrowserFrameRate` (e.g. 20) or `StreamQuality`. |
| Screen/deck invisible | Note the `Rendering with shader:` line and report it; try `ShaderOverride`. |

## Repository layout

```
src/FlappyCrix/        C# source of the BepInEx plugin
  Web/                 BrowserGame (page rules, bridge, self test), EdgeBrowserGame, UwbBrowserGame,
    Cdp/               DevTools protocol client: launcher, WebSocket, JSON, page controller (no Unity code)
  VR/                  XR input, screen, arcade deck, joystick, laser
  Native/              native fallback: rules + renderer (no Unity code) + Unity glue
mod/FlappyCrix/        the plugin folder as shipped (Web/ = packaged website + bridge.js)
tools/                 tests (engine-harness, native-harness, live-harness, load-check, api-audit, Playwright suites),
                       packaging, build scripts
docs/screenshots/      screenshots and mock-ups
.github/workflows/     builds FlappyCrix.dll and the release zip on every push / tag
```

## Building

**With the game installed:**
```powershell
dotnet build src\FlappyCrix -c Release -p:GamePath="C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag"
```
(The project references the optional UnityWebBrowser DLLs; for a build without them use the Mono route below.)

**Without the game (Linux/macOS/CI, Mono):** compiles against the real BepInEx DLL plus reference
stand-ins for Unity/UnityWebBrowser in `tools/refs/`:
```sh
sudo apt install mono-devel
tools/get_bepinex.sh
tools/refs/build_dll.sh        # -> mod/FlappyCrix/FlappyCrix.dll
tools/load-check/run.sh        # loads it like a normal install and runs the start-up
tools/api-audit/run.sh         # checks every Unity call against Unity's own source
tools/live-harness/run.sh      # the website engine vs a slow / down / stalled stand-in live site
```
GitHub Actions does this automatically; pushing a version tag (e.g. `v1.0.0`) publishes a release with the zip attached.

## Optional: UnityWebBrowser engine

Instead of the PC's Edge, the mod can use UnityWebBrowser's bundled Chromium (set `Engine = UnityWebBrowser`).
That needs its DLLs and engine folder from a Unity build matching Gorilla Tag's Unity version — see
[DEPENDENCIES.md](DEPENDENCIES.md) and `tools/collect_uwb.ps1`. Most people don't need this.

## Testing

What was verified, how, and what still needs a real headset: [TESTING.md](TESTING.md).

## Credits & licences

- Flappy Crix game, art, sounds and font © CRIX — [CRIX447/crix-website](https://github.com/CRIX447/crix-website).
- Mod source code: MIT licence ([LICENSE](LICENSE)). Website content in `mod/FlappyCrix/Web/` is **not** covered by the MIT licence.
- Made with AI: Claude by Anthropic.
- Third-party components: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Not affiliated with Another Axiom or Microsoft. Gorilla Tag is a trademark of Another Axiom.
