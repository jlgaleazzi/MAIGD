using UnityEngine;
using UnityEngine.UI;
using Biplanes.Core;

namespace Biplanes.UI {

    /// <summary>
    /// In-game HUD overlay visible during Playing missions. Provides real-time feedback:
    /// damage bar (with smoke warning at the smoke threshold), score, bomb icon row and
    /// mission objective text. All widgets are built from code, no prefabs.
    ///
    /// Subscribes reactively to GameEvents, no polling in Update (UI_SPEC §3.3).
    /// The initial state never depends on event order (B022): every widget is built showing
    /// a fresh mission (0% damage, score 0, a full bomb bay read from Balance.json), and the
    /// events only update it afterwards.
    ///
    /// Layout rule (B022): every RectTransform is placed with anchor + pivot +
    /// anchoredPosition + sizeDelta. Never set offsetMin/offsetMax after sizeDelta: they
    /// overwrite the size. Every Image gets a sprite (a Filled Image without one draws a
    /// full white rect) and every Text gets a font (without one it draws nothing).
    /// Duotone palette colours from ART_STYLE.md + UI_SPEC §2.
    /// </summary>
    public class HudView : MonoBehaviour {

        // ── Layout (UI pixels, UI_SPEC §3.3) ──────────────────────────────

        [SerializeField] private float _barWidth = 200f;         // damage bar max width
        [SerializeField] private float _barHeight = 24f;         // damage bar height
        [SerializeField] private float _bombSlotSize = 36f;      // bomb icon size
        [SerializeField] private float _bombSlotGap = 8f;        // gap between bomb icons
        [SerializeField] private float _padding = 16f;           // distance from screen corners

        private const float FramePadX = 4f;          // frame border around the fill, each side
        private const float FramePadY = 3f;
        private const float ScoreWidth = 300f;
        private const float ScoreHeight = 40f;
        private const float ObjectiveHeight = 64f;
        private const float SmokeHeight = 28f;
        private const float WidgetGap = 6f;

        /// <summary>Used only when Balance.json can't be read (UI_SPEC §3.3: 7 slots).</summary>
        private const int FallbackBombCapacity = 7;
        /// <summary>Used only when Balance.json can't be read (GDD: smoke at 70%).</summary>
        private const float FallbackSmokeThreshold = 70f;

        // ── Palette (UI_SPEC §2) ──────────────────────────────────────────

        public static readonly Color PaperCol    = new Color(0.059f, 0.165f, 0.29f, 1f);  // #0F2A4A
        public static readonly Color InkCol      = new Color(0.91f, 0.945f, 0.98f, 1f);   // #E8F1FA
        public static readonly Color HostileCol  = new Color(0.894f, 0.341f, 0.18f, 1f);  // #E4572E
        public static readonly Color FriendlyCol = new Color(0.949f, 0.757f, 0.306f, 1f); // #F2C14E
        public static readonly Color PencilCol   = new Color(0.616f, 0.706f, 0.8f, 1f);   // #9DB4CC
        public static readonly Color HealCol     = new Color(0.482f, 0.827f, 0.537f, 1f); // #7BD389

        // ── Resource paths ────────────────────────────────────────────────

        private const string BombIconPath     = "Sprites/ui/ui_bomb_icon";
        private const string DamageFramePath  = "Sprites/ui/ui_damage_frame";
        private const string FontResource     = "LegacyRuntime.ttf";

        // Shared across HUD instances so a missing asset warns once per session, not per event
        // or per mission.
        private static Sprite _whiteSprite;
        private static Font   _font;
        private static bool   _warnedBombIcon;
        private static bool   _warnedDamageFrame;

        // ── UI component references (built in Awake) ──────────────────────

        private Image      _damageFrame;
        private Image      _damageFill;
        private GameObject _smokeIconObj;
        private Text       _scoreText;
        private Text       _objectiveText;
        private Image[]    _bombIcons;

        private Sprite _bombIconSprite;     // ui_bomb_icon, or the white fallback
        private Sprite _damageFrameSprite;  // ui_damage_frame, or the white fallback
        private float  _smokeThreshold = FallbackSmokeThreshold;
        private int    _bombCapacity   = FallbackBombCapacity;

        // Objective tracking state, set by MissionRunner during mission init.
        private string _objectiveTargetType = "";
        private int    _objectiveRemaining  = 0;
        private bool   _objectiveActive     = false;

        /// <summary>Number of bomb icons currently drawn as available (friendly colour).</summary>
        public int DisplayedBombs { get; private set; }

        /// <summary>Number of bomb slots in the row (Balance.json player.bombCapacity).</summary>
        public int BombSlots => _bombIcons != null ? _bombIcons.Length : 0;

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Awake() {
            ReadBalance();
            CacheAssets();
            BuildVisuals();
            ApplyInitialState();
            SubscribeEvents();
        }

        private void OnDestroy() {
            UnsubscribeEvents();
        }

        // ── Event subscription (reactive, no polling) ─────────────────────

        private void SubscribeEvents() {
            GameEvents.PlayerDamageChanged += OnPlayerDamageChanged;
            GameEvents.ScoreChanged        += OnScoreChanged;
            GameEvents.BombsChanged        += OnBombsChanged;
            GameEvents.EntityDestroyed     += OnEntityDestroyed;
            GameEvents.ObjectiveCompleted  += OnObjectiveCompleted;
        }

        private void UnsubscribeEvents() {
            GameEvents.PlayerDamageChanged -= OnPlayerDamageChanged;
            GameEvents.ScoreChanged        -= OnScoreChanged;
            GameEvents.BombsChanged        -= OnBombsChanged;
            GameEvents.EntityDestroyed     -= OnEntityDestroyed;
            GameEvents.ObjectiveCompleted  -= OnObjectiveCompleted;
        }

        // ── Setup ─────────────────────────────────────────────────────────

        private void ReadBalance() {
            GameBalance balance = GameBalance.Get();
            if (balance != null && balance.player != null) {
                if (balance.player.bombCapacity > 0) _bombCapacity = balance.player.bombCapacity;
                if (balance.player.smokeThreshold > 0f) _smokeThreshold = balance.player.smokeThreshold;
            }
        }

        /// <summary>Loads every sprite and the font once. Missing assets warn once, ever.</summary>
        private void CacheAssets() {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>(FontResource);

            _bombIconSprite = Resources.Load<Sprite>(BombIconPath);
            if (_bombIconSprite == null) {
                if (!_warnedBombIcon) {
                    Debug.LogWarning($"HudView: Missing sprite '{BombIconPath}', using a tinted rect fallback.");
                    _warnedBombIcon = true;
                }
                _bombIconSprite = WhiteSprite();
            }

            _damageFrameSprite = Resources.Load<Sprite>(DamageFramePath);
            if (_damageFrameSprite == null) {
                if (!_warnedDamageFrame) {
                    Debug.LogWarning($"HudView: Missing sprite '{DamageFramePath}', using a paper-colour fallback.");
                    _warnedDamageFrame = true;
                }
                _damageFrameSprite = WhiteSprite();
            }
        }

        /// <summary>Builds every HUD widget hierarchy from code (no prefabs).</summary>
        private void BuildVisuals() {
            BuildDamageBar();
            BuildScoreText();
            BuildObjectiveText();
            BuildBombIcons();
        }

        /// <summary>A fresh mission: 0% damage, score 0, a full bomb bay.</summary>
        private void ApplyInitialState() {
            OnPlayerDamageChanged(0f);
            OnScoreChanged(0);
            OnBombsChanged(_bombCapacity);
        }

        // ── Damage bar: top-left, ink to hostile at the smoke threshold ───

        private void BuildDamageBar() {
            float frameW = _barWidth + FramePadX * 2f;
            float frameH = _barHeight + FramePadY * 2f;

            var frameRect = NewRect("DamageFrame", transform);
            Place(frameRect, new Vector2(0f, 1f), new Vector2(_padding, -_padding), new Vector2(frameW, frameH));
            _damageFrame = frameRect.gameObject.AddComponent<Image>();
            _damageFrame.raycastTarget = false;
            _damageFrame.sprite = _damageFrameSprite;
            // The real sprite carries its own ink lines; the fallback is a paper-coloured rect.
            _damageFrame.color = _damageFrameSprite == _whiteSprite ? PaperCol : Color.white;

            // Fill: left-pinned inside the frame. A Filled Image needs a sprite, otherwise
            // it ignores fillAmount and draws a full white rect.
            var fillRect = NewRect("DamageFill", frameRect);
            fillRect.anchorMin = new Vector2(0f, 0.5f);
            fillRect.anchorMax = new Vector2(0f, 0.5f);
            fillRect.pivot     = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = new Vector2(FramePadX, 0f);
            fillRect.sizeDelta = new Vector2(_barWidth, _barHeight);
            _damageFill = fillRect.gameObject.AddComponent<Image>();
            _damageFill.raycastTarget = false;
            _damageFill.sprite = WhiteSprite();
            _damageFill.type = Image.Type.Filled;
            _damageFill.fillMethod = Image.FillMethod.Horizontal;
            _damageFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _damageFill.fillAmount = 0f;
            _damageFill.color = InkCol;

            // Smoke warning directly below the damage bar (UI_SPEC §3.3).
            var smokeRect = NewRect("SmokeWarning", transform);
            Place(smokeRect, new Vector2(0f, 1f),
                new Vector2(_padding, -(_padding + frameH + WidgetGap)),
                new Vector2(_barWidth, SmokeHeight));
            var smokeTxt = AddText(smokeRect.gameObject, "SMOKE", 16, TextAnchor.MiddleLeft, HostileCol);
            smokeTxt.fontStyle = FontStyle.Bold;
            _smokeIconObj = smokeRect.gameObject;
            _smokeIconObj.SetActive(false);
        }

        // ── Score display: top-right, large font (UI_SPEC §3.3) ───────────

        private void BuildScoreText() {
            var rect = NewRect("ScoreDisplay", transform);
            Place(rect, new Vector2(1f, 1f), new Vector2(-_padding, -_padding), new Vector2(ScoreWidth, ScoreHeight));
            _scoreText = AddText(rect.gameObject, "", 24, TextAnchor.MiddleRight, InkCol);
            _scoreText.fontStyle = FontStyle.Bold;
        }

        // ── Objective text: below score (UI_SPEC §3.3) ────────────────────

        private void BuildObjectiveText() {
            var rect = NewRect("ObjectiveText", transform);
            Place(rect, new Vector2(1f, 1f),
                new Vector2(-_padding, -(_padding + ScoreHeight + WidgetGap)),
                new Vector2(ScoreWidth, ObjectiveHeight));
            _objectiveText = AddText(rect.gameObject, "Objective:", 18, TextAnchor.UpperLeft, PencilCol);
            _objectiveText.fontStyle = FontStyle.Bold;
            rect.gameObject.SetActive(false);   // hidden until MissionRunner sets an objective
        }

        // ── Bomb icons row: bottom-left, one slot per bomb (UI_SPEC §3.3) ─

        private void BuildBombIcons() {
            int slots = _bombCapacity;
            float rowWidth = slots * _bombSlotSize + (slots - 1) * _bombSlotGap;

            var rowRect = NewRect("BombRow", transform);
            Place(rowRect, Vector2.zero, new Vector2(_padding, _padding), new Vector2(rowWidth, _bombSlotSize));

            _bombIcons = new Image[slots];
            for (int i = 0; i < slots; i++) {
                var iconRect = NewRect("BombIcon_" + i, rowRect);
                Place(iconRect, Vector2.zero,
                    new Vector2(i * (_bombSlotSize + _bombSlotGap), 0f),
                    new Vector2(_bombSlotSize, _bombSlotSize));

                var icon = iconRect.gameObject.AddComponent<Image>();
                icon.raycastTarget = false;
                icon.sprite = _bombIconSprite;
                icon.preserveAspect = true;   // ui_bomb_icon is portrait (96x128)
                icon.color = FriendlyCol;
                _bombIcons[i] = icon;
            }
        }

        // ── Event handlers (reactive updates, no polling) ──────────────────

        /// <summary>GameEvents.PlayerDamageChanged: damage fill and smoke warning.</summary>
        private void OnPlayerDamageChanged(float damage) {
            if (_damageFill != null) {
                _damageFill.fillAmount = Mathf.Clamp01(damage / 100f);
                // Ink up to the smoke threshold, then towards hostile (UI_SPEC §2).
                if (damage >= _smokeThreshold) {
                    float t = Mathf.InverseLerp(_smokeThreshold, 100f, damage);
                    _damageFill.color = Color.Lerp(InkCol, HostileCol, t);
                } else {
                    _damageFill.color = InkCol;
                }
            }

            if (_smokeIconObj != null) {
                _smokeIconObj.SetActive(damage >= _smokeThreshold);
            }
        }

        /// <summary>GameEvents.ScoreChanged: score display text.</summary>
        private void OnScoreChanged(int score) {
            if (_scoreText != null) {
                _scoreText.text = $"Score: {score:D4}";
            }
        }

        /// <summary>GameEvents.BombsChanged: friendly icons for available bombs, pencil for spent.</summary>
        private void OnBombsChanged(int bombsRemaining) {
            if (_bombIcons == null) return;
            int available = Mathf.Clamp(bombsRemaining, 0, _bombIcons.Length);
            for (int i = 0; i < _bombIcons.Length; i++) {
                if (_bombIcons[i] == null) continue;
                _bombIcons[i].color = i < available ? FriendlyCol : PencilCol;
            }
            DisplayedBombs = available;
        }

        /// <summary>GameEvents.EntityDestroyed: decrements the objective counter.</summary>
        private void OnEntityDestroyed(string entityType, Team team) {
            if (_objectiveText == null || !_objectiveActive || entityType == null) return;

            if (!string.IsNullOrEmpty(_objectiveTargetType)
                && !entityType.Equals(_objectiveTargetType, System.StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            _objectiveRemaining = Mathf.Max(0, _objectiveRemaining - 1);
            _objectiveText.text = $"Destroy {_objectiveRemaining} more {entityType.ToLowerInvariant()}s";

            if (_objectiveRemaining == 0) {
                _objectiveText.color = HealCol;
            }
        }

        /// <summary>Set or update the active mission objective (called by MissionRunner).</summary>
        public void SetObjective(string targetType, int total) {
            _objectiveTargetType = targetType ?? "";
            _objectiveRemaining  = total;
            _objectiveActive     = true;

            if (_objectiveText != null) {
                _objectiveText.text = $"Destroy {total} more {_objectiveTargetType.ToLowerInvariant()}s";
                _objectiveText.color = PencilCol;
                _objectiveText.gameObject.SetActive(true);
            }
        }

        /// <summary>GameEvents.ObjectiveCompleted: shows a short completion message.</summary>
        private void OnObjectiveCompleted(string objectiveId) {
            if (!_objectiveActive) return;

            if (_objectiveText != null) {
                _objectiveText.text = "Objective Complete!";
                _objectiveText.color = HealCol;
                if (isActiveAndEnabled) StartCoroutine(HideObjectiveAfterDelay(2f));
            }
            _objectiveActive = false;
        }

        /// <summary>Hides the objective text after a delay (UI_SPEC §4 flash feedback).</summary>
        private System.Collections.IEnumerator HideObjectiveAfterDelay(float delay) {
            yield return new WaitForSeconds(delay);
            if (_objectiveText != null) {
                _objectiveText.gameObject.SetActive(false);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static RectTransform NewRect(string name, Transform parent) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Anchor and pivot at the same corner, then position and size. No offsets.</summary>
        private static void Place(RectTransform rect, Vector2 corner, Vector2 position, Vector2 size) {
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot     = corner;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static Text AddText(GameObject go, string text, int size, TextAnchor align, Color color) {
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>A shared 1x1 white sprite, tinted by Image.color.</summary>
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
