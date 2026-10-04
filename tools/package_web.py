#!/usr/bin/env python3
"""
Package the Flappy Crix web game from a checkout of CRIX447/crix-website into
the mod's Web/ folder, so it runs offline from the mod directory.

    python tools/package_web.py <path-to-crix-website> <output-Web-folder>

What it does
  * copies flappycrix.html and every local script it loads
  * copies every /img/... asset referenced from the page or those scripts
  * copies img/emoji/72/ whole (the page builds those paths at runtime)
  * writes a sanitized api.json (the repo's api.json carries a YouTube key and
    online-service config; the offline build needs neither)
  * writes an empty stub for Vercel's analytics script (it only exists on Vercel)
  * keeps the bridge (Web/__flappycrix/) untouched
  * writes FLAPPYCRIX_MANIFEST.json (file list, sizes, sha256, source commit)

It does not modify any of the website's files.
"""
import hashlib, json, os, re, shutil, subprocess, sys

ENTRY = "flappycrix.html"
# Paths the page loads that only exist on the live host.
STUBS = {"_vercel/insights/script.js": "/* Vercel analytics: not available offline */\n"}
# Copied whole because filenames are built at runtime (emojiFile(e)).
WHOLE_DIRS = ["img/emoji/72"]
# Always include, even if not matched literally (font is referenced from CSS).
# NOT packaged on purpose: robots.txt. The game pings /robots.txt to decide
# whether it is online; a 404 puts it in its own built-in offline mode
# (single player, store, locker keep working), which is the truth here.
ALWAYS = ["img/font.woff2", "img/font.otf", "manifest.json"]
# Third-party SDKs the offline build never uses. Left out so the mod only
# redistributes what it needs (Photon's SDK has its own licence terms).
# The page already handles them failing to load (onerror / typeof checks).
EXCLUDE = {"photon-realtime-browser.js"}

REF = re.compile(r"""(?:src|href)\s*=\s*["'](/[^"'?#$]+)|["'`](/(?:img|audio|js|css)/[^"'`?#$\s)]+)|url\(\s*["']?(/[^"')?#]+)""")


def local_refs(text):
    out = set()
    for m in REF.finditer(text):
        p = next(g for g in m.groups() if g)
        if p.startswith("//"):
            continue
        out.add(p.lstrip("/"))
    return out


def main(src, dst):
    src, dst = os.path.abspath(src), os.path.abspath(dst)
    entry = os.path.join(src, ENTRY)
    if not os.path.isfile(entry):
        sys.exit(f"{ENTRY} not found in {src}")

    html = open(entry, encoding="utf-8").read()
    wanted = {ENTRY} | local_refs(html) | set(ALWAYS)

    # Follow local scripts one level (they reference images/sounds too)
    for p in list(wanted):
        fp = os.path.join(src, p)
        if p.endswith(".js") and os.path.isfile(fp):
            wanted |= local_refs(open(fp, encoding="utf-8", errors="ignore").read())

    # Sound effects are loaded as '/img/' + name + '.mp3', so the names never
    # appear as whole paths; take every MP3 in img/ (about 8 MB).
    for f in os.listdir(os.path.join(src, "img")):
        if f.lower().endswith(".mp3"):
            wanted.add(f"img/{f}")

    for d in WHOLE_DIRS:
        for f in os.listdir(os.path.join(src, d)):
            wanted.add(f"{d}/{f}")

    # Clean previous package but keep the bridge
    for item in os.listdir(dst) if os.path.isdir(dst) else []:
        if item == "__flappycrix":
            continue
        path = os.path.join(dst, item)
        shutil.rmtree(path) if os.path.isdir(path) else os.remove(path)
    os.makedirs(dst, exist_ok=True)

    copied, missing = [], []
    for rel in sorted(wanted):
        if rel in STUBS or rel == "api.json" or rel in EXCLUDE:
            continue
        s = os.path.join(src, rel)
        if not os.path.isfile(s):
            missing.append(rel)
            continue
        d = os.path.join(dst, rel)
        os.makedirs(os.path.dirname(d), exist_ok=True)
        shutil.copy2(s, d)
        copied.append(rel)

    for rel, body in STUBS.items():
        d = os.path.join(dst, rel)
        os.makedirs(os.path.dirname(d), exist_ok=True)
        open(d, "w").write(body)
        copied.append(rel)

    # Offline config: no YouTube key, no Firebase/PlayFab/Photon/Discord.
    # The game handles each of these being absent (verified by tools/test_web.py).
    open(os.path.join(dst, "api.json"), "w").write(json.dumps(
        {"_note": "Offline build for the Gorilla Tag mod. Online services are intentionally left out."}, indent=2))
    copied.append("api.json")

    try:
        commit = subprocess.check_output(["git", "-C", src, "log", "-1", "--format=%H %ci"], text=True).strip()
    except Exception:
        commit = "unknown"

    files = []
    for rel in sorted(copied):
        b = open(os.path.join(dst, rel), "rb").read()
        files.append({"path": rel, "bytes": len(b), "sha256": hashlib.sha256(b).hexdigest()})
    json.dump({"source": "https://github.com/CRIX447/crix-website", "commit": commit,
               "entry": ENTRY, "files": files}, open(os.path.join(dst, "FLAPPYCRIX_MANIFEST.json"), "w"), indent=1)

    total = sum(f["bytes"] for f in files)
    print(f"Packaged {len(files)} files, {total/1e6:.1f} MB, from commit {commit}")
    if missing:
        print("Referenced but not in the repo (expected for server routes):")
        for m in missing:
            print("   ", m)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
