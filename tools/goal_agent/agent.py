"""The Iteration Loop.

    scan -> detect gaps -> rank (priority_score) -> write the best gap -> verify -> re-scan -> ...

A write is kept only when it passes every gate; otherwise it is undone and the reason goes
back to the backend on the next attempt:

    1. the reply parses and validates (paths, one type per file, JSON, no placeholders)
    2. the verifier passes (compile_check.py by default)
    3. a re-scan shows the gap closed or further along
    4. no requirement that was satisfied before is broken

The loop stops when
    complete          every requirement is satisfied
    nothing_to_write  what is left is not writable (art), blocked, stuck or only a stub
    max_iterations    the iteration budget ran out
    backend_error     the backend is unreachable (no point trying the next gap)
    dry_run           plan printed, nothing written
"""
import os
import subprocess
import sys
from dataclasses import dataclass, field
from typing import Callable, Dict, List, Optional, Set, Tuple

from .backends import BackendError
from .gaps import Detection, Gap, STUB, detect_gaps, unmapped_gdd_items
from .gdd import GddDoc, load_gdd
from .requirements import Requirement, load_spec
from .scanner import Scan, scan_codebase
from .scoring import build_context, rank, score_gap
from .writer import (MAX_FILES_PER_WRITE, SYSTEM_PROMPT, WriteError, apply_changes, build_prompt,
                     plan_changes)

COMPLETE, NOTHING, MAX_ITER, BACKEND_ERR, DRY_RUN = (
    "complete", "nothing_to_write", "max_iterations", "backend_error", "dry_run")


# ----------------------------------------------------------------------------- verifiers

class NoVerifier:
    name = "none"

    def verify(self) -> Tuple[bool, str, bool]:
        return True, "", True            # (ok, message, skipped)


class CompileVerifier:
    """Runs tools/compile_check.py (exit 0 pass, 1 errors, 3 can't run)."""
    name = "compile"

    def __init__(self, root: str, timeout: int = 900):
        self.script = os.path.join(root, "tools", "compile_check.py")
        self.root, self.timeout = root, timeout

    def verify(self) -> Tuple[bool, str, bool]:
        if not os.path.isfile(self.script):
            return True, "compile_check.py not found", True
        p = subprocess.run([sys.executable, self.script], cwd=self.root, capture_output=True,
                           text=True, timeout=self.timeout)
        out = (p.stdout + p.stderr).strip()
        if p.returncode == 0:
            return True, out, False
        if p.returncode == 3:
            return True, "compile check could not run: " + out[-300:], True
        return False, out[-3500:], False


# ----------------------------------------------------------------------------- records

@dataclass
class Attempt:
    number: int
    ok: bool
    message: str
    files: List[str] = field(default_factory=list)


@dataclass
class Iteration:
    index: int
    req_id: str
    title: str
    score: float
    status_before: str
    attempts: List[Attempt] = field(default_factory=list)
    outcome: str = "failed"             # closed | improved | failed
    unverified: bool = False


@dataclass
class RunResult:
    stop_reason: str
    iterations: List[Iteration]
    detection: Detection
    stuck: Set[str]
    plan: List[Tuple[float, Gap]] = field(default_factory=list)   # dry-run ranking
    unmapped: list = field(default_factory=list)

    @property
    def complete(self) -> bool:
        return self.stop_reason == COMPLETE


# ----------------------------------------------------------------------------- agent

class Agent:
    def __init__(self, root: str, gdd_path: str, spec_path: str, backend, verifier=None,
                 max_iterations: int = 10, max_attempts: int = 2, max_visits: int = 2,
                 dry_run: bool = False, log: Callable[[str], None] = print,
                 save_replies: Optional[str] = None, max_files: Optional[int] = None):
        self.root = os.path.abspath(root)
        self.gdd: GddDoc = load_gdd(gdd_path)
        self.reqs: List[Requirement] = load_spec(spec_path)
        self.backend = backend
        self.verifier = verifier or NoVerifier()
        self.max_iterations, self.max_attempts, self.max_visits = max_iterations, max_attempts, max_visits
        self.dry_run = dry_run
        self.log = log
        self.save_replies = save_replies
        self.max_files = max_files or MAX_FILES_PER_WRITE
        arch = os.path.join(self.root, "docs", "ARCHITECTURE.md")
        self.architecture = ""
        if os.path.isfile(arch):
            with open(arch, encoding="utf-8") as f:
                self.architecture = f.read()
        self.base = scan_codebase(self.root).base

    # -- the loop -------------------------------------------------------------------
    def run(self) -> RunResult:
        scan = scan_codebase(self.root)
        iterations: List[Iteration] = []
        attempts: Dict[str, int] = {}       # failed write attempts per requirement (feeds priority_score)
        visits: Dict[str, int] = {}         # how often a requirement used up all its attempts
        stuck: Set[str] = set()

        for index in range(1, self.max_iterations + 1):
            det = detect_gaps(self.reqs, scan, self.gdd)               # Gap-Detection Pass
            if not det.gaps:
                return self._result(COMPLETE, iterations, det, stuck)
            ctx = build_context(self.reqs, det.gaps, attempts)         # unmet = ALL gaps, for blocking
            candidates = [g for g in det.gaps if g.writable and g.req.id not in stuck]
            ranked = [(s, g) for s, g in rank(candidates, ctx) if s > 0]
            if self.dry_run:
                return self._result(DRY_RUN, iterations, det, stuck, plan=rank(det.gaps, ctx))
            if not ranked:
                return self._result(NOTHING, iterations, det, stuck)

            score, gap = ranked[0]
            self.log(f"[{index}/{self.max_iterations}] {gap.req.id} {gap.req.title}  "
                     f"(score {score:.1f}, {gap.status}, {gap.passed}/{gap.total} checks)")
            it = Iteration(index, gap.req.id, gap.req.title, score, gap.status)
            iterations.append(it)
            try:
                closed_or_better = self._close_gap(gap, scan, det, it, attempts)
            except BackendError as e:
                it.attempts.append(Attempt(len(it.attempts) + 1, False, f"backend error: {e}"))
                self.log(f"    backend error: {e}")
                return self._result(BACKEND_ERR, iterations, det, stuck)

            if not closed_or_better:
                visits[gap.req.id] = visits.get(gap.req.id, 0) + 1
                if visits[gap.req.id] >= self.max_visits:
                    stuck.add(gap.req.id)
                    self.log(f"    giving up on {gap.req.id}")
            scan = scan_codebase(self.root)                            # re-scan after every write

        det = detect_gaps(self.reqs, scan, self.gdd)
        return self._result(COMPLETE if not det.gaps else MAX_ITER, iterations, det, stuck)

    def _result(self, reason, iterations, det, stuck, plan=None) -> RunResult:
        return RunResult(reason, iterations, det, stuck, plan or [], unmapped_gdd_items(self.gdd, self.reqs))

    def _save(self, req_id: str, n: int, prompt: str, reply: str) -> None:
        """--save-replies: keep every prompt and raw reply so a rejection can be diagnosed."""
        if not self.save_replies:
            return
        os.makedirs(self.save_replies, exist_ok=True)
        stem = os.path.join(self.save_replies, f"{req_id}_attempt{n}")
        with open(stem + "_reply.txt", "w", encoding="utf-8") as f:
            f.write(reply)
        with open(stem + "_prompt.txt", "w", encoding="utf-8") as f:
            f.write(prompt)

    # -- Code-Writing Step for one gap ------------------------------------------------
    def _close_gap(self, gap: Gap, scan: Scan, det: Detection, it: Iteration, attempts: Dict[str, int]) -> bool:
        feedback = ""
        for n in range(1, self.max_attempts + 1):
            prompt = build_prompt(gap, scan, self.gdd, self.architecture, feedback)
            reply = self.backend.complete(SYSTEM_PROMPT, prompt)
            self._save(gap.req.id, n, prompt, reply)
            try:
                changes = plan_changes(reply, self.base, scan, self.max_files)
            except WriteError as e:
                feedback = str(e)
                it.attempts.append(Attempt(n, False, f"rejected: {feedback}"))
                attempts[gap.req.id] = attempts.get(gap.req.id, 0) + 1
                self.log(f"    attempt {n} rejected: {feedback}")
                self.log(f"    reply began: {reply.strip()[:200]!r} ({len(reply)} chars)")
                continue

            files = [c.path for c in changes]
            undo = apply_changes(changes, self.base)
            ok, message, skipped = self.verifier.verify()
            if not ok:
                undo.undo()
                feedback = "Compile errors (your files were reverted):\n" + message
                it.attempts.append(Attempt(n, False, "verify failed: " + message.splitlines()[-1] if message else "verify failed", files))
                attempts[gap.req.id] = attempts.get(gap.req.id, 0) + 1
                self.log(f"    attempt {n}: compile failed, reverted")
                continue

            new_scan = scan_codebase(self.root)
            new_det = detect_gaps(self.reqs, new_scan, self.gdd)
            broken = [r for r in det.satisfied if r in set(new_det.gap_ids)]
            new_gap = next((g for g in new_det.gaps if g.req.id == gap.req.id), None)
            better = new_gap is None or new_gap.progress > gap.progress
            if broken or not better:
                undo.undo()
                if broken:
                    feedback = "Your change broke requirements that were met before: " + ", ".join(broken)
                else:
                    feedback = "It compiled but the requirement is not closer to done. Still failing: " + \
                               "; ".join(new_gap.failures)
                it.attempts.append(Attempt(n, False, feedback.splitlines()[0], files))
                attempts[gap.req.id] = attempts.get(gap.req.id, 0) + 1
                self.log(f"    attempt {n}: {feedback.splitlines()[0]} (reverted)")
                continue

            it.attempts.append(Attempt(n, True, "kept" + (" (unverified: " + message[:80] + ")" if skipped and message else ""), files))
            it.outcome = "closed" if new_gap is None else "improved"
            it.unverified = skipped
            self.log(f"    attempt {n}: wrote {', '.join(files)} -> {it.outcome}")
            return True
        return False


# ----------------------------------------------------------------------------- report

def explain_remaining(gap: Gap, result: RunResult, reqs: List[Requirement]) -> str:
    req = gap.req
    unmet = set(result.detection.gap_ids)
    blockers = [d for d in req.deps if d in unmet]
    if not req.writable:
        return f"needs the {req.owner} agent (not code the agent may write)"
    if req.id in result.stuck:
        return "stuck: the backend's attempts were rejected or reverted"
    if blockers:
        return "blocked by " + ", ".join(blockers)
    if gap.status == STUB:
        return "only a placeholder exists"
    return "not reached (iteration limit)" if result.stop_reason == MAX_ITER else "open"


def render_report(result: RunResult, reqs: List[Requirement]) -> str:
    det = result.detection
    out = ["# Goal agent report", ""]
    out.append(f"- Stop reason: **{result.stop_reason}**")
    out.append(f"- Requirements: {len(reqs)} total, {len(det.satisfied)} met, {len(det.gaps)} open")
    out.append(f"- Iterations run: {len(result.iterations)}")
    if det.stale_anchors:
        out.append(f"- Spec anchors no longer in the GDD: {', '.join(det.stale_anchors)}")
    if result.unmapped:
        out.append(f"- GDD items no requirement tracks yet: {len(result.unmapped)}")
    if result.plan:
        out += ["", "## Plan (dry run)", "", "| # | Requirement | Status | Score | Note |", "|---|---|---|---|---|"]
        ctx = build_context(reqs, det.gaps)
        for n, (score, g) in enumerate(result.plan, 1):
            s = score_gap(g, ctx)
            note = ("blocked by " + ", ".join(s.blocked_by)) if s.blocked else \
                   ("not writable" if not g.writable else "")
            out.append(f"| {n} | {g.req.id} {g.req.title} | {g.status} | {score:.1f} | {note} |")
    if result.iterations:
        out += ["", "## Iterations", "", "| # | Requirement | Score | Outcome | Attempts |", "|---|---|---|---|---|"]
        for it in result.iterations:
            tries = "; ".join(f"{a.number}: {a.message}" for a in it.attempts)
            out.append(f"| {it.index} | {it.req_id} {it.title} | {it.score:.1f} | {it.outcome}"
                       f"{' (unverified)' if it.unverified else ''} | {tries} |")
    if det.gaps and not result.plan:
        out += ["", "## Still open", ""]
        out += [f"- **{g.req.id}** {g.req.title}: {explain_remaining(g, result, reqs)}" for g in det.gaps]
    return "\n".join(out) + "\n"
