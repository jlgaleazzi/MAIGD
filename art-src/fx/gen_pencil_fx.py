"""Generate the pencil-sketch FX SVG masters (T944).

    python art-src/fx/gen_pencil_fx.py      rewrites art-src/fx/{explosion_0..5,smoke_puff,flak_burst_0..3}.svg
    python tools/rasterize.py               then renders them to PNG

Seeded, so re-running gives identical files. Technique (see docs/ART_STYLE.md "Pencil FX"):
sketchy contours drawn 2-3 times with small offsets, cauliflower billow lobes, inner swirl
arcs, 45 deg hatching + cross-hatching (strokes only, clipped to the shape, denser at the
core/underside), wavy wisps, debris flecks with motion lines.
"""
import math
import random
from pathlib import Path

INK = "#E8F1FA"
PEN = "#9DB4CC"
HERE = Path(__file__).resolve().parent


def f(v):
    return f"{v:.1f}".rstrip("0").rstrip(".")


# ---------------------------------------------------------------- shapes
def lobe_points(cx, cy, rx, ry, n, rng, rjit=0.12, rot=0.0):
    pts = []
    for i in range(n):
        a = rot + 2 * math.pi * i / n + rng.uniform(-0.18, 0.18) * 2 * math.pi / n
        k = 1 + rng.uniform(-rjit, rjit)
        pts.append((cx + math.cos(a) * rx * k, cy + math.sin(a) * ry * k))
    return pts


def cloud_d(pts, cx, cy, bulge, rng, jit=0.0, bj=0.0, open_gap=None):
    """Closed lobed path: each chord P_i->P_i+1 bulges outward (quadratic)."""
    n = len(pts)
    pj = [(x + rng.uniform(-jit, jit), y + rng.uniform(-jit, jit)) for x, y in pts]
    d = f"M {f(pj[0][0])} {f(pj[0][1])}"
    gap = open_gap or set()
    for i in range(n):
        x0, y0 = pj[i]
        x1, y1 = pj[(i + 1) % n]
        if i in gap:
            d += f" M {f(x1)} {f(y1)}"
            continue
        mx, my = (x0 + x1) / 2, (y0 + y1) / 2
        ox, oy = mx - cx, my - cy
        ol = math.hypot(ox, oy) or 1
        ch = math.hypot(x1 - x0, y1 - y0)
        b = ch * (bulge + rng.uniform(-bj, bj))
        qx, qy = mx + ox / ol * b, my + oy / ol * b
        d += f" Q {f(qx)} {f(qy)} {f(x1)} {f(y1)}"
    if open_gap is None:
        d += " Z"
    return d


def star_d(cx, cy, r_out, r_in, n, rng, jit=0.0, rot=0.0):
    """Spiky burst outline with slightly concave curved edges."""
    pts = []
    for i in range(2 * n):
        a = rot + math.pi * i / n + rng.uniform(-0.08, 0.08)
        r = (r_out if i % 2 == 0 else r_in) * (1 + rng.uniform(-0.15, 0.15))
        pts.append((cx + math.cos(a) * r + rng.uniform(-jit, jit),
                    cy + math.sin(a) * r + rng.uniform(-jit, jit)))
    d = f"M {f(pts[0][0])} {f(pts[0][1])}"
    for i in range(len(pts)):
        x0, y0 = pts[i]
        x1, y1 = pts[(i + 1) % len(pts)]
        mx, my = (x0 + x1) / 2, (y0 + y1) / 2
        qx, qy = mx + (cx - mx) * 0.12, my + (cy - my) * 0.12
        d += f" Q {f(qx)} {f(qy)} {f(x1)} {f(y1)}"
    return d + " Z"


def hatch_d(ex, ey, erx, ery, ang_deg, sp, rng, wob=0.6, trim=0.25):
    """Hatch strokes at ang_deg filling an ellipse, ends trimmed randomly, slight wobble."""
    a = math.radians(ang_deg)
    dx, dy = math.cos(a), math.sin(a)
    nx, ny = -dy, dx
    R = max(erx, ery)
    t = -R + rng.uniform(0, sp)
    d = []
    while t < R:
        px, py = ex + nx * t, ey + ny * t
        # solve ((px+dx u - ex)/erx)^2 + ((py+dy u - ey)/ery)^2 = 1
        A = (dx / erx) ** 2 + (dy / ery) ** 2
        B = 2 * ((px - ex) * dx / erx ** 2 + (py - ey) * dy / ery ** 2)
        C = ((px - ex) / erx) ** 2 + ((py - ey) / ery) ** 2 - 1
        disc = B * B - 4 * A * C
        if disc > 0:
            s = math.sqrt(disc)
            u0, u1 = (-B - s) / (2 * A), (-B + s) / (2 * A)
            L = u1 - u0
            u0 += rng.uniform(0, trim) * L
            u1 -= rng.uniform(0, trim) * L
            if u1 - u0 > 1.5:
                x0, y0 = px + dx * u0, py + dy * u0
                x1, y1 = px + dx * u1, py + dy * u1
                w = rng.uniform(-wob, wob)
                mx, my = (x0 + x1) / 2 + nx * w, (y0 + y1) / 2 + ny * w
                d.append(f"M {f(x0)} {f(y0)} Q {f(mx)} {f(my)} {f(x1)} {f(y1)}")
        t += sp * rng.uniform(0.8, 1.2)
    return " ".join(d)


def swirls_d(cx, cy, rx, ry, count, size, rng, up=True):
    """Inner cauliflower arcs: short C-curves suggesting billows inside the mass."""
    d = []
    for _ in range(count):
        a = rng.uniform(0, 2 * math.pi)
        r = math.sqrt(rng.uniform(0.05, 0.6))
        x, y = cx + math.cos(a) * rx * r, cy + math.sin(a) * ry * r
        s = size * rng.uniform(0.7, 1.3)
        tilt = rng.uniform(-0.6, 0.6)
        x0, y0 = x - s * math.cos(tilt), y - s * math.sin(tilt)
        x1, y1 = x + s * math.cos(tilt), y + s * math.sin(tilt)
        k = -1 if up else 1
        q1x, q1y = x0 + s * 0.2 * math.sin(tilt), y0 + k * s * 1.1
        q2x, q2y = x1, y1 + k * s * 1.1
        d.append(f"M {f(x0)} {f(y0)} C {f(q1x)} {f(q1y)} {f(q2x)} {f(q2y)} {f(x1)} {f(y1)}")
    return " ".join(d)


def wisp_d(x, y, ang_deg, length, amp, waves, rng):
    """Wavy flowing smoke line (no straight segments): sequence of S-curves."""
    a = math.radians(ang_deg)
    dx, dy = math.cos(a), math.sin(a)
    nx, ny = -dy, dx
    seg = length / waves
    d = f"M {f(x)} {f(y)}"
    side = rng.choice((-1, 1))
    for i in range(waves):
        k = min(amp, seg * 0.3) * (1 - 0.4 * i / waves) * rng.uniform(0.7, 1.2) * side
        bx, by = x + dx * seg, y + dy * seg
        c1x, c1y = x + dx * seg * 0.33 + nx * k, y + dy * seg * 0.33 + ny * k
        c2x, c2y = x + dx * seg * 0.66 + nx * k, y + dy * seg * 0.66 + ny * k
        d += f" C {f(c1x)} {f(c1y)} {f(c2x)} {f(c2y)} {f(bx)} {f(by)}"
        x, y = bx, by
        side = -side
        a += rng.uniform(-0.25, 0.25)
        dx, dy = math.cos(a), math.sin(a)
        nx, ny = -dy, dx
    return d


def debris(cx, cy, n, r0, r1, size, trail, rng, rot=0.0):
    """Small sketched shards + 2 short motion lines pointing back toward the centre."""
    shards, trails = [], []
    for i in range(n):
        a = rot + 2 * math.pi * i / n + rng.uniform(-0.35, 0.35)
        r = rng.uniform(r0, r1)
        x, y = cx + math.cos(a) * r, cy + math.sin(a) * r
        s = size * rng.uniform(0.7, 1.3)
        k = rng.randint(3, 4)
        p = []
        for j in range(k):
            b = 2 * math.pi * j / k + rng.uniform(-0.4, 0.4)
            p.append((x + math.cos(b) * s * rng.uniform(0.6, 1.1),
                      y + math.sin(b) * s * rng.uniform(0.6, 1.1)))
        shards.append("M " + " L ".join(f"{f(px)} {f(py)}" for px, py in p) + " Z")
        dx, dy = math.cos(a), math.sin(a)
        nx, ny = -dy, dx
        for off, ln in ((-1.3, trail), (1.3, trail * 0.6)):
            sx, sy = x - dx * (s + 1.5) + nx * off, y - dy * (s + 1.5) + ny * off
            ex, ey = sx - dx * ln, sy - dy * ln
            trails.append(f"M {f(sx)} {f(sy)} L {f(ex)} {f(ey)}")
    return " ".join(shards), " ".join(trails)


def rays_d(cx, cy, n, r0, r1, rng, rot=0.0, curve=1.2):
    d = []
    for i in range(n):
        a = rot + 2 * math.pi * i / n + rng.uniform(-0.12, 0.12)
        ra = r0 * rng.uniform(0.9, 1.1)
        rb = r1 * rng.uniform(0.7, 1.05)
        x0, y0 = cx + math.cos(a) * ra, cy + math.sin(a) * ra
        x1, y1 = cx + math.cos(a) * rb, cy + math.sin(a) * rb
        nx, ny = -math.sin(a), math.cos(a)
        w = rng.uniform(-curve, curve)
        mx, my = (x0 + x1) / 2 + nx * w, (y0 + y1) / 2 + ny * w
        d.append(f"M {f(x0)} {f(y0)} Q {f(mx)} {f(my)} {f(x1)} {f(y1)}")
    return " ".join(d)


# ---------------------------------------------------------------- svg assembly
class Svg:
    def __init__(self, size):
        self.size = size
        self.defs = []
        self.body = []

    def path(self, d, color=INK, w=0.75, op=1.0, extra=""):
        if not d:
            return
        o = "" if op >= 1 else f' opacity="{op:.2f}"'
        self.body.append(f'  <path d="{d}" stroke="{color}" stroke-width="{w}"{o}{extra}/>')

    def clip(self, cid, d):
        self.defs.append(f'    <clipPath id="{cid}"><path d="{d}"/></clipPath>')

    def open_group(self, gid, clip=None):
        c = f' clip-path="url(#{clip})"' if clip else ""
        self.body.append(f'  <g id="{gid}"{c}>')

    def close_group(self):
        self.body.append("  </g>")

    def text(self):
        s = self.size
        out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{s}" height="{s}" viewBox="0 0 {s} {s}">']
        if self.defs:
            out += ["  <defs>"] + self.defs + ["  </defs>"]
        out.append('  <g fill="none" stroke-linecap="round" stroke-linejoin="round">')
        out += ["  " + b for b in self.body]
        out.append("  </g>")
        out.append("</svg>")
        return "\n".join(out) + "\n"


def billow_mass(svg, rng, tag, cx, cy, rx, ry, n, bulge, color, ops, widths, jit,
                hatch=None, swirl=None, gap=None):
    """Lobed mass: hatching (clipped), swirl arcs, then a 2-3 pass sketchy contour."""
    pts = lobe_points(cx, cy, rx, ry, n, rng)
    base = cloud_d(pts, cx, cy, bulge, random.Random(rng.random()))
    svg.clip(f"{tag}c", base)
    if hatch:
        svg.open_group(f"{tag}_hatch", clip=f"{tag}c")
        for (hx, hy, hrx, hry, ang, sp, hc, hw, hop) in hatch:
            svg.path(hatch_d(hx, hy, hrx, hry, ang, sp, rng), hc, hw, hop)
        svg.close_group()
    if swirl:
        cnt, sz, sc, sw, sop = swirl
        svg.path(swirls_d(cx, cy, rx, ry, cnt, sz, rng), sc, sw, sop)
    for i, (op, w) in enumerate(zip(ops, widths)):
        sub = random.Random(rng.random())
        d = cloud_d(pts, cx, cy, bulge, sub, jit=0 if i == 0 else jit, bj=0.06,
                    open_gap=gap)
        svg.path(d, color, w, op)
    return pts


# ---------------------------------------------------------------- frames
C = 64


def explosion(frame):
    rng = random.Random(9440 + frame)
    s = Svg(128)
    if frame == 0:
        # Bright tight burst: spiky outer flash, cauliflower core, rays, debris.
        s.open_group("rays")
        s.path(rays_d(C, C, 14, 30, 56, rng), INK, 1.0, 0.9)
        s.path(rays_d(C, C, 10, 28, 44, rng, rot=0.22), PEN, 0.5, 0.8)
        s.close_group()
        s.clip("starc", star_d(C, C, 40, 26, 11, random.Random(70), rot=0.1))
        s.open_group("star_hatch", clip="starc")
        s.path(hatch_d(C, C + 18, 34, 16, 45, 3.6, rng), PEN, 0.5, 0.7)
        s.close_group()
        for i, (op, w, j) in enumerate(((0.95, 1.25, 0), (0.6, 0.75, 1.6), (0.4, 0.5, 2.0))):
            s.path(star_d(C, C, 40, 26, 11, random.Random(70), jit=j, rot=0.1), INK, w, op)
        billow_mass(s, rng, "core", C, C + 1, 20, 18, 8, 0.55, INK, (1.0, 0.6, 0.45),
                    (1.25, 0.75, 0.5), 1.8,
                    hatch=[(C, C + 13, 22, 12, 45, 3.2, INK, 0.5, 0.75),
                           (C, C + 15, 18, 8, -45, 3.4, INK, 0.5, 0.6)],
                    swirl=(5, 4.5, INK, 0.75, 0.85))
        sh, tr = debris(C, C, 7, 46, 54, 2.8, 8, rng, rot=0.4)
        s.path(sh, INK, 0.75, 0.95)
        s.path(tr, INK, 0.5, 0.75)
    elif frame in (1, 2, 3):
        # Growing billows; spikes fade, mass rises and darkens underneath.
        rx = (30, 40, 46)[frame - 1]
        ry = (27, 35, 40)[frame - 1]
        lift = (0, -2, -3)[frame - 1]
        n = (9, 10, 11)[frame - 1]
        col_out = INK if frame < 3 else PEN
        if frame == 1:
            for op, w, j in ((0.5, 0.75, 0), (0.3, 0.5, 1.6)):
                s.path(star_d(C, C, 48, 34, 12, random.Random(71), jit=j), INK, w, op)
            s.path(rays_d(C, C, 10, 36, 56, rng, rot=0.15), PEN, 0.5, 0.7)
        hatch = [(C, C + lift + ry * 0.45, rx * 0.95, ry * 0.6, 45, 3.0, INK, 0.5, 0.7),
                 (C, C + lift + ry * 0.55, rx * 0.8, ry * 0.4, -45, 3.2, INK, 0.5, 0.6),
                 (C - rx * 0.3, C + lift - ry * 0.1, rx * 0.35, ry * 0.3, 45, 4.5, PEN, 0.5, 0.6)]
        if frame >= 2:
            hatch.append((C, C + lift + ry * 0.7, rx * 0.6, ry * 0.25, 90, 3.0, PEN, 0.5, 0.6))
        billow_mass(s, rng, "mass", C, C + lift, rx, ry, n, 0.55, col_out,
                    (1.0, 0.65, 0.4), (1.25, 0.75, 0.5), 2.0,
                    hatch=hatch, swirl=(4 + 2 * frame, 4.5 + frame, INK, 0.75, 0.8))
        # Bright inner core shrinking over time.
        if frame < 3:
            core_r = (14, 11)[frame - 1]
            billow_mass(s, rng, "core", C + 2, C + lift + 2, core_r, core_r * 0.85, 7, 0.65, INK,
                        (0.95, 0.5), (1.0, 0.5), 1.2)
        sh, tr = debris(C, C, (6, 5, 4)[frame - 1], rx + 6, rx + 11, 2.6, (7, 8, 6)[frame - 1],
                        rng, rot=0.4 + frame * 0.3)
        s.path(sh, INK, 0.75, 0.9 - 0.15 * (frame - 1))
        s.path(tr, PEN, 0.5, 0.8 - 0.1 * (frame - 1))
        if frame == 3:
            for k in range(3):
                x = C - 20 + 20 * k
                s.path(wisp_d(x, C + lift - ry + 2, -90 + (x - C) * 0.8, 16, 2.5, 2, rng),
                       PEN, 0.75, 0.6)
    else:
        # 4-5: dissipating: broken billow fragments and loose wavy wisps.
        op = 0.75 if frame == 4 else 0.5
        rx, ry = (44, 38)[frame - 4], (34, 28)[frame - 4]
        lift = (-2, -4)[frame - 4]
        gap = {1, 4, 7} if frame == 4 else {0, 2, 3, 5, 7, 8}
        hatch = [(C, C + lift + ry * 0.4, rx * 0.8, ry * 0.5, 45, 4.5 if frame == 4 else 6,
                  PEN, 0.5, op)]
        if frame == 4:
            hatch.append((C, C + lift + ry * 0.5, rx * 0.5, ry * 0.3, -45, 4.5, PEN, 0.5, op * 0.8))
        billow_mass(s, rng, "mass", C, C + lift, rx, ry, 10, 0.5, PEN,
                    (op, op * 0.7), (1.0, 0.5), 1.8, hatch=hatch,
                    swirl=(5 if frame == 4 else 3, 5.5, PEN, 0.75, op * 0.9), gap=gap)
        nw = 6 if frame == 4 else 5
        for k in range(nw):
            x = C - rx * 0.8 + k * (1.6 * rx / (nw - 1)) + rng.uniform(-3, 3)
            y = C + lift - ry * rng.uniform(0.3, 0.8)
            ang = -90 + (x - C) * 0.6 + rng.uniform(-10, 10)
            ln = rng.uniform(18, 24) if frame == 4 else rng.uniform(20, 26)
            w = wisp_d(x, y, ang, ln, 3.0, 2, rng)
            s.path(w, PEN, 0.75, op)
            s.path(wisp_d(x + 1.5, y + 1, ang + 4, ln * 0.7, 2.5, 2, rng), PEN, 0.5, op * 0.6)
    return s.text()


def smoke_puff():
    rng = random.Random(9450)
    s = Svg(48)
    cx, cy = 24, 25
    billow_mass(s, rng, "puff", cx, cy, 16, 13.5, 7, 0.5, INK, (0.9, 0.55, 0.35),
                (1.0, 0.6, 0.5), 1.1,
                hatch=[(cx, cy + 7, 15, 9, 45, 2.6, PEN, 0.6, 0.9),
                       (cx + 1, cy + 9, 11, 5, -45, 2.8, PEN, 0.5, 0.8)],
                swirl=(2, 3.5, INK, 0.6, 0.7))
    s.path(wisp_d(cx - 4, cy - 13, -100, 6, 1.6, 2, rng), PEN, 0.6, 0.7)
    s.path(wisp_d(cx + 5, cy - 12, -75, 5, 1.4, 2, rng), PEN, 0.5, 0.6)
    return s.text()


def flak(frame):
    rng = random.Random(9460 + frame)
    s = Svg(96)
    c = 48
    if frame == 0:
        # Flash: bold ink jagged star (shape = threat, white only), dense
        # cross-hatched core, ink rays, small sketchy puff ring.
        s.path(rays_d(c, c, 12, 16, 40, rng), INK, 0.75, 0.9)
        s.clip("flashc", star_d(c, c, 20, 11, 9, random.Random(80)))
        s.open_group("flash_hatch", clip="flashc")
        s.path(hatch_d(c, c, 14, 14, 45, 1.7, rng, trim=0.05), INK, 0.6, 0.9)
        s.path(hatch_d(c, c, 11, 11, -45, 1.9, rng, trim=0.08), INK, 0.5, 0.8)
        s.close_group()
        for op, w, j in ((1.0, 1.75, 0), (0.7, 0.75, 1.2), (0.45, 0.5, 1.8)):
            s.path(star_d(c, c, 20, 11, 9, random.Random(80), jit=j), INK, w, op)
        for op, w, j in ((0.9, 1.0, 0), (0.45, 0.5, 1.2)):
            s.path(cloud_d(lobe_points(c, c, 9, 8, 6, random.Random(81)), c, c, 0.5,
                           random.Random(82), jit=j), INK, w, op)
        sh, tr = debris(c, c, 6, 34, 40, 1.6, 5, rng, rot=0.3)
        s.path(sh, INK, 0.75, 0.9)
        s.path(tr, PEN, 0.5, 0.8)
    elif frame in (1, 2):
        rx = (24, 31)[frame - 1]
        ry = (21, 27)[frame - 1]
        lift = (0, -3)[frame - 1]
        billow_mass(s, rng, "mass", c, c + lift, rx, ry, 8 + frame, 0.55, INK,
                    (0.95, 0.6, 0.4), (1.25, 0.75, 0.5), 1.3,
                    hatch=[(c, c + lift + ry * 0.4, rx, ry * 0.65, 45, 2.8, INK, 0.5, 0.65),
                           (c, c + lift + ry * 0.55, rx * 0.8, ry * 0.4, -45, 3.0, INK, 0.5, 0.55)],
                    swirl=(3 + frame, 4 + frame, INK, 0.75, 0.75))
        # Hot core: a small bold spiky star (flak's shape signature, white only) with
        # dense ink cross-hatching inside.
        hr = (12, 8)[frame - 1]
        core = star_d(c, c + lift, hr, hr * 0.58, 8, random.Random(83), rot=0.2)
        s.clip("hotc", core)
        s.open_group("hot_hatch", clip="hotc")
        s.path(hatch_d(c, c + lift, hr, hr, 45, 1.5, rng, trim=0.03), INK, 0.6, 0.95)
        s.path(hatch_d(c, c + lift, hr, hr, -45, 1.8 if frame == 1 else 2.2, rng,
                       trim=0.08), INK, 0.5, 0.85)
        s.close_group()
        s.path(core, INK, 1.5 if frame == 1 else 1.25, 1.0)
        s.path(star_d(c, c + lift, hr, hr * 0.58, 8, random.Random(83), jit=1.0, rot=0.2),
               INK, 0.5, 0.55)
        sh, tr = debris(c, c, 4, rx + 6, rx + 10, 1.5, 5, rng, rot=0.9)
        s.path(sh, INK, 0.75 if frame == 2 else 1.0, 0.9)
        s.path(tr, PEN, 0.5, 0.7)
    else:
        # Dissipating flak cloud: broken contour, sparse hatch, wisps, a few embers.
        rx, ry, lift = 33, 26, -7
        billow_mass(s, rng, "mass", c, c + lift, rx, ry, 10, 0.5, PEN, (0.75, 0.5), (1.0, 0.5),
                    1.6, hatch=[(c, c + lift + ry * 0.4, rx * 0.8, ry * 0.5, 45, 4.5, PEN, 0.5, 0.65)],
                    swirl=(4, 5, PEN, 0.75, 0.6), gap={1, 5, 8})
        for k in range(4):
            x = c - 22 + k * 14 + rng.uniform(-3, 3)
            s.path(wisp_d(x, c + lift - ry * rng.uniform(0.4, 0.7), -90 + (x - c) * 0.7,
                          rng.uniform(16, 22), 2.5, 2, rng), PEN, 0.75, 0.6)
        sh, tr = debris(c, c + lift, 4, 6, 16, 1.0, 3, rng, rot=0.2)
        s.path(sh, INK, 1.0, 0.85)
    return s.text()


def main():
    files = {f"explosion_{i}": explosion(i) for i in range(6)}
    files["smoke_puff"] = smoke_puff()
    files.update({f"flak_burst_{i}": flak(i) for i in range(4)})
    for name, txt in files.items():
        (HERE / f"{name}.svg").write_text(txt, encoding="utf-8")
        print(f"{name}.svg  {txt.count('<')} tags")


if __name__ == "__main__":
    main()
