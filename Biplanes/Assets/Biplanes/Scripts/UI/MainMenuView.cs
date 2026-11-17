using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI; // InputSystemUIInputModule (project uses Input System only)
using UnityEngine.UI;
using Biplanes.Audio;
using Biplanes.Core;
using Biplanes.Missions;

namespace Biplanes.UI {

    /// <summary>
    /// Main menu with mission select (UI_SPEC §3.1). Shown while GameManager is in Menu.
    ///
    /// Layout, anchored to the canvas centre: title logo, a row of 5 mission buttons
    /// (missions above SaveSystem.GetUnlockedMission() are dimmed and not clickable), and a
    /// "How to Play" button that toggles a controls panel.
    ///
    /// Built entirely from code on its own overlay canvas — no prefabs (ARCHITECTURE §0).
    /// Public API: <see cref="Show"/>, <see cref="Hide"/>, <see cref="IsShowing"/>.
    /// </summary>
    public class MainMenuView : MonoBehaviour {

        // ── Layout constants (reference resolution 1280×720) ───────────────

        private const int   MissionCount        = 5;
        private const int   CanvasSortingOrder  = 100;   // above the HUD canvas
        private const float ReferenceWidth      = 1280f;
        private const float ReferenceHeight     = 720f;
        private const float MatchWidthOrHeight  = 0.5f;

        private const float TitleY              = 190f;
        private const float TitleWidth          = 640f;
        private const float TitleHeight         = 160f;
        private const int   TitleFontSize       = 64;

        private const float MissionRowY         = 0f;
        private const float MissionButtonWidth  = 220f;
        private const float MissionButtonHeight = 96f;
        private const float MissionButtonGap    = 20f;
        private const int   MissionFontSize     = 18;
        private const float LabelPadding        = 8f;

        private const float HowToPlayY          = -150f;
        private const float HowToPlayWidth      = 220f;
        private const float HowToPlayHeight     = 50f;
        private const int   ButtonFontSize      = 20;

        private const float HelpPanelY          = -260f;
        private const float HelpPanelWidth      = 760f;
        private const float HelpPanelHeight     = 150f; // 5 lines of help
        private const int   HelpFontSize        = 18;

        private const float LockedAlpha         = 0.35f;
        private const float ColorFadeDuration   = 0.1f;

        private const string FontResource       = "LegacyRuntime.ttf";
        private const string TitleSpritePath    = "Sprites/ui/title_logo";
        private const string ButtonSpritePath   = "Sprites/ui/ui_button";
        private const string PanelSpritePath    = "Sprites/ui/ui_panel";

        private const string HelpText =
            "W / S  climb / dive\n" +
            "A / D  face left / right\n" +
            "LEFT MOUSE (hold)  machine gun\n" +
            "SPACE  drop a bomb (7 per mission)\n" +
            "ESC  pause";

        // ── Palette (ART_STYLE / UI_SPEC §2) ───────────────────────────────

        private static readonly Color Paper   = new Color(0.059f, 0.165f, 0.29f, 1f); // #0F2A4A
        private static readonly Color Ink     = new Color(0.91f, 0.945f, 0.98f, 1f); // #E8F1FA
        private static readonly Color Pencil  = new Color(0.616f, 0.706f, 0.8f, 1f); // #9DB4CC
        private static readonly Color Hostile = new Color(0.894f, 0.341f, 0.18f, 1f); // #E4572E

        // ── State ──────────────────────────────────────────────────────────

        private static MainMenuView _instance;

        private Font _font;
        private Sprite _buttonSprite;
        private GameObject _helpPanel;
        private readonly Button[] _missionButtons = new Button[MissionCount];

        /// <summary>True while a main menu exists in the scene.</summary>
        public static bool IsShowing => _instance != null;

        // ── Public API ─────────────────────────────────────────────────────

        /// <summary>
        /// Create the main menu (its own overlay canvas, plus an EventSystem if the scene
        /// has none) and return it. If it already exists, refresh the unlocked missions and
        /// return the existing one.
        /// </summary>
        public static MainMenuView Show() {
            if (_instance != null) {
                _instance.RefreshUnlocked();
                return _instance;
            }

            EnsureEventSystem();

            var canvasGO = new GameObject("MainMenuCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = MatchWidthOrHeight;

            canvasGO.AddComponent<GraphicRaycaster>();
            return canvasGO.AddComponent<MainMenuView>();
        }

        /// <summary>Destroy the main menu (its whole canvas). Safe to call when none exists.</summary>
        public static void Hide() {
            if (_instance == null) return;
            var go = _instance.gameObject;
            _instance = null;
            Destroy(go);
        }

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            _font = Resources.GetBuiltinResource<Font>(FontResource);
            _buttonSprite = LoadSprite(ButtonSpritePath);
            BuildVisuals();
            RefreshUnlocked();
            PlayUiCue(SfxId.UiPage); // the menu screen opens (T952)
        }

        private void OnDestroy() {
            if (_instance == this) _instance = null;
        }

        // ── Visual construction (no prefabs) ───────────────────────────────

        private void BuildVisuals() {
            // Full-screen paper backdrop: hides whatever the world is doing behind the menu.
            var backdrop = CreateRect("Backdrop", transform, Vector2.zero, Vector2.zero);
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero;
            backdrop.offsetMax = Vector2.zero;
            var backdropImg = backdrop.gameObject.AddComponent<Image>();
            backdropImg.color = Paper;          // no sprite = solid rect
            backdropImg.raycastTarget = true;   // swallow clicks meant for the world

            BuildTitle();

            // Mission buttons, one row centred on the canvas.
            float rowWidth = MissionCount * MissionButtonWidth + (MissionCount - 1) * MissionButtonGap;
            float firstX = -rowWidth * 0.5f + MissionButtonWidth * 0.5f;
            for (int i = 0; i < MissionCount; i++) {
                int missionIndex = i + 1;
                string label = "Mission " + missionIndex + "\n" + LoadMissionTitle(missionIndex);
                var pos = new Vector2(firstX + i * (MissionButtonWidth + MissionButtonGap), MissionRowY);
                var btn = CreateButton("Mission" + missionIndex + "Button", label, pos,
                    new Vector2(MissionButtonWidth, MissionButtonHeight), MissionFontSize);
                btn.onClick.AddListener(() => OnMissionClicked(missionIndex));
                _missionButtons[i] = btn;
            }

            var howTo = CreateButton("HowToPlayButton", "How to Play", new Vector2(0f, HowToPlayY),
                new Vector2(HowToPlayWidth, HowToPlayHeight), ButtonFontSize);
            howTo.onClick.AddListener(OnHowToPlayClicked);

            BuildHelpPanel();
        }

        private void BuildTitle() {
            var rect = CreateRect("Title", transform, new Vector2(0f, TitleY), new Vector2(TitleWidth, TitleHeight));
            var logo = Resources.Load<Sprite>(TitleSpritePath);
            if (logo != null) {
                var img = rect.gameObject.AddComponent<Image>();
                img.sprite = logo;
                img.preserveAspect = true;
                img.raycastTarget = false;
            } else {
                Debug.LogWarning("MainMenuView: Missing sprite '" + TitleSpritePath + "', using text title.");
                var txt = AddText(rect.gameObject, "BIPLANES", TitleFontSize, Ink);
                txt.raycastTarget = false;
            }
        }

        private void BuildHelpPanel() {
            var rect = CreateRect("HowToPlayPanel", transform, new Vector2(0f, HelpPanelY),
                new Vector2(HelpPanelWidth, HelpPanelHeight));
            var img = rect.gameObject.AddComponent<Image>();
            var panelSprite = LoadSprite(PanelSpritePath);
            if (panelSprite != null) {
                img.sprite = panelSprite;
                img.color = Color.white;
            } else {
                img.color = Paper;
            }
            img.raycastTarget = false;

            var textRect = CreateRect("HowToPlayText", rect, Vector2.zero, Vector2.zero);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(LabelPadding, LabelPadding);
            textRect.offsetMax = new Vector2(-LabelPadding, -LabelPadding);
            var txt = AddText(textRect.gameObject, HelpText, HelpFontSize, Ink);
            txt.raycastTarget = false;

            _helpPanel = rect.gameObject;
            _helpPanel.SetActive(false);
        }

        private Button CreateButton(string name, string label, Vector2 pos, Vector2 size, int fontSize) {
            var rect = CreateRect(name, transform, pos, size);
            var img = rect.gameObject.AddComponent<Image>();
            if (_buttonSprite != null) img.sprite = _buttonSprite;
            else img.color = Paper; // solid fallback (warning already logged once)
            img.raycastTarget = true;

            var btn = rect.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = Pencil;   // hover = pencil glow (UI_SPEC §2)
            colors.selectedColor    = Pencil;
            colors.pressedColor     = Hostile;  // active = hostile tint (UI_SPEC §2)
            colors.disabledColor    = new Color(Pencil.r, Pencil.g, Pencil.b, LockedAlpha);
            colors.colorMultiplier  = 1f;
            colors.fadeDuration     = ColorFadeDuration;
            btn.colors = colors;

            var textRect = CreateRect(name + "Text", rect, Vector2.zero, Vector2.zero);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(LabelPadding, LabelPadding);
            textRect.offsetMax = new Vector2(-LabelPadding, -LabelPadding);
            var txt = AddText(textRect.gameObject, label, fontSize, Ink);
            txt.raycastTarget = false;

            return btn;
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 pos, Vector2 size) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }

        private Text AddText(GameObject go, string value, int fontSize, Color color) {
            var txt = go.AddComponent<Text>();
            txt.font = _font;
            txt.text = value;
            txt.fontSize = fontSize;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.color = color;
            return txt;
        }

        private static Sprite LoadSprite(string path) {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null) {
                Debug.LogWarning("MainMenuView: Missing sprite '" + path + "', using a solid fallback.");
            }
            return sprite;
        }

        /// <summary>Read the mission title from Resources/Missions/mission_0N.json (fallback "Mission N").</summary>
        private static string LoadMissionTitle(int missionIndex) {
            var asset = Resources.Load<TextAsset>($"Missions/mission_{missionIndex:D2}");
            if (asset == null) return "";
            var data = JsonUtility.FromJson<MissionData>(asset.text);
            return data != null && !string.IsNullOrEmpty(data.title) ? data.title : "";
        }

        /// <summary>Create an EventSystem with an InputSystemUIInputModule if the scene has
        /// none. Shared by every code-built view (Briefing, Pause, Result).</summary>
        internal static void EnsureEventSystem() {
            if (EventSystem.current != null) return;
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            var module = esGO.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        // ── Unlock state ───────────────────────────────────────────────────

        /// <summary>Enable missions 1..unlocked; dim and disable the rest.</summary>
        private void RefreshUnlocked() {
            int unlocked = Mathf.Clamp(SaveSystem.GetUnlockedMission(), 1, MissionCount);
            for (int i = 0; i < MissionCount; i++) {
                var btn = _missionButtons[i];
                if (btn == null) continue;
                bool open = (i + 1) <= unlocked;
                btn.interactable = open;
                var label = btn.GetComponentInChildren<Text>();
                if (label != null) label.color = open ? Ink : new Color(Pencil.r, Pencil.g, Pencil.b, LockedAlpha);
            }
        }

        // ── Button handlers ────────────────────────────────────────────────

        private void OnMissionClicked(int missionIndex) {
            if (missionIndex > SaveSystem.GetUnlockedMission()) return; // locked: do nothing
            PlayUiCue(SfxId.UiClick);
            // Menu → Briefing (UI_SPEC §3.1): leaving Menu hides this view; BriefingView
            // then calls GameManager.StartMission (T937).
            GameManager.Instance.ShowBriefing(missionIndex);
        }

        private void OnHowToPlayClicked() {
            PlayUiCue(SfxId.UiClick);
            if (_helpPanel != null) _helpPanel.SetActive(!_helpPanel.activeSelf);
        }

        /// <summary>UI cue through AudioManager.PlayUi (plays while paused). Skipped when no
        /// AudioManager exists (EditMode tests) (T952).</summary>
        private static void PlayUiCue(SfxId id) {
            var audio = AudioManager.GetOrNull();
            if (audio != null) audio.PlayUi(id);
        }
    }
}
