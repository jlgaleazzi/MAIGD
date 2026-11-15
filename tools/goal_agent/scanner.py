"""Scan the Unity project: which files exist, what they contain, which types they declare.

Paths are relative to `<root>/Biplanes/Assets/Biplanes` and use forward slashes, which is
also how the spec and the writer refer to files (`Scripts/Enemies/FighterAI.cs`).
"""
import fnmatch
import os
import re
from dataclasses import dataclass, field
from typing import Dict, List, Optional

BASE_PARTS = ("Biplanes", "Assets", "Biplanes")
TEXT_EXT = {".cs", ".json", ".asmdef"}
BINARY_EXT = {".png"}
SKIP_DIRS = {"Library", "Temp", "obj", "Logs", "_Recovery", ".git"}

# Evidence in a file that it is a placeholder, not a finished implementation.
STUB_MARKERS = re.compile(r"GOAL-AGENT-SCAFFOLD|NotImplementedException|TODO\(goal-agent\)")

_COMMENTS = re.compile(r"//[^\n]*|/\*.*?\*/", re.S)
_STRINGS = re.compile(r'"(?:\\.|[^"\\\n])*"')
_TYPE_DECL = re.compile(
    r"(?:^|[{};])[ \t]*(?:\[[^\]\n]*\][ \t]*)*(?:(?:public|internal|private|protected|static|sealed|abstract|"
    r"partial|readonly)[ \t]+)*(?:class|struct|interface|enum)[ \t]+([A-Za-z_]\w*)", re.M)


def declared_types(cs_text: str) -> List[str]:
    """Type names declared in a C# file (comments and string literals ignored)."""
    clean = _STRINGS.sub('""', _COMMENTS.sub("", cs_text))
    return _TYPE_DECL.findall(clean)


@dataclass
class FileInfo:
    path: str
    text: Optional[str]      # None for binary files
    size: int
    stub: bool = False


@dataclass
class Scan:
    root: str
    base: str
    files: Dict[str, FileInfo] = field(default_factory=dict)
    classes: Dict[str, str] = field(default_factory=dict)   # type name -> file

    def has_file(self, rel: str) -> bool:
        return rel in self.files

    def text(self, rel: str) -> Optional[str]:
        f = self.files.get(rel)
        return f.text if f else None

    def glob(self, pattern: str) -> List[str]:
        return sorted(p for p in self.files if fnmatch.fnmatch(p, pattern))

    def line_count(self, rel: str) -> int:
        t = self.text(rel)
        return t.count("\n") + 1 if t else 0


def project_base(root: str) -> str:
    return os.path.join(root, *BASE_PARTS)


def scan_codebase(root: str) -> Scan:
    base = project_base(root)
    scan = Scan(root=root, base=base)
    if not os.path.isdir(base):
        return scan
    for dirpath, dirnames, filenames in os.walk(base):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in filenames:
            ext = os.path.splitext(name)[1].lower()
            if name.endswith(".meta") or ext not in TEXT_EXT | BINARY_EXT:
                continue
            full = os.path.join(dirpath, name)
            rel = os.path.relpath(full, base).replace(os.sep, "/")
            size = os.path.getsize(full)
            if ext in BINARY_EXT:
                scan.files[rel] = FileInfo(rel, None, size)
                continue
            with open(full, encoding="utf-8-sig", errors="replace", newline="") as f:
                text = f.read().replace("\r\n", "\n")
            info = FileInfo(rel, text, size, stub=bool(STUB_MARKERS.search(text)))
            scan.files[rel] = info
            if ext == ".cs":
                for t in declared_types(text):
                    scan.classes.setdefault(t, rel)
    return scan
