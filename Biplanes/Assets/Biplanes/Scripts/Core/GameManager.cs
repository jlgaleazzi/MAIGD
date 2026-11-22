using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // Keyboard.current for Esc (project uses Input System only)
using UnityEngine.SceneManagement;
using Biplanes.Audio;
using Biplanes.UI;

namespace Biplanes.Core {

    /// <summary>
    /// Game state machine singleton.
    ///
    /// Central hub for the high-level lifecycle:
    ///   Menu → Briefing → Playing → Paused / Won / Lost
    ///
    /// Each state owns one code-built view with its own overlay canvas (ARCHITECTURE §0: no
    /// prefabs, no scene objects): Menu = MainMenuView, Briefing = BriefingView, Paused =
    /// PauseView, Won/Lost = ResultView. Entering a state creates its view, leaving it
    /// destroys the view, so no state can end on an empty, frozen screen (T937).
    /// Views are only built in Play mode; in EditMode tests the state logic runs alone.
    /// </summary>
    public class GameManager : MonoBehaviour {

        // ── States ───────────────────────────────────────────────────────

        public enum GameState {
            Menu,
            Briefing,
            Playing,
            Paused,
            Won,
            Lost
        }

        // ── Singleton ────────────────────────────────────────────────────

        private static GameManager _instance;

        /// <summary>Singleton accessor — created on first access if needed.</summary>
        public static GameManager Instance {
            get {
                if (_instance == null) {
                    var go = new GameObject("GameManager");
                    _instance = go.AddComponent<GameManager>();
                    // DontDestroyOnLoad throws outside Play mode (EditMode tests, B047).
                    if (Application.isPlaying) DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        /// <summary>The manager if it exists, or a new one in Play mode. Null outside Play mode
        /// when none exists (EditMode tests), so gameplay callers fall back to GameEvents
        /// instead of building a manager (same pattern as AudioManager/FxManager.GetOrNull).</summary>
        public static GameManager GetOrNull() {
            if (_instance != null) return _instance;
            return Application.isPlaying ? Instance : null;
        }

        // ── Runtime state ────────────────────────────────────────────────

        private GameState _currentState;

        /// <summary>Current game state.</summary>
        public GameState CurrentState => _currentState;

        /// <summary>Mission index being played (1-5).</summary>
        public int ActiveMissionIndex { get; private set; }

        /// <summary>Number of missions (GDD: 5). Next after the last one returns to the menu.</summary>
        public const int MissionCount = 5;

        /// <summary>Scene names created by ProjectBuilder.BuildScenes (ARCHITECTURE §6).</summary>
        private const string MenuSceneName = "MainMenu";
        private const string GameSceneName = "Game";

        /// <summary>Mission picked in the MainMenu scene, started once Game finishes loading (0 = none).</summary>
        private int _pendingMission;

        /// <summary>Delayed result screen (crash fall), cancelled by any state change.</summary>
        private Coroutine _pendingResult;

        /// <summary>PlayerInput components disabled while paused (Space would still drop
        /// bombs at timeScale 0, because PlayerInput polls in Update). Re-enabling runs
        /// PlayerInput.OnEnable, which resets its PlayerInputGate, so the click or Space
        /// still held from closing the pause screen is spent until released.</summary>
        private readonly List<Behaviour> _pausedInputs = new List<Behaviour>();

        // ── Unity lifecycle ──────────────────────────────────────────────

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            SetState(GameState.Menu);
        }

        private void OnDestroy() {
            if (_instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>Boot into the main menu (UI_SPEC §1 flow: MainMenu → Briefing → Playing).</summary>
        private void Start() {
            if (_currentState == GameState.Menu) EnterMenu();
        }

        /// <summary>Esc pauses while Playing and resumes while Paused (UI_SPEC §3.4).</summary>
        private void Update() {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

            if (_currentState == GameState.Playing) PauseGame();
            else if (_currentState == GameState.Paused) ResumeGame();
        }

        /// <summary>
        /// MainMenu scene (Quit / Menu) → show the menu. Game scene loaded for a mission
        /// picked from the MainMenu scene or a Retry → start that mission now that
        /// MissionRunner exists.
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (scene.name == MenuSceneName) {
                if (_currentState == GameState.Menu) EnterMenu(); else SetState(GameState.Menu);
            } else if (scene.name == GameSceneName && _pendingMission > 0) {
                int mission = _pendingMission;
                _pendingMission = 0;
                StartMission(mission);
            }
        }

        // ── State transitions ───────────────────────────────────────────

        /// <summary>
        /// Transition to a new game state. Delegates to the appropriate view / system.
        /// </summary>
        public void SetState(GameState newState) {
            string reason = newState == GameState.Won ? DefaultWinReason : DefaultLossReason;
            SetState(newState, reason, 0f);
        }

        /// <summary>Default MissionEnded reasons for SetState(Won) / SetState(Lost).</summary>
        public const string DefaultWinReason = "All objectives completed";
        public const string DefaultLossReason = "Player crashed";

        /// <summary>
        /// The mission is lost for `reason` (shown on the result screen and passed with
        /// GameEvents.MissionEnded). Ignored if the mission already ended (B016, P2-1).
        /// </summary>
        public void LoseMission(string reason) {
            LoseMission(reason, 0f);
        }

        /// <summary>
        /// As LoseMission(reason), but the result screen opens `resultDelaySeconds` (real
        /// time) later. The state becomes Lost and MissionEnded fires at once, so no win can
        /// follow; the delay only lets the world keep running while the plane falls (T937).
        /// </summary>
        public void LoseMission(string reason, float resultDelaySeconds) {
            SetState(GameState.Lost, string.IsNullOrEmpty(reason) ? DefaultLossReason : reason,
                Mathf.Max(0f, resultDelaySeconds));
        }

        /// <summary>True in Won or Lost: the current mission has ended.</summary>
        public bool IsMissionOver => _currentState == GameState.Won || _currentState == GameState.Lost;

        private void SetState(GameState newState, string reason, float resultDelay) {
            if (_currentState == newState) return;

            // A mission ends once: a crash after a win (or a win after a loss) must not flip
            // the result. Only StartMission/Retry/Menu leave Won or Lost (T936 P2-1).
            bool ending = newState == GameState.Won || newState == GameState.Lost;
            if (ending && IsMissionOver) {
                Debug.Log($"[GameManager] Ignoring {newState} ('{reason}'): mission already ended as {_currentState}.");
                return;
            }

            // Leave current state first: every state removes its own view.
            switch (_currentState) {
                case GameState.Menu:
                    MainMenuView.Hide();
                    Time.timeScale = 1f;
                    break;
                case GameState.Briefing:
                    BriefingView.Hide();
                    break;
                case GameState.Paused:
                    PauseView.Hide();
                    RestorePausedInputs();
                    Time.timeScale = 1f;
                    break;
                case GameState.Playing:
                    break;
                case GameState.Won:
                case GameState.Lost:
                    HideResultView();
                    break;
            }

            _currentState = newState;
            Debug.Log("[GameManager] State changed: " + _currentState);

            // Enter new state — delegate to appropriate systems.
            switch (newState) {
                case GameState.Menu:
                    EnterMenu();
                    break;
                case GameState.Briefing:
                    EnterBriefing();
                    break;
                case GameState.Playing:
                    ShowHud();
                    break;
                case GameState.Paused:
                    ShowPauseView();
                    break;
                case GameState.Won:
                    ShowResultView(true, reason, resultDelay);
                    break;
                case GameState.Lost:
                    ShowResultView(false, reason, resultDelay);
                    break;
            }

            // Raise the mission-ended event when entering Won or Lost.
            if (ending) {
                GameEvents.RaiseMissionEnded(newState == GameState.Won, reason);
            }
        }

        // ── Mission lifecycle ───────────────────────────────────────────

        /// <summary>
        /// Start a mission (from Briefing, a Retry reload or the menu scene) and go to Playing.
        /// The actual scene / entity creation is delegated to MissionRunner.
        /// </summary>
        public void StartMission(int missionIndex) {
            ActiveMissionIndex = missionIndex;
            Debug.Log("[GameManager] Starting mission " + missionIndex);

            // Transition: drop any overlay of the previous mission and unfreeze time
            // (the result screen and the briefing both run at timeScale 0).
            HideResultView();
            PauseView.Hide();
            Time.timeScale = 1f;

            // Delegate initialization to the MissionRunner in the current scene.
            var runner = FindFirstObjectByType<Missions.MissionRunner>();
            if (runner == null && SceneManager.GetActiveScene().name != GameSceneName) {
                // Picked from the MainMenu scene: load Game, OnSceneLoaded starts the mission.
                _pendingMission = missionIndex;
                SceneManager.LoadScene(GameSceneName);
                return;
            }
            if (runner != null) {
                Debug.Log("[GameManager] Found MissionRunner — calling StartMission.");
                runner.StartMission(missionIndex);
            } else {
                Debug.LogWarning("[GameManager] No MissionRunner found in scene! Entities will not spawn.");
            }

            // Transition the state machine into Playing (hides the briefing).
            SetState(GameState.Playing);
        }

        /// <summary>Pause the running mission (Esc while Playing). Opens PauseView.</summary>
        public void PauseGame() {
            if (_currentState != GameState.Playing) return;
            SetState(GameState.Paused);
        }

        /// <summary>Resume from Paused back to Playing (Esc again or PauseView Resume).</summary>
        public void ResumeGame() {
            if (_currentState != GameState.Paused) return;
            // Leaving Paused hides PauseView, re-enables the input and restores timeScale 1.
            SetState(GameState.Playing);
        }

        /// <summary>
        /// Retry the given mission: reload the Game scene for a clean world, and start the
        /// same mission from OnSceneLoaded through _pendingMission (B013). The state stays
        /// Won/Lost until that StartMission moves it to Playing.
        /// </summary>
        public void RetryMission(int missionIndex) {
            if (missionIndex < 1 || missionIndex > MissionCount) missionIndex = ActiveMissionIndex;
            if (missionIndex < 1 || missionIndex > MissionCount) {
                Debug.LogWarning($"[GameManager] RetryMission: no valid mission to retry ({missionIndex}).");
                return;
            }
            ActiveMissionIndex = missionIndex;
            HideResultView();
            Time.timeScale = 1f; // the result screen froze time; the reloaded mission must run.
            Debug.Log("[GameManager] Retrying mission " + missionIndex);

            _pendingMission = missionIndex;
            SceneManager.LoadScene(GameSceneName);
        }

        /// <summary>
        /// Result "Next": the briefing of the mission after `missionIndex`, or the main menu
        /// when `missionIndex` was the last mission.
        /// </summary>
        public void NextMission(int missionIndex) {
            if (missionIndex < 1) missionIndex = ActiveMissionIndex;
            if (missionIndex >= MissionCount) {
                QuitToMenu();
                return;
            }
            ShowBriefing(missionIndex + 1);
        }

        /// <summary>
        /// Leave the mission for the main menu (PauseView Quit, ResultView Menu). Loads the
        /// MainMenu scene for a clean world; OnSceneLoaded switches to Menu. If that scene
        /// is not in the build, shows the menu over the current scene instead.
        /// </summary>
        public void QuitToMenu() {
            _pendingMission = 0;
            CancelPendingResult();
            HideResultView();
            PauseView.Hide();
            BriefingView.Hide();
            RestorePausedInputs();
            Time.timeScale = 1f;

            if (Application.isPlaying && Application.CanStreamedLevelBeLoaded(MenuSceneName)) {
                SceneManager.LoadScene(MenuSceneName);
            } else {
                SetState(GameState.Menu);
            }
        }

        // ── View helpers (delegated to view components) ────────────────

        /// <summary>Show the main menu overlay.</summary>
        public void ShowMainMenu() {
            SetState(GameState.Menu);
        }

        /// <summary>Menu entry: freeze any leftover mission world and show MainMenuView.</summary>
        private void EnterMenu() {
            CancelPendingResult();
            HideResultView();
            PauseView.Hide();
            BriefingView.Hide();
            Time.timeScale = 0f; // uGUI runs on unscaled time; restored when leaving Menu.
            MainMenuView.Show();
        }

        /// <summary>
        /// Show the briefing screen for a mission (menu pick or result Next, UI_SPEC §3.2).
        /// BriefingView then calls StartMission(missionIndex).
        /// </summary>
        public void ShowBriefing(int missionIndex) {
            ActiveMissionIndex = Mathf.Clamp(missionIndex, 1, MissionCount);
            Debug.Log("[GameManager] Showing briefing for mission " + ActiveMissionIndex);
            if (_currentState == GameState.Briefing) {
                EnterBriefing(); // already briefing: rebuild it for the new mission
            } else {
                SetState(GameState.Briefing);
            }
        }

        /// <summary>Briefing entry: freeze whatever world is behind it and show BriefingView.</summary>
        private void EnterBriefing() {
            CancelPendingResult();
            // The briefing runs on unscaled time. Freezing the world keeps a finished
            // mission (Next) from ending itself again behind the briefing.
            Time.timeScale = 0f;
            if (!Application.isPlaying) return;
            BriefingView.Show(ActiveMissionIndex);
        }

        /// <summary>Show the HUD (health bar, bomb counter, score).</summary>
        public void ShowHud() {
            Debug.Log("[GameManager] Showing HUD");
            // MissionRunner creates / destroys the HUD during mission start/end.
        }

        /// <summary>Show the pause overlay: freeze time, take the controls away, open PauseView.</summary>
        public void ShowPauseView() {
            Time.timeScale = 0f;
            DisableInputsForPause();
            if (!Application.isPlaying) return;
            PauseView.Show();
            Debug.Log("[GameManager] Showing pause view");
        }

        /// <summary>Show the mission result overlay now.</summary>
        public void ShowResultView(bool won, string reason) {
            ShowResultView(won, reason, 0f);
        }

        /// <summary>
        /// Show the mission result overlay after `delaySeconds` of real time (0 = now). The
        /// world keeps running until it opens; then timeScale goes to 0. ResultView is
        /// created on demand with its own canvas, so this never ends with only a warning.
        /// </summary>
        public void ShowResultView(bool won, string reason, float delaySeconds) {
            CancelPendingResult();
            Debug.LogFormat("[GameManager] Mission result: won={0} reason='{1}'", won, reason);
            if (!Application.isPlaying) return; // EditMode: state and events only, no UI

            if (delaySeconds > 0f) {
                _pendingResult = StartCoroutine(OpenResultAfter(won, reason, delaySeconds));
            } else {
                OpenResultView(won, reason);
            }
        }

        private IEnumerator OpenResultAfter(bool won, string reason, float delaySeconds) {
            yield return new WaitForSecondsRealtime(delaySeconds);
            _pendingResult = null;
            // Only if nothing (Retry, Menu, a new mission) moved the state on meanwhile.
            GameState expected = won ? GameState.Won : GameState.Lost;
            if (_currentState == expected) OpenResultView(won, reason);
        }

        private void OpenResultView(bool won, string reason) {
            Time.timeScale = 0f;
            // Win / Lose sting, once per mission end: the result screen opens once per Won /
            // Lost (SetState ignores a second ending; Retry / Next / Menu never reopen it). PlayUi
            // because time is frozen from here on (T952).
            // A second ShowResultView call while the screen is up only refreshes it, silently.
            if (!ResultView.IsShowing) {
                var audio = AudioManager.GetOrNull();
                if (audio != null) audio.PlayUi(won ? SfxId.Win : SfxId.Lose);
            }
            // Read final score from ScoreKeeper (singleton, created by MissionRunner).
            int finalScore = ScoreKeeper.Instance != null ? ScoreKeeper.Instance.CurrentScore : 0;
            ResultView.Create().Show(ActiveMissionIndex, won, finalScore, reason);
        }

        private void CancelPendingResult() {
            if (_pendingResult != null) {
                StopCoroutine(_pendingResult);
                _pendingResult = null;
            }
        }

        /// <summary>Hide the result overlay (destroys its canvas). Safe when none exists.</summary>
        private void HideResultView() {
            CancelPendingResult();
            ResultView.HideAll();
        }

        // ── Pause input gate ────────────────────────────────────────────

        private void DisableInputsForPause() {
            var inputs = FindObjectsByType<Player.PlayerInput>(FindObjectsSortMode.None);
            foreach (var input in inputs) {
                if (input == null || !input.enabled) continue;
                input.enabled = false;
                _pausedInputs.Add(input);
            }
        }

        private void RestorePausedInputs() {
            foreach (var input in _pausedInputs) {
                if (input != null) input.enabled = true;
            }
            _pausedInputs.Clear();
        }
    }
}
