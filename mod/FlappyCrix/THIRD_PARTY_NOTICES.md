# Third-party notices

## Flappy Crix website (packaged in `Web/`)
From <https://github.com/CRIX447/crix-website> (commit recorded in `Web/FLAPPYCRIX_MANIFEST.json`).
Game code, art, sounds, music and the `CrixCustom` font belong to CRIX / the repository owner and are
included with the mod for offline play. The repository has no licence file, so redistribution relies on the
owner's permission. Changes: none to the website's files; `api.json` is replaced with an empty offline config,
`_vercel/insights/script.js` is an empty stub, `robots.txt` and `photon-realtime-browser.js` are not included.

**Noto Color Emoji** images (`Web/img/emoji/72/`, credited in the site as "Noto Color Emoji") —
© Google, Apache License 2.0. <https://github.com/googlefonts/noto-emoji>

## Browser engine (added by `tools/collect_uwb.ps1`)
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
