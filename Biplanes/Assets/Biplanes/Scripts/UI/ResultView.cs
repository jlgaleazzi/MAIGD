using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Biplanes.Core;

namespace Biplanes.UI {

    /// <summary>
    /// Mission results overlay shown on MissionEnded (UI_SPEC §3.5).
    /// Two layouts: "Won" → MISSION COMPLETE! + score + Retry/Next buttons.
    ///               "Lost" → MISSION FAILED  + score + Retry button only.
    ///
    /// Built entirely from code — no prefabs (ARCHITECTURE §0 rule 1).
    /// </summary>
    public class ResultView : MonoBehaviour {

        // ── Exposed config ────────────────────────────────────────────────

        [SerializeField] private float _resultPanelWidth  = 420f;
        [SerializeField] private float _resultPanelHeight = 300f;
        [SerializeField] private float _buttonWidth       = 180f;
        [SerializeField] private float _buttonHeight      = 50f;
        [SerializeField] private float _buttonSpacing     = 20f;

        // ── Internal state ────────────────────────────────────────────────

        /// <summary>CanvasGroup wrapping this overlay (set in Awake).</summary>
        private CanvasGroup _canvasGroup;

        /// <summary>Title text element (MISSION COMPLETE! / MISSION FAILED).</summary>
        private Text _titleText;

        /// <summary>Score display text element.</summary>
        private Text _scoreText;

        /// <summary>Previous best score label (shown only when a record exists).</summary>
        private Text _bestScoreText;

        /// <summary>Retry button GameObject (always present but may be disabled).</summary>
        private Button _retryButton;

        /// <summary>Next button GameObject (hidden for the final mission).</summary>
        private Button _nextButton;

        /// <summary>The panel background image for hiding the Next button.</summary>
        private Image _nextButtonPanel;

        /// <summary>Current mission index (1-5) set by GameManager before Show() is called.</summary>
        private int _currentMission;

        /// <summary>Whether this result is for a win or loss.</summary>
        private bool _isWon;

        // ── Unity lifecycle ───────────────────────────────────────────────

        private void Awake() {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                Debug.LogWarning("ResultView: CanvasGroup missing — added one.");
            }
            // Start hidden.
            _canvasGroup.alpha       = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            BuildVisuals();
        }

        private void OnDestroy() {
            ClearSubscriptions();
        }

        // ── Visual construction (no prefabs) ───────────────────────────────

        /// <summary>
        /// Build the result panel hierarchy per UI_SPEC §3.5:
        ///   [ RESULT PANEL ]
        ///     MISSION COMPLETE!  /  MISSION FAILED
        ///     Score: XXXX
        ///     Best: XXXX              (when a previous best exists)
        ///     ┌──────────┐  ┌──────────┐
        ///     │  Retry   │  │   Next   │   (Next hidden on last mission)
        ///     └──────────┘  └──────────┘
        /// </summary>
        private void BuildVisuals() {
            // Container panel.
            var panel = new GameObject("ResultPanel");
            panel.transform.SetParent(transform, false);

            var panelRect = panel.GetComponent<RectTransform>() ?? panel.AddComponent<RectTransform>();
            panelRect.anchorMin      = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax      = new Vector2(0.5f, 0.5f);
            panelRect.pivot          = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta      = new Vector2(_resultPanelWidth, _resultPanelHeight);

            var panelImg = panel.AddComponent<Image>();
            var bgSprite = Resources.Load<Sprite>("Sprites/ui/ui_panel");
            if (bgSprite != null) {
                panelImg.sprite = bgSprite;
            } else {
                Debug.LogWarning("ResultView: Missing sprite 'Sprites/ui/ui_panel', using 1×1 white fallback.");
                panelImg.sprite = CreateWhiteSprite();
            }
            panelImg.color = Color.white;
            panelImg.raycastTarget = false;

            // ── Title text ────────────────────────────────────────────────
            var titleObj = new GameObject("ResultTitle");
            titleObj.transform.SetParent(transform, false);

            var titleRect = titleObj.GetComponent<RectTransform>() ?? titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin      = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax      = new Vector2(0.5f, 0.5f);
            titleRect.pivot          = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta      = new Vector2(_resultPanelWidth - 60f, 60f);

            _titleText = titleObj.AddComponent<Text>();
            _titleText.text       = "MISSION COMPLETE!";
            _titleText.fontStyle  = FontStyle.Bold;
            _titleText.alignment  = TextAnchor.MiddleCenter;
            _titleText.fontSize   = 32;
            // Win colour is hostile red for dramatic effect.
            _titleText.color      = new Color(0.9f, 0.34f, 0.18f, 1f);

            // ── Score text ────────────────────────────────────────────────
            var scoreObj = new GameObject("ResultScore");
            scoreObj.transform.SetParent(transform, false);

            var scoreRect = scoreObj.GetComponent<RectTransform>() ?? scoreObj.AddComponent<RectTransform>();
            scoreRect.anchorMin      = new Vector2(0.5f, 0.5f);
            scoreRect.anchorMax      = new Vector2(0.5f, 0.5f);
            scoreRect.pivot          = new Vector2(0.5f, 0.5f);
            scoreRect.sizeDelta      = new Vector2(_resultPanelWidth - 80f, 40f);

            _scoreText = scoreObj.AddComponent<Text>();
            _scoreText.text       = "Score: 0";
            _scoreText.fontStyle  = FontStyle.Normal;
            _scoreText.alignment  = TextAnchor.MiddleCenter;
            _scoreText.fontSize   = 26;
            _scoreText.color      = new Color(0.91f, 0.94f, 0.98f, 1f); // ink #E8F1FA

            // ── Best score label (initially hidden) ───────────────────────
            var bestObj = new GameObject("ResultBestScore");
            bestObj.transform.SetParent(transform, false);

            var bestRect = bestObj.GetComponent<RectTransform>() ?? bestObj.AddComponent<RectTransform>();
            bestRect.anchorMin      = new Vector2(0.5f, 0.5f);
            bestRect.anchorMax      = new Vector2(0.5f, 0.5f);
            bestRect.pivot          = new Vector2(0.5f, 0.5f);
            bestRect.sizeDelta      = new Vector2(_resultPanelWidth - 80f, 30f);

            _bestScoreText = bestObj.AddComponent<Text>();
            _bestScoreText.text       = "";
            _bestScoreText.fontStyle  = FontStyle.Normal;
            _bestScoreText.alignment  = TextAnchor.MiddleCenter;
            _bestScoreText.fontSize   = 18;
            _bestScoreText.color      = new Color(0.62f, 0.71f, 0.8f, 1f); // pencil #9DB4CC

            // ── Retry button ──────────────────────────────────────────────
            var retryBtnObj = CreateButton("RetryButton", "Retry", _buttonWidth, _buttonHeight, -50f);
            _retryButton    = retryBtnObj.GetComponent<Button>();
            if (_retryButton != null) {
                _retryButton.onClick.AddListener(OnRetryClicked);
            }

            // ── Next button (will be hidden on final mission) ────────────
            var nextBtnObj  = CreateButton("NextButton", "Next", _buttonWidth, _buttonHeight, -50f);
            _nextButton     = nextBtnObj.GetComponent<Button>();
            _nextButtonPanel = nextBtnObj.GetComponent<Image>();

            if (_nextButton != null) {
                // Position to the right of Retry.
                _nextButton.transform.localPosition = new Vector3(110f, -50f, 0f);

                _nextButton.onClick.AddListener(OnNextClicked);
            }
        }

        /// <summary>
        /// Create a centered button child at a given vertical offset from centre.
        /// Returns the GameObject for caller to attach extra components if needed.
        /// </summary>
        private GameObject CreateButton(string name, string buttonText, float width, float height, float yOffsetFromCentre) {
            var btnObj = new GameObject(name);
            btnObj.transform.SetParent(transform, false);

            var btnRect = btnObj.GetComponent<RectTransform>() ?? btnObj.AddComponent<RectTransform>();
            btnRect.anchorMin      = new Vector2(0.5f, 0.5f);
            btnRect.anchorMax      = new Vector2(0.5f, 0.5f);
            btnRect.pivot          = new Vector2(0.5f, 0.5f);
            btnRect.sizeDelta      = new Vector2(width, height);
            btnRect.localPosition  = new Vector3(0f, yOffsetFromCentre, 0f);

            // Button background image.
            var bgSprite = Resources.Load<Sprite>("Sprites/ui/ui_button");
            if (bgSprite != null) {
                var btnImg = btnObj.AddComponent<Image>();
                btnImg.sprite     = bgSprite;
                btnImg.color      = Color.white;
                btnImg.raycastTarget = true; // enable raycast for button click.
            }

            // Button label text (centre of button).
            var txtObj = new GameObject(name + "Text");
            txtObj.transform.SetParent(btnObj.transform, false);

            var txtRect = txtObj.GetComponent<RectTransform>() ?? txtObj.AddComponent<RectTransform>();
            txtRect.anchorMin   = Vector2.zero;
            txtRect.anchorMax   = Vector2.one;
            txtRect.offsetMin   = new Vector3(8f, 4f);
            txtRect.offsetMax   = new Vector3(-8f, -4f);

            var txt = txtObj.AddComponent<Text>();
            txt.text       = buttonText;
            txt.fontStyle  = FontStyle.Bold;
            txt.alignment  = TextAnchor.MiddleCenter;
            txt.fontSize   = 20;
            txt.color      = new Color(0.91f, 0.94f, 0.98f, 1f); // ink #E8F1FA

            return btnObj;
        }

        /// <summary>Creates a 1×1 white sprite for fallback when a resource is missing.</summary>
        private static Sprite CreateWhiteSprite() {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
        }

        // ── Public API (called by GameManager / MissionRunner) ───────────────

        /// <summary>
        /// Show the results panel with the given state and score.
        /// This is the entry point from GameManager on mission end.
        /// </summary>
        /// <param name="missionIndex">Mission number (1-5).</param>
        /// <param name="won">True if the player completed all objectives.</param>
        /// <param name="currentScore">Final score earned this run.</param>
        public void Show(int missionIndex, bool won, int currentScore) {
            _currentMission = missionIndex;
            _isWon          = won;

            // ── Title ────────────────────────────────────────────────
            if (_titleText != null) {
                _titleText.text    = won ? "MISSION COMPLETE!" : "MISSION FAILED";
                _titleText.color   = won
                    ? new Color(0.4f, 0.93f, 0.42f, 1f)    // green for win
                    : new Color(0.9f, 0.34f, 0.18f, 1f);   // hostile red for loss
            }

            // ── Score ────────────────────────────────────────────────
            if (_scoreText != null) {
                _scoreText.text = "Score: " + currentScore;
            }

            // ── Best score comparison ────────────────────────────────
            int bestScore = SaveSystem.GetBestScore(missionIndex);
            if (_bestScoreText != null && bestScore > 0) {
                _bestScoreText.text = "Best: " + bestScore;
                _bestScoreText.gameObject.SetActive(true);
            } else {
                _bestScoreText.gameObject.SetActive(false);
            }

            // ── Retry button (always visible on result screen) ───────
            if (_retryButton != null) {
                _retryButton.gameObject.SetActive(true);
            }

            // ── Next button: hidden on final mission ─────────────────
            bool isLastMission = missionIndex >= 5;
            if (_nextButton != null) {
                _nextButton.gameObject.SetActive(!isLastMission);
                if (_nextButtonPanel != null && isLastMission) {
                    _nextButtonPanel.raycastTarget = false;
                }
            }

            // ── Persist score and unlock next mission on win ─────────
            if (won) {
                SaveSystem.SaveBestScore(missionIndex, currentScore);
            }

            // Show the overlay.
            ShowOverlay();
        }

        /// <summary>Hide the results panel.</summary>
        public void Hide() {
            _isWon       = false;
            _currentMission = 0;
            HideOverlay();
        }

        // ── Button handlers ────────────────────────────────────────────────

        /// <summary>Retry button handler: restart the current mission.</summary>
        private void OnRetryClicked() {
            ClearSubscriptions();

            var gm = GameManager.Instance;
            if (gm != null) {
                gm.RetryMission(_currentMission);
            } else {
                Debug.LogWarning("ResultView: GameManager not available — retrying via direct scene reload.");
                SceneManager.LoadScene("MainScene");
            }
        }

        /// <summary>Next button handler: advance to the next mission or show main menu.</summary>
        private void OnNextClicked() {
            ClearSubscriptions();

            var gm = GameManager.Instance;
            if (gm != null) {
                if (_currentMission < 5) {
                    // Advance to next mission.
                    gm.StartMission(_currentMission + 1);
                } else {
                    // Last mission completed — show main menu.
                    gm.ShowMainMenu();
                }
            } else {
                Debug.LogWarning("ResultView: GameManager not available — going to MainMenu.");
                SceneManager.LoadScene("MainMenu");
            }
        }

        // ── Display helpers ────────────────────────────────────────────────

        /// <summary>Show overlay and make it interactive.</summary>
        private void ShowOverlay() {
            if (_canvasGroup != null) {
                _canvasGroup.alpha       = 1f;
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }
        }

        /// <summary>Hide overlay and block input.</summary>
        private void HideOverlay() {
            if (_canvasGroup != null) {
                _canvasGroup.alpha       = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
        }

        private void ClearSubscriptions() {
            // No GameEvents subscriptions on this view.
        }
    }
}
