TASK: B017
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs
- Biplanes/Assets/Biplanes/Scripts/Missions/ObjectiveTracker.cs
- Biplanes/Assets/Biplanes/Scripts/Core/GameManager.cs
- Biplanes/Assets/Biplanes/Scripts/Core/Health.cs
- Biplanes/Assets/Biplanes/Scripts/UI/ResultView.cs
- Biplanes/Assets/Biplanes/Tests/EditMode/MissionEndRulesTests.cs (new, 18 tests; Unity writes the .meta)
SUMMARY:
- Map end: `ObjectiveTracker.CheckMapEnd(cameraX, length)` returns the first open objective that is not surviveToEnd once the camera reaches `length`. MissionRunner.Update checks it after the surviveToEnd check and loses through `HandleObjectiveFailed(id, reason)` → `GameManager.LoseMission(reason)`. The reasons are "Reached the end of the map with an objective open: <desc>" and, for failAtX, "Objective failed: <desc> (the target got away)". failAtX semantics are unchanged.
- GameManager: new `LoseMission(string)`, `IsMissionOver`, and the `DefaultWinReason`/`DefaultLossReason` consts. A private `SetState(state, reason)` passes the reason to `ResultView.Show(mission, won, score, reason)` (new overload, adds a reason line) and to MissionEnded. Won/Lost is refused while the state is already Won/Lost. `RetryMission` now sets `_pendingMission`, sets timeScale to 1 and loads "Game", and it no longer calls SetState(Playing) before the reload. ResultView's fallback is also "Game". Any MissionEnded disables the plane's PlayerInput and sets the scroll speed to 0 (MissionRunner.EndPlayerControl).
- Health.GetDamageMultiplier: `case DamageSource.Heal: return 1f;`, so the unknown-source warning is gone.
VERIFY:
- `python tools/compile_check.py` → PASS Biplanes (+6 new file(s) not yet in the csproj), PASS Biplanes.Editor, exit 0. The `bash tools/unity.sh compile` step wasn't run: the editor has the project open.
- Tests compiled in a scratch build: the runtime DLL was built from source, then all 8 Tests/EditMode/*.cs files were built against it (test csproj with ProjectReferences dropped and v4.7.1 changed to v4.7.2), 0 errors. Nothing was left in the project. They were not run.
- Run them with Unity closed: `bash tools/unity.sh test`, then MissionEndRulesTests, ObjectiveTrackerTests and MissionRestartTests.
- Playtest:
  - M1: let a fighter escape and fly to x 300 → MISSION FAILED with the map-end reason, the controls and scroll stop.
  - M2: reach camera 370 → the reason is not "Player crashed".
  - Win, then crash into the ground → the state stays Won.
  - Retry → Game reloads and the same mission starts.
  - Pick up a health balloon → no Health warning.
- Risks:
  - `RetryMission_QueuesPendingMissionBeforeLoadingGame` calls LoadScene in EditMode, which throws. The test catches the exception and sets `LogAssert.ignoreFailingMessages`.
  - The tests copy MissionRestartTests' assumption that Awake doesn't run in EditMode. The fixture sets `ScrollController.Instance` itself when it is null.
REQUESTS:
- orchestrator: update ARCHITECTURE.md:
  - §5 end rule: "lost when player damage reaches 100%, any failAtX triggers, or the camera reaches `length` with a non-surviveToEnd objective still open (`ObjectiveTracker.CheckMapEnd`); surviveToEnd-only missions win there".
  - §3 GameManager: add `LoseMission(string reason)` and `IsMissionOver`. SetState ignores Won/Lost while the state is already Won/Lost. `SetState(Lost)` keeps the reason "Player crashed". RetryMission = `_pendingMission` + `LoadScene("Game")`.
  - §3 ResultView: add `Show(int, bool, int, string reason)`.
  - §3 MissionRunner: on MissionEnded it disables the player's PlayerInput and sets the scroll speed to 0.
- orchestrator: B018 should end its loss through MissionRunner.HandleObjectiveFailed(id, reason) / GameManager.LoseMission, with reason "… reached its target".
- orchestrator, for T937 (ResultView owner):
  - `ResultView.CreateButton` never adds a `Button` component, so `_retryButton` and `_nextButton` are null and the buttons can't be clicked.
  - None of the view's Texts has a font assigned.
  - Title, score and best all sit at local y 0. Only the new reason line is offset (+70).
  - I left these alone because they are outside B017.
