# Third-party notices

## Flappy Crix website (packaged in `Web/`)
From <https://github.com/CRIX447/crix-website> (commit recorded in `Web/FLAPPYCRIX_MANIFEST.json`).
Game code, art, sounds and music belong to CRIX / the repository owner and are
included with the mod for offline play and the in-game version's sounds. The site's UI font (`img/font.otf`, which the site calls
`CrixCustom`) is Bubble Sans, under the SIL Open Font License 1.1 (see below). The repository has no licence file, so redistribution relies on the
owner's permission. Changes: none to the website's files; `api.json` is replaced with an empty offline config,
`_vercel/insights/script.js` is an empty stub, `robots.txt` and `photon-realtime-browser.js` are not included.

**Noto Color Emoji** images (`Web/img/emoji/72/`, credited in the site as "Noto Color Emoji") —
© Google, Apache License 2.0 (`LICENSES/Apache-2.0.txt`). <https://github.com/googlefonts/noto-emoji>

## Built into FlappyCrix.dll (the in-game version)
- **Pictures** (`sprites.bin`, made by `tools/make_sprites.py`): smaller copies of the site's own bird, coin,
  hat, witch and sleigh images (CRIX's art, as above) and of 47 Noto Color Emoji images (© Google, Apache
  License 2.0, `LICENSES/Apache-2.0.txt`; resized, otherwise unchanged).
- **Text font** (`ui-font.bin`, made by `tools/make_font.py`): the site's UI font file `img/font.otf`, which is
  **Bubble Sans** — Copyright 2025 The Bubble Sans Project Authors (https://github.com/abayemes/bubblesans),
  SIL Open Font License 1.1 (`LICENSES/OFL-1.1-BubbleSans.txt`) — drawn into small bitmaps at six sizes.
- **Chat filter**: the site's `filter.js` (CRIX's code, as above) is built in and read by the mod to clean
  multiplayer chat the way the website does.
- **Sounds and music**: the site's own mp3 files (CRIX's, as above) are built in unchanged and unpacked to
  `%LOCALAPPDATA%\FlappyCrix\sounds` for Unity to load.
- No online service's code is included: the mod talks to crixgamingvr.com, Firebase/Firestore, PlayFab and
  Photon with its own code, using the website's public client settings.

## Downloaded website engine (not included)
Google's **Chrome for Testing headless shell** (Chromium; BSD-3-Clause © The Chromium Authors, plus
third-party component licences in the download's licence file) is downloaded by the mod from Google's servers
on PCs with no browser that can run hidden. It is not part of this mod, its releases or this repository.
See DEPENDENCIES.md.

## Optional browser engine (added by `tools/collect_uwb.ps1`)
| Component | Licence | Source |
|---|---|---|
| UnityWebBrowser, UnityWebBrowser.Shared, CEF engine host | MIT © Voltstro-Studios | github.com/Voltstro-Studios/UnityWebBrowser |
| VoltRpc | MIT © Voltstro-Studios | github.com/Voltstro-Studios/VoltRpc |
| NativeArraySpanExtensions | MIT © Voltstro-Studios | github.com/Voltstro-Studios/NativeArraySpanExtensions |
| UniTask | MIT © Yoshifumi Kawai / Cysharp, Inc. | github.com/Cysharp/UniTask |
| Newtonsoft.Json (only if bundled) | MIT © James Newton-King | github.com/JamesNK/Newtonsoft.Json |
| Chromium Embedded Framework | BSD-3-Clause © Marshall A. Greenblatt | bitbucket.org/chromiumembedded/cef |
| Chromium | BSD-3-Clause © The Chromium Authors, plus third-party component licences listed in the engine's credits file | chromium.org |
| Xilium.CefGlue (used by the UWB engine) | MIT/BSD per upstream | gitlab.com/xiliumhq/chromiumembedded/cefglue |

Full licence texts are copied into `LICENSES/` by `collect_uwb.ps1`. The MIT and BSD licences require the
copyright notice and licence text to accompany binary redistribution — keep `LICENSES/` in releases.
