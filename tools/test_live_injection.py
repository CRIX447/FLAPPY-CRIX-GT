import asyncio, json
from playwright.async_api import async_playwright
BRIDGE=open('/home/claude/FlappyCrixMod/mod/FlappyCrix/Web/__flappycrix/bridge.js').read()
async def main():
    async with async_playwright() as p:
        b=await p.chromium.launch(executable_path="/opt/pw-browsers/chromium-1194/chrome-linux/chrome")
        ctx=await b.new_context(viewport={"width":1024,"height":1280})
        await ctx.add_init_script("window.__ev=[];window.uwb={ExecuteJsMethod:(n,m)=>{window.__ev.push(m);return true}};")
        pg=await ctx.new_page()
        await pg.route("**/*", lambda r: r.continue_() if r.request.url.startswith("http://127.0.0.1") else r.abort())
        # unmodified site, as on crixgamingvr.com
        await pg.goto("http://127.0.0.1:8765/flappycrix.html", wait_until="load"); await pg.wait_for_timeout(1500)
        intro_before = await pg.evaluate("!document.getElementById('intro').hidden")
        await pg.evaluate("window.__FLAPPYCRIX_CONFIG={skipIntro:true,disableServiceWorker:true,flapStartsGame:true};\n"+BRIDGE)
        await pg.wait_for_timeout(500)
        ev=await pg.evaluate("window.__ev")
        print("intro playing before injection:", intro_before, "| hidden after:", await pg.evaluate("document.getElementById('intro').hidden"))
        print("BridgeReady after late injection:", any(e.startswith("BridgeReady") for e in ev))
        if await pg.evaluate("document.getElementById('tutorialModal')?.classList.contains('active')"): await pg.click("#closeTutorialBtn")
        print("flap from menu ->", await pg.evaluate("FlappyCrixBridge.flap()"), json.loads(await pg.evaluate("FlappyCrixBridge.state()"))["screen"])
        # double injection is a no-op
        await pg.evaluate(BRIDGE); print("re-inject keeps same object:", await pg.evaluate("FlappyCrixBridge.version"))
        await b.close()
asyncio.run(main())
