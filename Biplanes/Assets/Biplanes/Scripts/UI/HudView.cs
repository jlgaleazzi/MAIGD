using UnityEngine;
using UnityEngine.UI;
using Biplanes.Core;
using Biplanes.Audio;
using Biplanes.Missions;

namespace Biplanes.UI {

    /// <summary>
    /// In-game HUD overlay visible during Playing missions. Provides real-time feedback:
    /// damage bar (with smoke warning at the smoke threshold), score, bomb icon row and
    /// mission objective text, plus the top-centre minimap (MinimapView, B044). All widgets
    /// are built from code, no prefabs.
    ///
    /// Subscribes reactively to GameEvents, no polling in Update (UI_SPEC §3.3). The objective
    /// number is read from the mission's ObjectiveTracker on kill events (B053).
    /// The initial state never depends on event order (B022): every widget is built showing
    /// a fresh mission (0% damage, score 0, a full bomb bay read from Balance.json), and the
    /// events only update it afterwards.
    ///
    /// Layout rule (B022): every RectTransform is placed with anchor + pivot +
    /// anchoredPosition + sizeDelta. Never set offsetMin/offsetMax after sizeDelta: they
    /// overwrite the size. Every Image gets a sprite (a Filled Image without one draws a
    /// full white rect) and every Text gets a font (without one it draws nothing).
    ///
    /// White only (B038b, GDD Style Guide > Palette and UI): every widget uses ink, pencil or
    /// paper. Meaning comes from shape, fill and flashing, never from hue: available bombs
    /// are filled ink, spent ones pencil at half alpha; the damage fill is solid ink and,
    /// from smokeThreshold, turns hatched and flashes (alpha 1.0 to 0.4, about 3 Hz, on
    /// unscaled time) together with the ink SMOKE warning (UI_SPEC §2, §3.3).
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

        // ── Warning flash and hatching (UI_SPEC §2) ───────────────────────

        private const float FlashHz        = 3f;     // bar + smoke warning flash rate
        private const float FlashMinAlpha  = 0.4f;   // alpha swings 1.0 <-> 0.4
        private const float SpentBombAlpha = 0.5f;   // spent bomb icon: pencil at 50% alpha
        private const int   HatchPeriod    = 8;      // texels per diagonal stripe pair
        private const int   HatchStripe    = 4;      // texels of solid ink per period
        private const float HatchGapAlpha  = 0.3f;   // the gaps between stripes stay faint ink

        /// <summary>Used only when Balance.json can't be read (UI_SPEC §3.3: 7 slots).</summary>
        private const int FallbackBombCapacity = 7;
        /// <summary>Used only when Balance.json can't be read (GDD: smoke at 70%).</summary>
        private const float FallbackSmokeThreshold = 70f;

        // ── Palette (UI_SPEC §2: ink, pencil, paper only; alpha allowed) ──

        public static readonly Color PaperCol    = new Color(0.059f, 0.165f, 0.29f, 1f);  // #0F2A4A
        public static readonly Color InkCol      = new Color(0.91f, 0.945f, 0.98f, 1f);   // #E8F1FA
        public static readonly Color PencilCol   = new Color(0.616f, 0.706f, 0.8f, 1f);   // #9DB4CC

        /// <summary>Tint of an available bomb icon: filled ink.</summary>
        public static readonly Color BombAvailableCol = InkCol;
        /// <summary>Tint of a spent bomb icon: pencil at 50% alpha.</summary>
        public static readonly Color BombSpentCol =
            new Color(PencilCol.r, PencilCol.g, PencilCol.b, SpentBombAlpha);

        /// <summary>Identity multiplier for sprites that carry their own ink lines (not a
        /// displayed colour).</summary>
        private static readonly Color NoTint = Color.white;

        // ── Resource paths ────────────────────────────────────────────────

        private const string BombIconPath     = "Sprites/ui/ui_bomb_icon";
        private const string DamageFramePath  = "Sprites/ui/ui_damage_frame";
        private const string FontResource     = "LegacyRuntime.ttf";

        // Shared across HUD instances so a missing asset warns once per session, not per event
        // or per mission.
        private static Sprite _whiteSprite;
        private static Sprite _hatchSprite;
        private static Font   _font;
        private static bool   _warnedBombIcon;
        private static bool   _warnedDamageFrame;

        // ── UI component references (built in Awake) ──────────────────────

        private Image      _damageFrame;
        private Image      _damageFill;
        private GameObject _smokeIconObj;
        private Text       _smokeText;
        private Text       _scoreText;
        private Text       _objectiveText;
        private Image[]    _bombIcons;
        private MinimapView _minimap;

        private Sprite _bombIconSprite;     // ui_bomb_icon, or the white fallback
        private Sprite _damageFrameSprite;  // ui_damage_frame, or the white fallback
        private float  _smokeThreshold = FallbackSmokeThreshold;
        private int    _bombCapacity   = FallbackBombCapacity;

        /// <summary>True while the damage is at or above the smoke threshold. The damage-warning
        /// cue plays on the upward crossing only and re-arms once the damage drops below (T951).</summary>
        private bool   _damageWarningLatched;

        /// <summary>True while the damage bar is hatched and flashing (damage at or above
        /// smokeThreshold).</summary>
        public bool DamageFlashing { get; private set; }
        private float _flashStartTime;

        // Objective display state, set by MissionRunner during mission init (SetObjective).
        // When the HUD sits under a MissionRunner whose mission has a matching
        // destroyAllOfType objective, the number is read from that runner's ObjectiveTracker
        // (B053), never counted here. _objectiveRemaining is only a standalone fallback.
        private string _objectiveTargetType = "";
        private int    _objectiveRemaining  = 0;
        private bool   _objectiveActive     = false;
        private ObjectiveTracker _objectiveTracker;      // found once in SetObjective
        private int    _trackedObjectiveIndex = -1;      // index into the mission objectives
        private bool   _objectiveRefreshPending;         // re-read next frame (event order)
        // B054: which mission objective the text shows, so another objective completing
        // (M4: base bombed before the balloons are down) leaves the counter alone.
        private ObjectiveEntry[] _boundObjectives;       // the runner's objectives, or null
        private int    _shownObjectiveIndex = -1;        // index into _boundObjectives, -1 = unbound
        private string _shownDescription;                // non-null: fixed text (non-destroyAll objective)
        private Coroutine _hideObjectiveRoutine;

        /// <summary>The objective number currently shown (tracker count when tracked).</summary>
        public int DisplayedObjectiveRemaining => _objectiveRemaining;

        /// <summary>True when the objective number is read from an ObjectiveTracker.</summary>
        public bool ObjectiveFromTracker => _objectiveTracker != null && _trackedObjectiveIndex >= 0;

        /// <summary>Number of bomb icons currently drawn as available (filled ink).</summary>
        public int DisplayedBombs { get; private set; }

        /// <summary>Number of bomb slots in the row (Balance.json player.bombCapacity).</summary>
        public int BombSlots => _bombIcons != null ? _bombIcons.Length : 0;

        /// <summary>The top-centre minimap strip (B044, UI_SPEC §3.3.1).</summary>
        public MinimapView Minimap => _minimap;

        /// <summary>How far the top-left widgets (damage frame) reach in from the left edge, px.</summary>
        public float LeftReserve => _padding + _barWidth + FramePadX * 2f;

        /// <summary>How far the top-right widgets (score, objective text) reach in from the
        /// right edge, px.</summary>
        public float RightReserve => _padding + ScoreWidth;

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

        /// <summary>Animates the warning flash only; does nothing below the smoke threshold.
        /// Unscaled time, so it keeps its rate under hit-stop or pause.</summary>
        private void Update() {
            if (_objectiveRefreshPending) {
                _objectiveRefreshPending = false;
                if (_objectiveActive) RefreshObjectiveText();
            }
            if (!DamageFlashing) return;
            ApplyFlashAlpha(FlashAlpha(Time.unscaledTime - _flashStartTime));
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
            BuildMinimap();
        }

        /// <summary>A fresh mission: 0% damage, score 0, a full bomb bay.</summary>
        private void ApplyInitialState() {
            OnPlayerDamageChanged(0f);
            OnScoreChanged(0);
            OnBombsChanged(_bombCapacity);
        }

        // ── Damage bar: top-left, solid ink, hatched + flashing from smoke ─

        private void BuildDamageBar() {
            float frameW = _barWidth + FramePadX * 2f;
            float frameH = _barHeight + FramePadY * 2f;

            var frameRect = NewRect("DamageFrame", transform);
            Place(frameRect, new Vector2(0f, 1f), new Vector2(_padding, -_padding), new Vector2(frameW, frameH));
            _damageFrame = frameRect.gameObject.AddComponent<Image>();
            _damageFrame.raycastTarget = false;
            _damageFrame.sprite = _damageFrameSprite;
            // The real sprite carries its own ink lines; the fallback is a paper-coloured rect.
            _damageFrame.color = _damageFrameSprite == _whiteSprite ? PaperCol : NoTint;

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
            _smokeText = AddText(smokeRect.gameObject, "DAMAGE", 16, TextAnchor.MiddleLeft, InkCol);
            _smokeText.fontStyle = FontStyle.Bold;
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
                icon.color = BombAvailableCol;
                _bombIcons[i] = icon;
            }
        }

        // ── Minimap: top-centre, clear of the corner widgets (UI_SPEC §3.3.1) ─

        /// <summary>MinimapView shows itself only while Playing or Paused and binds to the
        /// MissionRunner that owns this HUD canvas.</summary>
        private void BuildMinimap() {
            _minimap = MinimapView.Create(transform, LeftReserve, RightReserve);
        }

        // ── Event handlers (reactive updates, no polling) ──────────────────

        /// <summary>GameEvents.PlayerDamageChanged: damage fill and smoke warning.</summary>
        private void OnPlayerDamageChanged(float damage) {
            if (_damageFill != null) {
                _damageFill.fillAmount = Mathf.Clamp01(damage / 100f);
            }

            // Solid ink below the smoke threshold; hatched ink + flashing from it (UI_SPEC §2).
            bool warn = damage >= _smokeThreshold;
            if (_damageFill != null) {
                Sprite want = warn ? HatchSprite(_barWidth, _barHeight) : WhiteSprite();
                if (_damageFill.sprite != want) _damageFill.sprite = want;
            }
            if (warn && !DamageFlashing) _flashStartTime = Time.unscaledTime;   // starts at alpha 1
            DamageFlashing = warn;
            if (!warn) ApplyFlashAlpha(1f);   // no flashing below the threshold

            if (_smokeIconObj != null) {
                _smokeIconObj.SetActive(warn);
            }

            UpdateDamageWarningCue(damage);
        }

        /// <summary>Flash alpha <paramref name="elapsed"/> unscaled seconds after the warning
        /// started: 1.0 at 0, down to FlashMinAlpha and back, FlashHz times per second.</summary>
        public static float FlashAlpha(float elapsed) {
            float wave = 0.5f + 0.5f * Mathf.Cos(elapsed * FlashHz * 2f * Mathf.PI);
            return Mathf.Lerp(FlashMinAlpha, 1f, wave);
        }

        /// <summary>Bar fill and smoke warning share one alpha, so they flash in sync.</summary>
        private void ApplyFlashAlpha(float alpha) {
            var ink = new Color(InkCol.r, InkCol.g, InkCol.b, alpha);
            if (_damageFill != null) _damageFill.color = ink;
            if (_smokeText != null) _smokeText.color = ink;
        }

        /// <summary>DamageWarning once when the damage crosses smokeThreshold upward (T951).
        /// Re-arms only after the damage falls back below it (a health balloon). A fatal jump
        /// to 100% plays no warning: the crash cue covers it. Ui priority, so PlayUi.</summary>
        private void UpdateDamageWarningCue(float damage) {
            bool above = damage >= _smokeThreshold;
            if (!above) {
                _damageWarningLatched = false;
                return;
            }
            if (_damageWarningLatched) return;
            _damageWarningLatched = true;
            if (damage >= 100f) return;
            var audio = AudioManager.GetOrNull();
            if (audio != null) audio.PlayUi(SfxId.DamageWarning);
        }

        /// <summary>GameEvents.ScoreChanged: score display text.</summary>
        private void OnScoreChanged(int score) {
            if (_scoreText != null) {
                _scoreText.text = $"Score: {score:D4}";
            }
        }

        /// <summary>GameEvents.BombsChanged: filled ink icons for available bombs, half-alpha
        /// pencil for spent ones.</summary>
        private void OnBombsChanged(int bombsRemaining) {
            if (_bombIcons == null) return;
            int available = Mathf.Clamp(bombsRemaining, 0, _bombIcons.Length);
            for (int i = 0; i < _bombIcons.Length; i++) {
                if (_bombIcons[i] == null) continue;
                _bombIcons[i].color = i < available ? BombAvailableCol : BombSpentCol;
            }
            DisplayedBombs = available;
        }

        /// <summary>
        /// GameEvents.EntityDestroyed: refreshes the objective number. Only Team.Enemy kills
        /// that the tracker would count (ObjectiveTracker.CountsAsKillOf, so reinforcements
        /// count for "fighter") matter. With a tracker the number is re-read from it now and
        /// again next frame, so the HUD is right whichever handler runs first.
        /// </summary>
        private void OnEntityDestroyed(string entityType, Team team) {
            if (_objectiveText == null || !_objectiveActive || entityType == null) return;
            if (team != Team.Enemy) return;
            if (_shownDescription != null) return;   // a fixed-text objective has no counter
            if (!string.IsNullOrEmpty(_objectiveTargetType)
                && !ObjectiveTracker.CountsAsKillOf(_objectiveTargetType, entityType)) {
                return;
            }

            if (ObjectiveFromTracker) {
                _objectiveRefreshPending = true;
            } else {
                _objectiveRemaining = Mathf.Max(0, _objectiveRemaining - 1);
            }
            RefreshObjectiveText();
        }

        /// <summary>Set or update the active mission objective (called by MissionRunner).
        /// Finds the parent runner's ObjectiveTracker and the matching destroyAllOfType
        /// objective once; `total` is used only when there is no tracker to read.</summary>
        public void SetObjective(string targetType, int total) {
            _objectiveTargetType = targetType ?? "";
            _objectiveRemaining  = total;
            _objectiveActive     = true;
            _objectiveRefreshPending = false;
            _shownDescription    = null;
            if (_hideObjectiveRoutine != null) {
                StopCoroutine(_hideObjectiveRoutine);
                _hideObjectiveRoutine = null;
            }
            BindObjectiveTracker();

            if (_objectiveText != null) {
                _objectiveText.gameObject.SetActive(true);
                RefreshObjectiveText();
            }
        }

        /// <summary>One-time lookup: the MissionRunner above this HUD, its ObjectiveTracker and
        /// the objective the text shows: the first open destroyAllOfType on the target type,
        /// else (MissionRunner's no-destroy-all fallback) the first open objective on that
        /// type. The number is read from the tracker only for a destroyAllOfType objective.</summary>
        private void BindObjectiveTracker() {
            _objectiveTracker = null;
            _trackedObjectiveIndex = -1;
            _boundObjectives = null;
            _shownObjectiveIndex = -1;
            if (string.IsNullOrEmpty(_objectiveTargetType)) return;

            var runner = GetComponentInParent<MissionRunner>();
            ObjectiveEntry[] objectives = runner != null && runner.MissionData != null
                ? runner.MissionData.objectives : null;
            if (objectives == null) return;

            int index = FindOpenObjective(objectives, _objectiveTargetType, true);
            if (index < 0) index = FindOpenObjective(objectives, _objectiveTargetType, false);
            if (index < 0) return;
            _boundObjectives = objectives;
            _shownObjectiveIndex = index;

            var trackers = runner.GetComponents<ObjectiveTracker>();
            for (int i = 0; i < trackers.Length; i++) {
                if (trackers[i] != null && trackers[i].enabled) { _objectiveTracker = trackers[i]; break; }
            }
            if (_objectiveTracker != null && objectives[index].type == "destroyAllOfType") {
                _trackedObjectiveIndex = index;
            }
        }

        /// <summary>First open (not evaluated) objective on `targetType` (case-insensitive);
        /// only destroyAllOfType ones when `destroyAllOnly`. -1 when none.</summary>
        private static int FindOpenObjective(ObjectiveEntry[] objectives, string targetType, bool destroyAllOnly) {
            for (int i = 0; i < objectives.Length; i++) {
                var obj = objectives[i];
                if (obj == null || obj.evaluated) continue;
                if (destroyAllOnly && obj.type != "destroyAllOfType") continue;
                if (string.Equals(obj.targetType, targetType, System.StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>Writes "Destroy N more &lt;type plural&gt;" from the tracker (or the
        /// fallback counter), or the fixed description of a non-destroyAll objective the HUD
        /// moved on to. Pencil while open, full ink at 0.</summary>
        private void RefreshObjectiveText() {
            if (_shownDescription != null) {
                if (_objectiveText == null) return;
                _objectiveText.text = _shownDescription;
                _objectiveText.color = PencilCol;
                return;
            }
            if (ObjectiveFromTracker) {
                _objectiveRemaining = _objectiveTracker.RemainingKills(_trackedObjectiveIndex);
            }
            if (_objectiveText == null) return;
            _objectiveText.text =
                $"Destroy {_objectiveRemaining} more {ObjectiveTracker.ReadableType(_objectiveTargetType, true)}";
            _objectiveText.color = _objectiveRemaining == 0 ? InkCol : PencilCol;
        }

        /// <summary>
        /// GameEvents.ObjectiveCompleted (B054). Reacts only when the objective it shows is the
        /// one that completed: another objective completing (M4: the base bombed while balloons
        /// remain) keeps the current counter. When the shown one completes, the HUD moves on to
        /// the next open objective (its counter, or its description), and shows "Objective
        /// Complete!" only when none is left. A HUD not bound to a mission objective (standalone)
        /// keeps the old rule: any completion shows the message.
        /// </summary>
        private void OnObjectiveCompleted(string objectiveId) {
            if (!_objectiveActive) return;

            if (_boundObjectives != null && _shownObjectiveIndex >= 0
                    && _shownObjectiveIndex < _boundObjectives.Length) {
                var shown = _boundObjectives[_shownObjectiveIndex];
                // Not ours, and ours is still open: keep showing it.
                if (shown != null && !shown.evaluated && !IsObjectiveKey(shown, objectiveId)) return;
                if (ShowNextOpenObjective()) return;
            }

            if (_objectiveText != null) {
                _objectiveText.text = "Objective Complete!";
                _objectiveText.color = InkCol;   // done: pencil turns full ink
                if (isActiveAndEnabled) _hideObjectiveRoutine = StartCoroutine(HideObjectiveAfterDelay(2f));
            }
            _objectiveActive = false;
        }

        /// <summary>True when `objectiveId` (an ObjectiveCompleted payload) names `obj`: its id,
        /// or for an id-less entry an empty payload or its type (MissionRunner's key).</summary>
        private static bool IsObjectiveKey(ObjectiveEntry obj, string objectiveId) {
            if (string.IsNullOrEmpty(obj.id)) {
                return string.IsNullOrEmpty(objectiveId) || objectiveId == obj.type;
            }
            return objectiveId == obj.id;
        }

        /// <summary>Shows the first open objective in mission order: a destroyAllOfType one as
        /// the tracker-driven counter, any other as its ObjectiveTracker.GetDescription text.
        /// Needs the tracker; false when there is none or nothing is open.</summary>
        private bool ShowNextOpenObjective() {
            if (_objectiveTracker == null || _boundObjectives == null) return false;
            for (int i = 0; i < _boundObjectives.Length; i++) {
                var obj = _boundObjectives[i];
                if (obj == null || obj.evaluated) continue;

                _shownObjectiveIndex = i;
                _objectiveTargetType = obj.targetType ?? "";
                _objectiveRefreshPending = false;
                if (obj.type == "destroyAllOfType") {
                    _trackedObjectiveIndex = i;
                    _shownDescription = null;
                } else {
                    _trackedObjectiveIndex = -1;
                    _shownDescription = _objectiveTracker.GetDescription(i);
                }
                RefreshObjectiveText();
                return true;
            }
            return false;
        }

        /// <summary>Hides the objective text after a delay (UI_SPEC §4 flash feedback).</summary>
        private System.Collections.IEnumerator HideObjectiveAfterDelay(float delay) {
            yield return new WaitForSeconds(delay);
            _hideObjectiveRoutine = null;
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

        /// <summary>
        /// A shared diagonal-hatch sprite the size of the bar (one texel per UI pixel, so the
        /// Filled image crops it without stretching the stripes). White stripes on faint
        /// white, tinted ink by Image.color. Rebuilt only if the bar size changes.
        /// </summary>
        private static Sprite HatchSprite(float width, float height) {
            int w = Mathf.Max(1, Mathf.RoundToInt(width));
            int h = Mathf.Max(1, Mathf.RoundToInt(height));
            if (_hatchSprite != null && _hatchSprite.texture != null
                && _hatchSprite.texture.width == w && _hatchSprite.texture.height == h) {
                return _hatchSprite;
            }
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var stripe = Color.white;
            var gap = new Color(1f, 1f, 1f, HatchGapAlpha);
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    pixels[y * w + x] = ((x + y) % HatchPeriod) < HatchStripe ? stripe : gap;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            _hatchSprite = Sprite.Create(texture, new Rect(0, 0, w, h), Vector2.one * 0.5f);
            return _hatchSprite;
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
