# T943 verify: B029 (fixes for QA T942: airplane pitch and nose-aligned bullets)

**Verdict: PASS.** All 9 T942 bugs are fixed. No P0 and no P1. 2 new P2 bugs, plus 1 latent risk that a new test now guards.

Unity is open, so I couldn't run the tests or play the game. Everything below comes from the compile check, the scratch build and reading the code paths.

## 1. Commands

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes (+1 new file not yet in the csproj: `PlanePitch.cs`), PASS Biplanes.Editor, **exit 0** |
| Tests scratch build (B018 method) | Runtime DLL built with compile_check's helpers. The generated `Biplanes.Tests.EditMode.csproj` had its ProjectReferences and Compile items removed, TargetFrameworkVersion set to v4.7.2, ScriptAssemblies DLLs and the scratch runtime DLL referenced, and **all 15** `Tests/EditMode/*.cs` added. Result: runtime PASS, tests PASS, **0 errors**. `zz_check_tests.csproj` was deleted and the build output went to a temp dir, so nothing was left in the project. Script: `t943_build_tests.py` in the session scratchpad |
| `bash tools/unity.sh compile/test` | Not run: the editor has the project open |
| `python tools/validate_missions.py` | M2, M3 and M5 OK. M1 and M4 fail "y must be in [0, 8.5]", which is the known V001 and out of scope. B029 touched no mission |

Static checks:
- `PlanePitch` is in `Biplanes.Core`, under Scripts/Core, and is a single public static class.
- No `rb.velocity` and no `UnityEngine.Input`.
- grep finds no `transform.rotation =` or `eulerAngles =` writes left in Player/, FighterAI or BomberAI.
- Every API exists (the compile proves it).
- No hand-written YAML.
- `PlanePitch.cs` and `Tests/EditMode/PlanePitchTests.cs` have no `.meta` yet. Unity creates them on the next refresh (focus the editor). Don't hand-write them.

## 2. The 9 T942 bugs

| T942 bug | Status | Evidence |
|---|---|---|
| P0-1 Facing left, the gun fires backwards from the tail | **Fixed** | `PlanePitch.cs:89-92`: NoseDirection = (facing·cos p, sin p). `PlayerPlane.cs:495-498`: the muzzle is pos + nose·0.7, the rotation is atan2(nose), and the direction is the nose. Facing left and level, the offset is (−0.7, 0) and the velocity is −x, the same as T924/B002 |
| P1-1 The fighter's nose is mirrored relative to its shot | **Fixed** | `FighterAI.cs:266-274`: the aim pitch is `PlanePitch.PitchToward(target-from)` (`PlanePitch.cs:98-102`, atan2(dy,\|dx\|)), so it's + for a player above. `FighterAI.cs:79`: BodyAngle = −pitch with flipX, so the drawn nose goes up |
| P1-2 Shots up to 40° leave off a nose clamped at 15° | **Fixed** | `FighterAI.cs:250-262`: the aim pitch limit is `pitch.fighterMaxDegrees` 40 (`Balance.json:67`), which equals `fighterConeDegrees` 40 (`Balance.json:54`). `FighterAI.cs:305-310`: it fires only when Angle(nose, toPlayer) ≤ `aimToleranceDegrees` 10 (`Balance.json:63`), and the shot is the nose ± spread. AimWithinCone with forward = the nose and a cone of 180 just applies the spread |
| P2-1 Ease is too slow (15°/s) | **Fixed** | `PlanePitch.cs:39-67`: the target is vy/fullPitchSpeed·max and the rate is `easeDegreesPerSecond` 60. Full pitch takes 0.25 s and a full reversal 0.5 s |
| P2-2 Bomber pitch sign | **Fixed** | `BomberAI.cs:100,107`: Facing −1, BodyAngle = −pitch. `:227-228` uses Step plus MoveRotation |
| P2-3 transform.rotation writes on the RB root | **Fixed** | `PlayerPlane.cs:481`, `FighterAI.cs:239` and `BomberAI.cs:228` all use `_rb.MoveRotation(BodyAngle)`. The public PitchAngle, BodyAngle and NoseDirection are on all 3 planes, and the tests read them |
| P2-4 Fallback rules differ | **Fixed** | One resolver, `GameBalance.cs:23-38` `PitchData.Resolve` (`>0` or `PlanePitch.Default*`, capped at 89). It's used at `PlayerPlane.cs:196,266,479`, `FighterAI.cs:135,251` and `BomberAI.cs:158,226` |
| P2-5 The pitched hitbox raises the ground threshold | **Fixed (accepted)** | `PlayerPlane.cs:308-309,519-531` `HitboxLowestPoint()` uses the exact rotated box (`PlanePitch.cs:108-115`) and BodyAngle, so it doesn't lag MoveRotation. The ~0.19 drop at 15° is documented (ARCHITECTURE:175) and tested |
| P2-6 ARCHITECTURE is missing pitch | **Fixed, mostly** | ARCHITECTURE.md:150 and :174-175 now describe pitch, nose fire and `Bullet.Spawn(…, Vector2)`. Two stale spots remain: see new P2-1 below |

T942's "also noted" items are fixed too:
- The fighter no longer snaps on a shot: UpdatePitch eases every tick and TickFire never writes the pitch.
- The nose no longer turns when the pool is exhausted.
- `FighterAI.NoseDirection` is now the live drawn nose.

## 3. The 5 previously failing PlanePitchTests and PlayerFacingTests (by code path)

| Test | Expected | Why |
|---|---|---|
| Player_NoseDirection_MatchesDrawnNose_BothFacings | pass | Facing left at +15: BodyAngle −15. Euler(−15)·(−1,0) = (−cos15, sin15), which equals NoseDirection |
| Player_FacingLeft_Climbing_BulletLeavesAlongTheNose | pass | v = nose·speed (Projectile speed > 0 even without Awake), so v.x<0 and v.y>0. The muzzle is at −0.7·cos15, left of the centre |
| Fighter_ShotUpward_NoseAgreesWithBullet | pass | Player 9.46° up-left. 11 ticks × 1.2° settles the nose on 9.46°. TickFire(1.21): cooldown ≤ 0, in the cone, angle 0 ≤ 10, so it fires along the nose ± 4. The drawn nose y > 0 |
| Fighter_ShotAtConeEdge_StillLeavesAlongTheNose | pass | 30.26° bearing. 36 ticks gives a 43.2° swing, so it settles (limit 40). The muzzle is pos + nose·0.75, so the angle is 0 |
| Bomber_Climbing_DrawnNoseUp | pass | The escort lerp gives vy = 1.35, so the target is 6.75°. Facing −1 gives BodyAngle −6.75, and with flipX the nose is (−cos, +sin). The gunner returns early (no player). There's no targetX, so it drops no bombs |
| PlayerFacingTests.OnFire_FacingLeft_SpawnsAtMirroredGunAndFliesLeft | pass | It's level after the turn tick (vy=0), so the muzzle is x −0.7 and v.x<0 |
| PlayerFacingTests.OnFire_FacingLeftOrRight_WhileClimbing_BulletLeavesTheNose (new) | pass | sign(v.x) = sign(dx) = facing in both facings |

Regression:
- **EnemyFireTests** stay consistent:
  - Fighter at (6,1) vs player (0,0): 9.46° off level, so it's within the 10° tolerance even before tracking.
  - The cadence tests only call TickFire, so the nose doesn't move.
  - Fighter_HoldsFire_UntilItsNoseIsOnThePlayer: 30° is more than 10, so no shot. After 100 tracking ticks it fires at once.
  - Fighter_Shot_FliesAlongTheNose: level nose, player 8° off, 20 shots within ±4 of the nose. The pool holds 32.
- **GameFlowTests** (FlakTo100, the ram cases): the plane is at y=1, far above the ground. The crash path is unchanged apart from `_crashAngle = BodyAngle`.
- **PlaneCollisionTests**:
  - Terrain contact at GroundBaseY: HitboxLowestPoint = −4.9 ≤ −4.5, so it crashes. MakePlane sets `rb.position`, so `_rb.position` is valid.
  - The grace window and bomber multi-hitbox paths are untouched.
- **PlayerInputGateTests**: no rotation, bounds or OnMove-pitch dependency (grep).
- **ZeppelinAndBaseTests**: the line-of-fire test is geometry-only. The level-flight player bullet is still horizontal (`Player_LevelFlight_FiresHorizontally_OnceEased`).

**New test (QA):** `PlanePitchTests.Player_RunwayTakeoff_FirstPitchedTick_DoesNotTouchTheGround` (PlanePitchTests.cs:477). See §4.

## 4. MoveRotation on the kinematic body, and its interactions

- **Timing:** `MovePosition` and `MoveRotation` both queue a target that the next physics step applies, after all FixedUpdates. They're independent, so calling both in one tick is fine. Interpolation is off (the default), so there's no jitter source. The playtest still covers it (P-3).
- **Crash fall (T937):** no conflict.
  - Once crashed, OnMove is unsubscribed and PlayerInput is disabled. `UpdateCrashFall` is the only rotation writer (`PlayerPlane.cs:653`).
  - `_crashAngle` starts at BodyAngle (`:619`), which is the same value the last OnMove queued.
  - If the crash comes from OnTriggerEnter2D (inside the physics step), that step already applied the queued pose. Either order is consistent.
- **B025 grace:** `TickPlaneCollisionGrace` is unchanged and only the collider orientation changes. Rams still enter through OnTriggerEnter2D.
- **Smoke:** it's a sprite tint (`_smokeTrailRoot` is never assigned), so rotation can't misplace it.
- **Screen clamp:** it clamps the centre. Pinned at the top or bottom, vy = 0, so the nose levels in 0.25 s. The rotated box can poke up to 0.09 past the 0.5 margin for that moment. That's cosmetic.
- **Ground check:** `HitboxLowestPoint` = the old `_rb.position` (MovePosition pending) and the new BodyAngle. That's conservative by one tick.
- **Takeoff margin (latent risk, guarded by the new test):** MissionRunner rests a runway plane `RunwayClearance` 0.02 above the ground (`MissionRunner.cs:84,692`).
  - On the takeoff tick the nose pitches 60·0.02 = 1.2°, and the 1.6×0.8 box's tail corner drops by **0.0167**. The body hasn't risen yet in the worst order (PlayerInput's OnMove before PlayerPlane.FixedUpdate).
  - So the margin is 0.0033: no crash today.
  - `pitch.easeDegreesPerSecond` above **≈72** would make every M1 runway takeoff an instant ground crash.
  - The new test reproduces the worst case (EditMode never applies MovePosition) and fails if anyone raises the rate or lowers the clearance.
  - Not filed as a bug, because the current values are correct.
- **Hitboxes:** they rotate with the body (the colliders are on the RB's GameObject). Enemy bullets and flak are kinematic trigger projectiles with `useFullKinematicContacts` (Projectile.Init), and the player body has it too. Overlaps are reported at any rotation, so a pitched player is still hit.

## 5. Fighter fire

- **Nose eases toward the player only in the cone and range:**
  - `TryGetAimPitch` (`FighterAI.cs:266-274`) requires a live, uncrashed player and `InFiringCone(left, 40°, range 11)`.
  - Outside that, the nose follows climb or dive (`:256-260`), easing down from up to 40° at 60°/s.
  - The test `Fighter_AimPitch_ClampedToFighterMax_AndEasedBackToFlightLimit` covers both.
- **Fires only within 10° of the nose:** `FighterAI.cs:305-307`. The shot is the nose ± 4° and leaves from the nose (`:310-313`).
- **Mission 1 fire arithmetic:** 3 sine fighters and 1 straight, all closing at about 5 u/s relative.
  - The cooldown starts on screen entry. The fighter is in range about 0.3 s later.
  - First-shot delay 0.4–1.2 s, then 1.8–2.5 s between shots, so there's 1 shot per pass, or 2 if the player hangs back.
  - Worst case: 4–8 shots, 32–64% if every shot hits. That's unchanged from B023, and actual hits are fewer (see P2-2).
  - The health balloon gives +30. M1 stays survivable.
- **Fairness at the 40° cap:**
  - Steep shots were already possible under the B023 cone. Now they're telegraphed by a visibly raised or lowered nose, and they fly at 8 u/s against the player's 5 u/s, so they can be dodged. That's fairer than before.
  - At close range (d < ~9) the bearing can change faster than 60°/s when both planes move vertically in opposite directions (player 5 plus sine up to 4 u/s). The fighter loses lock and holds fire, so dodging works.
- **Sine and dive fighters:** while the player is in the cone, the nose tracks the player even when the fighter is climbing or sinking along its pattern, so a sinking fighter can show its nose up. This matches GDD "a fighter swings its nose onto you". Outside the cone, the nose follows the motion again. It's a visual call for playtest E-1, not a bug.

## 6. Mission 5 zeppelin turrets with ease 60°/s and fullPitchSpeed 3

- ZeppelinAI and ZeppelinTurret haven't changed since T942 (last modified 11-13), so the T942 §3 clear-band table (a function of the shot angle θ) still holds.
- At level flight the bands are: underside 0.31 (both facings), top fore 0.37 (from the left) and top aft 0.35 (from the right). All are ≥ the 0.25 B021 acceptance.
- Player vy is 0 or ±5. 5/3·15 saturates, so pitch is effectively 0 or ±15, with 0.25 s transitions.
- **Firing while climbing at the underside turret:** θ = +15, so the band widens to 0.58.
- **Firing while diving onto a top turret:** θ = −15, so the band widens to 0.55.
- **Dive-and-fire into the underside band:** the gondolas still absorb shots below −5°, but the nose now levels in **0.25 s** (T942: ~1 s). The player hovers in the band during that time, since vy = 0 once S is released.
- P0-1 is fixed, so the right-side approach (facing left) uses the facing-left column: underside 0.31 at 0°, top aft 0.35.
- **Shield:** all 3 turrets are killable from a level hover in a band ≥ 0.31, so the shield drops. Then the hull takes bullets and bombs, and the **win is feasible.**

## 7. Bugs

```
BUG: P2-1 ARCHITECTURE still describes the old fighter aim, and §4 lacks the new Balance keys
PRIORITY: P2 polish
OWNER: orchestrator
WHERE: docs/ARCHITECTURE.md:144 (FighterAI "shoots EnemyBullets at the player's current position ... the shot is clamped into that cone"); docs/ARCHITECTURE.md:330 (§4 enemyFire list has no aimToleranceDegrees; §4 has no pitch block)
STEPS: Static. Line 144 contradicts line 175 (fire along the nose, only within aimToleranceDegrees). grep "fullPitchSpeed" finds only line 175.
EXPECTED: Rule 4 (never invent APIs): the contract lists the live behaviour and the Balance schema (B029 REQUESTS, §4 item).
ACTUAL: Stale text at 144. §4 lacks `enemyFire.aimToleranceDegrees 10` and `pitch {maxDegrees 15, fighterMaxDegrees 40, easeDegreesPerSecond 60, fullPitchSpeed 3}`.
```

```
BUG: P2-2 A fighter fires on the first tick its nose enters the 10 degree tolerance, so shots taken while it is still acquiring are aimed up to ~14 degrees off the player
PRIORITY: P2 polish (balance; design call: orchestrator)
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/FighterAI.cs:305-310
STEPS: Static. Cooldown expires while the nose is still swinging (first shot: the cooldown runs from screen entry, tracking only from cone+range entry, so 0.1-0.9 s of tracking at 60 deg/s = 6-54 deg of swing). E.g. bearing 30 deg, nose from 0: Angle first drops to <=10 at pitch ~20, so the shot leaves 10 deg +-4 off. At 7 units that is ~0.7-1.7 units off; a hit needs <= 0.48 (half the 0.8 box plus the 0.08 bullet radius), so it always misses. When the nose has already settled (the usual case for later shots) the aim is exact.
EXPECTED: GDD Flight attitude: "fires only when you are close to where it points". That is met. ARCHITECTURE/B023 intent: fighter shots are a real threat.
ACTUAL: A share of the first shots are guaranteed misses (they still telegraph). Fighters are somewhat weaker than in B023. Acceptable. If playtests find M1/M3 fighters toothless, fire when Angle <= tolerance AND (the nose has reached the aim pitch, or Angle <= ~3 deg), or lower aimToleranceDegrees.
```

Out of scope (known): B015, B019, V001 (M1/M4 validator), B027, B028. Not re-reported.

## 8. Final manual playtest checklist: pitch and nose fire

Do this after Unity refreshes and creates the `.meta` files for `PlanePitch.cs` and `PlanePitchTests.cs`. Then run Test Runner → EditMode → all, and expect every test in PlanePitchTests (31), EnemyFireTests, PlayerFacingTests, PlaneCollisionTests and GameFlowTests to be green. Then Biplanes → BuildScenes → Play.

Player:
- [ ] P-1. **M1 takeoff:** press W, and separately D alone. The plane leaves the runway with no ground crash. The nose rises during the climb-out.
- [ ] P-2. Hold W: the nose reaches about 15° in about ¼ s. Release it and the nose levels in about ¼ s. Hold S and the nose dips. Do this facing right **and facing left**. Facing left, a climb points the drawn nose up.
- [ ] P-3. Fire while climbing, level and diving, in both facings. Tracers leave the drawn nose tip and fly along it. **Facing left, they fly left.** Level shots are horizontal.
- [ ] P-4. Climb and dive hard for 10 s. Motion and rotation are smooth, with no jitter or stutter (MoveRotation).
- [ ] P-5. Skim the ground pitched up and pitched down. Note the art-to-terrain gap at the crash (accepted, up to about 0.19).
- [ ] P-6. Die at 100% while pitched. The wreck tumbles from its current angle, falls, and explodes once. Retry and Next start level.
- [ ] P-7. Ram a fighter while pitched: 50%, 0.5 s grace, and the fighter dies.
- [ ] P-8. Pause while pitched, then resume. Nothing jumps.
- [ ] P-9. Take fighter bullets and flak while pitched 15°. Hits still register.

Fighters and bomber:
- [ ] E-1. Outside the cone, sine and dive fighters (M1, M3) nose up when rising and down when falling. In the cone, they swing their nose onto you even while sinking or rising. Judge whether it reads well.
- [ ] E-2. Fly 2–3 units above, then below, an incoming fighter. Its nose turns **toward** you, smoothly (no snap), up to about 40°.
- [ ] E-3. Tracers always leave the fighter's nose tip along the nose (±4°). It holds fire until its nose is on you.
- [ ] E-4. Dodge vertically at close range. The fighter can't keep its nose on you and holds fire.
- [ ] E-5. Fairness: M1 and M3 fighter damage feels threatening but survivable. Count the first-pass shots that obviously miss while the nose is still turning (P2-2).
- [ ] E-6. The M2 bomber flies level. Its gunner fires from the centre in any direction (the documented exception). Its bombs drop as before.

Mission 5:
- [ ] M5-a. Facing right, hover level in the underside band (world y about −0.2 to −0.4). The underside turret dies.
- [ ] M5-b. Climb up into it while firing. The rising shots reach it.
- [ ] M5-c. Dive into the band, release S, and fire. Shots reach the turret after about ¼ s of levelling.
- [ ] M5-d. Dive onto a top turret while firing. The shots reach it.
- [ ] M5-e. The same from the right side, **facing left**.
- [ ] M5-f. Kill all 3 turrets, then the hull. The win screen shows once.

Builds:
- [ ] WebGL in Chrome, Firefox and Safari: pitch is smooth and nose fire behaves as above.
