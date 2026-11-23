TASK: B064
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Enemies/BombBalloon.cs (new: pickup, Spawn factory, pure placement helpers, nested BombBalloon.RechargeTimer)
- Biplanes/Assets/Biplanes/Scripts/Player/BombBay.cs (AddBombs(int), static ClampedAdd)
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs ("bombballoon" spawn case, BombsChanged subscription, UpdateBombRecharge auto-spawn)
- Biplanes/Assets/Biplanes/Scripts/Core/GameBalance.cs (BombBalloonData + Resolve; root keys bombBalloonGrant/Delay/Lead)
- Biplanes/Assets/Biplanes/Resources/Balance.json (bombBalloonGrant 4, bombBalloonDelay 4.0, bombBalloonLead 12.0)
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionData.cs (type comment only)
- Biplanes/Assets/Biplanes/Tests/EditMode/BombBalloonTests.cs (new, 15 tests)
SUMMARY: The new BombBalloon pickup (Neutral, drifts like HealthBalloon, player-only, SfxId.Pickup, sprite Sprites/pickups/bomb_balloon or an ink-white 1x1 fallback with a warning) calls BombBay.AddBombs(grant). AddBombs clamps to capacity and raises BombsChanged. A balloon collected with a full bay is still used up. MissionRunner arms a RechargeTimer when BombsChanged reports 0 and cancels it on a refill. The timer counts scaled time only after HasStarted (it stays idle during the scroll hold and pause). It is cancelled on a crash or mission end. When it is due, one auto balloon spawns `lead` ahead of the player along Facing (x clamped to [0, length]), at the player's y clamped to [terrain+1.5, cam top-1]. The spawn is held off while a live bomb balloon is on screen or ahead within `lead`, or while an unspawned JSON bombBalloon entry is in that span. Each empty-bay event gives one spawn, and there is no lifetime cap. Bomb balloons never go into _enemyAircraft or _groundEnemies, so objectives, counters and EnemyPressure don't see them.
VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes, PASS Biplanes.Editor, exit 0.
- The tests assembly is not covered by compile_check. A scratch dotnet build of Biplanes.Tests.EditMode against the fresh runtime DLL showed 0 errors in BombBalloonTests.cs apart from the NUnit-resolution errors that affect every test file outside Unity. So every game API the tests call resolves, but they were not run.
- Unity not run (the editor may hold the lock). QA: run EditMode tests `BombBalloonTests` (plus HealthBalloonFullHealTests and HudBombIconsTests for regressions).
- Playtest: drop all 7 bombs, wait 4 s, and a balloon appears about 12 u ahead at the plane's height. Collect it: the HUD shows 4. Drop 1 of them and collect a JSON balloon: the HUD shows 7. Pause during the 4 s and the countdown pauses. Collecting a balloon at 3 s by some other route cancels the auto spawn.
REQUESTS:
- orchestrator: ARCHITECTURE.md needs a new section after "EnemyPressure" (§3), "Recharge bomb balloon (B064)":
  - Spawn type `bombBalloon` (case-insensitive, JSON y = height above terrain).
  - `BombBalloon.Spawn(Vector3 pos, bool autoSpawned=false)`. Team.Neutral, player-only pickup, adds `bombBalloonGrant` (4) via `BombBay.AddBombs(int)` (clamped to `player.bombCapacity`, raises BombsChanged), and is consumed even at a full bay. Raises EntityDestroyed("BombBalloon", Neutral).
  - Auto-spawn: MissionRunner listens to GameEvents.BombsChanged. On 0 it arms `BombBalloon.RechargeTimer`, and a refill cancels it. After `bombBalloonDelay` (4 s scaled, only while HasStarted, cancelled on crash or mission end) it spawns one balloon `bombBalloonLead` (12) ahead of the player along Facing, at the player y clamped to the flight band. The spawn is held off while a live or pending bomb balloon is on screen or ahead within the lead. There is one spawn per empty-bay event and no cap.
  - Balance keys: top-level `bombBalloonGrant`, `bombBalloonDelay` and `bombBalloonLead`, read via `BombBalloonData.Resolve` (defaults 4 / 4.0 / 12.0).
  - MissionRunner.BombBalloons lists the balloons for the minimap. Pickups are not in EnemyAircraft or GroundEnemies.
- mechanics (follow-up, P2):
  - MinimapView marker skipped: health balloons have no minimap marker either, and adding one needs a new pool plus a shape. MissionRunner.BombBalloons is ready for it.
  - ScoreKeeper.GetScoreFor logs an "unknown entityType" warning for "BombBalloon" (it already does for "HealthBalloon"). Make it return 0 silently for Team.Neutral.
  - ObjectiveTracker.ReadableType could gain "bombballoon".
- orchestrator (design note): if the player misses the auto balloon, no second one comes until the bay is refilled and emptied again (one spawn per empty-bay event, as briefed). Consider re-arming when an uncollected auto balloon falls off screen behind the player.
