# Changelog

All versions of this mod were made with AI (Claude by Anthropic).
The version stays at 1.0.0 until CRIX decides to change it.

## 1.0.0 — 2026-10-04

- The real Flappy Crix website game on an in-game screen. The website engine is the Microsoft Edge that ships
  with Windows 10/11 (or Chrome/Brave), started hidden — no window — and streamed onto the screen over the
  DevTools protocol. Live page by default, packaged offline copy as fallback. Nothing extra to install.
- Arcade control deck: joystick navigates the site's menus; FLAP, SELECT, START, PAUSE, and − / + screen size.
- Portable: opens in front of you on load; **B** hides, **Y** opens in front of you.
- Sign-in, socials and other site pages open in the desktop browser, never in-game. Only the game page may
  load in the panel.
- Laser pointer, keyboard controls, X/A flap.
- Native Unity fallback (single-image renderer) when no website engine can run.
- In-game self test (`BrowserData/selftest.txt`); logs show the engine, shader, stream rate and decode time.
- Optional UnityWebBrowser engine (`Engine = UnityWebBrowser`).

### Fixed after the first in-game test

- The website didn't run (the first build's engine needed a separate install): replaced by the hidden-Edge engine.
- Menu text was only visible behind your hand (draw-order bug): the screen is now one solid picture and text
  uses a built-in pixel font.
- The screen was too big: now 0.55 m wide at 1.3 m (was 1.0 m at 1.6 m), with − / + buttons; settings files from
  the first build are upgraded to the new size once.
- `confirm()` / `alert()` / `prompt()` no longer freeze the page.

### Fixed after the second in-game test

- **Nothing appeared at all.** The start-up code checked for the optional UnityWebBrowser engine by calling into
  that engine's own code. That engine isn't shipped, so Mono refused to run the whole start-up method, no engine
  started, and the screen was never switched on. The check now only looks for the engine's files; each engine is
  started in its own isolated method; and the screen opens in front of you first, whatever happens to the engines.
- The screen now shows **"Loading..."** while the website starts, and **"Could not start"** (pointing to the log)
  if nothing can run — it is never invisible or blank.
- Each part (screen, deck, laser, controllers, picture, input) is guarded on its own: an error is logged once and
  the rest keeps working.
- Plain .NET calls only (no newer-runtime string overloads), so the DLL fits any Unity version Gorilla Tag uses.
- New checks run on every build: `tools/load-check` (loads the DLL the way a normal install has it and runs the
  start-up — it reproduces this bug on the old DLL) and `tools/api-audit` (every Unity call checked against Unity's
  own source for 2021.3, 2022.3 and Unity 6).

### Fixed after the third in-game test

- **The website showed its offline mode.** Two causes, both fixed:
  - The mod only counted the live site as started at the page's `load` event, which waits for every image,
    sound and script to download. Over the internet that could run past the 12 s the mod allowed (counted from
    game start), so it switched to the packaged copy, which is always offline. Now the game counts as started
    as soon as its own code has run (a fraction of a second after the page arrives). The live site gets
    30 s just to *start* loading, counted from when the hidden browser is up, then as long as it needs.
  - The site decides it's offline if its first quick check (`/robots.txt`, 5 s limit) is slow — likely while
    everything else is still downloading — and in a hidden browser it never checked again. The mod now asks
    it to check again every 6 s (through the site's own `online` handler) until it is back online.
- If the live site really can't be reached, the screen says why ("Playing the offline copy — couldn't load
  crixgamingvr.com (reason)"), and the mod **switches back to the live site by itself** when it can be reached,
  never in the middle of a run.
- **Deck buttons are pressed with your fingertip**, not your palm: the mod uses Gorilla Tag's own fingertip
  points (worked out from the controller if a game update moves them). Each button's ring lights up while a
  fingertip is over it.
- New `tools/live-harness`: the mod's real website engine against a real browser and a stand-in for
  crixgamingvr.com that is slow, down or stalled — 9/9.

### Fixed after the fourth in-game test

- **Works with any browser.** CRIX's PC has no Edge or Chrome (only Opera GX), so the website never started
  ("No Microsoft Edge or Chrome found"). The mod now:
  - finds every browser that can run hidden — all Edge and Chrome channels, Edge's leftover `EdgeCore` engine,
    Brave, Chromium, Vivaldi, Thorium, Supermium, and anything in Windows' installed-browsers list — and tries
    them in turn, remembering any that fail;
  - lists browsers that can't run hidden (Opera / Opera GX, Firefox) in the log instead of opening them;
  - if nothing usable is installed, downloads Google's official Chrome for Testing headless shell once (about
    100 MB, only from Google's servers), with progress on the in-game screen.
- Browser profiles, the downloaded engine and `selftest.txt` moved to `%LOCALAPPDATA%\FlappyCrix` (always
  writable; the game can be under Program Files).
- **SELECT works as FLAP**: it starts a run from the menu, flaps while playing and retries after a game over.
  It only presses a menu item when you've highlighted one with the joystick (or a window like the tutorial
  is open).
- **The screen stands just behind the deck**, like an arcade cabinet: 0.75 m away (was 1.3 m), 0.36 m wide,
  bottom edge just above the deck. − / + resize it upwards in 4 cm steps.
