using NUnit.Framework;
using Biplanes.Fx;

namespace Biplanes.Tests {

    /// <summary>
    /// B030: pure frame-animation math behind FxManager's pencil FX (explosion 6 frames at
    /// 0.08 s, flak 4 frames at 0.07 s, smoke puff faded over its 1.2 s life).
    /// </summary>
    public class FxFramesTests {

        private const int ExplosionFrames = 6;
        private const float ExplosionFrameSeconds = 0.08f;

        [Test]
        public void Duration_IsFrameCountTimesFrameTime_ZeroWhenEmpty() {
            Assert.AreEqual(0.48f, FxFrames.Duration(ExplosionFrames, ExplosionFrameSeconds), 1e-5f);
            Assert.AreEqual(0.28f, FxFrames.Duration(4, 0.07f), 1e-5f);
            Assert.AreEqual(0f, FxFrames.Duration(0, ExplosionFrameSeconds));
            Assert.AreEqual(0f, FxFrames.Duration(ExplosionFrames, 0f));
        }

        [Test]
        public void FrameIndex_ZeroAtStart_StepsInOrder() {
            Assert.AreEqual(0, FxFrames.FrameIndex(0f, ExplosionFrames, ExplosionFrameSeconds));
            Assert.AreEqual(0, FxFrames.FrameIndex(-1f, ExplosionFrames, ExplosionFrameSeconds), "negative time clamps to 0");
            Assert.AreEqual(0, FxFrames.FrameIndex(0.079f, ExplosionFrames, ExplosionFrameSeconds));
            Assert.AreEqual(1, FxFrames.FrameIndex(0.08f, ExplosionFrames, ExplosionFrameSeconds), "exact boundary shows the next frame");
            Assert.AreEqual(2, FxFrames.FrameIndex(0.16f, ExplosionFrames, ExplosionFrameSeconds));
            Assert.AreEqual(3, FxFrames.FrameIndex(0.27f, ExplosionFrames, ExplosionFrameSeconds));
        }

        [Test]
        public void FrameIndex_LastFrameNearTheEnd_ClampedAfter() {
            float duration = FxFrames.Duration(ExplosionFrames, ExplosionFrameSeconds);
            Assert.AreEqual(ExplosionFrames - 1, FxFrames.FrameIndex(duration - 0.01f, ExplosionFrames, ExplosionFrameSeconds));
            Assert.AreEqual(ExplosionFrames - 1, FxFrames.FrameIndex(duration + 5f, ExplosionFrames, ExplosionFrameSeconds));
        }

        [Test]
        public void FrameIndex_FewerFramesLoaded_StaysInRange() {
            // A missing later frame: FxManager plays the 3 that exist.
            for (float t = 0f; t < 1f; t += 0.01f) {
                int i = FxFrames.FrameIndex(t, 3, ExplosionFrameSeconds);
                Assert.That(i, Is.InRange(0, 2));
            }
            Assert.AreEqual(0, FxFrames.FrameIndex(0.5f, 1, 1.2f), "a one-frame smoke puff always shows frame 0");
            Assert.AreEqual(-1, FxFrames.FrameIndex(0.1f, 0, ExplosionFrameSeconds), "no frames");
        }

        [Test]
        public void IsDone_OnlyAfterDuration() {
            float duration = FxFrames.Duration(ExplosionFrames, ExplosionFrameSeconds);
            Assert.IsFalse(FxFrames.IsDone(0f, duration));
            Assert.IsFalse(FxFrames.IsDone(duration - 0.01f, duration));
            Assert.IsTrue(FxFrames.IsDone(duration, duration));
            Assert.IsTrue(FxFrames.IsDone(duration + 0.1f, duration));
        }

        [Test]
        public void FadeOutAlpha_OneEarly_FadesOnlyAtTheEnd_ZeroAtEnd() {
            float duration = 0.48f;
            float fade = 0.12f;
            Assert.AreEqual(1f, FxFrames.FadeOutAlpha(0f, duration, fade));
            Assert.AreEqual(1f, FxFrames.FadeOutAlpha(0.3f, duration, fade), "full alpha before the last frames");
            Assert.AreEqual(1f, FxFrames.FadeOutAlpha(duration - fade, duration, fade), 1e-5f);
            Assert.AreEqual(0.5f, FxFrames.FadeOutAlpha(duration - fade * 0.5f, duration, fade), 1e-4f);
            Assert.Less(FxFrames.FadeOutAlpha(duration - 0.005f, duration, fade), 0.05f, "~0 near the end");
            Assert.AreEqual(0f, FxFrames.FadeOutAlpha(duration, duration, fade));
        }

        [Test]
        public void FadeOutAlpha_WholeLife_IsLinear() {
            // Smoke puff: fades over its whole 1.2 s life.
            Assert.AreEqual(1f, FxFrames.FadeOutAlpha(0f, 1.2f, 1.2f), 1e-5f);
            Assert.AreEqual(0.5f, FxFrames.FadeOutAlpha(0.6f, 1.2f, 1.2f), 1e-4f);
            Assert.AreEqual(0f, FxFrames.FadeOutAlpha(1.2f, 1.2f, 1.2f));
        }

        [Test]
        public void Scale_LerpsFromTo_AndClamps() {
            Assert.AreEqual(1.0f, FxFrames.Scale(0f, 1.2f, 1.0f, 1.6f), 1e-5f);
            Assert.AreEqual(1.3f, FxFrames.Scale(0.6f, 1.2f, 1.0f, 1.6f), 1e-4f);
            Assert.AreEqual(1.6f, FxFrames.Scale(5f, 1.2f, 1.0f, 1.6f), 1e-5f);
            Assert.AreEqual(1.6f, FxFrames.Scale(0f, 0f, 1.0f, 1.6f), 1e-5f, "zero duration is already at the end");
        }
    }
}
