#!/usr/bin/env python3
"""
Dev-time verification of the packaged website + bridge in headless Chromium,
served by the mod's own C# LocalWebServer (run under Mono/.NET).

    python tools/test_web.py http://127.0.0.1:47321/flappycrix.html [screenshot-dir]

Chromium here stands in for CEF (UnityWebBrowser embeds CEF = Chromium), but
this is NOT a test inside Gorilla Tag. See TESTING.md for the in-game self test.

Simulates UnityWebBrowser's `uwb.ExecuteJsMethod` so the JS -> Unity events can
be checked, blocks ALL non-loopback network traffic (offline check), and drives
the game with real (trusted) keyboard/mouse events, as UWB's SendKeyboardControls
and SendMouseClick do.
"""
import asyncio, json, sys, os
from playwright.async_api import async_playwright

URL = sys.argv[1]
SHOTS = sys.argv[2] if len(sys.argv) > 2 else "."
CHROME = os.environ.get("CHROME", "/opt/pw-browsers/chromium-1194/chrome-linux/chrome")
KNOWN_MISSING = {"/img/pumpkin-head.png", "/img/witch-hat.png", "/img/skeleton-mask.png", "/img/bunny-ears.png",
                 "/img/santa-hat.png", "/img/reindeer-ears.png", "/img/eastermusic.mp3", "/img/witchl.mp3",
                 "/robots.txt", "/photon-realtime-browser.js"}  # robots.txt: intentional, keeps the game in its offline mode

FAKE_UWB = """
window.__unityEvents = [];
window.uwb = { EngineName: 'test-double', ExecuteJsMethod: function(name, msg){
    window.__unityEvents.push(name + '|' + msg); return true; } };
"""

results = []
def check(name, ok, detail=""):
    results.append((name, bool(ok), detail))
    print(("PASS " if ok else "FAIL ") + name + ("  -- " + str(detail) if detail else ""))


async def events(pg):
    return await pg.evaluate("window.__unityEvents.splice(0)")


async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(executable_path=CHROME)
        ctx = await b.new_context(viewport={"width": 1024, "height": 1280})
        await ctx.add_init_script(FAKE_UWB)
        pg = await ctx.new_page()
        bad, external, errors = [], set(), []
        pg.on("pageerror", lambda e: errors.append(str(e)[:200]))
        pg.on("response", lambda r: r.url.startswith("http://127.0.0.1") and r.status >= 400 and bad.append((r.status, r.url)))

        async def route(r):
            if r.request.url.startswith("http://127.0.0.1"):
                await r.continue_()
            else:
                external.add(r.request.url.split("?")[0]); await r.abort()
        await pg.route("**/*", route)

        await pg.goto(URL, wait_until="load", timeout=60000)
        await pg.wait_for_timeout(3000)
        ev = await events(pg)

        # 1. Loading
        local_404 = [u for s, u in bad if "/" + u.split("/", 3)[3].split("?")[0] not in KNOWN_MISSING]
        check("no unexpected local 404s (offline)", not local_404, local_404[:5])
        check("bridge reported ready to Unity", any(e.startswith("FlappyCrixEvent|BridgeReady") for e in ev), ev[:3])
        check("intro video skipped by bridge", await pg.evaluate("document.getElementById('intro').hidden"))
        st = json.loads(await pg.evaluate("FlappyCrixBridge.selfTest()"))
        print("    selfTest:", st)
        check("JavaScript executed (game functions defined)", st.get("js"))
        check("CSS applied", st.get("css"))
        check("custom font loaded", st.get("fontLoaded"))
        check("images loaded", st.get("imagesLoaded", 0) > 5 and st.get("imagesFailed", 1) == 0, f"{st.get('imagesLoaded')} ok / {st.get('imagesFailed')} failed")
        check("bird sprite decoded", st.get("birdSprite"))
        check("audio files fetched", st.get("audioFetched", 0) > 3, st.get("audioFetched"))
        check("MP3 playable by engine", st.get("mp3") in ("probably", "maybe"), st.get("mp3"))
        check("canvas painted (multiple colours)", st.get("canvasColours", 0) >= 2, st.get("canvasColours"))
        await pg.screenshot(path=f"{SHOTS}/1-menu.png")

        # close the first-run tutorial the site shows, using its own close button (as a VR laser click would)
        if await pg.evaluate("document.getElementById('tutorialModal')?.classList.contains('active')"):
            await pg.click("#closeTutorialBtn")
        # 2. Native input path (UWB SendKeyboardControls -> trusted keydown)
        await pg.evaluate("FlappyCrixBridge.start()")
        await pg.wait_for_timeout(400)
        ev = await events(pg)
        check("GameStarted event", any("GameStarted" in e for e in ev), ev[:4])
        check("screen = playing", json.loads(await pg.evaluate("FlappyCrixBridge.state()"))["screen"] == "playing")
        v0 = await pg.evaluate("bird.velocity")
        await pg.keyboard.press("Space")
        v1 = await pg.evaluate("bird.velocity")
        check("native Space key flaps (website's own keydown handler)", v1 < -5, f"velocity {v0:.2f} -> {v1:.2f}")
        check("Space reached page as a key event", json.loads(await pg.evaluate("FlappyCrixBridge.selfTest()"))["inputKeys"] >= 1)
        music = await pg.evaluate("typeof bgMusic!=='undefined' && bgMusic && !bgMusic.paused")
        check("music playing after trusted input", music)

        # 3. Bridge flap + scoring: simple autopilot using FlappyCrixBridge.flap()
        await pg.evaluate("""() => { window.__ap = setInterval(() => {
            const p = pipes.find(p => p.x + pipeWidth > bird.x - 20);
            const target = p ? (p.top + p.bottom) / 2 + 18 : 300;
            if (bird.y > target && bird.velocity > -1) FlappyCrixBridge.flap();
        }, 16); }""")
        await pg.wait_for_timeout(9000)
        sc = await pg.evaluate("score")
        ev = await events(pg)
        check("score increases while flapping via bridge", sc >= 2, f"score {sc}")
        check("ScoreChanged events sent to Unity", any(e.startswith("FlappyCrixEvent|ScoreChanged:") and not e.endswith(":0") for e in ev),
              [e for e in ev if "Score" in e][-3:])
        await pg.screenshot(path=f"{SHOTS}/2-playing.png")
        fps = json.loads(await pg.evaluate("FlappyCrixBridge.selfTest()"))["fps"]
        check("page frame rate", fps >= 30, f"{fps} fps (headless, software rendering)")

        # 4. Pause / resume
        print("    pause:", await pg.evaluate("FlappyCrixBridge.pause()"))
        await pg.wait_for_timeout(300)
        y1 = await pg.evaluate("bird.y"); await pg.wait_for_timeout(500); y2 = await pg.evaluate("bird.y")
        check("pause freezes the game", y1 == y2 and await pg.evaluate("isPaused"))
        print("    resume:", await pg.evaluate("FlappyCrixBridge.resume()"))
        await pg.wait_for_timeout(200)
        check("resume continues", not await pg.evaluate("isPaused"))

        # 5. Game over -> event -> restart via bridge
        await pg.evaluate("clearInterval(window.__ap)")
        ev = []
        for _ in range(40):          # up to 12 s: a picked-up shield power-up can delay the fall
            await pg.wait_for_timeout(300)
            ev += await events(pg)
            if any("GameOver" in e for e in ev):
                break
        check("GameOver event", any("GameOver" in e for e in ev), [e for e in ev if "GameOver" in e or "Screen" in e])
        await pg.screenshot(path=f"{SHOTS}/3-dead.png")
        print("    restart:", await pg.evaluate("FlappyCrixBridge.restart()"))
        await pg.wait_for_timeout(500)
        s = json.loads(await pg.evaluate("FlappyCrixBridge.state()"))
        check("restart starts a fresh run", s["screen"] == "playing" and s["score"] == 0, s)

        # 6. Mouse click on the play area = the site's own tap-to-flap
        v = await pg.evaluate("bird.velocity")
        box = await pg.locator("#canvasWrapper").bounding_box()
        await pg.mouse.click(box["x"] + box["width"] / 2, box["y"] + box["height"] / 2)
        check("native mouse click on canvas flaps", await pg.evaluate("bird.velocity") < -5)

        # 7. Links that would navigate the panel away are blocked
        before = pg.url
        await pg.evaluate("""() => { const a=document.createElement('a'); a.href='https://discord.com/invite/x'; a.id='__t';
                                     a.textContent='x'; document.body.appendChild(a); }""")
        await pg.evaluate("document.getElementById('__t').click()")
        await pg.wait_for_timeout(300)
        ev = await events(pg)
        check("external link kept out of the panel, sent to Unity to open on the desktop", pg.url == before and any("OpenExternal" in e for e in ev))

        check("no uncaught page errors", not errors, errors[:3])
        print("    external requests blocked (offline):", sorted(external))
        await b.close()

    failed = [r for r in results if not r[1]]
    print(f"\n{len(results) - len(failed)}/{len(results)} checks passed")
    sys.exit(1 if failed else 0)

asyncio.run(main())
