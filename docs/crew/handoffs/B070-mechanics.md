TASK: B070
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Core/ScoreKeeper.cs: adds CapturePreviousBest(mission), CapturedMission, CapturedPreviousBest, PreviousBestFor(mission) and the pure BestToShow.
- Biplanes/Assets/Biplanes/Scripts/UI/ResultView.cs: "Best:" now reads ScoreKeeper.PreviousBestFor, falling back to SaveSystem.GetBestScore.
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs:
  - captures the previous best at StartMission, next to ResetScore;
  - sets PlayerPlane.HoldAudioOnWin before a delayed win;
  - EndPlayerControl now calls PlayerPlane.EndMissionAudio.
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs: adds HoldAudioOnWin, EndMissionAudio() and AudioStopped. OnMissionEndedAudio skips a held win.
- Biplanes/Assets/Biplanes/Scripts/UI/MinimapView.cs: new VisibleIn(state, resultShowing) overload. The strip uses it with ResultView.IsShowing.
- Biplanes/Assets/Biplanes/Tests/EditMode/WinDelayFixTests.cs (new, 8 tests)
SUMMARY: All three B069 side effects are fixed:
- "Best:" again shows the best from before this run, captured at mission start. It no longer shows the score the kill win already saved.
- The engine and smoke loops keep running through the 2.5 s win window and stop when the controls end.
- The minimap stays up in Won until the result screen opens.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes, PASS Biplanes.Editor, exit 0. I did not run Unity batch.
- A scratch dotnet build of the new test file showed only unresolved NUnit types; the NUnit DLL does not load outside Unity.
- QA EditMode: run WinDelayFixTests, then re-run WinDelayTests, WinDelayQaTests, AudioManagerQaTests (PlayerPlane_MissionEnded_StopsLoops_AndTheyStayStopped: an un-held win still stops at once), MinimapTests (the single-argument VisibleIn is unchanged) and MissionEndRulesTests.
- Play:
  - Set a new record in M2. The result shows "Best:" with the old record, or no Best line on a first clear.
  - During the 2.5 s after the kill, the engine is still heard and the minimap stays visible.
  - Both stop or hide when the result opens.
  - On a crash loss the engine winds down as before, and the minimap hides at once.
NOTES:
- Best score:
  - The label meaning is unchanged (UI_SPEC §3.5, previous best). There is no new-best banner in ResultView, so nothing else depends on it.
  - Saving is unchanged: ScoreKeeper.OnMissionEnded saves at the win, and ResultView.Show saves the max again.
  - If ResultView is opened with no ScoreKeeper, or for a mission that was never captured, it falls back to the saved best, as before.
- Audio:
  - HoldAudioOnWin is set only when WinResultDelayFor > 0 (kill wins).
  - Losses, instant wins and planes without a MissionRunner behave exactly as before.
  - EndMissionAudio is idempotent and keeps a crashing plane's wind-down, so a crash during the win window still plays its wind-down.
  - The loops stop at TickWinControlEnd. That uses the same real-time 2.5 s as GameManager's WaitForSecondsRealtime, so they may differ by at most one frame.
- Minimap: hidden in Lost, as before, including during the crash-fall delay. That is outside this task.
REQUESTS:
- orchestrator: UI_SPEC (minimap section): "The strip shows while Playing or Paused, and in Won until the result screen opens (the B069 kill-win window), via `MinimapView.VisibleIn(state, ResultView.IsShowing)`."
- orchestrator: ARCHITECTURE (the B069 WinMission paragraph), append: "The previous best for the result's 'Best:' label is captured at mission start (`ScoreKeeper.CapturePreviousBest`, read with `PreviousBestFor`). During the window the player's engine and smoke loops keep running (`PlayerPlane.HoldAudioOnWin`) and stop in `EndPlayerControl` (`EndMissionAudio`)."
