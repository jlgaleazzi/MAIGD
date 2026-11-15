# B007 + B008 + B009 verification (QA task T928)

Scope: the mechanics handoff `docs/crew/handoffs/B008-mechanics.md`, which fixes the four bugs
filed in `docs/qa/B003-verify.md` § Bugs plus the tracked B007. The Unity editor is open, so
this is a compile and static pass. No tests were run.

## Verdicts

| Bug | Verdict | Notes |
|---|---|---|
| B007 (player bomb hit uses bomberGunner) | **PASS** | Only `EnemyBomb` reaches the player with `DamageSource.Bomb`. No other path changed. |
| B008 (bombs tunnel through the AAA trigger) | **FAIL (partial)** | The burst works as specified, but it is centred on the bomb's below-ground position. Drops from high up still miss: about 13% at 50 fps, and up to about 48% at 30 fps (WebGL). See P1-1. |
| B009a (AAA kill scores twice) | **PASS** | One `score.aaa` (150) per kill. |
| B009b (AAA lead always zero) | **PASS** | `PlayerPlane.Velocity`: the sign and units are right for both facings. The lead under-estimates the flight time (P2-1). |
| B009c (stale flak and enemy-bomb pools not disposed) | **PASS** | Disposes before the rebuild. It doesn't touch the new root and is safe on already-destroyed items and roots. |

Bugs: 0 P0, 1 P1, 2 P2.

## Checks run

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes (+1 new file, EnemyBomb.cs), PASS Biplanes.Editor, exit 0. Re-run after the test edits: same result. |
| Tests scratch build: a source-built Biplanes.dll, then all 4 `Tests/EditMode/*.cs` against it. Uses the generated `Biplanes.Tests.EditMode.csproj`, with ProjectReferences replaced by the ScriptAssemblies DLLs, retargeted to v4.7.2. The temporary csproj was deleted and the output went to %TEMP%. | PASS, 0 errors. Nothing left in the project: no `zz_*` files, and `Biplanes/obj` is untouched and gitignored. |
| Static rules on the 6 changed files: no `rb.velocity`, no `UnityEngine.Input`, namespaces match folders, one public class per file | PASS |
| `grep AddPoints Scripts/Enemies/AAAGun.cs` | Only the comment at :290. The only `AddPoints` definition is ScoreKeeper.cs:52. |
| `grep _playerRb Scripts/Enemies/AAAGun.cs` | None. |
| ARCHITECTURE §3 already carries the requested text (Player :138-139, AAAGun :145-147, Weapons :164-167) | OK |
| Fixed timestep (ProjectSettings/TimeManager.asset) | 0.02 s, which the P1-1 numbers use. |

## Static review

### B008: Bomb.BurstDamage (Bomb.cs:131-151, called at :97)
- **Filter:**
  - `target.Team != Team.Enemy` skips the player (also excluded by team) and every Neutral object.
  - `IsDead` skips a target that is already dead (an AAA between its death and its LateUpdate destroy).
  - `ReferenceEquals(target, _directHit)` skips the target the bomb already hit directly.
  - The `BurstDamaged` list dedupes a target that has several colliders.
- **Allocation:**
  - `ContactFilter2D` is a struct.
  - `BurstHits` (16) and `BurstDamaged` (16) are static and reused.
  - The array overload of `OverlapCircle` doesn't allocate.
  - `BurstHits[i] = null` drops references.
  - So there is no allocation per burst, apart from the editor-only fake-null of `GetComponentInParent`. More than 16 overlapping colliders are silently ignored, which is fine at this scale.
- **`useTriggers = true`:** needed, because every hittable collider is a trigger. The bomb's own collider resolves to no IDamageable and is skipped.
- **`_directHit`:** cleared in `Spawn` (:62). Note that `OnHit` always recycles the bomb, so a bomb that hit something directly never reaches the terrain burst. `_directHit` is a correct safety net that is never exercised today.
- **EnemyBomb has no burst:** its Update (EnemyBomb.cs:94-106) only plays the FX and recycles. Enemies are never hurt by enemy bombs. **OK.**
- **Burst centre:** the burst is centred on `transform.position`, the first position seen *below* the ground, not the impact point. See P1-1.

#### Newly bombable through the burst (only Team.Enemy IDamageables within 0.5 of the burst point)

| Entity | Reachable? | Consistent? |
|---|---|---|
| AAA (`aaa`) | Yes, the intended target. | GDD Enemies §2, ARCHITECTURE §3. |
| Fighters, bombers | Only within 0.5 of the terrain. MissionRunner spawns planes at least 1.5 above it (`MinPlaneHeight`), so in practice only a plane already hitting the ground. | Yes. Bombs could already hit planes directly through the trigger (Projectile team rule), so this isn't new. |
| Barrage balloons | No. Missions put them 3-4 units up with a 1x1 box, so the bottom is at least 2.5 above the terrain. | Yes. GDD only says "low-flying", and a direct bomb hit was already possible. |
| HealthBalloon | No. It is Neutral, and its `ApplyDamage` is a no-op anyway. | Yes. |
| StaticObstacle (buildings, trees) | No. It is Neutral and not an IDamageable. | Yes. |
| Zeppelin turrets, zeppelin, enemy base | Not spawnable yet (MissionRunner has no case for them). When they arrive as Team.Enemy IDamageables, a bomb landing next to the enemy base will damage it, which GDD mission 4 ("drop a bomb on the enemy base") wants. Turrets are airborne. | Yes, no conflict. |

### B009a: AAAGun.HandleDeath (AAAGun.cs:286-294)
- `AddPoints` is gone. The only award is `OnDamageTaken` → `RaiseEntityDestroyed("aaa", Enemy)` → `ScoreKeeper.GetScoreFor("aaa")` = 150.
- `ApplyDamage` returns early once `IsDead`, so a second bomb can't raise the event again. **Exactly once.**

### B009b: PlayerPlane.Velocity and AAAGun.PredictPosition
- `_velocity = (target - start) / Time.fixedDeltaTime` (PlayerPlane.cs:411) is the world displacement per tick, so it includes the scroll ride-along, the anchor ease and the W/S climb. The units are u/s.
- It is signed by world direction, not facing: flying left gives `vel.x < 0`, so the lead goes left. `predicted = pos + vel * t` is correct for both facings.
- On the runway, the plane doesn't move and `Velocity` is 0.
- The bomb drop still uses `_velocity.x` (unchanged).
- `t = dy / ShellSpeed` ignores the horizontal part of the shell's path (P2-1).

### B009c: FlakShell/EnemyBomb.EnsurePool
- The rebuild branch runs only when `_pool == null || _root == null`. `_pool` and `_root` are always assigned together, so in practice it runs on the first call and after a scene unload destroyed `_root`.
- `_pool?.Dispose()` runs **before** `_root` is reassigned, so it can't reach the new scene root. It only destroys its own items and its inner `Pool<T>` root.
- On already-destroyed items and roots: `IsAlive` and `_root != null` skip them, so there is no throw and no double Destroy. It also unsubscribes from `Application.quitting`.
- The new test `EnemyBomb_RebuildsPool_AfterRootDestroyed_DisposesStalePool` and the extended `FlakShell_RebuildsPool_AfterRootDestroyed` cover this.

### B007: Health.GetDamageMultiplier (Health.cs:117-120)
- Player mode only (`maxHitPoints == 0`): `Bomb` → `damageTaken.bomberGunner` (10). One EnemyBomb (damage 1) = 10%.
- **Other paths:**
  - The player's own bombs never reach the player's Health. Projectile skips its own team (Projectile.cs:93), and the burst requires Team.Enemy.
  - Collisions still use `Collision` (PlayerPlane.cs:262, :284-285; StaticObstacle.cs:61).
  - Enemies use hit-point mode, so a bomb on them stays `bombDamage / hp`, with no multiplier.
  - AAA has its own multiplier.
- **Unchanged.**

## Bugs

```
BUG: The bomb's ground burst is centred below the ground, so high drops (and low frame rates) still miss AAA
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Weapons/Bomb.cs:94-98 (BurstDamage(transform.position) and the FX use the
  below-ground position)
STEPS: Static, with numbers.
  - Bomb.Update sees the bomb only after the physics step that took it under the ground, so its centre is
    d below the ground, with d up to one fixed step of fall (two steps when a frame spans two steps).
  - The AAA box spans ground ± 0.3. A burst of radius 0.5 reaches it only while d <= 0.8.
  - The trigger already catches d <= 0.42, so the burst covers (0.42, 0.8] and nothing deeper.
  - Fall per 0.02 s step = 0.02 x sqrt(2 x 117.7 x h): about 0.69 from h = 5, 0.81 from 7, 0.92 from 9.
  - At 50+ fps (one step per frame): misses only above h ≈ 6.8. About 13% of well-aimed bombs from h = 9 still do nothing.
  - At 30 fps (WebGL, some frames run 2 steps): about 33% miss from h = 5 and about 48% from h = 9. That is
    back to the original B008 rate.
  - The explosion FX is also drawn up to about 0.9 inside the ground.
EXPECTED: docs/qa/B003-verify.md B008 / GDD Enemies §2 + mission 3: a bomb that visibly lands on the gun destroys it,
  "regardless of the fall speed".
ACTUAL: The burst is correct (filter, once each, no allocation), but its centre depends on the step depth.
  Fix: burst and explode at the impact point,
  `var p = new Vector2(transform.position.x, terrain.HeightAt(transform.position.x));`, then use BurstDamage(p) and
  TriggerExplosion(p). With the centre on the ground, any x within the gun's half-width + 0.5 registers.
```

```
BUG: The AAA lead uses the vertical distance / shell speed as the flight time, so shells still trail a scrolling plane
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/AAAGun.cs:222-223 (timeToImpact = dy / FlakShell.ShellSpeed)
STEPS: Static. Player 6 units above and moving at 5 u/s: t = 1.0 s, so the aim is 5 units ahead. The shell flies
  along (5,6)/7.8 at 6 u/s, so its vertical speed is 4.6 u/s, and it reaches the player's height after 1.3 s.
  By then the plane is 6.5 ahead, so the shell passes about 1.5 units behind. (Before B009b it passed 5 behind.)
EXPECTED: ARCHITECTURE §3 Enemies: "fires flak bursts up at the player's predicted position".
ACTUAL: An improved but systematic under-lead that grows with height and speed. Fix: iterate once or twice:
  t = |predicted - gun| / ShellSpeed, then recompute predicted (or solve the intercept quadratic).
  Low priority. It may even be desirable for fairness, so the orchestrator can accept it as is.
```

```
BUG: A health balloon absorbs player bombs and bullets (the projectile is spent on a no-op hit)
PRIORITY: P2 polish (pre-existing, found while checking what bombs reach; not part of this verdict)
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Weapons/Projectile.cs:88-96 (only skips own team), Bomb.cs:105-121 and
  Bullet.cs OnHit (always Recycle); Enemies/HealthBalloon.cs:105-108 (Team.Neutral, ApplyDamage no-op)
STEPS: Static. A HealthBalloon is a Neutral IDamageable with a 1x1 trigger 1.5-2 units up (missions 1 and 2). A bomb
  dropped through it, or a burst of fire through it, calls OnHit, which "damages" the balloon (no-op), plays an
  explosion (bomb) and recycles. The bomb is lost from the 7, and the AAA beyond it is not hit.
EXPECTED: GDD Obstacles: a health balloon is a pickup that "restores health" when flown through. Nothing says it
  blocks fire.
ACTUAL: Bombs and bullets stop on health balloons. Fix: in Projectile.OnTriggerEnter2D (or the Bullet and Bomb OnHit),
  ignore HealthBalloon (for example, skip Team.Neutral targets that aren't damageable, or check `is HealthBalloon`).
```

### Known and tracked, not re-filed
- `BomberAI.targetX` has no setter (T047).
- R1: the quit-time `ObjectPool.Dispose` uses a deferred Destroy (waiting on a playtest).
- AAA fires within half-width + 2 (accepted).
- The bomb drifts in the post-turn swing (accepted, GDD Change Log).

## EditMode tests added or changed (compiled, not run)

`Biplanes/Assets/Biplanes/Tests/EditMode/EnemyWeaponsAndPoolTests.cs` (11 → 18 tests; Unity still has to generate its `.meta`):
- **Extended:** `FlakShell_RebuildsPool_AfterRootDestroyed` now also checks that the stale pool `IsDisposed` and that the new `_root` is alive (B009c).
- **New:** `EnemyBomb_RebuildsPool_AfterRootDestroyed_DisposesStalePool` checks no throw, the old pool disposed, one root, and a live item (B009c).
- **New:** `Bomb_GroundBurst_KillsAdjacentAAA_SparesPlayerNeutralAndHealthBalloon` (B008):
  - A burst 0.6 under the ground kills the gun.
  - The player Health, a Neutral Health, a HealthBalloon and an enemy 3 units away are all untouched.
- **New:** `Bomb_GroundBurst_SkipsDirectHit_AndDamagesMultiColliderTargetOnce` (B008): a target with two colliders takes 25%, not 50%. The `_directHit` target is skipped, and a second burst adds exactly one hit.
- **New:** `Bomb_Spawn_ClearsDirectHitFromPreviousFlight` (B008).
- **New:** `AAAGun_BombKill_ScoresAaaExactlyOnce` (B009a): the bomb kill, a second bomb, then `HandleDeath`. The score is exactly `score.aaa`. HandleDeath's edit-mode `Destroy` error is ignored with `LogAssert.ignoreFailingMessages`, which is scoped to the test.
- **New:** `AAAGun_PredictPosition_LeadsWithPlayerVelocity_BothFacings` (B009b): +x and -x leads of `vel * dy / ShellSpeed`, plus the y lead.
- **New:** `Health_PlayerBombHit_UsesBomberGunner_OtherSourcesAndEnemiesUnchanged` (B007).
- The two burst tests call `Assume` on `Physics2D.OverlapPoint` seeing an EditMode collider. If 2D queries don't see EditMode objects, they report Inconclusive rather than a false failure.
- The suite now has **61** EditMode tests.

## Consolidated manual playtest checklist (everything since T924 that still needs a human)

Run these in the editor (Unity → Biplanes → BuildScenes, then Play) and once in a WebGL build.
The earlier lists still stand. Run them by reference and don't repeat them here:
- `docs/qa/T924-verify.md` § Manual playtest checklist, steps 1-19: reversible flight, turns, the start limit, terrain and parallax when reversing, and WebGL at a narrow window.
- `docs/qa/B001-verify.md` § Manual playtest checklist, steps 1-9: Next/Retry cleanliness, a single tracker, and the first bullet burst speed.
- `docs/qa/B003-verify.md` § Manual playtest checklist (B003-B006):
  - Steps 1-2 and 4-7: held-A takeoff, live flak, enemy bombs, the pool count over Next/Retry, R1 when leaving Play mode, and win/lose latching.
  - Step 3 is **replaced** by B1 and B2 below.

New and changed for B007-B009:
- **B1. Mission 3, AAA kill rate (P1-1):**
  - Bomb a gun 10 times from near the top of the screen and 10 times from 2 units up, and count the kills.
  - Expected after the fix: 10/10 both ways. With the current build, expect a few misses from high up.
  - Repeat the high drops in the WebGL build in a busy scene, or with the editor's frame rate capped at 30 (Application.targetFrameRate). Misses then rise sharply.
  - Watch where the explosion is drawn: on the ground surface, or inside it.
- **B2. AAA scoring:** each AAA kill adds exactly 150 on the HUD, never 300. Bullets never kill AAA.
- **B3. Burst side effects:**
  - Bomb the ground next to (within half a unit of) a gun: it dies.
  - Bomb the ground 1+ unit away: it survives.
  - Bomb the ground under the player's own plane on the runway, or just after a low pass: the player takes no damage.
  - A health balloon near the impact is not consumed by the burst.
- **B4. Health balloon blocking (P2-3, missions 1-2):**
  - Fire and bomb through a health balloon, and note whether the projectiles stop on it.
  - Note whether a bomb is used up and the balloon stays.
- **B5. Flak lead (B009b, P2-1):**
  - Fly right at cruise height over mission 3 AAA: bursts now aim ahead of the plane, not behind it. They may still pass slightly behind.
  - Turn left and repeat: the lead flips to the left.
  - On the runway and while climbing straight up, aim is sensible (no wild lead).
- **B6. Enemy bomb damage (B007; needs T047, or a test mission with a bomber `targetX`):**
  - One enemy bomb hit adds exactly 10% on the HUD (it was 8%).
  - Enemy bombs never hurt other enemies or AAA.
- **B7. Pool rebuild (B009c):**
  - Chain Retry 5 times in mission 3 after AAA has fired and a bomber has dropped bombs.
  - The Hierarchy shows one `FlakShellPool` (and one `EnemyBombPool`).
  - Then quit Play mode: no errors and no "objects were not cleaned up" warnings (with R1).
- **B8.** Unity Test Runner → EditMode → all 61 tests pass, none Inconclusive. Unity generates the `.meta` for `EnemyWeaponsAndPoolTests.cs` on import.
