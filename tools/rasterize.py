"""Render the Biplanes SVG masters (art-src/) to Unity sprites and check them.

    python tools/rasterize.py           render changed SVGs, then check everything
    python tools/rasterize.py --all     re-render every SVG
    python tools/rasterize.py --check   check only (QA): sizes, palette, names, missing PNGs

Needs: pip install cairosvg pillow   (Linux/DGX: libcairo2 is usually already installed)
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "art-src"
OUT = ROOT / "Biplanes" / "Assets" / "Biplanes" / "Resources" / "Sprites"
PREVIEW = ROOT / "docs" / "art" / "preview.png"

PALETTE = {"#0f2a4a", "#1c3f66", "#e8f1fa", "#9db4cc", "#f2c14e", "#e4572e", "#7bd389"}
UI_SCALE = 2  # UI sprites are rendered at 2x for crisp text-like strokes
HEX = re.compile(r"#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b")
SIZE = re.compile(r'<svg[^>]*\bwidth="(\d+(?:\.\d+)?)"[^>]*\bheight="(\d+(?:\.\d+)?)"', re.S)


def out_path(svg: Path) -> Path:
    return OUT / svg.relative_to(SRC).with_suffix(".png")


def check_svg(svg: Path) -> list:
    problems = []
    text = svg.read_text(encoding="utf-8")
    if not re.fullmatch(r"[a-z0-9_]+", svg.stem):
        problems.append("file name must be lower_snake_case")
    if not SIZE.search(text):
        problems.append('<svg> needs explicit width="" and height="" in px')
    for color in set(c.lower() for c in HEX.findall(text)):
        if len(color) == 4:
            color = "#" + "".join(ch * 2 for ch in color[1:])
        if color not in PALETTE:
            problems.append(f"off-palette color {color}")
    if text.count("<") > 400:
        problems.append("too many elements (>400); simplify or use <use>")
    return problems


def render(svg: Path) -> None:
    import cairosvg
    scale = UI_SCALE if svg.parent.name == "ui" else 1
    dest = out_path(svg)
    dest.parent.mkdir(parents=True, exist_ok=True)
    cairosvg.svg2png(url=str(svg), write_to=str(dest), scale=scale)


def preview(pngs: list) -> None:
    from PIL import Image
    thumbs = []
    for p in pngs:
        im = Image.open(p).convert("RGBA")
        im.thumbnail((200, 200))
        thumbs.append(im)
    if not thumbs:
        return
    cols = 6
    rows = (len(thumbs) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * 210, rows * 210), (15, 42, 74, 255))
    for i, im in enumerate(thumbs):
        sheet.alpha_composite(im, (i % cols * 210 + 5, i // cols * 210 + 5))
    PREVIEW.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(PREVIEW)


def main(argv: list) -> int:
    check_only = "--check" in argv
    force = "--all" in argv
    svgs = sorted(SRC.rglob("*.svg")) if SRC.exists() else []
    if not svgs:
        print("No SVGs in art-src/ yet.")
        return 0
    failed = 0
    for svg in svgs:
        rel = svg.relative_to(ROOT)
        problems = check_svg(svg)
        dest = out_path(svg)
        if not check_only and not problems and (force or not dest.exists()
                                                or dest.stat().st_mtime < svg.stat().st_mtime):
            try:
                render(svg)
            except Exception as exc:  # report and keep going so one bad file doesn't hide others
                problems.append(f"render failed: {exc}")
        if not dest.exists() and not problems:
            problems.append("PNG missing (run without --check)")
        if problems:
            failed += 1
            print(f"FAIL {rel}: " + "; ".join(problems))
        else:
            print(f"OK   {rel}")
    if not check_only:
        try:
            preview(sorted(OUT.rglob("*.png")))
            print(f"Preview sheet: {PREVIEW.relative_to(ROOT)}")
        except ImportError:
            print("Pillow not installed; skipped preview sheet.")
    print(f"{len(svgs) - failed}/{len(svgs)} OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
