TASK: T935
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs (edit)
- Biplanes/Assets/Biplanes/Scripts/Missions/ObjectiveTracker.cs (rewrite of the evaluation logic, same class and public members kept)
- docs/crew/handoffs/T935-mechanics.md
GameEvents.cs was not changed.

SUMMARY:
MissionRunner now spawns `enemybase`, `playerhq` and `zeppelin`, and all four objective types plus failAtX evaluate for real. Before this task ObjectiveTracker only logged: destroyAllOfType and destroyById never completed, so **missions 1, 2 and 3 could not be won either**. They now can. This is the only behaviour change for missions 1–3.
- Spawn cases sit next to the existing ones and register into _spawnedEntities in the same way.
  - `enemybase`: `EnemyBase.Spawn(position, entry.id)`.
  - `playerhq`: `PlayerHQ.Spawn(position, entry.id)`.
  - `zeppelin`: `ZeppelinAI.Spawn(position, entry.id)`, plus `Init(_playerPlane)` when the plane exists.
  - All three are non-plane types, so they use the ground look-ahead trigger (atX − half screen width − 2) and position = (atX, HeightAt(atX) + y). The zeppelin keeps its JSON y and stays out of the random plane placement.
- Objective types that now work:
  - destroyAllOfType: the number needed is the sum of `count` over the spawn entries of targetType (case-insensitive), passed through a new optional `Init(..., SpawnEntry[] spawns = null)` argument. Each EntityDestroyed(targetType, Team.Enemy) counts one kill, and the objective completes when kills reach that number. Mission 1 needs 12 fighters, mission 3 needs 8 AAA, mission 4 needs 6 barrage balloons.
  - destroyById: MissionRunner keeps `_idTargets`, a list of (id, type key, IDamageable component) for each spawn with a non-empty id: bomber (its Health, key "bomber"), zeppelin (ZeppelinAI, key "zeppelin") and enemy base. On EntityDestroyed(type), any tracked target with that type key that is now `IsDead` is reported once through `ObjectiveTracker.OnTargetDestroyed(id)`. The dying object is still in memory at that point because Destroy is deferred. A zeppelin that despawned silently (Unity-null) never counts as destroyed. This works for the mission 2 bomber_01 and the mission 5 zeppelin_01.
  - bombTarget: MissionRunner subscribes to `EnemyBase.Destroyed` in StartMission (unsubscribing first), and unsubscribes in TearDownPreviousMission and OnDestroy, the same way as EntityDestroyed. When `KilledByBomb` is true it calls `ObjectiveTracker.OnBombTargetDestroyed(base.Id, "enemyBase")`. That matches on targetId, or on targetType when targetId is empty. No points are added here.
  - surviveToEnd: fixed. It used to add to the completed count every frame without setting `evaluated`. It now completes once.
  - failAtX: each Update, before the win check, `ObjectiveTracker.CheckFailAtX(cameraX)` (cameraX = ScrollController.MissionDistanceX) returns the id of the first unevaluated objective with failAtX ≥ 0 and cameraX ≥ failAtX. `HandleObjectiveFailed` then sets `_missionEnded` and calls `GameManager.Instance.SetState(Lost)`, which is the same loss path PlayerPlane's crash uses. Without a GameManager it raises MissionEnded(false, ...) instead. Mission 2 fails at camera x 370 and mission 5 at 620 if the target hasn't been destroyed (alive, despawned or never spawned). failAtX comes from the JSON. PlayerHQ.WorldX (650) is not used, because it doesn't match the JSON's 620.
- ObjectiveTracker internals:
  - A single `Complete(i)` sets `evaluated`, adds 1 to the completed count and raises ObjectiveCompleted. Nothing can be counted twice.
  - `AllComplete` is now `>=`.
  - Init resets `evaluated`.
  - `CheckFailAtX(string, float)` (a stub with no callers) was replaced by `string CheckFailAtX(float cameraX)`.
  - `OnBombApplied(string)` is kept and goes through Complete.
- The mission-end-once guard, teardown (it also clears `_idTargets`) and Next/Retry are unchanged. StartMission still reuses one tracker. The MissionRestartTests members (`_missionEnded`, `OnDestroy`, `HandleObjectiveComplete`, `StartMission`) were not renamed.

VERIFY:
- `python tools/compile_check.py`: exit 0 ("PASS Biplanes (+6 new file(s) not yet in the csproj)", "PASS Biplanes.Editor"). Unity batch compile wasn't run because the editor is open.
- Play test (needs the user), mission by mission:
  - M1: killing all 12 fighters wins.
  - M2: killing the bomber wins. If the camera reaches 370 with it alive, the mission is lost.
  - M3: killing all 8 AAA wins.
  - M4: the base appears at x 460. Pop the 6 balloons, then bomb the base: one bomb wins. Bullets on the base do nothing.
  - M5: the zeppelin enters from the right at about x 60, and the HQ shows at 650. Killing the hull wins. If the camera reaches 620 with it alive or despawned, the mission is lost.
  - Next/Retry after each mission must not win twice or double-count kills.

REQUESTS:
- orchestrator: in ARCHITECTURE §3 Missions, replace "`ObjectiveTracker`: evaluates the objective types in §5." with:
  "`ObjectiveTracker`: evaluates the §5 objective types. It is `Init(objectives, cameraX, spawns)`, and each objective completes once (`evaluated`, then ObjectiveCompleted).
  - destroyAllOfType counts EntityDestroyed(targetType, Enemy) up to the total `count` of that type in `spawns`.
  - destroyById: EntityDestroyed has no id, so MissionRunner tracks every spawn that has an id (bomber Health, ZeppelinAI, EnemyBase). On EntityDestroyed of that type key, if the tracked instance is IsDead, it calls `OnTargetDestroyed(id)`. A silent despawn doesn't count.
  - bombTarget: MissionRunner subscribes to `EnemyBase.Destroyed` (unsubscribed in teardown and OnDestroy). If `KilledByBomb`, it calls `OnBombTargetDestroyed(id, "enemyBase")`, matching by targetId, or by targetType when targetId is empty. No extra score.
  - failAtX: each Update, `CheckFailAtX(camera MissionDistanceX)` returns the first open objective with failAtX ≥ 0 that the camera has reached. MissionRunner then ends the mission once as Lost via `GameManager.SetState(Lost)`.
  - Spawn types `enemyBase`, `playerHQ` and `zeppelin` use the ground look-ahead trigger and sit at (atX, HeightAt(atX) + y). The zeppelin gets `Init(player)`."
  Also in §5, change destroyById's "If failAtX ≥ 0 and the target reaches that x" to "If failAtX ≥ 0 and the camera x reaches failAtX before the target is destroyed".
- mechanics: GameManager.SetState(Lost) always shows and raises the reason "Player crashed", so a failAtX loss shows the wrong text. Suggest `SetState(GameState, string reason = null)` or a `LoseMission(string reason)` (GameManager.cs, ResultView text). Reason for failAtX: "The bomber reached our airfield" / "The zeppelin reached HQ".
- mechanics: after a failAtX loss the player plane keeps its input (only a crash calls UnsubscribeInput). If the result screen doesn't pause time, consider disabling player input on any MissionEnded (PlayerPlane.cs).
- mechanics (T047 still open): BomberAI never gets targetX and ignores escortOf. Mission 2 depends only on failAtX for its fail condition, which now works.
- qa (T936): also re-check missions 1–3, because their destroyAllOfType/destroyById objectives can now be won for the first time. Suggested EditMode tests for ObjectiveTracker (pure logic):
  - destroyAllOfType reaches its count.
  - destroyById completes only for the matching id.
  - bombTarget matches by id, or by type when the id is empty.
  - CheckFailAtX returns null after the objective completes.
  - surviveToEnd counts once.
