# T974: QA of B072 (barrage balloon: 100 HP, collision pops it for 33.3% player damage, balloons simulated)

**Verdict: PASS** (0 P0, 0 P1, 3 P2)

## What ran

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes, PASS Biplanes.Editor (exit 0) |
| Test assembly compile (scratch script: compile_check's method applied to `Biplanes.Tests.EditMode.csproj`, TargetFrameworkVersion raised to v4.7.2 for nunit) | PASS. The whole EditMode test assembly compiles, including `BarrageBalloonCollisionTests.cs` (B072) and the new `BarrageBalloonQaTests.cs` |
| `python tools/validate_missions.py` | 5/5 OK (exit 0). Only the existing duration warnings for missions 3 and 4 |
| Unity batch `tools/unity.sh test` | **NOT RUN.** The editor has the project open (`Unity.exe -projectpath D:\Developer\MAIGD\Biplanes`, `Temp/UnityLockfile` present), so batch mode is blocked. `Logs/editmode-results.xml` (12:18, 665/665 passed) is from **before** B072: it has no BarrageBalloon tests, and the editor's ScriptAssemblies (12:17) don't include the B072 sources (12:36). Run it in the Test Runner, or close the editor and run batch. |

## Static review (B072 files)

- Namespaces match the folders. No `rb.velocity`, no `UnityEngine.Input`. No hand-written .prefab, .unity, .asset or .meta files. One public class per file.
- `Balance.json` has `enemyHealth.barrageBalloon` 100 and `damageTaken.barrageBalloonCollision` 50. Both have code defaults of 100/50 in `GameBalance.cs`.
- **Hit points:** Health enemy mode gives 25/100 = 25% per bullet, so 4 bullets kill, and `bombDamage` 100 means 1 bomb kills. Collision and PlaneCollision are fatal for enemies.
- **Player damage:** `BarrageBalloonCollisionScale` = 50/50 = 1, then Health applies `planeCollision` 50 / resistance 1.5 = 33.33%. Three hits reach 100: the B052 snap fixes the float sum of 99.99996, and the plane crashes.
- **Grace window:** `HitBarrageBalloon` pops the balloon before it checks the grace window, so a balloon touched inside the window, including right after a fighter, still pops without extra damage. A dead balloon returns early, so a second hitbox in the same pass, a re-entry, or touching a balloon already shot down costs nothing.
- **Pops once:** `Health.ApplyDamage` returns while `IsDead`, so `EntityDestroyed("barrageBalloon")` fires once. ScoreKeeper (+75) and `ObjectiveTracker.OnEntityDestroyed` each count it once.
- **`simulated = true` side effects** (each checked against the code):
  - Player bullets and bombs: `Projectile.Init` sets `useFullKinematicContacts`, so the kinematic-vs-kinematic trigger fires. `Bomb.BurstDamage` OverlapCircle now finds the balloon. This is what makes mission 4 winnable at all: before B072, nothing could damage a balloon.
  - Enemy bullets, flak and enemy bombs: `Projectile.OnTriggerEnter2D` skips the own team (balloons are Team.Enemy), and every enemy projectile's `OnHit` only accepts Team.Player.
  - Fighters and bombers: kinematic, without full kinematic contacts, so they get no trigger with the balloon. Their `OnTriggerEnter2D` does nothing anyway (BomberAI is a no-op, FighterAI has none). BarrageBalloon's own `OnTriggerEnter2D` does nothing for any team.
  - Blocking or pushing: the body is kinematic and the collider is a trigger, so it never blocks or pushes anything.
  - Zeppelin, turrets, EnemyBase, PlayerHQ, StaticObstacle triggers: they only act on PlayerPlane or Team.Player.
  - Ground: terrain has no collider, and the tether is a LineRenderer with no collider.
- **Drift:** `driftAxis` is right, so drift is horizontal only and can't push a balloon into terrain. Each step adds `sin(t)·0.8·dt·speed`, and the integrated offset is `0.8·(cos t0 − cos t)`, which stays within 1.6 u. Spawn y is the height above the local terrain (2 x 3.0, 2 x 4.0, 2 x 3.5 in mission 4). Pair spacing is 5 or 6 u, more than 2 x 1.6, so the two balloons of a pair can't overlap. See P2-2 for the off-centre swing.
- **EnemyPressure / minimap / counters:** EnemyPressure only looks at aircraft. Minimap `IsLive` skips balloons that are inactive or dead. HUD counters read `ObjectiveTracker.RemainingKills`. None of these changed.
- **Death cleanup:** after Health dies, `FixedUpdate` calls `OnDestroyedBalloon`: one explosion, then the tether is disabled and destroyed, then `SetActive(false)`. FixedUpdate doesn't run again after that, so there's no second explosion. Mission teardown destroys the root GameObject, and the tether goes with it as a child. No leak.
- **Mission 4 feasibility:** the `pop_all_balloons` objective needs 6 kills (3 entries x 2). At scroll speed 3.9, a balloon stays on screen for about 4.5 s, and 4 bullets take about 0.5 s at fireRate 8. One bomb, or a ram at 33% cost, also works, and the player can turn back (A/D). It's feasible.
- **GDD and ARCHITECTURE:** Obstacles item 1, the 2026-11-22 Change Log row, and ARCHITECTURE §3/§4 already describe the B072 behaviour, and the code matches them.

## Tests added: `Biplanes/Assets/Biplanes/Tests/EditMode/BarrageBalloonQaTests.cs` (13)

- `Balloon_IsSimulatedKinematicTrigger_NeverABlockingBody`
- `Balloon_AfterAwake_IsFoundByPhysicsQueries`: regression test for the simulated fix.
- `BombBurst_AtBalloon_PopsIt_Once`
- `PlayerBullets_ThroughTrigger_FourPop`: runs through `Projectile.OnTriggerEnter2D`.
- `EnemyBullet_Flak_EnemyBomb_ThroughTrigger_LeaveBalloonUntouched`
- `BalloonOwnTrigger_FromAnyTeam_DoesNothing`
- `FighterTouch_ThenBalloonInGrace_BalloonPops_PlayerHitOnce`
- `BalloonTouch_ThenFighterInGrace_FighterDies_PlayerHitOnce`
- `ShotDownBalloon_ThenTouched_NoPlayerDamage`
- `Ram_Scores75_Once_EvenWithRepeatedContacts`
- `Mission4_PopAllBalloons_Needs6_RamsAndShotsBothCount_Once`
- `Mission4_Balloons_AboveTerrain_WithDriftMargin`
- `DeadBalloon_FixedUpdate_Explodes_Deactivates_RemovesTether`

They compile. They have not run yet (Unity is unavailable, see above).

## Bugs

```
BUG: Balloon death uses FxManager.Instance instead of GetOrNull
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/BarrageBalloon.cs:155 (OnDestroyedBalloon)
STEPS: Static. AAAGun.HandleDeath and FlakShell use FxManager.GetOrNull; this path uses
       FxManager.Instance, which creates a DontDestroyOnLoad manager (throws InvalidOperationException
       in EditMode when no manager exists). Also calls GameObject.Destroy on the tether, which logs an
       error outside Play mode. Harmless in Play mode.
EXPECTED: Cosmetic death FX skip safely when there is no manager (ARCHITECTURE §3 FX, AAAGun pattern).
ACTUAL: EditMode tests must seed an FxManager and ignore the Destroy log to exercise the death path.
```

```
BUG: Balloon drift is not centred on its mission placement
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/BarrageBalloon.cs:120-133 (FixedUpdate)
STEPS: Static. Each step adds sin(t)*amp*dt*speed with t = Time.time*speed + random seed, so the
       position is x0 + amp*(cos(t0) - cos(t)): the swing centre is offset by amp*cos(t0) (up to 0.8 u)
       and the balloon can sit up to 1.6 u from its JSON x. The spawn `pattern` field is ignored.
EXPECTED: "Drifts side-to-side" around the placed point (class doc; GDD Obstacles 1).
ACTUAL: Bounded, horizontal only, no gameplay impact in mission 4 (pairs 5-6 u apart), but the
        swing is lopsided. Fix: set the position to the spawn point + sin(t)*amp instead of integrating.
```

```
BUG: Balloon tether ends at y = -4.5, not at the terrain under it
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/BarrageBalloon.cs:192 (UpdateTetherVisual), :182 (material = null)
STEPS: Static. Mission 4 terrain rises up to 2.5 u above GroundBaseY, but the tether always goes
       to y = -4.5, so it runs down into the hills. The LineRenderer also has a null material.
       Neither changed in B072.
EXPECTED: The tether is anchored at the ground under the balloon (TerrainStrip.HeightAt(x)).
ACTUAL: The tether runs below the terrain surface. It's visual only.
```

## Not covered in EditMode (manual playtest, mission 4)

- In real physics, a stream of bullets pops a balloon on the 4th hit, and a bomb dropped onto it pops it in one.
- Flying through a balloon gives +75, adds 33% on the HUD, and shows the explosion. Flying through two balloons in one pass costs 33% once.
- The balloon objective counter goes down by one per balloon, whether it was shot or rammed.
- Balloons drift slightly and never overlap. Fighters fly through them with no effect.
