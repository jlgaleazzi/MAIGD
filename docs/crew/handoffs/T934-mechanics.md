TASK: T934
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/UI/MainMenuView.cs (new)
- Biplanes/Assets/Biplanes/Scripts/Core/GameManager.cs (menu wiring, +46/-10 lines incl. comments)
- docs/crew/handoffs/T934-mechanics.md
SUMMARY:
- MainMenuView (UI_SPEC §3.1) builds its own overlay canvas from code: paper backdrop, title_logo (text "BIPLANES" fallback), 5 mission buttons "Mission N + JSON title" (missions > SaveSystem.GetUnlockedMission() dimmed and non-interactable), and a How to Play toggle with the controls. No Quit button (UI_SPEC doesn't list one). Clicking an unlocked mission calls GameManager.Instance.StartMission(n).
- GameManager now boots into Menu (Start -> EnterMenu shows MainMenuView, timeScale 0) instead of auto-starting mission 1. Leaving Menu hides the view and restores timeScale 1. Loading the "MainMenu" scene (PauseView.Quit) switches to Menu. Picking a mission while no MissionRunner exists loads "Game" and starts the mission on sceneLoaded. Playing/Paused/Won/Lost are unchanged. No ProjectBuilder change needed: Game is build scene 0 and now opens on the menu.

PUBLIC API (Biplanes.UI.MainMenuView):
- `public static MainMenuView Show()` creates "MainMenuCanvas" (sortingOrder 100, 1280x720 scaler) plus an EventSystem/InputSystemUIInputModule if the scene has none. If the menu already exists, it re-reads the unlocked mission and returns it.
- `public static void Hide()` destroys the menu canvas. Safe when none exists.
- `public static bool IsShowing`
GameManager: the new code is all private (EnterMenu, OnSceneLoaded, OnDestroy, _pendingMission, the scene-name consts). No public signature changed. ShowMainMenu() now really shows the menu, through SetState(Menu) -> EnterMenu.

VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes, PASS Biplanes.Editor, exit 0. (unity.sh not run: the editor is open.)
- Manual playtest (Unity: Biplanes -> BuildScenes, then Play Game.unity):
  1. Play opens on the blueprint menu: logo or "BIPLANES", 5 buttons in one row with mission titles, and "How to Play" below. The world must not be visible or moving.
  2. Fresh save (PlayerPrefs.DeleteAll or key unlocked_mission absent): only Mission 1 is bright. Missions 2-5 are dim, show no hover tint, and clicking them does nothing.
  3. Set PlayerPrefs unlocked_mission=3, then replay: Missions 1-3 are clickable and 4-5 are dim.
  4. Hovering an unlocked button gives a pencil tint. Pressing it gives a hostile red tint.
  5. "How to Play" toggles the controls panel on and off.
  6. Clicking Mission 1 removes the menu, starts mission 1 (runway takeoff) and shows the HUD. The game is not frozen (timeScale 1).
  7. The console has no errors. The only warnings expected are missing-sprite warnings, if a sprite really is missing.

REQUESTS:
- orchestrator: Briefing isn't in the flow yet. Nothing creates BriefingView, GameManager.ShowBriefing only logs, and BriefingView.TriggerTransition raises ObjectiveCompleted instead of calling GameManager.StartMission. So the menu goes straight to StartMission. Proposed mechanics task: the menu calls GameManager.ShowBriefing(n) -> SetState(Briefing) creates a BriefingView, and TriggerTransition calls GameManager.Instance.StartMission(currentMission). Touches BriefingView.cs, GameManager.cs and MainMenuView.OnMissionClicked.
- orchestrator: PauseView and ResultView are never instantiated anywhere, and nothing handles Esc. So Pause -> Quit and Results -> menu can't be reached in play yet, although the GameManager side is now wired. Proposed mechanics task: create both views (for example on GameManager's state entry) and add an Esc handler.
- orchestrator: GameManager.RetryMission and the ResultView fallback load the scene "MainScene", which doesn't exist (ProjectBuilder builds "Game"). That's a P1 bug for mechanics.
- qa: the other views (Briefing/Pause/Result/Hud) create legacy `Text` without assigning `font`. At runtime the font is null, so their text probably doesn't render. MainMenuView sets `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`. Please check this in play mode.
- orchestrator: ARCHITECTURE §3 UI and UI_SPEC §3.1 changes. Locked missions are shown dimmed rather than hidden (that's what the brief asked for, and it differs from UI_SPEC's "only shown if unlocked"). Game.unity boots into the menu, and the ProjectBuilder comment about "Game boots first ... until T034" is now stale but harmless. The menu currently skips Briefing (see above).
