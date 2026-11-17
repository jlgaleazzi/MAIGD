using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Audio;
using Biplanes.Core;
using Biplanes.Player;

namespace Biplanes.Tests {

    /// <summary>
    /// T951: player, damage and pickup sound hooks. EditMode has no AudioManager
    /// (GetOrNull returns null), so the hooks must be silent no-ops; the pure helpers on
    /// PlayerPlane (sputter, crash timing) are checked directly.
    /// </summary>
    public class AudioHooksPlayerTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        private Health MakeHealth(string name, Team team, float maxHitPoints) {
            var go = new GameObject(name);
            _created.Add(go);
            go.AddComponent<TeamTag>().team = team;
            var h = go.AddComponent<Health>();
            var awake = typeof(Health).GetMethod("Awake", Private);
            Assert.IsNotNull(awake, "Health.Awake not found by reflection");
            awake.Invoke(h, null);
            if (maxHitPoints > 0f) h.SetMaxHitPoints(maxHitPoints);
            return h;
        }

        [Test]
        public void AudioManager_GetOrNull_IsNullInEditMode() {
            Assert.IsNull(AudioManager.GetOrNull());
        }

        [Test]
        public void Health_HitsAndHeals_WithoutAudioManager_DoNotThrow() {
            var player = MakeHealth("player", Team.Player, 0f);
            var enemy = MakeHealth("fighter", Team.Enemy, 200f);
            Assert.DoesNotThrow(() => {
                player.ApplyDamage(1f, DamageSource.Bullet);
                player.ApplyDamage(0f, DamageSource.Bullet);
                player.ApplyDamage(-30f, DamageSource.Heal);
                enemy.ApplyDamage(1f, DamageSource.Bullet);
                enemy.ApplyDamage(1f, DamageSource.Collision);
            });
            Assert.IsTrue(enemy.IsDead);
            Assert.IsNull(AudioManager.GetOrNull(), "a hit must not create an AudioManager in EditMode");
        }

        [Test]
        public void SputterAmount_StaysIn0To1_AndIsIrregular() {
            int dips = 0;
            int samples = 2000;
            for (int i = 0; i < samples; i++) {
                float t = i * 0.01f;
                float s = PlayerPlane.SputterAmount(t);
                Assert.That(s, Is.InRange(0f, 1f));
                if (s > 0f) dips++;
            }
            Assert.Greater(dips, 0, "a smoking engine must sputter sometimes");
            Assert.Less(dips, samples / 2, "sputters are brief coughs, not most of the time");
            Assert.AreEqual(PlayerPlane.SputterAmount(3.21f), PlayerPlane.SputterAmount(3.21f));
        }

        [Test]
        public void CrashTimeToGround_MatchesFreeFall() {
            Assert.AreEqual(0f, PlayerPlane.CrashTimeToGround(0f, -2f, 14f));
            Assert.AreEqual(0f, PlayerPlane.CrashTimeToGround(-1f, 0f, 14f));
            // From rest: h = g t^2 / 2  ->  t = sqrt(2h / g).
            Assert.AreEqual(Mathf.Sqrt(2f * 7f / 14f), PlayerPlane.CrashTimeToGround(7f, 0f, 14f), 1e-4f);
            // Already diving lands sooner than from rest.
            Assert.Less(PlayerPlane.CrashTimeToGround(7f, -5f, 14f), PlayerPlane.CrashTimeToGround(7f, 0f, 14f));
        }

        [Test]
        public void CrashClipTiming_CrunchSitsInsideTheClip() {
            Assert.Greater(PlayerPlane.CrashClipSeconds, PlayerPlane.CrashCrunchOffset);
            Assert.Greater(PlayerPlane.CrashCrunchOffset, 0f);
        }

        [Test]
        public void LoopVolumes_SitBelowFullScale() {
            Assert.That(PlayerPlane.EngineFlightVolume, Is.InRange(0.6f, 0.7f));
            Assert.Less(PlayerPlane.EngineIdleVolume, PlayerPlane.EngineFlightVolume);
            Assert.Less(PlayerPlane.EngineIdlePitch, 1f);
            Assert.That(PlayerPlane.SmokeCrackleVolume, Is.InRange(0.4f, 0.6f));
            Assert.That(Biplanes.Enemies.ZeppelinAI.HumVolume, Is.InRange(0.4f, 0.5f));
        }
    }
}
