#!/usr/bin/env python3
"""Arcade deck -> website: joystick navigation, SELECT, START, PAUSE through the bridge.
Needs the packaged site served on :47321. Made with AI (Claude by Anthropic)."""
import asyncio, json, os
SHOTS = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'docs', 'screenshots')
from playwright.async_api import async_playwright
res=[]
def check(n,ok,d=""): res.append(ok); print(("PASS " if ok else "FAIL ")+n+(f"  -- {d}" if d else ""))
async def main():
    async with async_playwright() as p:
        b=await p.chromium.launch(executable_path=os.environ.get("CHROME","/opt/pw-browsers/chromium-1194/chrome-linux/chrome"))
        pg=await b.new_page(viewport={"width":768,"height":960})
        await pg.route("**/*", lambda r: r.continue_() if r.request.url.startswith("http://127.0.0.1") else r.abort())
        await pg.goto("http://127.0.0.1:47321/flappycrix.html", wait_until="load"); await pg.wait_for_timeout(3000)
        st=lambda: pg.evaluate("JSON.parse(FlappyCrixBridge.state()).screen")
        # Tutorial modal is open on first run: joystick + SELECT should be able to work it
        tut=await pg.evaluate("document.getElementById('tutorialModal').classList.contains('active')")
        r=await pg.evaluate("FlappyCrixBridge.navigate(0,-1)"); await pg.wait_for_timeout(200)
        foc=await pg.evaluate("document.querySelector('.pad-focus')?.id || document.querySelector('.pad-focus')?.textContent.trim().slice(0,20)")
        check("joystick moves highlight (site's padMove)", foc is not None, f"{r}, highlighted: {foc!r}")
        await pg.screenshot(path=os.path.join(SHOTS, "08-joystick-highlight.png"))
        # close tutorial via navigation: move until the close button is highlighted then SELECT
        for _ in range(12):
            if not await pg.evaluate("document.getElementById('tutorialModal').classList.contains('active')"): break
            idn=await pg.evaluate("document.querySelector('.pad-focus')?.id||''")
            if idn=="closeTutorialBtn": await pg.evaluate("FlappyCrixBridge.select()"); await pg.wait_for_timeout(300); break
            await pg.evaluate("FlappyCrixBridge.navigate(0,-1)"); await pg.wait_for_timeout(200)
        check("SELECT activates highlighted item (closed tutorial)", tut and not await pg.evaluate("document.getElementById('tutorialModal').classList.contains('active')"))
        # Navigate to START button on menu and SELECT
        found=False
        for _ in range(40):
            t=await pg.evaluate("(document.querySelector('.pad-focus')||{}).id||''")
            if t=="startGameBtn": found=True; break
            await pg.evaluate("FlappyCrixBridge.navigate(0,-1)"); await pg.wait_for_timeout(180)
        await pg.screenshot(path=os.path.join(SHOTS, "09-start-highlighted.png"))
        await pg.evaluate("FlappyCrixBridge.select()"); await pg.wait_for_timeout(400)
        check("joystick to START + SELECT starts a run", found and await st()=="playing")
        y=await pg.evaluate("bird.velocity"); await pg.evaluate("FlappyCrixBridge.select()")
        check("SELECT while playing flaps", await pg.evaluate("bird.velocity")<-5)
        r=await pg.evaluate("FlappyCrixBridge.navigate(0,1)"); check("joystick ignored while playing", r=="ignored:playing")
        await pg.evaluate("FlappyCrixBridge.togglePause()"); await pg.wait_for_timeout(200)
        check("PAUSE pauses", await st()=="paused")
        await pg.evaluate("FlappyCrixBridge.startButton()"); await pg.wait_for_timeout(200)
        check("START resumes from pause", await st()=="playing")
        for _ in range(40):
            await pg.wait_for_timeout(300)
            if await st()=="dead": break
        await pg.wait_for_timeout(500)
        await pg.evaluate("FlappyCrixBridge.startButton()"); await pg.wait_for_timeout(400)
        check("START retries from game over", await st()=="playing")
        await b.close()
    print(f"{sum(res)}/{len(res)} passed")
asyncio.run(main())
