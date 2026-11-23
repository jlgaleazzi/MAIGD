# T969 verify: B066 player turn-around flip

**Verdict: FAIL.** There is 1 bug, a P2. Flight, the hitbox, the gun, bombs, scroll, reversals and resets all pass. The one failure is acceptance item "crash mid-turn OK / no pop": a wreck that crashes in the first half of a turn pops its nose from down to up at the flip.

## What ran
| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes, PASS Biplanes.Editor (exit 0) |
| Scratch build of `Biplanes.Tests.EditMode` (dotnet, against a fresh runtime build, net472; script in the session scratchpad, nothing written to the project) | PASS. It picks up the new PlayerFlipTests.cs and PlayerFlipQaTests.cs |
| `bash tools/unity.sh test` (no `Temp/UnityLockfile`; only the Hub's unity.exe was running; `UNITY_PATH="/d/Program Files/6000.6.5f1/Editor/Unity.exe"`) | **602 total, 601 passed, 1 failed**: `PlayerFlipQaTests.Crash_InTheFirstHalfOfATurn_WreckNoseDoesNotPopAtTheFlip` (BUG-1 below). PlayerFlipTests (11), PlayerFacingTests, PlanePitchTests, FxManagerTests, MinimapTests/MinimapQaTests and BombDirectionTests all pass |
| Static: namespaces, APIs exist, no `rb.velocity` / `UnityEngine.Input`, one public class per file, no hand-written YAML | PASS. Unity generated the two new test `.meta` files during the batch import |

Note: `tools/unity.sh` exits 3 unless `UNITY_PATH` is set. The editor is at `D:\Program Files\6000.6.5f1\Editor\Unity.exe`.

## Acceptance
| Criterion | Result | Evidence |
|---|---|---|
| Same curve and timing as the fighters (0.6 s, 0.15 at the midpoint) | PASS | Calls `FighterAI.StepHeading/FacingFromHeading/TurnScaleX` directly; Balance `player.turnSeconds` = 0.6; Turn_FollowsTheFighterCurve |
| No flipX pop in flight | PASS | flipX switches only at heading 0 (scale 0.15); ReverseBeforeTheMidpoint_NeverFlipsTheSprite |
| Facing, scroll, gun and bombs switch at once | PASS | Gun_FiresTheNewWay, Gun_AtTheSqueezeMidpoint_MuzzleIsUnscaled (-0.7), Bomb_DroppedOnTheFirstTickOfATurn (vx < 0 while the anchor slide pushes right, so B057 holds; offset (0, -0.35) unscaled) |
| Hitbox world size unchanged | PASS | World width, height and offset are constant every tick, and HitboxLowestPoint dy is constant through the 0.15 squash |
| Pitch mirrors at the midpoint, no nose flicker (flight) | PASS | Pitch_MatchesTheDrawnFacing |
| Reversing (once or twice) mid-turn | PASS | ReverseTwiceMidTurn: no heading jump > one step, ends on the last key |
| New plane resets scale and flipX | PASS | NewPlane_AfterAHalfDoneTurn: scale 1, flipX off, heading 1, local box at base size |
| Crash mid-turn | **FAIL (P2)** | It finishes the squash (passes), but the wreck nose pops at the flip (BUG-1) |
| No regression in MinimapView, FxManager smoke or bomb direction | PASS | Suite green apart from BUG-1. MinimapView uses `Facing` (immediate), as specified |

## Bugs

```
BUG: Wreck that crashes in the first half of a turn flips its nose from down to up at the squash midpoint
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs:917-926 (UpdateCrashFall), 893 (TriggerCrash _crashAngle = BodyAngle)
STEPS: Climb, press A, and get killed within ~0.3 s (heading still > 0). The wreck spins its nose down
  (_crashAngle -= VisualFacing*300*dt), and StepTurn keeps running. When the heading crosses 0, flipX and
  VisualFacing switch, but _crashAngle is not mirrored, so the drawn nose rot(a)*(VisualFacing,0) keeps
  its x mirror and also flips its y sign. The spin then also reverses, so the nose "rewinds" through level.
  EditMode: PlayerFlipQaTests.Crash_InTheFirstHalfOfATurn_WreckNoseDoesNotPopAtTheFlip fails at tick 12:
  angle -51, nose.y -0.839 -> +0.777 in one tick. The smoke tail (EmitSmoke, same nose vector) jumps with it.
EXPECTED: ARCHITECTURE §3 Player (B066): "the pitch rotation mirrors" at the midpoint; "a crash mid-turn
  finishes the squash on the wreck" with no pop. The wreck nose should keep pointing down. In flight
  BodyAngle = pitch*VisualFacing does this.
ACTUAL: The nose snaps from pointing down to pointing up at the flip, then spins back down.
FIX HINT: when VisualFacing changes during UpdateCrashFall, negate _crashAngle (mirror). Or freeze the turn
  on crash (snap heading to Facing in TriggerCrash and mirror _crashAngle once). Optionally also use
  VisualFacing for the wreck's drift x (TriggerCrash:891 uses _facing).
```

## Files added by QA
- `Biplanes/Assets/Biplanes/Tests/EditMode/PlayerFlipQaTests.cs`: 7 tests. Six pass. One fails until BUG-1 is fixed.

## Manual playtest (needs the user: Unity, Biplanes, BuildScenes, Play)
- Press A/D in level flight. The sprite squashes edge-on and widens mirrored over ~0.6 s, and the scroll and gun switch at once.
- Climb and reverse. The nose stays up through the turn.
- Tap A then D quickly (twice). There is no snap.
- Gizmos on: the hitbox keeps its width through the squash.
- Bomb during a turn. It falls the new way.
- Die just after pressing A (fly into terrain or flak). Watch the wreck nose for BUG-1.
- Retry or Next after a turn. The plane starts full width, facing right.
