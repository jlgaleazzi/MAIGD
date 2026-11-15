# B008 (reopened) + B010 + B011 verification (QA task T929)

Scope: the mechanics handoff `docs/crew/handoffs/B010-mechanics.md` (Bomb.cs, Bullet.cs, AAAGun.cs), against
the bugs in `docs/qa/B008-verify.md` § Bugs (P1-1 burst depth, P2-1 AAA under-lead, P2-3 balloons absorb fire).
The Unity editor is open, so this is a compile, static and numeric pass. No tests were run.

## Verdicts

| Bug | Verdict | Notes |
|---|---|---|
| B008 reopened (burst below the ground) | **PASS** | Burst and explosion at (crossing x, HeightAt(x)). Estimated miss rate 0% at 30, 50 and 60 fps from 2, 5 and 9 units, on flat and sloped ground. |
| B010 (AAA lead under-estimates the flight time) | **PASS** | PredictPosition is the exact intercept for both facings. The fallback, the stationary plane and \|v\| = ShellSpeed are all safe. The FlakShell clamp bends shallow shots: see P2-1, which is for the orchestrator to decide. |
| B011 (balloons absorb player bullets and bombs) | **PASS** | Player Bullet and Bomb ignore Team.Neutral and fly on. Pickup, enemy projectiles and obstacles are unchanged. HealthBalloon is the only Neutral IDamageable in play. |

Bugs: 0 P0, 0 P1, 1 P2 (new, a design decision). The earlier P2-1 and P2-3 from B008-verify are closed by B010 and B011.

## Checks run

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes (+1 new file, EnemyBomb.cs), PASS Biplanes.Editor, exit 0. |
| Tests scratch build: a source-built Biplanes.dll, then all 4 `Tests/EditMode/*.cs` against it. Uses the generated `Biplanes.Tests.EditMode.csproj` with ProjectReferences swapped for the ScriptAssemblies DLLs, retargeted to v4.7.2, built in the session scratch dir. | PASS, 0 errors. Nothing was written into the project. |
| Static rules on Bomb.cs, Bullet.cs, AAAGun.cs: no `rb.velocity`, no `UnityEngine.Input`, namespaces match the folders, one public class per file | PASS |
| `grep IsGroundTarget` (removed member) | No references left in Scripts or Tests. |
| Reflected names the tests use: `_pool`, `_directHit`, `_prevPos`, `BurstDamage(Vector2)`, `PredictPosition()`, `_groundY`, `Update`, `FxManager._instance`, `PlayerPlane._velocity` | All present. |
| ARCHITECTURE §3 (lines 145-147 AAAGun, 169-171 Bomb and Bullet) | It already has the requested B008, B010 and B011 text. |
| Mission terrain slopes (all 5 mission JSONs) | 0.008-0.015 maximum. |

## B008: the bomb bursts at the impact point (Bomb.cs:92-126)

- **Placement:** `_prevPos` is set in Spawn (:75) and at the end of every above-ground Update (:112). On reaching the ground, `ImpactPoint` takes the linear zero-crossing of f = y - HeightAt(x) between prev and cur. Both `BurstDamage(p)` and `TriggerExplosion(p)` use p = (x, HeightAt(x)), so the step depth no longer matters.
- **Exactness:** Bomb velocity changes only in Update, so the path between two Updates is a straight line, even when a frame runs 2 physics steps. HeightAt is piecewise-linear. The crossing is therefore exact, except when a terrain keyframe falls inside the segment. With slopes of 0.015 or less and at most 0.2 units of horizontal travel per frame, that error is under 0.003.
- **The first Update is already below ground:**
  - `_prevPos` is the spawn point. If the spawn point is above ground, the crossing is interpolated correctly.
  - If the spawn point is itself below ground (`above <= 0`, for example a drop from a plane sitting on the runway), the point used is (cur.x, HeightAt(cur.x)). That is still on the surface.
  - There is no division by zero: the code guards with `above - below > 1e-6`.
- **Hit geometry:** the AAA box is 1 x 0.6, centred on the surface at the gun's x. The burst centre is on the surface, so on flat ground any crossing within 1.0 of the gun's x registers. On a slope s, the centre sits s·dx off the gun's y. That still registers for any s < 1.6, and missions use 0.015 or less.
- **Miss-rate estimate:**
  - Method: a Python model of the Update and physics ordering. Fixed step 0.02 s, frame jitter ±15%, gravity 117.7, vx in {-5, 0, 5}. The aim point is uniform within the gun sprite (±0.5), and the trigger is checked per step.
  - Results, 2000 drops per cell:

    | fps | h = 2 | h = 5 | h = 9 | slopes tested |
    |---|---|---|---|---|
    | 30 | 0% miss | 0% | 0% (17% via the burst, the rest via the trigger) | 0, 0.015, 0.1, 0.5 |
    | 50 | 0% | 0% | 0% (7% via the burst) | same |
    | 60 | 0% | 0% | 0% | same |

  - Before this fix, the same model gave about 13% misses (50 fps) and about 48% (30 fps) from h = 9.
- **Rules unchanged:**
  - The filter still applies: Team.Enemy only, not IsDead, not `_directHit`, and deduped through `BurstDamaged`.
  - There are no new allocations: `_prevPos` is a struct field, and `ImpactPoint` is static and allocation-free.
  - `_directHit` is still cleared in Spawn.
  - The direct-hit FX (OnHit) still uses the mid-air `transform.position`. That is correct for a hit on a bomber.

## B010: AAA intercept (AAAGun.cs:214-251)

- **Solve:** it solves (v·v − S²)t² + 2(D·v)t + D·D = 0, with D = player − gun and S = 6.
  - **|v| < S** (always true in normal flight, since cruise is 5): a < 0 and c > 0, so there is exactly one positive root, and `hi` is chosen. Correct.
  - **|v| > S:** the roots have the same sign. It takes `lo` if it is positive, then `hi`, then the fallback. Correct.
  - **|v| = S:** the a = 0 branch is taken (|a| < 1e-4) and t = −c/b. It divides only when b < 0, so b = 0 never reaches the division.
  - **No solution or t ≤ 0:** the fallback is t = |D|/S. A NaN/Inf guard is included.
  - **Stationary plane:** t = |D|/S and A = P (aims straight at it).
  - **Player on the gun (D = 0):** t = 0, so the fallback gives 0, A = gun and dist < 0.01. FireBurstRoutine then takes the leftward default, and the clamp makes the shell fly up-left. That is harmless.
- **Numeric check:** a shell simulated at 0.02 s steps, closest approach to the plane:

  | gun (0,0), plane, v | t | closest approach |
  |---|---|---|
  | (−2,6), (5,0) | 1.2035 | 0.018 |
  | (2,6), (−5,0) | 1.2035 | 0.018 |
  | (−6,8), (5,0) | 1.338 | 0.013 |
  | (3,4), (−5,0) | 0.669 | 0.068 |
  | (0,5) and (4,3), stationary | 0.833 | 0.04 |

  All are well inside 0.3. The residue is only the step sampling. In the test case gun (50,0), plane (48,6), v (5,0), t = 1.2035 and x = 54.017 (the handoff says 1.2028 and 54.014, the same within rounding).
- **The FlakShell MinUpward clamp (dir.y ≥ 0.5, so 30° above horizontal) in ordinary play:**
  - Sweep: camera half-width 8.9 + 2, plane 1.5-9.5 above the gun, |v| = 5, both facings, 1760 cases. A hit means within 0.4 and within the 3 s lifetime.

    | case | clamped | meets, with clamp | meets, without clamp |
    |---|---|---|---|
    | approaching, height 4 or more | 0 / 605 | 605 | 605 |
    | approaching, below 4 | 68 / 275 | 238 | 275 |
    | receding, height 4 or more | 582 / 605 | 51 | 108 |
    | receding, below 4 | 271 / 275 | 16 | 79 |

  - So the clamp leaves the aim intact for every approach at normal height. It undoes the aim for low approaches from far out (about 13% of those lose the hit) and for almost every shot at a plane that has already passed the gun. Most of those receding shots could not hit within the 3 s lifetime anyway. See P2-1. That is a design call: it may be desirable for fairness.

## B011: projectiles pass through health balloons (Bullet.cs:62-79, Bomb.cs:131-148)

- **Projectiles:** `Bullet.OnHit` and `Bomb.OnHit` return before damage, FX, `_directHit` and Recycle when `target.Team == Team.Neutral`. The projectile stays active and keeps flying. A null target still recycles. That path is unreachable from OnTriggerEnter2D, which returns early on null.
- **Pickup still works:** `HealthBalloon.OnTriggerEnter2D` collects only when `GetComponentInParent<TeamTag>()` is Player. The bullet and bomb GameObjects (`PlayerPlane.MakeProjectile`) and their pool roots carry no TeamTag. So projectiles can't collect a balloon, and the plane (TeamTag Player) still does.
- **Enemy projectiles are unchanged:** FlakShell.OnHit and EnemyBomb.OnHit both already ignore everything that isn't Team.Player.
- **Static obstacles are unchanged:** StaticObstacle has a Neutral TeamTag but no IDamageable, so `Projectile.OnTriggerEnter2D` returns on the null target, as before.
- **Every Team.Neutral IDamageable:**
  1. `HealthBalloon`: `Team => Team.Neutral` is hard-coded. Its `ApplyDamage` is a no-op, so ignoring it loses nothing.
  2. `Health` with no TeamTag: the Awake fallback makes it Neutral and logs a warning. No production code creates one. PlayerPlane, FighterAI, BomberAI and BarrageBalloon all add the TeamTag, then set the team, then add the Health (PlayerPlane.cs:139-140, FighterAI.cs:223-227, BomberAI.cs:234-238, BarrageBalloon.cs:208-218).
     - The Awake paths of FighterAI and BomberAI (`AddComponent<Health>()` before `_teamTag` is created, :62/:78) run only if Health is missing. Create always adds it first, so they are not reached.
     - If such a Neutral Health ever appeared on an enemy, bullets would now pass through it. Before, they would have damaged it. No path does this today.
  3. Every other IDamageable is AAAGun (Enemy, hard-coded), FighterAI or BarrageBalloon (Enemy), or BomberAI and the PlayerPlane through their Health (Enemy and Player).
  - So nothing that should be hittable stopped being hittable.

## Bugs

```
BUG: FlakShell's 30-degree minimum climb bends the new intercept aim on shallow shots
PRIORITY: P2 polish (design decision; may be accepted as is)
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Weapons/FlakShell.cs:17-18, 39-42 (MinUpward = 0.5)
STEPS: Static/numeric (see § B010 above). The intercept point is below 30 degrees elevation when the plane is low
  (< 4 units) and more than about 2x its height away, or when it has already passed the gun. Spawn then raises
  dir.y to 0.5 and the shell flies above the plane's path: about 25% of low approaching shots are bent (so 13% of
  all low approaching shots lose a hit they would have scored), and about 97% of shots at a receding plane are bent.
EXPECTED: ARCHITECTURE §3 Enemies: AAAGun "fires flak bursts up at the exact straight-line intercept".
ACTUAL: The aim is exact at normal heights when approaching, but overridden for shallow shots. Options: accept it
  (record it in the GDD Change Log as a fairness rule), or lower MinUpward to about 0.25 (about 15 degrees) so
  that only near-horizontal shots are bent.
```

### Known and tracked, not re-filed
- `BomberAI.targetX` has no setter (T047).
- R1: the quit-time `ObjectPool.Dispose` (waiting on a playtest).
- AAA fires within half-width + 2 (accepted).
- The bomb drifts in the post-turn swing (accepted).

## EditMode tests (compiled, not run)

`Biplanes/Assets/Biplanes/Tests/EditMode/EnemyWeaponsAndPoolTests.cs` (18 → 21 tests; the suite is now **64**; Unity still has to generate this file's `.meta`):
- **Rewritten:** `AAAGun_PredictPosition_LeadsWithPlayerVelocity_BothFacings`.
  - It asserts the intercept property: A = P + v·t, with t = |A − gun| / ShellSpeed, within 1e-3.
  - Cases: flying right (x ≈ 54.017), its exact left mirror, and flying left while climbing and moving away.
- **New:** `AAAGun_PredictPosition_StationaryPlane_SpeedEqualsShell_NoSolutionFallback`:
  - A stationary plane gives A = P.
  - \|v\| = ShellSpeed while closing gives the linear branch (A = (58, 6)).
  - \|v\| = ShellSpeed while moving away, and \|v\| = 8 while moving away, give the finite fallback t = |D|/S.
  - \|v\| = 8 while closing gives the earliest positive root, and it is a true intercept.
- **New:** `PlayerBulletAndBomb_OnHitHealthBalloon_FlyOn_BalloonUntouched`:
  - The pooled Bullet and Bomb stay active after OnHit on a HealthBalloon, there is no `_directHit`, and the balloon is not dead and still active.
  - The control: a bullet hit on an enemy damages it and recycles the bullet, and `bomb.Recycle()` deactivates the bomb. This proves the pool is wired, so the "active" assertions are not vacuous.
- **New:** `Bomb_Update_BelowGround_BurstsOnSurfaceAtCrossingX_KillsAaaThere`:
  - Setup: a TerrainStrip with slope 0.1, `_prevPos` 0.6 above the surface at x 109.5, and the bomb 0.9 below the surface at x 110. It runs the real `Update`.
  - The gun at 108.9 dies. It can only be reached from the crossing point (109.7, −3.53): neither the below-ground point nor the surface under it reaches it.
  - The explosion GameObject is at (109.7, HeightAt(109.7)).
  - FxManager is seeded through `_instance`, because its DontDestroyOnLoad needs Play mode. The test resets `_instance`, removes the explosions and clears `TerrainStrip.Instance` in `finally`.
  - The test is Inconclusive if 2D queries don't see EditMode colliders.
- **Header comment:** updated for T929 and the FxManager seeding.
- **Stale-test review:** no test referenced the removed `Bomb.IsGroundTarget`. The B008 burst tests call `BurstDamage(Vector2)` directly with an explicit point, so they are still valid. No other test covers the three changed methods.

## Consolidated manual playtest checklist

Run in the editor (Unity → Biplanes → BuildScenes, then Play) and once in a WebGL build (Chrome, Firefox, Safari).
**Unchanged steps:** run `docs/qa/B008-verify.md` § Consolidated manual playtest checklist by reference: the T924, B001
and B003 lists it points to, plus B2, B3, B6 and B7. The steps below **replace** B1, B4, B5 and B8 there.

- **C1. Mission 3, AAA kill rate (replaces B1):**
  - Bomb a gun 10 times from the top of the screen and 10 times from 2 units up. Expected: 10/10 both ways.
  - Repeat the high drops with `Application.targetFrameRate = 30` and in the WebGL build. Expected: still 10/10.
  - Every explosion is drawn on the ground surface, never inside it.
  - Bomb 1 unit or more beside a gun: it survives.
- **C2. Health balloons (replaces B4, missions 1-2):**
  - Fire through a balloon at an enemy beyond it: the bullets reach the enemy.
  - Bomb through a balloon: the bomb falls on through it and explodes on the ground. The bomb count drops by exactly 1 per drop.
  - The balloon stays until the plane flies into it, then heals by healthBalloonRestore.
  - Nothing else changes: fighters, bombers, barrage balloons and AAA still take hits, and buildings and trees are unaffected by fire.
- **C3. Flak lead (replaces B5):**
  - Fly level, right then left, at mid and high cruise toward a mission 3 AAA: the bursts meet the plane, rather than passing behind it or ahead of it.
  - Fly low (under 4 units) and pass over the gun: note whether the shells now pass above the plane (the P2-1 clamp), and decide whether that is acceptable.
  - On the runway, and while climbing straight up, the aim is sensible (no wild lead).
- **C4. Tests (replaces B8):** Unity Test Runner → EditMode → all 64 tests pass, none Inconclusive. Unity generates the `.meta` for `EnemyWeaponsAndPoolTests.cs` on import.
