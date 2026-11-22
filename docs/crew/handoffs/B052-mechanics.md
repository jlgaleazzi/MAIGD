TASK: B052
AGENT: mechanics
STATUS: DONE (code, compile and test-assembly compile verified; the real Unity test run is pending because the editor has the project open)
FILES:
- Biplanes/Assets/Biplanes/Resources/Balance.json (`player.resistance`: 1.5)
- Biplanes/Assets/Biplanes/Scripts/Core/GameBalance.cs (PlayerData.resistance, const DefaultResistance = 1, static ResistanceOrDefault(PlayerData): null, missing or <= 0 -> 1)
- Biplanes/Assets/Biplanes/Scripts/Core/Health.cs (player branch: damage = ApplyPlayerResistance(percent * damageTaken multiplier, src, resistance); new public static ApplyPlayerResistance(float damage, DamageSource src, float resistance); const PlayerDeathSnap = 0.001)
- Biplanes/Assets/Biplanes/Tests/EditMode/PlayerResistanceTests.cs (new, 17 tests)
SUMMARY: Player damage from Bullet (5 -> 3.33%), Bomb (10 -> 6.67%), Flak (15 -> 10%), ZeppelinTurret (12 -> 8%) and PlaneCollision (50 -> 33.33%) is now divided by Balance `player.resistance` (1.5). Not scaled: DamageSource.Collision (terrain, buildings, rams stay an instant crash), DamageSource.Heal and any negative or zero amount (health balloons are 1:1, Heal() untouched), and enemy (hit-point mode) Health. A missing or <= 0 resistance behaves as 1. The smoke threshold, HUD bar and audio cues read the resulting percent unchanged.
  Extra fix needed by the change: the divided hits are fractional floats, and 3 x 33.333332f sums to 99.999996f, which left the player alive after three rams. In player mode, a positive hit that lands within PlayerDeathSnap (0.001%) of 100 now snaps to 100. That's far below the smallest real hit (~3.3%). Enemies are unaffected.
  Gameplay effect: a mid-air contact is now 33.3% instead of 50%, so it takes 3 contacts to crash instead of 2. Other hits to death: 30 fighter bullets (was 20), 15 bomber bombs (was 10), 10 flak (was 7), 13 zeppelin-turret hits (was 9).
VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes, PASS Biplanes.Editor (exit 0).
- B018-style scratch compile of the EditMode test assembly: all 34 Tests/EditMode/*.cs files, built with dotnet against a fresh runtime build. It needs TargetFrameworkVersion raised to v4.7.2 in the temp csproj, because nunit.framework is net472. Result: 0 errors. No files were left in the project.
- `bash tools/unity.sh test`: NOT run. The orchestrator said the Unity editor has the project open. Real suite: pending (total/passed/failed unknown).
REQUESTS:
- qa: run the real EditMode suite once the editor is closed (`UNITY_PATH="/d/Program Files/6000.6.5f1/Editor/Unity.exe" bash tools/unity.sh test`). I expect these existing tests to FAIL, because they assert the un-divided player percent. I did not edit them. Each should expect `damageTaken.X / PlayerData.ResistanceOrDefault(g.player)`:
  - EnemyFireTests.cs:182, 187 (fighter shot = fighterBullet; + bomberGunner)
  - EnemyWeaponsAndPoolTests.cs:257 (flak shell = damageTaken.flak); :597 (bomb = bomberGunner) and :601 (bullet = fighterBullet) in Health_PlayerBombHit_UsesBomberGunner_OtherSourcesAndEnemiesUnchanged. Its :605 (collision) and :610 (enemy) still pass.
  - ZeppelinAndBaseTests.cs:140, 144, 182, 196 (zeppelinTurret / flak percents on a player Health)
  - PlaneCollisionTests.cs:188, 189 (50 -> 33.33), :220, :232, :246 (50 -> 33.33). Also SecondContact_AfterGrace_Reaches100_AndCrashes (:205-207), because two contacts now reach 66.7%, not 100%, and the plane doesn't crash. Rewrite it as three contacts, or as "two contacts leave the player alive".
  - Should still pass: FxManagerTests:267 (loops until smoke), MissionEndRulesTests:450 (Heal 1:1), AudioHooksPlayerTests and AudioManagerQaTests (hit cues), PlaneCollisionTests terrain/building/TerrainContact_At50 (no exact percent asserted on the non-fatal hit).
- orchestrator: ARCHITECTURE §4 already has the `player.resistance` line. Please also add the following:
  - `"resistance": 1.5` in the §4 Balance.json player schema block, if that block lists the keys.
  - In §3 Health: the API `Health.ApplyPlayerResistance(damage, src, resistance)` and `PlayerData.ResistanceOrDefault`.
  - In §3 Health, the snap rule: player damage within `Health.PlayerDeathSnap` (0.001%) of 100 counts as 100.
  - The GDD mechanics line "Touching a fighter or bomber costs 50%" now resolves to 33.3% (3 contacts to crash). Update the GDD text or the checklist so QA doesn't flag it as a P1.
