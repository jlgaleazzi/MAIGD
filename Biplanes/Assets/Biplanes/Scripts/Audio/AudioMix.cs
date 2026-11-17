using Biplanes.Core;
using UnityEngine;

namespace Biplanes.Audio {

    /// <summary>
    /// Pure mixing math for AudioManager (ARCHITECTURE §3 Audio). No Unity objects are created
    /// here and nothing is random: callers pass in their random numbers, so every function is
    /// EditMode-testable and deterministic.
    ///  • ClipName(id): the Resources/Audio/sfx file name of a clip (snake_case).
    ///  • Priority(id): voice-stealing rank. UI and warnings outrank effects, effects outrank
    ///    gun rattles.
    ///  • Pan(screenX01): -0.8 at the left screen edge .. +0.8 at the right, clamped.
    ///  • Jitter / VolumeScale: pitch 1 +/- amount, volume 1 - (0..amount).
    ///  • OffscreenFactor: 1 inside the camera view, offscreenVolume outside.
    ///  • EnginePitch(verticalVelocity, damagePercent): 1.0 level, up to 1.15 climbing,
    ///    down to 0.9 diving, a little lower as damage grows. The irregular sputter at the
    ///    smoke threshold is the caller's job.
    ///  • VoiceToSteal: the oldest voice of the lowest priority, or -1 when every voice
    ///    outranks the new cue.
    /// </summary>
    public static class AudioMix {

        /// <summary>Resources folder of the clips (append the clip name).</summary>
        public const string ClipFolder = "Audio/sfx/";

        /// <summary>Largest stereo pan, at the screen edges (not hard-panned).</summary>
        public const float MaxPan = 0.8f;

        /// <summary>Engine pitch at full climb.</summary>
        public const float EngineClimbPitch = 1.15f;
        /// <summary>Engine pitch at full dive.</summary>
        public const float EngineDivePitch = 0.9f;
        /// <summary>Steady pitch drop at 100% damage (the engine labours).</summary>
        public const float EngineDamageDrop = 0.04f;

        /// <summary>Gun rattles: the first voices to be stolen.</summary>
        public const int PriorityRattle = 0;
        /// <summary>Ordinary gameplay effects.</summary>
        public const int PriorityNormal = 1;
        /// <summary>Big events the player must hear (hits on the player, crashes, big blasts).</summary>
        public const int PriorityImportant = 2;
        /// <summary>UI cues, the damage warning and win/lose: never stolen by gameplay cues.</summary>
        public const int PriorityUi = 3;

        private static readonly string[] Names = {
            "engine_loop",      // EngineLoop
            "player_gun",       // PlayerGun
            "bomb_release",     // BombRelease
            "bomb_impact",      // BombImpact
            "hit_enemy",        // HitEnemy
            "hit_player",       // HitPlayer
            "enemy_gun",        // EnemyGun
            "flak_fire",        // FlakFire
            "flak_burst",       // FlakBurst
            "explosion",        // Explosion
            "explosion_big",    // ExplosionBig
            "zeppelin_hum",     // ZeppelinHum
            "smoke_crackle",    // SmokeCrackle
            "crash",            // Crash
            "pickup",           // Pickup
            "damage_warning",   // DamageWarning
            "ui_click",         // UiClick
            "ui_page",          // UiPage
            "ui_pause",         // UiPause
            "typewriter",       // Typewriter
            "stamp",            // Stamp
            "win",              // Win
            "lose"              // Lose
        };

        /// <summary>Number of clip names (one per SfxId).</summary>
        public static int ClipCount => Names.Length;

        /// <summary>The clip's file name without extension, e.g. "explosion_big". Null for an
        /// id outside the table.</summary>
        public static string ClipName(SfxId id) {
            int i = (int)id;
            return i >= 0 && i < Names.Length ? Names[i] : null;
        }

        /// <summary>The Resources.Load path of the clip, e.g. "Audio/sfx/explosion_big".</summary>
        public static string ClipPath(SfxId id) {
            string name = ClipName(id);
            return name != null ? ClipFolder + name : null;
        }

        /// <summary>Voice-stealing rank of a cue; higher survives longer.</summary>
        public static int Priority(SfxId id) {
            switch (id) {
                case SfxId.PlayerGun:
                case SfxId.EnemyGun:
                    return PriorityRattle;
                case SfxId.HitPlayer:
                case SfxId.Crash:
                case SfxId.ExplosionBig:
                case SfxId.Pickup:
                    return PriorityImportant;
                case SfxId.DamageWarning:
                case SfxId.UiClick:
                case SfxId.UiPage:
                case SfxId.UiPause:
                case SfxId.Typewriter:
                case SfxId.Stamp:
                case SfxId.Win:
                case SfxId.Lose:
                    return PriorityUi;
                default:
                    return PriorityNormal;
            }
        }

        /// <summary>Stereo pan for a camera-viewport x (0 = left edge, 1 = right edge).
        /// Off-screen x clamps to the edge pan.</summary>
        public static float Pan(float screenX01) {
            float t = Mathf.Clamp01(screenX01);
            return Mathf.Lerp(-MaxPan, MaxPan, t);
        }

        /// <summary>Symmetric jitter around 1: rand01 0 -> 1 - amount, 0.5 -> 1, 1 -> 1 + amount.
        /// Used for one-shot pitch.</summary>
        public static float Jitter(float rand01, float amount) {
            float r = Mathf.Clamp01(rand01);
            float a = Mathf.Max(0f, amount);
            return 1f + (2f * r - 1f) * a;
        }

        /// <summary>One-sided volume jitter: 1 - rand01 * amount, never below 0.</summary>
        public static float VolumeScale(float rand01, float amount) {
            float r = Mathf.Clamp01(rand01);
            float a = Mathf.Max(0f, amount);
            return Mathf.Max(0f, 1f - r * a);
        }

        /// <summary>True when a viewport point (camera WorldToViewportPoint) is inside the view.</summary>
        public static bool IsOnScreen(float viewportX, float viewportY) {
            return viewportX >= 0f && viewportX <= 1f && viewportY >= 0f && viewportY <= 1f;
        }

        /// <summary>1 inside the camera view, offscreenVolume (clamped 0..1) outside it.</summary>
        public static float OffscreenFactor(float viewportX, float viewportY, float offscreenVolume) {
            return IsOnScreen(viewportX, viewportY) ? 1f : Mathf.Clamp01(offscreenVolume);
        }

        /// <summary>Engine-loop pitch for a vertical velocity (world units/s, + = climbing) and a
        /// damage percentage (0..100). Full climb/dive is reached at PlanePitch's default
        /// full-pitch speed.</summary>
        public static float EnginePitch(float verticalVelocity, float damagePercent) {
            return EnginePitch(verticalVelocity, damagePercent, PlanePitch.DefaultFullPitchSpeed);
        }

        /// <summary>EnginePitch with an explicit full-pitch speed (balance pitch.fullPitchSpeed).
        /// A non-positive fullPitchSpeed falls back to the PlanePitch default.</summary>
        public static float EnginePitch(float verticalVelocity, float damagePercent, float fullPitchSpeed) {
            float full = fullPitchSpeed > 0f ? fullPitchSpeed : PlanePitch.DefaultFullPitchSpeed;
            float t = Mathf.Clamp(verticalVelocity / full, -1f, 1f);
            float pitch = t >= 0f
                ? Mathf.Lerp(1f, EngineClimbPitch, t)
                : Mathf.Lerp(1f, EngineDivePitch, -t);
            float damage01 = Mathf.Clamp01(damagePercent / 100f);
            return pitch * (1f - EngineDamageDrop * damage01);
        }

        /// <summary>
        /// The voice to steal for a new cue when the pool is full: among voices whose priority
        /// is at most incomingPriority, the lowest priority wins, and among equal priorities the
        /// oldest (smallest start value; any increasing counter works). Ties keep the lowest
        /// index. Returns -1 when the arrays are null/empty or every voice outranks the new cue.
        /// Only the first min(priorities.Length, startTimes.Length) entries are considered.
        /// </summary>
        public static int VoiceToSteal(int[] priorities, float[] startTimes, int incomingPriority) {
            if (priorities == null || startTimes == null) return -1;
            int n = Mathf.Min(priorities.Length, startTimes.Length);
            int best = -1;
            for (int i = 0; i < n; i++) {
                if (priorities[i] > incomingPriority) continue;
                if (best < 0
                    || priorities[i] < priorities[best]
                    || (priorities[i] == priorities[best] && startTimes[i] < startTimes[best])) {
                    best = i;
                }
            }
            return best;
        }

        /// <summary>VoiceToSteal without a priority guard: always returns a voice when there is one.</summary>
        public static int VoiceToSteal(int[] priorities, float[] startTimes) {
            return VoiceToSteal(priorities, startTimes, int.MaxValue);
        }
    }
}
