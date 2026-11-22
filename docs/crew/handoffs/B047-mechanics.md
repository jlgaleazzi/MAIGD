TASK: B047
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs (old background/terrain teardown now uses DestroyNow; win/loss use GameManager.GetOrNull)
- Biplanes/Assets/Biplanes/Scripts/Core/ScrollController.cs (Create sets Instance when it builds a new controller)
- Biplanes/Assets/Biplanes/Scripts/Core/GameManager.cs (new GetOrNull(); Instance guards DontDestroyOnLoad with Application.isPlaying)
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs (TriggerCrash uses GameManager.GetOrNull)
- Biplanes/Assets/Biplanes/Scripts/Missions/TerrainStrip.cs (DestroyChunk: DestroySafe = Destroy in Play, DestroyImmediate otherwise)
- Biplanes/Assets/Biplanes/Scripts/Weapons/EnemyBullet.cs (ApplyStats() runs at pool construction and in Awake)
SUMMARY:
Root causes: (1) MissionRunner.StartMission removed the old ParallaxBackground/TerrainStrip with Destroy, which logs an error in edit mode. (2) ScrollController.Create never set Instance, because only Awake set it and Awake doesn't run in EditMode. So every StartMission added another controller (the 9/12 count and the FirstStart NRE). (3) PlayerPlane.TriggerCrash called GameManager.Instance, which ran DontDestroyOnLoad. (4) The fighter test was not a FighterAI team bug: shots[0].Team is the EnemyBullet's team. That bullet came from an unprimed pool, where Awake never ran, so the Projectile default Team.Neutral stayed. Bullets now get their team, speed, lifetime and damage in the pool factory too.
Play-mode behaviour is unchanged: Awake still sets the same values, GetOrNull returns Instance in Play mode, and DestroyNow/DestroySafe call Destroy in Play mode.
VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes, PASS Biplanes.Editor (exit 0).
- `UNITY_PATH="/d/Program Files/6000.6.5f1/Editor/Unity.exe" bash tools/unity.sh test` -> total 418, passed 415, failed 3 (exit 2). All 8 code-caused failures now pass (EnemyFireTests.Fighter_FiresAtPlayerAhead_WithCadenceFromBalance, MissionEndRulesTests.Runner_AnyEnd_DisablesPlayerInputAndStopsScroll, 5 MissionRestartTests, PlanePitchTests.Player_Crash_TumblesFromItsCurrentAttitude). ObjectiveTrackerTests.Runner_M5_DespawnedZeppelinNeverCounts_MapEndLoses also passes now; it was a side effect of the missing ScrollController.Instance.
- The 3 remaining failures are the stale mission-1 count tests that QA owns (T962): ObjectiveTrackerTests.DestroyAllOfType_M1_CompletesOnThe12thFighterOnly_Once, DestroyAllOfType_RequiredCount_IsTheSpawnTotal_PerMission ("expected 12, was 37"), Runner_Next_OneSubscriptionEach_AndKillsCountedOnce_M1WinsOnce.
- The batch run imported assets and wrote .meta files for new files under Resources/Audio, as expected.
REQUESTS:
- orchestrator: ARCHITECTURE §3 Core: document `GameManager.GetOrNull()`, which returns the existing manager, or a new one in Play mode, or null outside Play mode. Gameplay code should use it instead of `GameManager.Instance` (the UI views keep Instance). Also note that `ScrollController.Create` sets `Instance`.
- mechanics (follow-up, P2): EnemyPressure.cs:117 still calls `GameManager.Instance`, and EnemyPressure.cs:179 calls `Destroy`. Both run only from Update (Play mode), so they're harmless today. Switch them to GetOrNull / an isPlaying-guarded destroy for consistency. I left them out to stay within the 6-file limit.
- qa: none beyond T962 (EnemyFireTests needed no change).
