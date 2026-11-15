"""priority_score(): decide which gap the agent closes next.

    value = W_IMPACT*impact + W_CORE*core + W_UNBLOCK*(unmet requirements it unblocks)
    cost  = effort * (1 + W_RETRY*failed_attempts) * (1 - W_PROGRESS*progress)
    score = value / cost                     (0.0 when a dependency is still unmet)

Why these terms:
- impact: what the GDD cares about most goes first.
- core: the core loop (player, controls, scoring) beats content that sits on top of it.
- unblocks: a gap that other gaps depend on (transitively) is worth the sum of what it frees.
- effort: among equally valuable gaps, the cheaper one first (value per unit of work).
- progress: a half-built feature is cheaper to finish than starting from nothing.
- retries: a gap the writer already failed on drifts down so one stubborn gap can't starve the rest.
- blocked: a gap whose dependencies are unmet scores 0.0 and is never picked, because its
  code would reference types that don't exist yet. Real scores are always > 0.
"""
from dataclasses import dataclass, field
from typing import Dict, Iterable, List, Set, Tuple

from .gaps import Gap

W_IMPACT = 10.0
W_CORE = 8.0
W_UNBLOCK = 4.0
W_PROGRESS = 0.5     # a fully built feature would cost half
W_RETRY = 0.5        # each failed attempt makes the gap 50% "more expensive"


@dataclass
class ScoreContext:
    unmet: Set[str]                                       # ids of every unmet requirement
    dependents: Dict[str, Set[str]] = field(default_factory=dict)   # id -> ids that directly depend on it
    attempts: Dict[str, int] = field(default_factory=dict)          # id -> failed write attempts so far

    def unblocked_by(self, req_id: str) -> Set[str]:
        """Unmet requirements that (transitively) depend on req_id."""
        seen: Set[str] = set()
        stack = list(self.dependents.get(req_id, ()))
        while stack:
            d = stack.pop()
            if d in seen:
                continue
            seen.add(d)
            stack.extend(self.dependents.get(d, ()))
        return seen & self.unmet


@dataclass
class Score:
    total: float
    blocked_by: List[str]
    impact: float = 0.0
    core: float = 0.0
    unblock: float = 0.0
    cost: float = 1.0

    @property
    def blocked(self) -> bool:
        return bool(self.blocked_by)


def build_context(reqs, gaps: Iterable[Gap], attempts: Dict[str, int] = None) -> ScoreContext:
    dependents: Dict[str, Set[str]] = {}
    for r in reqs:
        for d in r.deps:
            dependents.setdefault(d, set()).add(r.id)
    return ScoreContext({g.req.id for g in gaps}, dependents, dict(attempts or {}))


def score_gap(gap: Gap, ctx: ScoreContext) -> Score:
    req = gap.req
    blocked_by = sorted(d for d in req.deps if d in ctx.unmet)
    if blocked_by:
        return Score(0.0, blocked_by)
    impact = W_IMPACT * req.impact
    core = W_CORE if req.core else 0.0
    unblock = W_UNBLOCK * len(ctx.unblocked_by(req.id))
    retries = ctx.attempts.get(req.id, 0)
    cost = req.effort * (1 + W_RETRY * retries) * (1 - W_PROGRESS * gap.progress)
    return Score((impact + core + unblock) / cost, [], impact, core, unblock, cost)


def priority_score(gap: Gap, ctx: ScoreContext) -> float:
    """Higher = do it sooner. 0.0 = blocked by an unmet dependency."""
    return score_gap(gap, ctx).total


def rank(gaps: List[Gap], ctx: ScoreContext) -> List[Tuple[float, Gap]]:
    """Gaps sorted best-first; ties break on requirement id so the order is deterministic."""
    scored = [(priority_score(g, ctx), g) for g in gaps]
    return sorted(scored, key=lambda t: (-t[0], t[1].req.id))
