# T960 verify: V009 + V010 (extra health balloons, M1-M5)

Date: 2026-11-18. Unity has the project open, so the tests were compile-checked, not run.

## Verdict: PASS (0 P0, 0 P1, 1 new P2)

## Commands

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes, PASS Biplanes.Editor, exit 0 |
| Tests scratch build (B018 method: runtime DLL from compile_check helpers, temp copy of Biplanes.Tests.EditMode.csproj retargeted v4.7.1 -> v4.7.2, all **29** `Tests/EditMode/*.cs` listed) | **0 errors**, no `zz_*` left in Biplanes/ |
| `bash tools/unity.sh test` | Not run: the editor has the project open |
| `python tools/validate_missions.py` | OK for all 5 (exit 0) |
| JSON parse (python json) | all 5 parse; every `spawns` array is sorted by atX |

The runner does not rely on spawn order anyway: `MissionRunner.Update` walks every entry with a per-index `_spawnedFlags[i]` and fires on `FarthestX >= trigger` (MissionRunner.cs:680-689).

## Counts and positions

Spawn rule (MissionRunner.cs:256-257, 307-309): balloon at world `(atX, HeightAt(atX) + y)`, y = height above terrain; it triggers at `atX - (5*aspect + 2)` like other ground objects. HealthBalloon only bobs (no x motion).

| M | Balloons (atX:y) | Count | World y range | Nearest AAA/base/HQ/barrage x |
|---|---|---|---|---|
| 1 | 110:2.5, 170:1.5, 225:2.0, 280:2.5 | **4** | -1.50 .. -0.50 | 20 (110 vs AAA 130) |
| 2 | 65:2.5, 135:2.5, 195:2.5, 250:3.0, 320:2.5 | **5** | -1.48 .. -0.57 | none in mission |
| 3 | 180:1.5, 230:2.5, 280:1.5, 330:2.5, 380:1.5, 450:2.5 | **6** | -2.30 .. -0.13 | 10 (330 vs AAA 320) |
| 4 | 80:2.5, 140:2.0, 230:1.5, 320:2.0, 380:2.0, 445:2.5, 532:2.0 | **7** | -1.50 .. -0.15 | 15 (445 vs base 460) |
| 5 | 80:2.5, 150:2.0, 200:1.5, 255:2.0, 290:2.5, 320:2.0, 385:2.5, 470:2.5 | **8** | -1.75 .. -0.58 | 12 (255 vs AAA 243) |

- Every entry has count 1 and a unique x per mission, so no (x, y) is shared.
- Heights: all y are 1.5-3.0 above terrain. 1.5 is `MinPlaneHeight`, the bottom of the fighter band. The highest world y is -0.13, far below the band top (camera half height 5 - `PlaneTopMargin` 1 = 4). All are reachable.
- No balloon sits within 10 x of an AAA, base, HQ or barrage balloon. M3 330 is 10 x past the AAA at 320, inside its flak lane but not on it. That is intentional (the sheet says so).

## Nothing else changed

HEAD is several uncommitted tasks behind (V005-V008 and the briefings), so a raw `git diff HEAD` is not a clean baseline. I compared against the last QA'd state (T957/T958/T959 records):

| M | Now | Last QA'd | Same |
|---|---|---|---|
| 1 | 37 fighters, 3 AAA (130, 250 x2), kill_all_fighters | T958: 37, 3 AAA | yes |
| 2 | 29 fighters + bomber_1, destroy_bomber failAtX 10 | T957: 29 | yes |
| 3 | 46 fighters, 5 AAA, destroy_all_aaa | T958: 46, 5 AAA | yes |
| 4 | 48 fighters, 5 AAA, 6 barrage balloons (40, 180, 400), enemy_base_01 at 460, 2 objectives | T959: 48, 5 AAA | yes |
| 5 | 62 + 5 escorts, 5 AAA (120 x2, 235 x3), hq_01 at 360, failAtX 360, airship_01 atX 420 y 4.9, escorts 3 @ 424 / 2 @ 429 | T959 | yes |

- The titles, briefings and terrain match the T959 state. The file mtimes (22:10-22:11) are the V009/V010 writes only.
- Each sheet's "before" balloon list matches the T958/T959 records: M1 170; M2 65, 135; M3 180, 280, 380; M4 140, 230, 320, 380; M5 150, 200, 255, 290, 320, 385.
- The sheets in docs/missions/mission_01..05.md list exactly the JSON balloons.

## Heal budget (100% + 30% per balloon; `healthBalloonRestore` 30, unchanged)

| M | Expected dmg dodging / dueling (T958/T959) | Budget before -> now | Headroom dodging |
|---|---|---|---|
| 1 | 60 / 96 | 130 -> **220** | +160 |
| 2 | 87 / 115 | 160 -> **250** | +163 |
| 3 | 165 / 210 | 190 -> **280** | +115 (dueling also covered now) |
| 4 | 168 / 215 | 220 -> **310** | +142 (still +112 without 532) |
| 5 | 235 / 300 | 280 -> **340** | +105 (still +75 without 470) |

M4/M5 stay covered, and every mission now survives even the dueling estimate. Headroom no longer rises with mission number (M1-M2 about +160, M5 +105). The ramp now comes from the enemy count, not from the heal budget. That is the effect the user asked for, so I note it and don't file it as a bug.

## Tests

The existing tests still hold by inspection:
- `AllMissions_HealthBalloons_NeverStackAtTheSameSpot`: count 1 each, unique (x, y), at least 1 per mission.
- `M4AndM5_HealBudget_CoversTheV008PaperEstimate`: M4 7 >= 4, M5 8 >= 6, AAA 5 and 5, budgets 310 > 168 and 340 > 235.
- No other test reads balloon spawns or spawn indices.

Added to `Tests/EditMode/MissionDataRulesTests.cs`:
- `AllMissions_HealthBalloons_ClearOfGroundTargets_AndInsideTheFlightBand`: y >= `MinPlaneHeight` (reflection), world y <= 5 - `PlaneTopMargin`, atX < length, and at least 3 x from every AAA, enemyBase, playerHQ, building, tree or barrageBalloon entity (atX + spacing*i). It passes by hand computation against the data above.
- `AllMissions_HealthBalloonCount_AtLeastTheV009V010Plan`: M1-M5 have at least 4, 5, 6, 7 and 8 balloons.

## Bugs

```
BUG: M5 balloon at x 470 is not where the zeppelin fight happens
PRIORITY: P2 polish
OWNER: mission-design
WHERE: Resources/Missions/mission_05.json spawn {atX 470, healthBalloon}; docs/missions/mission_05.md:26
STATUS: open
STEPS: The zeppelin spawns at camera about 409 and flies LEFT from x 420 to the HQ at 360
  (1 u/s, 60 s). The 470 balloon only spawns once FarthestX >= about 459 and sits 50-110 x
  to the right of the hull for the whole fight. To take it, the player must fly about
  40+ x away from the zeppelin, against a 60 s timer, then come back.
EXPECTED: The sheet says "Recover mid zeppelin fight (hull at about x 420 minus travel)",
  so the balloon should sit in the fight area, about 365-415 (for example x 405, y 2.5,
  clear of the 385 balloon and the escorts' lane).
ACTUAL: It is a detour, not a mid-fight top-up. The budget still covers M5 without it
  (310 vs 235), so this is not a balance bug.
```

Info only, not bugs: M4 532 (after the base at 460) and M3 450 (after the last AAA at 420) appear only if the objective is still open at that point, meaning a missed bomb run or a skipped AAA. They help on a retry pass and are harmless.

Still open and deferred from earlier: T957 BUG 8, 10 and 12 (all P2).

## Playtest additions
- [ ] M1-M5: count the health balloons (4/5/6/7/8). None overlaps a gun, base, HQ or barrage balloon, and each is collectable at normal flight height.
- [ ] M3: the 330 balloon right after the AAA at 320 can be taken without eating flak every time.
- [ ] M5: during the zeppelin fight, is the 470 balloon ever worth the detour? (BUG above.)
- [ ] Note the end-of-mission damage. Missions should still feel harder from M1 to M5.
