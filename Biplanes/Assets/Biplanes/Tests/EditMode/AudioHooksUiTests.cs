using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using Biplanes.Audio;
using Biplanes.UI;

namespace Biplanes.Tests {

    /// <summary>
    /// T952: UI sound hooks. The briefing typewriter schedule is pure math; the code-built
    /// views must still build and hide when no AudioManager exists (EditMode).
    /// </summary>
    public class AudioHooksUiTests {

        private bool _hadEventSystem;

        [SetUp]
        public void SetUp() {
            _hadEventSystem = Object.FindFirstObjectByType<EventSystem>() != null;
        }

        [TearDown]
        public void TearDown() {
            PauseView.Hide();
            ResultView.HideAll();
            // The views create an EventSystem when the scene has none; remove ours.
            if (!_hadEventSystem) {
                var es = Object.FindFirstObjectByType<EventSystem>();
                if (es != null) Object.DestroyImmediate(es.gameObject);
            }
        }

        // ── Briefing typewriter schedule ────────────────────────────────

        [Test]
        public void Typewriter_NoClackBeforeStartDelay() {
            Assert.AreEqual(0, BriefingView.TypewriterClacksDue(0f));
            Assert.AreEqual(0, BriefingView.TypewriterClacksDue(BriefingView.TypewriterStartDelay - 0.001f));
        }

        [Test]
        public void Typewriter_FirstClackAtStartDelay_ThenOnePerInterval() {
            float t0 = BriefingView.TypewriterStartDelay;
            float dt = BriefingView.TypewriterInterval;
            Assert.AreEqual(1, BriefingView.TypewriterClacksDue(t0));
            Assert.AreEqual(1, BriefingView.TypewriterClacksDue(t0 + dt * 0.5f));
            Assert.AreEqual(2, BriefingView.TypewriterClacksDue(t0 + dt * 1.01f));
            Assert.AreEqual(3, BriefingView.TypewriterClacksDue(t0 + dt * 2.01f));
        }

        [Test]
        public void Typewriter_CapsAtClackCount_WithinBriefSpec() {
            Assert.That(BriefingView.TypewriterClacks, Is.InRange(4, 6));
            Assert.That(BriefingView.TypewriterInterval, Is.InRange(0.05f, 0.07f));
            Assert.AreEqual(BriefingView.TypewriterClacks, BriefingView.TypewriterClacksDue(10f));
            Assert.AreEqual(BriefingView.TypewriterClacks, BriefingView.TypewriterClacksDue(1000f));
        }

        // ── Views without an AudioManager ───────────────────────────────

        [Test]
        public void PauseView_BuildsAndHides_WithoutAudioManager() {
            Assume.That(AudioManager.GetOrNull(), Is.Null);
            var view = PauseView.Show();
            Assert.IsNotNull(view);
            Assert.IsTrue(PauseView.IsShowing);
            PauseView.Hide();
            Assert.IsFalse(PauseView.IsShowing);
        }

        [Test]
        public void ResultView_BuildsAndHides_WithoutAudioManager() {
            Assume.That(AudioManager.GetOrNull(), Is.Null);
            var view = ResultView.Create();
            Assert.IsNotNull(view);
            Assert.IsTrue(ResultView.IsShowing);
            ResultView.HideAll();
            Assert.IsFalse(ResultView.IsShowing);
        }
    }
}
