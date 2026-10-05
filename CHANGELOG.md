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
- **A DLL-only install works**: CRIX's plugin folder had `FlappyCrix.dll` but no `Web` folder, which stopped
  website mode before it even tried the live site. The bridge is now built into the DLL, the live site works
  without the `Web` folder, and if the live site can't load either, the log says the offline copy isn't
  installed (unzip the whole `FlappyCrix` folder to get it).
- **SELECT works as FLAP**: it starts a run from the menu, flaps while playing and retries after a game over.
  It only presses a menu item when you've highlighted one with the joystick (or a window like the tutorial
  is open).
- **The screen stands just behind the deck**, like an arcade cabinet: 0.75 m away (was 1.3 m), 0.36 m wide,
  bottom edge just above the deck. − / + resize it upwards in 4 cm steps.

### Changed after the fifth in-game test

- **Only the real crixgamingvr.com/flappycrix**, exactly as in a desktop browser: no Unity remake and no
  packaged offline copy by default (`AutoFallbackToNative = false`, `UseOfflineCopy = false`). If the site
  can't be reached, the screen says so and the mod keeps retrying until the real page loads.
- **Desktop layout**: the hidden browser window is 1280×800, like a desktop browser, so the page looks the same
  as on the PC (screen 0.6 m wide). `ResolutionWidth/Height = 768/960` gives the old tablet layout.
- **SELECT is for menus only**: it presses what the joystick highlighted (or highlights the first item); it
  never flaps.
- Fixed: a missing `Web` folder made the previous build wrongly mark working browsers as "can't run hidden";
  only real browser start-up failures are remembered now (old list ignored).

### Changed after the sixth request

- **The in-game version is back as the default** (`UseWebsite = false`), rebuilt to play like the website with
  its own layout: the site's game rules tick for tick, its pictures, font, music and sounds, plus everything
  the site has offline — Store (power-ups, hats, trails), Locker, Daily rewards, 16 awards, levels, Settings,
  How to play, pause with quick-buy, game over. Progress is saved in `%LOCALAPPDATA%\FlappyCrix\save.txt`
  (the old native best score carries over).
- **Seasonal themes** on the site's dates (Sydney time): Halloween (bats, moon, pumpkins, cobwebs, the witch,
  Trick or Treat calendar), Christmas (snow, Santa's sleigh, Advent calendar), Easter (eggs, Egg Hunt),
  Birthday (confetti, party hat on 18 March), each with its music and calendar prizes. Settings can choose a
  season or turn them off.
- **Silent while hidden**: B stops the music and sounds, Y brings them back — in website mode too.
- No sign-in, multiplayer, voice or text chat, and nothing opens in the browser — so no "opened on your PC"
  message (removed from website mode as well).
- The in-game version runs and draws on its own thread, so it doesn't cost Gorilla Tag frames.
- Website mode: SELECT no longer flaps during a run (it is for menus only).

### Added after the seventh request

- **Link your crixgamingvr.com account** (LINK ACCOUNT): the game shows a code and a QR code; sign in on
  crixgamingvr.com/link on your phone or PC (Google, or email and password) and enter the code. It uses the
  site's own device-link feature, stays signed in, and saves coins, best score, games, cosmetics, awards and
  level to the account in the website's format, shared both ways (the first sign-in takes your guest progress).
  Banned accounts are recognised.
- **Multiplayer with website players** (PLAY ONLINE) on the website's own Photon rooms: room list, create a room
  (Free Play, Race, Last One Standing, Coin Rush; public or code-only), join by code with an on-screen keyboard,
  the room menu (PAUSE), other birds with names and cosmetics, scores, results, the host's controls, chat from
  website players through the site's filter. Ranked rooms, the seasonal room modes and voice chat stay on the
  website.
- Settings: `OnlineFeatures` (on).
- Tested against stand-ins for crixgamingvr.com, Firebase, Firestore, PlayFab and Photon, and against the
  website's own multiplayer code in Chromium (`tools/online-harness`, 62 checks). Two security problems found on the site are written up in SITE-NOTES.md.

