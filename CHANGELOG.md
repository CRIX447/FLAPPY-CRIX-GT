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
