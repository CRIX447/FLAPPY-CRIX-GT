#!/usr/bin/env python3
"""The mod's multiplayer against the WEBSITE'S OWN multiplayer code.

The real flappycrix.html (from CRIX447/crix-website, with its own photon-realtime-browser.js) runs
in headless Chromium; its Photon connection is pointed at photon_standin.py (Chromium's
--host-resolver-rules maps ns.photonengine.io to 127.0.0.1). The mod's code runs as ModBot.exe.
Both join the same rooms and play: room list, join by code, names and cosmetics, the same pipes
from the host's seed, positions, coins, chat (through the site's filter), host start, Last One
Standing win, Freeplay END, host hand-over.

  python3 interop.py <site folder> <ModBot.exe> <cert.pem> <key.pem>
Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
"""
import asyncio
import json
import os
import subprocess
import sys
import time

from playwright.async_api import async_playwright

SITE, BOT, CERT, KEY = sys.argv[1:5]
HERE = os.path.dirname(os.path.abspath(__file__))
CHROME = os.environ.get("CHROME", "/opt/pw-browsers/chromium-1194/chrome-linux/chrome")
results = []


def check(name, ok, detail=""):
    results.append(bool(ok))
    print(("PASS " if ok else "FAIL ") + name + (("  -- " + str(detail)) if detail != "" else ""), flush=True)


class Bot:
    def __init__(self):
        self.p = subprocess.Popen(["mono", BOT], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=open(os.path.join(HERE, "..", "..", "modbot.log") if False else os.devnull, "w"), text=True, bufsize=1)

    def cmd(self, c):
        self.p.stdin.write(c + "\n")
        self.p.stdin.flush()

    def state(self):
        self.cmd("state")
        return json.loads(self.p.stdout.readline())

    def close(self):
        try:
            self.cmd("quit")
            self.p.wait(3)
        except Exception:
            self.p.kill()


async def wait_for(fn, secs, step=0.1):
    end = time.time() + secs
    while time.time() < end:
        v = fn()
        if asyncio.iscoroutine(v):
            v = await v
        if v:
            return v
        await asyncio.sleep(step)
    return None


HOOKS = r"""
window.__T = { tops: [], results: null, lastBeat: -1, god: false };
setInterval(() => { try {
    if (typeof pipes !== 'undefined' && pipes.length && _lastPipeBeat !== __T.lastBeat) { __T.lastBeat = _lastPipeBeat; __T.tops.push(Math.round(_lastPipeTop * 100) / 100); }
    if (__T.god) _graceLeft = 100000;
} catch (e) {} }, 10);
const __o = mpShowMatchOver;
mpShowMatchOver = function (r, w, m) { __T.results = { r: r, w: w, m: m }; return __o.apply(this, arguments); };
true;
"""


async def site_page(browser):
    pg = await browser.new_page()
    errors = []
    pg.on("pageerror", lambda e: errors.append(str(e)[:200]))
    await pg.route("**/*", lambda r: r.continue_() if r.request.url.startswith("http://127.0.0.1") else r.abort())
    await pg.goto("http://127.0.0.1:8090/flappycrix.html", wait_until="domcontentloaded")
    ok = await wait_for(lambda: pg.evaluate("typeof mpConnected !== 'undefined' && mpConnected"), 30, 0.3)
    await pg.evaluate(HOOKS)
    return pg, ok, errors


def same_prefix(a, b, n=3):
    k = min(len(a), len(b), n)
    return k >= 2 and all(abs(a[i] - b[i]) < 0.02 for i in range(k)), k


async def main():
    standin = subprocess.Popen([sys.executable, os.path.join(HERE, "photon_standin.py"), "--cert", CERT, "--key", KEY],
                               stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
    standin.stdout.readline()
    web = subprocess.Popen([sys.executable, "-m", "http.server", "8090", "--bind", "127.0.0.1"], cwd=SITE,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(1)
    bot = Bot()
    try:
        async with async_playwright() as p:
            browser = await p.chromium.launch(executable_path=CHROME, args=[
                "--host-resolver-rules=MAP ns.photonengine.io 127.0.0.1", "--ignore-certificate-errors", "--no-proxy-server",
                "--autoplay-policy=no-user-gesture-required"])
            pg, ok, errors = await site_page(browser)
            check("the website's own Photon code reaches the lobby (stand-in)", ok)
            bot.cmd("connect")
            check("the mod reaches the same lobby", await wait_for(lambda: bot.state()["phase"] == "Lobby", 20))

            # ---------------- 1. website hosts a Race, the mod joins
            await pg.evaluate("mpSelectedMode = 'race'; mpSelectedQueue = 'casual'; mpSelectedPrivacy = 'public'; mpCreateRoom();")
            code = await wait_for(lambda: pg.evaluate("photonClient.isJoinedToRoom() ? photonClient.myRoom().name.slice(5) : ''"), 15)
            check("website creates a room", code, code)
            st = await wait_for(lambda: (lambda s: s if any(r["name"] == "crix_" + code for r in s["rooms"]) else None)(bot.state()), 10)
            room = [r for r in (st or {"rooms": []})["rooms"] if r["name"] == "crix_" + str(code)]
            check("the mod sees it in the room list, with its name and mode", room and room[0]["mode"] == "race" and room[0]["title"].endswith("Lobby"), room)
            bot.cmd("join " + code.lower())
            st = await wait_for(lambda: (lambda s: s if s["phase"] == "Room" and len(s["players"]) == 2 else None)(bot.state()), 15)
            check("the mod joins by code (mode race, not host)", st and st["mode"] == "race" and not st["host"], st and {k: st[k] for k in ("phase", "mode", "host")})
            mod_actor = st["me"] if st else -1
            web_actor = await pg.evaluate("mpLocalActor")
            ok = await wait_for(lambda: pg.evaluate("(p => p && p.name === 'VR Gorilla' && p.hat === 'bucket' && p.trail === 'trail_rainbow' && p.level === 7)(mpPlayers[%d])" % mod_actor), 10)
            check("the website shows the mod's player: name, hat, trail, level", ok, await pg.evaluate("JSON.stringify(mpPlayers[%d])" % mod_actor))
            st = bot.state()
            webp = [x for x in st["players"] if x["actor"] == web_actor]
            check("the mod has the website player's name", webp and webp[0]["name"].startswith("Guest"), webp)

            bot.cmd("god on")
            await pg.evaluate("__T.god = true")
            await pg.evaluate("mpStartMatch()")
            check("website host STARTs: the mod starts the same match", await wait_for(lambda: bot.state()["matchActive"], 10))
            await asyncio.sleep(7)
            st = bot.state()
            web_tops = await pg.evaluate("__T.tops")
            same, n = same_prefix(st["pipes"], web_tops, 4)
            check("both have the SAME pipes from the website host's seed", same, "mod %s / web %s" % (st["pipes"][:4], web_tops[:4]))
            ok = await pg.evaluate("(p => p && typeof p.simY === 'number' && !p.dead)(mpPlayers[%d])" % mod_actor)
            check("the website receives the mod's bird positions", ok, await pg.evaluate("JSON.stringify({y: mpPlayers[%d].simY, t: mpPlayers[%d].travelled})" % (mod_actor, mod_actor)))
            webp = [x for x in st["players"] if x["actor"] == web_actor]
            check("the mod receives the website bird's positions", webp and webp[0]["seenPos"], webp)
            bot.cmd("auto on")
            got = await wait_for(lambda: bot.state()["coinsTaken"], 15, 0.3)
            if got:
                await asyncio.sleep(0.5)
                ok = await pg.evaluate("[...mpTakenCoins.keys ? mpTakenCoins.keys() : mpTakenCoins].map(Number).includes(%d)" % got[0])
                check("a coin the mod takes vanishes on the website too", ok, got[:3])
            else:
                check("the mod picked up a coin", False)
            await pg.evaluate("photonClient.raiseEvent(5, {text: 'gg you are shit at this'})")
            st = await wait_for(lambda: (lambda s: s if s["chat"] else None)(bot.state()), 5)
            check("website chat reaches the mod through the site's own filter", st and "****" in st["chat"][-1]["text"] and st["chat"][-1]["text"].startswith("gg"), st and st["chat"])

            # host leaves: the mod becomes host
            await pg.evaluate("photonClient.leaveRoom()")
            st = await wait_for(lambda: (lambda s: s if s["host"] and len(s["players"]) == 1 else None)(bot.state()), 10)
            check("the website host leaves: the mod becomes host", st)
            bot.cmd("leave")
            check("the mod goes back to the lobby", await wait_for(lambda: bot.state()["phase"] == "Lobby", 10))
            await wait_for(lambda: pg.evaluate("mpConnected && !photonClient.isJoinedToRoom()"), 10)

            # ---------------- 2. the mod hosts Last One Standing, the website joins
            bot.cmd("auto off")
            bot.cmd("god on")
            bot.cmd("create lastone 1")
            st = await wait_for(lambda: (lambda s: s if s["phase"] == "Room" and s["code"] else None)(bot.state()), 15)
            code = st["code"] if st else ""
            check("the mod creates a room", st and st["host"], code)
            listed = await wait_for(lambda: pg.evaluate("Object.values(photonClient.availableRooms ? photonClient.availableRooms() : []).some(r => r.name === 'crix_%s')" % code), 10)
            check("the website lists the mod's room", listed)
            await pg.evaluate("document.getElementById('mpJoinCodeInput').value = '%s'; _joinInFlight = false; mpJoinByCode();" % code)
            ok = await wait_for(lambda: pg.evaluate("photonClient.isJoinedToRoom()"), 15)
            check("the website joins the mod's room by code", ok)
            await asyncio.sleep(1)
            web_actor = await pg.evaluate("mpLocalActor")
            host = await pg.evaluate("photonClient.myRoomMasterActorNr()")
            st = bot.state()
            check("the website sees the mod as host, and the room's mode", host == st["me"] and await pg.evaluate("mpMatchMode || (photonClient.myRoom().getCustomProperty('mode'))") == "lastone",
                  {"host": host, "mod": st["me"]})
            bot.cmd("start")
            ok = await wait_for(lambda: pg.evaluate("mpMatchActive"), 10)
            check("the mod STARTs: the website starts the same match", ok)
            await pg.evaluate("__T.tops = []; __T.god = true")
            await asyncio.sleep(5)
            st = bot.state()
            web_tops = await pg.evaluate("__T.tops")
            same, n = same_prefix(st["pipes"][-len(web_tops):] if web_tops else [], web_tops, 3)
            check("the website builds the mod host's pipes", same, "mod %s / web %s" % (st["pipes"][-3:], web_tops[:3]))
            await pg.evaluate("__T.god = false; _graceLeft = 0; bird.velocity = 7; bird.y = 589;")
            res = await wait_for(lambda: pg.evaluate("__T.results"), 15)
            check("the website crashes -> the mod (host) ends Last One Standing; the website shows the mod won",
                  res and res["w"] == st["me"], res)
            st = await wait_for(lambda: (lambda s: s if s["phase"] == "Results" else None)(bot.state()), 5)
            check("the mod shows the results with itself as winner (+10 XP)", st and st["winner"] == st["me"] and st["rewardXp"] == 10, st and {k: st[k] for k in ("winner", "me", "rewardXp", "results")})
            bot.cmd("results")

            # ---------------- 3. Freeplay: the mod's END button
            bot.cmd("mode freemode")
            await asyncio.sleep(0.5)
            check("the website follows the host's mode change", await wait_for(lambda: pg.evaluate("mpMatchMode === 'freemode' || photonClient.myRoom().getCustomProperty('mode') === 'freemode'"), 5))
            await pg.evaluate("__T.results = null; __T.god = true")
            bot.cmd("start")
            await wait_for(lambda: pg.evaluate("mpMatchActive"), 10)
            await asyncio.sleep(2)
            bot.cmd("end")
            res = await wait_for(lambda: pg.evaluate("__T.results"), 10)
            check("the mod's END finishes Freeplay on the website (no winner)", res is not None and not res.get("w"), res)

            bot.cmd("leave")
            await asyncio.sleep(1)
            check("no script errors on the website", not errors, errors[:5])
            await browser.close()
    finally:
        bot.close()
        standin.terminate()
        web.terminate()
    print("%d/%d passed" % (sum(results), len(results)))
    sys.exit(0 if all(results) else 1)


asyncio.run(main())
