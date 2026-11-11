"""Turn the line-art blueprints in blueprints/ into Unity sprites.

    python tools/import_blueprints.py            write the sprites
    python tools/import_blueprints.py --check    check only: every sprite exists at the right size
    python tools/import_blueprints.py --metrics  also print the line-weight metric per sprite

Some sprites come from white-on-transparent blueprint PNGs instead of art-src/ SVGs. This
script is their "rasterize" step. For each entry in SPRITES it:
- upsamples the source alpha WORK_SCALE times, so line processing can work in sub-pixels;
- line-weight normalization: optionally `grow`s the lines (a max filter, in source px of
  extra width) and applies an alpha `gain`, so every sprite meets the same stroke metric;
- optionally `erase`s source regions (boxes in source px), for example the propeller in
  an animation frame;
- recolours the line art to the palette `ink` (#E8F1FA), keeping its alpha;
- mirrors it when the drawing faces left, because every aircraft sprite faces right
  (docs/ART_STYLE.md); code flips enemies that fly left;
- crops it to the drawing, then fits it inside the ART_STYLE size with 2 px padding,
  centred on a transparent canvas;
- draws the faction `marks` (and other overlays) on top in palette colours. Mark
  coordinates are in SOURCE pixels of the blueprint (before mirroring); they are drawn
  vector-style at final size with 8x supersampling, so they stay crisp. Mark fills are
  opaque and hide the blueprint's own ink mark underneath.

Mark styles:
- ("roundel", cx, cy, r): bullseye ink / paper / ink (MARK_FRIENDLY) with an ink outline.
- ("cross", cx, cy, h, w0, w1): cross pattee filled MARK_HOSTILE (ink) with an ink outline. h = arm
  length from the centre, w0 = arm half-width at the centre, w1 = at the arm end.
- ("prop", cx, cy, rx, ry, blade_ry): dashed propeller disc (ink, 0.75 px) plus a
  foreshortened blade (an ink outlined lens of half-height blade_ry).

Line-weight metric (--metrics), measured on the final sprite with the marks excluded:
- width: mean effective stroke width in px. Every horizontal and vertical run of line
  pixels (alpha >= 15%) of at most 4 px is one cross-section; its width is sum(alpha)/255.
- alpha: mean alpha (0..1) of the line pixels.

A sprite listed here must not also have an SVG master in art-src/, or rasterize.py would
overwrite it.

Needs: pip install pillow
"""
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "blueprints"
OUT = ROOT / "Biplanes" / "Assets" / "Biplanes" / "Resources" / "Sprites"
INK = (0xE8, 0xF1, 0xFA)
PAPER = (0x0F, 0x2A, 0x4A)
FRIENDLY = (0xF2, 0xC1, 0x4E)
HOSTILE = (0xE4, 0x57, 0x2E)
# Faction marks are drawn in ink (user decision: white marks, not friendly/hostile).
MARK_FRIENDLY = INK
MARK_HOSTILE = INK
PADDING = 2
WORK_SCALE = 4   # line processing happens on a 4x upsampled copy of the source
SUPERSAMPLE = 8  # marks are drawn at 8x final size, then reduced
MARK_OUTLINE = 1.25  # px at final size (ART_STYLE: outline weight)
FINE = 0.75          # px at final size (ART_STYLE: structure weight)

PLAYER_SRC = "airplane_blueprint_256px.png"
PLAYER_ROUNDEL = ("roundel", 124.5, 53.0, 14.0)

# sprite path (under Resources/Sprites, no extension) -> settings.
#   src: blueprint file; size: canvas; mirror: source faces left;
#   grow: extra line width in source px; gain: alpha multiplier;
#   erase: source-px boxes (x0, y0, x1, y1) cleared before cropping;
#   marks: overlays drawn on top (see the docstring).
SPRITES = {
    "player/player_biplane": dict(
        src=PLAYER_SRC, size=(160, 80), mirror=False, grow=0.0, gain=1.0,
        erase=[], marks=[PLAYER_ROUNDEL]),
    # Propeller frame 2: same blueprint and crop as player_biplane; the blurred blade is
    # erased (the spinner rows 45..58 stay) and replaced by a dashed disc and a short blade.
    "player/player_biplane_frame2": dict(
        src=PLAYER_SRC, size=(160, 80), mirror=False, grow=0.0, gain=1.0,
        erase=[(241, 4, 256, 45), (241, 58, 256, 92)],
        marks=[PLAYER_ROUNDEL, ("prop", 247.0, 49.0, 4.5, 41.0, 22.0)]),
    "enemies/enemy_fighter": dict(
        src="fokker_blueprint_transparent_256px.png", size=(150, 75), mirror=True,
        grow=0.0, gain=2.2, erase=[],
        marks=[("cross", 143.0, 51.3, 11.2, 1.4, 6.6),    # fuselage
               ("cross", 232.5, 35.0, 10.6, 1.3, 6.2)]),  # tail (rudder)
}


def _lines(spec):
    """Processed ink alpha at work scale, and the crop box of the full drawing."""
    from PIL import Image, ImageDraw, ImageFilter

    src = Image.open(SRC / spec["src"]).convert("RGBA")
    alpha = src.getchannel("A")
    alpha = alpha.resize((src.width * WORK_SCALE, src.height * WORK_SCALE), Image.BICUBIC)
    k = int(round(spec["grow"] * WORK_SCALE)) + 1
    if k > 1:
        alpha = alpha.filter(ImageFilter.MaxFilter(k if k % 2 else k + 1))
    if spec["gain"] != 1.0:
        g = spec["gain"]
        alpha = alpha.point(lambda v: min(255, int(round(v * g))))
    # The crop box comes from the whole drawing (before erase), so animation frames of
    # the same blueprint share one placement.
    box = alpha.getbbox()
    draw = ImageDraw.Draw(alpha)
    for x0, y0, x1, y1 in spec["erase"]:
        draw.rectangle([x0 * WORK_SCALE, y0 * WORK_SCALE,
                        x1 * WORK_SCALE - 1, y1 * WORK_SCALE - 1], fill=0)
    if spec["mirror"]:
        alpha = alpha.transpose(Image.FLIP_LEFT_RIGHT)
        box = (alpha.width - box[2], box[1], alpha.width - box[0], box[3])
    return alpha, box


def _transform(spec, work_w, box):
    """Placement (w, h, ox, oy), final px per source px, and source px -> final px."""
    width, height = spec["size"]
    bw, bh = box[2] - box[0], box[3] - box[1]
    scale = min((width - 2 * PADDING) / bw, (height - 2 * PADDING) / bh)
    w, h = max(1, round(bw * scale)), max(1, round(bh * scale))
    ox, oy = (width - w) // 2, (height - h) // 2
    sx, sy = w / bw, h / bh

    def to_final(x, y):
        wx, wy = x * WORK_SCALE, y * WORK_SCALE
        if spec["mirror"]:
            wx = work_w - wx
        return ox + (wx - box[0]) * sx, oy + (wy - box[1]) * sy

    return (w, h, ox, oy), sx * WORK_SCALE, to_final


def _cross_points(cx, cy, h, w0, w1):
    pts = []
    for ang in (0, 90, 180, 270):  # right, down, left, up arms (screen coords)
        c, s = math.cos(math.radians(ang)), math.sin(math.radians(ang))
        for along, across in ((w0, -w0), (h, -w1), (h, w1), (w0, w0)):
            pts.append((cx + along * c - across * s, cy + along * s + across * c))
    return pts


def _draw_marks(spec, size, unit, to_final):
    """RGBA overlay of the marks at final size; unit = final px per source px."""
    from PIL import Image, ImageDraw

    S = SUPERSAMPLE
    over = Image.new("RGBA", (size[0] * S, size[1] * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(over)

    def P(x, y):
        fx, fy = to_final(x, y)
        return fx * S, fy * S

    def disc(cx, cy, r, color):
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=color + (255,))

    for mark in spec["marks"]:
        kind = mark[0]
        if kind == "roundel":
            _, x, y, r = mark
            cx, cy = P(x, y)
            R = r * unit * S
            o = MARK_OUTLINE * S
            disc(cx, cy, R + o / 2, INK)
            disc(cx, cy, R - o / 2, MARK_FRIENDLY)
            disc(cx, cy, R * 0.62, PAPER)
            disc(cx, cy, R * 0.30, MARK_FRIENDLY)
        elif kind == "cross":
            _, x, y, h, w0, w1 = mark
            cx, cy = P(x, y)
            k = unit * S
            o = MARK_OUTLINE * S
            # Ink silhouette grown by half the outline, hostile fill shrunk by half.
            d.polygon(_cross_points(cx, cy, h * k + o / 2, w0 * k + o / 2, w1 * k + o / 2),
                      fill=INK + (255,))
            d.polygon(_cross_points(cx, cy, h * k - o / 2, max(0.5, w0 * k - o / 2),
                                    w1 * k - o / 2), fill=MARK_HOSTILE + (255,))
        elif kind == "prop":
            _, x, y, rx, ry, bry = mark
            cx, cy = P(x, y)
            RX, RY, BRY = rx * unit * S, ry * unit * S, bry * unit * S
            lw = max(1, round(FINE * S))
            # Dashed disc: 28 dashes around the ellipse, every other segment drawn.
            n = 56
            for i in range(0, n, 2):
                a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
                pts = [(cx + RX * math.cos(a0 + (a1 - a0) * t / 4),
                        cy + RY * math.sin(a0 + (a1 - a0) * t / 4)) for t in range(5)]
                d.line(pts, fill=INK + (200,), width=lw)
            # Foreshortened blade: a slim lens above and below the hub, outlined in ink.
            bw = max(RX * 0.45, 1.2 * S)
            for sgn in (-1, 1):
                pts = [(cx + bw * math.sin(math.pi * t / 16), cy + sgn * BRY * t / 16)
                       for t in range(17)]
                pts += [(cx - bw * math.sin(math.pi * t / 16), cy + sgn * BRY * t / 16)
                        for t in range(16, -1, -1)]
                d.line(pts, fill=INK + (255,), width=max(1, round(1.0 * S)), joint="curve")
    return over.convert("RGBa").resize(size, Image.BOX).convert("RGBA")


def _render(spec):
    """(final RGBA sprite, line-only alpha, mark overlay alpha)."""
    from PIL import Image

    alpha, box = _lines(spec)
    (w, h, ox, oy), unit, to_final = _transform(spec, alpha.width, box)
    alpha = alpha.crop(box).resize((w, h), Image.LANCZOS)
    size = spec["size"]
    lines = Image.new("L", size, 0)
    lines.paste(alpha, (ox, oy))
    canvas = Image.new("RGBA", size, INK + (0,))
    canvas.putalpha(lines)
    marks = _draw_marks(spec, size, unit, to_final)
    canvas.alpha_composite(marks)
    return canvas, lines, marks.getchannel("A")


def build(spec):
    return _render(spec)[0]


def metrics(spec):
    """(mean stroke width px, mean line alpha) at final size, marks excluded."""
    _, lines, marks = _render(spec)
    W, H = lines.size
    a = lines.load()
    m = marks.load()
    T = int(0.15 * 255)

    def val(x, y):
        return 0 if m[x, y] else a[x, y]

    widths = []
    for horizontal in (True, False):
        outer, inner = (H, W) if horizontal else (W, H)
        for i in range(outer):
            run = []
            for j in range(inner + 1):
                v = (val(j, i) if horizontal else val(i, j)) if j < inner else 0
                if v >= T:
                    run.append(v)
                elif run:
                    if len(run) <= 4:
                        widths.append(sum(run) / 255)
                    run = []
    px = [v for v in (val(x, y) for y in range(H) for x in range(W)) if v >= T]
    mean_w = sum(widths) / len(widths) if widths else 0.0
    mean_a = sum(px) / len(px) / 255 if px else 0.0
    return mean_w, mean_a


def main(argv: list) -> int:
    check_only = "--check" in argv
    show_metrics = "--metrics" in argv
    failed = 0
    for name, spec in SPRITES.items():
        src = SRC / spec["src"]
        size = spec["size"]
        dest = OUT / (name + ".png")
        problems = []
        if not src.exists():
            problems.append(f"blueprint missing: {src.relative_to(ROOT)}")
        svg = ROOT / "art-src" / (name + ".svg")
        if svg.exists():
            problems.append(f"{svg.relative_to(ROOT)} also exists; rasterize.py would overwrite it")
        if not problems and not check_only:
            dest.parent.mkdir(parents=True, exist_ok=True)
            build(spec).save(dest)
        if not problems:
            if not dest.exists():
                problems.append("PNG missing (run without --check)")
            else:
                from PIL import Image
                if Image.open(dest).size != size:
                    problems.append(f"size {Image.open(dest).size}, expected {size}")
        if problems:
            failed += 1
            print(f"FAIL {name}: " + "; ".join(problems))
        else:
            extra = ""
            if show_metrics:
                mw, ma = metrics(spec)
                extra = f"  [stroke width {mw:.3f} px, line alpha {ma:.3f}]"
            print(f"OK   {name} <- blueprints/{spec['src']}{extra}")
    print(f"{len(SPRITES) - failed}/{len(SPRITES)} OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
