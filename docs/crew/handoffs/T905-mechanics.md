TASK: T905
AGENT: mechanics
STATUS: DONE
FILES:
  - Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs (new file)
SUMMARY: Created MissionRunner — the central mission orchestrator. It loads mission JSON via
         Resources.Load<TextAsset> + JsonUtility, creates ScrollController with the speed from
         JSON, spawns all entity types using their static Spawn() factories (FighterAI, BomberAI,
         AAAGun, HealthBalloon, BarrageBalloon, StaticObstacle), wires ObjectiveTracker to
         GameEvents.EntityDestroyed, creates the HUD canvas+HudView from code, and calls
         GameManager.StartMission() to transition into Playing state. Update() checks camera x
         against each SpawnEntry's atX threshold and fires spawns at the right moment.
VERIFY: Compile check — `tools/unity.sh compile` (or static check if Unity unavailable):
        1. Confirm MissionRunner.cs compiles with no errors.
        2. In-game check: load mission_01 — HUD appears, player plane spawned at runway,
           fighters spawn when camera passes x=10, objectives update on kills.
        3. Verify scroll speed matches JSON value for the loaded mission.
        4. Kill all entities — verify "Objective Complete!" message and win state transition.
REQUESTS: none
