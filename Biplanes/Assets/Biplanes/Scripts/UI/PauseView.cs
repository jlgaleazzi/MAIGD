using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Biplanes.Core;

namespace Biplanes.UI {

    /// <summary>
    /// Pause overlay shown when the player presses Escape during a Playing mission.
    /// Displays a "PAUSED" title with Resume and Quit buttons (UI_SPEC §3.4).
    /// While visible: timeScale = 0, HUD canvas hidden.
    ///
    /// Built entirely from code — no prefabs (ARCHITECTURE §0 rule 1).
    /// </summary>
    public class PauseView : MonoBehaviour {

        [SerializeField] private float _pausePanelWidth = 360f;
        [SerializeField] private float _pausePanelHeight = 280f;
        [SerializeField] private float _buttonWidth = 180f;
        [SerializeField] private float _buttonHeight = 50f;
        [SerializeField] private float _buttonSpacing = 20f;

        /// <summary>CanvasGroup wrapping this pause overlay (set in Awake).</summary>
        private CanvasGroup _canvasGroup;

        // ── GameManager reference (set by GameManager itself) ───────────────

        /// <summary>Set by GameManager during Awake/OnEnable to avoid circular references.</summary>
        private GameManager _gameManager;

        // ── Unity lifecycle ───────────────────────────────────────────────

        private void Awake() {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                Debug.LogWarning("PauseView: CanvasGroup missing — added one.");
            }
            // Start invisible.
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            BuildVisuals();
        }

        private void OnEnable() {
            Show();
        }

        private void OnDisable() {
            Hide();
        }

        // ── Visual construction (no prefabs) ───────────────────────────────

        /// <summary>
        /// Build the pause panel hierarchy per UI_SPEC §3.4:
        ///   [ PAUSED ]
        ///     ┌──────┐
        ///     │Resume│
        ///     └──────┘
        ///     ┌──────┐
        ///     │ Quit │
        ///     └──────┘
        /// </summary>
        private void BuildVisuals() {
            // Container panel.
            var panel = new GameObject("PausePanel");
            panel.transform.SetParent(transform, false);

            var panelRect = panel.GetComponent<RectTransform>() ?? panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(_pausePanelWidth, _pausePanelHeight);

            var panelImg = panel.AddComponent<Image>();
            var bgSprite = Resources.Load<Sprite>("Sprites/ui/ui_panel");
            if (bgSprite != null) {
                panelImg.sprite = bgSprite;
            } else {
                Debug.LogWarning("PauseView: Missing sprite 'Sprites/ui/ui_panel', using 1×1 white fallback.");
                panelImg.sprite = CreateWhiteSprite();
            }
            panelImg.color = Color.white;
            panelImg.raycastTarget = false;

            // PAUSED title label.
            var titleObj = new GameObject("PauseTitle");
            titleObj.transform.SetParent(transform, false);
            var titleRect = titleObj.GetComponent<RectTransform>() ?? titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(_pausePanelWidth - 40f, 60f);

            var titleText = titleObj.AddComponent<Text>();
            titleText.text = "PAUSED";
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.fontSize = 36;
            titleText.color = new Color(0.91f, 0.94f, 0.98f, 1f); // ink #E8F1FA

            // ── Resume button (above Quit) ──────────────────────────────
            var resumeBtnObj = CreateButton("ResumeButton", "Resume", _buttonWidth, _buttonHeight, -105f);
            var resumeBtn = resumeBtnObj.GetComponent<Button>();
            if (resumeBtn != null) {
                resumeBtn.onClick.AddListener(OnResumeClicked);
            }

            // ── Quit button (below Resume) ──────────────────────────────
            var quitBtnObj = CreateButton("QuitButton", "Quit", _buttonWidth, _buttonHeight, 105f);
            var quitBtn = quitBtnObj.GetComponent<Button>();
            if (quitBtn != null) {
                quitBtn.onClick.AddListener(OnQuitClicked);
            }
        }

        /// <summary>
        /// Helper: create a centered button child at a given vertical offset from centre.
        /// Returns the GameObject for caller to attach extra components if needed.
        /// </summary>
        private GameObject CreateButton(string name, string buttonText, float width, float height, float yOffsetFromCentre) {
            var btnObj = new GameObject(name);
            btnObj.transform.SetParent(transform, false);

            var btnRect = btnObj.GetComponent<RectTransform>() ?? btnObj.AddComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.5f, 0.5f);
            btnRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnRect.pivot = new Vector2(0.5f, 0.5f);
            btnRect.sizeDelta = new Vector2(width, height);
            btnRect.localPosition = new Vector3(0f, yOffsetFromCentre, 0f);

            // Button background image.
            var bgSprite = Resources.Load<Sprite>("Sprites/ui/ui_button");
            if (bgSprite != null) {
                var btnImg = btnObj.AddComponent<Image>();
                btnImg.sprite = bgSprite;
                btnImg.color = Color.white;
                btnImg.raycastTarget = true; // enable raycast for button click.
            }

            // Button label text (centre of button).
            var txtObj = new GameObject(name + "Text");
            txtObj.transform.SetParent(btnObj.transform, false);
            var txtRect = txtObj.GetComponent<RectTransform>() ?? txtObj.AddComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector3(8f, 4f);
            txtRect.offsetMax = new Vector3(-8f, -4f);

            var txt = txtObj.AddComponent<Text>();
            txt.text = buttonText;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.fontSize = 20;
            txt.color = new Color(0.91f, 0.94f, 0.98f, 1f); // ink #E8F1FA

            return btnObj;
        }

        /// <summary>Creates a 1×1 white sprite for fallback when a resource is missing.</summary>
        private static Sprite CreateWhiteSprite() {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
        }

        // ── Pause / Resume / Quit ───────────────────────────────────────────

        /// <summary>
        /// Called when the Escape key is pressed during a Playing mission.
        /// Pauses time and shows this overlay. (GAME_MANAGER hook.)
        /// </summary>
        public void OnEscapePressed() {
            Time.timeScale = 0f;           // freeze all physics / coroutines.
            Show();                         // make panel visible & interactive.
        }

        /// <summary>Resume button handler: restore timeScale and hide the pause overlay.
        /// Delegates to GameManager.Instance.ResumeGame() (ARCHITECTURE §3).</summary>
        private void OnResumeClicked() {
            var gm = GameManager.Instance;
            if (gm != null) {
                gm.ResumeGame();
            } else {
                Debug.LogWarning("PauseView: GameManager not yet assigned — resuming directly.");
                Resume();
            }
        }

        /// <summary>Quit button handler: load the MainMenu scene.</summary>
        private void OnQuitClicked() {
            Quit();
        }

        // ── Internal helpers (called from GameManager or PauseState) ───────

        /// <summary>Sets the GameManager reference without circular dependency.</summary>
        internal void SetGameManager(GameManager gm) {
            _gameManager = gm;
        }

        // ── Display helpers ──────────────────────────────────────────────────

        /// <summary>Show overlay and make it interactive.</summary>
        internal void Show() {
            if (_canvasGroup != null) {
                _canvasGroup.alpha = 1f;
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }
        }

        /// <summary>Hide overlay and block input.</summary>
        internal void Hide() {
            if (_canvasGroup != null) {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
        }

        /// <summary>Resume the game (called by GameManager.ResumeGame).</summary>
        internal void Resume() {
            Time.timeScale = 1f;
            Hide();
        }

        /// <summary>Load the MainMenu scene (called by GameManager on Quit).</summary>
        internal void Quit() {
            SceneManager.LoadScene("MainMenu");
        }
    }
}
