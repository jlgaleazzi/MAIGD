using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Biplanes.Audio;
using Biplanes.Core;
using Biplanes.Player;
using Biplanes.Weapons;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// T953 (QA, milestone M6 audio): the clip files, the AudioManager core (pools, loops,
    /// pause mirror, PlayUi while paused, scene-unload stop, no source growth across
    /// Retry-like cycles), the PlayerPlane loop owner and the Health hit-cue counts.
    ///
    /// In EditMode GetOrNull() is null, so these tests build a manager with
    /// AudioManager.Instance (no DontDestroyOnLoad outside Play mode) and destroy it in
    /// TearDown, so the other suites still see "no AudioManager". Private members are reached
    /// by reflection; the number of cues played is read from AudioManager._playCounter, which
    /// both Play and PlayUi increment once per cue actually started.
    /// </summary>
    public class AudioManagerQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const int UiVoices = 4;

        private readonly List<GameObject> _created = new List<GameObject>();
        private AudioManager _am;
        private float _timeScale;

        [SetUp]
        public void SetUp() {
            _timeScale = Time.timeScale;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        [TearDown]
        public void TearDown() {
            Time.timeScale = _timeScale;
            AudioListener.pause = false;

            if (_am != null) Object.DestroyImmediate(_am.gameObject);
            _am = null;
            typeof(AudioManager).GetField("_instance", PrivateStatic).SetValue(null, null);
            typeof(PlayerPlane).GetField("_audioOwner", PrivateStatic).SetValue(null, null);

            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();

            // PlayerPlane.Create builds the bullet and bomb pools.
            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (p != null) Object.DestroyImmediate(p.gameObject);
            }
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go != null && go.hideFlags == HideFlags.HideAndDontSave && go.name.StartsWith("Pool<")) {
                    Object.DestroyImmediate(go);
                }
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private AudioManager MakeManager() {
            Assume.That(AudioManager.GetOrNull(), Is.Null, "an AudioManager leaked from another test");
            _am = AudioManager.Instance;
            Assert.IsNotNull(_am);
            var mix = _am.Mix; // forces EnsureInit (Awake does not run in EditMode)
            Assert.IsNotNull(mix);
            return _am;
        }

        private static void AssumeClip(SfxId id) {
            Assume.That(Resources.Load<AudioClip>(AudioMix.ClipPath(id)), Is.Not.Null,
                "clip " + AudioMix.ClipPath(id) + " not imported yet");
        }

        private float PlayCounter => (float)typeof(AudioManager).GetField("_playCounter", Private).GetValue(_am);

        private int SourceCount => _am.GetComponents<AudioSource>().Length;

        private static void Invoke(object target, string method, params object[] args) {
            var m = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found by reflection");
            m.Invoke(target, args);
        }

        private void SceneUnload() {
            Invoke(_am, "OnSceneUnloaded", default(Scene));
        }

        /// <summary>The clip of the most recently started gameplay one-shot voice.</summary>
        private AudioClip LastGameplayClip() {
            var voices = (AudioSource[])typeof(AudioManager).GetField("_voices", Private).GetValue(_am);
            var order = (float[])typeof(AudioManager).GetField("_voiceOrder", Private).GetValue(_am);
            int best = -1;
            for (int i = 0; i < voices.Length; i++) {
                if (order[i] > 0f && (best < 0 || order[i] > order[best])) best = i;
            }
            return best >= 0 ? voices[best].clip : null;
        }

        private AudioSource LoopSource(string key) {
            var loops = typeof(AudioManager).GetField("_loops", Private).GetValue(_am) as System.Collections.IDictionary;
            Assert.IsNotNull(loops);
            if (!loops.Contains(key)) return null;
            object loop = loops[key];
            return (AudioSource)loop.GetType().GetField("Src").GetValue(loop);
        }

        private Health MakeHealth(string name, Team team, float maxHitPoints) {
            var go = new GameObject(name);
            _created.Add(go);
            go.AddComponent<TeamTag>().team = team;
            var h = go.AddComponent<Health>();
            Invoke(h, "Awake");
            if (maxHitPoints > 0f) h.SetMaxHitPoints(maxHitPoints);
            return h;
        }

        private PlayerPlane MakePlane(bool onRunway) {
            var plane = PlayerPlane.Create(new Vector3(0f, 0f, 0f), onRunway);
            _created.Add(plane.gameObject);
            Invoke(plane.GetComponent<Health>(), "Awake");
            return plane;
        }

        // ── Clips on disk (T948) ────────────────────────────────────────

        private static string SfxFolder =>
            Path.Combine(Application.dataPath, "Biplanes", "Resources", "Audio", "sfx");

        [Test]
        public void ClipFiles_OneWavPerSfxId_NamesMatchClipName_NoExtras() {
            var expected = new HashSet<string>();
            foreach (SfxId id in Enum.GetValues(typeof(SfxId))) {
                string name = AudioMix.ClipName(id);
                expected.Add(name + ".wav");
                Assert.IsTrue(File.Exists(Path.Combine(SfxFolder, name + ".wav")), "missing " + name + ".wav");
            }
            foreach (string f in Directory.GetFiles(SfxFolder, "*.wav")) {
                Assert.IsTrue(expected.Contains(Path.GetFileName(f)), "unexpected clip " + Path.GetFileName(f));
            }
            Assert.AreEqual(23, expected.Count);
        }

        [Test]
        public void ClipFiles_AreMono16Bit44k1Pcm() {
            foreach (SfxId id in Enum.GetValues(typeof(SfxId))) {
                string path = Path.Combine(SfxFolder, AudioMix.ClipName(id) + ".wav");
                Assume.That(File.Exists(path), path);
                using (var r = new BinaryReader(File.OpenRead(path))) {
                    Assert.AreEqual("RIFF", new string(r.ReadChars(4)), path);
                    r.ReadInt32();
                    Assert.AreEqual("WAVE", new string(r.ReadChars(4)), path);
                    bool found = false;
                    while (r.BaseStream.Position + 8 <= r.BaseStream.Length) {
                        string chunk = new string(r.ReadChars(4));
                        int size = r.ReadInt32();
                        if (chunk == "fmt ") {
                            short format = r.ReadInt16();
                            short channels = r.ReadInt16();
                            int rate = r.ReadInt32();
                            r.ReadInt32(); // byte rate
                            r.ReadInt16(); // block align
                            short bits = r.ReadInt16();
                            Assert.AreEqual(1, format, path + " PCM");
                            Assert.AreEqual(1, channels, path + " mono");
                            Assert.AreEqual(44100, rate, path + " 44.1 kHz");
                            Assert.AreEqual(16, bits, path + " 16-bit");
                            found = true;
                            break;
                        }
                        r.BaseStream.Position += size + (size & 1);
                    }
                    Assert.IsTrue(found, path + " has no fmt chunk");
                }
            }
        }

        [Test]
        public void Clips_LoadThroughResources_ForEverySfxId() {
            AssumeClip(SfxId.EngineLoop); // the folder is imported at all
            foreach (SfxId id in Enum.GetValues(typeof(SfxId))) {
                Assert.IsNotNull(Resources.Load<AudioClip>(AudioMix.ClipPath(id)), AudioMix.ClipPath(id));
            }
        }

        // ── Singleton in EditMode ───────────────────────────────────────

        [Test]
        public void Instance_InEditMode_IsNotDontDestroyOnLoad_AndGetOrNullIsNullAfterDestroy() {
            var am = MakeManager();
            Assert.AreNotEqual("DontDestroyOnLoad", am.gameObject.scene.name);
            Assert.AreSame(am, AudioManager.GetOrNull(), "an existing manager is returned");
            Object.DestroyImmediate(am.gameObject);
            _am = null;
            Assert.IsNull(AudioManager.GetOrNull(), "EditMode never creates one");
        }

        [Test]
        public void Pools_AreMaxVoicesPlusFourUi_UiIgnoresListenerPause_GameplayDoesNot() {
            var am = MakeManager();
            var sources = am.GetComponents<AudioSource>();
            Assert.AreEqual(am.Mix.maxVoices + UiVoices, sources.Length);
            int ui = 0;
            foreach (var s in sources) {
                if (s.ignoreListenerPause) ui++;
                Assert.IsFalse(s.playOnAwake);
                Assert.AreEqual(0f, s.spatialBlend);
            }
            Assert.AreEqual(UiVoices, ui);
        }

        // ── Pause and PlayUi ────────────────────────────────────────────

        [Test]
        public void Paused_PlayIsDropped_ListenerPauses_PlayUiStillPlays_ResumeUnpauses() {
            var am = MakeManager();
            AssumeClip(SfxId.Explosion);
            AssumeClip(SfxId.UiClick);

            Time.timeScale = 0f;
            float c0 = PlayCounter;
            am.Play(SfxId.Explosion);
            Assert.AreEqual(c0, PlayCounter, "a gameplay one-shot is dropped while paused");
            Assert.IsTrue(AudioListener.pause, "timeScale 0 is mirrored into AudioListener.pause");

            am.PlayUi(SfxId.UiClick);
            Assert.AreEqual(c0 + 1f, PlayCounter, "PlayUi plays while paused");
            var clip = Resources.Load<AudioClip>(AudioMix.ClipPath(SfxId.UiClick));
            bool onUiVoice = false;
            foreach (var s in am.GetComponents<AudioSource>()) {
                if (s.clip == clip) {
                    Assert.IsTrue(s.ignoreListenerPause, "UI cue must sit on an ignoreListenerPause voice");
                    onUiVoice = true;
                }
            }
            Assert.IsTrue(onUiVoice);

            Time.timeScale = 1f;
            am.Play(SfxId.Explosion);
            Assert.AreEqual(c0 + 2f, PlayCounter);
            Assert.IsFalse(AudioListener.pause, "resuming unpauses the listener");
            Assert.AreEqual(Resources.Load<AudioClip>(AudioMix.ClipPath(SfxId.Explosion)), LastGameplayClip());
        }

        // ── Loops ───────────────────────────────────────────────────────

        [Test]
        public void StartLoop_SameIdAndKey_IsNoOp_OtherIdReplaces_StopRecyclesTheSource() {
            var am = MakeManager();
            AssumeClip(SfxId.EngineLoop);
            AssumeClip(SfxId.SmokeCrackle);
            int baseline = SourceCount;

            am.StartLoop(SfxId.EngineLoop, "k");
            am.StartLoop(SfxId.EngineLoop, "k");
            Assert.AreEqual(1, am.LoopCount);
            Assert.AreEqual(baseline + 1, SourceCount);
            Assert.IsTrue(LoopSource("k").loop);

            am.StartLoop(SfxId.SmokeCrackle, "k");
            Assert.AreEqual(1, am.LoopCount, "a different id under the same key replaces it");
            Assert.AreEqual(baseline + 1, SourceCount);
            Assert.AreEqual(Resources.Load<AudioClip>(AudioMix.ClipPath(SfxId.SmokeCrackle)), LoopSource("k").clip);

            am.StopLoop("k");
            Assert.IsFalse(am.HasLoop("k"));
            Assert.AreEqual(0, am.LoopCount);
            am.StartLoop(SfxId.EngineLoop, "k2");
            Assert.AreEqual(baseline + 1, SourceCount, "a stopped loop's source is reused");
        }

        [Test]
        public void LoopPitchAndVolume_AreClamped_UnknownOrEmptyKeysAreIgnored() {
            var am = MakeManager();
            AssumeClip(SfxId.EngineLoop);
            am.StartLoop(SfxId.EngineLoop, "k");
            var src = LoopSource("k");

            am.SetLoopPitch("k", 10f);
            Assert.AreEqual(3f, src.pitch, 1e-4f);
            am.SetLoopPitch("k", 0f);
            Assert.AreEqual(0.1f, src.pitch, 1e-4f);
            am.SetLoopVolume("k", 0.5f);
            Assert.AreEqual(am.Mix.master * am.Mix.sfx * 0.5f, src.volume, 1e-4f);
            am.SetLoopVolume("k", 4f);
            Assert.AreEqual(am.Mix.master * am.Mix.sfx, src.volume, 1e-4f);

            Assert.DoesNotThrow(() => {
                am.StopLoop("nope");
                am.StopLoop(null);
                am.StopLoop("");
                am.SetLoopPitch("nope", 1f);
                am.SetLoopVolume(null, 1f);
                am.StartLoop(SfxId.EngineLoop, null);
                am.StartLoop(SfxId.EngineLoop, "");
            });
            Assert.AreEqual(1, am.LoopCount);
            Assert.IsFalse(am.HasLoop(null));
        }

        [Test]
        public void SceneUnload_StopsEveryLoop() {
            var am = MakeManager();
            AssumeClip(SfxId.EngineLoop);
            AssumeClip(SfxId.ZeppelinHum);
            am.StartLoop(SfxId.EngineLoop, PlayerPlane.EngineLoopKey);
            am.StartLoop(SfxId.ZeppelinHum, "zeppelin_hum_1");
            Assert.AreEqual(2, am.LoopCount);
            SceneUnload();
            Assert.AreEqual(0, am.LoopCount);
        }

        [Test]
        public void RetryCycles_DoNotGrowAudioSources() {
            var am = MakeManager();
            AssumeClip(SfxId.EngineLoop);
            AssumeClip(SfxId.SmokeCrackle);
            AssumeClip(SfxId.ZeppelinHum);
            int baseline = SourceCount;

            for (int i = 0; i < 6; i++) {
                am.StartLoop(SfxId.EngineLoop, PlayerPlane.EngineLoopKey);
                am.StartLoop(SfxId.SmokeCrackle, PlayerPlane.SmokeLoopKey);
                am.StartLoop(SfxId.ZeppelinHum, "zeppelin_hum_" + (100 + i)); // a fresh key per mission
                am.Play(SfxId.Explosion);
                am.PlayUi(SfxId.UiClick);
                SceneUnload();
                Assert.AreEqual(0, am.LoopCount, "cycle " + i);
            }
            Assert.AreEqual(baseline + 3, SourceCount,
                "only one source per concurrent loop is ever added; cycles reuse them");
            Assert.AreEqual(1, Object.FindObjectsByType<AudioManager>(FindObjectsSortMode.None).Length);
        }

        // ── PlayerPlane loop owner ──────────────────────────────────────

        [Test]
        public void PlayerPlane_RunwayIdle_StartsEngineAtIdlePitchAndVolume() {
            var am = MakeManager();
            AssumeClip(SfxId.EngineLoop);
            var plane = MakePlane(true);
            Invoke(plane, "UpdateAudio");
            Assert.IsTrue(am.HasLoop(PlayerPlane.EngineLoopKey));
            Assert.IsFalse(am.HasLoop(PlayerPlane.SmokeLoopKey), "no smoke at 0% damage");
            var src = LoopSource(PlayerPlane.EngineLoopKey);
            Assert.AreEqual(PlayerPlane.EngineIdlePitch, src.pitch, 1e-4f);
            Assert.AreEqual(am.Mix.master * am.Mix.sfx * PlayerPlane.EngineIdleVolume, src.volume, 1e-4f);
        }

        [Test]
        public void PlayerPlane_OldPlaneTeardown_DoesNotSilenceTheNewPlane() {
            var am = MakeManager();
            AssumeClip(SfxId.EngineLoop);
            var oldPlane = MakePlane(false);
            Invoke(oldPlane, "UpdateAudio");
            Assert.IsTrue(am.HasLoop(PlayerPlane.EngineLoopKey));

            // Retry: the new plane runs a frame before the old one's late OnDisable.
            var newPlane = MakePlane(true);
            Invoke(newPlane, "UpdateAudio");
            Invoke(oldPlane, "OnDisable");
            Assert.IsTrue(am.HasLoop(PlayerPlane.EngineLoopKey), "the old plane must not stop the new engine");

            // A late sceneUnloaded after the new plane started: the next frame restarts it.
            SceneUnload();
            Assert.IsFalse(am.HasLoop(PlayerPlane.EngineLoopKey));
            Invoke(newPlane, "UpdateAudio");
            Assert.IsTrue(am.HasLoop(PlayerPlane.EngineLoopKey), "the engine self-heals on the next frame");

            // The owner's own teardown stops it.
            Invoke(newPlane, "OnDisable");
            Assert.IsFalse(am.HasLoop(PlayerPlane.EngineLoopKey));
        }

        [Test]
        public void PlayerPlane_MissionEnded_StopsLoops_AndTheyStayStopped() {
            var am = MakeManager();
            AssumeClip(SfxId.EngineLoop);
            var plane = MakePlane(false);
            Invoke(plane, "UpdateAudio");
            Assert.IsTrue(am.HasLoop(PlayerPlane.EngineLoopKey));

            Invoke(plane, "OnMissionEndedAudio", true, "won");
            Assert.AreEqual(0, am.LoopCount);
            Invoke(plane, "UpdateAudio");
            Assert.AreEqual(0, am.LoopCount, "no loop restarts after the mission ended");
        }

        // ── Health hit cues ─────────────────────────────────────────────

        [Test]
        public void Health_PlayerHit_OneHitPlayer_Throttled_HealsAndZeroHitsSilent() {
            MakeManager();
            AssumeClip(SfxId.HitPlayer);
            var player = MakeHealth("player", Team.Player, 0f);

            float c0 = PlayCounter;
            player.ApplyDamage(0.1f, DamageSource.Bullet);
            Assert.AreEqual(c0 + 1f, PlayCounter);
            Assert.AreEqual(Resources.Load<AudioClip>(AudioMix.ClipPath(SfxId.HitPlayer)), LastGameplayClip());

            player.ApplyDamage(0.1f, DamageSource.Bullet); // same Time.time: inside the 0.05 s window
            Assert.AreEqual(c0 + 1f, PlayCounter, "throttled to one cue per HitCueMinInterval");

            typeof(Health).GetField("_lastHitCueTime", Private).SetValue(player, -1f);
            player.ApplyDamage(-30f, DamageSource.Heal);
            player.ApplyDamage(0f, DamageSource.Bullet);
            Assert.AreEqual(c0 + 1f, PlayCounter, "heals and 0-damage hits play nothing");
        }

        [Test]
        public void Health_EnemyHit_NonFatalTicks_FatalIsSilent() {
            MakeManager();
            AssumeClip(SfxId.HitEnemy);
            var hurt = MakeHealth("fighter", Team.Enemy, 200f);
            var killed = MakeHealth("fighter", Team.Enemy, 200f);

            float c0 = PlayCounter;
            hurt.ApplyDamage(10f, DamageSource.Bullet);
            Assert.AreEqual(c0 + 1f, PlayCounter);
            Assert.AreEqual(Resources.Load<AudioClip>(AudioMix.ClipPath(SfxId.HitEnemy)), LastGameplayClip());

            killed.ApplyDamage(1f, DamageSource.Collision);
            Assert.IsTrue(killed.IsDead);
            Assert.AreEqual(c0 + 1f, PlayCounter, "a fatal enemy hit plays no HitEnemy (its explosion covers it)");
        }
    }
}
