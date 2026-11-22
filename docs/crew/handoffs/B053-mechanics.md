TASK: B053
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/UI/HudView.cs (the objective number now comes from ObjectiveTracker.RemainingKills; the private counter is only a fallback when there is no tracker)
- Biplanes/Assets/Biplanes/Scripts/UI/MinimapView.cs (destroyAllOfType diamonds use ObjectiveTracker.CountsAsKillOf; the reinforcement exclusion is gone)
SUMMARY: The HUD no longer counts kills itself. SetObjective finds the parent MissionRunner, its enabled ObjectiveTracker and the index of the matching destroyAllOfType objective once. Each Team.Enemy kill that CountsAsKillOf the target re-reads RemainingKills now and again on the next Update, so event order doesn't matter. The text uses ObjectiveTracker.ReadableType(type, true). The minimap gives reinforcements fighter-objective diamonds.
DECISIONS:
- HudView.SetObjective(targetType, total) keeps its signature, so MissionRunner is unchanged.
  - With a tracker, the number shown is the tracker's `_required - _kills`. Note: MissionRunner.CountSpawnedOfType counts spawn entries, not `count`, so the old initial number could differ from the tracker's required number. The tracker now wins.
  - With no runner or tracker (standalone HUD in tests), or for non-destroyAllOfType objectives, it falls back to the old decrement. The fallback now also requires Team.Enemy and CountsAsKillOf.
- Event order. MissionRunner subscribes to EntityDestroyed before the HUD exists, so the tracker normally updates first. If the HUD handler runs first, it shows the stale number for one frame. `_objectiveRefreshPending` makes Update re-read it on the next frame. When the kill completes the objective, ObjectiveCompleted sets `_objectiveActive = false` and the pending refresh is skipped, so "Objective Complete!" stays, the same as before.
- Text: "Destroy N more fighters" for fighters is unchanged. Other types now read e.g. "AAA guns" / "barrage balloons" (was "aaas" / "barrageballoons"). Colour: pencil while N > 0, ink at 0.
- New read-only properties for tests: `HudView.DisplayedObjectiveRemaining` and `HudView.ObjectiveFromTracker`.
- Minimap: `IsObjectiveTarget` destroyAllOfType now matches with `ObjectiveTracker.CountsAsKillOf(obj.targetType, typeKey)`, the same matcher the tracker uses. DrawAircraft passes "fighter" for every FighterAI with no name check, so this costs no per-frame allocation. The lookups run once in SetObjective, with no Find calls per frame.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes, PASS Biplanes.Editor, exit 0.
- unity.sh not run (per the brief). QA: run the EditMode suite after B049-B053. Manual check: in mission 1, kill JSON fighters and reinforcements. Every kill lowers "Destroy N more fighters", and reinforcements show minimap diamonds.
REQUESTS:
- qa: update these tests:
  - MinimapTests.cs:169: `IsObjectiveTarget(all, ScoreKeeper.ReinforcementFighterKey, "", true)` is now TRUE (assert IsTrue).
  - MinimapQaTests.cs:121-148 (LiveAircraft_..._ReinforcementsGetNoDiamond_...): DiamondCount becomes 3 (two fighters + the reinforcement), then 2 after leftFighter dies. Rename the test.
  - New HUD tests:
    - A HUD under a MissionRunner with a tracker shows RemainingKills and moves on a "fighterReinforcement" Team.Enemy kill.
    - Player/other-team kills don't move it.
    - The text never contains "fighterreinforcement".
    - The standalone fallback still decrements.
- orchestrator: in ARCHITECTURE/UI_SPEC §3.3, note that the HUD objective number is read from ObjectiveTracker.RemainingKills (no HUD-side counter), and that §3.3.1 minimap destroyAllOfType diamonds use ObjectiveTracker.CountsAsKillOf (reinforcements included). Optional follow-up (after B049): MissionRunner.CountSpawnedOfType counts entries, not `count`. It now only feeds the HUD fallback, but it could be removed or fixed.
