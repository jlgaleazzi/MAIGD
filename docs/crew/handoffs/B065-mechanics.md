TASK: B065
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs (re-arm rule: `_lastBombs`, static `ShouldRearmRecharge`, `UpdateBombRecharge` re-arms the timer)
- Biplanes/Assets/Biplanes/Scripts/Core/ScoreKeeper.cs (static `IsScorelessPickup`; Neutral pickups return early, no warning)
- Biplanes/Assets/Biplanes/Scripts/Missions/ObjectiveTracker.cs (ReadableType "bombballoon" -> "bomb balloon(s)")
- Biplanes/Assets/Biplanes/Tests/EditMode/BombBalloonTests.cs (+5 tests)
SUMMARY:
- **Re-arm:** MissionRunner remembers the last BombsChanged value. Each frame after the mission has started, if the timer is idle, the bay is at 0, and no live or pending bomb balloon is on screen or ahead within the lead (missed, scrolled off behind, or recycled), it calls `timer.Reset()` and then `OnBombsChanged(0)`. A new balloon then spawns bombBalloonDelay (4 s) later, ahead of the player.
- **Guards:** At most one balloon at a time, because the re-arm needs the spawned balloon to be gone. A refill, crash or mission end still cancels the timer.
- **Score:** ScoreKeeper ignores "HealthBalloon" and "BombBalloon" from Team.Neutral silently, and ReadableType knows "bombballoon".
VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes, PASS Biplanes.Editor, exit 0.
- A scratch dotnet build of the test assembly showed 0 errors in BombBalloonTests.cs. The only errors were NUnit failing to resolve outside Unity, and those hit every test file. The tests were not run.
- QA: run the EditMode tests `BombBalloonTests`, including the new ones: `Rearm_MissedBalloon_WithEmptyBay_Rearms`, `Rearm_NotWhenCollected_OnScreen_OrAlreadyCounting`, `Rearm_RestartsTheFullDelay_ThenSpawnsOnce`, `ScoreKeeper_NeutralPickups_ScoreZero_WithoutWarning` and `ReadableType_HasBombBalloonName`.
- Playtest:
  1. Empty the bay.
  2. Fly past or around the auto balloon without touching it until it is off screen behind you.
  3. About 4 s later a new balloon should appear about 12 units ahead.
  4. Collect it: the bay shows 4 and no further balloon spawns.
REQUESTS:
- orchestrator: in the ARCHITECTURE.md "Recharge bomb balloon (B064)" section, replace "one spawn per empty-bay event, no cap" with:
  > While the bay stays at 0 and no live or pending bomb balloon is on screen or ahead within the lead, the timer re-arms (MissionRunner.ShouldRearmRecharge), so a missed balloon is replaced bombBalloonDelay later. Only one balloon at a time.
- Also note: ScoreKeeper ignores Neutral pickups (ScoreKeeper.IsScorelessPickup).
