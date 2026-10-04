/**
 * android-bridge.js
 * =================
 * Loaded only inside the Android app. On the web it does nothing, so the same
 * game.html works in both places — which is what keeps crossplay free.
 *
 * Handles the things a browser gets for free but a native shell does not:
 *   - Back button that doesn't kill the app mid-match
 *   - Offline detection with a visible banner
 *   - Keyboard pushing the layout around
 *   - Splash screen timing
 *   - Haptic feedback on taps
 *   - Preventing sleep while playing
 *
 * Add to game.html (before the closing </body>):
 *   <script src="/android-bridge.js"></script>
 */
(function () {
    'use strict';

    // Detect the native shell. The appended user agent comes from
    // capacitor.config.json, so this is reliable rather than a guess.
    const isNative = !!(window.Capacitor && window.Capacitor.isNativePlatform &&
                        window.Capacitor.isNativePlatform()) ||
                     /FlappyCrixAndroid/.test(navigator.userAgent);

    window.__IS_ANDROID_APP__ = isNative;
    if (!isNative) return;   // web build — nothing to do

    console.log('[Android] native bridge active');
    document.documentElement.classList.add('android-app');

    const plugin = name => (window.Capacitor && window.Capacitor.Plugins &&
                            window.Capacitor.Plugins[name]) || null;

    /* ---------------- BACK BUTTON ----------------
       Android's back button closes the app by default. That is fine on the
       main menu, but losing a match to a stray swipe is not — so it closes
       whatever is open first, and only exits after a confirm tap. */
    const App = plugin('App');
    if (App) {
        let exitArmed = false;

        App.addListener('backButton', () => {
            // 1. Close any open modal
            const modal = document.querySelector(
                '.mp-modal.active, .store-modal.active, .locker-modal.active, ' +
                '.achievements-modal.active, .settings-modal.active');
            if (modal) { modal.classList.remove('active'); return; }

            // 2. Leave a lobby rather than the app
            if (window.mpCurrentRoom && typeof window.mpLeaveLobby === 'function') {
                window.mpLeaveLobby();
                toast('Left the lobby');
                return;
            }

            // 3. Pause an active game
            if (window.gameRunning && !window.gameOver) {
                if (typeof window.togglePause === 'function') window.togglePause();
                else toast('Press back again to exit');
                return;
            }

            // 4. Confirm before exiting
            if (exitArmed) { App.exitApp(); return; }
            exitArmed = true;
            toast('Press back again to exit');
            setTimeout(() => { exitArmed = false; }, 2000);
        });

        // Save progress when the app is backgrounded — Android can kill it
        // without warning and unsaved coins would be lost.
        App.addListener('appStateChange', ({ isActive }) => {
            if (!isActive) {
                try { if (typeof window.autoSave === 'function') window.autoSave(); } catch (e) {}
                try { if (window.bgMusic && !window.bgMusic.paused) window.bgMusic.pause(); } catch (e) {}
            }
        });
    }

    /* ---------------- NETWORK ----------------
       The app loads from the live site, so a dropped connection means a blank
       screen. Warn instead of failing silently. */
    const Network = plugin('Network');
    if (Network) {
        let banner = null;
        const show = () => {
            if (banner) return;
            banner = document.createElement('div');
            banner.id = 'offlineBanner';
            banner.textContent = '⚠️ No internet connection';
            banner.style.cssText =
                'position:fixed;top:0;left:0;right:0;z-index:99999;background:#FF4655;' +
                'color:#fff;text-align:center;padding:10px;font-size:13px;font-weight:bold;' +
                'font-family:inherit;padding-top:calc(10px + env(safe-area-inset-top));';
            document.body.appendChild(banner);
        };
        const hide = () => { if (banner) { banner.remove(); banner = null; } };

        Network.addListener('networkStatusChange', s => s.connected ? hide() : show());
        Network.getStatus().then(s => { if (!s.connected) show(); }).catch(() => {});
    }

    /* ---------------- KEYBOARD ----------------
       Without this the chat input hides behind the on-screen keyboard. */
    const Keyboard = plugin('Keyboard');
    if (Keyboard) {
        Keyboard.addListener('keyboardWillShow', info => {
            document.documentElement.style.setProperty('--kb-height', info.keyboardHeight + 'px');
            document.body.classList.add('keyboard-open');
        });
        Keyboard.addListener('keyboardWillHide', () => {
            document.documentElement.style.setProperty('--kb-height', '0px');
            document.body.classList.remove('keyboard-open');
        });
    }

    /* ---------------- HAPTICS ----------------
       A short tick on button presses. Deliberately not on every flap — that
       would drain the battery and get irritating within a minute. */
    const Haptics = plugin('Haptics');
    if (Haptics) {
        document.addEventListener('click', e => {
            if (e.target.closest('button, .action-btn, .icon-btn, .mp-btn-primary, .cyber-stat')) {
                Haptics.impact({ style: 'LIGHT' }).catch(() => {});
            }
        }, { passive: true });

        // A stronger buzz on death, which is worth feeling
        window.addEventListener('crix:death', () =>
            Haptics.impact({ style: 'MEDIUM' }).catch(() => {}));
    }

    /* ---------------- SPLASH ---------------- */
    const Splash = plugin('SplashScreen');
    if (Splash) {
        // Hide once the game has actually drawn, not just when the page loads
        const done = () => setTimeout(() => Splash.hide().catch(() => {}), 300);
        if (document.readyState === 'complete') done();
        else window.addEventListener('load', done);
    }

    /* ---------------- KEEP AWAKE ----------------
       The screen dims mid-match otherwise, since tapping is intermittent. */
    let wakeLock = null;
    async function requestWake() {
        try {
            if ('wakeLock' in navigator && !wakeLock) {
                wakeLock = await navigator.wakeLock.request('screen');
                wakeLock.addEventListener('release', () => { wakeLock = null; });
            }
        } catch (e) {}
    }
    function releaseWake() {
        try { if (wakeLock) { wakeLock.release(); wakeLock = null; } } catch (e) {}
    }
    window.addEventListener('crix:gamestart', requestWake);
    window.addEventListener('crix:gameover', releaseWake);
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) releaseWake();
        else if (window.gameRunning) requestWake();
    });

    /* ---------------- STATUS BAR ---------------- */
    const StatusBar = plugin('StatusBar');
    if (StatusBar) {
        StatusBar.setStyle({ style: 'DARK' }).catch(() => {});
        StatusBar.setBackgroundColor({ color: '#0A0A0F' }).catch(() => {});
    }

    /* ---------------- SIGN-IN ----------------
       Popup OAuth does not work reliably inside a webview, so force the
       redirect flow. Without this Google sign-in silently fails in the app
       while working fine in a browser. */
    window.__FORCE_REDIRECT_AUTH__ = true;


    /* ---------------- EXTERNAL LINKS ----------------
       The app is the GAME. The website, CrixChat, the merch shop, Discord
       and YouTube belong in the phone's real browser — not layered over the
       game, and never loaded inside the app, where the back button offers
       to quit rather than go back.

       This used to hand every link to Browser.open, which is a Chrome Custom
       Tab: an overlay drawn on top of the app, so those pages felt like part
       of the game. And when that call failed it fell back to
       window.open(url, '_system') — a Cordova convention Capacitor does not
       know, so the link simply did nothing. */
    const Browser = plugin('Browser');
    const INTERNAL = ['/flappycrix', '/game.html'];

    function isInternal(href) {
        try {
            const u = new URL(href, location.href);
            if (u.origin !== location.origin) return false;
            return INTERNAL.some(p => u.pathname.startsWith(p));
        } catch (e) { return false; }
    }
    function available(name) {
        try { return !!(window.Capacitor && window.Capacitor.isPluginAvailable &&
                        window.Capacitor.isPluginAvailable(name)); } catch (e) { return false; }
    }

    function openInBrowser(href) {
        let u;
        try { u = new URL(href, location.href); } catch (e) { return; }
        // 1. The phone's actual browser app. Needs @capacitor/app-launcher in
        //    the Android build; when it is there, every link goes this way.
        if (available('AppLauncher')) {
            plugin('AppLauncher').openUrl({ url: u.href }).catch(() => fallback(u));
            return;
        }
        fallback(u);
    }
    function fallback(u) {
        // 2. Another site: navigating to it is handed to Android, which opens
        //    the default browser — Capacitor only keeps its own host inside.
        if (u.host !== location.host) { location.href = u.href; return; }
        // 3. Our own site's other pages would load inside the app that way, so
        //    without the launcher they get a browser tab over the app instead.
        if (Browser) { Browser.open({ url: u.href }).catch(() => { location.href = u.href; }); return; }
        location.href = u.href;
    }
    window.__openInBrowser = openInBrowser;

    document.addEventListener('click', e => {
        const a = e.target.closest('a[href]');
        if (!a) return;
        const href = a.getAttribute('href');
        if (!href || href.startsWith('#') || href.startsWith('javascript:') ||
            href.startsWith('mailto:') || href.startsWith('tel:')) return;
        // Anything that is not the game itself goes to the real browser
        if (!isInternal(href)) {
            e.preventDefault();
            openInBrowser(href);
        }
    }, true);

    // Same for anything opened in code
    const _open = window.open;
    window.open = function (url, target, features) {
        if (url && !isInternal(url)) { openInBrowser(String(url)); return null; }
        return _open.call(window, url, target, features);
    };

    /* ---------------- helpers ---------------- */
    function toast(msg) {
        const t = document.createElement('div');
        t.textContent = msg;
        t.style.cssText =
            'position:fixed;bottom:calc(70px + env(safe-area-inset-bottom));left:50%;' +
            'transform:translateX(-50%);background:rgba(0,0,0,.88);color:#fff;' +
            'padding:11px 20px;border-radius:22px;font-size:13px;z-index:99999;' +
            'font-family:inherit;pointer-events:none;';
        document.body.appendChild(t);
        setTimeout(() => t.remove(), 1800);
    }
    window.__androidToast = toast;
})();
