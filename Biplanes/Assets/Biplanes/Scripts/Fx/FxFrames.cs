using UnityEngine;

namespace Biplanes.Fx {

    /// <summary>
    /// Pure frame-animation math for FxManager (B030). No scene or Resources access, so it is
    /// EditMode-testable. Times are in seconds; an animation of N frames at S seconds per frame
    /// lasts N * S and shows frame floor(t / S), clamped to the last frame.
    /// </summary>
    public static class FxFrames {

        /// <summary>Total length of an animation; 0 when there are no frames or no frame time.</summary>
        public static float Duration(int frameCount, float secondsPerFrame) {
            if (frameCount <= 0 || secondsPerFrame <= 0f) return 0f;
            return frameCount * secondsPerFrame;
        }

        /// <summary>Frame to show after `elapsed` seconds: 0 at the start, the last frame from
        /// (frameCount - 1) * secondsPerFrame onwards. -1 when there are no frames.</summary>
        public static int FrameIndex(float elapsed, int frameCount, float secondsPerFrame) {
            if (frameCount <= 0) return -1;
            if (secondsPerFrame <= 0f) return frameCount - 1;
            if (elapsed <= 0f) return 0;
            // A tiny epsilon keeps exact frame boundaries (0.16 / 0.08) from rounding down a frame.
            int index = Mathf.FloorToInt(elapsed / secondsPerFrame + 1e-4f);
            return Mathf.Clamp(index, 0, frameCount - 1);
        }

        /// <summary>True once `elapsed` has reached `duration`.</summary>
        public static bool IsDone(float elapsed, float duration) {
            return elapsed >= duration;
        }

        /// <summary>Progress 0..1 through `duration` (1 when the duration is 0).</summary>
        public static float Progress(float elapsed, float duration) {
            if (duration <= 0f) return 1f;
            return Mathf.Clamp01(elapsed / duration);
        }

        /// <summary>Alpha that stays 1 until the last `fadeSeconds` of `duration`, then falls
        /// linearly to 0 at the end. fadeSeconds >= duration fades over the whole life.</summary>
        public static float FadeOutAlpha(float elapsed, float duration, float fadeSeconds) {
            if (duration <= 0f) return 0f;
            if (elapsed >= duration) return 0f;
            if (fadeSeconds <= 0f) return 1f;
            float fade = Mathf.Min(fadeSeconds, duration);
            float start = duration - fade;
            if (elapsed <= start) return 1f;
            return Mathf.Clamp01(1f - (elapsed - start) / fade);
        }

        /// <summary>Scale lerped from `from` to `to` over `duration`.</summary>
        public static float Scale(float elapsed, float duration, float from, float to) {
            return Mathf.Lerp(from, to, Progress(elapsed, duration));
        }
    }
}
