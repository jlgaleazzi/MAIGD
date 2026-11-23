# T972: verify B069 (kill-win result delay, bomber ram = 250 HP)

**Verdict: PASS** (0 P0, 0 P1, 3 P2). B069 closes T971 BUG 1: a kill win now leaves the
world running for 2.5 s of real time, so the bomber's crash, falling wreck and ground
burst play before the result screen opens. The ram change works as specified. The three
P2 bugs are side effects of the 2.5 s window (the best-score label, engine audio and the
minimap).

## What ran
| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes, PASS Biplanes.Editor, exit 0 |
| `python tools/validate_missions.py` | 5/5 OK. Only the existing duration warnings on M3 and M4 |
| Unity batch `tools/unity.sh test` (editor closed, no `Temp/UnityLockfile`) | **total 649, passed 649, failed 0** (`Biplanes/Logs/editmode-results.xml`, start 2026-11-22 18:10:29Z). This includes the B069 tests (WinDelayTests 7, BomberRamTests 6, the updated PlaneCollisionTests) and the QA tests below |

Static check of GameManager.cs, MissionRunner.cs, PlayerPlane.cs and GameBalance.cs:
- Namespaces match the folders.
- No `rb.velocity` and no `UnityEngine.Input`. `using UnityEngine.InputSystem` is present where `Keyboard` is used (GameManager).
- `RamDamageData` is `[System.Serializable]`, so JsonUtility reads `ramDamage`. It is a second public class in GameBalance.cs, which follows the file's existing pattern.
- Every member used exists: `WinMission`, `KillWinDelayOrDefault`, `WinResultDelayFor`, `ControlEndDue`, `TickWinControlEnd`, `RamDamageFor`, `RamDamageData.BomberOrDefault`.
- No hand-written `.meta`. Unity generated `WinDelayQaTests.cs.meta` in the batch run.

## Acceptance results
- **The result opens about 2.5 s of real time after a kill win: PASS.**
  - `HandleObjectiveComplete` sets `_winResultDelay`, then calls `WinMission(reason, 2.5)`. `ShowResultView` starts `OpenResultAfter` (`WaitForSecondsRealtime`), and `OpenResultView` (timeScale 0) runs only if the state is still Won.
  - Test: `_endControlAt` is kill time + 2.5 s (within 0.25 s).
- **The game keeps running, so the crash plays: PASS.**
  - Test `M2_Kill_BomberFixedUpdateCrashes_WreckLandsBeforeTheResultOpens`: after the 40th bullet, timeScale is still 1. The bomber's own `FixedUpdate` runs `BeginCrash`, which removes the bomber and leaves one wreck. Stepped at 0.02 s, the wreck lands (ground burst) in about 1.2-1.3 s, with at least 0.5 s left before the result screen.
  - Worst case: a wreck from the top of the screen (y 5) down to the ground base (-4.5) lands in about 1.9 s, still under 2.5 s (test `Wreck_FromTheTopOfTheScreen_LandsInsideTheWinDelay`).
- **No Lost and no second end during the window: PASS.**
  - Player crash: `SetState` refuses Lost once the state is Won. Test: a terrain crash through `PlayerPlane.FixedUpdate` leaves the state Won with one `MissionEnded(true)`.
  - Map end and failAtX: `_missionEnded` is set, so both are skipped. Test: the camera at length + 5 plus Update leaves the state Won.
  - Pause: `PauseGame` acts only in Playing, so Esc does nothing during the window. timeScale stays 1, input stays on, and the result opens on time. Test covered. This is by design, not a bug.
  - Retry and Menu: there is no UI for them during the window. Both code paths cancel the pending result: `RetryMission` calls `HideResultView`, which calls `CancelPendingResult`, and `QuitToMenu` calls `CancelPendingResult`. `OpenResultAfter` also re-checks the state.
  - Next and teardown: `TearDownPreviousMission` clears `_endControlAt`. Covered by `Runner_NextMission_ClearsAPendingControlEnd`.
  - A scene reload destroys the MissionRunner. Its `OnDestroy` unsubscribes, and the GameManager coroutine is cancelled by the Retry/Menu path, or skipped by the state check.
- **survive_to_end is unchanged (immediate): PASS.** `WinResultDelayFor` returns 0, and `Runner_SurviveToEnd_StillEndsControlAtOnce` covers it. Note: no current mission uses surviveToEnd. All 5 missions win by a kill (destroyAllOfType, destroyById or bombTarget), so every mission win now has the 2.5 s delay. That matches the B069 design.
- **Score and best-score consistency:**
  - The "saved vs shown mismatch" in the B069 KNOWN note is **not real**. `ResultView.Show` (ResultView.cs:299) saves `currentScore` again on a win, and `SaveBestScore` keeps the maximum, so the saved best ends up equal to the shown score, including kills made during the window.
  - There is a different, real regression: the "Best:" label now shows the wrong number (BUG 1, P2).
- **Rams: PASS.**
  - A fighter still dies from a ram, even inside the grace window.
  - A bomber loses 250 (25%) per ram, once per 0.5 s grace window, whichever hitboxes are touched. It survives 3 rams and dies on the 4th with one EntityDestroyed.
  - The player takes 50 / 1.5 = 33.3% per contact.
  - New test: a mutual ram (the 4th ram kills the bomber while the player's 3rd contact crashes it) is a **win**. The kill is applied before the player's damage.
  - `BomberCrashQaTests.Ram_KillsTheToughBomberAtOnce_OneEvent` is renamed to `Collision_IsFatalForEnemyHealth`, and its summary and message are updated (B069 request).
- **Mission 2 is still winnable: YES.** The 40th bullet wins at the hit (T971 tests and this run). Rams now give no shortcut: 4 rams would cost the player 133%, so at most 2 rams (500 HP) are survivable without a health balloon.
- Docs: ARCHITECTURE (lines 131, 373 and 567) and the GDD Change Log 2026-11-22 already carry the B069 text. `docs/missions/mission_02.md:30` now says 1000 hp, so T971 BUG 2 is closed.

## Bugs

```
BUG: Result "Best:" shows this run's score instead of the previous best after a kill win
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Scripts/Missions/MissionRunner.cs:556-560 (ScoreKeeper.OnMissionEnded saves the best at the win),
       Scripts/UI/ResultView.cs:292-294 (reads SaveSystem.GetBestScore when the screen opens, 2.5 s later)
STEPS: Static evidence. Previous best for mission 2 = 0 (or any value lower than this run). Kill the bomber.
       HandleObjectiveComplete -> WinMission(.., 2.5) -> ScoreKeeper.OnMissionEnded(2, true) ->
       SaveBestScore(2, 500) right away. 2.5 s later OpenResultView -> ResultView.Show reads
       GetBestScore(2) = 500 and shows "Best: 500 / Score: 500". Before B069 (delay 0) ResultView.Show ran
       inside SetState, before ScoreKeeper saved, so it showed the previous best. This affects every
       mission, because all 5 win by a kill.
EXPECTED: UI_SPEC §3.5 (line 187): "best_score_N from SaveSystem shows previous best above current score";
       ResultView's own comment: "Previous best, read before this run is saved".
ACTUAL: On a new record, "Best" equals the score at the win moment. That hides the comparison, and the
       label appears even when the previous best was 0. Suggested fix: GameManager/ResultView captures the
       previous best at the win moment (e.g. ScoreKeeper or MissionRunner remembers it before saving), or
       ScoreKeeper.OnMissionEnded leaves the best-score save to ResultView.Show (it already saves) and only
       saves the unlock.
```

```
BUG: Engine and smoke audio stop at the kill while the player keeps flying for 2.5 s
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Scripts/Player/PlayerPlane.cs:1081-1085 (OnMissionEndedAudio: StopPlayerLoops, _audioStopped = true),
       :995 (UpdateAudio returns while _audioStopped)
STEPS: Static evidence. MissionEnded(true) fires at the killing hit (B069). OnMissionEndedAudio stops the
       engine and smoke loops at once, but B069 keeps the input and the scroll alive until the result opens.
EXPECTED: ARCHITECTURE "GameManager.WinMission ... During the delay the player keeps input and the camera
       keeps scrolling". The plane is still flying, so its engine should still be heard until the result
       screen (timeScale 0) opens.
ACTUAL: The plane flies on in silence for 2.5 s. Gun and bomb SFX still play. Suggested fix: stop the loops
       when the controls end (MissionRunner.EndPlayerControl / TickWinControlEnd) or when the result opens,
       not at MissionEnded(true).
```

```
BUG: Minimap disappears at the kill, 2.5 s before the result screen
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Scripts/UI/MinimapView.cs:223-225 (VisibleIn: Playing or Paused only)
STEPS: Static evidence. The state becomes Won at the killing hit, so the strip hides while the player is
       still flying and the wreck is still falling.
EXPECTED: The HUD stays consistent while the player keeps control (B069 window). The other HUD elements stay up.
ACTUAL: Only the minimap vanishes mid-flight. Low impact. Either keep it visible while the win is pending
       (e.g. until ResultView.IsShowing) or accept it and record the decision in UI_SPEC.
```

## Tests
- **New:** `Biplanes/Assets/Biplanes/Tests/EditMode/WinDelayQaTests.cs`, 4 tests, all pass:
  - `M2_Kill_BomberFixedUpdateCrashes_WreckLandsBeforeTheResultOpens`: the deadline is kill + 2.5 s, timeScale is 1, FixedUpdate runs BeginCrash, and the wreck lands with 0.5 s or more to spare. One MissionEnded.
  - `Wreck_FromTheTopOfTheScreen_LandsInsideTheWinDelay`: the worst-case fall height.
  - `DuringTheWindow_MapEnd_Pause_PlayerCrash_ChangeNothing`.
  - `MutualRam_FourthRamKillsBomberAndCrashesPlayer_IsAWin`.
- **Renamed:** `BomberCrashQaTests.Ram_KillsTheToughBomberAtOnce_OneEvent` is now `Collision_IsFatalForEnemyHealth`, with its class summary and assert message updated.
- Not covered by EditMode: the real `WaitForSecondsRealtime` coroutine and the UI. `ShowResultView` returns early outside Play mode. These need the manual checks below.

## Manual playtest checklist (B069)
- [ ] M2: kill the bomber with bullets. The mid-air explosion plays, then the wreck dives smoking and makes a 3-explosion ground burst. The result opens about 2.5 s after the kill, after the burst. +500 once.
- [ ] M2: during the 2.5 s you can still steer, fire and bomb, and the world scrolls. Controls stop when the result opens.
- [ ] M2: crash into the ground (or get shot down) inside the window. The result still says MISSION COMPLETE.
- [ ] M2: Esc inside the window does nothing. The result opens on time.
- [ ] M2: ram the bomber once. You take about 33%, and the bomber smokes lightly (25%) and flies on. A second ram 0.5 s or more later takes another 25%.
- [ ] M2: a ram into an escort fighter still kills the fighter.
- [ ] M1, M3, M4 (last balloon or the base bomb) and M5 (zeppelin): the last kill also waits about 2.5 s before the result.
- [ ] Result screen after a new record: "Best:" should show the old record (BUG 1, currently shows the new score).
- [ ] Retry, Next and Menu from the result: no stray wreck, and the new mission is not ended by an old pending result.
- [ ] WebGL (Chrome, Firefox, Safari): the 2.5 s delay is the same in a backgrounded or throttled tab (real time).
