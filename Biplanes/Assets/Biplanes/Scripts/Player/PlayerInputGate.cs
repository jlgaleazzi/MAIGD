namespace Biplanes.Player {

    /// <summary>
    /// Pure input mapping used by <see cref="PlayerInput"/> (no Unity calls, so EditMode tests
    /// can drive it). Turns one frame of raw device state into an <see cref="InputState"/>:
    ///  - W / S / A / D pass through, unless that hold is spent (B049: a key held through the
    ///    briefing, Begin, pause or result screen counts only after it is released, so it can't
    ///    start the mission on its own; ARCHITECTURE §3 "Mission start gate").
    ///  - Fire = left mouse button held, unless that hold is "spent" by a UI interaction.
    ///  - Drop bomb = Space pressed this frame (one-shot), unless that press is spent.
    ///
    /// A hold (any of the six actions) is spent (needs a release before it counts again) when:
    ///  - a UI screen was blocking (menu, briefing, pause, result) on this or the previous
    ///    step, and the button is still down — so the click or Space that dismissed the
    ///    screen never reaches the plane, even if the screen closed earlier in the same frame;
    ///  - the mouse button went down this frame while the pointer was over a UI element.
    /// Every new gate, and every <see cref="Reset"/> (PlayerInput.OnEnable), starts as if the
    /// previous step was blocked.
    /// </summary>
    public sealed class PlayerInputGate {

        /// <summary>Raw device state for one frame.</summary>
        public struct Raw {
            public bool up;
            public bool down;
            public bool left;
            public bool right;
            public bool fireHeld;          // left mouse button isPressed
            public bool firePressedNow;    // left mouse button wasPressedThisFrame
            public bool bombHeld;          // Space isPressed
            public bool bombPressedNow;    // Space wasPressedThisFrame
        }

        private bool _blockedLastStep = true;
        private bool _fireNeedsRelease = true;
        private bool _bombNeedsRelease = true;
        private bool _upNeedsRelease = true;
        private bool _downNeedsRelease = true;
        private bool _leftNeedsRelease = true;
        private bool _rightNeedsRelease = true;

        /// <summary>Forget the past: the next step treats any held button as spent.</summary>
        public void Reset() {
            _blockedLastStep = true;
            SpendAll();
        }

        /// <summary>Every button and key counts as held-through (state unknown after a reset).</summary>
        private void SpendAll() {
            _fireNeedsRelease = true;
            _bombNeedsRelease = true;
            _upNeedsRelease = true;
            _downNeedsRelease = true;
            _leftNeedsRelease = true;
            _rightNeedsRelease = true;
        }

        /// <summary>True when `s` holds any player action (move, fire or bomb). Every action the
        /// gate lets through is a real press, so the first such state starts a mission (B049).</summary>
        public static bool AnyAction(InputState s) {
            return s.up || s.down || s.left || s.right || s.fire || s.dropBomb;
        }

        /// <summary>
        /// Map one frame. `uiBlocking` = a menu, briefing, pause or result screen is up (or the
        /// world is frozen). `pointerOverUi` = the pointer is over a UI element this frame; it
        /// only matters on the frame the mouse button goes down.
        /// </summary>
        public InputState Step(Raw raw, bool uiBlocking, bool pointerOverUi) {
            if (uiBlocking) {
                _blockedLastStep = true;
                _fireNeedsRelease = true;
                _bombNeedsRelease = true;
                // A key is spent only if it is down while the screen blocks; a key pressed on the
                // first frame after the screen closes counts at once.
                _upNeedsRelease = raw.up;
                _downNeedsRelease = raw.down;
                _leftNeedsRelease = raw.left;
                _rightNeedsRelease = raw.right;
                return default(InputState);
            }

            // A spent button frees itself once it is seen up, or when it went down this frame
            // after an unblocked step (it was released and pressed again in between).
            if (!raw.fireHeld || (raw.firePressedNow && !_blockedLastStep)) _fireNeedsRelease = false;
            if (!raw.bombHeld || (raw.bombPressedNow && !_blockedLastStep)) _bombNeedsRelease = false;
            // Keys have no "pressed this frame" here: a spent key frees itself on release.
            if (!raw.up) _upNeedsRelease = false;
            if (!raw.down) _downNeedsRelease = false;
            if (!raw.left) _leftNeedsRelease = false;
            if (!raw.right) _rightNeedsRelease = false;

            // A click that lands on a UI element belongs to the UI, for the whole hold.
            if (raw.firePressedNow && pointerOverUi) _fireNeedsRelease = true;

            _blockedLastStep = false;

            var s = new InputState();
            s.up       = raw.up && !_upNeedsRelease;
            s.down     = raw.down && !_downNeedsRelease;
            s.left     = raw.left && !_leftNeedsRelease;
            s.right    = raw.right && !_rightNeedsRelease;
            s.fire     = raw.fireHeld && !_fireNeedsRelease;
            s.dropBomb = raw.bombPressedNow && !_bombNeedsRelease;
            return s;
        }
    }
}
