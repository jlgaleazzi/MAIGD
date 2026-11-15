namespace Biplanes.Player {

    /// <summary>
    /// Pure input mapping used by <see cref="PlayerInput"/> (no Unity calls, so EditMode tests
    /// can drive it). Turns one frame of raw device state into an <see cref="InputState"/>:
    ///  - W / S / A / D pass straight through.
    ///  - Fire = left mouse button held, unless that hold is "spent" by a UI interaction.
    ///  - Drop bomb = Space pressed this frame (one-shot), unless that press is spent.
    ///
    /// A hold is spent (needs a release before it counts again) when:
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

        /// <summary>Forget the past: the next step treats any held button as spent.</summary>
        public void Reset() {
            _blockedLastStep = true;
            _fireNeedsRelease = true;
            _bombNeedsRelease = true;
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
                return default(InputState);
            }

            // A spent button frees itself once it is seen up, or when it went down this frame
            // after an unblocked step (it was released and pressed again in between).
            if (!raw.fireHeld || (raw.firePressedNow && !_blockedLastStep)) _fireNeedsRelease = false;
            if (!raw.bombHeld || (raw.bombPressedNow && !_blockedLastStep)) _bombNeedsRelease = false;

            // A click that lands on a UI element belongs to the UI, for the whole hold.
            if (raw.firePressedNow && pointerOverUi) _fireNeedsRelease = true;

            _blockedLastStep = false;

            var s = new InputState();
            s.up       = raw.up;
            s.down     = raw.down;
            s.left     = raw.left;
            s.right    = raw.right;
            s.fire     = raw.fireHeld && !_fireNeedsRelease;
            s.dropBomb = raw.bombPressedNow && !_bombNeedsRelease;
            return s;
        }
    }
}
