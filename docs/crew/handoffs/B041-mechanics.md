TASK: B041
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Missions/EnemyPressure.cs (new pure helper CountsTowardCap; CountLiveFightersNear uses it; doc comments)
- Biplanes/Assets/Biplanes/Scripts/Enemies/FighterAI.cs (class comment only)
- Biplanes/Assets/Biplanes/Tests/EditMode/EnemyPressureCapTests.cs (new, 10 tests)
- docs/crew/handoffs/B041-mechanics.md
SUMMARY: Fixes QA T958 N1. A fighter that is off screen and holding its distance or moving away no longer counts toward pressure.maxLiveFighters, so these "ghost" fighters can't block a reinforcement wave. Also fixes N2: the FighterAI class comment no longer says the gun cone points "(left)".
DECISIONS:
- New pure helper `EnemyPressure.CountsTowardCap(float x, float relativeVelocityX, Rect view, float range)`. It returns true only when:
  - the fighter is within `range` (CapRangeScreens = 1 screen width, as before) beyond either edge, AND
  - EITHER its hitbox (FighterHalfWidth 0.75) overlaps the view horizontally (on screen, whatever its velocity),
  - OR it is off screen and closing on the view in the camera's frame: left of the view and relVelX > ClosingSpeedEpsilon (0.1), or right of the view and relVelX < -0.1.
- `CountLiveFightersNear` passes relVelX = `f.WorldVelocityX - ScrollController.Velocity` (0 if there is no ScrollController). This is the same frame and the same epsilon as the B040 `IsEscaped` check.
- Effects of the rule:
  - Static fighters, and escorts whose WorldVelocityX is 0, count only if they are on screen, or if the moving camera is closing on them.
  - A camera pinned at a mission limit uses the fighter's own speed.
- The escape and removal rules are unchanged: no change to IsEscaped, margins, DecideEscape or the wave size. The QA hint's second option (escape at once when |relVelX| <= epsilon) was not applied, to keep the change to the cap rule only. A ghost fighter still lingers until it escapes or the player turns, but it no longer blocks reinforcements, so the sky still refills.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes (+2 new files not yet in the csproj), PASS Biplanes.Editor, exit 0. `tools/unity.sh` was not run because the editor has the project open.
- Tests compiled in a scratch build (B018 method), not run:
  - The runtime DLL was built with the compile_check helpers.
  - A temp copy of Biplanes.Tests.EditMode.csproj was retargeted v4.7.1 -> v4.7.2, with all 29 Tests/EditMode/*.cs listed explicitly.
  - Result: 0 errors. The temp csproj files were removed, and no `zz_*` files are left.
- With Unity closed, run `bash tools/unity.sh test`. Expected: EnemyPressureCapTests all pass (on screen, edge overlap, closing off screen, receding, holding distance/epsilon, beyond range, camera moving M1/M3 cases, camera pinned, 4 ghosts no longer block a wave of 2). EnemyPressureTests and EnemyPressureFixTests are unchanged and should pass.
- Playtest (QA checklist line "M3/M4 (N1)"): let a reinforcement pass you, then turn and chase it. Over the next 10-20 s the sky still refills within about 2.2 s whenever it is empty.
REQUESTS:
- orchestrator: ARCHITECTURE §3 "Enemy pressure (Missions/EnemyPressure.cs) - B035, B040". Change the heading to "- B035, B040, B041". Replace the sentence "`maxLiveFighters` (6) counts fighters within one screen width beyond either edge." with: "`maxLiveFighters` (6) counts live fighters on screen, plus those off screen within one screen width that are closing on the view in the camera's frame (fighter WorldVelocityX minus camera velocity, epsilon 0.1 u/s, the same frame as the escape check; `EnemyPressure.CountsTowardCap`). A fighter off screen that holds its distance or recedes never blocks a wave (B041)."
