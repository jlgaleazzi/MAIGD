using UnityEngine;
using UnityEngine.UI;
using Biplanes.Core;

namespace Biplanes.UI {

    /// <summary>
    /// Mission results overlay shown on Won / Lost (UI_SPEC §3.5).
    ///   Won  → MISSION COMPLETE! + reason + best + score + [Retry] [Next] [Menu]
    ///   Lost → MISSION FAILED    + reason + best + score + [Retry] [Menu]
    /// Next is shown only after a win below the last mission (GameManager.NextMission).
    ///
    /// Built entirely from code on its own overlay canvas — no prefabs, no scene objects
    /// (ARCHITECTURE §0). Works at Time.timeScale 0: uGUI clicks and colour fades use
    /// unscaled time. Every Image gets a sprite, every Text gets LegacyRuntime.ttf, rects use
    /// anchoredPosition + sizeDelta (B022 UI rules).
    /// </summary>
    public class ResultView : MonoBehaviour {

        // ── Layout (reference resolution 1280×720, centre anchored) ───────

        private const int   CanvasSortingOrder = 120;   // above HUD, menu, briefing, pause
        private const float ReferenceWidth     = 1280f;
        private const float ReferenceHeight    = 720f;
        private const float MatchWidthOrHeight = 0.5f;

        private const float PanelWidth   = 560f;
        private const float PanelHeight  = 400f;
        private const float TextWidth    = 500f;

        private const float TitleY       = 140f;
        private const float TitleHeight  = 56f;
        private const int   TitleSize    = 36;

        private const float ReasonY      = 85f;
        private const float ReasonHeight = 44f;
        private const int   ReasonSize   = 18;

        private const float BestY        = 35f;
        private const float BestHeight   = 30f;
        private const int   BestSize     = 18;

        private const float ScoreY       = -10f;
        private const float ScoreHeight  = 40f;
        private const int   ScoreSize    = 28;

        private const float ButtonY      = -120f;
        private const float ButtonWidth  = 150f;
        private const float ButtonHeight = 52f;
        private const float ButtonGap    = 20f;
        private const int   ButtonSize   = 20;
        private const float LabelPadding = 6f;

        private const float BackdropAlpha     = 0.6f;
        private const float ColorFadeDuration = 0.1f;

        private const string FontResource     = "LegacyRuntime.ttf";
        private const string PanelSpritePath  = "Sprites/ui/ui_panel";
        private const string ButtonSpritePath = "Sprites/ui/ui_button";

        // ── Palette (ART_STYLE / UI_SPEC §2) ───────────────────────────────

        private static readonly Color Paper    = new Color(0.059f, 0.165f, 0.29f, 1f);  // #0F2A4A
        private static readonly Color Ink      = new Color(0.91f, 0.945f, 0.98f, 1f);   // #E8F1FA
        private static readonly Color Pencil   = new Color(0.616f, 0.706f, 0.8f, 1f);   // #9DB4CC
        private static readonly Color Hostile  = new Color(0.894f, 0.341f, 0.18f, 1f);  // #E4572E
        private static readonly Color Friendly = new Color(0.949f, 0.757f, 0.306f, 1f); // #F2C14E

        // ── State ──────────────────────────────────────────────────────────

        private static ResultView _instance;
        private static Sprite _whiteSprite;
        private static bool _warnedPanel;
        private static bool _warnedButton;

        private Font _font;
        private Text _titleText;
        private Text _reasonText;
        private Text _bestScoreText;
        private Text _scoreText;
        private Button _retryButton;
        private Button _nextButton;
        private Button _menuButton;

        private int _currentMission;
        private bool _isWon;
        private bool _clicked;

        /// <summary>True while a result screen exists.</summary>
        public static bool IsShowing => _instance != null;

        // ── Factory ────────────────────────────────────────────────────────

        /// <summary>
        /// Return the result screen, creating "ResultCanvas" (overlay canvas + scaler +
        /// raycaster, plus an EventSystem if the scene has none) when there is none.
        /// </summary>
        public static ResultView Create() {
            if (_instance != null) return _instance;

            MainMenuView.EnsureEventSystem();

            var canvasGO = new GameObject("ResultCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = MatchWidthOrHeight;

            canvasGO.AddComponent<GraphicRaycaster>();

            var view = canvasGO.AddComponent<ResultView>();
            view.Init(); // built here, not in Awake, so it also works outside Play mode
            return view;
        }

        /// <summary>Destroy the result screen (its whole canvas). Safe when none exists.</summary>
        public static void HideAll() {
            if (_instance == null) return;
            var go = _instance.gameObject;
            _instance = null;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        private void Init() {
            if (_instance != null && _instance != this) return;
            _instance = this;
            _font = Resources.GetBuiltinResource<Font>(FontResource);
            BuildVisuals();
        }

        private void OnDestroy() {
            if (_instance == this) _instance = null;
        }

        // ── Visual construction (no prefabs) ───────────────────────────────

        private void BuildVisuals() {
            // Dim backdrop: the frozen world stays faintly visible, clicks never reach it.
            var backdrop = NewRect("Backdrop", transform);
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.pivot = new Vector2(0.5f, 0.5f);
            backdrop.anchoredPosition = Vector2.zero;
            backdrop.sizeDelta = Vector2.zero;
            var backdropImg = backdrop.gameObject.AddComponent<Image>();
            backdropImg.sprite = WhiteSprite();
            backdropImg.color = new Color(Paper.r, Paper.g, Paper.b, BackdropAlpha);
            backdropImg.raycastTarget = true;

            // Panel.
            var panel = NewRect("ResultPanel", transform);
            Place(panel, Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            var panelImg = panel.gameObject.AddComponent<Image>();
            var panelSprite = Resources.Load<Sprite>(PanelSpritePath);
            if (panelSprite != null) {
                panelImg.sprite = panelSprite;
                panelImg.color = Color.white;
            } else {
                if (!_warnedPanel) {
                    Debug.LogWarning("ResultView: Missing sprite '" + PanelSpritePath + "', using a solid fallback.");
                    _warnedPanel = true;
                }
                panelImg.sprite = WhiteSprite();
                panelImg.color = Paper;
            }
            panelImg.raycastTarget = false;

            _titleText = AddLabel("ResultTitle", TitleY, TitleHeight, TitleSize, FontStyle.Bold, Ink);
            _reasonText = AddLabel("ResultReason", ReasonY, ReasonHeight, ReasonSize, FontStyle.Italic, Pencil);
            _bestScoreText = AddLabel("ResultBestScore", BestY, BestHeight, BestSize, FontStyle.Normal, Pencil);
            _scoreText = AddLabel("ResultScore", ScoreY, ScoreHeight, ScoreSize, FontStyle.Bold, Ink);

            _retryButton = CreateButton("RetryButton", "Retry");
            _retryButton.onClick.AddListener(OnRetryClicked);
            _nextButton = CreateButton("NextButton", "Next");
            _nextButton.onClick.AddListener(OnNextClicked);
            _menuButton = CreateButton("MenuButton", "Menu");
            _menuButton.onClick.AddListener(OnMenuClicked);
        }

        private Text AddLabel(string name, float y, float height, int size, FontStyle style, Color color) {
            var rect = NewRect(name, transform);
            Place(rect, new Vector2(0f, y), new Vector2(TextWidth, height));
            var txt = AddText(rect.gameObject, "", size, color);
            txt.fontStyle = style;
            return txt;
        }

        private Button CreateButton(string name, string label) {
            var rect = NewRect(name, transform);
            Place(rect, new Vector2(0f, ButtonY), new Vector2(ButtonWidth, ButtonHeight));

            var img = rect.gameObject.AddComponent<Image>();
            var sprite = Resources.Load<Sprite>(ButtonSpritePath);
            if (sprite != null) {
                img.sprite = sprite;
                img.color = Color.white;
            } else {
                if (!_warnedButton) {
                    Debug.LogWarning("ResultView: Missing sprite '" + ButtonSpritePath + "', using a solid fallback.");
                    _warnedButton = true;
                }
                img.sprite = WhiteSprite();
                img.color = Paper;
            }
            img.raycastTarget = true;

            var btn = rect.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = Pencil;   // hover = pencil glow (UI_SPEC §2)
            colors.selectedColor    = Pencil;
            colors.pressedColor     = Hostile;  // active = hostile tint (UI_SPEC §2)
            colors.colorMultiplier  = 1f;
            colors.fadeDuration     = ColorFadeDuration;
            btn.colors = colors;

            var textRect = NewRect(name + "Text", rect);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = new Vector2(-2f * LabelPadding, -2f * LabelPadding);
            var txt = AddText(textRect.gameObject, label, ButtonSize, Ink);
            txt.fontStyle = FontStyle.Bold;

            return btn;
        }

        /// <summary>Centre the active buttons in one row at ButtonY.</summary>
        private void LayoutButtons() {
            var row = new System.Collections.Generic.List<Button>(3);
            if (_retryButton.gameObject.activeSelf) row.Add(_retryButton);
            if (_nextButton.gameObject.activeSelf) row.Add(_nextButton);
            if (_menuButton.gameObject.activeSelf) row.Add(_menuButton);

            float total = row.Count * ButtonWidth + (row.Count - 1) * ButtonGap;
            float x = -total * 0.5f + ButtonWidth * 0.5f;
            foreach (var b in row) {
                ((RectTransform)b.transform).anchoredPosition = new Vector2(x, ButtonY);
                x += ButtonWidth + ButtonGap;
            }
        }

        // ── Public API (called by GameManager) ─────────────────────────────

        /// <summary>Show the results for a mission without an end reason.</summary>
        public void Show(int missionIndex, bool won, int currentScore) {
            Show(missionIndex, won, currentScore, null);
        }

        /// <summary>
        /// Show the results panel with the reason the mission ended (GameManager passes the
        /// MissionEnded reason: "Player crashed", a failed objective, the map end...).
        /// </summary>
        public void Show(int missionIndex, bool won, int currentScore, string reason) {
            _currentMission = missionIndex;
            _isWon = won;
            _clicked = false;

            _titleText.text  = won ? "MISSION COMPLETE!" : "MISSION FAILED";
            _titleText.color = won ? Friendly : Hostile;

            _reasonText.text = reason ?? "";
            _reasonText.gameObject.SetActive(!string.IsNullOrEmpty(reason));

            // Previous best, read before this run is saved (UI_SPEC §3.5).
            int bestScore = SaveSystem.GetBestScore(missionIndex);
            _bestScoreText.text = "Best: " + bestScore;
            _bestScoreText.gameObject.SetActive(bestScore > 0);

            _scoreText.text = "Score: " + currentScore;

            if (won) SaveSystem.SaveBestScore(missionIndex, currentScore);

            _retryButton.gameObject.SetActive(true);
            _nextButton.gameObject.SetActive(won && missionIndex < GameManager.MissionCount);
            _menuButton.gameObject.SetActive(true);
            LayoutButtons();
        }

        /// <summary>Hide the results panel (destroys the whole result canvas).</summary>
        public void Hide() {
            if (_instance == this) HideAll();
        }

        // ── Button handlers ────────────────────────────────────────────────

        /// <summary>Retry: reload the Game scene and restart the same mission.</summary>
        private void OnRetryClicked() {
            if (_clicked) return;
            _clicked = true;
            GameManager.Instance.RetryMission(_currentMission);
        }

        /// <summary>Next: the briefing of the next mission, or the menu after the last one.</summary>
        private void OnNextClicked() {
            if (_clicked || !_isWon) return;
            _clicked = true;
            GameManager.Instance.NextMission(_currentMission);
        }

        /// <summary>Menu: back to the main menu.</summary>
        private void OnMenuClicked() {
            if (_clicked) return;
            _clicked = true;
            GameManager.Instance.QuitToMenu();
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static RectTransform NewRect(string name, Transform parent) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Centre anchor and pivot, then position and size. No offsets.</summary>
        private static void Place(RectTransform rect, Vector2 position, Vector2 size) {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private Text AddText(GameObject go, string value, int size, Color color) {
            var txt = go.AddComponent<Text>();
            txt.font = _font;
            txt.text = value;
            txt.fontSize = size;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.color = color;
            txt.raycastTarget = false;
            return txt;
        }

        /// <summary>A shared 1×1 white sprite, tinted by Image.color.</summary>
        private static Sprite WhiteSprite() {
            if (_whiteSprite == null) {
                var texture = new Texture2D(1, 1);
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                _whiteSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
            }
            return _whiteSprite;
        }
    }
}
