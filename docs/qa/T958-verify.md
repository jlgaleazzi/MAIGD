# T958 verify: B040 (pressure and escape fixes, fighterBullet 5) and V007 (duplicate balloons)

Scope: re-verify the fixes for QA T957 BUG 1-12 (B040-mechanics, V007-mission-design).
Date: 2026-11-18. Unity has the project open, so the tests were compile-checked, not run.

## Verdict: FAIL (1 P1, 7 P2; 0 P0)

The code fixes are correct. BUG 1, 2, 3, 5, 6, 7 and 9 are fixed, and BUG 11 (docs) is fixed too.
The FAIL is the balance: with fighterBullet 5, missions 1-3 are survivable on paper, but
mission 4 is just over budget and mission 5 is well over (BUG 4 carried, now P1 for M4/M5 only).
One new P2: fighters that can never come back stay off screen and hold the reinforcement
cap (N1). One B040 test asserted flipX without Awake; I fixed it.

## Commands

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes (+2 new files not yet in the csproj), PASS Biplanes.Editor, exit 0 |
| Tests scratch build (B018 method: runtime DLL from compile_check helpers, temp copy of Biplanes.Tests.EditMode.csproj retargeted v4.7.1 -> v4.7.2, all 28 `Tests/EditMode/*.cs` listed) | **0 errors**. Temp csproj files removed, no `zz_*` left in the project |
| `bash tools/unity.sh test` | "Unity not available" (editor open). Tests not run |
| `python tools/validate_missions.py` | OK for all 5 missions |
| `python tools/style_loop audit` | 46 artifacts, 0 violations |
| Static rules | No `rb.velocity`, no `UnityEngine.Input`. EnemyPressure.cs: one public class (the enum is nested), namespace Biplanes.Missions. No hand-written YAML or .meta |

## Tests fixed and added (Tests/EditMode)

- **EnemyFireTests.cs**
  - `EnemyBullet_FighterShotDeals8Percent_BomberShotDeals10Percent` is now
    `EnemyBullet_FighterShotDealsBalanceFighterBullet_BomberShotDealsBalanceBomberGunner`. It
    asserts Balance fighterBullet is 5 and bomberGunner is 10, then checks the hits against
    `GameBalance.Get().damageTaken` (5, then 15).
  - New tests for right-flying fighters (B040), each with Awake run and `SetFlightDirection(+1)` before Start:
    - `RightFighter_Start_KeepsSpriteUnmirrored_NoseAndBodyPointRight`: invokes Start, checks flipX stays false (no tail-first sprite) and the nose points +x.
    - `RightFighter_PlayerAhead_FiresRight_FromTheNose`: 10 shots. Each has velocity x > 0 within the spread of +x, spawns ahead of the centre, and the tracer rotation is about 0 degrees.
    - `RightFighter_PlayerBehindOnTheLeft_HoldsFire`: no backwards shooting.
    - `RightFighter_PitchesNoseUpTowardAPlayerAbove`: pitch > 0, BodyAngle > 0 (unmirrored), the nose is within tolerance of the player, and the shot climbs to the right.
- **EnemyPressureFixTests.cs**
  - **Fixed** `Fighter_FlyingRight_NoseAndVelocityPointRight`. EditMode doesn't run Awake, so
    `_sr` was null and `SetFlightDirection` never touched the sprite. The last assert,
    `flipX == true` after `SetFlightDirection(-5)`, would have failed, and the `flipX == false`
    assert passed vacuously. The test now invokes Awake first.
  - New `ReEnteredFighter_KilledOnce_RaisesOneEntityDestroyed_SilentRemovalRaisesNone`: damage
    carries across two re-entries, the 3rd hit kills it, and there is exactly one
    EntityDestroyed("fighter"), even after an extra hit. A destroyed (removed) fighter raises none.
  - New `Escort_WithLiveTarget_IsExempt_FallsBackToItsFlightWhenTheTargetIsGone`: IsEscorting
    with WorldVelocityX 0. Once the target is destroyed: not escorting, and WorldVelocityX < 0.
  - New `ReEntry_NeverLoopsOffScreen_InMission1`: for both facings and any camera velocity
    -2..2, a fresh re-entry closes on the view at 1 u/s or more and is not escaped.
- Grep for other stale tests:
  - No other `8f` or 8% fighter-shot assumption. EnemyWeaponsAndPoolTests:601 reads Balance.
  - No "fighters never despawn" assumption. ObjectiveTrackerTests:626 is about the zeppelin.
  - Left-only fighter assumptions are all still valid for default (JSON) fighters:
    PlanePitchTests:166-172 sets flipX for a left flier, and the EnemyFireTests cone tests use `Vector2.left` explicitly.

## Fix-by-fix review

| T957 bug | Evidence | Status |
|---|---|---|
| BUG 1: cast exception | `EnemyPressure.IsLiveAircraft` (EnemyPressure.cs:201-206): an `is IDamageable` pattern, else `GetComponent<Health>()`. No cast left. `AnyEnemyAircraftInView` uses it. `CountLiveFightersNear` and `HandleEscapedFighters` use `is FighterAI`. Tests: IsLiveAircraft_* and WhiteOnlyAndPressureQaTests.Pressure_InViewScan_* | fixed |
| BUG 3: facing side | MissionRunner.cs:395-411. EntryX goes past the edge of `PlayerFacing` (ScrollController.Direction), and `SetFlightDirection(EntryFlightDirection)` = -facing. FighterAI: `Forward`, `BodyAngle`, `NoseDirection` and the movement in every pattern use `_direction`. flipX is set in Start and in SetFlightDirection; SetFlightDirection is called after Spawn (Awake has cached `_sr`) and before Start, which re-applies `flipX = _direction < 0`. EnemyBullet.Spawn rotates by atan2(dir), and the dot-gap-dot sprite is symmetric. New tests cover sprite, nose, cone, pitch and shot direction | fixed |
| BUG 2/6: escapes | `IsEscaped` in the camera frame, then `DecideEscape`. JSON fighters re-enter only while `OpenDestroyAllCounts(name)` (unevaluated destroyAllOfType, case-insensitive; the same ObjectiveEntry objects ObjectiveTracker marks). Otherwise `Destroy` (Health raises EntityDestroyed only in ApplyDamage, so there's no score or credit). Re-entry keeps the same object (name "fighter", the same Health), so there's no double count and no lost kill. Dead fighters are skipped. Escorts with a live target are skipped (`IsEscorting`, Unity-null aware). Only M1 has a fighter destroy-all, so in M2-M5 escaped fighters are removed | fixed (see N1) |
| Re-entry loop | Entry offset is at most 1.25 u, less than EscapeMargin 3, and the fighter flies toward the player. M1: fighter 3 vs camera at most 2, so the closing speed is at least 1 u/s in every facing, even mid-turn (Direction flips at once, the camera eases). It crosses the screen every cycle, so it is never held off screen. Test added | OK in M1 |
| BUG 5: fallback | FighterAI.cs:68 `FallbackHitPoints = 75f`. The doc (lines 19-20) says three bullets | fixed (stale "(left)" at line 24, N2) |
| BUG 7: arrival | Plane 0 is at most 0.25 + 0.25 = 0.5 u off screen. At 3 u/s (camera pinned) that is 0.17 s, about 2.17 s + 1 frame worst case. ARCHITECTURE §3 says "about 2.2 s" and the EnemyPressure doc says "about 0.2 s". Test Entry_FirstPlane_* | fixed |
| BUG 9: duplicate balloons | Health balloons: M1 170. M2 65, 135. M3 180, 280, 380. M4 230, 380. M5 200, 255, 320. No duplicates | fixed |
| BUG 11: docs | No "pencil outline" or "dashed pair" left in GDD.md or UI_SPEC.md | fixed |
| BUG 4: balance | See below | **still open for M4 and M5 (P1)** |
| BUG 8, 10, 12 | Deferred / process. PlayerPlane.cs:300 still builds the 6x2 placeholder. The Color.white 1x1 fallbacks are unchanged. There's still no .meta for player_bomb.png, EnemyPressure.cs, BombBallistics.cs or the new tests | open P2 |

## Regressions

- **Mission 1, 37 fighters:**
  - Escapees come back, reinforcements don't count, and the objective needs 37 kills of "fighter".
  - The last wave is seen around camera x 265-275, which leaves about 12-17 s at 2 u/s before the map-end loss at 300. Winnable.
  - A fighter that escapes late still has to be killed before x 300 (design note in B040, no HUD hint).
- **Mission 2:**
  - bomber_1 is at atX 80 with failAtX 10, and the objective is unchanged.
  - The 3 escorts (`escortOf` bomber_1) are exempt while it lives. Once it dies they fly straight left and are removed when they escape.
  - The bomber now counts as in view without an exception.
- **Mission 5:**
  - The zeppelin escorts follow airship_01 and are exempt while it lives.
  - The JSON zeppelin, HQ and failAtX are unchanged (validate_missions OK).
- **EnemyPressure.Update gating is unchanged:**
  - The timer holds when not Playing.
  - `IsEligible` (runway, crash, `ObjectivesOpen`) resets it.
  - It never spawns on the runway, while paused, or after the objectives are done.
  - The escape pass runs in every state, but it only re-enters or removes, never spawns. After the objectives are done, `OpenDestroyAllCounts` is false, so escapees are removed.

## Balance on paper with fighterBullet 5

Same model as T957: about 1.3 shots per uncontested fighter, hit chance p = 0.25 (dodging)
or 0.4 (dueling), AAA about 18% each while passed, and turrets estimated at about 12% each.
The budget is 100 + 30 per health balloon (after V007).

| Mission | Fighters | Fighter dmg p=.25 / .4 | Other (est.) | Total p=.25 / .4 | Budget | Verdict |
|---|---|---|---|---|---|---|
| M1 | 37 | 60 / 96 | 3 AAA optional, 0 | 60 / 96 | 130 (1 balloon) | survivable |
| M2 | 29 | 47 / 75 | gunner about 40 | 87 / 115 | 160 (2) | survivable |
| M3 | 46 | 75 / 120 | 5 AAA about 90 | 165 / 210 | 190 (3) | survivable if dodging, marginal |
| M4 | 48 | 78 / 125 | 5 AAA about 90 | **168** / 215 | 160 (2) | **over budget even when dodging (P1)**. Fine (123) only if the AAA is bombed early |
| M5 | 67 | 109 / 174 | 7 AAA about 126, 3 turrets about 36 | **271** / 336 | 190 (3) | **unsurvivable on paper (P1)**. Still 208 with half the AAA exposure |

Not counted: re-entries in M1 (about +1.6% per extra pass) and reinforcements (only when the
sky is empty). These are estimates, not playtest results.

## Bugs

```
BUG 4 (carried from T957, narrowed): Missions 4 and 5 still over the damage budget on paper with fighterBullet 5
PRIORITY: P1 mechanic wrong vs GDD (balance; confirm in playtest)
OWNER: mission-design (health balloons / AAA count) or mechanics (Balance flak)
WHERE: mission_04.json (2 health balloons: 230, 380), mission_05.json (3: 200, 255, 320); Balance damageTaken.flak 15
STEPS: See the balance table. M4 expects 168% at p=.25 against a budget of 160%. M5 expects 271% against 190% (208% even if half the AAA is bombed before it fires).
EXPECTED: GDD §2 "each harder than the one before": hard but survivable for a careful player.
ACTUAL: M1-M3 are now fine. M4 is just over, and M5 is clearly over.
LEVERS: M4 +1 health balloon (budget 190). M5 +2 health balloons (250), or flak 15 -> 12 plus 1 balloon. Or fewer AAA guns on M5's path before the HQ.
```

```
BUG N1: Fighters that can never return linger off screen and hold the reinforcement cap
PRIORITY: P2 polish (could break the 2 s rule; confirm in playtest)
OWNER: mechanics
WHERE: EnemyPressure.cs:294-298 (IsEscaped needs |x beyond edge| > margin) and :146-155 (CountLiveFightersNear counts every fighter within one screen width)
STEPS: Static.
  (a) M3/M4: scroll 3 equals fighter speed 3. A reinforcement flies past the player and leaves
      by the trailing edge, and the player turns to chase it. Relative speed becomes 0, so the
      fighter sits forever 0-20 u beyond the edge it is heading for (the reinforcement margin
      is 20 u; 0-3 u for a JSON fighter). It is invisible, can't be caught, never fires, and is
      never escaped, until the player turns the other way or the camera pins at the map end.
  (b) M1/M2/M5: the same escape drifts away at only 0.5-1 u/s, so a reinforcement stays off
      screen for 20-40 s before it is removed.
  In both cases CountLiveFightersNear counts these fighters, so a few of them (4 or more with
  cap 6 and wave 2) trim or block reinforcement waves while the sky is empty.
EXPECTED: GDD "Enemy pressure": an escaped fighter "leaves the battle for good", and the sky is never empty for more than 2 s.
ACTUAL: Ghost fighters that count toward maxLiveFighters.
FIX HINT: count only fighters in view or closing on it in CountLiveFightersNear, and/or treat a fighter fully off screen with |relVelX| <= epsilon as escaped at once (it can never come back).
```

```
BUG N2: FighterAI class doc still says the gun cone is "(left)"
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/FighterAI.cs:24
STEPS: Static. "...within enemyFire.range and fighterConeDegrees of its flight direction (left)".
EXPECTED: B040: the flight direction can be either way.
ACTUAL: Stale comment. Drop "(left)".
```

Carried over, still open (deliberately deferred):

```
BUG 8 (T957): Dead placeholder: PlayerPlane builds a 6x2 bullet sprite that Bullet.Awake replaces
PRIORITY: P2 polish
OWNER: mechanics
WHERE: PlayerPlane.cs:300
STATUS: open, deferred by the orchestrator.
```

```
BUG 10 (T957): Leftover Color.white 1x1 fallbacks (pure #FFFFFF, not ink #E8F1FA)
PRIORITY: P2 polish
OWNER: mechanics
WHERE: AAAGun, BomberAI, EnemyBase, FighterAI:455, PlayerHQ, StaticObstacle, ZeppelinAI, ZeppelinTurret, PlayerPlane
STATUS: open, deferred by the orchestrator.
```

```
BUG 12 (T957): New assets and scripts not imported yet (no .meta)
PRIORITY: P2 polish (process)
OWNER: user (Unity focus)
WHERE: player_bomb.png, EnemyPressure.cs, BombBallistics.cs, Tests/EditMode/{AAAGunBulletTests, BombBallisticsTests, EnemyPressureTests, EnemyPressureFixTests, WhiteOnlyAndPressureQaTests}.cs
STATUS: open. Focus Unity once, then commit the generated .meta files.
```

Count: P0 0, P1 1 (BUG 4), P2 5 (N1, N2, 8, 10, 12).

## Playtest checklist (user: Unity -> Biplanes -> BuildScenes, then Play)

Enemy pressure and escapes:
- [ ] M1: after takeoff, there are never more than about 2.2 s without an enemy on screen. No waves on the runway, while paused, or after the win.
- [ ] M1: let a fighter pass. Shortly after it leaves the left edge it re-enters from the right edge, flying at you, with its damage kept (it dies on the remaining hits). The objective count still needs it, and killing it adds exactly 1.
- [ ] M1: face left for a while. Escapees and reinforcements enter from the LEFT edge, flying right: the sprite faces right (no tail-first plane), it pitches toward you, and it shoots toward you, never backwards.
- [ ] M1: finish with all 37 before x 300, including at least one re-entered fighter.
- [ ] M2: no InvalidCastException in the Console while the bomber lives. The escorts stay with the bomber. After the bomber dies, the escorts fly off and vanish. Losing at failAtX 10 still works.
- [ ] M3: face left for 5 s. Two fighters enter from the left within about 2.2 s, and there is no burst of 4-6 when you turn right.
- [ ] M3/M4 (N1): let a reinforcement pass you, then turn to chase it. Does it vanish for good? Over the next 10-20 s, does the sky still refill every 2 s?
- [ ] M5: the zeppelin escorts stay with it. Fighters that get past you vanish (no score).
- [ ] A fighter shot costs 5% (HUD), and a bomber gunner shot 10%.

Balance (BUG 4):
- [ ] Note your damage at each health balloon and at the end of M3, M4 and M5. Does M4 finish under 100% while dodging? Does M5?
- [ ] M2 has exactly 2 health balloons (65, 135), and M3 has exactly 3 (180, 280, 380), with no stacked double balloon.

Carried from T957 (unchanged):
- [ ] A fighter dies on the 3rd hit. An AAA gun dies to 5 machine-gun hits or 1 bomb.
- [ ] Bomb arc level, left, diving and climbing. The bomb sprite shows after the import (BUG 12).
- [ ] White-only readability: no colour anywhere. The 3x1 bullets are visible at 1080p and in a small WebGL canvas. The damage bar hatches and flashes at 70% or more. Spent bomb icons are fainter.
- [ ] Pause/menu works. WebGL in Chrome, Firefox and Safari: bullets are still visible after canvas scaling.
