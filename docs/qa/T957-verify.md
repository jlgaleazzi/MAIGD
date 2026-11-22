# T957 verify: difficulty, parabolic bombs, AAA 5 shots, white only, 3x1 bullets

Scope: B035, V005, V006, B037, A-BOMB, B036, A-WHITE, B038a, B038b, B039.
Date: 2026-11-18. Unity has the project open, so the tests were compile-checked, not run.

## Verdict: FAIL (1 code P1, 3 design/balance P1, 8 P2)

The code compiles, the broken tests are fixed, and the art, palette and data checks pass.
The FAIL comes from four P1s:
- An InvalidCastException in EnemyPressure whenever a bomber is alive (mission 2).
- Mission 1 is lost if even one of its 37 three-hit fighters gets away.
- Reinforcements enter behind the player when facing left, and in M3/M4 they never arrive.
- Missions 4 and 5 look unsurvivable on paper.

## Commands

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes (+2 new files not yet in the csproj), PASS Biplanes.Editor, exit 0 |
| Tests scratch build (B018 method: runtime DLL from compile_check helpers, temp copy of Biplanes.Tests.EditMode.csproj retargeted v4.7.1 -> v4.7.2, all 27 `Tests/EditMode/*.cs`) | Before my fixes: 2 errors (HudBombIconsTests 70, 92: `HudView.FriendlyCol`). After: **0 errors**. No temp files left in the project |
| `bash tools/unity.sh test` | "Unity not available" (editor open). Tests not run |
| `python tools/validate_missions.py` | OK for all 5 missions, no warnings |
| `python tools/style_loop audit` | 46 artifacts, 0 violations |
| `python tools/rasterize.py --check` | 41/41 OK + 4/4 OK |
| Static rules | No `rb.velocity`, no `UnityEngine.Input`. New files: one public class each, and namespaces match the folders. No hand-written YAML. player_bomb.png, EnemyPressure.cs, BombBallistics.cs and the new tests have no .meta yet; Unity imports them on the next focus |

## Tests fixed and added (Tests/EditMode)

- **HudBombIconsTests.cs**
  - `FriendlyCol` is now `BombAvailableCol` at line 70; spent icons are now `BombSpentCol` at line 92.
  - New tests:
    - `BombColours_AreInkAndHalfPencil`.
    - `DamageBelowSmoke_SolidInk_NoFlash_AtSmoke_HatchedAndFlashing`: 69 / 70 / 50, sprite swap, SmokeWarning, alpha 1.
    - `FlashAlpha_StartsAtOne_DipsToPoint4_At3Hz`.
    - `EveryHudGraphic_IsInkPencilPaperOrNoTint`.
- **EnemyFireTests.cs**: the hostile-tint test is renamed `..._InkDotGapDot`. It now asserts:
  - The renderer colour is white.
  - The sprite is 3x1 at 100 ppu.
  - Pixels 0 and 2 are opaque ink, and pixel 1 has alpha 0.
- **EnemyWeaponsAndPoolTests.cs**: the AAA trigger test is renamed `AAAGun_TriggerCollider_BulletsChip_FlakIgnored_BombKills`. It asserts:
  - A bullet does 25 / enemyHealth.aaa x 100 (20%).
  - Flak still does nothing.
  - One bomb still kills.
- **New WhiteOnlyAndPressureQaTests.cs**:
  - Player bullet Awake gives a 3x1 solid ink sprite at 100 ppu, point filtered and untinted.
  - `player_bomb` loads at 32x14 and 100 ppu. This needs the Unity import first.
  - Balance: a fighter takes 3 bullets and an AAA gun takes 5.
  - An AAA gun survives 4 Balance bullets and dies on the 5th.
  - Two EnemyPressure tests with a live BomberAI in `EnemyAircraft`. **These fail until BUG 1 is fixed.**
- Grepped the tests for other stale assumptions: 1-hit fighters, accent colours (E4572E/F2C14E/7BD389, Hostile/Friendly/Heal), `gravityMultiplier` or the old 12x fall, and 6x2 or 32 ppu bullets. Nothing else is stale:
  - AudioManagerQaTests:403 "OneHitPlayer" builds its own Health.
  - EnemyFireTests:335 kills a fighter with the full enemyHealth.fighter.
  - BomberHitboxTests:183 uses one bulletDamage against the bomber.
  - FxManagerTests checks alpha only.

## GDD conformance

| Rule | Evidence | OK? |
|---|---|---|
| A fighter dies to exactly 3 player bullets | Balance fighter 75 / bulletDamage 25. In float32 the 3rd hit lands at exactly 100.0, and 2 hits give 66.7. `Health.ApplyDamage` uses enemy mode. Tests: EnemyPressureTests and WhiteOnlyAndPressureQaTests | yes |
| AAA dies to exactly 5 bullets or 1 bomb | AAAGun.cs:68-98: Bomb sets 100, Bullet adds dmg/125*100 with a 99.999 snap, Flak is ignored. Direct hits and ground bursts both call ApplyDamage(Bomb) (Bomb.cs:200, 230) | yes |
| Pressure: 2 s quiet gives 2 fighters | EnemyPressure.Update/Step/WaveSize. Balance `pressure` is 2 / 2 / 6 | yes, except BUG 1 and BUG 3 |
| Pressure: never on the runway, while paused, or after the objectives | Paused: the timer holds. Runway, crash and `ObjectivesOpen == false`: it resets (IsEligible) | yes |
| Pressure: cap 6 near the camera | CountLiveFightersNear counts within one screen width beyond either edge | yes |
| Reinforcements score but don't advance destroy-all | Named `fighterReinforcement`. ScoreKeeper.cs:130 maps it to score.fighter. ObjectiveTracker.SameType is an exact, case-insensitive match. Tested | yes |
| Bombs: full plane velocity, gravity 10, drag 0.15, nose follows velocity | PlayerPlane.cs:684-685 (`_velocity * inheritFactor 1.0`), BombBallistics.Step, Bomb.FixedUpdate and ApplyRotation, Balance `bomb` block | yes |
| Bombs: ground burst and direct hits still work | Bomb.Update/ImpactPoint/BurstDamage are unchanged and OnHit is unchanged. The worst fall (9 u, climbing 5 u/s) takes about 1.9 s, under the 3 s Projectile lifetime | yes |
| player_bomb sprite | 32x14 PNG, `Resources.Load<Sprite>("Sprites/player/player_bomb")` with a placeholder and one warning. **No .meta yet**: until Unity imports it, bombs show the placeholder | yes, once imported |
| White only in Scripts/ | No F2C14E/E4572E/7BD389. The only `Color.red/yellow/green` is the AAAGun.cs:365 Gizmo (`#if UNITY_EDITOR`, OnDrawGizmosSelected, editor-only: OK). Every `new Color` literal is ink, pencil, paper or one of those with alpha. Leftovers in BUG 10 | yes |
| Bullets are 3x1 line bursts | Bullet.cs:95-106 and EnemyBullet.cs:157-172. Colliders are still r 0.08 | yes |

## Missions

- validate_missions passes for all 5 missions.
- Fighters: M1 37, M2 29 (3 escorts), M3 46, M4 48, M5 67 (5 escorts).
- M2: bomber_1 has failAtX 10, and the bomber is at atX 80. M5: airship_01 is at x 420, the HQ is at 360, and failAtX is 360. MissionDataRulesTests covers both. Neither changed.
- Live fighters: I ran 300 random runs per mission of the real placement rules, uncontested, flying right the whole time, with scratch `sim.py`. At most 4 / 5 / 4 / 2 / 5 fighters are on screen at once in M1-M5, always under the cap of 6. Counting one screen width beyond either edge (the cap range), M1-M3 peak at 7. That only holds back reinforcements; it caps nothing in the JSON.
- **M1 destroy-all (BUG 2):** fighters never despawn and always fly left at 3 u/s.
  - Facing left, the camera only scrolls at 2 u/s, so a fighter that gets past the left edge can never be caught.
  - The last wave (atX 252, 5 dive) is on screen around camera x 265-275, well before 300, so the timing works if nothing escapes.
  - But one escapee means `CheckMapEnd` (MissionRunner.cs:669) loses the mission at x 300, with no way to recover.
- **Duplicate health balloons (BUG 9):** M2 has two at 135, and M3 has two at 180 and two at 380, each pair at the same x and y. The mission sheets list one at each spot.

## Balance on paper (fighterBullet 8%, flak 15%, gunner 10%, turret 12%, balloon +30)

Model:
- A fighter passing uncontested is inside range 11 for about 2.2 s (closing speed 3 + scroll).
- Its first shot comes after 0.4-1.2 s, then every 1.8-2.5 s. That gives about 1.3 shots per fighter, or about 0.6 if the player kills it within about 1 s.
- p is the chance a shot hits. Fighters meet the player head-on in the left third of the screen, so p is about 0.25 (dodging) to 0.4 (dueling).
- I estimate AAA at about 18% each while it is passed (4 shells x 0.3 x 15).

| Mission | Fighters | Fighter dmg p=.25 / p=.4 | Other threats (est.) | Heal (JSON) | Budget (100 + heal) | Verdict |
|---|---|---|---|---|---|---|
| M1 | 37 | 96% / 154% | 3 AAA (optional) ~0-50% | 1 = 30 | 130 | tight. Combined with BUG 2: P1 |
| M2 | 29 | 75% / 121% | bomber gunner ~40% | 3 = 90 (60 intended) | 190 | survivable |
| M3 | 46 | 120% / 191% | 5 AAA ~90% | 5 = 150 (90 intended) | 250 | marginal |
| M4 | 48 | 125% / 200% | 5 AAA ~90%, 6 barrage balloons | 2 = 60 | 160 | **P1, unsurvivable on paper** |
| M5 | 67 | 174% / 279% | 7 AAA ~126%, 3 turrets | 3 = 90 | 190 | **P1, unsurvivable on paper** |

Compared with the previous round: M4 had 9 fighters and M5 had 15, so fighter exposure is about 5x and 4x. These are estimates, not playtest results.

## Bugs

```
BUG 1: EnemyPressure casts BomberAI to IDamageable -> InvalidCastException every frame (M2)
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Missions/EnemyPressure.cs:114 (registered at MissionRunner.cs:289)
STEPS: Static. MissionRunner.RegisterAircraft(bomber) adds BomberAI to EnemyAircraft.
  BomberAI is `class BomberAI : MonoBehaviour` (BomberAI.cs:33), not IDamageable.
  AnyEnemyAircraftInView does `((IDamageable)body).IsDead`. While bomber_1 lives, any frame
  where no aircraft earlier in the list is in view throws. Update aborts before Step, so no
  reinforcements come, and the console fills with errors every frame (costly in WebGL).
  Test: WhiteOnlyAndPressureQaTests.Pressure_InViewScan_* (fail until fixed).
EXPECTED: GDD "Enemy pressure" and ARCHITECTURE §3 Enemy pressure: a bomber counts as an enemy aircraft in view, and the timer runs.
ACTUAL: InvalidCastException. M2 gets no pressure net while the bomber lives (V005 relies on it for the thin tail after x 170).
FIX HINT: `body is IDamageable d ? d.IsDead : (body.GetComponent<Health>()?.IsDead ?? false)`.
```

```
BUG 2: Mission 1 is lost if any one of the 37 fighters escapes off the left edge
PRIORITY: P1 mechanic wrong vs GDD (mission effectively unwinnable for most players)
OWNER: mission-design + mechanics (design call for orchestrator)
WHERE: mission_01.json (37 fighters, destroyAllOfType fighter); FighterAI.cs:179 (no despawn and no return); MissionRunner.cs:669 (map-end loss)
STEPS: Let one fighter pass. It flies left at 3 u/s. Turning back only scrolls the camera
  at 2 u/s (min x 0), so it can never be reached. The objective stays at 36/37 and the
  camera reaching x 300 loses the mission.
  Each fighter needs 3 hits within about 3.9 s on screen, with 3-4 alive at once.
EXPECTED: GDD Missions 1, "destroy every incoming enemy airplane" stays achievable. GDD 2026-11-18 raises difficulty, it doesn't make a single miss fatal.
ACTUAL: One miss = guaranteed loss at the map end, with nothing on the HUD to say so.
OPTIONS: fighters that are far behind loop back in from the right, or escaped fighters are dropped from the required count, or the objective becomes "destroy N of 37", or M1 goes back to about 12-15 fighters with EnemyPressure filling the gaps.
```

```
BUG 3: Reinforcements always enter at the RIGHT edge, behind a left-facing player; in M3/M4 they never arrive
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mechanics
WHERE: MissionRunner.cs:398 (SpawnReinforcementFighter x = camera right edge + 1.5 + ...)
STEPS: In M3 or M4 (scrollSpeed 3), face left (A) for over 2 s, for example to go back to a
  missed AAA. A wave spawns off the right edge. Fighters fly left at 3 u/s and the camera
  moves left at 3 u/s, so they never enter the view. The timer keeps hitting 2 s and more
  waves stack up off screen until the cap of 6. When the player turns right, all of them
  arrive together. In M1/M2/M5 they catch up at only 0.5-1 u/s, taking 2-7 s.
EXPECTED: GDD "Enemy pressure": reinforcements enter "ahead of the player", and the sky is never empty for more than 2 s.
ACTUAL: Empty sky for as long as the player flies left in M3/M4, then a burst of up to 6 fighters.
FIX HINT: spawn past the edge the player faces (ScrollController.Direction), flying toward the player. Or, with fighters only able to fly left, at least don't count the quiet time while Direction < 0.
```

```
BUG 4: Missions 4 and 5 look unsurvivable on paper with 3-hit fighters at 4-5x the count
PRIORITY: P1 mechanic wrong vs GDD (balance; confirm in playtest)
OWNER: mission-design (balance proposal) / mechanics (Balance.json)
WHERE: mission_04.json (48 fighters, 2 health balloons), mission_05.json (67 fighters, 3 health balloons); Balance damageTaken.fighterBullet 8
STEPS: See the balance table above. Expected fighter damage alone is 125-200% (M4) and
  174-279% (M5), against budgets of 160% and 190%, before AAA and turrets.
EXPECTED: GDD §2 "5 missions, each harder than the one before": hard but survivable.
ACTUAL: On paper the player dies before the end of M4/M5 unless they dodge almost every shot.
LEVERS: fighterBullet 8 -> 5; +1-2 health balloons in M4/M5; fighterInterval 1.8-2.5 -> 2.5-3.2; or V006's own suggestion (step 12-13 and let EnemyPressure fill the gaps).
```

```
BUG 5: FighterAI still documents one-hit kills and falls back to 25 hp
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/FighterAI.cs:16 and :124
STEPS: Static. The class doc says "one player bullet kills it", and the Awake fallback is `25f` when Balance is missing.
EXPECTED: GDD Enemies 1: three hits. The fallback should be 75.
ACTUAL: Stale doc. Without Balance, fighters would die to 1 bullet.
```

```
BUG 6: JSON fighters never despawn (up to 67 live FighterAI per mission)
PRIORITY: P2 polish (performance, mainly WebGL)
OWNER: mechanics
WHERE: FighterAI.cs:179 FixedUpdate; MissionRunner keeps them in _enemyAircraft
STEPS: Static. Only reinforcements are removed 20 u behind the camera. Escaped JSON fighters fly left forever, running FixedUpdate, MoveRotation and the pitch maths.
EXPECTED: ARCHITECTURE §3: nothing heavy in the update loop.
ACTUAL: The number of live objects grows through M3-M5. Fix this together with BUG 2.
```

```
BUG 7: Reinforcement arrival can exceed the 2 s rule by about 0.3-1.2 s
PRIORITY: P2 polish
OWNER: mechanics
WHERE: MissionRunner.cs:398-399 (spawn at right edge + 1.5 + 2*i + rand 0..2)
STEPS: Static. The wave spawns when the quiet time reaches 2.0 s, but 1.5-5.5 u off screen, closing at 5-6 u/s.
EXPECTED: GDD "never more than 2 seconds without an enemy aircraft on screen".
ACTUAL: About 2.3-3.2 s of empty sky. Spawn just past the edge, or trigger at maxQuietSeconds minus the lead time.
```

```
BUG 8: Dead placeholder: PlayerPlane builds a 6x2 bullet sprite that Bullet.Awake replaces
PRIORITY: P2 polish
OWNER: mechanics
WHERE: PlayerPlane.cs:300
STEPS: Static. `_bulletSprite = CreateSolidSprite(Color.white, 6, 2, 32f)` is overridden in Bullet.Awake (B039).
EXPECTED: No dead code.
ACTUAL: One unused texture. Remove it, or pass Bullet's sprite.
```

```
BUG 9: Duplicate health balloon entries (same x and y) in M2 and M3
PRIORITY: P2 polish
OWNER: mission-design
WHERE: mission_02.json lines 25-26 (x 135); mission_03.json lines 24-25 (x 180) and 36-37 (x 380)
STEPS: Static. Each pair is identical (y 2.5 / 1.5, drift). docs/missions/mission_02.md and _03.md list one balloon at each spot.
EXPECTED: One balloon per listed position (V005 handoff: "M2 at 135, M3 at 180 and 380").
ACTUAL: Two overlapping balloons, +60% in one pass. Remove the extras, or document a deliberate double heal.
```

```
BUG 10: Leftover Color.white 1x1 fallbacks (pure #FFFFFF, not ink #E8F1FA)
PRIORITY: P2 polish
OWNER: mechanics
WHERE: AAAGun.cs:355, BomberAI.cs:410, EnemyBase.cs:152, FighterAI.cs:413, PlayerHQ.cs:84, StaticObstacle.cs:145, ZeppelinAI.cs:417, ZeppelinTurret.cs:250, PlayerPlane.cs:221 and :315
STEPS: Static grep. These are drawn only when a sprite fails to load. UI `NoTint = Color.white` and the white hatch/1x1 texels are identity multipliers, so they're fine. The AAAGun.cs:365 Gizmo is editor-only, also fine.
EXPECTED: ARCHITECTURE §3 Weapons: "No code may use a colour outside ink, pencil and paper."
ACTUAL: Near-white fallbacks. Cosmetic, and only when assets are missing.
```

```
BUG 11: Docs describe a spent bomb icon as a pencil OUTLINE and enemy tracers as "dashed pairs"
PRIORITY: P2 polish
OWNER: orchestrator (GDD Style Guide palette text, UI_SPEC §2) / art
WHERE: docs/GDD.md Palette paragraph; docs/UI_SPEC.md §2 bomb icons
STEPS: Static. HudView tints the filled ui_bomb_icon pencil at alpha 0.5 (no outline sprite exists). B039 made the enemy tracer a 3x1 dot-gap-dot burst.
EXPECTED: Docs match the build.
ACTUAL: GDD: "an empty one is a pencil outline", "enemy tracers are short dashed pairs". UI_SPEC: "pencil outline at 50% alpha".
```

```
BUG 12: New assets/scripts not imported yet (no .meta)
PRIORITY: P2 polish (process)
OWNER: user (Unity focus)
WHERE: Resources/Sprites/player/player_bomb.png; Scripts/Missions/EnemyPressure.cs; Scripts/Weapons/BombBallistics.cs; Tests/EditMode/{AAAGunBulletTests, BombBallisticsTests, EnemyPressureTests, WhiteOnlyAndPressureQaTests}.cs
STEPS: ls shows no .meta files.
EXPECTED: Unity generates the .meta files on import. Never hand-write them.
ACTUAL: Until Unity refreshes, bombs use the placeholder (with one warning) and PlayerBombSprite_Loads_32x14_At100Ppu fails. Focus Unity once, then commit the generated .meta files.
```

## Playtest checklist (user: Unity -> Biplanes -> BuildScenes, then Play)

Difficulty feel:
- [ ] M1: after takeoff, at most about 2-3 s without an enemy on screen. No waves while on the runway or while paused.
- [ ] A fighter dies on the 3rd hit, never the 2nd.
- [ ] M1: deliberately let one fighter pass, then fly to the end. Confirm the BUG 2 loss at x 300.
- [ ] M2: watch the Console while the bomber is alive. Expect InvalidCastException spam (BUG 1).
- [ ] M3: face left for 5 s. Do fighters appear? (BUG 3.) Turn right: do 4-6 arrive at once?
- [ ] M4/M5: note your damage at each health balloon and whether you finish. Report the % at the end (BUG 4).
- [ ] An AAA gun dies to 5 machine-gun hits (it greys from 4 hits), and to one bomb.
- [ ] Killing a reinforcement adds 100 but doesn't change the M1 objective count.

Bomb arc:
- [ ] Level flight: the bomb arcs forward and turns nose-down, and lands about 2-3 u ahead from mid height.
- [ ] Facing left: the bomb points left, fins up, and arcs left.
- [ ] Diving: it lands sooner and steeper. Climbing: it lofts first.
- [ ] The bomb sprite shows (not a white rectangle) after Unity imports player_bomb.png.
- [ ] A bomb on a ground AAA kills it (ground burst), and a direct hit on a fighter or bomber hurts it.

White-only readability:
- [ ] No yellow, red or green anywhere: HUD, menus, flak, health balloon, bullets, damage tint (the plane greys at 70%+).
- [ ] The 3x1 px player bullets are visible at 1080p and in a small WebGL canvas. The enemy dot-gap-dot tracers are distinguishable from player shots and readable in motion.
- [ ] The damage bar is hatched and flashing at 70% or more, in sync with SMOKE. It goes back to solid at <70% after a balloon.
- [ ] Spent bomb icons are clearly fainter than available ones.
- [ ] Pressed menu buttons fill with ink and the label turns paper. Win title: paper on an ink plate. Loss: plain ink.
- [ ] WebGL in Chrome, Firefox and Safari: bullets are still visible after canvas scaling.
