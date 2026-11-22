# T964 verify: B054 HUD objective tests + real EditMode run (B049-B054, T963)

**Verdict: PASS.** Real Unity EditMode run: **473 total, 473 passed, 0 failed**, 0 skipped
(Unity 6000.6.5f1 batch mode, `Biplanes/Logs/editmode-results.xml`, end 2026-11-20 18:52:57Z).
0 P0, 0 P1, 2 P2 (one stale test, fixed by QA; one text-grammar polish item).

## Runs
- `Biplanes/Temp/UnityLockfile` absent before both runs; the editor GUI was not opened.
- `python tools/compile_check.py`: PASS Biplanes, PASS Biplanes.Editor, exit 0 (before and after).
- Run 1: 473 total, 472 passed, 1 failed: `GameFlowTests.SecondRam_Crashes_ThroughTheSameLossPath`
  (GameFlowTests.cs:280, `Assert.IsTrue(plane.HasCrashed)`). Test-side, see bug 1.
- Run 2 (after the test fix): 473 / 473 passed, exit 0.
- Count: 468 earlier tests + 5 new = 473. T963 estimated 469 earlier tests; the real number
  is 468 (the estimate was off by one; no test is missing or skipped).
- Batch Unity created `Tests/EditMode/HudObjectiveQaTests.cs.meta` (expected).

## New tests: `Tests/EditMode/HudObjectiveQaTests.cs` (5, all pass)
M4-shaped data: destroyAllOfType barrageBalloon `pop_all_balloons` x3 + bombTarget enemyBase
`bomb_base` (targetId `enemy_base_01`). HUD under a MissionRunner with an initialised tracker,
Awake/OnDestroy by reflection (FighterCounterQaTests style). The hide coroutine is never ticked
in EditMode, so no StartCoroutine issues appeared (no logged errors).
- `M4_BaseFirst_KeepsBalloonCounter_ThenCompletesOnLastBalloon`: bomb first keeps
  "Destroy 3 more barrage balloons"; 3/2/1 counts; the 3rd pop shows "Objective Complete!";
  a late balloon event doesn't overwrite it.
- `M4_BalloonsFirst_ShowsBombTheBase_IgnoresLaterBalloons_ThenCompletes`: 3 pops -> "Bomb the
  enemy base" (not tracker-driven); 3 stray balloon kills and a repeated `pop_all_balloons`
  payload don't change it; bombing the base -> "Objective Complete!".
- `M4_BalloonsFirst_HudHearsKillBeforeTracker_StillMovesOn`: reverse event order on the last
  balloon; the pending refresh doesn't bring the counter back.
- `Standalone_AnyCompletion_ShowsObjectiveComplete`: `RaiseObjectiveCompleted("x")` on an
  unbound HUD shows "Objective Complete!".
- `SetObjective_AfterComplete_ShowsTheNewObjectiveAgain`: the next SetObjective re-shows text.

B054 conforms to UI_SPEC §3.3 Objective text row. T963 bug "HUD objective counter is cleared
by any objective completing" is fixed by B054 and now covered.

## Bugs

BUG: GameFlowTests ram test still assumed a 50% ram (missed by B052 and T963; fixed by QA)
PRIORITY: P2
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Tests/EditMode/GameFlowTests.cs:268-280
STEPS: Real run 1: `SecondRam_Crashes_ThroughTheSameLossPath` failed at :280, HasCrashed false.
EXPECTED: GDD change log 2026-11-20 (B052): one ram = planeCollision / resistance = 33.3%, so the third ram crashes.
ACTUAL: Code is right; the test crashed on the second ram. QA renamed it
`LethalRam_Crashes_ThroughTheSameLossPath` and reads the ram count from Balance
(2 survivable rams, the 3rd crashes through the same loss path). Passes now. No code change.

BUG: Singular objective count reads "Destroy 1 more barrage balloons"
PRIORITY: P2
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/UI/HudView.cs:522 (`ReadableType(_objectiveTargetType, true)` always plural)
STEPS: M4, pop 2 of 3 balloons; also "Destroy 1 more fighters" in M1.
EXPECTED: UI_SPEC §3.3 "Destroy N more <ReadableType>": natural English for N = 1 (polish).
ACTUAL: Always plural. Fix suggestion: `ReadableType(type, _objectiveRemaining != 1)`. Tests
asserting the 1-count text (HudObjectiveQaTests) would need the singular string after the fix.
