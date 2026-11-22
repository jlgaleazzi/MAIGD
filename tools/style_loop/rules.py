"""Evaluator and auto-fixer for every `auto` rule in the GDD's Enforced rules table.

Each rule is a class with `check(...) -> [Violation]` and `fix(...) -> int` (fixes made).
A rule runs only if its ID is in the GDD table with check `auto`; its numbers come from the
table's Parameters column. A rule ID in the GDD with no class here goes to the model judge.
"""
import re
from dataclasses import dataclass
from typing import Callable, Dict, List, Optional, Tuple

from .artifacts import SvgArtifact, TextArtifact, local, patch_svg, snapshot
from .guide import Rule, StyleGuide
from .svgstyle import (Resolved, explicit_colours, get_prop, norm_colour, set_prop, walk)


@dataclass(frozen=True)
class Violation:
    rule: str
    where: str
    message: str
    fixable: bool = True     # an auto-fix exists
    source: str = "auto"     # "auto" or "judge"

    def line(self) -> str:
        tag = "" if self.fixable else " (needs the model)"
        return f"[{self.rule}] {self.where}: {self.message}{tag}"


# ---- helpers -----------------------------------------------------------------------------

def _rgb(h: str) -> Tuple[int, int, int]:
    return int(h[1:3], 16), int(h[3:5], 16), int(h[5:7], 16)


def nearest(colour: str, allowed: List[str]) -> str:
    if colour == "#UNKNOWN" or not colour.startswith("#"):
        return allowed[0]
    r, g, b = _rgb(colour)
    return min(allowed, key=lambda h: sum((a - c) ** 2 for a, c in zip(_rgb(h), (r, g, b))))


def allowed_colours(guide: StyleGuide, art: SvgArtifact) -> List[str]:
    """Palette colours this asset may use: every base colour plus the accents it is allowed."""
    accents = guide.accents()
    out = []
    for name, hx in guide.palette.items():
        if name in accents and not guide.accent_allowed(name, art.name):
            continue
        out.append(hx)
    return out


def _where(r: Resolved) -> str:
    el = r.el
    ident = el.get("id")
    anc = next((a.get("id") for a in r.ancestors if a.get("id")), None)
    bits = [f"<{r.tag}"]
    if ident:
        bits.append(f'id="{ident}"')
    elif anc:
        bits.append(f'in #{anc}')
    return " ".join(bits) + ">"


def _url_ids(el):
    for prop, raw in explicit_colours(el):
        g = re.match(r"url\(#([^)]+)\)", raw or "")
        if g:
            yield g.group(1)


def mask_only(root) -> set:
    """python id()s of elements that only feed a <mask>: its content and the gradients only it
    uses. Mask colours are luminance (white = show), not paint, so the palette doesn't apply."""
    skip, grads, painted = set(), set(), set()
    for m in root.iter():
        if isinstance(m.tag, str) and local(m.tag) == "mask":
            for e in m.iter():
                skip.add(id(e))
                grads.update(_url_ids(e))
    for e in root.iter():
        if isinstance(e.tag, str) and id(e) not in skip:
            painted.update(_url_ids(e))
    for e in root.iter():
        if isinstance(e.tag, str) and e.get("id") in grads - painted:
            for sub_el in e.iter():
                skip.add(id(sub_el))
    return skip


def _base_hex(guide: StyleGuide, name: str, fallback: str) -> str:
    return guide.palette.get(name, fallback)


def applies_to_svg(rule: Rule, art: SvgArtifact) -> bool:
    a = rule.applies
    if a == "art":
        return True
    if a == "sprites":
        cats = rule.param_list("categories")
        return art.category in cats if cats else art.category not in ("backgrounds", "ui", "fx")
    if a == "backgrounds":
        return art.category == "backgrounds"
    if a == "fx":
        return art.category == "fx"
    if a == "aircraft":
        return art.is_aircraft
    return False


def applies_to_text(rule: Rule) -> bool:
    return rule.applies in ("text", "briefing", "title")


# ---- SVG rules ---------------------------------------------------------------------------

class SvgRule:
    id = ""

    def check(self, root, art: SvgArtifact, guide: StyleGuide, rule: Rule) -> List[Violation]:
        raise NotImplementedError

    def fix(self, root, art: SvgArtifact, guide: StyleGuide, rule: Rule) -> int:
        raise NotImplementedError


class Palette(SvgRule):
    id = "A-PALETTE"

    def check(self, root, art, guide, rule):
        ok, out = guide.hex_set(), []
        skip = mask_only(root)
        for el in root.iter():
            if not isinstance(el.tag, str) or id(el) in skip:
                continue
            for prop, raw in explicit_colours(el):
                c = norm_colour(raw)
                if c and c not in ok:
                    out.append(Violation(self.id, f"<{local(el.tag)}> {prop}", f"{raw} is not a palette colour"))
            if local(el.tag) == "style":
                for raw in re.findall(r"#[0-9a-fA-F]{3,6}\b", el.text or ""):
                    if norm_colour(raw) not in ok:
                        out.append(Violation(self.id, "<style>", f"{raw} is not a palette colour"))
        for r in walk(root):
            if r.rendered and r.fill() == "#000000" and r.props.get("fill") is None:
                out.append(Violation(self.id, _where(r), "no fill declared, so it renders SVG's default black"))
        return out

    def fix(self, root, art, guide, rule):
        allowed, ok, n = allowed_colours(guide, art), guide.hex_set(), 0
        skip = mask_only(root)
        for el in root.iter():
            if not isinstance(el.tag, str) or id(el) in skip:
                continue
            for prop, raw in list(explicit_colours(el)):
                c = norm_colour(raw)
                if c and c not in ok:
                    set_prop(el, prop, nearest(c, allowed))
                    n += 1
            if local(el.tag) == "style" and el.text:
                def repl(m):
                    c = norm_colour(m.group(0))
                    return m.group(0) if c in ok else nearest(c, allowed)
                new = re.sub(r"#[0-9a-fA-F]{3,6}\b", repl, el.text)
                n += new != el.text
                el.text = new
        for r in walk(root):
            if r.rendered and r.fill() == "#000000" and r.props.get("fill") is None:
                set_prop(r.el, "fill", "none")
                if r.stroke() is None:
                    set_prop(r.el, "stroke", _base_hex(guide, "ink", allowed[0]))
                n += 1
        return n


class Accent(SvgRule):
    id = "A-ACCENT"

    def _bad(self, guide, art):
        return {hx: name for name, hx in guide.accents().items() if not guide.accent_allowed(name, art.name)}

    def check(self, root, art, guide, rule):
        bad, out = self._bad(guide, art), []
        for el in root.iter():
            if not isinstance(el.tag, str):
                continue
            for prop, raw in explicit_colours(el):
                c = norm_colour(raw)
                if c in bad:
                    out.append(Violation(self.id, f"<{local(el.tag)}> {prop}",
                                         f"accent '{bad[c]}' {c} is reserved for: {guide.palette_use.get(bad[c], '')}"))
        return out

    def fix(self, root, art, guide, rule):
        bad, n = self._bad(guide, art), 0
        base = [hx for name, hx in guide.palette.items() if name not in guide.accents()]
        for el in root.iter():
            if not isinstance(el.tag, str):
                continue
            for prop, raw in list(explicit_colours(el)):
                c = norm_colour(raw)
                if c in bad:
                    # an accent stroke becomes ink; an accent fill becomes nothing (line art)
                    set_prop(el, prop, _base_hex(guide, "ink", base[0]) if prop != "fill" else "none")
                    if prop == "fill" and get_prop(el, "stroke") in (None, "none"):
                        set_prop(el, "stroke", _base_hex(guide, "ink", base[0]))
                    n += 1
        return n


class NoFill(SvgRule):
    id = "A-NO-FILL"

    def _offenders(self, root, art, guide, rule):
        marks = [m.lower() for m in rule.param_list("mark_ids")]
        thin = rule.param_float("line_max_px", 0.0)
        accents = set(guide.accents().values())
        paper = guide.palette.get("paper")
        grid = guide.palette.get("paper grid")
        for r in walk(root):
            if not r.rendered:
                continue
            c = r.fill()
            if c is None or c in accents or c == paper:
                continue
            if c == grid and art.category == "backgrounds":
                continue
            if any(m in i.lower() for i in r.ids() for m in marks):
                continue
            if thin and r.thickness_px() <= thin:
                continue    # a filled sliver or dot no thicker than a line is a drawn line
            yield r, c

    def check(self, root, art, guide, rule):
        return [Violation(self.id, _where(r), f"filled with {c}; tone must come from lines (fill only marks, accents, paper)")
                for r, c in self._offenders(root, art, guide, rule)]

    def fix(self, root, art, guide, rule):
        allowed = [h for h in allowed_colours(guide, art) if h not in guide.accents().values()]
        n = 0
        for r, c in list(self._offenders(root, art, guide, rule)):
            if r.stroke() is None:
                set_prop(r.el, "stroke", nearest(c, allowed))
                if get_prop(r.el, "stroke-width") is None:
                    set_prop(r.el, "stroke-width", "0.75")
            set_prop(r.el, "fill", "none")
            n += 1
        return n


def _width(v) -> float:
    m = re.match(r"\s*([\d.]+)", v or "")
    return float(m.group(1)) if m else 1.0


class LineWeight(SvgRule):
    id = "A-LINE-WEIGHT"

    @staticmethod
    def _owner(r: Resolved):
        """(element that sets the width, width in its units). A <use> target's own
        stroke-width beats what the <use> passes down."""
        if r.tag == "use":
            tgt = r.target()
            if tgt is not None and local(tgt.tag) in ("g", "symbol"):
                inherited = get_prop(tgt, "stroke-width")
                best = (r.el, r.stroke_width_px() / r.scale if r.scale else r.stroke_width_px())
                if inherited is not None:
                    best = (tgt, _width(inherited))
                kids = [k for k in tgt.iter() if k is not tgt and isinstance(k.tag, str)
                        and local(k.tag) in ("path", "line", "polyline", "polygon", "rect", "circle", "ellipse")]
                if kids and all(get_prop(k, "stroke-width") is not None for k in kids):
                    best = max(((k, _width(get_prop(k, "stroke-width"))) for k in kids), key=lambda t: t[1])
                return best
            if tgt is not None and get_prop(tgt, "stroke-width") is not None:
                try:
                    return tgt, float(re.match(r"[\d.]+", get_prop(tgt, "stroke-width")).group(0))
                except (AttributeError, ValueError):
                    pass
        return r.el, r.stroke_width_px() / r.scale if r.scale else r.stroke_width_px()

    def _offenders(self, root, rule):
        mx = rule.param_float("max_px", 1.5)
        for r in walk(root):
            if not r.rendered or r.stroke() is None or (r.in_defs and r.tag != "use" and r.el.get("id")):
                continue
            el, w = self._owner(r)
            if w * r.scale > mx + 1e-6:
                yield r, el, w * r.scale, mx

    def check(self, root, art, guide, rule):
        return [Violation(self.id, _where(r), f"stroke {px:.2f} px at sprite size; max {mx} px")
                for r, _, px, mx in self._offenders(root, rule)]

    def fix(self, root, art, guide, rule):
        n = 0
        for r, el, px, mx in list(self._offenders(root, rule)):
            cur = get_prop(el, "stroke-width")
            want = mx / r.scale if r.scale else mx
            if cur is not None:
                try:
                    want = min(want, float(re.match(r"[\d.]+", cur).group(0)))
                except (AttributeError, ValueError):
                    pass
            set_prop(el, "stroke-width", f"{want:.3g}")
            n += 1
        return n


class BackgroundQuiet(SvgRule):
    id = "A-BG-QUIET"

    def _offenders(self, root, guide, rule):
        mx = rule.param_float("max_ink_opacity", 0.45)
        ink = guide.palette.get("ink")
        accents = {hx: name for name, hx in guide.accents().items()}
        for r in walk(root):
            if not r.rendered:
                continue
            for colour, alpha, what in ((r.stroke(), r.stroke_alpha(), "stroke"), (r.fill(), r.fill_alpha(), "fill")):
                if colour in accents:
                    yield r, what, f"accent '{accents[colour]}' in the scenery", None
                elif colour == ink and alpha > mx + 1e-6:
                    yield r, what, f"ink {what} at opacity {alpha:.2f}; scenery ink max {mx}", alpha / mx

    def check(self, root, art, guide, rule):
        return [Violation(self.id, _where(r), msg) for r, _, msg, _ in self._offenders(root, guide, rule)]

    def fix(self, root, art, guide, rule):
        pencil = guide.palette.get("pencil")
        n, done = 0, set()
        for r, what, _, factor in list(self._offenders(root, guide, rule)):
            if factor is None:
                set_prop(r.el, what, pencil)
            elif id(r.el) not in done:
                own = float(get_prop(r.el, "opacity") or 1.0)
                set_prop(r.el, "opacity", f"{own / factor:.3g}")
                done.add(id(r.el))
            n += 1
        return n


class NoText(SvgRule):
    id = "A-NO-TEXT"

    def check(self, root, art, guide, rule):
        return [Violation(self.id, f"<{local(el.tag)}>", "lettering inside art; redraw it as strokes or move it to UI text",
                          fixable=False)
                for el in root.iter() if isinstance(el.tag, str) and local(el.tag) in ("text", "textPath")]

    def fix(self, root, art, guide, rule):
        return 0


SVG_RULES: Dict[str, SvgRule] = {r.id: r for r in (Palette(), Accent(), NoFill(), LineWeight(),
                                                   BackgroundQuiet(), NoText())}


# ---- text rules --------------------------------------------------------------------------

SMALL_WORDS = {"a", "an", "the", "of", "on", "in", "to", "and", "at", "for", "by", "or", "with"}
TARGET_WORDS = {
    "fighter": ("fighter", "fighters", "airplane", "airplanes", "aeroplane", "aeroplanes", "aircraft"),
    "bomber": ("bomber",),
    "aaa": ("anti-aircraft", "ground gun", "ground fire", "battery", "batteries", "flak"),
    "barrageballoon": ("balloon", "balloons"),
    "enemybase": ("base", "hangar", "hangars"),
    "zeppelin": ("zeppelin", "airship", "turret"),
}


def norm_text(text: str) -> str:
    """Unicode hyphens the UI font may lack -> '-' (models like U+2011 in 'anti-aircraft')."""
    return re.sub("[\u2010\u2011\u2012]", "-", text)


def sentences(text: str) -> List[str]:
    return [s for s in re.split(r"(?<=[.!?])\s+", text.strip()) if s.strip()]


def words(text: str) -> List[str]:
    return re.findall(r"[A-Za-z0-9][A-Za-z0-9'\-]*", text)


def _vocab_hits(guide: StyleGuide, text: str):
    """(start, end, found, use) for each banned term, longest terms first, no overlaps."""
    terms = sorted(((bad, use) for use, bads in guide.vocab for bad in bads), key=lambda t: -len(t[0]))
    taken, hits = [], []
    for bad, use in terms:
        for m in re.finditer(r"(?<![\w-])" + re.escape(bad) + r"(?![\w-])", text, re.I):
            if any(m.start() < e and s < m.end() for s, e in taken):
                continue
            taken.append((m.start(), m.end()))
            hits.append((m.start(), m.end(), m.group(0), use))
    return sorted(hits)


def _fields(art: TextArtifact, rule: Rule):
    if rule.applies == "title":
        return [("title", art.title)]
    if rule.applies == "briefing":
        return [("briefing", art.briefing)]
    return [("title", art.title), ("briefing", art.briefing)]


class TextRule:
    id = ""

    def check(self, art: TextArtifact, guide: StyleGuide, rule: Rule) -> List[Violation]:
        raise NotImplementedError

    def fix(self, art: TextArtifact, guide: StyleGuide, rule: Rule) -> Tuple[TextArtifact, int]:
        return art, 0


class Vocab(TextRule):
    id = "N-VOCAB"

    def check(self, art, guide, rule):
        out = []
        for field_name, text in _fields(art, rule):
            for _, _, found, use in _vocab_hits(guide, text):
                fixable = use != "(remove)"
                hint = f"use '{use}'" if fixable else "no period equivalent: rewrite without it"
                out.append(Violation(self.id, field_name, f"'{found}' is not period vocabulary; {hint}", fixable))
        return out

    def fix(self, art, guide, rule):
        n, new = 0, {}
        for field_name, text in _fields(art, rule):
            for s, e, found, use in reversed(_vocab_hits(guide, text)):
                if use == "(remove)":
                    continue
                rep = use
                at_start = s == 0 or re.search(r"[.!?]\s*$", text[:s])
                if at_start or (found[:1].isupper() and found[1:2].islower() and not use.startswith("the ")):
                    rep = use[:1].upper() + use[1:]
                text = text[:s] + rep + text[e:]
                n += 1
            new[field_name] = text
        return art.with_text(new.get("title", art.title), new.get("briefing", art.briefing)), n


class Length(TextRule):
    id = "N-LENGTH"

    def check(self, art, guide, rule):
        out = []
        sents, n = sentences(art.briefing), len(words(art.briefing))
        lo, hi, mx = rule.param_int("min_sentences", 1), rule.param_int("max_sentences", 99), rule.param_int("max_words", 999)
        if not lo <= len(sents) <= hi:
            out.append(Violation(self.id, "briefing", f"{len(sents)} sentences; must be {lo} to {hi}", False))
        if n > mx:
            out.append(Violation(self.id, "briefing", f"{n} words; at most {mx}", False))
        return out


class Exclaim(TextRule):
    id = "N-EXCLAIM"

    def check(self, art, guide, rule):
        mx, c = rule.param_int("max", 1), art.briefing.count("!")
        return [Violation(self.id, "briefing", f"{c} exclamation marks; at most {mx}")] if c > mx else []

    def fix(self, art, guide, rule):
        mx, text = rule.param_int("max", 1), art.briefing
        extra = text.count("!") - mx
        if extra <= 0:
            return art, 0
        out, n = list(text), 0
        for i, ch in enumerate(out):        # keep the last `mx`, the closing order
            if ch == "!" and n < extra:
                out[i] = "."
                n += 1
        return art.with_text(art.title, "".join(out)), n


class Title(TextRule):
    id = "N-TITLE"

    def check(self, art, guide, rule):
        out, ws = [], words(art.title)
        lo, hi = rule.param_int("min_words", 1), rule.param_int("max_words", 99)
        if not lo <= len(ws) <= hi:
            out.append(Violation(self.id, "title", f"{len(ws)} words; must be {lo} to {hi}", False))
        bad = [w for i, w in enumerate(ws) if not w[0].isupper() and not (i and w.lower() in SMALL_WORDS)]
        if bad:
            out.append(Violation(self.id, "title", f"not Title Case: {', '.join(bad)}"))
        return out

    def fix(self, art, guide, rule):
        ws = art.title.split(" ")
        new = [w if (i and w.lower() in SMALL_WORDS) or not w else w[:1].upper() + w[1:] for i, w in enumerate(ws)]
        title = " ".join(new)
        return art.with_text(title, art.briefing), int(title != art.title)


class Target(TextRule):
    id = "N-TARGET"

    def check(self, art, guide, rule):
        text, out = norm_text(art.briefing).lower(), []
        for t in art.targets:
            names = TARGET_WORDS.get(t.lower(), (t.lower(),))
            if not any(re.search(r"(?<![\w-])" + re.escape(w) + r"(?:e?s)?(?![\w-])", text) for w in names):
                out.append(Violation(self.id, "briefing", f"does not name the target '{t}' (say: {', '.join(names)})", False))
        return out


class Grounded(TextRule):
    id = "N-GROUNDED"

    def check(self, art, guide, rule):
        if not art.present:
            return []
        text, here, out = norm_text(art.briefing).lower(), {p.lower() for p in art.present}, []
        for kind, names in TARGET_WORDS.items():
            if kind in here or kind == "fighter":      # "enemy aircraft" is too generic to judge
                continue
            hit = next((w for w in names if re.search(r"(?<![\w-])" + re.escape(w) + r"(?:e?s)?(?![\w-])", text)), None)
            if hit:
                out.append(Violation(self.id, "briefing", f"mentions '{hit}', but this mission has no {kind}; "
                                     f"it has: {', '.join(art.present)}", False))
        return out


TEXT_RULES: Dict[str, TextRule] = {r.id: r for r in (Vocab(), Length(), Exclaim(), Title(), Target(), Grounded())}


# ---- entry points ------------------------------------------------------------------------

def auto_rules(guide: StyleGuide, art) -> List[Rule]:
    table = SVG_RULES if art.kind == "svg" else TEXT_RULES
    out = []
    for rule in guide.rules.values():
        if rule.check != "auto" or rule.id not in table:
            continue
        if art.kind == "svg" and applies_to_svg(rule, art):
            out.append(rule)
        elif art.kind == "text" and applies_to_text(rule):
            out.append(rule)
    return out


def judge_rules(guide: StyleGuide, art) -> List[Rule]:
    """GDD rules the code can't check: judged by the model."""
    table = SVG_RULES if art.kind == "svg" else TEXT_RULES
    out = []
    for rule in guide.rules.values():
        if rule.check == "auto" and rule.id in table:
            continue
        if art.kind == "svg" and applies_to_svg(rule, art):
            out.append(rule)
        elif art.kind == "text" and applies_to_text(rule):
            out.append(rule)
    return out


def check(guide: StyleGuide, art) -> List[Violation]:
    out = []
    if art.kind == "svg":
        root = art.tree()
        for rule in auto_rules(guide, art):
            out += SVG_RULES[rule.id].check(root, art, guide, rule)
    else:
        for rule in auto_rules(guide, art):
            out += TEXT_RULES[rule.id].check(art, guide, rule)
    return out


def auto_fix(guide: StyleGuide, art):
    """Apply every auto fix once. Returns (new artifact, {rule id: fixes})."""
    counts = {}
    if art.kind == "svg":
        root = art.tree()
        before = snapshot(root)
        for rule in auto_rules(guide, art):
            n = SVG_RULES[rule.id].fix(root, art, guide, rule)
            if n:
                counts[rule.id] = n
        return (art.with_text(patch_svg(art.text, root, before)) if counts else art), counts
    for rule in auto_rules(guide, art):
        art, n = TEXT_RULES[rule.id].fix(art, guide, rule)
        if n:
            counts[rule.id] = n
    return art, counts
