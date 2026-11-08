using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Biplanes.Core;

namespace Biplanes.Player {

    /// <summary>Snapshot of every keyboard action for one physics tick.</summary>
    [Serializable]
    public struct InputState {
        public bool up;
        public bool down;
        public bool left;
        public bool right;
        public bool fire;     // Space held this frame (autofire gated inside PlayerInput)
        public bool dropBomb; // Enter pressed this frame (one-shot)
    }

    /// <summary>Reads the keyboard through the Input System package (ARCHITECTURE §3:
    /// Keyboard.current, no .inputactions asset) and exposes typed events for gameplay.
    /// W / A / S / D → movement direction (OnMove, raised every FixedUpdate)
    /// Space         → hold to autofire at GameBalance.player.fireRate shots/s (OnFire)
    /// Enter         → drop one bomb per press, not hold (OnDropBomb)
    /// </summary>
    public class PlayerInput : MonoBehaviour {

        // ── Public events — PlayerPlane subscribes here ───────────────

        public static event Action<InputState> OnMove;
        public static event Action OnFire;
        public static event Action OnDropBomb;

        [SerializeField] private float _fireRateOverride = -1f; // -1 → read from Balance.json

        private float _fireRate;
        private float _fireTimer;      // seconds accumulated toward the next autofire shot
        private bool  _wasSpaceDown;   // Space state from last Update
        private InputState _state;     // latest keyboard snapshot, consumed in FixedUpdate

        // ── Lifecycle ───────────────────────────────────────────────

        private void Awake() {
            var g = GameBalance.Get();
            _fireRate = (_fireRateOverride > 0f) ? _fireRateOverride : (g != null ? g.player.fireRate : 8f);
            if (_fireRate <= 0f) _fireRate = 8f;
            _fireTimer = 0f;
        }

        // ── Input polling (Update) ─────────────────────────────────

        private void Update() {
            var kb = Keyboard.current;
            if (kb == null) {
                // No keyboard (e.g. WebGL page not focused yet): release everything.
                _state = default(InputState);
                _wasSpaceDown = false;
                return;
            }

            _state.left  = kb.aKey.isPressed;
            _state.right = kb.dKey.isPressed;
            _state.up    = kb.wKey.isPressed;
            _state.down  = kb.sKey.isPressed;
            _state.fire  = kb.spaceKey.isPressed;
            _state.dropBomb = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;

            // Bomb is one-shot per key press, so raise it from Update where
            // wasPressedThisFrame is reliable.
            if (_state.dropBomb) OnDropBomb?.Invoke();

            // A fresh Space press fires immediately on the next physics tick.
            if (_state.fire && !_wasSpaceDown) _fireTimer = 1f / _fireRate;
            _wasSpaceDown = _state.fire;
        }

        // ── Movement + autofire (FixedUpdate) ──────────────────────

        private void FixedUpdate() {
            OnMove?.Invoke(_state);

            if (_wasSpaceDown) {
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

        public bool IsFiring => _wasSpaceDown;
    }
}
