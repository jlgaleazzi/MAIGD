# T963 verify: B049, B050 + B053, B051, B052

**Verdict: PASS.** 0 P0, 0 P1, 4 P2. Two of the P2s are doc comments, one is a test that
B052 missed (QA fixed it), and one HUD behaviour predates these changes.

The real Unity EditMode run is **pending**, because the Unity editor has the project open,
so `tools/unity.sh test` can't run. Everything below comes from the compile checks, a full
read of the code, and the tests I updated or added.

## Build and compile
- `python tools/compile_check.py`: PASS Biplanes, PASS Biplanes.Editor, exit 0.
- Test assembly, built with the B018 scratch method: the fresh runtime build plus all 35
  `Tests/EditMode/*.cs` files. ProjectReferences were swapped for DLLs and the target was
  retargeted to v4.7.2. Result: 0 errors. No temp csproj was left in the project.
- Static checks on the changed files:
  - No `rb.velocity` and no `UnityEngine.Input`. `PlayerInput` uses `UnityEngine.InputSystem`.
  - Namespaces match the folders. One public class per file.
  - No hand-written `.meta`, `.prefab`, `.unity` or `.asset` files. The new test files have
    no .meta yet; Unity will create them.

## Tests updated for the five changes (they had become wrong)
| File | Change |
|---|---|
| ObjectiveTrackerTests | M1 test renamed `DestroyAllOfType_M1_JsonAndReinforcementKillsCount_CompletesOnce`: total-3 JSON kills, non-counting kills (other type, Team.Player, Neutral), 2 reinforcement kills reach total-1, and the last kill is a reinforcement, which completes the objective once. `Runner_Next_...`: total-4 fighters + 3 reinforcements = total-1, not won; the next kill wins once. The ReinforcementKey doc comment is updated. |
| EnemyPressureTests | `Reinforcement_ScoresLikeAFighter_AndCountsForDestroyAll` (renamed, flipped): the first reinforcement kill completes required 1, and later kills raise no second ObjectiveCompleted. |
| MinimapTests:169 | `IsObjectiveTarget(all, fighterReinforcement)` is now IsTrue. |
| MinimapQaTests | `LiveAircraft_..._ReinforcementsGetDiamonds_...`: 3 diamonds, then 2 after the left fighter dies, then 1 (the reinforcement) after the right fighter is torn down. |
| EnemyFireTests | Fighter and bomber shots are `damageTaken.X / PlayerData.ResistanceOrDefault`. |
| EnemyWeaponsAndPoolTests | Flak shell, the bomb (bomberGunner) and the bullet are divided by resistance. Collision and enemy asserts are unchanged. |
| ZeppelinAndBaseTests | The zeppelinTurret and flak percents (4 asserts) are divided by resistance. |
| PlaneCollisionTests | New `ContactPercent` helper (planeCollision / resistance). The 50% asserts now expect 33.3%. `SecondContact_...` became `ThirdContact_AfterGrace_Reaches100_AndCrashes`: 2 contacts = 66.7% and alive, the 3rd contact = 100%, crashed, 3 EntityDestroyed. The two `Takes50` test names were renamed. |
| **GameFlowTests** (missed by B052) | `FlakTo100_CrashesOnNextTick_NotBefore` hard-coded 6 x 15 = 90 and then 1 more hit to 100. With resistance that is 70%, then 70% again, so the test would fail. It now reads the hits-to-kill from Balance (10): 9 hits fly, and the 10th reaches 100 and crashes. |

Other tests I checked, which should still pass:
- FxManagerTests:267 loops until the smoke threshold, 7 x 10 = 70.
- MissionEndRulesTests:450 is a relative heal check.
- The Audio* tests check cues, not exact percents.
- The TerrainContact / Building / ForceCrash tests: Collision is not scaled.
- PlayerInputGateTests settle with an empty step, so `Movement_PassesThrough` still holds.
- PlayerFacingTests and PlanePitchTests use an unheld `ScrollController.Create`.
- The runner-based tests move the camera directly and never tick ScrollController, so the hold doesn't affect them.
- EnemyPressureFixTests:344 is still correct: a reinforcement never re-enters.

## New tests: `Tests/EditMode/FighterCounterQaTests.cs` (8)
- `CountsAsKillOf_ReinforcementCountsForFighterOnly`: covers fighter<-reinforcement, case-insensitivity, bomber<-reinforcement false, the reverse false, and null or empty.
- `RequiredCount_IsTheJsonFighterTotal_ReinforcementEntriesAddNothing`.
- `RemainingKills_DropsByOnePerCountedKill_FloorsAtZero_CompletesOnce`: covers other teams and types, completion once, the floor at 0, non-destroyAll objectives and a bad index.
- `Hud_UnderRunner_ShowsTrackerCount_AndMovesOnAReinforcementKill`:
  - The tracker number beats the `total` argument (99 -> 6).
  - A reinforcement kill moves it.
  - Event order is checked both ways: with the HUD first, it re-reads on the next Update.
  - The text never contains "fighterreinforcement".
- `Hud_UnderRunner_IgnoresNonEnemyAndOtherTypeKills`.
- `Hud_Standalone_FallbackCountsDown_EnemyFighterKillsOnly`: a reinforcement counts, a player-team kill or another type doesn't, and the count floors at 0.
- `Hud_ObjectiveText_UsesReadableTypeNames`: "AAA guns", "barrage balloons".
- `PlayerDeathSnap_NeverKillsOnANonLethalTotal`:
  - 29 bullets (96.7%) leave the player alive.
  - 2 contacts plus bullets up to 96.7% leave the player alive.
  - A raw 99.99% leaves the player alive.
  - A heal never snaps the total to 100.

Expected real suite: 418 (last real run) + 19 (B049) + 7 (B051) + 17 (B052) + 8 (T963) =
**469 tests**. Renames and rewrites don't change the count.

When Unity is closed, run `UNITY_PATH="/d/Program Files/6000.6.5f1/Editor/Unity.exe" bash tools/unity.sh test`.
The fixtures to watch are:
- MissionStartGateTests, FighterSmokeTests, PlayerResistanceTests, FighterCounterQaTests
- ObjectiveTrackerTests, EnemyPressureTests, MinimapTests, MinimapQaTests
- EnemyFireTests, EnemyWeaponsAndPoolTests, ZeppelinAndBaseTests, PlaneCollisionTests, GameFlowTests
- FxManagerTests, PlayerInputGateTests, PlayerFacingTests, PlanePitchTests, ScrollControllerTests

## Verification by reading
**B049 (mission start gate): OK** (GDD "Mission start", ARCH §3 "Mission start gate")
- `MissionRunner.StartMission` calls `Hold()` after `SetLimits`. Retry (reload) and Next (re-run) both go through StartMission, so they wait again.
- `ScrollController.FixedUpdate` while held: velocity is 0 and it returns before moving the camera, so FarthestX, the parallax (camera-driven LateUpdate) and the minimap stay still.
- `Release` eases up from 0 with the turn accel.
- No spawns while waiting. Every first spawn is at atX >= 30 (M1) or 40-66 (M2-M5), and the camera starts at 0. Ground look-ahead is about 10.9, so no entry triggers at FarthestX = 0.
- The plane holds. The body is Kinematic, and `OnMove` returns before any MovePosition while `IsWaitingForStart && !AnyAction`.
- EnemyPressure: `eligible` requires `_runner.HasStarted`, so `Step` keeps resetting the quiet timer.
- A key held through the briefing is ignored. While blocked, `_upNeedsRelease = raw.up` etc., so a held key is spent until it is released. Fire and bomb were already spent. Reset (OnEnable) spends everything.
- The first input acts:
  - Move: `BeginMission`, then the same tick moves or turns.
  - Fire: OnMove releases, then `OnFire` fires in the same FixedUpdate. `OnFire` also calls BeginMission.
  - Bomb: `OnDropBomb` calls BeginMission, then drops.
- Runway: the airborne branch is skipped. Only `state.up || state.right` takes off and calls BeginMission. `OnFire` and `OnDropBomb` return on `_onRunway`. A and S do nothing.
- Pause while waiting: PauseView blocks input via `IsShowing` and timeScale 0. ScrollController stays held, and Resume needs a fresh key press.

**B050 + B053 (fighter counter): OK** (GDD "Enemy pressure", change log 2026-11-20, UI_SPEC §3.3, §3.3.1)
- The required count is `CountOfType` with an exact match, which equals the JSON total: M1 37, M3 aaa 5, M4 barrageBalloon 6.
- `OnEntityDestroyed` takes Team.Enemy only and matches with `CountsAsKillOf`. Each kill is counted once, because Health raises EntityDestroyed once (`IsDead` guard) and FighterAI.FixedUpdate does not raise it again.
- Completion happens once: `obj.evaluated` skips later kills, and `Complete` is guarded.
- HUD:
  - Binds once in SetObjective to the parent runner's enabled tracker and index. Reads `RemainingKills`, then again on the next Update.
  - Fallback is Team.Enemy + `CountsAsKillOf` only.
  - Text uses `ReadableType`.
- Minimap: `IsObjectiveTarget` destroyAll uses `CountsAsKillOf`. DrawAircraft passes "fighter" for every FighterAI, so reinforcements get diamonds.
- `EnemyPressure.HandleEscapedFighters` still never re-enters reinforcements, which is correct.

**B051 (damaged fighter smoke): OK** (GDD Enemies item 1, ARCH §3 FX)
- Interval: 0 when healthy or dead, 0.25 s after 1 hit, 0.12 s from 2 hits. On the 2nd hit the timer is clamped to the shorter interval at once.
- `HitsFromDamage(33.3%, 75, 25)` = 1, and 66.7% = 2.
- `SmokeTailPoint = center - NoseDirection(pitch, dir) * 0.75`:
  - Left flight puts the tail on the right; right flight puts it on the left.
  - Pitch moves the tail opposite the nose.
- ReEnter keeps the same object, so the damage and the smoke carry over. Paused (dt 0) emits nothing.
- FxManager: separate `Kind.EnemySmokePuff`, cap 32, recycling only within that kind. The player's SmokePuff cap stays 24 and is never recycled by enemy smoke.
- Same `smoke_puff` sprite. The colour is never tinted (only alpha), so it stays white only.

**B052 (player 50% tougher): OK** (GDD change log 2026-11-20, ARCH §3 Health, §4 player.resistance)
- Balance `player.resistance` is 1.5.
- Player branch: `ApplyPlayerResistance(percent * multiplier, src, res)`.
  - Bullet, Bomb, Flak, ZeppelinTurret and PlaneCollision are divided by the resistance.
  - Collision and Heal (and any value <= 0) pass through unchanged.
- Enemies (hit-point branch) are untouched.
- Heals are 1:1: `Heal()` is untouched, and HealthBalloon's negative Heal passes through.
- PlayerDeathSnap 0.001 applies only for player mode with a positive hit. Every real player hit is a multiple of 1/3 % (3.33, 6.67, 10, 8, 33.3), so the closest non-lethal total is 99.67%. The snap can't kill a non-lethal total, and the new test covers this.
- Hits to crash: 3 contacts, 30 bullets, 10 flak. This matches the GDD change log.

## Bugs
```
BUG: Stale doc comments still say reinforcements never count toward destroyAllOfType
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Scripts/Missions/EnemyPressure.cs:25-26, Scripts/Missions/MissionRunner.cs:431-432, Scripts/Core/ScoreKeeper.cs:50
STEPS: grep "never count" in Scripts/
EXPECTED: GDD "Enemy pressure" / ARCH §3: reinforcement kills count toward a "fighter" destroy-all objective (B050)
ACTUAL: three XML doc comments still describe the old rule (behaviour is correct)
```
```
BUG: B052 missed a player-damage test (fixed by QA in T963)
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Tests/EditMode/GameFlowTests.cs FlakTo100_CrashesOnNextTick_NotBefore
STEPS: static: 7 flak hits were hard-coded as 6 x 15 = 90 then 100; with resistance 1.5 that is 60% then 70%, not crashed -> the test would fail
EXPECTED: tests read damage from Balance (damageTaken.X / PlayerData.ResistanceOrDefault)
ACTUAL: fixed in T963 to compute hits-to-kill from Balance; noted so future Balance changes grep GameFlowTests too
```
```
BUG: HUD objective counter is cleared by any objective completing (pre-existing, not from B049-B053)
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Scripts/UI/HudView.cs:495-504 OnObjectiveCompleted (ignores objectiveId)
STEPS: mission 4 has destroyAllOfType barrageBalloon + bombTarget enemy_base_01. Bomb the base before all balloons are down.
EXPECTED: UI_SPEC §3.3: "Destroy N more barrage balloons" stays until that objective completes
ACTUAL: "Objective Complete!" shows and the balloon counter disappears while balloons remain (the mission does not end; only the HUD text is wrong)
```
```
BUG: B053 handoff says MissionRunner.CountSpawnedOfType counts entries, not `count`
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Scripts/Missions/MissionRunner.cs:810-819
STEPS: read: it sums spawn.count (exact, case-insensitive)
EXPECTED: n/a (documentation accuracy only)
ACTUAL: the code is fine; the handoff's "optional follow-up" is unnecessary. Only the HUD fallback uses it now.
```

## Playtest checklist (these five changes)
1. **Wait for the first input**, in M1-M5 after Begin: background, parallax, terrain and minimap stay still. The plane hovers (M2-M5) or sits on the runway (M1). No enemies appear and no reinforcements come, even after 5+ s.
2. Hold D through the briefing and Begin: nothing happens until D is released and pressed again. Then the world eases into motion and the plane faces right.
3. With the airborne start (M2), check each kind of first input: W climbs and starts; A turns left and starts (the scroll eases left); click fires at once and starts; Space drops a bomb and starts.
4. M1 runway: A, S, click and Space do nothing. W or D takes off and starts the mission.
5. Esc while waiting: pause works. Resume: still waiting. Retry and Next from the result screen: each waits again.
6. **Counter**, in M1: the start reads "Destroy 37 more fighters". Kill JSON fighters and reinforcements; every kill lowers it by 1, and the text never shows "fighterreinforcement". Every live fighter, reinforcements included, has a minimap diamond. The win comes at the 37th kill, whichever kind it is.
7. **Fighter smoke**: one hit gives a thin pencil trail from the tail, two hits a denser one, and the third hit explodes it and the smoke stops. Check a right-flying reinforcement (smoke on its left side) and a climbing or diving fighter (the smoke follows the tail). With many damaged fighters on screen, the player's 70% smoke trail stays continuous.
8. **Toughness**: one mid-air contact puts the bar at about 33% (not 50%), two at about 67%, and the third crashes. Smoke and the warning flash appear at 70%: about 21-22 fighter bullets (float rounding at the threshold), or 7 flak. Terrain or buildings still crash at once. A health balloon heals the same amount as before.
9. WebGL (Chrome, Firefox, Safari): the page needs focus before the first key. Clicking the canvas to focus counts as fire and starts the mission. This is expected; confirm it feels fine.
