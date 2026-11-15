"""Read the GDD: split the markdown into feature items and answer "is this phrase in it?".

The spec (spec.json) ties every requirement to a short phrase of the GDD (`gdd_anchor`).
This module lets the agent check that each anchor still exists (so the spec can't drift
away from the GDD) and list the GDD items no requirement covers yet.
"""
import re
from dataclasses import dataclass
from typing import List

# Sections that describe the project, not the game's features.
SKIP_SECTIONS = ("change log", "ai architecture")
MIN_PARAGRAPH_CHARS = 25


def normalize(text: str) -> str:
    """Lower-case, drop markdown emphasis, collapse whitespace."""
    text = re.sub(r"[*`]", "", text)
    return re.sub(r"\s+", " ", text).strip().lower()


@dataclass
class GddItem:
    section: str   # "2. Game Mechanics > Enemies"
    text: str      # the bullet / table row / paragraph, joined onto one line
    line: int      # 1-based line where it starts

    @property
    def norm(self) -> str:
        return normalize(self.text)


class GddDoc:
    def __init__(self, text: str, items: List[GddItem]):
        self.text = text
        self.items = items
        self.norm_text = normalize(text)

    def has(self, phrase: str) -> bool:
        """True when the phrase appears anywhere in the GDD (case/markdown/wrapping-proof)."""
        return normalize(phrase) in self.norm_text

    def items_containing(self, phrase: str) -> List[GddItem]:
        p = normalize(phrase)
        return [i for i in self.items if p in i.norm]

    def excerpt(self, phrase: str, max_chars: int = 600) -> str:
        """The GDD items that mention the phrase, for the writer's prompt."""
        hits = self.items_containing(phrase)
        if not hits:
            return ""
        out = "\n".join(f"[{i.section}] {i.text}" for i in hits)
        return out[:max_chars]


def parse_gdd(text: str) -> GddDoc:
    items: List[GddItem] = []
    stack: List[tuple] = []      # (level, title)
    cur = None                   # item being extended by continuation lines
    in_table = False
    prev_blank = True

    def flush():
        nonlocal cur
        cur = None

    for no, raw in enumerate(text.splitlines(), 1):
        line = raw.rstrip()
        head = re.match(r"^(#{1,6})\s+(.*)", line)
        if head:
            level = len(head.group(1))
            stack = [(l, t) for l, t in stack if l < level] + [(level, head.group(2).strip())]
            flush()
            in_table = False
            prev_blank = True
            continue
        section = " > ".join(t for _, t in stack)
        if any(s in section.lower() for s in SKIP_SECTIONS):
            flush()
            continue
        if not line.strip():
            flush()
            in_table = False
            prev_blank = True
            continue

        if line.lstrip().startswith("|"):
            if re.match(r"^\s*\|[\s:|-]+\|\s*$", line):      # |---|---| ends the header row
                if cur is not None and items and items[-1] is cur:
                    items.pop()
                flush()
                continue
            cells = [c.strip() for c in line.strip().strip("|").split("|")]
            cur = GddItem(section, " | ".join(cells), no)
            items.append(cur)
            in_table = True
            prev_blank = False
            continue

        bullet = re.match(r"^\s*(?:[-*]|\d+\.)\s+(.*)", line)
        if bullet:
            cur = GddItem(section, bullet.group(1).strip(), no)
            items.append(cur)
        elif cur is not None and not prev_blank and not in_table:
            cur.text += " " + line.strip()                   # wrapped line of the same item
        elif not line.lstrip().startswith(("Author:", ">")):
            cur = GddItem(section, line.strip(), no)         # plain paragraph
            items.append(cur)
        prev_blank = False

    items = [i for i in items if len(i.text) >= MIN_PARAGRAPH_CHARS or "|" in i.text]
    return GddDoc(text, items)


def load_gdd(path) -> GddDoc:
    with open(path, encoding="utf-8") as f:
        return parse_gdd(f.read())
