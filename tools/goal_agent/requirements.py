"""Requirements: what the GDD asks for, and how to tell from the code that it's there.

A requirement is one GDD feature plus *evidence checks* the scan can answer:

    class        {"type": "class", "name": "ZeppelinAI"}                     type declared in a .cs file
    file         {"type": "file", "path": "Scripts/Enemies/ZeppelinAI.cs"}   file exists (also art assets)
    contains     {"type": "contains", "path": "Scripts/**.cs", "pattern": "regex"}   regex found in a file (glob ok)
    json_key     {"type": "json_key", "path": "Resources/Balance.json", "key": "player.bombCapacity"}

Paths are relative to Biplanes/Assets/Biplanes. The spec lives in spec.json so it can be
edited without touching code; load_spec() validates it.
"""
import json
import re
from dataclasses import dataclass, field
from typing import Dict, List, Optional

from .scanner import Scan

CHECK_TYPES = {"class", "file", "contains", "json_key"}


@dataclass
class Requirement:
    id: str
    title: str
    gdd_anchor: str                 # phrase that must appear in the GDD (traceability)
    category: str                   # gameplay | enemy | obstacle | ui | mission | data | build | art
    impact: int                     # 1..5  how much the GDD needs it
    effort: int                     # 1..5  how big the job is
    checks: List[dict]
    core: bool = False              # part of the core loop (player, scoring, controls)
    deps: List[str] = field(default_factory=list)
    writable: bool = True           # False: another role owns it (art), report only
    owner: str = "mechanics"        # crew role that owns the files (informational)
    context_files: List[str] = field(default_factory=list)   # shown to the writer as examples
    edit_files: List[str] = field(default_factory=list)      # existing files the writer may change
    new_files: List[str] = field(default_factory=list)       # files the writer is expected to create
    hint: str = ""                  # extra instructions for the writer
    extra_anchors: List[str] = field(default_factory=list)   # more GDD phrases this requirement covers

    @property
    def anchors(self) -> List[str]:
        return [self.gdd_anchor] + self.extra_anchors


@dataclass
class CheckResult:
    ok: bool
    desc: str
    stub: bool = False              # evidence exists but is a placeholder
    file: Optional[str] = None


class SpecError(ValueError):
    pass


def load_spec(path) -> List[Requirement]:
    with open(path, encoding="utf-8") as f:
        raw = json.load(f)
    reqs, seen = [], set()
    for entry in raw["requirements"]:
        try:
            r = Requirement(**entry)
        except TypeError as e:
            raise SpecError(f"{entry.get('id', '?')}: {e}")
        if r.id in seen:
            raise SpecError(f"duplicate requirement id {r.id}")
        seen.add(r.id)
        if not (1 <= r.impact <= 5 and 1 <= r.effort <= 5):
            raise SpecError(f"{r.id}: impact and effort must be 1..5")
        if not r.checks:
            raise SpecError(f"{r.id}: needs at least one check")
        for c in r.checks:
            if c.get("type") not in CHECK_TYPES:
                raise SpecError(f"{r.id}: unknown check type {c.get('type')!r}")
        reqs.append(r)
    ids = {r.id for r in reqs}
    for r in reqs:
        for d in r.deps:
            if d not in ids:
                raise SpecError(f"{r.id}: unknown dependency {d}")
    _check_no_cycles(reqs)
    return reqs


def _check_no_cycles(reqs: List[Requirement]) -> None:
    by_id = {r.id: r for r in reqs}
    state: Dict[str, int] = {}

    def visit(i: str, trail: List[str]):
        if state.get(i) == 2:
            return
        if state.get(i) == 1:
            raise SpecError("dependency cycle: " + " -> ".join(trail + [i]))
        state[i] = 1
        for d in by_id[i].deps:
            visit(d, trail + [i])
        state[i] = 2

    for r in reqs:
        visit(r.id, [])


def _dig(obj, dotted: str):
    for part in dotted.split("."):
        if not isinstance(obj, dict) or part not in obj:
            return False, None
        obj = obj[part]
    return True, obj


def run_check(check: dict, scan: Scan) -> CheckResult:
    kind = check["type"]
    if kind == "class":
        name = check["name"]
        rel = scan.classes.get(name)
        if not rel:
            return CheckResult(False, f"type {name} is not declared anywhere")
        return CheckResult(True, f"type {name} in {rel}", stub=scan.files[rel].stub, file=rel)

    if kind == "file":
        rel = check["path"]
        if not scan.has_file(rel):
            return CheckResult(False, f"file {rel} does not exist")
        return CheckResult(True, f"file {rel}", stub=scan.files[rel].stub, file=rel)

    if kind == "contains":
        pattern = re.compile(check["pattern"])
        paths = scan.glob(check["path"]) if any(c in check["path"] for c in "*?[") else (
            [check["path"]] if scan.has_file(check["path"]) else [])
        if not paths:
            return CheckResult(False, f"{check['path']} does not exist (needed for /{check['pattern']}/)")
        for rel in paths:
            text = scan.text(rel) or ""
            if pattern.search(text):
                return CheckResult(True, f"/{check['pattern']}/ in {rel}", stub=scan.files[rel].stub, file=rel)
        return CheckResult(False, f"/{check['pattern']}/ not found in {check['path']}")

    if kind == "json_key":
        rel = check["path"]
        text = scan.text(rel)
        if text is None:
            return CheckResult(False, f"{rel} does not exist (needed for key {check['key']})")
        try:
            found, _ = _dig(json.loads(text), check["key"])
        except json.JSONDecodeError:
            return CheckResult(False, f"{rel} is not valid JSON")
        if not found:
            return CheckResult(False, f"key {check['key']} missing in {rel}")
        return CheckResult(True, f"key {check['key']} in {rel}", file=rel)

    raise SpecError(f"unknown check type {kind}")


def evaluate(req: Requirement, scan: Scan) -> List[CheckResult]:
    return [run_check(c, scan) for c in req.checks]
