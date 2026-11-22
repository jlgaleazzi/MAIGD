"""Resolved SVG presentation attributes: what each element really renders with.

Handles attributes and inline `style="..."`, inheritance down the tree, multiplied
opacities, transform scale (for line weight at sprite size) and SVG's default black fill.
"""
import math
import re
import xml.etree.ElementTree as ET
from typing import Dict, Iterator, Optional, Tuple

from .artifacts import XLINK_NS, local

SHAPES = {"path", "rect", "circle", "ellipse", "polygon", "polyline", "line", "text", "tspan", "use"}
FILLABLE = {"path", "rect", "circle", "ellipse", "polygon", "polyline", "text", "tspan"}
NON_RENDERED = {"clipPath", "mask", "metadata", "title", "desc", "style", "marker"}
INHERITED = ("fill", "stroke", "stroke-width", "fill-opacity", "stroke-opacity")
COLOUR_PROPS = ("fill", "stroke", "stop-color", "flood-color", "lighting-color", "color")

NAMED = {"black": "#000000", "white": "#FFFFFF", "red": "#FF0000", "green": "#008000",
         "blue": "#0000FF", "yellow": "#FFFF00", "gray": "#808080", "grey": "#808080",
         "orange": "#FFA500", "silver": "#C0C0C0", "navy": "#000080", "maroon": "#800000"}


def norm_colour(value: Optional[str]) -> Optional[str]:
    """'#abc' / '#aabbcc' / 'rgb(..)' / named -> '#RRGGBB'; 'none', url(), currentColor -> None."""
    if value is None:
        return None
    v = value.strip()
    low = v.lower()
    if not v or low in ("none", "transparent", "inherit", "currentcolor") or low.startswith("url("):
        return None
    if re.fullmatch(r"#[0-9a-fA-F]{3}", v):
        return "#" + "".join(c * 2 for c in v[1:]).upper()
    if re.fullmatch(r"#[0-9a-fA-F]{6}", v):
        return v.upper()
    m = re.fullmatch(r"rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", low)
    if m:
        return "#%02X%02X%02X" % tuple(min(255, int(x)) for x in m.groups())
    return NAMED.get(low, "#UNKNOWN")


def parse_style(el: ET.Element) -> Dict[str, str]:
    out = {}
    for part in (el.get("style") or "").split(";"):
        if ":" in part:
            k, v = part.split(":", 1)
            out[k.strip()] = v.strip()
    return out


def get_prop(el: ET.Element, prop: str) -> Optional[str]:
    st = parse_style(el)
    return st[prop] if prop in st else el.get(prop)


def set_prop(el: ET.Element, prop: str, value: Optional[str]) -> None:
    """Set a property where it is declared (style wins over attribute); value None removes it."""
    st = parse_style(el)
    if prop in st:
        if value is None:
            del st[prop]
        else:
            st[prop] = value
        if st:
            el.set("style", ";".join(f"{k}:{v}" for k, v in st.items()))
        elif "style" in el.attrib:
            del el.attrib["style"]
        return
    if value is None:
        el.attrib.pop(prop, None)
    else:
        el.set(prop, value)


def _num(v: Optional[str], default: float) -> float:
    if v is None:
        return default
    m = re.match(r"\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)", v)
    return float(m.group(1)) if m else default


def transform_scale(t: Optional[str]) -> float:
    if not t:
        return 1.0
    s = 1.0
    for fn, args in re.findall(r"(\w+)\s*\(([^)]*)\)", t):
        nums = [float(x) for x in re.findall(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", args)]
        if fn == "scale" and nums:
            sx = nums[0]
            sy = nums[1] if len(nums) > 1 else sx
            s *= math.sqrt(abs(sx * sy))
        elif fn == "matrix" and len(nums) >= 4:
            s *= math.sqrt(abs(nums[0] * nums[3] - nums[1] * nums[2]))
    return s


def root_scale(root: ET.Element) -> float:
    vb = (root.get("viewBox") or "").replace(",", " ").split()
    w = _num(root.get("width"), 0)
    if len(vb) == 4 and w and float(vb[2]):
        return w / float(vb[2])
    return 1.0


class Resolved:
    """One element with everything it inherits."""
    __slots__ = ("el", "tag", "props", "opacity", "scale", "ancestors", "in_defs", "rendered", "refs")

    def __init__(self, el, tag, props, opacity, scale, ancestors, in_defs, rendered, refs):
        self.el, self.tag, self.props, self.opacity = el, tag, props, opacity
        self.scale, self.ancestors, self.in_defs, self.rendered = scale, ancestors, in_defs, rendered
        self.refs = refs

    def target(self):
        """The element a <use> points at, else None."""
        href = self.el.get("href") or self.el.get(f"{{{XLINK_NS}}}href") or ""
        return self.refs.get(href[1:]) if href.startswith("#") else None

    def fill(self) -> Optional[str]:
        """Rendered fill colour, '#000000' for SVG's default, None when no fill shows."""
        if self.tag == "use":
            raw = self.props.get("fill")
            if raw is None or _num(self.props.get("fill-opacity"), 1.0) <= 0:
                return None
            tgt = self.target()
            if tgt is not None and local(tgt.tag) not in FILLABLE and local(tgt.tag) not in ("g", "symbol"):
                return None
            return norm_colour(raw)
        if self.tag not in FILLABLE:
            return None
        raw = self.props.get("fill")
        if raw is None:
            # Undeclared: a <defs>/<symbol> child inherits from its <use>, so we can't know.
            if self.in_defs or shape_thickness(self.el) < 0.05:
                return None     # inherited at a <use>, or a zero-area line: nothing fills
            return "#000000"
        if _num(self.props.get("fill-opacity"), 1.0) <= 0:
            return None
        return norm_colour(raw)

    def stroke(self) -> Optional[str]:
        if self.tag not in SHAPES:
            return None
        if _num(self.props.get("stroke-opacity"), 1.0) <= 0:
            return None
        return norm_colour(self.props.get("stroke"))

    def stroke_width_px(self) -> float:
        return _num(self.props.get("stroke-width"), 1.0) * self.scale

    def stroke_alpha(self) -> float:
        return self.opacity * _num(self.props.get("stroke-opacity"), 1.0)

    def fill_alpha(self) -> float:
        return self.opacity * _num(self.props.get("fill-opacity"), 1.0)

    def ids(self):
        out = []
        for e in [self.el] + self.ancestors:
            out += [e.get("id") or "", e.get("class") or ""]
            href = e.get("href") or e.get(f"{{{XLINK_NS}}}href") or ""
            if href.startswith("#"):
                out.append(href[1:])
        return [x for x in out if x]

    def thickness_px(self) -> float:
        """How thick the filled shape is at sprite size (see shape_thickness)."""
        el = self.el
        if self.tag == "use":
            el = self.target()
            if el is None:
                return float("inf")
            if local(el.tag) in ("g", "symbol"):
                kids = [k for k in el.iter() if isinstance(k.tag, str) and local(k.tag) in FILLABLE]
                return max((shape_thickness(k) for k in kids), default=0.0) * self.scale
        return shape_thickness(el) * self.scale

    def radius(self) -> float:
        """Rough size for tiny dots (rivets): circle r, ellipse max r, rect half-diagonal."""
        el, tag = self.el, self.tag
        if tag == "use":
            tgt = self.target()
            if tgt is None:
                return float("inf")
            el, tag = tgt, local(tgt.tag)
        if tag == "circle":
            return _num(el.get("r"), 0) * self.scale
        if tag == "ellipse":
            return max(_num(el.get("rx"), 0), _num(el.get("ry"), 0)) * self.scale
        if tag == "rect":
            return math.hypot(_num(el.get("width"), 0), _num(el.get("height"), 0)) / 2 * self.scale
        return float("inf")


def walk(root: ET.Element) -> Iterator[Resolved]:
    base = root_scale(root)
    refs = {e.get("id"): e for e in root.iter() if isinstance(e.tag, str) and e.get("id")}

    def rec(el, props, opacity, scale, ancestors, in_defs, rendered):
        tag = local(el.tag) if isinstance(el.tag, str) else ""
        if not tag:
            return
        mine = dict(props)
        for p in INHERITED:
            v = get_prop(el, p)
            if v is not None and v != "inherit":
                mine[p] = v
        op = opacity * _num(get_prop(el, "opacity"), 1.0)
        sc = scale * transform_scale(el.get("transform"))
        defs = in_defs or tag in ("defs", "symbol", "pattern")
        rend = rendered and tag not in NON_RENDERED
        r = Resolved(el, tag, mine, op, sc, ancestors, in_defs, rend, refs)
        yield r
        for child in el:
            yield from rec(child, mine, op, sc, [el] + ancestors, defs, rend)

    yield from rec(root, {}, 1.0, base, [], False, True)


def explicit_colours(el: ET.Element) -> Iterator[Tuple[str, str]]:
    """(property, raw value) for every colour this element declares itself."""
    st = parse_style(el)
    for p in COLOUR_PROPS:
        if p in st:
            yield p, st[p]
        elif el.get(p) is not None:
            yield p, el.get(p)


_CMD = re.compile(r"([MmLlHhVvCcSsQqTtAaZz])|([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)")
_ARGS = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7, "Z": 0}


def path_subpaths(d: str):
    """Vertices (control points included) of each subpath: a rough outline, good for size."""
    subs, cur = [], []
    x = y = sx = sy = 0.0
    cmd, nums = None, []
    for c, n in _CMD.findall(d or ""):
        if c:
            cmd = c
            nums = []
            if c in "Zz":
                if len(cur) > 1:
                    subs.append(cur)
                cur, x, y, cmd = [], sx, sy, None
            continue
        if cmd is None:
            continue
        nums.append(float(n))
        up, rel = cmd.upper(), cmd.islower()
        k = _ARGS[up]
        if len(nums) < k:
            continue
        a, nums = nums, []
        if up == "H":
            x = a[0] + (x if rel else 0)
        elif up == "V":
            y = a[0] + (y if rel else 0)
        elif up == "A":
            x, y = (a[5] + x, a[6] + y) if rel else (a[5], a[6])
        else:
            pts = [(a[i] + (x if rel else 0), a[i + 1] + (y if rel else 0)) for i in range(0, k, 2)]
            if up == "M":
                if len(cur) > 1:
                    subs.append(cur)
                cur = []
                sx, sy = pts[-1]
                cmd = "l" if rel else "L"
            cur.extend(pts[:-1])
            x, y = pts[-1]
        cur.append((x, y))
    if len(cur) > 1:
        subs.append(cur)
    return subs


def shape_thickness(el: ET.Element) -> float:
    """2 * area / perimeter of a filled shape, in its own units: how thick a 'line' it is.
    A tapered sliver or a dot is thin; a filled panel is thick. Multi-part paths: thickest part."""
    tag = local(el.tag)
    if tag == "circle":
        return _num(el.get("r"), 0)
    if tag == "ellipse":
        rx, ry = _num(el.get("rx"), 0), _num(el.get("ry"), 0)
        return 2 * rx * ry / (rx + ry) if rx + ry else 0
    if tag == "rect":
        w, h = _num(el.get("width"), 0), _num(el.get("height"), 0)
        return w * h / (w + h) if w + h else 0
    if tag in ("polygon", "polyline"):
        nums = [float(v) for v in re.findall(r"[-+]?(?:\d+\.?\d*|\.\d+)", el.get("points") or "")]
        subs = [list(zip(nums[::2], nums[1::2]))]
    elif tag == "path":
        subs = path_subpaths(el.get("d"))
    else:
        return float("inf")
    best = 0.0
    for pts in subs:
        if len(pts) < 3:
            continue
        area = abs(sum(pts[i][0] * pts[i - 1][1] - pts[i - 1][0] * pts[i][1] for i in range(len(pts)))) / 2
        per = sum(math.dist(pts[i], pts[i - 1]) for i in range(len(pts)))
        if per:
            best = max(best, 2 * area / per)
    return best
