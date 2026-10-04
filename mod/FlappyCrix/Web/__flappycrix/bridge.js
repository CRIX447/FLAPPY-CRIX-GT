/*
 * Flappy Crix <-> Gorilla Tag bridge
 * ----------------------------------
 * Injected by the FlappyCrix BepInEx plugin. It does NOT change the game's
 * logic. It only:
 *   - exposes a small API Unity can call with ExecuteJs (window.FlappyCrixBridge)
 *   - calls the game's OWN existing functions/buttons (jump, startGame,
 *     togglePause, #dsRetry, #canvasWrapper) - names verified against
 *     flappycrix.html in CRIX447/crix-website
 *   - reports state changes back to Unity through UnityWebBrowser's
 *     uwb.ExecuteJsMethod('FlappyCrixEvent', 'name:payload')
 *
 * Safe to inject twice (remote mode injects after load; local mode injects
 * in <head> before the game's scripts run).
 */
(function () {
    'use strict';
    if (window.top !== window.self) return;          // main page only, never inside iframes
    if (window.FlappyCrixBridge && window.FlappyCrixBridge.version) return;

    var cfg = window.__FLAPPYCRIX_CONFIG || {};
    var VERSION = '1.0.0';

    // ---- environment shims (run before the game's scripts in local mode) ----
    // Skip the "CRIX STUDIOS" intro video: the site already honours this key.
    if (cfg.skipIntro) {
        try { sessionStorage.setItem('crix_intro_seen', '1'); } catch (e) {}
        // Injected after load (live-site mode): the intro may already be playing.
        var skip = document.getElementById('introSkip');
        var intro = document.getElementById('intro');
        if (skip && intro && !intro.hidden) skip.click();
    }
    // A service worker on 127.0.0.1 would cache old copies of the packaged
    // files across mod updates. The site handles a failed register() call.
    if (cfg.disableServiceWorker && navigator.serviceWorker) {
        try {
            navigator.serviceWorker.register = function () {
                return Promise.reject(new Error('Service worker disabled inside Gorilla Tag'));
            };
        } catch (e) {}
    }

    // ---- Everything that isn't the game opens on the PC desktop ----
    // Only the Flappy Crix game page stays in the in-game panel. Sign-in,
    // socials (Discord/YouTube/TikTok/shop) and the site's other pages are
    // handed to Unity, which opens them in the player's normal desktop browser.
    var SITE = (cfg.siteOrigin || 'https://crixgamingvr.com').replace(/\/$/, '');
    var GAME_URL = SITE + '/flappycrix';

    function isGamePage(url) {
        var p = url.pathname.replace(/\/+$/, '').toLowerCase();
        return (p === '/flappycrix' || p === '/flappycrix.html');
    }
    // The packaged copy runs on 127.0.0.1; its other pages are the live site's pages.
    function desktopUrl(url) {
        if (url.origin === location.origin && !/^https?:\/\/(www\.)?crixgamingvr\.com$/i.test(location.origin))
            return SITE + url.pathname + url.search + url.hash;
        return url.href;
    }
    var lastExternal = 0;
    function openOnDesktop(href, why) {
        var url;
        try { url = new URL(href, location.href); } catch (err) { return; }
        if (!/^https?:$/.test(url.protocol)) return;
        var now = Date.now();
        if (now - lastExternal < 1000) return;            // one press = one tab
        lastExternal = now;
        var target = desktopUrl(url);
        send('OpenExternal', target);
        var t = (typeof toast === 'function') ? toast : (typeof window.toast === 'function' ? window.toast : null);
        try {
            if (t) t({ kind: 'info', title: '🖥️ Opened on your PC',
                       body: (why || target.replace(/^https?:\/\//, '').slice(0, 60)) + ' — take your headset off to see it.' });
        } catch (err) {}
    }

    document.addEventListener('click', function (e) {
        // SIGN IN: the account lives in the desktop browser, not the panel
        var login = e.target && e.target.closest ? e.target.closest('#loginBtn') : null;
        if (login) { e.preventDefault(); e.stopPropagation(); openOnDesktop(GAME_URL, 'Sign in at crixgamingvr.com/flappycrix'); return; }

        var a = e.target && e.target.closest ? e.target.closest('a[href]') : null;
        if (!a) return;
        var href = a.getAttribute('href') || '';
        if (href === '' || href.charAt(0) === '#' || href.indexOf('javascript:') === 0) return;
        var url;
        try { url = new URL(href, location.href); } catch (err) { return; }
        if (url.origin === location.origin && isGamePage(url)) return;   // a link to the game itself
        e.preventDefault();
        e.stopPropagation();
        openOnDesktop(url.href);
    }, true);

    // Pop-ups (window.open) go to the desktop too. Firebase sign-in pop-ups
    // would only sign in the desktop browser, so open the game page there instead.
    try {
        window.open = function (u) {
            var s = String(u || '');
            if (/firebaseapp\.com|accounts\.google\.com|__\/auth\//i.test(s)) openOnDesktop(GAME_URL, 'Sign in at crixgamingvr.com/flappycrix');
            else if (s) openOnDesktop(s);
            return null;
        };
    } catch (err) {}

    // Browser dialogs can't be answered in VR (and would freeze the page). Show the site's
    // own toast instead; confirm() answers "no" so nothing destructive happens unseen,
    // prompt() (staff tools only) answers "cancel".
    function siteToast(kind, title, body) {
        var t = (typeof toast === 'function') ? toast : (typeof window.toast === 'function' ? window.toast : null);
        try { if (t) t({ kind: kind, title: title, body: body }); } catch (err) {}
    }
    try {
        window.alert = function (m) { siteToast('info', 'ℹ️ Message', String(m || '').slice(0, 200)); };
        window.confirm = function (m) {
            siteToast('warn', 'Not available in VR', String(m || '').slice(0, 120) + ' — do this on crixgamingvr.com on your PC.');
            return false;
        };
        window.prompt = function () { return null; };
    } catch (err) {}

    // Any code path that opens the in-page sign-in dialog -> close it, sign in on the PC.
    function watchAuth() {
        var m = document.getElementById('authModal');
        if (!m || !window.MutationObserver) return;
        new MutationObserver(function () {
            if (m.classList.contains('active')) {
                m.classList.remove('active');
                openOnDesktop(GAME_URL, 'Sign in at crixgamingvr.com/flappycrix');
            }
        }).observe(m, { attributes: true, attributeFilter: ['class'] });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', watchAuth); else watchAuth();

    // ---- JS -> Unity ----
    var queue = [];
    function send(name, payload) {
        var msg = name + ':' + (payload === undefined || payload === null ? '' : String(payload));
        try {
            // Edge/Chrome engine (DevTools binding)
            if (typeof window.flappyCrixSend === 'function') { window.flappyCrixSend(msg); return; }
            // UnityWebBrowser engine
            if (window.uwb && typeof window.uwb.ExecuteJsMethod === 'function') {
                window.uwb.ExecuteJsMethod('FlappyCrixEvent', msg);
                return;
            }
        } catch (e) { /* fall through to queue */ }
        queue.push(msg);                 // readable by tests / polling
        if (queue.length > 200) queue.shift();
    }

    // ---- helpers that read the game's globals without assuming they exist ----
    // Top-level let/const in the site's classic <script> blocks are global
    // bindings but NOT window properties, so they are read by name via eval.
    function g(name) {
        try { return (0, eval)('typeof ' + name + ' !== "undefined" ? ' + name + ' : undefined'); }
        catch (e) { return undefined; }
    }
    function fn(name) { var f = g(name); return typeof f === 'function' ? f : null; }
    function el(id) { return document.getElementById(id); }
    function visible(e) {
        if (!e) return false;
        var r = e.getBoundingClientRect();
        if (r.width === 0 || r.height === 0) return false;
        var s = getComputedStyle(e);
        return s.display !== 'none' && s.visibility !== 'hidden' && parseFloat(s.opacity || '1') > 0.01;
    }

    function screen() {
        var cls = document.body ? document.body.className : '';
        if (g('isPaused')) return 'paused';
        if (/\bdead\b/.test(cls) || (g('gameOver') && !g('gameRunning'))) return 'dead';
        if (/\bplaying\b/.test(cls) && g('gameRunning')) return 'playing';
        return 'menu';
    }

    function state() {
        return {
            screen: screen(),
            score: g('score') | 0,
            highScore: g('highScore') | 0,
            gameOver: !!g('gameOver'),
            gameRunning: !!g('gameRunning'),
            paused: !!g('isPaused'),
            multiplayer: !!g('mpMatchActive')
        };
    }

    // ---- Unity -> JS actions (all routed through the site's own code) ----
    var api = {
        version: VERSION,

        // Same thing a tap on the play area does in the site itself:
        // canvasWrapper's click handler = "dead ? startGame() : jump()".
        flap: function () {
            var s = screen();
            if (s === 'playing') {
                var j = fn('jump');
                if (j) { j(); return 'jump'; }
            }
            if (s === 'dead') return api.restart();
            if (s === 'menu' && cfg.flapStartsGame) return api.start();
            return 'ignored:' + s;
        },

        // Start a normal single-player run from the menu (the site's own button).
        start: function () {
            var b = el('startGameBtn');
            if (b) { b.click(); return 'startGameBtn'; }
            var sg = fn('startGame');
            if (sg) { sg(); return 'startGame'; }
            return 'unavailable';
        },

        // Retry from the death screen, exactly like pressing its RETRY button.
        restart: function () {
            if (g('isPaused')) api.resume();
            var r = el('dsRetry');
            if (r && visible(r)) { r.click(); return 'dsRetry'; }
            var w = el('canvasWrapper');
            if (w && g('gameOver')) { w.click(); return 'canvasWrapper'; }
            var sg = fn('startGame');
            if (sg) { sg(); return 'startGame'; }
            return 'unavailable';
        },

        pause: function () {
            var t = fn('togglePause');
            if (t && !g('isPaused')) { t(); return 'paused'; }
            return 'ignored';
        },
        resume: function () {
            var r = fn('resumeGame');
            if (r && g('isPaused')) { r(); return 'resumed'; }
            return 'ignored';
        },
        togglePause: function () {
            var t = fn('togglePause');
            if (t) { t(); return 'toggled'; }
            return 'unavailable';
        },

        // Close the topmost modal/toast, like the site's gamepad "B" button.
        back: function () {
            var open = [].slice.call(document.querySelectorAll(
                '.mp-modal.active, .store-modal.active, .locker-modal.active, .achievements-modal.active, .modal.active, [class*="-modal"].active'));
            var top = open.pop();
            if (top) { top.classList.remove('active'); return 'closed'; }
            return 'nothing-open';
        },

        // ---- arcade deck: joystick navigates, SELECT / START / PAUSE buttons ----
        // Uses the site's OWN controller navigation (padMove / padActivate /
        // cyclePadTab, written for gamepads) so the highlight, focus rules and
        // modal scoping are exactly what the site already does.
        navigate: function (dx, dy) {
            if (screen() === 'playing') return 'ignored:playing';
            if (dx !== 0) {
                var tabs = document.querySelectorAll('.mp-modal.active .queue-tab, .store-modal.active .store-tab');
                var ct = fn('cyclePadTab');
                if (tabs.length && ct) { ct(dx > 0 ? 1 : -1); return 'tab'; }
            }
            var pm = fn('padMove');
            var dir = dy !== 0 ? (dy > 0 ? -1 : 1) : (dx > 0 ? 1 : -1);   // stick up = previous item
            if (pm) { pm(dir); return 'move:' + dir; }
            return 'unavailable';
        },

        select: function () {
            var s = screen();
            if (s === 'playing') { var j = fn('jump'); if (j) j(); return 'jump'; }
            var pa = fn('padActivate');
            var focused = document.querySelector('.pad-focus');
            if (pa && focused) { pa(); return 'activate'; }
            // nothing highlighted yet: highlight the first item so the next press activates it
            var pm = fn('padMove');
            if (pm) { pm(1); return 'focus-first'; }
            return 'unavailable';
        },

        // Same as the site's gamepad Start button: start a run when not playing;
        // here it also resumes from pause and retries from game over.
        startButton: function () {
            var s = screen();
            if (s === 'paused') return api.resume();
            if (s === 'dead') return api.restart();
            if (s === 'menu') return api.start();
            return 'ignored:playing';
        },

        state: function () { return JSON.stringify(state()); },
        reportSelfTest: function () { send('SelfTest', api.selfTest()); return 'sent'; },
        drain: function () { var q = queue.slice(); queue.length = 0; return q; },

        // ---- self test: what the plugin reports in the BepInEx log ----
        selfTest: function () {
            var out = {};
            try {
                // JavaScript ran, and the game's script blocks defined their functions
                out.js = !!(fn('jump') && fn('startGame') && fn('setScreen'));
                // CSS: the site's stylesheet styles the start button
                var sb = el('startGameBtn');
                var sbs = sb ? getComputedStyle(sb) : null;
                out.css = !!(document.styleSheets.length > 0 && sbs &&
                    (sbs.fontFamily.toLowerCase().indexOf('crix') !== -1 || sbs.backgroundImage !== 'none'));
                out.fontLoaded = !!(document.fonts && document.fonts.check && document.fonts.check('16px "CrixCustom"'));
                // Resources actually fetched by the page
                var res = (performance.getEntriesByType && performance.getEntriesByType('resource')) || [];
                var imgs = 0, imgsFailed = 0, audio = 0;
                res.forEach(function (r) {
                    var u = r.name.split('?')[0];
                    var ok = r.responseStatus === undefined ? r.transferSize > 0 || r.decodedBodySize > 0 : (r.responseStatus >= 200 && r.responseStatus < 400);
                    if (/\.(png|gif|avif|jpg|jpeg|webp|svg)$/i.test(u)) { if (ok) imgs++; else imgsFailed++; }
                    if (/\.(mp3|ogg|wav|m4a)$/i.test(u) && ok) audio++;
                });
                out.imagesLoaded = imgs; out.imagesFailed = imgsFailed; out.audioFetched = audio;
                // The bird sprite specifically decoded
                var birdOk = false;
                [].slice.call(document.images).forEach(function (im) {
                    if (/bird/i.test(im.src) && im.complete && im.naturalWidth > 0) birdOk = true;
                });
                var bi = g('birdImage') || g('birdImg');
                if (bi && bi.complete && bi.naturalWidth > 0) birdOk = true;
                out.birdSprite = birdOk;
                // Canvas exists and has been painted with more than one colour
                var c = el('gameCanvas');
                out.canvas = !!(c && c.getContext);
                if (c) {
                    try {
                        var ctx = c.getContext('2d');
                        var d = ctx.getImageData(0, 0, c.width, c.height).data, seen = {}, n = 0;
                        for (var i = 0; i < d.length && n < 8; i += 4 * 997) {
                            var k = d[i] + ',' + d[i + 1] + ',' + d[i + 2];
                            if (!seen[k]) { seen[k] = 1; n++; }
                        }
                        out.canvasColours = n;
                    } catch (e) { out.canvasColours = -1; out.canvasError = String(e); }
                }
                // Audio decoding: can this engine play the site's MP3s?
                var a = document.createElement('audio');
                out.mp3 = a.canPlayType ? a.canPlayType('audio/mpeg') : '';
                out.webm = (document.createElement('video').canPlayType || function () { return ''; }).call(
                    document.createElement('video'), 'video/webm; codecs="vp9"');
                out.inputKeys = inputSeen.keys; out.inputClicks = inputSeen.clicks;
                out.fps = pageFps;
                out.screen = screen();
            } catch (e) { out.error = String(e); }
            return JSON.stringify(out);
        }
    };
    window.FlappyCrixBridge = api;

    // ---- observe native input arriving from Unity (for the self test only) ----
    var inputSeen = { keys: 0, clicks: 0 };
    document.addEventListener('keydown', function (e) { if (e.code === 'Space' || e.key === ' ') inputSeen.keys++; }, true);
    document.addEventListener('mousedown', function () { inputSeen.clicks++; }, true);

    // ---- page frame rate ----
    var pageFps = 0, frames = 0, last = 0;
    function tick(t) {
        frames++;
        if (!last) last = t;
        if (t - last >= 1000) { pageFps = Math.round(frames * 1000 / (t - last)); frames = 0; last = t; }
        requestAnimationFrame(tick);
    }
    requestAnimationFrame(tick);

    // ---- state change reporting (JS -> Unity) ----
    var lastScore = -1, lastScreen = '';
    window.addEventListener('crix:gamestart', function () { send('GameStarted', ''); });
    setInterval(function () {
        var s = screen();
        var sc = g('score') | 0;
        if (sc !== lastScore) { lastScore = sc; send('ScoreChanged', sc); }
        if (s !== lastScreen) {
            if (s === 'dead') send('GameOver', sc);
            send('Screen', s);
            lastScreen = s;
        }
    }, 100);

    function ready() { send('BridgeReady', VERSION); }
    if (document.readyState === 'complete') ready();
    else window.addEventListener('load', ready);
})();
