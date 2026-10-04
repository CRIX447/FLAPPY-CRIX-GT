#!/usr/bin/env python3
"""Checks the bridge's "everything except the game opens on the PC desktop" rules.
Run it in live-site mode (--inject). The packaged copy runs in the site's own offline
mode, where the site itself blocks sign-in/social links with its "needs the internet" toast.
    python tools/test_links.py http://127.0.0.1:47321/flappycrix.html          (packaged copy, bridge in <head>)
    python tools/test_links.py http://127.0.0.1:8765/flappycrix.html --inject  (unmodified site + bridge injected
                                                                                after load, as on crixgamingvr.com)
"""
import asyncio, sys, os
from playwright.async_api import async_playwright
URL = sys.argv[1]; INJECT = "--inject" in sys.argv
BRIDGE = open(os.path.join(os.path.dirname(__file__), "..", "mod/FlappyCrix/Web/__flappycrix/bridge.js")).read()
res = []
def check(n, ok, d=""): res.append(ok); print(("PASS " if ok else "FAIL ") + n + (f"  -- {d}" if d else ""))

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(executable_path=os.environ.get("CHROME", "/opt/pw-browsers/chromium-1194/chrome-linux/chrome"))
        ctx = await b.new_context(viewport={"width": 768, "height": 960})
        await ctx.add_init_script("window.__ev=[];window.uwb={ExecuteJsMethod:(n,m)=>{window.__ev.push(m);return true}};")
        pg = await ctx.new_page()
        await pg.route("**/*", lambda r: r.continue_() if r.request.url.startswith("http://127.0.0.1") else r.abort())
        await pg.goto(URL, wait_until="load"); await pg.wait_for_timeout(2500)
        if INJECT:
            await pg.evaluate("window.__FLAPPYCRIX_CONFIG={skipIntro:true,flapStartsGame:true,siteOrigin:'https://crixgamingvr.com'};\n" + BRIDGE)
        if await pg.evaluate("document.getElementById('tutorialModal')?.classList.contains('active')"):
            await pg.click("#closeTutorialBtn")
        start_url = pg.url

        async def opened(action, wait=400):
            await pg.evaluate("window.__ev.length=0")
            await pg.wait_for_timeout(1100)          # bridge allows one desktop open per second
            await pg.evaluate(action)
            await pg.wait_for_timeout(wait)
            return [e.split(":", 1)[1] for e in await pg.evaluate("window.__ev") if e.startswith("OpenExternal:")]

        o = await opened("document.getElementById('loginBtn').click()")
        check("SIGN IN opens crixgamingvr.com/flappycrix on the desktop", o == ["https://crixgamingvr.com/flappycrix"], o)
        check("...and the in-panel sign-in dialog stays closed", not await pg.evaluate("document.getElementById('authModal').classList.contains('active')"))

        o = await opened("document.getElementById('authModal').classList.add('active')")
        check("sign-in dialog opened by code -> desktop instead", o == ["https://crixgamingvr.com/flappycrix"] and
              not await pg.evaluate("document.getElementById('authModal').classList.contains('active')"), o)

        o = await opened("[...document.querySelectorAll('a[href]')].find(a=>a.href.includes('discord.com')).click()")
        check("Discord link -> desktop", len(o) == 1 and "discord.com" in o[0], o)
        o = await opened("[...document.querySelectorAll('a[href]')].find(a=>a.href.includes('youtube.com')).click()")
        check("YouTube link -> desktop", len(o) == 1 and "youtube.com" in o[0], o)
        o = await opened("[...document.querySelectorAll('a[href]')].find(a=>a.href.includes('shop.crixgamingvr.com')).click()")
        check("Shop link -> desktop", len(o) == 1 and "shop.crixgamingvr.com" in o[0], o)
        o = await opened("[...document.querySelectorAll('a[href]')].find(a=>a.getAttribute('href')==='/privacy').click()")
        check("Other site page (/privacy) -> https://crixgamingvr.com/privacy on desktop", o == ["https://crixgamingvr.com/privacy"], o)
        o = await opened("window.open('https://www.tiktok.com/@crixgamingvr')")
        check("window.open pop-up -> desktop", o == ["https://www.tiktok.com/@crixgamingvr"], o)
        o = await opened("window.open('https://crixgaming.firebaseapp.com/__/auth/handler?x=1')")
        check("Firebase sign-in pop-up -> game page on desktop", o == ["https://crixgamingvr.com/flappycrix"], o)
        o = await opened("window.open('javascript:alert(1)')")
        check("non-web links ignored", o == [], o)
        check("panel never navigated away from the game", pg.url == start_url, pg.url)
        toast = await pg.evaluate("document.body.innerText.includes('Opened on your PC')")
        check("site's own toast tells the player it opened on the PC", toast)
        await pg.screenshot(path=os.environ.get("SHOT", "/tmp/links.png"))
        o = await opened("(()=>{const a=document.createElement('a');a.href='/flappycrix';a.textContent='g';document.body.appendChild(a);a.click();})()")
        check("link to the game page itself stays in the panel (not sent to the desktop)", o == [], o)
        await b.close()
    print(f"{sum(res)}/{len(res)} passed"); sys.exit(0 if all(res) else 1)
asyncio.run(main())
