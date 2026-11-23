# T968 verify: recharge bomb balloon (B064, B065, A064, V064)

**Verdict: PASS (static + data; Unity run pending)**: 0 P0, 0 P1, 1 P2. Every item in the
user's request is met in code, data and art. The Unity batch EditMode run could not happen:
the editor (PID 82684) holds `Biplanes/Temp/UnityLockfile` on this project, and two retries
aborted with "another Unity instance is running with this project open". The whole test
assembly, the 20 B064/B065 tests and QA's 8 new tests (20 cases) included, compiles with 0 errors
against the fresh runtime DLL.

## Runs
| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes (+1 new file not yet in the csproj: BombBalloon.cs), PASS Biplanes.Editor, exit 0 |
| Test assembly with dotnet (QA scratch script: the `compile_check` recipe on `Biplanes.Tests.EditMode.csproj`, TargetFramework 4.7.2 so NUnit resolves) | PASS, 0 errors, all test files including BombBalloonTests.cs and BombBalloonQaTests.cs |
| `UNITY_PATH=".../6000.6.5f1/Editor/Unity.exe" bash tools/unity.sh test` (x2) | **Not run: project locked by the open editor** (exit 1). `Logs/editmode-results.xml` is stale (2026-11-20 17:52, 544/544, contains no BombBalloon test). Its total is **not** this task's result. |
| `python tools/validate_missions.py` | 5/5 OK. Old duration warnings only (M3 128 s vs 210, M4 154 s vs 240) |
| `python tools/rasterize.py --check` | 42/42 OK, 5/5 blueprint OK |
| `python -m tools.style_loop audit` | pickups/bomb_balloon listed, 0 violations |
| Static | Namespaces match the folders (`Biplanes.Enemies`, `Biplanes.Player`, `Biplanes.Missions`). No `rb.velocity`, no `UnityEngine.Input`. No hand-written .meta/.prefab/.asset files (BombBalloon.cs, bomb_balloon.png and the test files have no .meta yet: the open editor has not re-imported). `BombBalloonData` lives in GameBalance.cs next to the other `*Data` classes; this follows the existing convention there, so it is not filed. |

## Acceptance vs the user's request
| Requirement | Evidence | OK |
|---|---|---|
| >= 2 recharge sources per mission: one placed mid-map | JSON bombBalloon M1 x150/300, M2 225/450, M3 250/500, M4 300/600, M5 272/580, all y 3.5, count 1. The validator errors if none is in the middle third. QA test `EveryMission_PlacesABombBalloon_InTheMiddleThird` x5 | yes |
| ...and one auto-spawned 4 s after the bay is empty | MissionRunner `OnBombsChanged(0)` arms `RechargeTimer(delay=4)`, and `UpdateBombRecharge` spawns when Due. Missed: `ShouldRearmRecharge` re-arms (B065), one balloon at a time | yes |
| +4 bombs, capped at 7 | `BombBay.AddBombs` -> `ClampedAdd(remaining, grant, capacity)`. Balance grant 4. Tests: 0->4, 5->7, 7->7 | yes |
| HUD bomb icons update | AddBombs raises `GameEvents.BombsChanged`. HudView.OnBombsChanged repaints. QA test `Hud_ShowsFour_ThenSeven_AfterTwoBalloonsFromAnEmptyBay` | yes |
| Auto balloon reachable: 12 ahead along facing, y in flight band | `AutoSpawnX(p.x, Facing, lead=12, MissionStartCameraX, length)`, `AutoSpawnY(p.y, terrain(x)+1.5, camTop-1)` (MinPlaneHeight 1.5, PlaneTopMargin 1). Static world object, straight-line flight reaches it. Tests on both helpers plus QA `AutoSpawn_UsesBalanceLead_AheadAlongFacing` | yes |
| Timer pauses with the game | Ticks with `Time.deltaTime` (0 at timeScale 0 in Paused). Test `Timer_ZeroDtDoesNotAdvance_PauseAndHold` | yes |
| Timer waits for the first key | `if (!HasStarted) return;` before any tick or re-arm (HasStarted = scroll not held, B049) | yes |
| Crash / mission end | Cancels when `_missionEnded` or `HasCrashed`. TearDown unsubscribes BombsChanged, clears the list and resets the timer | yes |
| No effect on objectives | Balloons are never in `_enemyAircraft`/`_groundEnemies`/`_idTargets`. `EntityDestroyed("BombBalloon", Neutral)` matches no `CountsAsKillOf`. QA test `CollectingABombBalloon_NeverAdvancesAnObjective` x5, `NoObjective_TargetsABombBalloon` x5 | yes |
| No effect on counters / score | HudView.OnEntityDestroyed returns for team != Enemy. ScoreKeeper `IsScorelessPickup` returns early, with no warning | yes |
| No effect on EnemyPressure | EnemyPressure reads `EnemyAircraft` only | yes |
| No effect on minimap | `MinimapView.IsEnemySpawnType("bombBalloon")` false. No marker pool reads `BombBalloons`. QA test | yes |
| Weapons ignore it | Team.Neutral: Bullet.cs:117 and Bomb.cs:196 skip Neutral (B011) | yes |
| White-only art | SVG uses only `#0F2A4A` (paper) and `#E8F1FA` (ink), no `<text>`. PNG 70x100, the same as health_balloon. No off-palette pixels above alpha 16. The SpriteRenderer tint is ink `#E8F1FA` | yes |
| Sprite path loads | `Resources/Sprites/pickups/bomb_balloon.png` exists. `SpriteImportSettings` (AssetPostprocessor) forces Sprite/Single/PPU 100 for every PNG under `Resources/Sprites/`, so `Resources.Load<Sprite>("Sprites/pickups/bomb_balloon")` resolves after import. If it doesn't, the code falls back to an ink 1x1 with a warning, not a crash. QA test `Sprite_LoadsFromResources_AtArtStyleSize` checks it in Unity | yes (import pending) |

## Bugs

```
BUG: validate_missions warning text still says "no refills"
PRIORITY: P2 polish
OWNER: mission-design
WHERE: tools/validate_missions.py:135-136
STEPS: A mission with more than 7 aaa/enemyBase spawns prints "N bomb targets but only 7 bombs and no refills".
EXPECTED: GDD "Player item": every mission now has bomb refills (a mid-map balloon + the auto balloon), so the message is wrong. Reword it (for example "more bomb targets than one bay; relies on recharge balloons"), or count the placed bombBalloons x grant toward the budget.
ACTUAL: The text is out of date. No mission triggers it today (max 4 targets, in M3).
```

## Observations (not bugs)
- "Again every 4 s if it is missed" (GDD) really means 4 s after the missed balloon has left the
  screen and is no longer ahead within 12 u (ARCHITECTURE B065). That matches the design note.
- Missed balloons are never recycled. They stay active in the world, and a player who turns back
  can still collect them. With one balloon at a time the count stays small.
- Near the map end, `AutoSpawnX` clamps to `length`, so the balloon can land closer than 12 u.
  The map-end objective check ends the mission there anyway.
- `tools/unity.sh test` prints the totals of the old `editmode-results.xml` even when Unity aborts
  on the lock, so they look like fresh results. Read the timestamp.
- `BombBay.Refill` summary says "e.g. health-balloon pickup". Health balloons only heal. The
  comment is out of date, but it's harmless.

## New QA tests: `Tests/EditMode/BombBalloonQaTests.cs` (8 tests, 20 cases)
- `EveryMission_PlacesABombBalloon_InTheMiddleThird` [1..5]
- `NoObjective_TargetsABombBalloon` [1..5]
- `CollectingABombBalloon_NeverAdvancesAnObjective` [1..5]
- `BombBalloon_IsNotAKillOfAnyEnemyType_NorAMinimapEnemy`
- `Sprite_LoadsFromResources_AtArtStyleSize` (70x100; needs the Unity import)
- `Spawn_IsANeutralTrigger_AndKeepsTheAutoFlag`
- `AutoSpawn_UsesBalanceLead_AheadAlongFacing`
- `Hud_ShowsFour_ThenSeven_AfterTwoBalloonsFromAnEmptyBay`

To run once the editor is closed (or from the Test Runner window in the open editor, EditMode):
`UNITY_PATH="/d/Program Files/6000.6.5f1/Editor/Unity.exe" bash tools/unity.sh test`. Expected
total = 544 + new tests since 11-20 (BombBalloonTests 20 + BombBalloonQaTests 20 cases + others),
with 0 failures. Also check the regressions in HealthBalloonFullHealTests and HudBombIconsTests.

## Manual playtest checklist (each of M1-M5)
- [ ] Mid-map bomb balloon visible at about x = length/2, bomb mark readable, distinct from the health cross.
- [ ] Fly through it at 5 bombs: the HUD shows 7. At 7: the balloon is consumed, and 7 remain.
- [ ] Drop all 7. After 4 s a balloon appears about 12 u ahead at the plane's height. Fly straight: it is collected, and the HUD shows 4.
- [ ] Pause (Esc) during the 4 s: no spawn while paused, and the countdown resumes after.
- [ ] Miss the auto balloon (climb over it): about 4 s after it leaves the screen, a new one appears ahead. Never two at once.
- [ ] Face left (A) with an empty bay: the balloon spawns to the left.
- [ ] Shooting or bombing a bomb balloon does nothing. Score and objective counter unchanged on pickup. No minimap marker.
- [ ] No "not found in Resources" warning in the Console (the sprite loaded).
