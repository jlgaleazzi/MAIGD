# T973: verify B070 (previous-best label, held engine/smoke loops, minimap in the kill-win window)

**Verdict: PASS** (0 P0, 0 P1, 2 P2). B070 closes all three P2s from `docs/qa/T972-verify.md`.
There are two new low-impact findings:
- BUG 1 is a test-quality gap in B070's own audio test.
- BUG 2 is a one-frame minimap flash after Retry or Menu from a win result. It is static evidence only.

## What ran
| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes, PASS Biplanes.Editor, exit 0 |
| `python tools/validate_missions.py` | 5/5 OK. Only the existing duration warnings on M3 (128 s) and M4 (154 s) |
| Unity batch `tools/unity.sh test` (editor closed: no `Temp/UnityLockfile`, only the Unity Hub helper process running) | **total 665, passed 665, failed 0** (`Biplanes/Logs/editmode-results.xml`, start 2026-11-22 18:18:05Z). This is 649 from T972, plus WinDelayFixTests (8/8) and the new WinDelayFixQaTests (8/8). WinDelayTests, WinDelayQaTests, AudioManagerQaTests, MinimapTests and MissionEndRulesTests all pass |

Static check of ScoreKeeper.cs, ResultView.cs, MissionRunner.cs, PlayerPlane.cs and MinimapView.cs:
- Namespaces match the folders.
- No `rb.velocity` and no `UnityEngine.Input`.
- Every member used exists: `CapturePreviousBest`, `PreviousBestFor`, `BestToShow`, `CapturedMission`, `CapturedPreviousBest`, `HoldAudioOnWin`, `EndMissionAudio`, `AudioStopped`, `VisibleIn(state, bool)` and `ResultView.IsShowing`.
- No hand-written `.meta`. Unity generated `WinDelayFixQaTests.cs.meta`.

## Acceptance results
- **"Best:" shows the previous best: PASS.**
  - `MissionRunner.StartMission` (MissionRunner.cs:220-226) runs `ResetScore`, then `CapturePreviousBest(_missionData.id)` on every start (first start, Retry after the scene reload, and Next).
  - `ResultView.Show` (ResultView.cs:294-297) reads `PreviousBestFor(missionIndex)`, so the win-time save in `ScoreKeeper.OnMissionEnded` no longer leaks into the label.
  - The JSON ids 1-5 match `GameManager.ActiveMissionIndex`, so the captured mission and the result's mission agree.
  - New record: the saved best is 100, the run saves more than 100, and the label shows 100. Covered by B070's test.
  - First clear: no saved best, so after the win the saved best is above 0 but `PreviousBestFor` is 0, and `_bestScoreText` stays hidden. New QA test.
  - Retry: starting the same mission again recaptures, and `PreviousBestFor` returns the record just set. New QA test.
  - A different mission captures its own best (777 for M3), and M2 falls back to its saved best. New QA test, plus B070's pure `BestToShow` test.
  - Loss: nothing is saved, and the label shows the saved best. New QA test.
  - With no ScoreKeeper, the label falls back to `SaveSystem.GetBestScore`, as before.
- **Loops held through the window, then stopped once when control ends: PASS.**
  - `HandleObjectiveComplete` sets `HoldAudioOnWin = _winResultDelay > 0` *before* `WinMission` raises `MissionEnded`. `OnMissionEndedAudio` returns on a held win.
  - `TickWinControlEnd` clears `_endControlAt`, then `EndPlayerControl` calls `EndMissionAudio`. The second tick returns false.
  - `EndMissionAudio` is idempotent: `StopPlayerLoops` is guarded by `_audioOwner`, and `_audioStopped` keeps `UpdateAudio` off afterwards.
  - New QA test `SubscribedPlane_KillWin_AudioHeldUntilControlEnds_ThenStopsOnce` runs `PlayerPlane.Start`, so the plane really is subscribed to MissionEnded (see BUG 1). It confirms the loops are held until the deadline, stop once, and stay stopped.
- **Instant wins and losses are unchanged: PASS.**
  - Survive win: `WinResultDelayFor` returns 0, so the loops are not held and stop at once (B070 test).
  - Objective loss with the plane subscribed: the loops stop at once, and `HoldAudioOnWin` is false (QA test).
  - Crash loss: the loops are not stopped, so the engine wind-down plays, and the minimap is hidden in Lost (QA test).
  - A crash inside the win window stays Won and keeps its wind-down through the control end (QA test).
  - Next mission: the new plane starts unheld, and no control end is pending (QA test).
- **Minimap shows only in Playing, Paused, or Won with no result: PASS.**
  - `VisibleIn(state, resultShowing)` (MinimapView.cs:232-235) is used by `IsVisibleState`, which `Update` checks every frame.
  - In Won it hides as soon as `ResultView.IsShowing`.
  - Lost, Menu and Briefing hide. The single-argument overload is unchanged.
  - An instant win opens the result inside `SetState`, so the strip never shows in that Won.

## Bugs

```
BUG: B070 audio-hold runner test passes trivially (plane never subscribed to MissionEnded)
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Tests/EditMode/WinDelayFixTests.cs:121-133 (Runner_M2_KillWin_AudioRunsUntilControlEnds)
STEPS: Static evidence. PlayerPlane subscribes OnMissionEndedAudio in Start() (PlayerPlane.cs:411-414), and
       EditMode never runs Start for AddComponent. So "Assert.IsFalse(fx.Player.AudioStopped)" after the kill
       would also pass with B070's HoldAudioOnWin check removed.
EXPECTED: The test guards the held-win path (B070 handoff VERIFY).
ACTUAL: The test does not guard it. QA added WinDelayFixQaTests.SubscribedPlane_KillWin_AudioHeldUntilControlEnds_ThenStopsOnce,
       which invokes PlayerPlane.Start first and covers the path, so this is test hygiene only. Optional: delete the
       weak test or make it call Start.
```

```
BUG: Minimap can flash for one frame after Retry or Menu from a win result
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Scripts/UI/MinimapView.cs:232-235 + Scripts/Core/GameManager.cs:312-325 (RetryMission), :345-358 (QuitToMenu)
STEPS: Static evidence, not reproduced. On a won result, press Retry (or Menu).
       HideResultView nulls ResultView._instance at once, and timeScale goes back to 1. The state stays Won until
       the reloaded scene's StartMission or OnSceneLoaded sets the next state, and SceneManager.LoadScene completes
       on the next frame. In the rest of that frame, MinimapView.Update can see Won with ResultView.IsShowing
       false, re-enable the strip and render it once over the un-dimmed world.
EXPECTED: The strip is hidden once the result has opened (B070 handoff: "Won until the result screen opens").
ACTUAL: VisibleIn cannot tell "result not opened yet" from "result opened and closed". Impact is about 16 ms,
       and only on Won, not Lost. Possible fix: key visibility on GameManager having a pending result
       (_pendingResult != null) instead of !ResultView.IsShowing, or have ResultView remember that it opened
       for this mission end.
```

## Tests
- **New:** `Biplanes/Assets/Biplanes/Tests/EditMode/WinDelayFixQaTests.cs`, 8 tests, all pass:
  - `FirstClear_PreviousBestIsZero_SoTheBestLineStaysHidden`
  - `RestartSameMission_RecapturesTheNewRecord_AndAnotherMissionCapturesItsOwn`
  - `Loss_DoesNotSave_PreviousBestEqualsSaved`
  - `SubscribedPlane_KillWin_AudioHeldUntilControlEnds_ThenStopsOnce`
  - `SubscribedPlane_ObjectiveLoss_StopsAtOnce`
  - `SubscribedPlane_CrashLoss_KeepsTheWindDown`
  - `SubscribedPlane_CrashInsideTheWinWindow_StaysWon_KeepsTheWindDown`
  - `NextMission_NewPlaneStartsUnheld`
- Not covered by EditMode: the real AudioManager loops, the 2.5 s `WaitForSecondsRealtime` coroutine and the UI (ResultView and MinimapView canvases). These need the manual checks below.

## Manual playtest checklist (B070)
- [ ] M2, new record: the result shows "Best: <old record>" and "Score: <new>".
- [ ] M2, first clear (clear the PlayerPrefs first): there is no "Best:" line.
- [ ] Retry M2 after the record and win again with a lower score: "Best:" shows the record.
- [ ] Next to M3: the "Best:" is M3's own previous best, or no line.
- [ ] Loss: "Best:" shows the saved best, unchanged.
- [ ] During the 2.5 s after the kill, the engine (and the smoke crackle if damaged 70% or more) is still heard. Both stop when the result opens, with no restart.
- [ ] Crash into the ground during the window: the engine winds down, the result says MISSION COMPLETE.
- [ ] Crash loss: the engine winds down as before, and the minimap hides at once.
- [ ] The minimap stays up during the window and hides when the result opens.
- [ ] Watch for a one-frame minimap flash on Retry or Menu from a win result (BUG 2).
- [ ] WebGL (Chrome, Firefox, Safari): the loops stop when the result opens, and the timing is the same in a background tab.
