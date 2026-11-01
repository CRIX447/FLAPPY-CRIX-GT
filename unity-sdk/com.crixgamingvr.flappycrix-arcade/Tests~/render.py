# Draws cabinet.obj (from CabinetPreview) from a few angles into preview.png, with a simple
# z-buffer, textures and light, so the model can be checked without Unity. Needs numpy + Pillow.
#   python3 render.py <folder>
# Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).
import math, os, re, sys
import numpy as np
from PIL import Image

D = sys.argv[1]
W, H = 560, 760


def load_tex(name):
    m = re.match(r".*_(\d+)x(\d+)\.rgba$", name)
    w, h = (int(m.group(1)), int(m.group(2))) if m else (960, 640)
    a = np.frombuffer(open(os.path.join(D, name), "rb").read(), dtype=np.uint8).reshape(h, w, 4)[:, :, :3].astype(np.float32) / 255
    return a  # row 0 = Unity v = 0


mats, cur = {}, None
for line in open(os.path.join(D, "cabinet.mtl")):
    p = line.split()
    if not p: continue
    if p[0] == "newmtl": cur = mats.setdefault(p[1], {"kd": (1, 1, 1), "tex": None, "glow": False})
    elif p[0] == "Kd": cur["kd"] = tuple(map(float, p[1:4]))
    elif p[0] == "glow": cur["glow"] = p[1] == "1"
    elif p[0] == "map_Kd": cur["tex"] = load_tex(p[1])

V, T, N, faces, m = [], [], [], [], None
for line in open(os.path.join(D, "cabinet.obj")):
    p = line.split()
    if not p: continue
    if p[0] == "v": V.append(list(map(float, p[1:4])))
    elif p[0] == "vt": T.append(list(map(float, p[1:3])))
    elif p[0] == "vn": N.append(list(map(float, p[1:4])))
    elif p[0] == "usemtl": m = p[1]
    elif p[0] == "f": faces.append(([int(x.split("/")[0]) - 1 for x in p[1:4]], m))
V, T, N = np.array(V), np.array(T), np.array(N)


def look(eye, target):
    f = np.array(target) - np.array(eye); f /= np.linalg.norm(f)
    r = np.cross(f, [0, 1, 0]); r /= np.linalg.norm(r)   # numpy is right-handed: this is the camera's left in Unity terms
    u = np.cross(r, f)
    return np.array(eye), -r, u, f                      # right, up, forward (Unity, left-handed)


def render(eye, target, fov=38):
    e, r, u, f = look(eye, target)
    img = np.zeros((H, W, 3), np.float32); img[:] = (0.07, 0.07, 0.1)
    zb = np.full((H, W), np.inf, np.float32)
    rel = V - e
    cx, cy, cz = rel @ r, rel @ u, rel @ f
    k = (H / 2) / math.tan(math.radians(fov / 2))
    sx = W / 2 + cx / np.maximum(cz, 1e-3) * k
    sy = H / 2 - cy / np.maximum(cz, 1e-3) * k
    light = np.array([0.4, 0.8, 0.6]); light /= np.linalg.norm(light)
    for idx, mn in faces:
        a, b, c = idx
        if min(cz[a], cz[b], cz[c]) < 0.05: continue
        n = N[a]
        if np.dot(n, V[a] - e) > 0: continue          # facing away
        x = np.array([sx[a], sx[b], sx[c]]); y = np.array([sy[a], sy[b], sy[c]])
        x0, x1 = int(max(0, math.floor(x.min()))), int(min(W - 1, math.ceil(x.max())))
        y0, y1 = int(max(0, math.floor(y.min()))), int(min(H - 1, math.ceil(y.max())))
        if x0 > x1 or y0 > y1: continue
        X, Y = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(d) < 1e-9: continue
        w0 = ((y[1] - y[2]) * (X - x[2]) + (x[2] - x[1]) * (Y - y[2])) / d
        w1 = ((y[2] - y[0]) * (X - x[2]) + (x[0] - x[2]) * (Y - y[2])) / d
        w2 = 1 - w0 - w1
        inside = (w0 >= -1e-4) & (w1 >= -1e-4) & (w2 >= -1e-4)
        if not inside.any(): continue
        iz = w0 / cz[a] + w1 / cz[b] + w2 / cz[c]
        z = 1 / iz
        sub = zb[y0:y1 + 1, x0:x1 + 1]
        draw = inside & (z < sub)
        if not draw.any(): continue
        mat = mats[mn]
        col = np.ones(X.shape + (3,), np.float32) * np.array(mat["kd"], np.float32)
        if mat["tex"] is not None:
            tu = (w0 * T[a][0] / cz[a] + w1 * T[b][0] / cz[b] + w2 * T[c][0] / cz[c]) * z
            tv = (w0 * T[a][1] / cz[a] + w1 * T[b][1] / cz[b] + w2 * T[c][1] / cz[c]) * z
            tex = mat["tex"]; th, tw = tex.shape[:2]
            px = np.clip((tu * tw).astype(int), 0, tw - 1); py = np.clip((tv * th).astype(int), 0, th - 1)
            col = col * tex[py, px]
        if not mat["glow"]:
            lam = max(0.0, float(np.dot(n, light)))
            col = col * (0.35 + 0.75 * lam)
        sub[draw] = z[draw]
        img[y0:y1 + 1, x0:x1 + 1][draw] = np.clip(col[draw], 0, 1)
    return Image.fromarray((img * 255).astype(np.uint8))


views = [
    render((1.7, 1.7, 2.6), (0, 1.0, 0)),          # three quarters, from the front right
    render((0.0, 1.62, 0.95), (0, 1.15, 0.2), 60),  # what a player standing at it sees
    render((-3.3, 1.25, 0.9), (0, 0.97, 0)),        # the side art
]
sheet = Image.new("RGB", (W * len(views), H))
for i, v in enumerate(views): sheet.paste(v, (i * W, 0))
sheet.save(os.path.join(D, "preview.png"))
print("preview.png saved")
