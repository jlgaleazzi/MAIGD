TASK: T937
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Core/GameManager.cs (state machine creates/destroys every view, Esc, delayed result, NextMission, QuitToMenu, PauseGame)
- Biplanes/Assets/Biplanes/Scripts/UI/ResultView.cs (rewritten: static Create/HideAll, own canvas, real Buttons, fonts, layout)
- Biplanes/Assets/Biplanes/Scripts/UI/PauseView.cs (rewritten: static Show/Hide, own canvas, Resume/Quit through GameManager)
- Biplanes/Assets/Biplanes/Scripts/UI/BriefingView.cs (rewritten: static Show/Hide, own canvas, unscaled fade + 5 s timer, calls GameManager.StartMission)
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs (crash only: input off at once, scripted 1 s fall/tumble, LoseMission with result delay)
- Biplanes/Assets/Biplanes/Scripts/UI/MainMenuView.cs (2 lines: mission click -> GameManager.ShowBriefing; EnsureEventSystem made internal static and shared)
- MissionRunner.cs is NOT changed (views are created by GameManager). No test file added (6-file cap; Tests/ is outside the mechanics write lanes). The proof is the code-path check below.
SUMMARY:
- Root cause confirmed: nothing ever created ResultView/PauseView/BriefingView, so a crash set Lost, MissionRunner stopped scroll and input, and ShowResultView found no view: a frozen world with no screen. Now each GameManager state builds its own view on a dedicated overlay canvas on entry (Briefing 105, Pause 110, Result 120; EventSystem ensured) and destroys it on exit. Views are built only in Play mode, so EditMode tests still drive the state logic alone.
- Flow: Menu click -> ShowBriefing(n) -> Briefing (timeScale 0, fade 0.5 s, then Enter/Space/button/5 s realtime) -> StartMission (timeScale 1) -> Playing. Esc in Playing -> Paused (timeScale 0, PlayerInput disabled so Enter can't bomb), Esc/Resume -> Playing. Quit/Menu -> QuitToMenu (loads MainMenu, falls back to SetState(Menu) if the scene isn't in the build). Result: Retry -> RetryMission; Next (won and mission < 5) -> NextMission -> briefing of n+1; Menu -> QuitToMenu. NextMission(5) goes to the menu.
- Crash: TriggerCrash disables input and collider at once and calls LoseMission("Player crashed", CrashFallSeconds=1). State becomes Lost and MissionEnded fires immediately (no win can follow; MissionRunner stops the scroll as per B017), while enemies/flak/fx keep running. The wreck falls (gravity 14, drift 1.5 in the facing direction, spin 300 deg/s) via MovePosition/MoveRotation and explodes once on the terrain (HeightAt). The result opens after 1 s of real time (WaitForSecondsRealtime), then timeScale 0. Any state change cancels the pending result.

PUBLIC API added/changed:
- GameManager: `const int MissionCount = 5`; `LoseMission(string reason, float resultDelaySeconds)`; `ShowResultView(bool won, string reason, float delaySeconds)`; `PauseGame()`; `NextMission(int missionIndex)`; `QuitToMenu()`. `ShowBriefing(int)` now really enters Briefing. `ResumeGame()` just leaves Paused (SetState does the cleanup). Leaving Paused always restores timeScale 1.
- ResultView: `static ResultView Create()`, `static void HideAll()`, `static bool IsShowing`; `Show(int, bool, int[, string])` and `Hide()` kept.
- PauseView: `static PauseView Show()`, `static void Hide()`, `static bool IsShowing` (old instance Show/Hide/Resume/Quit/OnEscapePressed/SetGameManager removed; nothing called them).
- BriefingView: `static BriefingView Show(int mission)`, `static void Hide()`, `static bool IsShowing`, `TriggerTransition()` now calls `GameManager.Instance.StartMission(currentMission)` (no more ObjectiveCompleted event).
- PlayerPlane: `public const float CrashFallSeconds = 1f`.
- MainMenuView: `internal static void EnsureEventSystem()`.

NON-LETHAL HITS (code-path check, acceptance item 4):
- Only three paths reach PlayerPlane.TriggerCrash: (a) FixedUpdate when `_health.CurrentDamagePercent >= 100f` (the terrain test feeds this via ApplyDamage(1, Collision) = 100%); (b) OnTriggerEnter2D, only when the other collider belongs to a FighterAI or BomberAI (mid-air collision); (c) ForceCrash, called only by ZeppelinAI.cs:293 and ZeppelinTurret.cs:118 on body contact, after a Collision hit. GameManager.LoseMission / SetState(Lost) is otherwise only reached from MissionRunner objective failure (failAtX, map end).
- Projectile hits on the player: FlakShell damage 1 x damageTaken.flak (15) or x zeppelinTurret (12); EnemyBomb 1 x bomberGunner (10). Health.ApplyDamage only adds to currentDamage, clamps to 100 and raises PlayerDamageChanged (subscribed only by HudView). Nothing subscribes to that event to stop scroll or input, and ScrollController.SetScrollSpeed(0) / PlayerInput.enabled=false appear only in MissionRunner.EndPlayerControl (MissionEnded), GameManager pause, and TriggerCrash. So a 15% hit leaves the plane flying; only reaching 100% (7 flak hits) ends the mission.
- Note: no enemy fires a fighter bullet today (FighterAI only rams; Bullet is player-team), so damageTaken.fighterBullet (8) is currently unused.

VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes, PASS Biplanes.Editor, exit 0. (`tools/unity.sh compile` not run: the editor has the project open.)
- Tests scratch build (B018 method, script in the session scratchpad): runtime DLL from source, then all 10 Tests/EditMode/*.cs against it with ProjectReferences removed and v4.7.1 -> v4.7.2: 0 errors. Nothing left in the project. Not run. MissionEndRulesTests still hold by reading: state/events are immediate, views are skipped outside Play mode, RetryMission still sets _pendingMission and timeScale 1.
- Manual checklist (Unity: Biplanes -> BuildScenes, then Play Game.unity):
  1. Menu -> click Mission 1 -> the briefing fades in over 0.5 s with "Mission 1: <title>", the briefing text, "Begin Mission" and the hint. After 5 s (or Enter/Space/button) the mission starts with the HUD.
  2. Take a flak hit (15%): the damage bar rises, the plane keeps flying and scrolling.
  3. Esc -> PAUSED panel, world frozen, Enter doesn't drop a bomb. Esc again -> resumes. Esc -> Resume button -> resumes. Esc -> Quit -> main menu.
  4. Dive into the ground: input stops at once, the wreck explodes on the ground, enemies keep moving. About 1 s later: MISSION FAILED, "Player crashed", Score, [Retry] [Menu] centred, all text visible and at distinct heights.
  5. Collide with a fighter mid-air: the plane drops and tumbles about 1 s, then the result screen.
  6. Retry -> the same mission restarts (no briefing). Menu -> main menu.
  7. Win a mission: MISSION COMPLETE!, Best (if a previous record) above Score, [Retry] [Next] [Menu]. Next -> briefing of the next mission -> it plays. Win mission 5: no Next; Menu returns to the menu.
  8. The console has no errors and no "No ResultView/PauseView found" warnings.
- Risks: the result screen ignores Esc on purpose. Retry skips the briefing. A ground crash explodes on the first tick (no room to fall), so the "fall" is only visible for air crashes and collisions.

REQUESTS:
- orchestrator: ARCHITECTURE.md §3 UI/GameManager: add the APIs above and the rule "each GameManager state builds its own view on its own overlay canvas on entry and destroys it on exit (Briefing 105, Pause 110, Result 120; Menu 100); views only in Play mode". §3 Player: "TriggerCrash: input and collider off at once, LoseMission(reason, CrashFallSeconds) (state Lost + MissionEnded immediately, result screen after 1 s of real time), scripted fall/tumble with MovePosition/MoveRotation until the terrain".
- orchestrator: UI_SPEC.md §3.1: remove the "Until the Briefing flow is wired (T937)" sentence; a click now opens the briefing. §3.2: the briefing freezes the world (timeScale 0) and also appears for result "Next". §3.4: Quit = GameManager.QuitToMenu (MainMenu scene). §3.5: a Menu button is always shown (Won: Retry/Next/Menu, Lost: Retry/Menu), and on a crash the screen opens 1 s after the crash.
- orchestrator (optional): the crash constants (fall seconds, gravity, drift, spin) live in PlayerPlane as named consts. If they should be tunable, a Balance.json `player.crashFall*` block is a follow-up for mechanics.
- orchestrator: ProjectBuilder's comment about "Game boots first ... until T034" is stale (noted in T934) and still harmless.
- qa: run the manual checklist above and MissionEndRulesTests / MissionRestartTests in the Test Runner.
