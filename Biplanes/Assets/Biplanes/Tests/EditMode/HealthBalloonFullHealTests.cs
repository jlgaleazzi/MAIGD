using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// B062: a health balloon restores 100% of the player's health (GDD 2026-11-20). The heal
    /// goes through Health.ApplyDamage(-restore, DamageSource.Heal): no damageTaken multiplier,
    /// no player resistance, clamped at 0% damage. A balloon collected at full health is still
    /// used up. EditMode doesn't run Awake on AddComponent, so Health.Awake and the private
    /// HealthBalloon.Collect are invoked by reflection.
    /// </summary>
    public class HealthBalloonFullHealTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        /// <summary>A player-mode Health (percent damage) with TeamTag(Player), awake.</summary>
        private Health MakePlayer(float damage) {
            var go = new GameObject("B062_Player");
            _created.Add(go);
            go.AddComponent<TeamTag>().team = Team.Player;
            var h = go.AddComponent<Health>();
            var awake = typeof(Health).GetMethod("Awake", Private);
            Assert.IsNotNull(awake, "Health.Awake not found by reflection");
            awake.Invoke(h, null);
            var field = typeof(Health).GetField("currentDamage", Private);
            Assert.IsNotNull(field, "Health.currentDamage not found by reflection");
            field.SetValue(h, damage);
            Assert.AreEqual(damage, h.CurrentDamagePercent, 1e-4f);
            return h;
        }

        private HealthBalloon MakeBalloon() {
            var balloon = HealthBalloon.Spawn(new Vector3(0f, 2f, 0f));
            _created.Add(balloon.gameObject);
            return balloon;
        }

        private static void Collect(HealthBalloon balloon, Health player) {
            var collect = typeof(HealthBalloon).GetMethod("Collect", Private);
            Assert.IsNotNull(collect, "HealthBalloon.Collect not found by reflection");
            collect.Invoke(balloon, new object[] { player.GetComponent<TeamTag>() });
        }

        [Test]
        public void Balance_HealthBalloonRestore_IsAFullRepair() {
            var balance = GameBalance.Get();
            Assume.That(balance != null, "Balance.json not loadable");
            Assert.GreaterOrEqual(balance.healthBalloonRestore, 100f, "GDD: a balloon restores all health");
            Assert.AreEqual(100f, HealthBalloon.FallbackRestore, "the code fallback is a full repair too");
        }

        [Test]
        public void Collect_AtHeavyDamage_TakesDamageToZero_AndIsUsedUp() {
            var player = MakePlayer(95f);
            float reported = -1f;
            System.Action<float> onChanged = d => reported = d;
            GameEvents.PlayerDamageChanged += onChanged;
            try {
                var balloon = MakeBalloon();
                Collect(balloon, player);
                Assert.AreEqual(0f, player.CurrentDamagePercent, 1e-4f, "one balloon fully repairs the plane");
                Assert.AreEqual(0f, reported, 1e-4f, "the HUD hears the drop straight to 0%");
                Assert.IsFalse(player.IsDead);
                Assert.IsTrue(balloon.IsDead, "the balloon is collected");
                Assert.IsFalse(balloon.gameObject.activeSelf, "the balloon is recycled");
            } finally {
                GameEvents.PlayerDamageChanged -= onChanged;
            }
        }

        [Test]
        public void Collect_AtFullHealth_StaysAtZero_AndIsStillUsedUp() {
            var player = MakePlayer(0f);
            var balloon = MakeBalloon();
            Collect(balloon, player);
            Assert.AreEqual(0f, player.CurrentDamagePercent, 1e-4f, "a heal never goes below 0% damage");
            Assert.IsTrue(balloon.IsDead, "a balloon taken at full health is still consumed");
            Assert.IsFalse(balloon.gameObject.activeSelf);
        }

        [Test]
        public void HealSource_IsNotScaledByResistance_AndClampsAtZero() {
            Assert.AreEqual(-100f, Health.ApplyPlayerResistance(-100f, DamageSource.Heal, 1.5f), 1e-4f,
                "player resistance never shrinks a heal");
            var player = MakePlayer(40f);
            player.ApplyDamage(-250f, DamageSource.Heal);
            Assert.AreEqual(0f, player.CurrentDamagePercent, 1e-4f, "an oversized heal clamps at 0% damage");
        }
    }
}
