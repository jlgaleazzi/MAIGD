using UnityEngine;
using UnityEngine.InputSystem; // Keyboard.current (project uses Input System only)
using UnityEngine.UI;
using Biplanes.Audio;
using Biplanes.Core;
using Biplanes.Missions;

namespace Biplanes.UI {

    /// <summary>
    /// Briefing screen between mission selection and gameplay (UI_SPEC §3.2): the mission's
    /// title and briefing text from its JSON, and a "Begin Mission" button. The screen fades
    /// in over 0.5 s, then starts the mission after 5 s, on Space (Enter also works), or on the button,
    /// through GameManager.StartMission(mission).
    ///
    /// GameManager.ShowBriefing → SetState(Briefing) → BriefingView.Show(n). Runs on
    /// unscaled time (the briefing freezes the world at timeScale 0). Built from code on
    /// its own overlay canvas — no prefabs, no scene objects (ARCHITECTURE §0). Every Image
    /// gets a sprite, every Text gets LegacyRuntime.ttf (B022 UI rules).
    /// </summary>
    public class BriefingView : MonoBehaviour {

        // ── Layout (reference resolution 1280×720, centre anchored) ───────

        private const int   CanvasSortingOrder = 105;   // above HUD and menu
        private const float ReferenceWidth     = 1280f;
        private const float ReferenceHeight    = 720f;
        private const float MatchWidthOrHeight = 0.5f;

        private const float PanelWidth    = 760f;
        private const float PanelHeight   = 440f;

        private const float TitleY        = 150f;
        private const float TitleWidth    = 680f;
        private const float TitleHeight   = 56f;
        private const int   TitleSize     = 32;

        private const float BriefingY     = 20f;
        private const float BriefingWidth = 660f;
        private const float BriefingHeight= 180f;
        private const int   BriefingSize  = 20;

        private const float ButtonY       = -130f;
        private const float ButtonWidth   = 240f;
        private const float ButtonHeight  = 56f;
        private const int   ButtonSize    = 22;
        private const float LabelPadding  = 6f;

        private const float HintY         = -182f;
        private const float HintHeight    = 24f;
        private const int   HintSize      = 14;

        private const float ColorFadeDuration = 0.1f;

        private const string FontResource     = "LegacyRuntime.ttf";
        private const string PanelSpritePath  = "Sprites/ui/ui_panel";
        private const string ButtonSpritePath = "Sprites/ui/ui_button";

        // ── Palette (ART_STYLE / UI_SPEC §2) ───────────────────────────────

        private static readonly Color Paper   = new Color(0.059f, 0.165f, 0.29f, 1f); // #0F2A4A
        private static readonly Color Ink     = new Color(0.91f, 0.945f, 0.98f, 1f); // #E8F1FA
        private static readonly Color Pencil  = new Color(0.616f, 0.706f, 0.8f, 1f); // #9DB4CC
        private static readonly Color Hostile = new Color(0.894f, 0.341f, 0.18f, 1f); // #E4572E

        // ── Timing (UI_SPEC §3.2) ──────────────────────────────────────────

        [SerializeField] private float _autoDelay = 5f;        // seconds after the fade-in
        [SerializeField] private float _fadeInDuration = 0.5f; // canvas fade-in

        // ── Typewriter cue (T952) ──────────────────────────────────────────
        // The briefing text appears at once (with the canvas fade), so it gets a short burst
        // of typewriter clacks on unscaled time rather than one clack per character.

        /// <summary>Clacks in the burst while the briefing text appears.</summary>
        public const int   TypewriterClacks        = 5;
        /// <summary>Seconds between two clacks (unscaled).</summary>
        public const float TypewriterInterval      = 0.06f;
        /// <summary>Seconds after the view opens before the first clack (after the page turn).</summary>
        public const float TypewriterStartDelay    = 0.12f;

        // ── State ──────────────────────────────────────────────────────────

        private static BriefingView _instance;
        private static Sprite _whiteSprite;
        private static bool _warnedPanel;
        private static bool _warnedButton;

        private int _currentMission = 1;
        private MissionData _mission;
        private Font _font;
        private CanvasGroup _group;
        private Text _titleText;
        private Text _briefingText;
        private float _elapsed;
        private bool _transitioned;
        private int _clacksPlayed;

        /// <summary>Mission (1-5) this briefing starts.</summary>
        public int currentMission {
            get => _currentMission;
            set => _currentMission = Mathf.Clamp(value, 1, GameManager.MissionCount);
        }

        /// <summary>True while a briefing exists.</summary>
        public static bool IsShowing => _instance != null;

        // ── Factory ────────────────────────────────────────────────────────

        /// <summary>
        /// Show the briefing for `missionIndex`: creates "BriefingCanvas" (overlay canvas +
        /// scaler + raycaster + CanvasGroup, plus an EventSystem if the scene has none). An
        /// existing briefing is replaced.
        /// </summary>
        public static BriefingView Show(int missionIndex) {
            Hide();
            MainMenuView.EnsureEventSystem();

            var canvasGO = new GameObject("BriefingCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = MatchWidthOrHeight;

            canvasGO.AddComponent<GraphicRaycaster>();

            var view = canvasGO.AddComponent<BriefingView>();
            view.Init(missionIndex); // built here, not in Awake, so it also works outside Play mode
            return view;
        }

        /// <summary>Destroy the briefing (its whole canvas). Safe when none exists.</summary>
        public static void Hide() {
            if (_instance == null) return;
            var go = _instance.gameObject;
            _instance = null;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        private void Init(int missionIndex) {
            _instance = this;
            currentMission = missionIndex;
            _font = Resources.GetBuiltinResource<Font>(FontResource);
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _elapsed = 0f;
            _transitioned = false;
            _clacksPlayed = 0;

            LoadMissionData(_currentMission);
            BuildVisuals();
            PlayUiCue(SfxId.UiPage); // the briefing sheet turns over (T952)
        }

        private void OnDestroy() {
            if (_instance == this) _instance = null;
        }

        // ── Fade, auto-start and skip (unscaled time) ──────────────────────

        private void Update() {
            if (_transitioned) return;

            _elapsed += Time.unscaledDeltaTime;
            UpdateTypewriter();
            if (_group != null) {
                _group.alpha = _fadeInDuration > 0f ? Mathf.Clamp01(_elapsed / _fadeInDuration) : 1f;
            }

            bool ready = _elapsed >= _fadeInDuration;
            if (!ready) return;

            var kb = Keyboard.current;
            bool skip = kb != null && (kb.enterKey.wasPressedThisFrame
                                       || kb.numpadEnterKey.wasPressedThisFrame
                                       || kb.spaceKey.wasPressedThisFrame);
            if (skip || _elapsed >= _fadeInDuration + _autoDelay) TriggerTransition();
        }

        /// <summary>Play the clacks that are due by now. Runs only from Update, which stops
        /// after Begin (_transitioned) and when the view is hidden (destroyed).</summary>
        private void UpdateTypewriter() {
            int due = TypewriterClacksDue(_elapsed);
            if (_clacksPlayed >= due) return;
            _clacksPlayed = due; // never a catch-up burst after a long frame: one clack per frame at most
            PlayUiCue(SfxId.Typewriter);
        }

        /// <summary>
        /// How many typewriter clacks should have played `elapsed` unscaled seconds after the
        /// briefing opened: 0 before TypewriterStartDelay, then one more every
        /// TypewriterInterval, capped at TypewriterClacks (T952, pure for EditMode tests).
        /// </summary>
        public static int TypewriterClacksDue(float elapsed) {
            if (elapsed < TypewriterStartDelay) return 0;
            int due = Mathf.FloorToInt((elapsed - TypewriterStartDelay) / TypewriterInterval) + 1;
            return Mathf.Clamp(due, 0, TypewriterClacks);
        }

        // ── Visual construction (no prefabs) ───────────────────────────────

        private void BuildVisuals() {
            // Full-screen paper backdrop: hides the world behind the briefing.
            var backdrop = NewRect("Backdrop", transform);
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.pivot = new Vector2(0.5f, 0.5f);
            backdrop.anchoredPosition = Vector2.zero;
            backdrop.sizeDelta = Vector2.zero;
            var backdropImg = backdrop.gameObject.AddComponent<Image>();
            backdropImg.sprite = WhiteSprite();
            backdropImg.color = Paper;
            backdropImg.raycastTarget = true;

            var panel = NewRect("BriefingPanel", transform);
            Place(panel, Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            var panelImg = panel.gameObject.AddComponent<Image>();
            var panelSprite = Resources.Load<Sprite>(PanelSpritePath);
            if (panelSprite != null) {
                panelImg.sprite = panelSprite;
                panelImg.color = Color.white;
            } else {
                if (!_warnedPanel) {
                    Debug.LogWarning("BriefingView: Missing sprite '" + PanelSpritePath + "', using a solid fallback.");
                    _warnedPanel = true;
                }
                panelImg.sprite = WhiteSprite();
                panelImg.color = Paper;
            }
            panelImg.raycastTarget = false;

            var title = NewRect("Title", transform);
            Place(title, new Vector2(0f, TitleY), new Vector2(TitleWidth, TitleHeight));
            _titleText = AddText(title.gameObject, TitleLine(), TitleSize, Ink, TextAnchor.MiddleCenter);
            _titleText.fontStyle = FontStyle.Bold;

            var body = NewRect("BriefingText", transform);
            Place(body, new Vector2(0f, BriefingY), new Vector2(BriefingWidth, BriefingHeight));
            _briefingText = AddText(body.gameObject, _mission != null ? _mission.briefing : "",
                BriefingSize, Ink, TextAnchor.MiddleCenter);
            _briefingText.lineSpacing = 1.2f;

            var begin = CreateButton("BeginButton", "Begin Mission");
            begin.onClick.AddListener(TriggerTransition);

            var hint = NewRect("BriefingHint", transform);
            Place(hint, new Vector2(0f, HintY), new Vector2(TitleWidth, HintHeight));
            AddText(hint.gameObject, "SPACE  begin", HintSize, Pencil, TextAnchor.MiddleCenter);
        }

        private string TitleLine() {
            string t = _mission != null && !string.IsNullOrEmpty(_mission.title) ? _mission.title : "";
            return string.IsNullOrEmpty(t) ? $"Mission {_currentMission}" : $"Mission {_currentMission}: {t}";
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
                    Debug.LogWarning("BriefingView: Missing sprite '" + ButtonSpritePath + "', using a solid fallback.");
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
            var txt = AddText(textRect.gameObject, label, ButtonSize, Ink, TextAnchor.MiddleCenter);
            txt.fontStyle = FontStyle.Bold;

            return btn;
        }

        // ── Data loading ────────────────────────────────────────────────────

        /// <summary>Load the mission JSON from Resources/Missions/mission_0N.json.</summary>
        private void LoadMissionData(int missionNumber) {
            var path = $"Missions/mission_{missionNumber:D2}";
            var textAsset = Resources.Load<TextAsset>(path);
            MissionData data = textAsset != null ? JsonUtility.FromJson<MissionData>(textAsset.text) : null;
            if (data == null) {
                Debug.LogWarning($"BriefingView: Mission data missing or unreadable at '{path}'.");
                data = new MissionData();
                data.id = missionNumber;
                data.title = $"Mission {missionNumber}";
                data.briefing = "No briefing text available.";
            }
            _mission = data;
        }

        // ── Transition ──────────────────────────────────────────────────────

        /// <summary>Start the mission once (button, Space/Enter or the 5 s timer). The Space or
        /// click that starts it never reaches the plane: PlayerInput treats a button held
        /// across a UI screen as spent until it is released (PlayerInputGate).</summary>
        public void TriggerTransition() {
            if (_transitioned) return;
            _transitioned = true;
            // Begin (button, Space/Enter or the timer) stamps the orders. Stamp is the Begin
            // button's click cue, so no UiClick on top of it (T952).
            PlayUiCue(SfxId.Stamp);
            Debug.Log($"[BriefingView] Starting mission {_currentMission}: {_mission?.title}");
            // StartMission moves GameManager to Playing, which hides this view (or the
            // MainMenu → Game scene load destroys it).
            GameManager.Instance.StartMission(_currentMission);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>UI cue through AudioManager.PlayUi (plays while time is frozen). Skipped
        /// when no AudioManager exists (EditMode tests) (T952).</summary>
        private static void PlayUiCue(SfxId id) {
            var audio = AudioManager.GetOrNull();
            if (audio != null) audio.PlayUi(id);
        }

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

        private Text AddText(GameObject go, string value, int size, Color color, TextAnchor align) {
            var txt = go.AddComponent<Text>();
            txt.font = _font;
            txt.text = value;
            txt.fontSize = size;
            txt.alignment = align;
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
