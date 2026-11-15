TASK: B025
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs (OnTriggerEnter2D: 50% instead of a crash; grace window; TickPlaneCollisionGrace in FixedUpdate)
- Biplanes/Assets/Biplanes/Scripts/Core/DamageSource.cs (appended `PlaneCollision`, existing ordinals unchanged)
- Biplanes/Assets/Biplanes/Scripts/Core/Health.cs (PlaneCollision -> damageTaken.planeCollision for the player; fatal like Collision for enemies; default 50 when Balance isn't loaded)
- Biplanes/Assets/Biplanes/Scripts/Core/GameBalance.cs (DamageTakenData.planeCollision, `const DefaultPlaneCollision = 50`, `static PlaneCollisionOrDefault(DamageTakenData)`)
- Biplanes/Assets/Biplanes/Resources/Balance.json (damageTaken.planeCollision = 50.0 only)
- Biplanes/Assets/Biplanes/Tests/EditMode/PlaneCollisionTests.cs (new, 11 tests)
SUMMARY:
- Mid-air contact with a FighterAI/BomberAI: the enemy still gets ApplyDamage(1, Collision) (always 100% for enemies, so EntityDestroyed with "fighter"/"bomber", score and the enemy's own death/Destroy path are unchanged). The player gets ApplyDamage(1, PlaneCollision) = damageTaken.planeCollision (50%), the smoke tint refreshes, and TriggerCrash runs only if the total reaches 100%. The FixedUpdate >=100% check is unchanged.
- No double hit: a dead enemy is ignored (covers re-entry and the bomber's other hitboxes, since the first hitbox kills the bomber). A 0.5 s grace window (`PlayerPlane.PlaneCollisionGraceSeconds`, counted down in FixedUpdate) stops any further plane-collision damage to the player. Enemies touched in that window are still destroyed (enemies always die on a collision), but the player isn't charged again.
- Bomber, before and now: BomberAI.OnTriggerEnter2D is a no-op. The bomber was and still is destroyed outright by PlayerPlane (Collision = 100% for enemy-mode Health, despite its 200 hp). Only the player's side changed. Unchanged fatal paths: terrain (FixedUpdate HeightAt -> 1 x Collision = 100% -> crash), StaticObstacle (100 x Collision), EnemyBase/PlayerHQ (1 x Collision), zeppelin hull/turret ram (Collision + ForceCrash), BarrageBalloon (its own rule, untouched). No old test expected an instant plane-collision crash, so no existing test needed to change.
VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes (+2 new file(s) not yet in the csproj), PASS Biplanes.Editor, exit 0. `tools/unity.sh compile` not run because the editor has the project open.
- Tests scratch build (B018 method, script at C:\Users\JUANLU~1\AppData\Local\Temp\claude\0528dffa73e43d53\ee40c63b-e029-45ba-8e86-0c38e6405307\scratchpad\b025\build_tests.py): built the runtime DLL from source, then all 13 Tests/EditMode/*.cs against it with ProjectReferences removed and v4.7.1 -> v4.7.2. Result: 0 errors. Nothing was left in the project. The tests compiled but weren't run.
- Run in the Unity Test Runner (EditMode) -> PlaneCollisionTests:
  - Balance_PlaneCollisionIs50_DefaultIs50WhenMissing
  - PlaneCollisionSource_IsFatalForEnemies
  - FighterContact_At0_Takes50_NotCrashed_FighterDestroyed
  - SecondContact_AfterGrace_Reaches100_AndCrashes
  - SameFighter_ReEntry_DoesNotDamageTwice
  - SecondEnemy_WithinGrace_IsDestroyed_PlayerNotDamagedAgain
  - BomberContact_AllHitboxes_Takes50Once_BomberDestroyed
  - TerrainContact_StillCrashes
  - TerrainContact_At50_StillCrashes
  - BuildingContact_StillCrashes
  - ForceCrash_ZeppelinRam_StillCrashesAtOnce
  - Also re-run EnemyFireTests and EnemyWeaponsAndPoolTests.
- Playtest: ram a fighter. It explodes and scores, the HUD goes to 50%, and you keep flying. Ram another more than 0.5 s later: crash fall, then MISSION FAILED. Ram a bomber: it dies and you take 50%. Ground, building, tree and zeppelin are still instant crashes.
- Risks:
  - With 2 enemies overlapping at once (for example an escort fighter on its bomber), both die for 50% total, which is a small bonus kill. That's by design of the grace window.
  - BuildingContact_StillCrashes uses StaticObstacle.Spawn("house"). If that sprite is missing, it logs a LogWarning, which is not an error.
REQUESTS:
- orchestrator: ARCHITECTURE.md §4 Balance schema: add `damageTaken.planeCollision` (50, the percent the player takes from mid-air contact with a fighter or bomber; code default 50 if missing).
- orchestrator: ARCHITECTURE.md §3 Core: DamageSource gains `PlaneCollision` (player: x damageTaken.planeCollision; enemies: fatal like Collision).
- orchestrator: ARCHITECTURE.md §3 Player: replace "colliding with an enemy plane = crash" with "mid-air contact with a FighterAI/BomberAI destroys the enemy (Collision) and costs the player damageTaken.planeCollision through Health; it crashes only at 100% total. For PlaneCollisionGraceSeconds (0.5) after a contact, further contacts still kill enemies but don't damage the player. Terrain, buildings, trees, EnemyBase, PlayerHQ and zeppelin rams stay fatal."
- orchestrator: the GDD checklist line "an enemy plane = crash" in the agent prompts/AGENTS docs is now stale. It's for whoever owns .claude/agents.
- orchestrator (optional): the 0.5 s grace is a named const. If it should be tunable, a `player.planeCollisionGraceSeconds` Balance key is a small mechanics follow-up.
