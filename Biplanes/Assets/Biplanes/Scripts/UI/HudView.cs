using UnityEngine;
using UnityEngine.UI;
using Biplanes.Core;

namespace Biplanes.UI {

    /// <summary>
    /// In-game HUD overlay visible during Playing missions. Provides real-time feedback:
    /// damage bar (with smoke warning at >= 70%), kill score, bomb count row, and
    /// mission objective text. All widgets built programmatically from code — no prefabs.
    ///
    /// Subscribes reactively to GameEvents — zero polling in Update (UI_SPEC §3.3).
    /// Duotone palette colours from ART_STYLE.md + UI_SPEC §2.
    /// </summary>
    public class HudView : MonoBehaviour {

        // ── Balance / tunables ─────────────────────────────────────────────

        [SerializeField] private float _barWidth = 200f;         // damage bar max width (UI_SPEC §3.3)
        [SerializeField] private float _barHeight = 24f;         // damage bar height
        [SerializeField] private int   _bombSlotSize = 36;       // bomb icon size in pixels
        [SerializeField] private float _topPadding = 16f;        // 16px from corner (UI_SPEC)

        // ── UI component references (cached in Awake/OnEnable) ────────────

        // Damage bar.
        private Image _damageFrame;            // background frame
        private Image _damageFill;             // fill that scales with damage percentage
        private GameObject _smokeIconObj;      // smoke warning object
        private Image   _smokeIconImage;

        // Score display (top-right).
        private Text    _scoreText;

        // Objective text (below score, left-aligned under it).
        private Text    _objectiveText;

        // Objective tracking state — set by MissionRunner during mission init.
        private string  _objectiveTargetType = "";       // e.g. "fighter"
        private int     _objectiveRemaining  = 0;         // current count remaining
        private bool    _objectiveActive     = false;      // is this HUD objective visible?

        // Bomb icons row.
        private Image[] _bombIcons = new Image[7];  // UI_SPEC §3.3: exactly 7 slots

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Awake() {
            BuildVisuals();
            CacheSprites();
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

        // ── Visual construction per UI_SPEC §3.3 ───────────────────────────

        /// <summary>Builds every HUD widget hierarchy from code (no prefabs). (ART_RULE #1)</summary>
        private void BuildVisuals() {
            BuildDamageBar();
            BuildScoreText();
            BuildObjectiveText();
            BuildBombIcons();
        }

        // ── Damage bar: top-left, ink → hostile at 70% ─────────────────────

        /// <summary>Damage frame background (ui_damage_frame).</summary>
        private readonly Color _paperCol = new Color(0.059f, 0.165f, 0.29f, 1f); // #0F2A4A
        private readonly Color _inkCol   = new Color(0.91f, 0.94f, 0.98f, 1f);   // #E8F1FA
        private readonly Color _hostileCol = new Color(0.894f, 0.341f, 0.18f, 1f); // #E4572E

        private void BuildDamageBar() {
            // Damage frame (container panel).
            var frameObj = new GameObject("DamageFrame");
            frameObj.transform.SetParent(transform, false);
            var frameRect = frameObj.GetComponent<RectTransform>() ?? frameObj.AddComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0f, 1f);       // top-left
            frameRect.anchorMax = new Vector2(0f, 1f);
            frameRect.pivot   = new Vector2(0f, 1f);
            frameRect.sizeDelta = new Vector2(_barWidth + 8f, _barHeight + 6f);
            frameRect.offsetMin = new Vector3(_topPadding, -_topPadding);
            frameRect.offsetMax = Vector3.zero;

            _damageFrame = frameObj.AddComponent<Image>();
            var frameSprite = Resources.Load<Sprite>("Sprites/ui/ui_damage_frame");
            if (frameSprite != null) {
                _damageFrame.sprite = frameSprite;
            } else {
                Debug.LogWarning("HudView: Missing sprite 'Sprites/ui/ui_damage_frame', using paper-colour fallback.");
                _damageFrame.sprite = CreateWhiteSprite();
            }
            _damageFrame.color = _paperCol;

            // Damage fill (child of frame, scales in X from 0 to width).
            var fillObj = new GameObject("DamageFill");
            fillObj.transform.SetParent(frameObj.transform, false);
            var fillRect = fillObj.GetComponent<RectTransform>() ?? fillObj.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.left;                 // left-pinned fill
            fillRect.anchorMax = Vector2.left;
            fillRect.pivot   = new Vector2(0f, 0.5f);
            fillRect.sizeDelta = new Vector2(_barWidth, _barHeight);
            fillRect.offsetMin = new Vector3(4f, 0f);         // inside the frame padding
            fillRect.offsetMax = Vector3.zero;

            _damageFill = fillObj.AddComponent<Image>();
            _damageFill.type = Image.Type.Filled;
            _damageFill.fillMethod = Image.FillMethod.Horizontal;
            _damageFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _damageFill.fillAmount = 0f;                       // starts empty
            _damageFill.color = _inkCol;

            // Smoke warning icon — a simple text "SMOKE" label per UI_SPEC §3.3.
            // We use pencil colour because no warning-specific token exists.
            var smokeObj = new GameObject("SmokeWarning");
            smokeObj.transform.SetParent(transform, false);
            var smokeRect = smokeObj.GetComponent<RectTransform>() ?? smokeObj.AddComponent<RectTransform>();
            smokeRect.anchorMin = new Vector2(0f, 1f);
            smokeRect.anchorMax = new Vector2(0f, 1f);
            smokeRect.pivot   = new Vector2(0f, 1f);
            smokeRect.sizeDelta = new Vector2(_barWidth, 28f);
            smokeRect.offsetMin = new Vector3(_topPadding, -(_topPadding + _barHeight + 26f));
            smokeRect.offsetMax = Vector3.zero;

            _smokeIconObj = smokeObj;
            var smokeTxt = smokeObj.AddComponent<Text>();
            smokeTxt.text = "SMOKE";
            smokeTxt.fontStyle = FontStyle.Bold;
            smokeTxt.alignment = TextAnchor.MiddleLeft;
            smokeTxt.fontSize = 16;
            smokeTxt.color = _hostileCol; // visible in hostile tone when active
            _smokeIconImage = null;        // text component, not image-based.

            smokeObj.SetActive(false);   // hidden until damage >= threshold
        }

        // ── Score display: top-right, large font (UI_SPEC §3.3) ────────────

        private void BuildScoreText() {
            var obj = new GameObject("ScoreDisplay");
            obj.transform.SetParent(transform, false);
            var rect = obj.GetComponent<RectTransform>() ?? obj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);      // top-right
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot   = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(300f, 40f);
            rect.offsetMin = new Vector3(-_topPadding - 280f, -_topPadding); // right-anchored
            rect.offsetMax = Vector3.zero;

            _scoreText = obj.AddComponent<Text>();
            _scoreText.text = "Score: 0";
            _scoreText.fontStyle = FontStyle.Bold;
            _scoreText.alignment = TextAnchor.MiddleRight;
            _scoreText.fontSize = 24;                     // UI_SPEC §3.3: large font
            _scoreText.color = _inkCol;
        }

        // ── Objective text: below score, left-aligned (UI_SPEC §3.3) ───────

        private void BuildObjectiveText() {
            var obj = new GameObject("ObjectiveText");
            obj.transform.SetParent(transform, false);
            var rect = obj.GetComponent<RectTransform>() ?? obj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot   = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(300f, 64f);
            rect.offsetMin = new Vector3(-_topPadding - 280f, -(_topPadding + 50f)); // under score
            rect.offsetMax = Vector3.zero;

            _objectiveText = obj.AddComponent<Text>();
            _objectiveText.text = "Objective:";
            _objectiveText.fontStyle = FontStyle.Bold;
            _objectiveText.alignment = TextAnchor.MiddleLeft;
            _objectiveText.fontSize = 18;
            _objectiveText.color = new Color(0.62f, 0.71f, 0.8f, 1f); // pencil #9DB4CC

            // Start hidden until mission objectives exist.
            obj.SetActive(false);
        }

        // ── Bomb icons row: bottom-left, 7 slots (UI_SPEC §3.3) ────────────

        private void BuildBombIcons() {
            var rowObj = new GameObject("BombRow");
            rowObj.transform.SetParent(transform, false);
            var rowRect = rowObj.GetComponent<RectTransform>() ?? rowObj.AddComponent<RectTransform>();
            rowRect.anchorMin = Vector2.zero;                // bottom-left
            rowRect.anchorMax = Vector2.zero;
            rowRect.pivot   = new Vector2(0f, 0f);
            rowRect.sizeDelta = Vector2.zero;
            rowRect.offsetMin = new Vector3(_topPadding, _topPadding);
            rowRect.offsetMax = Vector3.zero;

            var spacing = _bombSlotSize + 8f;
            for (int i = 0; i < 7; i++) {
                var iconObj = new GameObject("BombIcon_" + i);
                iconObj.transform.SetParent(rowObj.transform, false);
                var iconRect = iconObj.GetComponent<RectTransform>() ?? iconObj.AddComponent<RectTransform>();
                iconRect.anchorMin = Vector2.zero;
                iconRect.anchorMax = Vector2.zero;
                iconRect.pivot   = new Vector2(0.5f, 0.5f);
                iconRect.sizeDelta = new Vector2(_bombSlotSize, _bombSlotSize);
                iconRect.offsetMin = new Vector3(i * spacing, 0f);
                iconRect.offsetMax = new Vector3((i + 1) * spacing, (float)_bombSlotSize);

                var iconImage = iconObj.AddComponent<Image>();
                // Fill colour starts friendly (available bomb).
                _bombIcons[i] = iconImage;
            }
        }

        // ── Sprite caching (single Resources.Load call per sprite) ─────────

        private Sprite _bombIconSprite;
        private void CacheSprites() {
            _bombIconSprite = Resources.Load<Sprite>("Sprites/ui/ui_bomb_icon");
        }

        // ── Event handlers (reactive updates, no polling) ──────────────────

        /// <summary>Handles GameEvents.PlayerDamageChanged — updates damage fill and smoke warning.</summary>
        private void OnPlayerDamageChanged(float damage) {
            if (_damageFill != null) {
                _damageFill.fillAmount = Mathf.Clamp01(damage / 100f);

                // Transition fill colour at smoke threshold: ink → hostile (UI_SPEC §2).
                if (damage >= 70f) {
                    // Lerp from ink towards hostile based on how far past 70%.
                    float t = Mathf.InverseLerp(70f, 100f, damage);
                    _damageFill.color = Color.Lerp(_inkCol, _hostileCol, t);
                } else {
                    _damageFill.color = _inkCol;
                }
            }

            // Smoke warning: visible when damage >= smokeThreshold (UI_SPEC §3.3).
            if (_smokeIconObj != null) {
                GameBalance balance = GameBalance.Get();
                float threshold = balance != null && balance.player != null ? balance.player.smokeThreshold : 70f;
                _smokeIconObj.SetActive(damage >= threshold);
            }
        }

        /// <summary>Handles GameEvents.ScoreChanged — updates score display text (UI_SPEC §3.3).</summary>
        private void OnScoreChanged(int score) {
            if (_scoreText != null) {
                _scoreText.text = $"Score: {score:D4}";
            }
        }

        /// <summary>Handles GameEvents.BombsChanged — updates bomb icons row (UI_SPEC §3.3).</summary>
        private void OnBombsChanged(int bombsRemaining) {
            for (int i = 0; i < _bombIcons.Length; i++) {
                if (_bombIcons[i] == null) continue;

                bool isAvailable = i < bombsRemaining;
                // Filled friendly #F2C14E when available, grey pencil #9DB4CC when empty (UI_SPEC §2).
                _bombIcons[i].color = isAvailable
                    ? new Color(0.949f, 0.757f, 0.306f, 1f)   // friendly #F2C14E
                    : new Color(0.616f, 0.706f, 0.8f, 1f);    // pencil #9DB4CC

                if (_bombIconSprite != null) {
                    _bombIcons[i].sprite = _bombIconSprite;
                } else {
                    Debug.LogWarning("HudView: Missing sprite 'Sprites/ui/ui_bomb_icon', using filled rect fallback.");
                    // Fallback: tinted solid rectangle.
                    _bombIcons[i].color = isAvailable
                        ? new Color(0.949f, 0.757f, 0.306f, 1f)   // friendly yellow
                        : new Color(0.616f, 0.706f, 0.8f, 1f);    // grey pencil
                }
            }
        }

        /// <summary>Handles GameEvents.EntityDestroyed — decrements objective counter (UI_SPEC §3.3).</summary>
        private void OnEntityDestroyed(string entityType, Team team) {
            if (_objectiveText == null || !_objectiveActive) return;

            // Only track the active target type.
            if (!string.IsNullOrEmpty(_objectiveTargetType) && !entityType.Equals(_objectiveTargetType, System.StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            _objectiveRemaining = Mathf.Max(0, _objectiveRemaining - 1);

            string targetLabel = entityType.ToLowerInvariant();
            _objectiveText.text = $"Destroy {_objectiveRemaining} more {targetLabel}s";

            if (_objectiveRemaining == 0) {
                // Objective complete: brief green flash (heal colour).
                _objectiveText.color = new Color(0.47f, 0.83f, 0.54f, 1f);  // heal #7BD389
            }
        }

        /// <summary>Set or update the active mission objective (called by MissionRunner).</summary>
        public void SetObjective(string targetType, int total) {
            _objectiveTargetType = targetType;
            _objectiveRemaining  = total;
            _objectiveActive     = true;

            if (_objectiveText != null) {
                string label = targetType.ToLowerInvariant();
                _objectiveText.text = $"Destroy {total} more {label}s";
                _objectiveText.color = new Color(0.62f, 0.71f, 0.8f, 1f); // pencil #9DB4CC (reset)
                if (_objectiveText.gameObject != null) {
                    _objectiveText.gameObject.SetActive(true);
                }
            }
        }

        /// <summary>Called when the mission objective is fully completed.</summary>
        private void OnObjectiveCompleted(string objectiveId) {
            if (!_objectiveActive) return;

            // Show completion text briefly then hide.
            if (_objectiveText != null && _objectiveText.gameObject != null) {
                _objectiveText.text = "Objective Complete!";
                _objectiveText.color = new Color(0.47f, 0.83f, 0.54f, 1f);  // heal #7BD389

                StartCoroutine(HideObjectiveAfterDelay(2f));
            }
            _objectiveActive = false;
        }

        /// <summary>Coroutine: hide the objective text after a delay (UI_SPEC §4 — flash feedback).</summary>
        private System.Collections.IEnumerator HideObjectiveAfterDelay(float delay) {
            yield return new WaitForSeconds(delay);
            if (_objectiveText != null && _objectiveText.gameObject != null) {
                _objectiveText.gameObject.SetActive(false);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>Creates a 1×1 white sprite for fallback when a resource is missing.</summary>
        private static Sprite CreateWhiteSprite() {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
        }
    }
}
