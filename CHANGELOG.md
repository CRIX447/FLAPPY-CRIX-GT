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
