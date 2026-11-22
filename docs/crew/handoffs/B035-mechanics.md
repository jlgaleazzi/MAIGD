TASK: B035
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Resources/Balance.json (enemyHealth.fighter 25 -> 75; new `pressure` block)
- Biplanes/Assets/Biplanes/Scripts/Core/GameBalance.cs (PressureData + Resolve; `pressure` field)
- Biplanes/Assets/Biplanes/Scripts/Core/ScoreKeeper.cs (ReinforcementFighterKey const, scored as fighter)
- Biplanes/Assets/Biplanes/Scripts/Missions/EnemyPressure.cs (new)
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs (creates EnemyPressure; aircraft list; SpawnReinforcementFighter)
- Biplanes/Assets/Biplanes/Tests/EditMode/EnemyPressureTests.cs (new, 12 tests)
SUMMARY: Fighters now take exactly 3 player bullets (75 / 25). EnemyPressure, added to the MissionRunner object and Init'ed in StartMission, sends a wave of `pressure.fillerCount` reinforcement fighters once no enemy aircraft has been in view for `maxQuietSeconds`. The wave is capped by `maxLiveFighters` and spawns through the same FighterAI.Spawn + Configure path as JSON fighters. Reinforcements are named "fighterReinforcement": ScoreKeeper pays them fighter score, and ObjectiveTracker never matches them against destroyAllOfType "fighter".
DECISIONS:
- Reinforcement type key: `ScoreKeeper.ReinforcementFighterKey = "fighterReinforcement"`. It's the GameObject name, so Health raises EntityDestroyed with it. ScoreKeeper maps it to score.fighter. ObjectiveTracker is unchanged: "fighterReinforcement" != "fighter", so it never counts. Nothing else in Scripts/ keys on the name "fighter" (grepped).
- Single fighter factory path: MissionRunner.SpawnFighter(position, pattern, escortOf) = FighterAI.Spawn + Configure + register. JSON fighters and reinforcements both use it, so reinforcements fly, shoot and pitch like normal fighters. Reinforcements use the "straight" pattern. They enter at the right edge + PlaneEntryMarginX (1.5) + 2*index + random 0..2. Each takes a random height inside slice `index` of the flight band (terrain + 1.5 .. screen top - 1), the same band RandomPlaneY uses.
- Timer rules (pure `EnemyPressure.Step`):
  - GameManager not Playing: the timer **holds** (pause freezes it).
  - Player missing, on the runway or HasCrashed, or no objective open (`MissionRunner.ObjectivesOpen` = mission running, not ended, tracker not AllComplete): it **resets to 0**.
  - Any live FighterAI/BomberAI/ZeppelinAI overlapping the camera rect: it resets. Half widths: fighter 0.75, bomber 1.5, zeppelin ZeppelinAI.HalfLength; half height 0.5.
  - Uses Time.deltaTime (scaled).
- At the limit: WaveSize = min(fillerCount, maxLiveFighters - liveNear). 0 = at the cap: the timer stays at the limit and it retries every frame. After a wave it resets to 0.
- Cap counts only live fighters within one screen width beyond either screen edge (`EnemyPressure.CapRangeScreens = 1`). FighterAI never despawns, so counting every fighter ever spawned would block reinforcements for the rest of a busy mission. Reinforcements more than 20 units behind the left screen edge are destroyed silently (no score, no objective), using the same margin as ZeppelinAI.
- No Find* per frame: MissionRunner keeps `_enemyAircraft` (fighters, bombers, zeppelins, reinforcements). Destroyed entries are pruned on each spawn and the list is cleared in teardown/OnDestroy. It's exposed as `IReadOnlyList<MonoBehaviour> EnemyAircraft`, alongside `PlayerPlane Player` and `bool ObjectivesOpen`.
- PressureData: `enabled` has a field initializer of true, so a missing key (or a missing block) keeps it on. Missing or non-positive numbers fall back to 2 / 2 / 6. enabled=false makes Update return at once.
VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes (+1 new file(s) not yet in the csproj), PASS Biplanes.Editor, exit 0. `tools/unity.sh compile` wasn't run because the editor has the project open.
- Tests compiled in a scratch build (B018 method, not run): runtime DLL built with compile_check helpers, temp test csproj retargeted v4.7.1 -> v4.7.2 with all 24 Tests/EditMode/*.cs. Result: 0 errors. No temp files left in the project.
- Run with Unity closed: `bash tools/unity.sh test`, EnemyPressureTests (Step_*, ShouldSpawn_FiresAtTheLimit, WaveSize_RespectsTheCap, SliceHeight_*, InView_AndIsNear, PressureData_Resolve_DefaultsAndDisable, Balance_PressureBlock_AndThreeHitFighters, Fighter_SurvivesTwoBullets_DiesOnTheThird, Reinforcement_ScoresLikeAFighter_ButNotForDestroyAll).
- Play checks (Unity -> Biplanes -> BuildScenes, Play):
  - Mission 1: no reinforcements while on the runway.
  - After takeoff, an empty screen for 2 s brings 2 fighters from the right.
  - A fighter dies on the 3rd bullet.
  - Killing reinforcements adds 100 each but doesn't advance "destroy all fighters". The mission still completes on the JSON count, and no waves come after the win.
  - Pause for over 2 s: no wave on resume until 2 s of quiet play have passed.
REQUESTS:
- qa: Tests/ grep found no test that assumes a 1-hit fighter. EnemyFireTests.Fighter_DeadOrPlayerDead_NeverFires applies enemyHealth.fighter (kills at any value). EnemyFireTests:190, AudioHooksPlayerTests:49 and AudioManagerQaTests:426 build Health with their own hit points (25 / 200), so they're unaffected. No updates needed. Please run the full EditMode suite plus EnemyPressureTests.
- mechanics (follow-up, small): FighterAI.cs is stale but harmless. Its class doc still says "one player bullet kills it", and the Awake fallback for missing balance is `25f`. Change the fallback to 75.
- orchestrator: ARCHITECTURE §3 "Enemy pressure" corrections:
  (1) While not Playing the timer holds. Runway, crash, missing player or objectives done reset it.
  (2) maxLiveFighters counts live fighters within one screen width beyond either screen edge (CapRangeScreens), not every fighter in the mission, because fighters never despawn.
  (3) Reinforcements are named `ScoreKeeper.ReinforcementFighterKey` ("fighterReinforcement"). They fly the "straight" pattern, enter at right edge + 1.5 + 2*i + rand(0..2), and take heights in per-plane slices of the flight band.
  (4) Reinforcements more than 20 units behind the left screen edge are destroyed silently.
  (5) Add to §3 Missions: MissionRunner exposes `EnemyAircraft`, `Player`, `ObjectivesOpen` and `SpawnReinforcementFighter(index, count)`, and routes every fighter spawn through one SpawnFighter helper.
