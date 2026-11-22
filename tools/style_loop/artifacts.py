"""The two kinds of thing the loop works on.

SvgArtifact   an art master in art-src/<category>/<name>.svg (sprites, FX, backgrounds, UI)
TextArtifact  the player-facing words of one mission: title and briefing in mission_0N.json

Both are immutable snapshots: the loop makes a new one per candidate, so any step can be
reverted by keeping the previous snapshot.
"""
import glob
import io
import json
import os
import re
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from typing import List, Optional

SVG_NS = "http://www.w3.org/2000/svg"
XLINK_NS = "http://www.w3.org/1999/xlink"
ET.register_namespace("", SVG_NS)
ET.register_namespace("xlink", XLINK_NS)

AIRCRAFT_TOKENS = ("plane", "biplane", "fighter", "bomber", "zeppelin")
MISSIONS_DIR = os.path.join("Biplanes", "Assets", "Biplanes", "Resources", "Missions")


class ArtifactError(ValueError):
    pass


def local(tag: str) -> str:
    return tag.split("}", 1)[1] if "}" in tag else tag


@dataclass(frozen=True)
class SvgArtifact:
    path: str          # repo-relative, forward slashes
    category: str      # art-src/<category>/
    name: str          # file stem
    text: str
    generated_by: Optional[str] = None   # a generator script owns this file: fix the script

    kind = "svg"

    @property
    def label(self) -> str:
        return f"{self.category}/{self.name}"

    @property
    def is_aircraft(self) -> bool:
        return any(t in self.name for t in AIRCRAFT_TOKENS)

    def with_text(self, text: str) -> "SvgArtifact":
        return SvgArtifact(self.path, self.category, self.name, text, self.generated_by)

    def tree(self) -> ET.Element:
        return parse_svg(self.text)

    def describe(self) -> str:
        return f"SVG art asset `{self.path}` (category {self.category})"


def parse_svg(text: str) -> ET.Element:
    try:
        parser = ET.XMLParser(target=ET.TreeBuilder(insert_comments=True))
        root = ET.fromstring(text.encode("utf-8"), parser=parser)
    except ET.ParseError as e:
        raise ArtifactError(f"not valid XML: {e}")
    if local(root.tag) != "svg":
        raise ArtifactError("root element is not <svg>")
    return root


def serialize_svg(root: ET.Element, original: str) -> str:
    buf = io.BytesIO()
    ET.ElementTree(root).write(buf, encoding="utf-8", xml_declaration=False)
    body = buf.getvalue().decode("utf-8")
    decl = re.match(r"\s*<\?xml[^>]*\?>\s*", original)
    return (decl.group(0).strip() + "\n" if decl else "") + body + ("\n" if not body.endswith("\n") else "")


_MASK = re.compile(r"<!--.*?-->|<\?.*?\?>|<!\[CDATA\[.*?\]\]>|<!DOCTYPE[^>]*>", re.S)
_START = re.compile(r"<([A-Za-z_][\w:.\-]*)((?:\s+[^\s=<>/]+\s*=\s*(?:\"[^\"]*\"|'[^']*'))*)\s*(/?)>")


def _raw_name(name: str) -> str:
    if name.startswith("{" + XLINK_NS + "}"):
        return "xlink:" + name.split("}", 1)[1]
    if name.startswith("{http://www.w3.org/XML/1998/namespace}"):
        return "xml:" + name.split("}", 1)[1]
    return name


def snapshot(root: ET.Element):
    """Attributes and text of every element, in document order, before fixes run."""
    return [(dict(e.attrib), e.text) for e in root.iter() if isinstance(e.tag, str)]


def patch_svg(original: str, root: ET.Element, before) -> str:
    """Write the fixed tree back by editing only the changed attributes of the original text,
    so comments, formatting and attribute order survive and the diff shows just the fix."""
    els = [e for e in root.iter() if isinstance(e.tag, str)]
    masked = _MASK.sub(lambda m: " " * len(m.group(0)), original)
    tags = list(_START.finditer(masked))
    if len(tags) != len(els) or any((a_t[1] != e.text and local(e.tag) == "style") for a_t, e in zip(before, els)):
        return serialize_svg(root, original)          # unusual markup: fall back to a rewrite
    out, pos = [], 0
    for m, el, (old, _) in zip(tags, els, before):
        new = dict(el.attrib)
        if new == old:
            continue
        tag = original[m.start():m.end()]
        for name in set(old) | set(new):
            raw = _raw_name(name)
            pat = re.compile(r"(\s)" + re.escape(raw) + r"\s*=\s*(\"[^\"]*\"|'[^']*')")
            if name not in new:
                tag = pat.sub("", tag, count=1)
            elif name not in old:
                end = len(tag) - (2 if tag.endswith("/>") else 1)
                tag = tag[:end].rstrip() + f' {raw}="{new[name]}"' + tag[end:]
                tag = tag.replace('" />', '"/>') if not original[m.start():m.end()].endswith(" />") else tag
            elif new[name] != old[name]:
                tag = pat.sub(lambda mm: f'{mm.group(1)}{raw}="{new[name]}"', tag, count=1)
        out.append(original[pos:m.start()])
        out.append(tag)
        pos = m.end()
    out.append(original[pos:])
    return "".join(out)


@dataclass(frozen=True)
class TextArtifact:
    path: str
    mission_id: str
    title: str
    briefing: str
    targets: tuple = field(default_factory=tuple)   # objective targetTypes, e.g. ("bomber",)
    gdd_mission: str = ""                            # the GDD's mission paragraph, for the model
    present: tuple = field(default_factory=tuple)   # every spawn type in the mission

    kind = "text"

    @property
    def label(self) -> str:
        return f"mission {self.mission_id}"

    def with_text(self, title: str, briefing: str) -> "TextArtifact":
        return TextArtifact(self.path, self.mission_id, title, briefing, self.targets, self.gdd_mission,
                            self.present)

    def describe(self) -> str:
        return f"the title and briefing of {self.label} (`{self.path}`)"


# ---- loading -----------------------------------------------------------------------------

def load_svg(root: str, rel: str) -> SvgArtifact:
    rel = rel.replace("\\", "/")
    with open(os.path.join(root, rel), encoding="utf-8") as f:
        text = f.read()
    parts = rel.split("/")
    category = parts[-2] if len(parts) >= 2 else ""
    name = os.path.splitext(parts[-1])[0]
    gen = sorted(glob.glob(os.path.join(root, os.path.dirname(rel), "gen_*.py")))
    gen_rel = os.path.relpath(gen[0], root).replace("\\", "/") if gen else None
    return SvgArtifact(rel, category, name, text, gen_rel)


def all_svgs(root: str) -> List[str]:
    out = []
    for p in sorted(glob.glob(os.path.join(root, "art-src", "*", "*.svg"))):
        rel = os.path.relpath(p, root).replace("\\", "/")
        if "/_retired/" not in rel:
            out.append(rel)
    return out


def gdd_mission_paragraph(gdd_text: str, number: int) -> str:
    """Item N of the GDD's "## Missions" list (not the other numbered lists in the GDD)."""
    sec = re.search(r"^## Missions.*?(?=^## |\Z)", gdd_text, re.M | re.S)
    gdd_text = sec.group(0) if sec else ""
    m = re.search(rf"^{number}\. \*\*(.+?)(?=^\d\. \*\*|^## |\Z)", gdd_text, re.M | re.S)
    return " ".join(m.group(0).split()) if m else ""


def load_mission(root: str, rel: str, gdd_text: str = "") -> TextArtifact:
    rel = rel.replace("\\", "/")
    with open(os.path.join(root, rel), encoding="utf-8") as f:
        data = json.load(f)
    targets = tuple(o.get("targetType", "") for o in data.get("objectives", []) if o.get("targetType"))
    m = re.search(r"(\d+)", os.path.basename(rel))
    number = int(m.group(1)) if m else 0
    present = tuple(sorted({s.get("type", "") for s in data.get("spawns", []) if s.get("type")}))
    return TextArtifact(rel, str(data.get("id", number)), data.get("title", ""), data.get("briefing", ""),
                        targets, gdd_mission_paragraph(gdd_text, number), present)


def all_missions(root: str) -> List[str]:
    return [os.path.relpath(p, root).replace("\\", "/")
            for p in sorted(glob.glob(os.path.join(root, MISSIONS_DIR, "mission_*.json")))]


# ---- saving ------------------------------------------------------------------------------

def save(root: str, art) -> None:
    full = os.path.join(root, art.path)
    if art.kind == "svg":
        parse_svg(art.text)                       # never write broken XML
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "w", encoding="utf-8", newline="\n") as f:
            f.write(art.text)
        return
    # Text: replace only the two values in the raw JSON, so the rest of the file is untouched.
    with open(full, encoding="utf-8", newline="") as f:
        raw = f.read()
    data = json.loads(raw)
    for key, new in (("title", art.title), ("briefing", art.briefing)):
        old = data.get(key, "")
        if old == new:
            continue
        pat = re.compile(r'("%s"\s*:\s*)%s' % (key, re.escape(json.dumps(old, ensure_ascii=False))))
        raw, n = pat.subn(lambda m: m.group(1) + json.dumps(new, ensure_ascii=False), raw, count=1)
        if n != 1:
            raise ArtifactError(f"could not find the {key} value in {art.path}")
    json.loads(raw)
    with open(full, "w", encoding="utf-8", newline="") as f:
        f.write(raw)
