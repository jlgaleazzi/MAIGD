using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Biplanes.Audio;
using Biplanes.Player;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// B032 (T953 bugs B1, B2, B3, B7): the AudioListener rule, the quitting rule, which UI
    /// cues survive a scene unload, and the crash-clip start rule. The listener itself is only
    /// added in Play mode, so EditMode checks the pure decision and that nothing is added here.
    /// </summary>
    public class AudioListenerTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private AudioManager _am;

        [TearDown]
        public void TearDown() {
            if (_am != null) Object.DestroyImmediate(_am.gameObject);
            _am = null;
            typeof(AudioManager).GetField("_instance", PrivateStatic).SetValue(null, null);
            AudioListener.pause = false;
        }

        private AudioManager MakeManager() {
            Assume.That(AudioManager.GetOrNull(), Is.Null, "an AudioManager leaked from another test");
            _am = AudioManager.Instance;
            Assert.IsNotNull(_am);
            Assert.IsNotNull(_am.Mix); // forces EnsureInit (Awake does not run in EditMode)
            return _am;
        }

        // ── B1: listener ────────────────────────────────────────────────

        [Test]
        public void NeedsListener_OnlyWhenNoOtherEnabledListenerExists() {
            Assert.IsTrue(AudioManager.NeedsListener(0), "no listener: the manager supplies one");
            Assert.IsFalse(AudioManager.NeedsListener(1), "a scene listener exists: never a second one");
            Assert.IsFalse(AudioManager.NeedsListener(2));
            Assert.IsTrue(AudioManager.NeedsListener(-1), "a bad count is treated as none");
        }

        [Test]
        public void EditMode_ManagerInit_AddsNoAudioListener() {
            var am = MakeManager();
            Assert.IsNull(am.GetComponent<AudioListener>(), "EditMode never adds a listener");
        }

        // ── B7: quitting ────────────────────────────────────────────────

        [Test]
        public void MayAccess_FalseOnlyInPlayModeWhileQuitting() {
            Assert.IsTrue(AudioManager.MayAccess(false, false));
            Assert.IsTrue(AudioManager.MayAccess(false, true), "EditMode tests are never blocked");
            Assert.IsTrue(AudioManager.MayAccess(true, false));
            Assert.IsFalse(AudioManager.MayAccess(true, true), "no manager is handed out or created at quit");
        }

        [Test]
        public void QuittingFlag_DoesNotBlockEditMode() {
            var field = typeof(AudioManager).GetField("_quitting", PrivateStatic);
            Assert.IsNotNull(field, "AudioManager._quitting not found by reflection");
            bool old = (bool)field.GetValue(null);
            try {
                field.SetValue(null, true);
                Assert.IsNull(AudioManager.GetOrNull(), "EditMode still never creates one");
                var am = MakeManager();
                Assert.AreSame(am, AudioManager.GetOrNull());
            } finally {
                field.SetValue(null, old);
            }
        }

        // ── B3: scene unload ────────────────────────────────────────────

        [Test]
        public void KeepsPlayingOnSceneUnload_UiScreenCuesOnly() {
            Assert.IsTrue(AudioManager.KeepsPlayingOnSceneUnload(SfxId.UiClick));
            Assert.IsTrue(AudioManager.KeepsPlayingOnSceneUnload(SfxId.UiPage));
            Assert.IsTrue(AudioManager.KeepsPlayingOnSceneUnload(SfxId.UiPause));
            Assert.IsTrue(AudioManager.KeepsPlayingOnSceneUnload(SfxId.Typewriter));
            Assert.IsTrue(AudioManager.KeepsPlayingOnSceneUnload(SfxId.Stamp));
            Assert.IsTrue(AudioManager.KeepsPlayingOnSceneUnload(SfxId.Win));
            Assert.IsTrue(AudioManager.KeepsPlayingOnSceneUnload(SfxId.Lose));
            Assert.IsFalse(AudioManager.KeepsPlayingOnSceneUnload(SfxId.Crash), "the crash tail must stop");
            Assert.IsFalse(AudioManager.KeepsPlayingOnSceneUnload(SfxId.DamageWarning));
        }

        [Test]
        public void SceneUnload_StopsCrashUiVoice_KeepsTheClick() {
            var am = MakeManager();
            Assume.That(Resources.Load<AudioClip>(AudioMix.ClipPath(SfxId.Crash)), Is.Not.Null, "crash clip not imported");
            Assume.That(Resources.Load<AudioClip>(AudioMix.ClipPath(SfxId.UiClick)), Is.Not.Null, "click clip not imported");

            am.PlayUi(SfxId.Crash);
            am.PlayUi(SfxId.UiClick);
            var ids = (SfxId[])typeof(AudioManager).GetField("_uiIds", Private).GetValue(am);
            // T955 (QA): PlayUi picks the first voice that isn't playing. If EditMode reports
            // isPlaying false (e.g. audio off in batch mode), both cues take voice 0 and this
            // setup can't hold; AudioFixesQaTests covers the rule with _uiIds set directly.
            Assume.That(System.Array.IndexOf(ids, SfxId.Crash) >= 0 && System.Array.IndexOf(ids, SfxId.UiClick) >= 0,
                "EditMode AudioSources don't report isPlaying here; see AudioFixesQaTests");

            var unload = typeof(AudioManager).GetMethod("OnSceneUnloaded", Private);
            Assert.IsNotNull(unload, "AudioManager.OnSceneUnloaded not found by reflection");
            unload.Invoke(am, new object[] { default(Scene) });

            CollectionAssert.DoesNotContain(ids, SfxId.Crash, "the crash voice is stopped and cleared");
            Assert.Contains(SfxId.UiClick, ids, "the Retry click keeps playing");
        }

        // ── B2: crash timing ────────────────────────────────────────────

        [Test]
        public void CrashClip_IsAShortCrunchNearTheStart() {
            Assert.AreEqual(0.1f, PlayerPlane.CrashCrunchOffset, 1e-4f);
            Assert.AreEqual(1.0f, PlayerPlane.CrashClipSeconds, 0.2f);
            Assert.Less(PlayerPlane.CrashCrunchOffset, PlayerPlane.CrashFallSeconds);
        }

        [Test]
        public void ShouldStartCrashSound_JustBeforeLanding_OrWhenTheResultFreezesTheFall() {
            Assert.IsFalse(PlayerPlane.ShouldStartCrashSound(0.5f, 0.2f), "mid-fall: not yet");
            Assert.IsFalse(PlayerPlane.ShouldStartCrashSound(PlayerPlane.CrashCrunchOffset + 0.02f, 0.2f));
            Assert.IsTrue(PlayerPlane.ShouldStartCrashSound(PlayerPlane.CrashCrunchOffset, 0.2f),
                "the crunch lands with the wreck");
            Assert.IsTrue(PlayerPlane.ShouldStartCrashSound(0f, 0f), "landing at once plays at once");
            Assert.IsTrue(PlayerPlane.ShouldStartCrashSound(float.PositiveInfinity,
                PlayerPlane.CrashFallSeconds - PlayerPlane.CrashCrunchOffset + 0.001f),
                "a fall the result screen cuts off still gets its crunch");
            Assert.IsFalse(PlayerPlane.ShouldStartCrashSound(float.PositiveInfinity, 0.5f));
        }
    }
}
