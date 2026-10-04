# Changelog

All versions of this mod were made with AI (Claude by Anthropic).

## 1.0.0 — 2026-10-04

- Real Flappy Crix website game on an in-game screen (UnityWebBrowser / CEF), live page by default,
  packaged offline copy as fallback.
- Arcade control deck: joystick navigates the site's menus; FLAP, SELECT, START, PAUSE buttons.
- Portable: opens in front of you on load; **B** hides, **Y** opens in front of you.
- Sign-in, socials and other site pages open in the desktop browser, never in-game.
- Laser pointer, keyboard controls, X/A flap.
- Native Unity fallback when the browser engine is missing or fails its self-test.
- In-game self-test report (`BrowserData/selftest.txt`).
