# B003 / B004 / B005 / B006 verification (QA task T927)

Scope: the mechanics fixes from `docs/crew/handoffs/B003-mechanics.md` (B003 + B006) and
`docs/crew/handoffs/B004-mechanics.md` (B004 + B005). The two agents worked in parallel, so
this also checks how their changes interact. The Unity editor is open, so the checks are
compile and static only. No tests were run.

## Verdict

| Bug | Verdict | Notes |
|---|---|---|
| B003 (held A at takeoff) | **PASS** | Traced and covered by 4 updated or new EditMode tests. |
| B004 (AAA never fires) | **PASS** | Flak is live, Team.Enemy, on a cooldown, aimed upward, fires only near the screen, and a hit does exactly `damageTaken.flak`. New P1 below: the AAA's 0.6-unit trigger lets fast bombs pass through it about half the time. |
| B005 (bomber bombs from the player pool) | **PASS** | `EnemyBomb` has its own pool, is Team.Enemy and hurts only the player. The player's Bomb pool and BombBay are untouched. |
| B006 (pool leak, score carry-over, repeated mission end) | **PASS** | Dispose and InitPool work, ScoreKeeper.ResetScore is called in StartMission, and `_missionEnded` covers win and loss. One P2 below (stale flak and enemy-bomb pools stay subscribed to `Application.quitting` after a Retry), and one unverified risk. |
| Interaction (lazy scene-root pools vs the new ObjectPool) | **PASS** | No double destroy, no use after a scene unload, no stale pool after Next. Details below. |

**Overall: PASS. 0 P0, 1 P1, 3 P2.**

## Commands

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes (+1 new file not yet in the csproj: EnemyBomb.cs), PASS Biplanes.Editor, exit 0 |
| Tests scratch build: a source-built Biplanes.dll, then all 4 `Tests/EditMode/*.cs` against it. Generated `Biplanes.Tests.EditMode.csproj`, ProjectReferences replaced by ScriptAssemblies DLLs, retargeted to v4.7.2 for the net472 nunit. Temporary csproj files deleted, output in %TEMP%. | PASS. Nothing left in the project (`git status`: no `zz_*` files). |
| `bash tools/unity.sh test` | Not run: the editor has the project open. |

## Static checks

- Namespaces match the folders: `EnemyBomb` and `FlakShell` are in `Biplanes.Weapons`, `AAAGun` is in `Biplanes.Enemies`.
- There is one public class per file, and the filenames match.
- No `rb.velocity`, `UnityEngine.Input` or `Input.Get*` anywhere in Scripts/.
- `Keyboard` appears only in a comment in PlayerPlane.cs.
- No hand-written `.meta` files. `EnemyBomb.cs` has no `.meta` yet; Unity generates it on import.
- `grep Bomb.Spawn Scripts/Enemies` finds only `EnemyBomb.Spawn` (BomberAI.cs:188, 199).
- APIs used and confirmed to exist:
  - `ObjectPool.Dispose`, `ObjectPool.IsDisposed`
  - `ScoreKeeper.ResetScore`
  - `FlakShell.ShellSpeed`
  - `GameEvents.MissionEnded`
  - `TerrainStrip.Instance.HeightAt`
  - `FxManager.Instance.TriggerExplosion`
  - the protected `Projectile._rb`, accessed from a subclass static on its own type

## Trace notes

### B003 (PlayerPlane.cs:353-401)
- **Takeoff tick:** `_ignoreHeldLeft = state.left`. The ignore block then sets `state.left = false`, UpdateFacing is skipped, and `_prevLeft` stores the suppressed `false`.
- **Later ticks with A held:** A stays suppressed, so the plane can't turn left.
- **The first tick without A** clears the flag. The next A is therefore a fresh press and turns the plane left.
- **D while the flag is set** still turns the plane right.
- **D+A at takeoff** gives facing +1, and A stays ignored after D is released.
- **A alone on the runway** doesn't take off and doesn't arm the flag.
- **Airborne starts:** Create sets the flag to `false`, and it is only set on a runway takeoff.
- The `!tookOffThisTick` skip at line 383 is now redundant but harmless.

### B006a pools
- Every item is parented under the pool root in AddItems, and Push re-parents it there.
- Dispose:
  - deactivates and destroys every tracked item (pooled or in flight), then the root;
  - is idempotent;
  - skips Unity-null items.
- `Pop` skips dead items. `Push`/`Expand` after Dispose are no-ops.
- `Bullet.InitPool` and `Bomb.InitPool` call `_pool?.Dispose()` first.
- PlayerPlane caches the projectile and fallback sprites in statics, so it builds no new Texture2D per mission.
- **Retry:** the Bullet/Bomb roots are still HideAndDontSave and unparented, so they survive the scene load. The new mission's `PlayerPlane.Create → InitPool` disposes them right away.
- **Next:** InitPool disposes the old pool in the same frame.

### B006b, B006c (MissionRunner.cs:137-200, 308-351, 398, 514-551)
- `ResetScore()` runs after the teardown and before the HUD is created.
- `_missionEnded` is cleared in StartMission.
- It is set on a win (HandleObjectiveComplete, before SetState(Won)).
- It is set on a loss through the `GameEvents.MissionEnded` subscription. GameManager.SetState(Lost) raises the event, and so does PlayerPlane's fallback when there is no GameManager.
- Update's surviveToEnd check is skipped once the flag is set.
- Subscribe and unsubscribe are paired in StartMission, TearDownPreviousMission and OnDestroy, so Next keeps one subscription (tested).

### B004 (FlakShell.cs, AAAGun.cs)
- **Shell setup:** shells are Team.Enemy, with speed 6, lifetime 3 and damage 1, all set in Awake (before the first Init).
- **Damage:** `OnHit` damages only `Team.Player`, so Health gives `1 × damageTaken.flak = 15` (tested). Enemies are skipped by Projectile's team check, and neutral objects by OnHit.
- **Direction:** the direction is normalised, and when y < 0.5 it is raised to 0.5 and normalised again. The effective minimum is y ≈ 0.447 (26.6°), not 0.5. That is cosmetic, but shells always climb (tested).
- **Cooldown:** `_lastFireTime` is now set when a burst starts. Bursts start 3 s apart, and each burst is 3 shells 0.2 s apart.
- **Lead:** the sign of `dy` is now correct. See P2-2: the lead term is always 0.
- **Placement:** the gun is placed at the terrain height (`GetTerrainHeightAt(posX)` in MissionRunner.cs:266, plus `_groundY`). The LateUpdate clamp now holds the gun at that height.
- **Fire range:** the gun fires within the camera half-width + 2. A gun up to 2 units off-screen therefore fires. Ground objects spawn 2 units past the right edge, so a gun fires as soon as it spawns. This matches the handoff, but ARCHITECTURE says "only while on screen". It's a design note, not filed.
- **Pool size:** 24 shells. The densest AAA groups (3 guns at 4-5 spacing in missions 3, 4 and 5) keep about 3-4 shells each in flight, so the pool is enough.
- **Collider safety:**
  - Bullets hitting the AAA call `ApplyDamage(Bullet)`, which is dropped, so bullets can't kill it. The bullet is recycled.
  - `PlayerPlane.OnTriggerEnter2D` only reacts to FighterAI/BomberAI, so the player never crashes into the AAA's trigger. Flying low enough to touch it is a terrain crash first, because the collider is only 0.3 above the ground.
  - The AAA's `Collision` case is never triggered by anything.
  - Shells spawn inside the AAA's own trigger and are skipped (same team).

### B005 (EnemyBomb.cs, BomberAI.cs)
- EnemyBomb has its own lazy pool (12) under `EnemyBombPool`.
- The bomb starts with the bomber's velocity and uses gravity ×12, the same as Bomb.
- It bursts on the terrain.
- Its OnHit damages only Team.Player (`DamageSource.Bomb`, 1 unit).
- It never references `Bomb._pool`, `BombBay` or `BombsChanged` (tested: the player's Bomb pool `Available` doesn't change).

### Interaction: lazy scene-root pools (FlakShell/EnemyBomb) and the new ObjectPool
- **Construction order:**
  1. `EnsurePool` creates the scene root.
  2. `new ObjectPool(..., parent: root)` creates the HideAndDontSave inner root under it.
  3. AddItems creates each item under the scene root (factory), then re-parents it under the inner root.
  4. `ClearDontSaveFlags` then sees exactly one child, the inner root, and resets it to `None`.
  - The items never had flags. Nothing is DontSave, so the whole tree dies with the scene (tested: no `Pool<FlakShell>` HideAndDontSave root remains).
- **Retry (scene reload):** the scene root, inner root and items are destroyed by the unload. `EnsurePool` sees `_root == null` and rebuilds (tested by destroying the root). The old pool object is never popped again.
- **Next (no reload):** the same pool is reused. The old mission's in-flight shells expire within 3 s near the old x. No stale pool.
- **Double destroy:**
  - On quit, the current pool's Dispose (the `Application.quitting` handler) calls `Destroy` on items and the inner root that the scene teardown also destroys. Unity tolerates a second Destroy of an object already pending destruction.
  - A stale pool's Dispose (from a previous Retry) finds only Unity-null items and a null root, so it destroys nothing.
  - No `DestroyImmediate` runs twice on the same object.
- **Leaving Play mode:** the flak and enemy-bomb trees are normal scene objects and are cleaned up with the scene. The Bullet/Bomb roots depend on the quitting handler (see Risk R1).

## Bugs

```
BUG: Player bombs pass through the AAA's trigger about half the time at normal drop heights
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/AAAGun.cs:124-126 (BoxCollider2D 1 x 0.6, centred on the ground);
  Weapons/Bomb.cs:69-82 (gravity x12, recycle as soon as y <= HeightAt)
STEPS: Static, with numbers.
  - The gun's centre sits at the terrain height (MissionRunner.cs:266), so its trigger reaches 0.3 above the ground.
    The bomb's radius is 0.12, so a bomb can only touch the gun while its centre is in (ground, ground + 0.42].
    Below the ground, Bomb.Update recycles it before the next physics step.
  - Triggers are sampled once per 0.02 s step. Bombs fall at 9.81 x 12 = 118 u/s^2 with no continuous detection.
  - From 5 units up the bomb arrives at about 34 u/s (0.69 per step). From 9 units it arrives at about 46 u/s (0.92 per step).
    The chance that a step lands inside the 0.42 window is about 61% and 46%. Mission 3's player flies 5-9 units above AAA.
  - The bomb still bursts visually on top of the gun (Bomb's terrain burst), so the player sees a hit that did nothing.
  - The gun sprite is 1.0 x 0.8 with a centre pivot, so its top is 0.4 above the ground. The collider's top is 0.3.
EXPECTED: GDD Enemies §2 "It can only be destroyed with bombs". GDD mission 3 "destroy AAA with bombs". A bomb that
  visibly lands on the gun destroys it (ARCHITECTURE §3 Enemies AAAGun "damaged by DamageSource.Bomb").
ACTUAL: Only about 50-60% of well-aimed bombs dropped from cruising height register. Low drops (2 units or less) nearly always hit.
  The estimate is static; confirm in play. Fix idea: on the ground burst in Bomb.Update, apply the bomb's damage to every
  non-player IDamageable within a small radius (Physics2D.OverlapCircle around the burst point, about 0.5). That makes ground
  targets robust regardless of the fall speed. Taller colliders alone don't help, because each step covers 0.7-0.9 units.
```

```
BUG: Destroying an AAA gun scores twice (300 instead of Balance score.aaa 150)
PRIORITY: P2 polish (pre-existing code, newly reachable: the AAA had no collider until B004)
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/AAAGun.cs:249 (RaiseEntityDestroyed) and :290-293 (HandleDeath AddPoints)
STEPS: Static. A bomb kills the AAA. OnDamageTaken raises GameEvents.EntityDestroyed("aaa", Enemy), and
  ScoreKeeper.OnEntityDestroyed adds score.aaa (150). On the next LateUpdate, HandleDeath calls ScoreKeeper.AddPoints(150) again.
EXPECTED: ARCHITECTURE §4 score table: aaa = 150 per kill. Fighters and bombers score only through EntityDestroyed.
ACTUAL: +300 per AAA, which inflates best scores in missions 3-5. Fix: remove the AddPoints call from HandleDeath.
```

```
BUG: AAA lead is always zero, because the player's Rigidbody2D.linearVelocity is never set
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/AAAGun.cs:138, 227 (reads _playerRb.linearVelocity);
  Player/PlayerPlane.cs:113, 407 (kinematic body moved only by MovePosition, velocity set to 0 once)
STEPS: Static. MovePosition doesn't write linearVelocity, so it stays Vector2.zero. PredictPosition returns
  the player's current position. The B004 dy sign fix is correct, but the vel term contributes nothing.
  A scrolling player moves at about the scroll speed, so 6 u/s shells aimed at where the player is now trail behind.
EXPECTED: ARCHITECTURE §3 Enemies: AAAGun "fires flak bursts up at the player's predicted position".
ACTUAL: shells aim at the player's current position. Fix idea: expose PlayerPlane's world velocity (its private
  _velocity, already computed per tick) as a read-only property and use it in PredictPosition.
```

```
BUG: The FlakShell and EnemyBomb pools aren't disposed when they're rebuilt after a Retry, so each stale pool stays subscribed to Application.quitting
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Weapons/FlakShell.cs:193-202 and EnemyBomb.cs:302-315 (EnsurePool);
  ObjectPool.cs:53 (Application.quitting += Dispose in Play mode)
STEPS: Static. After a scene reload, EnsurePool replaces _pool without calling Dispose. The old ObjectPool, with its
  queue, its list of 24 or 12 dead wrappers, and the delegate, stays reachable from Application.quitting until the app quits.
  These are managed objects only. The GameObjects themselves are freed with the scene.
EXPECTED: B006 "no pool leak across Next and Retry". The B003 handoff REQUEST asked for `_pool?.Dispose()` in these pools as well.
ACTUAL: one small managed leak per Retry per pool. Fix: call `_pool?.Dispose();` at the top of the rebuild branch in both
  EnsurePool methods. That is safe: the dead items are skipped, the null root is skipped, and it unsubscribes from quitting.
```

### Risk (unverified, not filed)
- **R1:** `ObjectPool.Dispose` runs from `Application.quitting` while `Application.isPlaying` is still true, so it uses the deferred `Object.Destroy`.
  - Static analysis can't confirm that the editor flushes these deferred destroys before it leaves Play mode.
  - If it doesn't, the HideAndDontSave `Pool<Bullet>`/`Pool<Bomb>` roots and their children would survive into Edit mode, which is the original B006 symptom.
  - Check it with playtest step 6. If they survive, use `DestroyImmediate` on the quitting path.

### Already tracked, not re-filed
- Health maps `DamageSource.Bomb` on the player to fighterBullet (8). Enemy bombs therefore do 8%, not bomberGunner's 10%.
- `BomberAI.targetX` has no setter (T047), so bombers never drop EnemyBombs in real missions yet.
- Bombs drift during the post-turn swing.
- A loss doesn't save a best score.
- Same tunnelling physics as P1 for enemy bombs vs the player. It doesn't matter until T047 lands.

## EditMode tests added or changed (compiled, not run)

`Biplanes/Assets/Biplanes/Tests/EditMode/`:

- **PlayerFacingTests.cs:**
  - `Runway_WPlusA_TakesOffFacingRight` is replaced by `Runway_WPlusA_HeldA_IgnoredUntilReleasedAndRepressed`.
  - New: `Runway_AHeldThenW_TakesOffRight_AndAStaysIgnored`.
  - New: `Runway_DPlusA_TakesOffRight_DStillWorks_FreshATurnsLeft`.
  - New: `AirborneStart_HeldA_TurnsLeftImmediately`.
  - `AfterTakeoff_FreshA_TurnsLeft` now also asserts that the flag isn't armed.
- **MissionRestartTests.cs (+3):**
  - `Next_ResetsScoreToZero` (also checks that ScoreChanged(0) is raised).
  - `Loss_BlocksObjectiveHandling_UntilNextStart`.
  - `Next_KeepsOneMissionEndedSubscription`.
  - The win path isn't driven in EditMode, because it would write the developer's PlayerPrefs (best score and unlock).
- **New `EnemyWeaponsAndPoolTests.cs` (11):**
  - ObjectPool:
    - `ObjectPool_ItemsUnderRoot_DisposeDestroysPooledAndInFlight`
    - `ObjectPool_PopSkipsItemsDestroyedElsewhere`
    - `BulletAndBombInitPool_Twice_DisposeThePreviousPool`
  - FlakShell:
    - `FlakShell_PoolBuiltOnce_UnderSceneRoot_WithoutDontSaveFlags`
    - `FlakShell_RebuildsPool_AfterRootDestroyed`
    - `FlakShell_FliesUpAlongAim_AtShellSpeed`
    - `FlakShell_HitOnPlayer_AddsExactlyDamageTakenFlak_IgnoresEnemies`
  - `AAAGun_TriggerCollider_TakesBombDamageOnly`
  - EnemyBomb:
    - `EnemyBomb_OwnPool_TeamEnemy_NeverTouchesPlayerBombPool`
    - `EnemyBomb_IgnoresEnemiesAndNeutrals`
  - `ScoreKeeper_ResetScore_ZeroesAndRaisesScoreChanged`
  - `EnemyBomb.OnHit` on the player isn't called in EditMode: FxManager.Instance calls DontDestroyOnLoad, which throws outside Play mode.
  - `EnemyWeaponsAndPoolTests.cs` needs Unity to generate its `.meta`.

The suite now has 54 EditMode tests.

## Manual playtest checklist (B003-B006)

1. **Mission 1 runway:**
   - Hold A, press W, and keep A held for 2 s: the plane takes off and stays facing right.
   - Release A and press it again: it turns left.
   - D still turns it right.
2. **Mission 3:**
   - AAA comes into view and fires bursts of 3 shells upward, about every 3 s.
   - A shell hit adds exactly 15% on the HUD.
   - Guns far off-screen don't fire.
3. **Mission 3 bombing:**
   - Bomb AAA from high altitude (near the top of the screen) 10 times and from low altitude 10 times, and count the kills (P1).
   - Each kill adds 150 points; note if it adds 300 (P2).
   - Shooting AAA with bullets never destroys it.
   - Flying low over AAA doesn't crash the plane unless it hits the ground.
4. **Bomber bombs** (needs T047, or a test mission with `targetX` set): the bombs fall and burst on the ground, a hit on the player adds damage, and the HUD bomb count doesn't change.
5. **Next and Retry:**
   - Win mission 1 and press Next: mission 2's score starts at 0 and stays per-mission.
   - Chain Next 1→2→3 and Retry several times.
   - The Hierarchy shows exactly one `FlakShellPool` (once AAA has fired) and one `EnemyBombPool` (once one exists).
   - The Profiler's GameObject count stays flat.
6. **Leave Play mode** after shooting and bombing:
   - The Console shows no "objects were not cleaned up" warnings.
   - In Edit mode, the Memory Profiler (or a `Resources.FindObjectsOfTypeAll<GameObject>()` probe) shows no `Pool<Bullet>`/`Pool<Bomb>` roots (R1).
7. **Win and lose:**
   - After a win, "all objectives complete" is logged exactly once.
   - After a crash, killing the last target (or reaching the end) doesn't produce a win.
8. Unity Test Runner → EditMode → all tests pass.
