# T959 verify: B041 (reinforcement cap rule) and V008 (M4/M5 health balloons, M5 AAA)

Scope: close the QA T958 findings: N1 and N2 (B041-mechanics) and BUG 4 (V008-mission-design).
Date: 2026-11-18. Unity has the project open, so the tests were compile-checked, not run.

## Verdict: PASS (0 P0, 0 P1, 3 P2 still deferred)

N1, N2 and BUG 4 are fixed. The only open bugs are the P2s the orchestrator deferred
(T957 BUG 8, 10 and 12).

## Commands

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes (+2 new files not yet in the csproj), PASS Biplanes.Editor, **exit 0** |
| Tests scratch build (B018 method): runtime and editor DLLs from compile_check's helpers, temp copy of Biplanes.Tests.EditMode.csproj retargeted v4.7.1 -> v4.7.2, all **29** `Tests/EditMode/*.cs` listed explicitly | **0 errors**. Temp csproj removed, no `zz_*` left in Biplanes/ |
| `bash tools/unity.sh test` | Not run: the editor has the project open |
| `python tools/validate_missions.py` | OK for all 5 missions |
| `python tools/style_loop audit` | 46 artifacts, **0 violations** |
| Static rules | No `rb.velocity` and no `UnityEngine.Input` in the touched files. EnemyPressure.cs has one public class (the enum is nested), and its namespace Biplanes.Missions matches the folder. No hand-written YAML or .meta |

## N1: cap rule (EnemyPressure.cs:150-160, 272-277)

`CountsTowardCap(x, relVelX, view, range)`:
1. Outside `IsNear(range)` (1 screen width past either edge): it never counts.
2. Hitbox overlaps the view horizontally (x ± FighterHalfWidth 0.75): it always counts, whatever its velocity.
3. Left of the view: it counts only if `relVelX > 0.1`. Right of the view: only if `relVelX < -0.1`.

Checked against the B040 escape test, `IsEscaped` (lines 315-319):
- Left side: escaped is `relVelX <= ClosingSpeedEpsilon`, and "counts" is `relVelX > ClosingSpeedEpsilon`. These are exact complements.
- Right side: escaped is `>= -eps`, and "counts" is `< -eps`. Also exact complements.
- Both use the same constant (0.1) and the same frame. In `CountLiveFightersNear` and in `HandleEscapedFighters`, relVelX is `f.WorldVelocityX - ScrollController.Instance.Velocity`, or 0 if there is no ScrollController.

So an off-screen fighter counts exactly when it is not on its way to escaping. A fighter that holds its distance or recedes never counts. Dead fighters and Unity-null entries are skipped (line 156).

Escorts and static fighters report `WorldVelocityX` 0, so off screen they count only when the camera is closing on them. That is acceptable: an escort's real speed is its target's.

Tests in EnemyPressureCapTests.cs (10). I checked every assert by hand:
- On screen (with edge overlap): counts.
- Off screen and closing: counts. Receding: doesn't.
- Holding distance or inside the epsilon: doesn't count.
- Beyond range: doesn't count.
- Camera moving (the M1 and M3 cases): correct. Example: camera -3 with a fighter at -2 on the left gives rel +1, which counts.
- Camera pinned: correct.
- 4 ghosts no longer block a wave of 2: correct.

No regression:
- `WaveSize`, `IsNear`, `IsEscaped`, `DecideEscape`, the margins and the entry helpers are unchanged.
- No test in EnemyPressureTests or EnemyPressureFixTests calls `CountLiveFightersNear` or depends on the old count. They only use `WaveSize` and `IsNear`, which are unchanged.
- ARCHITECTURE §3 already has the B041 heading and the cap sentence (lines 299 and 310-313).

## N2: FighterAI.cs class comment

Line 24 now reads "...which points along its flight direction (left or right, FlightDirection)". **Fixed.**

## BUG 4: balance (same method as T957/T958)

Method:
- Fighters: about 1.3 shots each × 5% × p, where p = 0.25 when dodging or 0.4 when dueling.
- AAA: about 18% each. Turrets: about 12% each.
- Budget: 100% + 30% per balloon. Balance `healthBalloonRestore` is 30.

| Mission | Fighters | AAA | Turrets | Expected p=.25 / .4 | Balloons | Budget | Headroom (dodging) |
|---|---|---|---|---|---|---|---|
| M4 | 48: 78 / 125 | 5: 90 | 0 | **168** / 215 | 4 (140, 230, 320, 380) | 220 | **+52 (31%)** |
| M5 | 62 + 5 escorts = 67: 109 / 174 | 5 (120 ×2, 235 ×3): 90 | 3: 36 | **235** / 300 | 6 (150, 200, 255, 290, 320, 385) | 280 | **+45 (19%)** |

Both missions are survivable on paper with headroom when the player dodges. When dueling, M4 breaks even (215 vs 220) and M5 is over (300 vs 280). V008 documents that as intended for the hardest missions, and it fits GDD §2 "each harder than the one before". These are estimates, so confirm them in the playtest.

Placement:
- **M4:**
  - 140 comes after the AAA at 100-108.
  - 320 comes after the AAA at 280-283.
  - 230 and 380 were already there.
  - No balloon overlaps a barrage-balloon group (40, 180, 400).
- **M5:**
  - 150 comes after the AAA at 120-126.
  - 290 comes after the 235-243 peak.
  - 385 replaces the removed AAA, right before the zeppelin trigger.
  - Fighters arrive every 11 x throughout, so there are no true sky lulls, but each balloon follows a ground-fire beat.
- **No duplicates:** every balloon entry has count 1 and a unique (x, y).

Mission 5 invariants:
- HQ `hq_01` is at atX 360, and failAtX is 360.
- Zeppelin `airship_01` is at atX 420, y 4.9, pattern straight.
- Escorts: 3 at 424 (spacing 1.5) and 2 at 429 (spacing 1.0), with `escortOf` airship_01. Unchanged.
- The git diff of mission_05.json, apart from fighters, only adds the 4 balloon lines and removes `{atX 385, aaa, count 2}`.

Objectives:
- M5's only objective is `destroy_zeppelin` (destroyById airship_01, failAtX 360). No objective or test needs the AAA at 385:
  - `BombOnlyRequiredTargets_FitTheBombBay` counts AAA only for destroyAllOfType aaa, and M5 has none.
  - The M5 failAtX, HQ, escort and zeppelin tests don't read AAA.
- The M4 objectives (pop_all_balloons, bomb_base enemy_base_01) are unchanged.

The briefings, the terrain and the mission docs (mission_04.md, mission_05.md) match the JSON.

## Tests added (Tests/EditMode/MissionDataRulesTests.cs, existing file with .meta)

- `AllMissions_HealthBalloons_NeverStackAtTheSameSpot`: each health balloon entry has count 1, no two share (x, y), and every mission has at least one. This guards T957 BUG 9 and V007.
- `M4AndM5_HealBudget_CoversTheV008PaperEstimate`: the restore value is 30. M4 has 4 or more balloons and at most 5 AAA. M5 has 6 or more balloons and at most 5 AAA. Each budget is above the dodging estimate (168 and 235). This guards BUG 4 against later edits to the mission data.

## Bugs

No new bugs. These are carried over, still open and deliberately deferred:

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
WHERE: AAAGun, BomberAI, EnemyBase, FighterAI, PlayerHQ, StaticObstacle, ZeppelinAI, ZeppelinTurret, PlayerPlane
STATUS: open, deferred by the orchestrator.
```

```
BUG 12 (T957): New assets and scripts not imported yet (no .meta)
PRIORITY: P2 polish (process)
OWNER: user (Unity focus)
WHERE: player_bomb.png, EnemyPressure.cs, BombBallistics.cs, Tests/EditMode/{AAAGunBulletTests, BombBallisticsTests, EnemyPressureTests, EnemyPressureFixTests, EnemyPressureCapTests (new, B041), WhiteOnlyAndPressureQaTests}.cs
STATUS: open. Focus Unity once, then commit the generated .meta files.
```

Count: P0 0, P1 0, P2 3 (BUG 8, 10, 12).

## Playtest

Use the full checklist in docs/qa/T958-verify.md, section "Playtest checklist". It is still valid. Also check these:
- [ ] Run `bash tools/unity.sh test` with Unity closed, or run the Test Runner (EditMode). All tests pass, including EnemyPressureCapTests and the 2 new MissionDataRulesTests.
- [ ] M3/M4 (N1): let a reinforcement pass you, then turn and chase it at matched speed. Over the next 10-20 s, the sky still refills within about 2.2 s whenever it is empty.
- [ ] M4: 4 health balloons (140, 230, 320, 380). Dodging play finishes with damage under 100%.
- [ ] M5: 6 health balloons (150, 200, 255, 290, 320, 385). AAA only at 120 and 235, and none near 385. Dodging play reaches the zeppelin with damage to spare. The zeppelin still loses the mission at the HQ (x 360).
