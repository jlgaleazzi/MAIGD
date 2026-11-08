using UnityEngine;
using UnityEngine.InputSystem; // Keyboard.current (project uses Input System only)
using UnityEngine.UI;
using Biplanes.Core;
using Biplanes.Missions;

namespace Biplanes.UI {

    /// <summary>
    /// Briefing screen view shown between mission selection and gameplay.
    /// Displays the current mission's title and briefing text, then auto-transitions
    /// to Playing after ~5 seconds or when the player presses Start / Enter.
    ///
    /// Canvas background fades in over 0.5s (UI_SPEC §3.2).
    /// Built entirely from code — no prefabs (ARCHITECTURE §0 rule 1).
    /// </summary>
    public class BriefingView : MonoBehaviour {

        [SerializeField] private int _currentMission = 1;       // mission number (1-5)
        [SerializeField] private float _autoDelay = 5f;        // seconds before auto-transition
        [SerializeField] private float _fadeInDuration = 0.5f; // canvas fade-in duration

        /// <summary>Set by the caller (GameManager) before OnEnable to pick which mission.</summary>
        public int currentMission { get => _currentMission; set => _currentMission = Mathf.Clamp(value, 1, 5); }

        private MissionData _mission;
        private Image _canvasImage;   // canvas-level image for Color.a fade
        private Text _titleText;
        private Text _briefingText;
        private Button _beginButton;
        private bool _isReady = false; // true once fade-in completes or is skipped
        private int _transitionedCount = 0;

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Awake() {
            // Create a full-canvas background for Color.a fade-in.
            var bgObj = new GameObject("CanvasBackground");
            bgObj.transform.SetParent(transform, false);
            var bgRect = bgObj.GetComponent<RectTransform>() ?? bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector3.zero;
            bgRect.offsetMax = Vector3.zero;

            _canvasImage = bgObj.AddComponent<Image>();
            // Ink-colour panel tint per UI_SPEC §2 (paper tone).
            _canvasImage.color = new Color(0.059f, 0.165f, 0.29f, 0f); // #0F2A4A, a=0 initially
            bgRect.SetAsFirstSibling(); // behind everything

            BuildVisuals();
        }

        private void OnEnable() {
            LoadMissionData(_currentMission);
            // Start the fade-in and auto-timer.
            if (_canvasImage != null) {
                StartCoroutine(FadeInCoroutine());
            }
            _isReady = false;
            _transitionedCount = 0;
        }

        private void Update() {
            if (!_isReady) return;

            // Allow player to skip the briefing with Enter or Space.
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) {
                TriggerTransition();
            }
        }

        private void OnDestroy() {
            StopAllCoroutines();
        }

        // ── Visual construction (no prefabs) ───────────────────────────────

        /// <summary>
        /// Build the briefing panel hierarchy: title text, briefing text, begin button.
        /// Anchored to the canvas center per UI_SPEC §3.2.
        /// </summary>
        private void BuildVisuals() {
            // Container panel (paper bg via background sprite).
            var panel = new GameObject("BriefingPanel");
            panel.transform.SetParent(transform, false);

            var rect = panel.GetComponent<RectTransform>() ?? panel.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector3.zero;
            rect.offsetMax = Vector3.zero;

            var panelImg = panel.AddComponent<Image>();
            // Try loading a background sprite; fall back to white if missing.
            var bgSprite = Resources.Load<Sprite>("Sprites/ui/ui_panel");
            if (bgSprite != null) {
                panelImg.sprite = bgSprite;
            } else {
                Debug.LogWarning("BriefingView: Missing sprite 'Sprites/ui/ui_panel', using 1×1 white fallback.");
                panelImg.sprite = CreateWhiteSprite();
            }
            panelImg.color = Color.white;
            panelImg.raycastTarget = false;

            // Title label.
            _titleText = CreateLabel("Title", "MISSION 1: TITLE", new Vector3(0f, 60f, 0f), 28);

            // Briefing text block.
            _briefingText = CreateLabel("BriefingText", "", new Vector3(0f, -10f, 0f), 20);
            _briefingText.lineSpacing = 1.4f;

            // Begin Mission button — build it once with its listener registered.
            var btnObj = new GameObject("BeginButton");
            btnObj.transform.SetParent(transform, false);
            var btnRect = btnObj.GetComponent<RectTransform>() ?? btnObj.AddComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.5f, 0.5f);
            btnRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnRect.pivot = new Vector2(0.5f, 0.5f);
            btnRect.offsetMin = new Vector3(-100f, -100f);
            btnRect.offsetMax = new Vector3(100f, -60f);

            var btnImg = btnObj.AddComponent<Image>();
            var btnSprite = Resources.Load<Sprite>("Sprites/ui/ui_button");
            if (btnSprite != null) {
                btnImg.sprite = btnSprite;
            } else {
                Debug.LogWarning("BriefingView: Missing sprite 'Sprites/ui/ui_button', using 1×1 white fallback.");
                btnImg.sprite = CreateWhiteSprite();
            }

            var btnTextObj = new GameObject("ButtonText");
            btnTextObj.transform.SetParent(btnObj.transform, false);
            var txtRect = btnTextObj.GetComponent<RectTransform>() ?? btnTextObj.AddComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector3(8f, 4f);
            txtRect.offsetMax = new Vector3(-8f, -4f);

            _beginButton = CreateButton(btnObj, btnImg, "Begin Mission", btnTextObj, txtRect);

            // Centre the button vertically below briefing text.
            rect.SetAsLastSibling(); // panel on bottom
        }

        private Text CreateLabel(string name, string initialText, Vector3 localPos, int fontSize) {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);

            var txtRect = obj.GetComponent<RectTransform>() ?? obj.AddComponent<RectTransform>();
            txtRect.anchorMin = new Vector2(0.5f, 0.5f);
            txtRect.anchorMax = new Vector2(0.5f, 0.5f);
            txtRect.pivot = new Vector2(0.5f, 0.5f);
            txtRect.localPosition = localPos;
            txtRect.sizeDelta = new Vector2(600f, 40f);

            var text = obj.AddComponent<Text>();
            text.text = initialText;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = fontSize;
            // Ink colour per UI_SPEC §2.
            text.color = new Color(0.91f, 0.94f, 0.98f, 1f); // #E8F1FA

            return text;
        }

        private Button CreateButton(GameObject buttonObj, Image buttonImg, string buttonText, GameObject textObj, RectTransform textRect) {
            var txt = textObj.AddComponent<Text>();
            txt.text = buttonText;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.fontSize = 20;
            txt.color = new Color(0.91f, 0.94f, 0.98f, 1f);

            textRect.offsetMin = Vector3.zero;
            textRect.offsetMax = Vector3.zero;

            var btn = buttonObj.GetComponent<Button>() ?? buttonObj.AddComponent<Button>();
            btn.onClick.AddListener(TriggerTransition);
            // Raycast on the button image.
            buttonImg.raycastTarget = true;

            return btn;
        }

        /// <summary>Creates a 1×1 white sprite for fallback when a resource is missing.</summary>
        private static Sprite CreateWhiteSprite() {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
        }

        // ── Data loading ────────────────────────────────────────────────────

        /// <summary>Load the mission JSON from Resources/Missions/mission_0N.json.</summary>
        private void LoadMissionData(int missionNumber) {
            var path = $"Missions/mission_{missionNumber:D2}";
            var textAsset = Resources.Load<TextAsset>(path);
            if (textAsset == null) {
                Debug.LogWarning($"BriefingView: Mission data not found at '{path}'.");
                _mission = new MissionData();
                _mission.id = missionNumber;
                _mission.title = $"Mission {missionNumber}";
                _mission.briefing = "No briefing text available.";
                return;
            }

            var data = JsonUtility.FromJson<MissionData>(textAsset.text);
            if (data == null) {
                Debug.LogWarning($"BriefingView: Failed to parse mission JSON at '{path}'.");
                data = new MissionData();
                data.id = missionNumber;
                data.title = $"Mission {missionNumber}";
                data.briefing = "No briefing text available.";
            }

            _mission = data;
        }

        // ── Coroutine: fade-in ─────────────────────────────────────────────

        /// <summary>Lerp the canvas background Color.a from 0 → target over fadeInDuration.</summary>
        private System.Collections.IEnumerator FadeInCoroutine() {
            float elapsed = 0f;
            while (elapsed < _fadeInDuration) {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _fadeInDuration);
                // Lerp color.a as the spec requires.
                var c = _canvasImage.color;
                c.a = Mathf.Lerp(0f, 1f, t);
                _canvasImage.color = c;
                yield return null;
            }

            if (_canvasImage != null) {
                var finalColor = _canvasImage.color;
                finalColor.a = 1f;
                _canvasImage.color = finalColor;
            }

            // Briefing is ready — player can press Enter/Space or wait.
            _isReady = true;

            // Update label texts now that we have mission data.
            if (_titleText != null) {
                _titleText.text = $"Mission {_currentMission:D2}: {_mission?.title ?? ""}";
            }
            if (_briefingText != null) {
                _briefingText.text = _mission?.briefing ?? "";
            }

            // Start auto-transition timer.
            StartCoroutine(AutoTransitionCoroutine());
        }

        // ── Coroutine: auto-transition ─────────────────────────────────────

        /// <summary>Wait for the auto-delay, then transition to gameplay.</summary>
        private System.Collections.IEnumerator AutoTransitionCoroutine() {
            yield return new WaitForSeconds(_autoDelay);
            TriggerTransition();
        }

        // ── Transition ──────────────────────────────────────────────────────

        /// <summary>Signal that the briefing is done; raise MissionEnded or transition via GameEvents.</summary>
        public void TriggerTransition() {
            if (_transitionedCount > 0) return;
            _transitionedCount++;

            // Ensure mission text is up to date.
            if (_titleText != null && _mission != null) {
                _titleText.text = $"Mission {_currentMission:D2}: {_mission.title}";
            }
            if (_briefingText != null && _mission != null) {
                _briefingText.text = _mission.briefing;
            }

            // Notify systems that the player has begun this mission.
            GameEvents.RaiseObjectiveCompleted($"mission_{_currentMission}_begun");

            Debug.LogFormat("[BriefingView] Starting mission {_currentMission}: {0}", _mission?.title);
        }
    }
}
