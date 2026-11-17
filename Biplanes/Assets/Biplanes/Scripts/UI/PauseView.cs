using UnityEngine;
using UnityEngine.UI;
using Biplanes.Audio;
using Biplanes.Core;

namespace Biplanes.UI {

    /// <summary>
    /// Pause overlay (UI_SPEC §3.4): "PAUSED" title with Resume and Quit buttons.
    /// GameManager shows it on Esc during Playing (timeScale 0, player input off) and
    /// hides it when it leaves Paused (Esc again or Resume). Quit goes to the main menu.
    ///
    /// Built entirely from code on its own overlay canvas — no prefabs, no scene objects
    /// (ARCHITECTURE §0). Every Image gets a sprite, every Text gets LegacyRuntime.ttf, rects
    /// use anchoredPosition + sizeDelta (B022 UI rules).
    /// </summary>
    public class PauseView : MonoBehaviour {

        // ── Layout (reference resolution 1280×720, centre anchored) ───────

        private const int   CanvasSortingOrder = 110;   // above HUD, below results
        private const float ReferenceWidth     = 1280f;
        private const float ReferenceHeight    = 720f;
        private const float MatchWidthOrHeight = 0.5f;

        private const float PanelWidth   = 360f;
        private const float PanelHeight  = 300f;

        private const float TitleY       = 85f;
        private const float TitleWidth   = 320f;
        private const float TitleHeight  = 60f;
        private const int   TitleSize    = 36;

        private const float ResumeY      = 0f;
        private const float QuitY        = -80f;
        private const float ButtonWidth  = 200f;
        private const float ButtonHeight = 52f;
        private const int   ButtonSize   = 20;
        private const float LabelPadding = 6f;

        private const float HintY        = -128f;
        private const float HintHeight   = 24f;
        private const int   HintSize     = 14;

        private const float BackdropAlpha     = 0.5f;
        private const float ColorFadeDuration = 0.1f;

        private const string FontResource     = "LegacyRuntime.ttf";
        private const string PanelSpritePath  = "Sprites/ui/ui_panel";
        private const string ButtonSpritePath = "Sprites/ui/ui_button";

        // ── Palette (ART_STYLE / UI_SPEC §2) ───────────────────────────────

        private static readonly Color Paper   = new Color(0.059f, 0.165f, 0.29f, 1f); // #0F2A4A
        private static readonly Color Ink     = new Color(0.91f, 0.945f, 0.98f, 1f); // #E8F1FA
        private static readonly Color Pencil  = new Color(0.616f, 0.706f, 0.8f, 1f); // #9DB4CC
        private static readonly Color Hostile = new Color(0.894f, 0.341f, 0.18f, 1f); // #E4572E

        // ── State ──────────────────────────────────────────────────────────

        private static PauseView _instance;
        private static Sprite _whiteSprite;
        private static bool _warnedPanel;
        private static bool _warnedButton;

        private Font _font;
        private bool _clicked;

        /// <summary>True while the pause overlay exists.</summary>
        public static bool IsShowing => _instance != null;

        // ── Factory ────────────────────────────────────────────────────────

        /// <summary>
        /// Create "PauseCanvas" (overlay canvas + scaler + raycaster, plus an EventSystem if
        /// the scene has none) and return the view; returns the existing one if shown.
        /// Does not touch timeScale: GameManager.ShowPauseView owns that.
        /// </summary>
        public static PauseView Show() {
            if (_instance != null) return _instance;

            MainMenuView.EnsureEventSystem();

            var canvasGO = new GameObject("PauseCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = MatchWidthOrHeight;

            canvasGO.AddComponent<GraphicRaycaster>();

            var view = canvasGO.AddComponent<PauseView>();
            view.Init(); // built here, not in Awake, so it also works outside Play mode
            return view;
        }

        /// <summary>Destroy the pause overlay (its whole canvas). Safe when none exists.</summary>
        public static void Hide() {
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
            PlayUiCue(SfxId.UiPause); // the pause hush, once per pause (T952)
        }

        private void OnDestroy() {
            if (_instance == this) _instance = null;
        }

        // ── Visual construction (no prefabs) ───────────────────────────────

        private void BuildVisuals() {
            // Dim backdrop over the frozen world; swallows clicks.
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

            var panel = NewRect("PausePanel", transform);
            Place(panel, Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            var panelImg = panel.gameObject.AddComponent<Image>();
            var panelSprite = Resources.Load<Sprite>(PanelSpritePath);
            if (panelSprite != null) {
                panelImg.sprite = panelSprite;
                panelImg.color = Color.white;
            } else {
                if (!_warnedPanel) {
                    Debug.LogWarning("PauseView: Missing sprite '" + PanelSpritePath + "', using a solid fallback.");
                    _warnedPanel = true;
                }
                panelImg.sprite = WhiteSprite();
                panelImg.color = Paper;
            }
            panelImg.raycastTarget = false;

            var title = NewRect("PauseTitle", transform);
            Place(title, new Vector2(0f, TitleY), new Vector2(TitleWidth, TitleHeight));
            var titleText = AddText(title.gameObject, "PAUSED", TitleSize, Ink);
            titleText.fontStyle = FontStyle.Bold;

            var resume = CreateButton("ResumeButton", "Resume", ResumeY);
            resume.onClick.AddListener(OnResumeClicked);

            var quit = CreateButton("QuitButton", "Quit", QuitY);
            quit.onClick.AddListener(OnQuitClicked);

            var hint = NewRect("PauseHint", transform);
            Place(hint, new Vector2(0f, HintY), new Vector2(TitleWidth, HintHeight));
            AddText(hint.gameObject, "ESC  resume", HintSize, Pencil);
        }

        private Button CreateButton(string name, string label, float y) {
            var rect = NewRect(name, transform);
            Place(rect, new Vector2(0f, y), new Vector2(ButtonWidth, ButtonHeight));

            var img = rect.gameObject.AddComponent<Image>();
            var sprite = Resources.Load<Sprite>(ButtonSpritePath);
            if (sprite != null) {
                img.sprite = sprite;
                img.color = Color.white;
            } else {
                if (!_warnedButton) {
                    Debug.LogWarning("PauseView: Missing sprite '" + ButtonSpritePath + "', using a solid fallback.");
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

        // ── Button handlers ────────────────────────────────────────────────

        /// <summary>Resume: GameManager leaves Paused, which hides this view and restores time.</summary>
        private void OnResumeClicked() {
            if (_clicked) return;
            _clicked = true;
            PlayUiCue(SfxId.UiClick);
            GameManager.Instance.ResumeGame();
        }

        /// <summary>Quit: back to the main menu.</summary>
        private void OnQuitClicked() {
            if (_clicked) return;
            _clicked = true;
            PlayUiCue(SfxId.UiClick);
            GameManager.Instance.QuitToMenu();
        }

        /// <summary>UI cue through AudioManager.PlayUi (plays while paused). Skipped when no
        /// AudioManager exists (EditMode tests) (T952).</summary>
        private static void PlayUiCue(SfxId id) {
            var audio = AudioManager.GetOrNull();
            if (audio != null) audio.PlayUi(id);
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
