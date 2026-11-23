# T970 verify: B067 wreck nose pop fix (re-verifies T969 BUG-1)

**Verdict: PASS.** There are 0 open bugs. T969 BUG-1 is closed.

## What ran
| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes, PASS Biplanes.Editor (exit 0) |
| `UNITY_PATH="/d/Program Files/6000.6.5f1/Editor/Unity.exe" bash tools/unity.sh test` (there was no `Temp/UnityLockfile`, and only the Hub's unity.exe was running) | **603 total, 603 passed, 0 failed** (Logs/editmode-results.xml). That is the expected 602 plus 1 new QA test |
| `PlayerFlipQaTests.Crash_InTheFirstHalfOfATurn_WreckNoseDoesNotPopAtTheFlip` | Passed (it failed in T969) |
| `PlayerFlipTests.Crash_MidTurn_FinishesTheTurnOnTheWreck` | Passed |
| `GameFlowTests.Crash_TurnsColliderAndInputOff_LosesAtOnce` | Passed |
| New `PlayerFlipQaTests.Crash_LandedBeforeTheFlip_WreckMirrorsAndStaysPut` (landed-wreck regression) | Passed |

## Code review (PlayerPlane.UpdateCrashFall, lines 917-960)
- While `IsTurning`, the code steps the turn. If `VisualFacing` changed, it negates `_crashAngle`. For a landed wreck, it also re-applies `MoveRotation`. The drawn nose `rot(-a)*(-1,0) = (-cos a, sin a)` keeps its y and mirrors only its x, which matches what ARCHITECTURE §3 Player (B066) says about the pitch mirroring.
- The spin after the flip uses the new `VisualFacing`, so the nose keeps going down. The drift x still uses `_facing`, as the handoff says.
- The mirror runs once per facing change, and the landed path does not move the wreck. The new test checks both.

## Landed wreck regression
The new test puts the plane 0.001 above its rest height early in a turn, then forces a crash. The wreck lands on the first tick, before the flip. Through the rest of the turn the test checks that:
- the wreck stays landed,
- the nose y stays the same (to 1e-4),
- at the flip the angle is negated exactly once and only the nose x mirrors,
- the scale ends at 1.

## Observation (not a bug, no task needed)
`UpdateCrashFall` calls `FxManager.Instance` on landing, not `FxManager.GetOrNull()`. In EditMode this throws, because the lazy create calls DontDestroyOnLoad. In Play mode it works fine. The new test seeds `FxManager._instance` by reflection to get around it. If mechanics touches this code again, switching to `GetOrNull()` (the B047 pattern) would make the landing path testable without that seeding.

## Files changed by QA
- `Biplanes/Assets/Biplanes/Tests/EditMode/PlayerFlipQaTests.cs`: added `Crash_LandedBeforeTheFlip_WreckMirrorsAndStaysPut`.

## Manual playtest (needs the user)
- Climb, press A, and die within about 0.3 s. The wreck nose keeps pointing down through the flip, and the smoke tail follows it.
- Turn at low altitude and hit the ground before the turn's midpoint. The wreck lies still, mirrors once, and does not jump.
