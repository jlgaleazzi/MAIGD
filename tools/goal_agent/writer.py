"""Code-Writing Step: ask a backend for the code of one gap, check it, apply it, undo it.

Reply format the backend must use (fenced blocks, first line is the header):

    ```csharp
    // FILE: Scripts/Enemies/ZeppelinAI.cs
    <the complete file>
    ```

    ```csharp
    // PATCH: Scripts/Missions/MissionRunner.cs
    <<<<<<< SEARCH
    <exact existing lines, must occur exactly once>
    =======
    <replacement lines>
    >>>>>>> REPLACE
    ```

    ```json
    // MERGE: Resources/Balance.json
    { "pitch": { "maxDegrees": 20 } }
    ```

New files use FILE. Existing C# files change through PATCH (or a complete FILE rewrite when
the file is small and the rewrite doesn't shrink it). Existing JSON files change through MERGE:
only the keys to add or change, deep-merged into the file (never a rewrite). Everything is validated before anything
touches the disk, and apply_changes() returns an undo handle.
"""
import os
import re
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple

from .gaps import Gap
from .gdd import GddDoc
from .scanner import Scan, declared_types

# Where the agent may write (relative to Biplanes/Assets/Biplanes). Docs, art, scenes and
# .meta files are never in scope (ARCHITECTURE §0 and §2).
ALLOWED_PREFIXES = ("Scripts/", "Editor/", "Tests/", "Resources/Missions/")
ALLOWED_FILES = ("Resources/Balance.json",)
ALLOWED_EXT = (".cs", ".json")
MAX_FILES_PER_WRITE = 6          # Claude crew task size (CLAUDE.md)
MAX_REWRITE_LINES = 200          # bigger existing files must be patched, not rewritten
MIN_REWRITE_RATIO = 0.6          # a rewrite that drops >40% of an existing file looks truncated
MAX_PROMPT_FILE_CHARS = 9000
MAX_EDIT_FILE_CHARS = 60000      # files listed for PATCH are shown in full (up to this size)
MAX_ARCH_CHARS = 28000

SYSTEM_PROMPT = """You are the Game Mechanics agent for Biplanes, a Unity 6 LTS (C# 9) side-scrolling WWI shooter.
You write production code that must compile against the existing project on the first try.

Rules (from docs/ARCHITECTURE.md §0, binding):
1. Never write Unity YAML (.unity .prefab .asset .meta). Load content with Resources.Load by path.
2. No tags or physics layers; identify objects by component (Team, IDamageable).
3. One type per file; the file name equals the type name; namespace Biplanes.<Area>.
4. Every tunable number comes from Balance.json (GameBalance) or the mission JSON. No magic numbers.
5. Use only types, methods and events that appear in the ARCHITECTURE excerpt or in the files shown to you. Never invent an API; if something you need is missing, say so in a short NOTE line before the code.
6. Write complete files. No "..." placeholders, no "rest unchanged". Change big existing files with PATCH blocks.
7. Reply with fenced code blocks in the FILE / PATCH / MERGE format you were given, plus at most a few NOTE lines. No other prose. Existing JSON files (Balance.json, missions) are changed with a MERGE block holding only the new or changed keys; never rewrite them.
"""

FORMAT_HELP = """Reply format. For each file you create:

```csharp
// FILE: Scripts/<Area>/<Name>.cs
<complete file>
```

For each change to an existing file (the SEARCH text must match the file exactly and occur once):

```csharp
// PATCH: Scripts/<Area>/<Name>.cs
<<<<<<< SEARCH
<exact existing lines>
=======
<new lines>
>>>>>>> REPLACE
```
For an existing JSON file (for example Balance.json) send only the keys to add or change; they are deep-merged into the file:

```json
// MERGE: Resources/Balance.json
{ "newSection": { "key": 1.0 } }
```
Paths are relative to Biplanes/Assets/Biplanes. The first line inside every fence is the header line.
"""


class WriteError(ValueError):
    """The reply can't be applied; the message goes back to the backend on the next attempt."""


@dataclass
class Change:
    path: str                         # relative to Biplanes/Assets/Biplanes
    new_text: str                     # full new content, \n line endings
    created: bool
    old_text: Optional[str] = None


@dataclass
class UndoHandle:
    base: str
    previous: Dict[str, Optional[bytes]] = field(default_factory=dict)   # path -> original bytes (None = didn't exist)

    def undo(self) -> None:
        for rel, data in self.previous.items():
            full = os.path.join(self.base, *rel.split("/"))
            if data is None:
                if os.path.exists(full):
                    os.remove(full)
                meta = full + ".meta"      # Unity may have imported it meanwhile
                if os.path.exists(meta):
                    os.remove(meta)
            else:
                with open(full, "wb") as f:
                    f.write(data)


# ----------------------------------------------------------------------------- prompt

def build_prompt(gap: Gap, scan: Scan, gdd: GddDoc, architecture: str, feedback: str = "") -> str:
    req = gap.req
    parts = [f"# Task: {req.id} — {req.title}", ""]
    parts.append(f"GDD requirement ({req.gdd_anchor!r}):")
    parts.append(gdd.excerpt(req.gdd_anchor) or "(no excerpt)")
    parts += ["", f"Status: {gap.status} ({gap.passed}/{gap.total} evidence checks pass). Still missing:"]
    parts += [f"- {f}" for f in gap.failures]
    if req.new_files:
        parts += ["", "Create: " + ", ".join(req.new_files)]
    # Files that hold placeholder evidence are the ones to fix, even if the spec didn't list them.
    stub_files = [r.file for r in gap.results if r.ok and r.stub and r.file and r.file.endswith(".cs")]
    edit_files = list(dict.fromkeys(req.edit_files + stub_files))
    if edit_files:
        parts += ["", "You may change (PATCH): " + ", ".join(edit_files)]
    if req.hint:
        parts += ["", "Notes: " + req.hint]
    parts += ["", "When you're done every check above must pass.", ""]

    parts += ["## Types that already exist (name -> file)"]
    parts += [f"- {n} -> {p}" for n, p in sorted(scan.classes.items())]

    parts += ["", "## docs/ARCHITECTURE.md (binding contract)", architecture[:MAX_ARCH_CHARS]]
    for rel in dict.fromkeys(req.context_files + edit_files):
        text = scan.text(rel)
        if text is None:
            continue
        # Files the model must PATCH are shown whole: a PATCH needs the exact text, and a model that
        # only sees the start of a file invents the rest (seen on a real run).
        shown = text[:MAX_EDIT_FILE_CHARS if rel in edit_files else MAX_PROMPT_FILE_CHARS]
        cut = "\n// ... (file continues; only the start is shown)" if len(text) > len(shown) else ""
        parts += ["", f"## Existing file: {rel}", "```csharp" if rel.endswith(".cs") else "```json", shown + cut, "```"]
    parts += ["", FORMAT_HELP]
    if feedback:
        parts += ["", "## Your previous attempt was rejected", feedback, "Fix exactly that and answer again."]
    return "\n".join(parts)


# ----------------------------------------------------------------------------- parsing

_BLOCK = re.compile(r"```[\w+-]*[ \t]*\n\s*(?://|#|/\*)?[ \t]*[*]*(FILE|PATCH|MERGE)[*]*:[ \t]*[*]*([^\n]*)\n(.*?)```",
                    re.S | re.I)
_HUNK = re.compile(r"<<<<<<< SEARCH\n(.*?)\n=======\n(.*?)\n?>>>>>>> REPLACE", re.S)


def normalize_path(p: str) -> str:
    p = p.strip().split()[0] if p.strip() else ""          # "Scripts/X.cs (new file)" -> the path
    p = p.strip("`'\"*").replace("\\", "/")
    for prefix in ("Biplanes/Assets/Biplanes/", "Assets/Biplanes/", "./"):
        if p.startswith(prefix):
            p = p[len(prefix):]
    return p


def clean_reply(reply: str) -> str:
    """Drop a reasoning model's <think> section; CRLF -> LF."""
    reply = reply.replace("\r\n", "\n")
    reply = re.sub(r"<think>.*?</think>", "", reply, flags=re.S | re.I)
    if "</think>" in reply.lower():                       # opening tag already stripped by the server
        reply = re.split(r"</think>", reply, flags=re.I)[-1]
    return reply


_HEADER = re.compile(r"^[ \t]*(?://|#|/\*)?[ \t]*[*]*(FILE|PATCH|MERGE)[*]*:[ \t]*[*]*([^\n]*)$", re.I | re.M)


def _split_by_headers(reply: str) -> List[Tuple[str, str, str]]:
    """Header-line layout: '// FILE: path' (or PATCH / MERGE) on its own line, the body below it
    until the next header, optionally wrapped in a ``` fence (some models put the header outside
    the fence, or leave patches and merges unfenced)."""
    heads = list(_HEADER.finditer(reply))
    out = []
    for i, m in enumerate(heads):
        end = heads[i + 1].start() if i + 1 < len(heads) else len(reply)
        body = reply[m.end() + 1:end] if m.end() < len(reply) else ""
        lines = body.split("\n")
        while lines and not lines[0].strip():
            lines.pop(0)
        while lines and not lines[-1].strip():
            lines.pop()
        if lines and lines[0].lstrip().startswith("```"):
            lines.pop(0)
            if not lines or lines[-1].strip() != "```":
                raise WriteError(f"the reply looks truncated: the code block for {normalize_path(m.group(2))} "
                                 f"was never closed. Answer with fewer or shorter files")
        if lines and re.match(r"^```\w+$", lines[-1].strip()):         # opener of the next block
            lines.pop()
            while lines and not lines[-1].strip():
                lines.pop()
        if lines and lines[-1].strip() == "```":                       # this block's own closing fence
            lines.pop()
        out.append((m.group(1).upper(), normalize_path(m.group(2)), "\n".join(lines) + "\n"))
    return out


def parse_reply(reply: str) -> List[Tuple[str, str, str]]:
    """-> [(kind 'FILE'|'PATCH'|'MERGE', path, body)]"""
    reply = clean_reply(reply)
    blocks = [(k.upper(), normalize_path(p), body) for k, p, body in _BLOCK.findall(reply)]
    headers = len(_HEADER.findall(reply))
    if headers > len(blocks):                 # headers outside the fences, or unfenced blocks
        if reply.count("```") % 2 == 1:
            raise WriteError("the reply looks truncated: a code block was never closed. Answer with fewer or "
                             "shorter files")
        blocks = _split_by_headers(reply)
    if not blocks:
        if not reply.strip():
            raise WriteError("the reply was empty (or only reasoning); answer with the code blocks")
        if reply.count("```") % 2 == 1:
            raise WriteError("the reply looks truncated: a code block was never closed. Answer with fewer or "
                             "shorter files")
        raise WriteError("no FILE, PATCH or MERGE blocks found; reply with fenced blocks whose first line is "
                         "'// FILE: <path>', '// PATCH: <path>' or '// MERGE: <path>'")
    return blocks


# ----------------------------------------------------------------------------- validation

def check_path(rel: str) -> None:
    if not rel or rel.startswith("/") or re.match(r"^[A-Za-z]:", rel) or ".." in rel.split("/"):
        raise WriteError(f"{rel!r}: path must be relative to Biplanes/Assets/Biplanes, without '..'")
    if rel.endswith(".meta"):
        raise WriteError(f"{rel}: never write .meta files; Unity creates them on import")
    if not rel.endswith(ALLOWED_EXT):
        raise WriteError(f"{rel}: only {', '.join(ALLOWED_EXT)} files can be written")
    if not (rel.startswith(ALLOWED_PREFIXES) or rel in ALLOWED_FILES):
        raise WriteError(f"{rel}: outside the writable areas {ALLOWED_PREFIXES + ALLOWED_FILES}")


def validate_cs(rel: str, text: str, old_text: Optional[str] = None) -> None:
    """ARCHITECTURE §0: one type per file, file name = type name, namespace Biplanes.*.

    For a file that already exists only the types the change ADDS count against the rule: a file
    like GameBalance.cs that already holds several data classes can still be patched."""
    types = declared_types(text)
    stem = os.path.splitext(os.path.basename(rel))[0]
    if not types:
        raise WriteError(f"{rel}: declares no type")
    if stem not in types:
        raise WriteError(f"{rel}: file name must match the type it declares (found {', '.join(types)})")
    if len(types) > 1 and not rel.startswith("Tests/"):
        already = set(declared_types(old_text)) if old_text else set()
        # A file that already holds several types (GameBalance.cs keeps every Balance data class)
        # has an established multi-type convention: a sibling type may join it.
        multi = len(already) > 1
        extra = [] if multi else [t for t in types if t != stem and t not in already]
        if extra:
            raise WriteError(f"{rel}: one type per file; put the new type(s) {', '.join(extra)} in their own "
                             f"file(s) (types that already live in this file may stay)")
    if not re.search(r"^\s*namespace\s+Biplanes(\.\w+)*", text, re.M):
        raise WriteError(f"{rel}: namespace must be Biplanes.<Area>")
    if re.search(r"^\s*(\.\.\.|//\s*\.\.\.)\s*$", text, re.M) or "rest unchanged" in text.lower():
        raise WriteError(f"{rel}: contains a placeholder; write the complete file")
    if text.count("{") != text.count("}"):
        raise WriteError(f"{rel}: unbalanced braces (reply truncated?)")


def _read_current(base: str, rel: str) -> Optional[str]:
    full = os.path.join(base, *rel.split("/"))
    if not os.path.isfile(full):
        return None
    with open(full, encoding="utf-8-sig", newline="") as f:
        return f.read().replace("\r\n", "\n")


def apply_hunks(rel: str, current: str, body: str) -> str:
    hunks = _HUNK.findall(body)
    if not hunks:
        raise WriteError(f"{rel}: PATCH block has no '<<<<<<< SEARCH / ======= / >>>>>>> REPLACE' hunk")
    text = current
    for n, (search, replace) in enumerate(hunks, 1):
        count = text.count(search)
        if count == 0:
            raise WriteError(f"{rel}: PATCH hunk {n} SEARCH text not found; copy it exactly from the file")
        if count > 1:
            raise WriteError(f"{rel}: PATCH hunk {n} SEARCH text occurs {count} times; include more context")
        text = text.replace(search, replace, 1)
    return text


def deep_merge(base: dict, patch: dict) -> dict:
    """Objects merge key by key; anything else in `patch` replaces the value."""
    out = dict(base)
    for k, v in patch.items():
        out[k] = deep_merge(out[k], v) if isinstance(v, dict) and isinstance(out.get(k), dict) else v
    return out


def plan_changes(reply: str, base: str, scan: Scan, max_files: int = MAX_FILES_PER_WRITE) -> List[Change]:
    """Parse and validate a reply into Changes without touching the disk."""
    blocks = parse_reply(reply)
    distinct = len({rel for _, rel, _ in blocks})          # several PATCH blocks for one file count once
    if distinct > max_files:
        raise WriteError(f"{distinct} files in one reply; the limit is {max_files}. Do only part of the task: "
                         f"the files the Notes name first")
    planned: Dict[str, Change] = {}
    for kind, rel, body in blocks:
        check_path(rel)
        current = planned[rel].new_text if rel in planned else _read_current(base, rel)
        if kind == "MERGE":
            import json
            try:
                patch = json.loads(body)
            except json.JSONDecodeError as e:
                raise WriteError(f"{rel}: MERGE body is not valid JSON ({e})")
            if not rel.endswith(".json") or not isinstance(patch, dict):
                raise WriteError(f"{rel}: MERGE works on a .json file with a JSON object body")
            if current is None:
                text = json.dumps(patch, indent=2) + "\n"
            else:
                try:
                    base_obj = json.loads(current)
                except json.JSONDecodeError as e:
                    raise WriteError(f"{rel}: the existing file is not valid JSON ({e})")
                text = json.dumps(deep_merge(base_obj, patch), indent=2) + "\n"
        elif kind == "FILE":
            text = body.rstrip("\n") + "\n"
            if current is not None:
                lines = current.count("\n") + 1
                if rel not in planned and lines > MAX_REWRITE_LINES:
                    how = "a MERGE block" if rel.endswith(".json") else "PATCH blocks"
                    raise WriteError(f"{rel} has {lines} lines; change it with {how}, not a full rewrite")
                if len(text) < len(current) * MIN_REWRITE_RATIO:
                    raise WriteError(f"{rel}: the rewrite is {100 - 100 * len(text) // max(len(current), 1)}% "
                                     f"shorter than the file; it looks truncated. "
                                     f"{'Use a MERGE block with only the new keys' if rel.endswith('.json') else 'Use PATCH blocks'}")
        else:
            if current is None:
                raise WriteError(f"{rel}: cannot PATCH a file that does not exist; use FILE")
            text = apply_hunks(rel, current, body)
        if rel.endswith(".cs"):
            validate_cs(rel, text, current)
        elif rel.endswith(".json"):
            import json
            try:
                json.loads(text)
            except json.JSONDecodeError as e:
                raise WriteError(f"{rel}: invalid JSON ({e})")
        orig = planned[rel].old_text if rel in planned else current
        planned[rel] = Change(rel, text, created=orig is None, old_text=orig)
    return list(planned.values())


# ----------------------------------------------------------------------------- applying

def apply_changes(changes: List[Change], base: str) -> UndoHandle:
    undo = UndoHandle(base)
    try:
        for ch in changes:
            full = os.path.join(base, *ch.path.split("/"))
            if os.path.isfile(full):
                with open(full, "rb") as f:
                    undo.previous[ch.path] = f.read()
            else:
                undo.previous[ch.path] = None
            os.makedirs(os.path.dirname(full), exist_ok=True)
            newline = "\r\n" if undo.previous[ch.path] and b"\r\n" in undo.previous[ch.path] else "\n"
            with open(full, "w", encoding="utf-8", newline="") as f:
                f.write(ch.new_text.replace("\n", newline))
    except OSError:
        undo.undo()
        raise
    return undo
