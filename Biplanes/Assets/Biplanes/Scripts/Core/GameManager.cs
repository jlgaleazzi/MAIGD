using UnityEngine;
using Biplanes.Core;
using Biplanes.UI;

namespace Biplanes.Core {

    /// <summary>
    /// Game state machine singleton.
    ///
    /// Central hub for the high-level lifecycle:
    ///   Menu → Briefing → Playing → Paused / Won / Lost
    ///
    /// Other systems (PlayerPlane, PauseView, ResultView) call through
    /// GameManager.Instance to transition between states. The actual view
    /// logic is delegated — each view class builds and shows its own UI
    /// (ARCHITECTURE §0: no prefabs).
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
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        // ── Runtime state ────────────────────────────────────────────────

        private GameState _currentState;

        /// <summary>Current game state.</summary>
        public GameState CurrentState => _currentState;

        /// <summary>Mission index being played (1-5).</summary>
        public int ActiveMissionIndex { get; private set; }

        // ── Unity lifecycle ──────────────────────────────────────────────

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            SetState(GameState.Menu);
        }

        /// <summary>Called once after all Awake() calls finish. Used to auto-transition from Menu to Playing if MainMenu is disabled (e.g., direct M0 testing)."</summary>
        private void Start() {
            if (_currentState != GameState.Menu) return;

            // Auto-bridge: if there is no BriefingView in the scene, transition directly to Playing.
            var briefing = FindObjectOfType<BriefingView>();
            if (briefing == null) {
                Debug.Log("[GameManager] No BriefingView found — auto-transitioning to Playing.");
                ActiveMissionIndex = 1; // Default to Mission 1 for direct play
                StartMission(1); // StartMission transitions to Playing itself.
            }
        }

        // ── State transitions ───────────────────────────────────────────

        /// <summary>
        /// Transition to a new game state. Delegates to the appropriate view / system.
        /// </summary>
        public void SetState(GameState newState) {
            if (_currentState == newState) return;

            // Leave current state first.
            switch (_currentState) {
                case GameState.Paused:
                    // Don't resume time here — OnResumeClicked does it.
                    break;
                case GameState.Playing:
                    // Clean up HUD before leaving playing state.
                    HideHud();
                    break;
                case GameState.Won:
                case GameState.Lost:
                    // Hide result view on exit if we're transitioning away.
                    break;
            }

            _currentState = newState;
            Debug.Log("[GameManager] State changed: " + _currentState);

            // Enter new state — delegate to appropriate systems.
            switch (newState) {
                case GameState.Menu:
                    ShowMainMenu();
                    break;
                case GameState.Briefing:
                    ShowBriefing(ActiveMissionIndex);
                    break;
                case GameState.Playing:
                    ShowHud();
                    break;
                case GameState.Paused:
                    ShowPauseView();
                    break;
                case GameState.Won:
                    ShowResultView(true, "All objectives completed");
                    break;
                case GameState.Lost:
                    ShowResultView(false, "Player crashed");
                    break;
            }

            // Raise the mission-ended event when entering Won or Lost.
            if (newState == GameState.Won || newState == GameState.Lost) {
                bool won = (newState == GameState.Won);
                string reason = won ? "All objectives completed" : "Player crashed";
                GameEvents.RaiseMissionEnded(won, reason);
            }
        }

        // ── Mission lifecycle ───────────────────────────────────────────

        /// <summary>
        /// Start a mission (transition from Briefing or Menu to Playing).
        /// The actual scene / entity creation is delegated to MissionRunner.
        /// </summary>
        public void StartMission(int missionIndex) {
            ActiveMissionIndex = missionIndex;
            Debug.Log("[GameManager] Starting mission " + missionIndex);

            // Transition: hide previous views, load mission entities via MissionRunner.
            HideHud();
            HideResultView();
            
            // Delegate initialization to the MissionRunner in the current scene.
            var runner = FindObjectOfType<Missions.MissionRunner>();
            if (runner != null) {
                Debug.Log("[GameManager] Found MissionRunner — calling StartMission.");
                runner.StartMission(missionIndex);
            } else {
                Debug.LogWarning("[GameManager] No MissionRunner found in scene! Entities will not spawn.");
            }

            // Transition the state machine into Playing.
            SetState(GameState.Playing);
        }

        /// <summary>Resume from Paused back to Playing.</summary>
        public void ResumeGame() {
            if (_currentState != GameState.Paused) return;

            // Delegate actual resume to PauseView — it controls timeScale and its own overlay.
            var pauseView = FindObjectOfType<PauseView>();
            if (pauseView != null) {
                pauseView.Resume();
            } else {
                Time.timeScale = 1f;
            }

            SetState(GameState.Playing);
        }

        /// <summary>Retry the given mission (reset state and restart).</summary>
        public void RetryMission(int missionIndex) {
            ActiveMissionIndex = missionIndex;
            HideResultView();
            ShowHud();
            // MissionRunner handles scene reset internally.
            Debug.Log("[GameManager] Retrying mission " + missionIndex);

            // For now, reload the main scene — MissionRunner will detect the
            // same mission index and re-spawn everything.
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainScene");

            SetState(GameState.Playing);
        }

        // ── View helpers (delegated to view components) ────────────────

        /// <summary>Show the main menu overlay.</summary>
        public void ShowMainMenu() {
            SetState(GameState.Menu);
        }

        /// <summary>Show the briefing screen for a specific mission.</summary>
        public void ShowBriefing(int missionIndex) {
            ActiveMissionIndex = missionIndex;
            Debug.Log("[GameManager] Showing briefing for mission " + missionIndex);
            // Actual briefing UI is built by BriefingView via GameEvents or direct find.
        }

        /// <summary>Show the HUD (health bar, bomb counter, score).</summary>
        public void ShowHud() {
            Debug.Log("[GameManager] Showing HUD");
            // HUD visibility is controlled by the HUD component via OnEnable/OnDisable.
            // MissionRunner creates / destroys it during mission start/end.
        }

        /// <summary>Show the pause overlay.</summary>
        public void ShowPauseView() {
            var pauseView = FindObjectOfType<PauseView>();
            if (pauseView != null) {
                Time.timeScale = 0f;
                pauseView.Show();
                Debug.Log("[GameManager] Showing pause view");
            } else {
                Debug.LogWarning("[GameManager] No PauseView found in scene for ShowPauseView");
            }
        }

        /// <summary>Show the mission result overlay.</summary>
        public void ShowResultView(bool won, string reason) {
            var resultView = FindObjectOfType<ResultView>();
            if (resultView != null) {
                Time.timeScale = 0f;
                // Read final score from ScoreKeeper (singleton, created by MissionRunner).
                int finalScore = ScoreKeeper.Instance != null ? ScoreKeeper.Instance.CurrentScore : 0;
                resultView.Show(ActiveMissionIndex, won, finalScore);
            } else {
                Debug.LogWarning("[GameManager] No ResultView found in scene for ShowResultView");
            }

            Debug.LogFormat("[GameManager] Mission result: won={0} reason='{1}'", won, reason);
        }

        /// <summary>Hide the HUD (called when leaving Playing state).</summary>
        private void HideHud() {
            // HUD will hide itself via OnDisable when destroyed by MissionRunner.
        }

        /// <summary>Hide the result overlay.</summary>
        private void HideResultView() {
            var resultView = FindObjectOfType<ResultView>();
            if (resultView != null) {
                Time.timeScale = 1f;
                resultView.Hide();
            }
        }
    }
}
