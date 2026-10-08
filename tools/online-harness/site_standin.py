#!/usr/bin/env python3
"""Stand-ins for crixgamingvr.com's account services, as the mod uses them:
/api.json, /api/device-link (create / poll, like api/device-link.js), Firebase Auth REST
(accounts:signInWithCustomToken, accounts:lookup, securetoken token refresh), Firestore REST
(users/{uid} GET + PATCH with update masks, typed values) and PlayFab Client (LoginWithCustomID,
Get/UpdatePlayerStatistics). Test helpers: POST /test/claim {code, uid} (the phone approving),
GET/POST /test/doc/{uid}, POST /test/pfserver {on} (api/playfab-login switched on or not).

  python3 site_standin.py --port 8091
Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
"""
import argparse
import base64
import json
import random
import string
import time
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ap = argparse.ArgumentParser()
ap.add_argument("--port", type=int, default=8091)
args = ap.parse_args()

KEY = "TESTKEY"
USERS = {"u1": {"displayName": "Crix Tester", "email": "crix@example.com"}, "banned": {"displayName": "", "email": "naughty@example.com"},
         "u2": {"displayName": "", "email": "second.player@example.com"}}
links = {}          # code -> {token, uid, expires}
docs = {}           # uid -> {"fields": {...}}
pf_stats = {}       # uid -> {name: value}
revoked = set()
calls = []
pf_server = {"on": True}   # api/playfab-login switched on (PLAYFAB_SECRET_KEY set on the site)


def jwt(uid):
    def b(d):
        return base64.urlsafe_b64encode(json.dumps(d).encode()).decode().rstrip("=")
    return b({"alg": "none"}) + "." + b({"user_id": uid, "sub": uid, "exp": int(time.time()) + 3600}) + ".sig"


def uid_from_auth(h):
    tok = (h or "").replace("Bearer ", "")
    try:
        p = tok.split(".")[1]
        p += "=" * (-len(p) % 4)
        return json.loads(base64.urlsafe_b64decode(p))["user_id"]
    except Exception:
        return None


def apply_mask(doc, fields, paths):
    """Firestore's update mask: each path is set from the body, or deleted if the body hasn't it."""
    root = doc.setdefault("fields", {})
    for path in paths:
        parts = path.split(".")
        src, dst = fields, root
        for k in parts[:-1]:
            src = (src or {}).get(k, {}).get("mapValue", {}).get("fields", {})
            dst = dst.setdefault(k, {"mapValue": {"fields": {}}}).setdefault("mapValue", {}).setdefault("fields", {})
        leaf = parts[-1]
        if src is not None and leaf in src:
            dst[leaf] = src[leaf]
        else:
            dst.pop(leaf, None)


class H(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def reply(self, code, obj):
        body = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def body(self):
        n = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(n).decode() if n else ""
        if "x-www-form-urlencoded" in (self.headers.get("Content-Type") or ""):
            return dict(urllib.parse.parse_qsl(raw))
        try:
            return json.loads(raw) if raw else {}
        except Exception:
            return {}

    def do_GET(self):
        u = urllib.parse.urlparse(self.path)
        calls.append("GET " + u.path)
        if u.path == "/api.json":
            return self.reply(200, {"youtube": {"apiKey": "not-for-the-mod"}, "firebase": {"apiKey": KEY, "projectId": "flappy-crix"},
                                    "photon": {"appId": "test-photon-app", "appVersion": "1.0", "region": "us"},
                                    "playfab": {"titleId": "TEST01"}})
        if u.path.startswith("/test/doc/"):
            return self.reply(200, docs.get(u.path[10:], {}))
        if u.path.startswith("/v1/projects/flappy-crix/databases/(default)/documents/"):
            rest = u.path.split("/documents/", 1)[1]
            col, _, docid = rest.partition("/")
            who = uid_from_auth(self.headers.get("Authorization"))
            if not who:
                return self.reply(401, {"error": {"code": 401, "status": "UNAUTHENTICATED"}})
            if col == "users" and docid in docs:
                return self.reply(200, {"name": "x/users/" + docid, **docs[docid]})
            return self.reply(404, {"error": {"code": 404, "status": "NOT_FOUND"}})
        self.reply(404, {"error": "not found"})

    def do_PATCH(self):
        u = urllib.parse.urlparse(self.path)
        b = self.body()
        rest = u.path.split("/documents/", 1)[1]
        col, _, docid = rest.partition("/")
        who = uid_from_auth(self.headers.get("Authorization"))
        calls.append("PATCH " + rest)
        if who != docid:
            return self.reply(403, {"error": {"code": 403, "status": "PERMISSION_DENIED"}})
        paths = urllib.parse.parse_qs(u.query).get("updateMask.fieldPaths", [])
        doc = docs.setdefault(docid, {"fields": {}})
        if paths:
            apply_mask(doc, b.get("fields", {}), paths)
        else:
            doc["fields"] = b.get("fields", {})
        self.reply(200, {"name": "x/" + rest, **doc})

    def do_POST(self):
        u = urllib.parse.urlparse(self.path)
        q = urllib.parse.parse_qs(u.query)
        b = self.body()
        calls.append("POST " + u.path)
        if u.path == "/api/device-link":
            a = b.get("action")
            if a == "create":
                code = "".join(random.choice("ABCDEFGHJKLMNPQRSTUVWXYZ23456789") for _ in range(3)) + "-" + "".join(random.choice("ABCDEFGHJKLMNPQRSTUVWXYZ23456789") for _ in range(3))
                tok = "".join(random.choice("0123456789abcdef") for _ in range(48))
                links[code] = {"token": tok, "uid": None, "expires": time.time() + 600}
                return self.reply(200, {"code": code, "token": tok, "expiresIn": 600})
            if a == "poll":
                code, tok = b.get("code"), b.get("token")
                if not code or not tok:
                    return self.reply(400, {"error": "Missing code or token"})
                d = links.get(code)
                if not d:
                    return self.reply(404, {"error": "expired"})
                if d["token"] != tok:
                    return self.reply(403, {"error": "Not your code"})
                if time.time() > d["expires"]:
                    links.pop(code, None)
                    return self.reply(410, {"error": "expired"})
                if not d["uid"]:
                    return self.reply(200, {"pending": True})
                links.pop(code, None)
                return self.reply(200, {"customToken": "ct-" + d["uid"]})
            return self.reply(400, {"error": "Unknown action"})
        if u.path == "/test/claim":
            d = links.get(b.get("code"))
            if not d:
                return self.reply(404, {"error": "That code is not valid"})
            if b.get("expire"):
                d["expires"] = 0
            else:
                d["uid"] = b.get("uid")
            return self.reply(200, {"ok": True})
        if u.path.startswith("/test/doc/"):
            docs[u.path[10:]] = b
            return self.reply(200, {})
        if u.path == "/test/revoke":
            revoked.add(b.get("uid"))
            return self.reply(200, {})
        if u.path == "/test/calls":
            return self.reply(200, calls)
        if u.path == "/test/pfserver":
            pf_server["on"] = bool(b.get("on"))
            return self.reply(200, {})
        if u.path == "/api/identity":
            # like api/identity.js: a pass for one room, for a signed-in player
            uid = uid_from_auth("Bearer " + (b.get("idToken") or ""))
            if not uid:
                return self.reply(401, {"error": "Not signed in"})
            return self.reply(200, {"pass": "pass-%s-%s-%s" % (uid, b.get("room"), b.get("actor")), "roles": []})
        if u.path == "/api/playfab-login":
            # like api/playfab-login.js: the Firebase sign-in in, the PlayFab session out
            if not pf_server["on"]:
                return self.reply(503, {"error": "not configured"})
            uid = uid_from_auth("Bearer " + (b.get("idToken") or ""))
            if not uid:
                return self.reply(401, {"error": "Not signed in"})
            if uid == "banned":
                return self.reply(403, {"error": "AccountBanned", "ban": {"reason": "Cheating", "until": "Indefinite"}})
            return self.reply(200, {"SessionTicket": "t-" + uid, "PlayFabId": "PF" + uid})
        if u.path.startswith("/v1/") and q.get("key", [""])[0] != KEY:
            return self.reply(400, {"error": {"code": 400, "message": "API key not valid. Please pass a valid API key."}})
        if u.path == "/v1/accounts:signInWithCustomToken":
            tok = b.get("token", "")
            if not tok.startswith("ct-"):
                return self.reply(400, {"error": {"message": "INVALID_CUSTOM_TOKEN"}})
            uid = tok[3:]
            return self.reply(200, {"idToken": jwt(uid), "refreshToken": "rt-" + uid, "expiresIn": "3600"})
        if u.path == "/v1/token":
            rt = b.get("refresh_token", "")
            uid = rt[3:]
            if not rt.startswith("rt-") or uid in revoked:
                return self.reply(400, {"error": {"message": "TOKEN_EXPIRED"}})
            return self.reply(200, {"id_token": jwt(uid), "refresh_token": rt, "user_id": uid, "expires_in": "3600"})
        if u.path == "/v1/accounts:lookup":
            uid = uid_from_auth("Bearer " + b.get("idToken", ""))
            info = USERS.get(uid, {"displayName": "", "email": ""})
            return self.reply(200, {"users": [{"localId": uid, **info}]})
        if u.path == "/Client/LoginWithCustomID":
            uid = b.get("CustomId")
            if uid == "banned":
                return self.reply(403, {"code": 403, "error": "AccountBanned", "errorCode": 1002, "errorMessage": "Cheating"})
            return self.reply(200, {"code": 200, "data": {"SessionTicket": "t-" + uid, "PlayFabId": "PF" + uid}})
        if u.path.startswith("/Client/"):
            uid = (self.headers.get("X-Authorization") or "")[2:]
            if u.path.endswith("GetPlayerStatistics"):
                st = pf_stats.get(uid, {})
                return self.reply(200, {"code": 200, "data": {"Statistics": [{"StatisticName": k, "Value": v} for k, v in st.items()]}})
            if u.path.endswith("UpdatePlayerStatistics"):
                st = pf_stats.setdefault(uid, {})
                for s in b.get("Statistics", []):
                    st[s["StatisticName"]] = s["Value"]
                return self.reply(200, {"code": 200, "data": {}})
        self.reply(404, {"error": "not found"})


srv = ThreadingHTTPServer(("127.0.0.1", args.port), H)
srv.daemon_threads = True
print("SITE STAND-IN READY", flush=True)
srv.serve_forever()
