#!/usr/bin/env python3
"""A stand-in for the live crixgamingvr.com, for tools/live-harness.

Serves the site files the way Vercel does for the real site (clean URLs: /flappycrix ->
flappycrix.html, and /robots.txt exists - the site's own online check asks for it), and
can be made to behave like a slow or broken internet connection:

  --slow-images N:S      the first N .png requests answer after S seconds (the page's "load"
                         event waits for them; the game itself doesn't). Only a few, so the
                         browser's 6 connections per site aren't all held up (the real site is
                         HTTP/2, where they never are).
  --slow-robots-first S  the FIRST /robots.txt answer takes S seconds (longer than the site's
                         5 s limit, so the site decides it is offline)
  --hang                 accept connections but never answer (a stalled connection)

  python3 live_site.py --root mod/FlappyCrix/Web --port 47400 [options]
Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
"""
import argparse
import mimetypes
import os
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ap = argparse.ArgumentParser()
ap.add_argument("--root", required=True)
ap.add_argument("--port", type=int, required=True)
ap.add_argument("--slow-images", default="0:0")
ap.add_argument("--slow-robots-first", type=float, default=0)
ap.add_argument("--hang", action="store_true")
args = ap.parse_args()
ROOT = os.path.realpath(args.root)
SLOW_N, SLOW_S = int(args.slow_images.split(":")[0]), float(args.slow_images.split(":")[1])
robots_lock = threading.Lock()
robots_seen = [0]
pngs_seen = [0]


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *a):
        pass

    def send(self, code, body, ctype="text/plain; charset=utf-8", extra=None):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        for k, v in (extra or {}).items():
            self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            try:
                self.wfile.write(body)
            except (BrokenPipeError, ConnectionResetError):
                pass          # the browser gave up on this request (e.g. the site's 5 s online check)

    def do_HEAD(self):
        self.do_GET()

    def do_GET(self):
        if args.hang:
            time.sleep(3600)
            return
        path = self.path.split("?", 1)[0].split("#", 1)[0]
        if path == "/robots.txt":
            with robots_lock:
                robots_seen[0] += 1
                first = robots_seen[0] == 1
            if first and args.slow_robots_first:
                time.sleep(args.slow_robots_first)
            return self.send(200, b"User-agent: *\nAllow: /\n")
        if path == "/flappycrix.html":                       # Vercel cleanUrls
            return self.send(308, b"", extra={"Location": "/flappycrix"})
        if path in ("/flappycrix", "/flappycrix/"):
            path = "/flappycrix.html"
        full = os.path.realpath(os.path.join(ROOT, path.lstrip("/")))
        if not full.startswith(ROOT + os.sep) or not os.path.isfile(full) or "/__flappycrix/" in full:
            return self.send(404, b"not found")
        if full.endswith(".png") and SLOW_N:
            with robots_lock:
                pngs_seen[0] += 1
                slow = pngs_seen[0] <= SLOW_N
            if slow:
                time.sleep(SLOW_S)
        with open(full, "rb") as f:
            body = f.read()
        ctype = mimetypes.guess_type(full)[0] or "application/octet-stream"
        return self.send(200, body, ctype)


srv = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
srv.daemon_threads = True
print("LIVE SITE READY http://localhost:%d/flappycrix" % args.port, flush=True)
srv.serve_forever()
