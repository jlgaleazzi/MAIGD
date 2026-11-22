TASK: B040
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Missions/EnemyPressure.cs (liveness check, facing-side entry, escape handling, pure helpers)
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs (SpawnReinforcementFighter on the facing edge; new PlayerFacing, OpenDestroyAllCounts, ReEnterFighter)
- Biplanes/Assets/Biplanes/Scripts/Enemies/FighterAI.cs (flight direction, ReEnter, WorldVelocityX/Pattern/IsEscorting, 3-hit doc, fallback 75)
- Biplanes/Assets/Biplanes/Resources/Balance.json (damageTaken.fighterBullet 8 -> 5, nothing else)
- Biplanes/Assets/Biplanes/Tests/EditMode/EnemyPressureFixTests.cs (new, 17 tests)
- docs/crew/handoffs/B040-mechanics.md
SUMMARY: Fixes T957 BUG 1 (EnemyPressure.IsLiveAircraft: an IDamageable answers itself, otherwise the Health component is asked, so there is no cast), BUG 3 (reinforcements enter past the edge the player faces and fly toward the player; FighterAI can now fly right), BUG 2 and 6 (escaped fighters re-enter at the leading edge while an open destroyAllOfType counts them, otherwise they are removed silently), BUG 7 (plane 0 enters within 0.5 u of the view) and BUG 5 (3-hit doc, fallback 75). Also applies fighterBullet 5.
DECISIONS:
- FighterAI direction: `_direction` (-1 by default) drives the movement for straight, sine, dive and escort-fallback, plus Forward (the gun cone), BodyAngle, NoseDirection and flipX. New public API: `FlightDirection`, `SetFlightDirection(int)`, `ReEnter(Vector3, int)` (same object, same name and damage; it resets the sine/dive base y and pitch tracking), `WorldVelocityX` (0 for static or while escorting), `Pattern`, `IsEscorting`. JSON fighters still fly left, unchanged.
- Entry (reinforcements and re-entries): `EntryX(camX, halfW, facing, offset)` = the facing edge ± offset, and they fly `-facing`. `EntryOffset(i, t)` = 0.75 + 0.25 + 1.25*i + 0.25*t. Facing comes from `MissionRunner.PlayerFacing` (ScrollController.Direction, else PlayerPlane.Facing, else +1).
- BUG 7 worst case: plane 0 is at most 0.5 u from overlapping the view. The slowest close is 3 u/s (camera pinned at a mission limit), so it is on screen at most about 0.17 s after the wave fires. That makes the worst gap about 2.17 s plus one frame for detection, and about 2.1 s when the camera is moving (closing at 5-6 u/s). The timer still fires at maxQuietSeconds.
- Escape test (`IsEscaped(x, relVelX, view, margin)`) works in the camera's frame. relVelX = fighter WorldVelocityX - ScrollController.Velocity. A fighter has escaped when it is more than `margin` beyond the left edge and not moving right relative to the view, or beyond the right edge and not moving left (epsilon 0.1 u/s). This is the brief's "past the trailing edge", generalised so it also covers backtracking:
  - M1 facing left: camera -2 vs fighter -3, so the gap keeps growing. Escaped.
  - M3/M4 facing left: fighters right of the view at equal speed hold their distance forever. Escaped.
  - Fighters still closing (about to enter, or catching up) never count.
  - Margins: JSON fighters 3 u (`EscapeMargin`), reinforcements 20 u (`DespawnBehindMargin`, the B035 rule, now on either side).
  - Escorts with a live target are skipped.
- Decision (`DecideEscape`): a reinforcement is removed. A JSON fighter re-enters when `MissionRunner.OpenDestroyAllCounts(name)` is true (mission running, unevaluated destroyAllOfType, case-insensitive type match, the same as ObjectiveTracker). Otherwise it is removed silently with Destroy (Health never raises EntityDestroyed, so no score and no credit).
- Escapes are handled every frame even when `pressure.enabled` is false, because they keep destroy-all winnable and bound the live fighter count (BUG 6).
- Side effect to know: in M3/M4 (scroll 3), a player who faces left for long enough has the not-yet-seen JSON fighters that hold distance off the right edge removed. They would never arrive while the player flies left, and EnemyPressure covers the gap.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes (+2 new files not yet in the csproj), PASS Biplanes.Editor, exit 0. I didn't run `tools/unity.sh` because the editor has the project open.
- Tests compiled in a scratch build (B018 method), not run:
  - The runtime DLL was built with compile_check helpers.
  - A temp copy of Biplanes.Tests.EditMode.csproj was retargeted v4.7.1 -> v4.7.2, with all 28 Tests/EditMode/*.cs listed explicitly.
  - Result: 0 errors. The temp csproj files were removed from the project.
- With Unity closed, run `bash tools/unity.sh test`. Expected:
  - EnemyPressureFixTests: all pass.
  - WhiteOnlyAndPressureQaTests.Pressure_InViewScan_*: now pass (BUG 1).
  - EnemyPressureTests: unchanged, pass.
  - EnemyFireTests.EnemyBullet_FighterShotDeals8Percent_BomberShotDeals10Percent: now fails (see REQUESTS: qa).
- Play checks (Unity -> Biplanes -> BuildScenes, Play):
  - M2: no InvalidCastException in the Console while the bomber lives, and reinforcements come when the sky is empty for 2 s.
  - M3: face left for 5 s. Two fighters enter from the LEFT edge flying right, about 2.2 s after the sky empties, and there is no burst when you turn back right.
  - M1: let a fighter pass. A few units past the left edge it reappears just past the edge you face, flying at you, with the same damage. The HUD count still needs it.
  - M2-M5: fighters that get past you vanish off screen. No score is given for them.
  - A fighter shot now costs 5%.
REQUESTS:
- qa: EnemyFireTests.cs:172-178 `EnemyBullet_FighterShotDeals8Percent_BomberShotDeals10Percent` hard-codes 8f. Change it to 5f, or better, to `GameBalance.Get().damageTaken.fighterBullet`, and rename it. No other test assumes 8 (EnemyWeaponsAndPoolTests:601 reads Balance) or a left-only fighter (PlanePitchTests:172 sets flipX itself for a default left fighter, which is still correct).
- orchestrator: ARCHITECTURE §3 "Enemy pressure" doc changes:
  (1) Line 305-306: "just past the screen edge on the right" becomes "just past the edge the player faces (MissionRunner.PlayerFacing), flying toward the player".
  (2) Line 311-313: the entry offset becomes FighterHalfWidth 0.75 + EntryGap 0.25 + EntryStagger 1.25*i + rand(0..EntryJitter 0.25). Worst case: on screen at most about 0.17 s after the 2 s limit.
  (3) Removal now works in the camera's frame, on either side. A fighter has escaped when it is more than the margin outside the view and not closing on it (EnemyPressure.IsEscaped). Reinforcements use 20 u. JSON fighters use 3 u (EscapeMargin) and then ReEnter while `MissionRunner.OpenDestroyAllCounts(type)`, otherwise they are removed silently. Escorts with a live target are exempt.
  (4) Line 315-316: "fighters never despawn" no longer holds. The cap range stays one screen width.
  (5) The EnemyAircraft liveness check is `EnemyPressure.IsLiveAircraft` (IDamageable, else Health).
  (6) MissionRunner API adds `PlayerFacing`, `OpenDestroyAllCounts(string)` and `ReEnterFighter(FighterAI)`. FighterAI API adds `FlightDirection`, `SetFlightDirection`, `ReEnter`, `WorldVelocityX`, `Pattern` and `IsEscorting`. Under §3 Enemies, fighters fly left by default, and reinforcements and re-entries may fly right.
- orchestrator (design note, no change made): M1 still loses at the map end (camera x 300) if a fighter the objective needs is alive. Now that escapees come back, the player has to finish them before 300, or turn back to fight them. Consider whether a re-entering fighter near the end needs a HUD hint, or whether destroy-all should keep the camera below the length until it is done.
