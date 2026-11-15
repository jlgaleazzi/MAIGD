using NUnit.Framework;
using Biplanes.Player;

namespace Biplanes.Tests {

    /// <summary>
    /// B026: EditMode tests for the control mapping (PlayerInputGate, the pure core of
    /// PlayerInput). Left mouse held = fire, Space pressed = one bomb, W/S/A/D pass through,
    /// Enter is no longer read at all. A button held across a UI screen, or a click that lands
    /// on UI, is spent until released.
    /// </summary>
    public class PlayerInputGateTests {

        private PlayerInputGate _gate;

        [SetUp]
        public void SetUp() {
            _gate = new PlayerInputGate();
            // Settle past the initial "just enabled" step with nothing held.
            _gate.Step(new PlayerInputGate.Raw(), false, false);
        }

        private static PlayerInputGate.Raw Mouse(bool held, bool pressedNow) {
            return new PlayerInputGate.Raw { fireHeld = held, firePressedNow = pressedNow };
        }

        private static PlayerInputGate.Raw Space(bool held, bool pressedNow) {
            return new PlayerInputGate.Raw { bombHeld = held, bombPressedNow = pressedNow };
        }

        // ── Mapping ─────────────────────────────────────────────────────

        [Test]
        public void Movement_PassesThrough() {
            var raw = new PlayerInputGate.Raw { up = true, left = true };
            var s = _gate.Step(raw, false, false);
            Assert.IsTrue(s.up);
            Assert.IsTrue(s.left);
            Assert.IsFalse(s.down);
            Assert.IsFalse(s.right);

            raw = new PlayerInputGate.Raw { down = true, right = true };
            s = _gate.Step(raw, false, false);
            Assert.IsTrue(s.down);
            Assert.IsTrue(s.right);
            Assert.IsFalse(s.up);
            Assert.IsFalse(s.left);
        }

        [Test]
        public void LeftMouseHeld_Fires_AndKeepsFiringWhileHeld() {
            Assert.IsTrue(_gate.Step(Mouse(true, true), false, false).fire, "press fires");
            Assert.IsTrue(_gate.Step(Mouse(true, false), false, false).fire, "hold keeps firing");
            Assert.IsFalse(_gate.Step(Mouse(false, false), false, false).fire, "release stops");
        }

        [Test]
        public void LeftMouse_NeverDropsABomb() {
            var s = _gate.Step(Mouse(true, true), false, false);
            Assert.IsFalse(s.dropBomb);
        }

        [Test]
        public void SpacePress_DropsExactlyOneBomb_AndNeverFires() {
            var s = _gate.Step(Space(true, true), false, false);
            Assert.IsTrue(s.dropBomb, "Space press drops a bomb");
            Assert.IsFalse(s.fire, "Space no longer fires");
            Assert.IsFalse(_gate.Step(Space(true, false), false, false).dropBomb, "holding Space does not repeat");
            _gate.Step(Space(false, false), false, false);
            Assert.IsTrue(_gate.Step(Space(true, true), false, false).dropBomb, "a second press drops again");
        }

        [Test]
        public void NoDevices_ReleasesEverything() {
            _gate.Step(Mouse(true, true), false, false);
            var s = _gate.Step(new PlayerInputGate.Raw(), false, false); // mouse gone → all false
            Assert.IsFalse(s.fire);
            Assert.IsFalse(s.dropBomb);
            Assert.IsFalse(s.up || s.down || s.left || s.right);
        }

        // ── UI guard ────────────────────────────────────────────────────

        [Test]
        public void WhileUiBlocking_NothingFiresOrDrops() {
            var raw = new PlayerInputGate.Raw {
                fireHeld = true, firePressedNow = true, bombHeld = true, bombPressedNow = true, up = true
            };
            var s = _gate.Step(raw, true, false);
            Assert.IsFalse(s.fire);
            Assert.IsFalse(s.dropBomb);
            Assert.IsFalse(s.up);
        }

        [Test]
        public void ClickOnUi_DoesNotFire_UntilReleasedAndPressedAgain() {
            Assert.IsFalse(_gate.Step(Mouse(true, true), false, true).fire, "press over UI");
            Assert.IsFalse(_gate.Step(Mouse(true, false), false, false).fire, "still the same hold, pointer moved off UI");
            Assert.IsFalse(_gate.Step(Mouse(false, false), false, false).fire);
            Assert.IsTrue(_gate.Step(Mouse(true, true), false, false).fire, "fresh press off UI fires");
        }

        [Test]
        public void HoldingFire_ThenPointerOverUi_KeepsFiring() {
            _gate.Step(Mouse(true, true), false, false);
            Assert.IsTrue(_gate.Step(Mouse(true, false), false, true).fire,
                "the UI check applies only at press time");
        }

        [Test]
        public void MouseHeldAcrossUiScreen_MustBeReleasedBeforeFiring() {
            _gate.Step(Mouse(true, true), true, false);              // click on a menu/pause button
            Assert.IsFalse(_gate.Step(Mouse(true, false), false, false).fire, "screen gone, still held");
            _gate.Step(Mouse(false, false), false, false);
            Assert.IsTrue(_gate.Step(Mouse(true, true), false, false).fire);
        }

        [Test]
        public void SpaceThatStartsTheBriefing_DoesNotDropABomb_EvenInTheSameFrame() {
            // Briefing up last frame; this frame BriefingView consumed Space and closed itself
            // before PlayerInput ran, so PlayerInput sees no UI but Space wasPressedThisFrame.
            _gate.Step(new PlayerInputGate.Raw(), true, false);
            Assert.IsFalse(_gate.Step(Space(true, true), false, false).dropBomb, "same-frame Space is spent");
            Assert.IsFalse(_gate.Step(Space(true, false), false, false).dropBomb);
            _gate.Step(Space(false, false), false, false);
            Assert.IsTrue(_gate.Step(Space(true, true), false, false).dropBomb, "next press drops");
        }

        [Test]
        public void SpaceThatStartsTheBriefing_PlayerInputRanFirst_DoesNotDropABomb() {
            // PlayerInput ran before BriefingView: it saw the briefing (blocked) with Space down.
            _gate.Step(Space(true, true), true, false);
            Assert.IsFalse(_gate.Step(Space(true, false), false, false).dropBomb);
        }

        [Test]
        public void Reset_SpendsButtonsAlreadyHeld() {
            _gate.Reset(); // PlayerInput.OnEnable: plane spawned or pause resumed
            var raw = new PlayerInputGate.Raw {
                fireHeld = true, firePressedNow = true, bombHeld = true, bombPressedNow = true
            };
            var s = _gate.Step(raw, false, false);
            Assert.IsFalse(s.fire);
            Assert.IsFalse(s.dropBomb);
        }
    }
}
