using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Biplanes.Core;
using Biplanes.UI;

namespace Biplanes.Player {

    /// <summary>Snapshot of every player action for one physics tick.</summary>
    [Serializable]
    public struct InputState {
        public bool up;
        public bool down;
        public bool left;
        public bool right;
        public bool fire;     // left mouse button held this frame (autofire gated inside PlayerInput)
        public bool dropBomb; // Space pressed this frame (one-shot)
    }

    /// <summary>Reads the keyboard and mouse through the Input System package (ARCHITECTURE §3:
    /// Keyboard.current / Mouse.current, no .inputactions asset) and exposes typed events for
    /// gameplay.
    /// W / A / S / D     → movement direction (OnMove, raised every FixedUpdate)
    /// Left mouse button → hold to autofire at GameBalance.player.fireRate shots/s (OnFire)
    /// Space             → drop one bomb per press, not hold (OnDropBomb)
    ///
    /// UI guard (PlayerInputGate): nothing fires or drops while a menu, briefing, pause or
    /// result screen is showing (or the world is frozen at timeScale 0), a mouse press that
    /// lands on a UI element never fires, and a button still held from a UI interaction (the
    /// click or Space that closed a screen) must be released before it counts again.
    /// </summary>
    public class PlayerInput : MonoBehaviour {

        // ── Public events — PlayerPlane subscribes here ───────────────

        public static event Action<InputState> OnMove;
        public static event Action OnFire;
        public static event Action OnDropBomb;

        [SerializeField] private float _fireRateOverride = -1f; // -1 → read from Balance.json

        private readonly PlayerInputGate _gate = new PlayerInputGate();
        private float _fireRate;
        private float _fireTimer;      // seconds accumulated toward the next autofire shot
        private bool  _wasFireDown;    // gated fire state from last Update
        private InputState _state;     // latest input snapshot, consumed in FixedUpdate

        // ── Lifecycle ───────────────────────────────────────────────

        private void Awake() {
            var g = GameBalance.Get();
            _fireRate = (_fireRateOverride > 0f) ? _fireRateOverride : (g != null ? g.player.fireRate : 8f);
            if (_fireRate <= 0f) _fireRate = 8f;
            _fireTimer = 0f;
        }

        /// <summary>Fresh start (spawn, or resume from pause): any button already held is spent.</summary>
        private void OnEnable() {
            _gate.Reset();
            _state = default(InputState);
            _wasFireDown = false;
            _fireTimer = 0f;
        }

        // ── Input polling (Update) ─────────────────────────────────

        private void Update() {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            // No keyboard or no mouse (e.g. WebGL page not focused yet): that device's
            // actions read as released.
            var raw = new PlayerInputGate.Raw();
            if (kb != null) {
                raw.left           = kb.aKey.isPressed;
                raw.right          = kb.dKey.isPressed;
                raw.up             = kb.wKey.isPressed;
                raw.down           = kb.sKey.isPressed;
                raw.bombHeld       = kb.spaceKey.isPressed;
                raw.bombPressedNow = kb.spaceKey.wasPressedThisFrame;
            }
            if (mouse != null) {
                raw.fireHeld       = mouse.leftButton.isPressed;
                raw.firePressedNow = mouse.leftButton.wasPressedThisFrame;
            }

            bool pointerOverUi = raw.firePressedNow && IsPointerOverUi();
            _state = _gate.Step(raw, IsUiBlocking(), pointerOverUi);

            // Bomb is one-shot per key press, so raise it from Update where
            // wasPressedThisFrame is reliable.
            if (_state.dropBomb) OnDropBomb?.Invoke();

            // A fresh fire hold shoots immediately on the next physics tick.
            if (_state.fire && !_wasFireDown) _fireTimer = 1f / _fireRate;
            _wasFireDown = _state.fire;
        }

        /// <summary>A menu, briefing, pause or result screen is up, or the world is frozen.</summary>
        private static bool IsUiBlocking() {
            return MainMenuView.IsShowing
                || BriefingView.IsShowing
                || PauseView.IsShowing
                || ResultView.IsShowing
                || Time.timeScale <= 0f;
        }

        private static bool IsPointerOverUi() {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        // ── Movement + autofire (FixedUpdate) ──────────────────────

        private void FixedUpdate() {
            OnMove?.Invoke(_state);

            if (_wasFireDown) {
                float interval = 1f / _fireRate;
                _fireTimer += Time.fixedDeltaTime;
                while (_fireTimer >= interval) {
                    _fireTimer -= interval;
                    OnFire?.Invoke();
                }
            } else {
                _fireTimer = 0f;
            }
        }

        public bool IsFiring => _wasFireDown;
    }
}
