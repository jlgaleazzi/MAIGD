using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Biplanes.Audio;
using Biplanes.Core;

namespace Biplanes.Tests {

    /// <summary>
    /// T949: the pure audio math (AudioMix) and the Balance "audio" block (AudioData.Resolve).
    /// No AudioSources, clips or scenes are involved.
    /// </summary>
    public class AudioMixTests {

        private const float Eps = 1e-4f;

        // ── Clip table ──────────────────────────────────────────────────

        [Test]
        public void SfxId_Has23Values_AndEveryOneHasAClipName() {
            var ids = (SfxId[])Enum.GetValues(typeof(SfxId));
            Assert.AreEqual(23, ids.Length);
            Assert.AreEqual(ids.Length, AudioMix.ClipCount);
            foreach (var id in ids) Assert.IsNotNull(AudioMix.ClipName(id), id.ToString());
        }

        [Test]
        public void ClipNames_AreSnakeCaseOfTheId_AndUnique() {
            var seen = new HashSet<string>();
            foreach (SfxId id in Enum.GetValues(typeof(SfxId))) {
                string name = AudioMix.ClipName(id);
                StringAssert.IsMatch("^[a-z]+(_[a-z]+)*$", name);
                string expected = Regex.Replace(id.ToString(), "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
                Assert.AreEqual(expected, name, id.ToString());
                Assert.IsTrue(seen.Add(name), "duplicate " + name);
            }
        }

        [Test]
        public void ClipPath_IsUnderAudioSfx() {
            Assert.AreEqual("Audio/sfx/explosion_big", AudioMix.ClipPath(SfxId.ExplosionBig));
            Assert.AreEqual("Audio/sfx/engine_loop", AudioMix.ClipPath(SfxId.EngineLoop));
            Assert.AreEqual("Audio/sfx/ui_click", AudioMix.ClipPath(SfxId.UiClick));
        }

        [Test]
        public void ClipName_OutOfRange_IsNull() {
            Assert.IsNull(AudioMix.ClipName((SfxId)999));
            Assert.IsNull(AudioMix.ClipPath((SfxId)(-1)));
        }

        [Test]
        public void Priority_UiAndWarningsOutrankEffects_EffectsOutrankGunRattles() {
            Assert.Greater(AudioMix.Priority(SfxId.UiClick), AudioMix.Priority(SfxId.Explosion));
            Assert.Greater(AudioMix.Priority(SfxId.DamageWarning), AudioMix.Priority(SfxId.HitPlayer));
            Assert.Greater(AudioMix.Priority(SfxId.Explosion), AudioMix.Priority(SfxId.PlayerGun));
            Assert.Greater(AudioMix.Priority(SfxId.FlakBurst), AudioMix.Priority(SfxId.EnemyGun));
            Assert.AreEqual(AudioMix.PriorityRattle, AudioMix.Priority(SfxId.PlayerGun));
            Assert.AreEqual(AudioMix.PriorityUi, AudioMix.Priority(SfxId.Win));
            Assert.AreEqual(AudioMix.PriorityUi, AudioMix.Priority(SfxId.Lose));
        }

        // ── Pan ─────────────────────────────────────────────────────────

        [Test]
        public void Pan_EdgesAndCentre() {
            Assert.AreEqual(-0.8f, AudioMix.Pan(0f), Eps);
            Assert.AreEqual(0f, AudioMix.Pan(0.5f), Eps);
            Assert.AreEqual(0.8f, AudioMix.Pan(1f), Eps);
            Assert.AreEqual(0.4f, AudioMix.Pan(0.75f), Eps);
        }

        [Test]
        public void Pan_OffscreenClampsToTheEdge() {
            Assert.AreEqual(-0.8f, AudioMix.Pan(-3f), Eps);
            Assert.AreEqual(0.8f, AudioMix.Pan(2.5f), Eps);
        }

        // ── Jitter ──────────────────────────────────────────────────────

        [Test]
        public void Jitter_IsSymmetricAroundOne() {
            Assert.AreEqual(0.94f, AudioMix.Jitter(0f, 0.06f), Eps);
            Assert.AreEqual(1f, AudioMix.Jitter(0.5f, 0.06f), Eps);
            Assert.AreEqual(1.06f, AudioMix.Jitter(1f, 0.06f), Eps);
        }

        [Test]
        public void Jitter_ClampsRandAndIgnoresNegativeAmount() {
            Assert.AreEqual(1.06f, AudioMix.Jitter(5f, 0.06f), Eps);
            Assert.AreEqual(0.94f, AudioMix.Jitter(-1f, 0.06f), Eps);
            Assert.AreEqual(1f, AudioMix.Jitter(0.9f, -0.2f), Eps);
            Assert.AreEqual(1f, AudioMix.Jitter(0.2f, 0f), Eps);
        }

        [Test]
        public void VolumeScale_OnlyLowersTheVolume() {
            Assert.AreEqual(1f, AudioMix.VolumeScale(0f, 0.15f), Eps);
            Assert.AreEqual(0.85f, AudioMix.VolumeScale(1f, 0.15f), Eps);
            Assert.AreEqual(0.925f, AudioMix.VolumeScale(0.5f, 0.15f), Eps);
            Assert.AreEqual(0f, AudioMix.VolumeScale(1f, 3f), Eps);
        }

        // ── Off-screen ──────────────────────────────────────────────────

        [Test]
        public void OffscreenFactor_FullInsideTheView_ReducedOutside() {
            Assert.AreEqual(1f, AudioMix.OffscreenFactor(0.5f, 0.5f, 0.4f), Eps);
            Assert.AreEqual(1f, AudioMix.OffscreenFactor(0f, 1f, 0.4f), Eps);
            Assert.AreEqual(0.4f, AudioMix.OffscreenFactor(1.2f, 0.5f, 0.4f), Eps);
            Assert.AreEqual(0.4f, AudioMix.OffscreenFactor(0.5f, -0.1f, 0.4f), Eps);
            Assert.IsFalse(AudioMix.IsOnScreen(-0.01f, 0.5f));
        }

        // ── Engine pitch ────────────────────────────────────────────────

        [Test]
        public void EnginePitch_LevelFlightIsOne() {
            Assert.AreEqual(1f, AudioMix.EnginePitch(0f, 0f), Eps);
        }

        [Test]
        public void EnginePitch_ClimbRaises_DiveLowers_AndClamps() {
            float full = PlanePitch.DefaultFullPitchSpeed;
            Assert.AreEqual(1.15f, AudioMix.EnginePitch(full, 0f), Eps);
            Assert.AreEqual(1.15f, AudioMix.EnginePitch(full * 10f, 0f), Eps);
            Assert.AreEqual(0.9f, AudioMix.EnginePitch(-full, 0f), Eps);
            Assert.AreEqual(0.9f, AudioMix.EnginePitch(-full * 10f, 0f), Eps);
            Assert.AreEqual(1.075f, AudioMix.EnginePitch(full * 0.5f, 0f), Eps);
            Assert.AreEqual(0.95f, AudioMix.EnginePitch(-full * 0.5f, 0f), Eps);
        }

        [Test]
        public void EnginePitch_DamageLowersItSlightly_AndIsDeterministic() {
            float healthy = AudioMix.EnginePitch(0f, 0f);
            float wrecked = AudioMix.EnginePitch(0f, 100f);
            Assert.Less(wrecked, healthy);
            Assert.Greater(wrecked, 0.9f);
            Assert.AreEqual(wrecked, AudioMix.EnginePitch(0f, 100f));
            Assert.AreEqual(wrecked, AudioMix.EnginePitch(0f, 250f), Eps);
            Assert.AreEqual(healthy, AudioMix.EnginePitch(0f, -20f), Eps);
        }

        [Test]
        public void EnginePitch_CustomFullSpeed_AndBadFullSpeedFallsBack() {
            Assert.AreEqual(1.15f, AudioMix.EnginePitch(6f, 0f, 6f), Eps);
            Assert.AreEqual(1.075f, AudioMix.EnginePitch(3f, 0f, 6f), Eps);
            Assert.AreEqual(AudioMix.EnginePitch(1.5f, 0f), AudioMix.EnginePitch(1.5f, 0f, 0f), Eps);
        }

        // ── Voice stealing ──────────────────────────────────────────────

        [Test]
        public void VoiceToSteal_PicksTheLowestPriority() {
            int[] prio = { 1, 0, 3, 1 };
            float[] start = { 1f, 9f, 0f, 2f };
            Assert.AreEqual(1, AudioMix.VoiceToSteal(prio, start));
        }

        [Test]
        public void VoiceToSteal_AmongEqualPriorities_PicksTheOldest() {
            int[] prio = { 0, 0, 0, 2 };
            float[] start = { 5f, 2f, 7f, 1f };
            Assert.AreEqual(1, AudioMix.VoiceToSteal(prio, start));
        }

        [Test]
        public void VoiceToSteal_TiesKeepTheLowestIndex() {
            int[] prio = { 1, 1 };
            float[] start = { 3f, 3f };
            Assert.AreEqual(0, AudioMix.VoiceToSteal(prio, start));
        }

        [Test]
        public void VoiceToSteal_GunRattleNeverStealsUiOrWarnings() {
            int[] prio = { AudioMix.PriorityUi, AudioMix.PriorityImportant, AudioMix.PriorityUi };
            float[] start = { 1f, 2f, 3f };
            Assert.AreEqual(-1, AudioMix.VoiceToSteal(prio, start, AudioMix.PriorityRattle));
            Assert.AreEqual(1, AudioMix.VoiceToSteal(prio, start, AudioMix.PriorityImportant));
            Assert.AreEqual(1, AudioMix.VoiceToSteal(prio, start, AudioMix.PriorityUi));
        }

        [Test]
        public void VoiceToSteal_EqualPriorityMayStealItsOwnKind() {
            int[] prio = { AudioMix.PriorityRattle, AudioMix.PriorityRattle };
            float[] start = { 4f, 3f };
            Assert.AreEqual(1, AudioMix.VoiceToSteal(prio, start, AudioMix.PriorityRattle));
        }

        [Test]
        public void VoiceToSteal_EmptyNullOrMismatchedArrays() {
            Assert.AreEqual(-1, AudioMix.VoiceToSteal(null, new float[2]));
            Assert.AreEqual(-1, AudioMix.VoiceToSteal(new int[2], null));
            Assert.AreEqual(-1, AudioMix.VoiceToSteal(new int[0], new float[0]));
            Assert.AreEqual(0, AudioMix.VoiceToSteal(new[] { 2, 0 }, new[] { 1f }));
        }

        // ── AudioData.Resolve ───────────────────────────────────────────

        [Test]
        public void Resolve_NullBalanceOrMissingBlock_GivesDefaults() {
            AssertDefaults(AudioData.Resolve(null));
            AssertDefaults(AudioData.Resolve(new GameBalance()));
        }

        [Test]
        public void Resolve_MissingKeys_FallBack_PresentKeysKept() {
            var balance = new GameBalance { audio = new AudioData { master = 0.5f, maxVoices = 6 } };
            var r = AudioData.Resolve(balance);
            Assert.AreEqual(0.5f, r.master, Eps);
            Assert.AreEqual(6, r.maxVoices);
            Assert.AreEqual(AudioData.DefaultSfx, r.sfx, Eps);
            Assert.AreEqual(AudioData.DefaultMusic, r.music, Eps);
            Assert.AreEqual(AudioData.DefaultPitchJitter, r.pitchJitter, Eps);
            Assert.AreEqual(AudioData.DefaultVolumeJitter, r.volumeJitter, Eps);
            Assert.AreEqual(AudioData.DefaultOffscreenVolume, r.offscreenVolume, Eps);
        }

        [Test]
        public void Resolve_ClampsVolumesAndVoices() {
            var balance = new GameBalance { audio = new AudioData { master = 4f, sfx = 2f, maxVoices = 500 } };
            var r = AudioData.Resolve(balance);
            Assert.AreEqual(1f, r.master, Eps);
            Assert.AreEqual(1f, r.sfx, Eps);
            Assert.AreEqual(AudioData.AbsoluteMaxVoices, r.maxVoices);
        }

        [Test]
        public void Resolve_DoesNotMutateTheSource() {
            var src = new AudioData();
            AudioData.Resolve(new GameBalance { audio = src });
            Assert.AreEqual(0f, src.master);
            Assert.AreEqual(0, src.maxVoices);
        }

        [Test]
        public void Defaults_MatchTheArchitectureContract() {
            Assert.AreEqual(0.8f, AudioData.DefaultMaster, Eps);
            Assert.AreEqual(1.0f, AudioData.DefaultSfx, Eps);
            Assert.AreEqual(0.6f, AudioData.DefaultMusic, Eps);
            Assert.AreEqual(0.06f, AudioData.DefaultPitchJitter, Eps);
            Assert.AreEqual(0.15f, AudioData.DefaultVolumeJitter, Eps);
            Assert.AreEqual(0.4f, AudioData.DefaultOffscreenVolume, Eps);
            Assert.AreEqual(12, AudioData.DefaultMaxVoices);
        }

        private static void AssertDefaults(AudioData r) {
            Assert.IsNotNull(r);
            Assert.AreEqual(AudioData.DefaultMaster, r.master, Eps);
            Assert.AreEqual(AudioData.DefaultSfx, r.sfx, Eps);
            Assert.AreEqual(AudioData.DefaultMusic, r.music, Eps);
            Assert.AreEqual(AudioData.DefaultPitchJitter, r.pitchJitter, Eps);
            Assert.AreEqual(AudioData.DefaultVolumeJitter, r.volumeJitter, Eps);
            Assert.AreEqual(AudioData.DefaultOffscreenVolume, r.offscreenVolume, Eps);
            Assert.AreEqual(AudioData.DefaultMaxVoices, r.maxVoices);
        }
    }
}
