# T962 verify: data-driven ObjectiveTracker tests

VERDICT: PASS. The full EditMode suite passes: 418/418 in a real Unity batch run with Unity 6000.6.5f1 and the editor closed.

## The failures and their causes
| Test | Cause | Who fixed it |
|---|---|---|
| DestroyAllOfType_M1_CompletesOnThe12thFighterOnly_Once | Stale data. M1 now spawns 37 fighters, not 12 (V005-V011). | QA (test), renamed `..._CompletesOnTheLastJsonFighterOnly_Once` |
| DestroyAllOfType_RequiredCount_IsTheSpawnTotal_PerMission | Stale data. The test hard-coded 12, 5 and 6. | QA (test) |
| Runner_Next_OneSubscriptionEach_AndKillsCountedOnce_M1WinsOnce | Stale data. It killed 11+1 fighters, but 37 are needed. This is not a code bug: the subscription asserts passed, and the win needs the JSON total. | QA (test) |
| Runner_M5_DespawnedZeppelinNeverCounts_MapEndLoses | A test-isolation problem caused by a code bug, not stale data. In EditMode, `ScrollController.Create` never set `Instance`, so MissionRestartTests leaked 9-12 controllers. `FindFirstObjectByType<ScrollController>()` then returned a stale one whose camera had been destroyed, so `MissionDistanceX` was 0 and the map end never fired. M5's new balloons and AAA layout play no part, because the test replaces the objective and spawns its own zeppelin. | Mechanics B047 (`ScrollController.Create` sets Instance). QA also hardened the test |

## What changed in the tests
- `ObjectiveTrackerTests.cs`:
  - New `SpawnTotal(data, type)`: the sum of the JSON spawn `count` for a type, matched case-insensitively. It is the same rule as `ObjectiveTracker.CountOfType`.
  - The RequiredCount test now loops over every destroyAllOfType objective in M1-M5. For each one it asserts required == JSON total and total > 0, and it checks at least 3 such objectives.
  - The M1 tracker test and the runner test kill N-1 fighters, then the Nth. Both now also raise `ScoreKeeper.ReinforcementFighterKey` ("fighterReinforcement") kills and assert that they don't count (B040).
  - The M4 balloon tests (tracker and runner) use the JSON barrage balloon total. `Runner_M4_..._ThenSixthBalloonWins` is renamed `..._ThenLastBalloonWins`.
  - New `BindScroll(cam)`: it picks the ScrollController whose Camera is the fixture's camera. `RunnerFixture` now destroys every ScrollController on construction and on Dispose.
- `MissionEndRulesTests.cs`: `Fixture.Start` binds to the controller that drives the fixture's camera, and `Dispose` destroys every ScrollController.
- Other tests: I grepped for hard-coded fighter, balloon and AAA counts and positions. The only other ones are `MissionDataRulesTests` design-rule minimums and maximums: health balloons at least 4/5/6/7/8, AAA at most 5 in M3/M4/M5, and so on. Those are intentional constraints, not snapshots of the data, so I kept them.

## Checks
- `bash tools/unity.sh test`: total 418, passed 418, failed 0. The results file is `Biplanes/Logs/editmode-results.xml`, test-run start 2026-11-19 04:35:52Z.
- `python tools/compile_check.py`: PASS Biplanes, PASS Biplanes.Editor.
- `python tools/validate_missions.py`: all 5 missions OK.
- The batch run wrote `.meta` files under `Resources/Audio`, which is expected.

## Bugs
None open from T962. The code-caused failures (ScrollController.Instance, ParallaxBackground Destroy, GameManager DontDestroyOnLoad, EnemyBullet team) were fixed by B047 and pass in this run.
