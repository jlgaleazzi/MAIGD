"""Generator -> Evaluator -> Refiner, repeated until the artifact obeys the GDD Style Guide.

    Generator  writes a first candidate with the model (a new SVG asset or a mission
               briefing), from the rules pulled out of the GDD.
    Evaluator  code checks for every `auto` rule, plus the model as judge for `judge` rules.
    Refiner    1) code fixes for every fixable auto violation (deterministic, no model),
               2) the model for what is left (non-fixable and judge violations), with the
                  violations and the rules in the prompt.

Self-correction: every refined candidate is re-evaluated before it is kept. A step that
breaks the file or leaves more violations than before is rolled back, and the reason goes
into the next refine prompt. The loop stops when nothing is left (clean), when two rounds
in a row make no progress (stalled), or at --max-iterations.
"""
import json
import re
from dataclasses import dataclass, field
from typing import List, Optional

from . import rules as R
from .artifacts import ArtifactError, SvgArtifact, TextArtifact, parse_svg
from .guide import StyleGuide

MAX_MODEL_SVG_CHARS = 40000      # backgrounds are bigger: code fixes only


# ---- prompts -----------------------------------------------------------------------------

SYSTEM = """You are the style enforcer for "Biplanes", a WWI side-scrolling shooter drawn as
blueprints. You follow the game's Style Guide exactly. It is quoted from the Game Design
Document below; it is the only authority on how things look and read."""


def _guide_block(guide: StyleGuide) -> str:
    return f"=== STYLE GUIDE (from {guide.source}) ===\n{guide.text.strip()}\n=== END STYLE GUIDE ==="


def _format_block(art) -> str:
    if art.kind == "svg":
        return ("Reply with the complete SVG file only, from <svg ...> to </svg> (an XML comment "
                "at the top is fine). viewBox in pixels at 100 px = 1 Unity unit, transparent "
                "background, round caps and joins.")
    return 'Reply with JSON only: {"title": "...", "briefing": "..."}'


def _text_context(guide: StyleGuide, art) -> str:
    banned = sorted({b for _, bs in guide.vocab for b in bs}, key=str.lower)
    return (f"\nThe GDD describes this mission as: {art.gdd_mission}\n"
            f"Objective targets: {', '.join(art.targets)}. Enemies actually in this mission: "
            f"{', '.join(art.present) or 'unknown'}. Mention no other enemy type.\n"
            f"Never use these words: {', '.join(banned)}.\n"
            "Use plain ASCII hyphens.\n")


def generate_prompt(guide: StyleGuide, art, request: str) -> str:
    rules = R.auto_rules(guide, art) + R.judge_rules(guide, art)
    ctx = ""
    if art.kind == "text":
        ctx = _text_context(guide, art) + f"Current title: {art.title}\nCurrent briefing: {art.briefing}\n"
    return (f"{_guide_block(guide)}\n\nTASK: create {art.describe()}.\n{request}\n{ctx}\n"
            f"These rules will be checked; obey every one:\n{guide.rule_text([r.id for r in rules])}\n\n"
            f"{_format_block(art)}")


def judge_prompt(guide: StyleGuide, art, rule_ids: List[str]) -> str:
    body = art.text if art.kind == "svg" else json.dumps({"title": art.title, "briefing": art.briefing},
                                                         ensure_ascii=False)
    ctx = _text_context(guide, art) if art.kind == "text" else ""
    return (f"{_guide_block(guide)}\n\nEvaluate {art.describe()} against ONLY these rules:\n"
            f"{guide.rule_text(rule_ids)}\n{ctx}\nARTIFACT:\n{body}\n\n"
            'Reply with JSON only: {"violations": [{"rule": "<id>", "where": "<part>", '
            '"why": "<what breaks the rule and how to fix it>"}]}. Report a rule only if the '
            'artifact clearly breaks it; an empty list means it passes.')


def refine_prompt(guide: StyleGuide, art, violations: List[R.Violation], feedback: str) -> str:
    body = art.text if art.kind == "svg" else json.dumps({"title": art.title, "briefing": art.briefing},
                                                         ensure_ascii=False)
    extra = ""
    if art.kind == "text":
        extra = _text_context(guide, art)
    fb = f"\nYOUR PREVIOUS ATTEMPT WAS ROLLED BACK: {feedback}\n" if feedback else ""
    rules = R.auto_rules(guide, art) + R.judge_rules(guide, art)
    return (f"{_guide_block(guide)}\n\nTASK: revise {art.describe()} so it breaks none of the rules.\n"
            f"Make the smallest edit that fixes the violations: keep the existing sentences, facts and "
            f"wording wherever they don't break a rule, and add nothing that isn't in the source.{extra}\n"
            f"VIOLATIONS TO FIX:\n" + "\n".join(f"- {v.line()}" for v in violations) +
            f"\n{fb}\nAll rules (a fix must not break another):\n{guide.rule_text([r.id for r in rules])}\n\n"
            f"CURRENT ARTIFACT:\n{body}\n\n{_format_block(art)}")


# ---- parsing model replies ---------------------------------------------------------------

def _strip_fences(reply: str) -> str:
    m = re.search(r"```(?:\w+)?\s*\n(.*?)```", reply, re.S)
    return m.group(1) if m else reply


def parse_reply(art, reply: str):
    """Model reply -> new artifact of the same kind. Raises ArtifactError when unusable."""
    if art.kind == "svg":
        body = _strip_fences(reply)
        m = re.search(r"(<\?xml[^>]*\?>\s*)?(<!--.*?-->\s*)*<svg\b.*?</svg>", body, re.S)
        if not m:
            raise ArtifactError("reply has no <svg>...</svg>")
        text = m.group(0).strip() + "\n"
        parse_svg(text)
        return art.with_text(text)
    body = _strip_fences(reply)
    m = re.search(r"\{.*\}", body, re.S)
    if not m:
        raise ArtifactError("reply has no JSON object")
    try:
        data = json.loads(m.group(0))
    except json.JSONDecodeError as e:
        raise ArtifactError(f"bad JSON: {e}")
    title, briefing = str(data.get("title", "")).strip(), str(data.get("briefing", "")).strip()
    title, briefing = R.norm_text(title), R.norm_text(briefing)
    if not title or not briefing:
        raise ArtifactError("JSON needs non-empty title and briefing")
    return art.with_text(title, briefing)


def parse_judgement(reply: str, allowed: List[str]) -> List[R.Violation]:
    m = re.search(r"\{.*\}", _strip_fences(reply), re.S)
    if not m:
        return []
    try:
        items = json.loads(m.group(0)).get("violations", [])
    except (json.JSONDecodeError, AttributeError):
        return []
    out = []
    for it in items if isinstance(items, list) else []:
        rid = str(it.get("rule", "")).strip()
        if rid in allowed:
            out.append(R.Violation(rid, str(it.get("where", "")).strip() or "artifact",
                                   str(it.get("why", "")).strip(), fixable=False, source="judge"))
    return out


# ---- the three roles ---------------------------------------------------------------------

class Generator:
    def __init__(self, guide: StyleGuide, backend):
        self.guide, self.backend = guide, backend

    def generate(self, art, request: str):
        if self.backend is None:
            raise ArtifactError("generating needs a model backend (--backend anthropic|openai)")
        return parse_reply(art, self.backend.complete(SYSTEM, generate_prompt(self.guide, art, request)))


class Evaluator:
    def __init__(self, guide: StyleGuide, backend=None, judge: bool = True):
        self.guide, self.backend, self.judge = guide, backend, judge

    def evaluate(self, art) -> List[R.Violation]:
        found = R.check(self.guide, art)
        jr = [r.id for r in R.judge_rules(self.guide, art)]
        if self.judge and self.backend is not None and jr and self._fits(art):
            try:
                found += parse_judgement(self.backend.complete(SYSTEM, judge_prompt(self.guide, art, jr)), jr)
            except Exception as e:          # a judge outage must not stop the code checks
                found.append(R.Violation("JUDGE", "model", f"judge unavailable: {e}", False, "judge"))
        return found

    @staticmethod
    def _fits(art) -> bool:
        return art.kind != "svg" or len(art.text) <= MAX_MODEL_SVG_CHARS


class Refiner:
    def __init__(self, guide: StyleGuide, backend=None):
        self.guide, self.backend = guide, backend

    def code_fix(self, art):
        return R.auto_fix(self.guide, art)

    def model_fix(self, art, violations, feedback: str):
        if self.backend is None or not Evaluator._fits(art):
            return None
        return parse_reply(art, self.backend.complete(SYSTEM, refine_prompt(self.guide, art, violations, feedback)))


# ---- the loop ----------------------------------------------------------------------------

@dataclass
class Step:
    iteration: int
    action: str
    violations_before: int
    violations_after: int
    kept: bool
    note: str = ""


@dataclass
class Result:
    label: str
    status: str                 # clean | stalled | max_iterations | report_only | error
    original: object
    final: object
    violations: List[R.Violation]
    steps: List[Step] = field(default_factory=list)
    first_violations: List[R.Violation] = field(default_factory=list)

    @property
    def changed(self) -> bool:
        return self.final != self.original


def score(vs: List[R.Violation]) -> int:
    """Code violations count each; the model judge counts once per rule it flags, so a
    chatty judge listing one rule five ways doesn't outweigh real fixes."""
    auto = sum(1 for v in vs if v.source == "auto")
    judged = len({v.rule for v in vs if v.source == "judge" and v.rule != "JUDGE"})
    return auto + judged


def auto_score(vs: List[R.Violation]) -> int:
    return sum(1 for v in vs if v.source == "auto")


def run(art, guide: StyleGuide, backend=None, *, max_iterations: int = 4, generate: Optional[str] = None,
        judge: bool = True, log=print) -> Result:
    gen, ev, ref = Generator(guide, backend), Evaluator(guide, backend, judge), Refiner(guide, backend)
    original = art
    steps: List[Step] = []
    if generate is not None:
        art = gen.generate(art, generate)
        steps.append(Step(0, "generate", 0, 0, True, f"model wrote a first {art.kind} candidate"))
    current = ev.evaluate(art)
    first = list(current)
    log(f"  {art.label}: {score(current)} violation(s) at start")
    feedback, stall = "", 0
    for it in range(1, max_iterations + 1):
        if score([v for v in current if v.rule != "JUDGE"]) == 0:
            break
        before = score(current)
        progressed = False

        # 1) code fixes: deterministic, cheap, always tried first
        fixed, counts = ref.code_fix(art)
        if counts:
            # code fixes only touch code-checked rules: accept them on the code checks alone,
            # then re-judge, so judge noise can't veto a correct deterministic fix
            keep = auto_score(R.check(guide, fixed)) < auto_score(current)
            after = ev.evaluate(fixed) if keep else current
            steps.append(Step(it, "code fix " + ", ".join(f"{k} x{v}" for k, v in counts.items()),
                              before, score(after), keep, "" if keep else "rolled back: no improvement"))
            log(f"  [{it}] code fix {counts}: {before} -> {score(after)}{'' if keep else ' (rolled back)'}")
            if keep:
                art, current, before, progressed = fixed, after, score(after), True

        # 2) the model, for what code can't fix (or what the judge found)
        left = [v for v in current if v.rule != "JUDGE"]
        if left and backend is not None:
            try:
                cand = ref.model_fix(art, left, feedback)
            except Exception as e:
                cand, why = None, f"unusable reply: {e}"
            else:
                why = "" if cand is not None else "too large for the model; code fixes only"
            if cand is not None:
                if cand.kind == "text" and not any(v.where == "title" for v in left):
                    cand = cand.with_text(art.title, cand.briefing)   # nothing wrong with the title: keep it
                cand, _ = ref.code_fix(cand)        # the model's slips that code can fix (vocab...)
                after = ev.evaluate(cand)
                new_rules = sorted({v.rule for v in after} - {v.rule for v in current})
                # Code-checked rules are hard: a candidate that breaks one it didn't break before
                # is never kept, however many judge opinions it clears.
                new_hard = {v.rule for v in after if v.source == "auto"} - {v.rule for v in current if v.source == "auto"}
                keep = score(after) < before and not new_hard
                why = "" if keep else (f"it left {score(after)} violation(s) (was {before})"
                                       + (f" and broke {', '.join(new_rules)}" if new_rules else "")
                                       + ": " + "; ".join(v.line() for v in after[:4]))
                steps.append(Step(it, "model refine", before, score(after), keep, why))
                log(f"  [{it}] model refine: {before} -> {score(after)}{'' if keep else ' (rolled back)'}")
                if keep:
                    art, current, progressed, feedback = cand, after, True, ""
                else:
                    feedback = why
            elif why:
                steps.append(Step(it, "model refine", before, before, False, why))
                feedback = why if why.startswith("unusable") else feedback
                log(f"  [{it}] model refine skipped: {why}")

        stall = 0 if progressed else stall + 1
        if stall >= 2 or (not progressed and backend is None):
            break

    left = score([v for v in current if v.rule != "JUDGE"])
    if left == 0:
        status = "clean"
    elif backend is None:
        status = "needs_model"      # what is left has no code fix; run with a backend
    elif stall >= 2:
        status = "stalled"
    else:
        status = "max_iterations"
    return Result(art.label, status, original, art, current, steps, first)
