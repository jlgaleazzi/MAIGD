"""Reads the style rules straight out of docs/GDD.md.

Everything the loop enforces comes from the GDD "Style Guide" section:
  - the palette table            (### Palette ...)       -> colours, accents
  - the vocabulary table         (| Use | Instead of |)   -> period vocabulary
  - the enforced-rules table     (| ID | Applies to | ...) -> rule ids, scope, parameters
  - the prose of the section                              -> what the model judge reads

Nothing here is hard-coded style: change the GDD and the loop follows it.
"""
import re
from dataclasses import dataclass, field
from typing import Dict, List, Tuple


class GuideError(RuntimeError):
    pass


@dataclass
class Rule:
    id: str
    applies: str
    text: str
    check: str                      # "auto" or "judge"
    params: Dict[str, str] = field(default_factory=dict)

    def param_list(self, key: str) -> List[str]:
        raw = self.params.get(key, "")
        return [p.strip() for p in raw.split(",") if p.strip()]

    def param_float(self, key: str, default: float) -> float:
        try:
            return float(self.params[key])
        except (KeyError, ValueError):
            return default

    def param_int(self, key: str, default: int) -> int:
        return int(self.param_float(key, default))


@dataclass
class StyleGuide:
    palette: Dict[str, str]                 # name (lower) -> "#RRGGBB"
    palette_use: Dict[str, str]             # name -> "Used for" text
    rules: Dict[str, Rule]
    vocab: List[Tuple[str, List[str]]]      # (use, [instead of ...]); use "(remove)" = no substitute
    text: str                               # the whole Style Guide section (for the model)
    source: str

    # ---- derived helpers -------------------------------------------------------------
    def hex_set(self) -> set:
        return set(self.palette.values())

    def accents(self) -> Dict[str, str]:
        """Accent name -> hex, from the A-ACCENT parameters (friendly=..., hostile=...)."""
        rule = self.rules.get("A-ACCENT")
        if not rule:
            return {}
        return {name: self.palette[name] for name in rule.params if name in self.palette}

    def accent_allowed(self, accent: str, asset_name: str) -> bool:
        rule = self.rules.get("A-ACCENT")
        if not rule:
            return True
        tokens = rule.param_list(accent)
        return any(t.lower() in asset_name.lower() for t in tokens)

    def colour(self, name: str) -> str:
        return self.palette[name]

    def rule_text(self, ids) -> str:
        return "\n".join(f"- {r.id} ({r.applies}): {r.text}" for r in
                         (self.rules[i] for i in ids if i in self.rules))


_HEX = re.compile(r"`?(#[0-9A-Fa-f]{6})`?")


def _section(md: str, title_prefix: str) -> str:
    m = re.search(rf"^## {re.escape(title_prefix)}.*$", md, re.M)
    if not m:
        raise GuideError(f"no '## {title_prefix}' section in the GDD")
    rest = md[m.end():]
    n = re.search(r"^## ", rest, re.M)
    return md[m.start(): m.end() + (n.start() if n else len(rest))]


def _tables(section: str) -> List[List[List[str]]]:
    """Every markdown table in the section, as rows of cells (header row first)."""
    tables, cur = [], []
    for line in section.splitlines():
        line = line.strip()
        if line.startswith("|") and line.endswith("|"):
            cells = [c.strip() for c in line.strip("|").split("|")]
            if all(re.fullmatch(r":?-{3,}:?", c) for c in cells if c):
                continue
            cur.append(cells)
        elif cur:
            tables.append(cur)
            cur = []
    if cur:
        tables.append(cur)
    return tables


def _params(raw: str) -> Dict[str, str]:
    out = {}
    for part in raw.split(";"):
        if "=" in part:
            k, v = part.split("=", 1)
            out[k.strip().lower()] = v.strip()
    return out


def parse_guide(md: str, source: str = "docs/GDD.md") -> StyleGuide:
    sec = _section(md, "Style Guide")
    palette, palette_use, rules, vocab = {}, {}, {}, []
    for table in _tables(sec):
        head = [h.lower() for h in table[0]]
        rows = table[1:]
        if head[:2] == ["colour", "hex"] or head[:2] == ["color", "hex"]:
            for r in rows:
                m = _HEX.search(r[1])
                if m:
                    name = r[0].lower()
                    palette[name] = m.group(1).upper()
                    palette_use[name] = r[2] if len(r) > 2 else ""
        elif head[:2] == ["use", "instead of"]:
            for r in rows:
                vocab.append((r[0].strip(), [t.strip() for t in r[1].split(",") if t.strip()]))
        elif head and head[0] == "id" and "rule" in head:
            idx = {h: i for i, h in enumerate(head)}
            for r in rows:
                rid = r[idx["id"]].strip()
                rules[rid] = Rule(rid, r[idx["applies to"]].strip().lower(), r[idx["rule"]].strip(),
                                  r[idx["check"]].strip().lower(),
                                  _params(r[idx["parameters"]]) if "parameters" in idx else {})
    if not palette:
        raise GuideError("the Style Guide has no palette table (| Colour | Hex | Used for |)")
    if not rules:
        raise GuideError("the Style Guide has no enforced-rules table (| ID | Applies to | Rule | Check | Parameters |)")
    return StyleGuide(palette, palette_use, rules, vocab, sec, source)


def load_guide(path: str) -> StyleGuide:
    with open(path, encoding="utf-8") as f:
        return parse_guide(f.read(), path)
