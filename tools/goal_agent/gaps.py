"""Gap-Detection Pass: compare the scanned codebase with the GDD requirements.

A requirement is *satisfied* when every check passes and none of the evidence is a stub.
Otherwise it is a Gap:

    missing   no check passes: nothing of the feature exists yet
    partial   some checks pass: the feature is started
    stub      the checks pass but the evidence is a placeholder (scaffold, NotImplementedException)
"""
from dataclasses import dataclass, field
from typing import Dict, List, Tuple

from .gdd import GddDoc
from .requirements import CheckResult, Requirement, evaluate
from .scanner import Scan

MISSING, PARTIAL, STUB = "missing", "partial", "stub"


@dataclass
class Gap:
    req: Requirement
    status: str
    results: List[CheckResult]

    @property
    def passed(self) -> int:
        return sum(1 for r in self.results if r.ok and not r.stub)

    @property
    def total(self) -> int:
        return len(self.results)

    @property
    def progress(self) -> float:
        """0..1: how much of the feature exists. Per check: real evidence 1, a stub 0.5, nothing 0."""
        score = sum(0.5 if r.stub else 1.0 for r in self.results if r.ok)
        return score / self.total

    @property
    def failures(self) -> List[str]:
        out = [r.desc for r in self.results if not r.ok]
        out += [f"{r.desc} is only a placeholder (stub)" for r in self.results if r.ok and r.stub]
        return out

    @property
    def writable(self) -> bool:
        return self.req.writable


@dataclass
class Detection:
    gaps: List[Gap]
    satisfied: List[str]            # requirement ids that are met
    stale_anchors: List[str]        # requirement ids whose gdd_anchor is no longer in the GDD

    @property
    def gap_ids(self) -> List[str]:
        return [g.req.id for g in self.gaps]


def detect_gaps(reqs: List[Requirement], scan: Scan, gdd: GddDoc = None) -> Detection:
    gaps: List[Gap] = []
    satisfied: List[str] = []
    for req in reqs:
        results = evaluate(req, scan)
        ok = [r for r in results if r.ok]
        if len(ok) == len(results) and not any(r.stub for r in ok):
            satisfied.append(req.id)
            continue
        if not ok:
            status = MISSING
        elif len(ok) == len(results):
            status = STUB
        else:
            status = PARTIAL
        gaps.append(Gap(req, status, results))
    stale = [r.id for r in reqs if gdd is not None and not all(gdd.has(a) for a in r.anchors)]
    return Detection(gaps, satisfied, stale)


def unmapped_gdd_items(gdd: GddDoc, reqs: List[Requirement]):
    """GDD items that no requirement's anchor points at: features the spec doesn't track yet."""
    from .gdd import normalize
    anchors = [normalize(a) for r in reqs for a in r.anchors]
    return [i for i in gdd.items if not any(a in i.norm for a in anchors)]
