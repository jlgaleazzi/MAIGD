# T940 verify: B021 (zeppelin turret line of fire, zeppelin spawn look-ahead) and the final M5 verdict

**Verdict: PASS. Bugs: 0 P0, 0 P1, 0 P2.**
- T939 P1-1 is closed:
  - The underside turret now has a 0.31-unit band of horizontal bullet heights, reachable from both sides.
  - The shield can drop, and mission 5 can be won by static analysis.
- T939 P2-1 is closed: at every aspect the hull is wholly 0.5 units past the right screen edge when it spawns.
- The HQ-before-map-end margin grew from 4.3 s to 5.8 s at 4:3, the binding aspect.
- Still open and out of scope: T937, B015, B019, and V001 (y range in M1/M4).

Scope:
- Read: T939-verify, the B021 handoff, `ZeppelinAI.cs`, `ZeppelinTurret.cs`, `MissionRunner.cs` (SpawnEntities, Update trigger), `mission_05.json` and `Balance.json`.
- Also read `Bullet.cs`, `Projectile.cs`, the `PlayerPlane` bullet pool and gun offset, `ScrollController.FixedUpdate`, `ObjectiveTracker.CheckMapEnd`/`OnCameraProgress`, `TerrainStrip.HeightAt` and ProjectSettings `TimeManager`.
- Tests read: `ZeppelinAndBaseTests.cs` (the 2 zeppelin geometry tests) and `MissionDataRulesTests.cs` (TriggerX, the 3 M5 tests).
- Zeppelin hull art was not reviewed. The art agent owns it and is editing it concurrently (A-ZEP-PYLON).

## Results

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS, exit 0 (`PASS Biplanes (+6 new file(s) not yet in the csproj)`, `PASS Biplanes.Editor`) |
| Test scratch build. Script: `t940_build.py` in the session scratchpad. The runtime DLL is built from source, then all 9 `Tests/EditMode/*.cs` files are built against it (ProjectReferences become ScriptAssemblies DLLs, retargeted to v4.7.2 for nunit's net472). | PASS, exit 0, **0 errors**. The only warnings are pre-existing CS0618 obsolete `FindObjectsByType(.., SortMode)`/`FindFirstObjectByType` warnings in older tests. No `zz_*` csproj is left in the project. |
| `bash tools/unity.sh test` | Not run: the editor has the project open. |
| `python tools/validate_missions.py` | Exit 1. `OK` for mission_02, 03 and 05. M1 spawns[1]/[5] and M4 spawns[2]/[9] fail only on "y must be in [0, 8.5]" (V001, known and out of scope). |
| Static checks on the B021 files | PASS on every check: <ul><li>Namespaces match the folders.</li><li>`ZeppelinAI.HalfLength` (public static) and `MissionRunner.ZeppelinEntryMarginX` (private const, read by test reflection) exist.</li><li>No `rb.velocity` and no `UnityEngine.Input`.</li><li>One public class per file.</li><li>No hand-written YAML or `.meta`.</li></ul> |
| ARCHITECTURE | The B021 REQUESTS are already applied: §3 ZeppelinAI line 150 and the §5 trigger rule at line 216. |

## Line of fire, recomputed independently

Inputs:
- Player bullet: a circle of radius 0.08 (`PlayerPlane` `MakeProjectile<Bullet>(…, 0.08f)`).
- `GunOffset.y` is 0, so the bullet's y equals the plane's y.
- `Bullet.Spawn` sets the direction to ±x only.
- The first collider the bullet overlaps takes the hit (`Projectile.OnTriggerEnter2D` → `GetComponentInParent<IDamageable>`). The turret's own GameObject holds its IDamageable, so a turret collider resolves to the turret, not the hull.
- A shielded hull absorbs the hit and the bullet is recycled.

Boxes in hull-local units, mirrored to fly left (x negated):

| Collider | x | y |
|---|---|---|
| Envelope | -4.15..3.00 | -0.58..0.78 |
| Tail | 3.00..3.92 | -0.60..0.80 |
| Gondola 1 | 1.40..2.10 | -0.94..-0.68 |
| Gondola 2 | -0.80..-0.10 | -0.94..-0.68 |
| Gondola 3 | -2.95..-2.00 | -0.94..-0.68 |
| Turret top fore | -2.15..-1.65 | 0.85..1.15 |
| Turret top aft | 1.05..1.55 | 0.85..1.15 |
| **Turret underside** | 0.40..0.90 | **-1.25..-0.95** |

Clear band of bullet-centre heights, where a bullet reaches the turret before it overlaps any hull box. Scan step 0.0005. Script: `t940_geom.py` in the session scratchpad.

| Turret | Band (local) | Width | From | World y (hull centre 0.9) |
|---|---|---|---|---|
| Top fore | 0.86..1.23 | **0.37** | left and right | 1.76..2.13 |
| Top aft | 0.86..1.23 | **0.37** | left and right | 1.76..2.13 |
| Underside | -1.33..-1.02 | **0.31** | left and right | -0.43..-0.12 |

Notes on the bands:
- The top turrets screen each other only at the same height, so one of them dies first, which is fine.
- The underside band runs from the gondola-blocked limit (-0.94 − 0.08) down to the turret's lower reach (-1.25 − 0.08).
- These figures match B021 and the regression test `Zeppelin_EveryTurret_HasAClearHorizontalLineOfFire` (min 0.25).

Practical reachability:
- W/S moves the plane 5 × 0.02 = 0.10 per FixedUpdate. Fixed timestep: TimeManager 2822399/141120000 = 0.02 s.
- With no input the plane holds level (`target.y += v·speed·dt`, v = 0), so a 0.31 band is about 3 steps wide and can be held.

Tunnelling:
- Per step, the bullet moves 14 × 0.02 = 0.28 and the zeppelin 0.02.
- The narrowest collider the bullet must not skip is the turret: 0.50 + 2 × 0.08 = 0.66 > 0.30.
- So discrete triggers can't skip a turret or a gondola.

Terrain:
- In M5, `HeightAt(420)` = -4.0, so the hull is at -4.0 + 4.9 = 0.9.
- The Start clamp is maxY = 0 + 5 − 1.3 = 3.7, so 0.9 isn't clamped.
- Underside turret: centre at world -0.20, hitbox bottom at -0.35.
- The highest ground on x 420→360 is -3.667 at x 360, so the gap is **3.32** (≥ 1.5).

## Kill budget in mission 5 (Balance.json)

| Target | Hit points | Bullets (25 each) | Bombs (100 each) | Time at fireRate 8/s |
|---|---|---|---|---|
| Each turret | 75 | 3 | 1 | 0.375 s |
| 3 turrets | 225 | 9 | 3 | 1.1 s |
| Hull (after the shield drops; `hullVulnerableEarly` false) | 1000 | 40 | 10 (only 7 carried) | 5.0 s |
| **Total** | | **49 bullets** | | **about 6.1 s of trigger time** |

- The window is 60 s from the spawn to the HQ loss.
- Bullet range: 14 × 3 s lifetime = 42 units, more than the screen width.
- The pool holds 40 bullets. At 8/s with a 3 s lifetime, at most 24 are in flight, so it never runs out.
- The shield and kill sequence (3 + 3 + 3 turret bullets, then 40 hull bullets, each event raised once) is covered by `Zeppelin_ShieldUntilLastTurret_ScoresEachOnce`.
- Verdict: **the shield can drop and the hull can be killed** with the player's weapons, well inside the window.

## Spawn trigger and HQ-vs-map-end, recomputed

- Trigger: `MissionRunner.Update`, zeppelin trigger = atX − (5·a + `HalfLength` 4.5 + 0.5). It fires on `FarthestX`.
- Right edge at the trigger: trigger + 5a = **415.0** at every aspect. The hull's left end is 420 − 4.5 = 415.5, so the gap is **0.5**: the hull flies in, no pop-in.
- HQ time: 60 / 1.0 = **60.0 s**. `ZeppelinAI.FixedUpdate` moves x only, and `ReachedTarget` is x ≤ 360.
- Map end: `CheckMapEnd` fires at camera x ≥ 540. The camera's top speed is 2.0 (`MoveTowards` toward ±scrollSpeed, no overshoot).

| Aspect | Trigger (camera x) | Fastest to map end | Margin over the HQ loss |
|---|---|---|---|
| 4:3 | 408.33 | 65.83 s | **5.83 s** (binding) |
| 16:10 (960x600) | 407.00 | 66.50 s | 6.50 s |
| 16:9 | 406.11 | 66.94 s | 6.94 s |
| 21:9 | 403.33 | 68.33 s | 8.33 s |
| 2560x1080 | 403.15 | 68.43 s | 8.43 s |

- Escorts trigger at camera 424 and 429, at least 15.7 after the hull at every aspect, so `FindEscortTarget` always finds the hull.
- The required target spawns before the map end at every aspect (trigger ≤ 408.3 < 540).
- These figures match B021's handoff and the tests `M5_ZeppelinReachesTheHQ_BeforeTheCameraCanReachTheMapEnd` and `M5_Zeppelin_SpawnsWhollyBeyondTheRightScreenEdge`.

## GDD conformance (Missions 5, Zeppelins)

| GDD rule | Status |
|---|---|
| "Disable the turrets by hitting them" | Yes: every turret has a horizontal line of fire. |
| "Takes several hits" | Yes: 40 hull bullets after the shield drops. |
| "Fighter escort" | Yes: 5 escorts follow the hull. |
| "Destroy it before it reaches our HQ" | Yes: failAtX 360 = the HQ position, and the loss comes before the map end. |
| Controls | Unchanged by B021. |

## Observations (no bug)
- The underside turret now hangs partly below the hull sprite canvas (px y 200..240 of 220). A-ZEP-PYLON (art) adds the mount strut. Re-check it visually after that art task, under the playtest item M5-2.
- `bash tools/unity.sh test` still has to be run once the editor is closed. All QA-written zeppelin and M5 rule tests are expected green by code reading.

## Final manual playtest checklist: mission 5 (M5 exit)

Setup:
- Unity → Biplanes → BuildScenes, then Play Game.unity with Gizmos on (the collider boxes are visible).
- Run at 16:9, 16:10 (960x600) and 4:3.
- Until T937 lands, read win and lose results in the Console.

Start and flight:
- M5-1. **Start.** Airborne start at x -6, 3 above the terrain, facing right (`onRunway` false). Check:
  - The camera scrolls at 2.0.
  - W/S climb and dive, A/D turn with eased scroll reversal, Space fires, Enter drops a bomb.
  - The plane holds level with no W/S input.
- M5-2. **Zeppelin entry.** At camera about 406–408, the zeppelin flies in from beyond the right edge. Check:
  - The nose isn't visible at the spawn frame.
  - It sits at mid height (hull centre about world 0.9), with every turret on screen and well above the ground.
  - The underside turret hangs below the gondolas, and the art pylon connects it once A-ZEP-PYLON is in.
- M5-3. **Escorts.** At camera 424/429, 5 fighters close on the hull, not on a turret, and keep escorting after a turret dies.

Turrets, shield and hull:
- M5-4. **Turret fire.** The turrets lead the player with flak, staggered (0.8 s apart, then every 2.5 s each). A hit adds 12% damage.
- M5-5. **Underside turret.** Fly level at world y about -0.2 to -0.4, just under the gondolas. Fire from the left, then from the right: 3 bullets kill it. The gondolas must not eat bullets in that band.
- M5-6. **Top turrets.** Fly at world y about 1.8–2.1: 3 bullets each. A bomb dropped onto a top turret kills it in 1.
- M5-7. **Shield.** Before the last turret dies, hull hits do nothing: the bullet vanishes and the hull takes no damage. After the last turret dies, 40 hull bullets → explosions along the hull, +2500, **Won once**. Each turret also scores +200, once.

Damage and pickups:
- M5-8. **Smoke and death.** Take flak until 70% damage → a smoke trail appears. Reach 100% → crash → **Lost**.
- M5-9. **Health balloons** at x 200 and 320: flying through one heals 30%, and bullets pass through it.
- M5-10. **AAA** at x 120/126, 235–243 and 385/390: bombs only. Bullets don't damage them.

Lose paths:
- M5-11. **HQ loss.** Leave the zeppelin alone and keep flying right. About 60 s after it appears you get Lost with "Objective failed: Destroy the enemy zeppelin (the zeppelin reached its target)". This must come **before** camera 540, at every aspect.
- M5-12. **Shadowing.** Turn back and shadow the zeppelin: the same HQ loss at x 360, and input and scroll stop. Outrunning it never despawns it.
- M5-13. **Ramming.** Ramming the hull, a gondola or a turret crashes the player once.

Menu and WebGL:
- M5-14. **Pause and menu.** Pause and resume during the zeppelin fight. Retry and Next don't leak pooled bullets or zeppelins (Hierarchy count stays stable).
- M5-15. **WebGL.** Run in Chrome, Firefox and Safari at 960x600 and check:
  - The same entry, the turret kills from the under-gondola band, and the timings.
  - The escorts follow the hull.
  - Audio and input work after the first click.
