#!/usr/bin/env python3
"""A stand-in for Photon's servers, speaking what the website's Photon JS SDK (4.4.0.0, JSON
build) speaks: secure WebSockets, subprotocol "Json", frames "~m~<len>~m~~j~{json}".

Name server (:19093) -> master (:19090, lobby + room list) -> game (:19091, rooms, events,
properties), behaving like Photon Realtime for what the site and the mod use. Used by
tools/online-harness to test the mod's client, and the mod against the website's own
multiplayer code running in Chromium (which is pointed here with --host-resolver-rules).

  python3 photon_standin.py --cert cert.pem --key key.pem [--log]
Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
"""
import argparse
import asyncio
import itertools
import json
import ssl
import sys
import time

import websockets

ap = argparse.ArgumentParser()
ap.add_argument("--cert", required=True)
ap.add_argument("--key", required=True)
ap.add_argument("--host", default="127.0.0.1")
ap.add_argument("--advertise", default="127.0.0.1", help="host name put in the master/game addresses")
ap.add_argument("--log", action="store_true")
args = ap.parse_args()

NS_PORT, MASTER_PORT, GAME_PORT = 19093, 19090, 19091
seq = itertools.count(1)
lobby = set()           # master connections in the lobby
rooms = {}              # name -> Room
pending = {}            # name -> create props (between master create and game create)
stats = {"ops": 0, "events": 0}


def log(*a):
    if args.log:
        print(*a, file=sys.stderr, flush=True)


def frame(obj):
    p = "~j~" + json.dumps(obj, separators=(",", ":"))
    return "~m~%d~m~%s" % (len(p), p)


def parse(text):
    text = text.replace("\0", "")
    if text.startswith("~m~"):
        i = 3
        while i < len(text) and text[i].isdigit():
            i += 1
        text = text[i + 3:]
    if text.startswith("~j~"):
        return json.loads(text[3:])
    return None


def pairs(vals):
    return {str(int(vals[i])) if isinstance(vals[i], (int, float)) else str(vals[i]): vals[i + 1] for i in range(0, len(vals) - 1, 2)}


def flat(d):
    out = []
    for k, v in d.items():
        out += [int(k), v]
    return out


class Peer:
    def __init__(self, ws, role):
        self.ws, self.role, self.actor, self.room, self.props = ws, role, 0, None, {}

    async def send(self, obj):
        try:
            await self.ws.send(frame(obj))
        except Exception:
            pass

    async def res(self, op, vals=None, err=0, msg=""):
        o = {"res": op, "err": err, "msg": msg, "vals": flat(vals or {})}    # the SDK needs err (0 = OK)
        await self.send(o)

    async def evt(self, code, vals):
        await self.send({"evt": code, "vals": flat(vals)})


class Room:
    def __init__(self, name, props):
        self.name, self.props, self.actors, self.next_actor = name, dict(props), {}, 1
        self.props.setdefault("253", True)
        self.props.setdefault("254", True)

    def master(self):
        mc = self.props.get("248")
        if mc in self.actors:
            return mc
        return min(self.actors) if self.actors else 0

    def listing(self):
        listed = self.props.get("250") or []
        d = {k: self.props[k] for k in ("255", "253", "254") if k in self.props}
        d["252"] = len(self.actors)
        for k in listed:
            if k in self.props:
                d[k] = self.props[k]
        return d


async def broadcast_room_list_update(name, removed=False):
    r = rooms.get(name)
    if removed or r is None or r.props.get("254") is False:
        entry = {"251": True}
    else:
        entry = r.listing()
    for p in list(lobby):
        await p.evt(229, {"222": {name: entry}})


async def full_room_list(p):
    lst = {n: r.listing() for n, r in rooms.items() if r.props.get("254") is not False}
    await p.evt(230, {"222": lst})


async def handle(ws, role):
    p = Peer(ws, role)
    sid = "session-%09d" % next(seq)
    await ws.send("~m~%d~m~%s" % (len(sid), sid))             # the session id frame (not JSON)
    try:
        async for raw in ws:
            m = parse(raw)
            if m is None:
                continue
            if "irq" in m:
                v = m.get("vals", [])
                await p.send({"irs": 1, "vals": [1, v[1] if len(v) > 1 else 0, 2, int(time.time() * 1000) % 2**31]})
                continue
            if "req" not in m:
                continue
            stats["ops"] += 1
            op, v = int(m["req"]), pairs(m.get("vals", []))
            log(role, "req", op, json.dumps(v)[:200])
            await dispatch(p, op, v)
    except websockets.ConnectionClosed:
        pass
    finally:
        lobby.discard(p)
        if p.room is not None:
            await leave(p)


async def dispatch(p, op, v):
    if p.role == "ns":
        if op == 230:
            if v.get("220") is None or v.get("224") is None:
                return await p.res(230, err=32767, msg="InvalidAuthentication")
            await p.res(230, {"230": "wss://%s:%d" % (args.advertise, MASTER_PORT), "221": "secret-%d" % next(seq), "225": "user-%d" % next(seq)})
        elif op == 220:
            await p.res(220, {"210": ["us", "eu"], "230": ["ns", "ns"]})
        return
    if p.role == "master":
        if op == 230:
            await p.res(230, {})
        elif op == 229:
            lobby.add(p)
            await p.res(229, {})
            await full_room_list(p)
        elif op == 227:
            name = v["255"]
            if name in rooms or name in pending:
                return await p.res(227, err=32766, msg="GameIdAlreadyExists")
            pending[name] = v.get("248", {})
            await p.res(227, {"230": "wss://%s:%d" % (args.advertise, GAME_PORT), "255": name})
        elif op == 226:
            name = v["255"]
            r = rooms.get(name)
            if r is None:
                return await p.res(226, err=32758, msg="GameDoesNotExist")
            if r.props.get("253") is False:
                return await p.res(226, err=32764, msg="GameClosed")
            if len(r.actors) >= int(r.props.get("255") or 99):
                return await p.res(226, err=32765, msg="GameFull")
            await p.res(226, {"230": "wss://%s:%d" % (args.advertise, GAME_PORT), "255": name})
        return
    # game server
    if op == 230:
        await p.res(230, {})
    elif op in (227, 226):
        name = v["255"]
        if op == 227:
            props = pending.pop(name, None) or v.get("248", {})
            if name in rooms:
                return await p.res(227, err=32766, msg="GameIdAlreadyExists")
            rooms[name] = Room(name, props)
        r = rooms.get(name)
        if r is None:
            return await p.res(226, err=32758, msg="GameDoesNotExist")
        p.actor = r.next_actor
        r.next_actor += 1
        p.room = r
        p.props = dict(v.get("249") or {})
        r.actors[p.actor] = p
        if "248" not in r.props:
            r.props["248"] = p.actor
        actor_props = {str(a): q.props for a, q in r.actors.items()}
        out = {"254": p.actor, "248": dict(r.props)}
        if op == 226:
            out["252"] = sorted(r.actors)
            out["249"] = actor_props
        await p.res(op, out)
        for a, q in r.actors.items():
            if a != p.actor:
                await q.evt(255, {"254": p.actor, "249": p.props, "252": sorted(r.actors)})
        await broadcast_room_list_update(name)
    elif op == 253:     # RaiseEvent
        r = p.room
        if r is None:
            return
        stats["events"] += 1
        code, content, recv = int(v.get("244", 0)), v.get("245"), int(v.get("246", 0) or 0)
        for a, q in list(r.actors.items()):
            if recv == 1 or (recv == 0 and a != p.actor) or (recv == 2 and a == r.master()):
                await q.evt(code, {"245": content, "254": p.actor})
    elif op == 252:     # SetProperties
        r = p.room
        if r is None:
            return
        props = v.get("251") or {}
        target = int(v.get("254", 0) or 0)
        if target:
            q = r.actors.get(target)
            if q:
                q.props.update(props)
        else:
            exp = v.get("231")
            if exp and any(r.props.get(k) != val for k, val in exp.items()):
                return await p.res(252, err=-2, msg="expected values didn't match")
            r.props.update(props)
            await broadcast_room_list_update(r.name)
        for a, q in r.actors.items():
            if a != p.actor:
                await q.evt(253, {"253": target, "251": props})
    elif op == 254:     # Leave
        await leave(p)
        await p.res(254, {})


async def leave(p):
    r = p.room
    if r is None:
        return
    p.room = None
    was_master = r.master() == p.actor
    r.actors.pop(p.actor, None)
    if not r.actors:
        rooms.pop(r.name, None)
        await broadcast_room_list_update(r.name, removed=True)
        return
    vals = {"254": p.actor}
    if was_master:
        r.props["248"] = min(r.actors)
        vals["203"] = r.props["248"]
    for q in r.actors.values():
        await q.evt(254, vals)
    await broadcast_room_list_update(r.name)


async def main():
    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.load_cert_chain(args.cert, args.key)
    servers = []
    for port, role in ((NS_PORT, "ns"), (MASTER_PORT, "master"), (GAME_PORT, "game")):
        servers.append(await websockets.serve(lambda ws, role=role: handle(ws, role), args.host, port, ssl=ctx,
                                              subprotocols=["Json"], max_size=2**22, ping_interval=None))
    print("PHOTON STAND-IN READY", flush=True)
    await asyncio.Future()


asyncio.run(main())
