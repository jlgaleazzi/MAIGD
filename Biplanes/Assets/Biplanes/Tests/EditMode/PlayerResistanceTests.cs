using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;

namespace Biplanes.Tests {

    /// <summary>
    /// B052: Balance player.resistance divides every non-fatal hit on the player.
    /// Collision stays fatal, heals are unchanged, enemies are untouched, and a missing
    /// or non-positive resistance behaves as 1.
    /// </summary>
    public class PlayerResistanceTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const float Eps = 1e-3f;
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

        private static GameBalance Balance() {
            var g = GameBalance.Get();
            Assert.IsNotNull(g, "Balance.json must load");
            Assert.IsNotNull(g.player);
            Assert.IsNotNull(g.damageTaken);
            return g;
        }

        // ── Balance ──────────────────────────────────────────────────────

        [Test]
        public void Balance_PlayerResistance_Is1_5() {
            var g = Balance();
            Assert.AreEqual(1.5f, g.player.resistance, 1e-5f);
            Assert.AreEqual(1.5f, PlayerData.ResistanceOrDefault(g.player), 1e-5f);
        }

        [Test]
        public void ResistanceOrDefault_MissingOrNonPositive_IsOne() {
            Assert.AreEqual(1f, PlayerData.ResistanceOrDefault(null), 1e-6f, "no player section");
            Assert.AreEqual(1f, PlayerData.ResistanceOrDefault(new PlayerData()), 1e-6f, "key missing (0)");
            Assert.AreEqual(1f, PlayerData.ResistanceOrDefault(new PlayerData { resistance = -2f }), 1e-6f);
            Assert.AreEqual(2f, PlayerData.ResistanceOrDefault(new PlayerData { resistance = 2f }), 1e-6f);
        }

        [Test]
        public void ResistanceOrDefault_ParsedFromJsonWithoutKey_IsOne() {
            var data = JsonUtility.FromJson<PlayerData>("{\"speed\":5}");
            Assert.AreEqual(1f, PlayerData.ResistanceOrDefault(data), 1e-6f);
        }

        // ── Pure helper ──────────────────────────────────────────────────

        [Test]
        public void ApplyPlayerResistance_NonFatalSources_DividedByResistance() {
            var sources = new[] {
                DamageSource.Bullet, DamageSource.Bomb, DamageSource.Flak,
                DamageSource.ZeppelinTurret, DamageSource.PlaneCollision
            };
            foreach (var src in sources) {
                Assert.AreEqual(10f / 1.5f, Health.ApplyPlayerResistance(10f, src, 1.5f), 1e-5f, src.ToString());
            }
        }

        [Test]
        public void ApplyPlayerResistance_CollisionAndHeal_Unchanged() {
            Assert.AreEqual(100f, Health.ApplyPlayerResistance(100f, DamageSource.Collision, 1.5f), 1e-6f);
            Assert.AreEqual(-30f, Health.ApplyPlayerResistance(-30f, DamageSource.Heal, 1.5f), 1e-6f);
            Assert.AreEqual(-30f, Health.ApplyPlayerResistance(-30f, DamageSource.Bullet, 1.5f), 1e-6f,
                "a negative amount is never scaled");
            Assert.AreEqual(0f, Health.ApplyPlayerResistance(0f, DamageSource.Bullet, 1.5f), 1e-6f);
        }

        [Test]
        public void ApplyPlayerResistance_NonPositiveResistance_BehavesAsOne() {
            Assert.AreEqual(12f, Health.ApplyPlayerResistance(12f, DamageSource.Flak, 0f), 1e-6f);
            Assert.AreEqual(12f, Health.ApplyPlayerResistance(12f, DamageSource.Flak, -1f), 1e-6f);
            Assert.AreEqual(12f, Health.ApplyPlayerResistance(12f, DamageSource.Flak, 1f), 1e-6f);
        }

        // ── Player Health with live Balance ──────────────────────────────

        private void AssertPlayerHit(DamageSource src, float expectedRaw, float expectedPercent) {
            var g = Balance();
            float r = PlayerData.ResistanceOrDefault(g.player);
            var player = MakeHealth("player_" + src, Team.Player, 0f);
            player.ApplyDamage(1f, src);
            Assert.AreEqual(expectedRaw / r, player.CurrentDamagePercent, Eps, src + ": damageTaken / resistance");
            Assert.AreEqual(expectedPercent, player.CurrentDamagePercent, 0.01f, src + ": expected percent");
            Assert.IsFalse(player.IsDead);
        }

        [Test]
        public void Player_Bullet_FighterBulletDividedByResistance() {
            AssertPlayerHit(DamageSource.Bullet, Balance().damageTaken.fighterBullet, 5f / 1.5f);
        }

        [Test]
        public void Player_Bomb_BomberGunnerDividedByResistance() {
            AssertPlayerHit(DamageSource.Bomb, Balance().damageTaken.bomberGunner, 10f / 1.5f);
        }

        [Test]
        public void Player_Flak_DividedByResistance() {
            AssertPlayerHit(DamageSource.Flak, Balance().damageTaken.flak, 10f);
        }

        [Test]
        public void Player_ZeppelinTurret_DividedByResistance() {
            AssertPlayerHit(DamageSource.ZeppelinTurret, Balance().damageTaken.zeppelinTurret, 8f);
        }

        [Test]
        public void Player_PlaneCollision_DividedByResistance() {
            AssertPlayerHit(DamageSource.PlaneCollision,
                DamageTakenData.PlaneCollisionOrDefault(Balance().damageTaken), 50f / 1.5f);
        }

        [Test]
        public void Player_ThreePlaneCollisions_Kill_DespiteFloatRounding() {
            var player = MakeHealth("player_rams", Team.Player, 0f);
            player.ApplyDamage(1f, DamageSource.PlaneCollision);
            player.ApplyDamage(1f, DamageSource.PlaneCollision);
            Assert.IsFalse(player.IsDead, "two rams: ~66.7%");
            player.ApplyDamage(1f, DamageSource.PlaneCollision);
            Assert.AreEqual(100f, player.CurrentDamagePercent, 1e-6f);
            Assert.IsTrue(player.IsDead, "3 x (50 / 1.5) must reach exactly 100");
        }

        [Test]
        public void Player_ThirtyFighterBullets_Kill_DespiteFloatRounding() {
            var g = Balance();
            var player = MakeHealth("player_bullets", Team.Player, 0f);
            float perHit = g.damageTaken.fighterBullet / PlayerData.ResistanceOrDefault(g.player);
            int needed = Mathf.RoundToInt(100f / perHit);
            for (int i = 0; i < needed - 1; i++) player.ApplyDamage(1f, DamageSource.Bullet);
            Assert.IsFalse(player.IsDead, (needed - 1) + " hits leave the player alive");
            player.ApplyDamage(1f, DamageSource.Bullet);
            Assert.IsTrue(player.IsDead, needed + " hits kill");
        }

        [Test]
        public void Player_Collision_StillFatal() {
            var player = MakeHealth("player_crash", Team.Player, 0f);
            player.ApplyDamage(1f, DamageSource.Collision);
            Assert.AreEqual(100f, player.CurrentDamagePercent, 1e-4f);
            Assert.IsTrue(player.IsDead, "terrain / building / ram crash stays instant");

            var obstacle = MakeHealth("player_obstacle", Team.Player, 0f);
            obstacle.ApplyDamage(100f, DamageSource.Collision); // StaticObstacle path
            Assert.IsTrue(obstacle.IsDead);
        }

        [Test]
        public void Player_Heal_Unchanged() {
            var g = Balance();
            var player = MakeHealth("player_heal", Team.Player, 0f);
            player.ApplyDamage(1f, DamageSource.Flak);
            player.ApplyDamage(1f, DamageSource.Flak);
            player.ApplyDamage(1f, DamageSource.Flak);
            float hurt = player.CurrentDamagePercent;
            player.ApplyDamage(-5f, DamageSource.Heal);
            Assert.AreEqual(Mathf.Max(0f, hurt - 5f), player.CurrentDamagePercent, Eps, "ApplyDamage(Heal) is 1:1");
            float before = player.CurrentDamagePercent;
            player.Heal(g.healthBalloonRestore);
            Assert.AreEqual(Mathf.Max(0f, before - g.healthBalloonRestore), player.CurrentDamagePercent, Eps,
                "Heal() is 1:1");
        }

        [Test]
        public void Player_SmokeThreshold_ReachedLaterButStillReached() {
            var g = Balance();
            var player = MakeHealth("player_smoke", Team.Player, 0f);
            int hits = 0;
            while (player.CurrentDamagePercent < g.player.smokeThreshold && hits < 100) {
                player.ApplyDamage(1f, DamageSource.Flak);
                hits++;
            }
            int raw = Mathf.CeilToInt(g.player.smokeThreshold / g.damageTaken.flak);
            Assert.GreaterOrEqual(hits, raw, "resistance never makes smoke come sooner");
            Assert.Less(player.CurrentDamagePercent, 100f, "smoke reached before death");
        }

        // ── Enemies untouched ────────────────────────────────────────────

        [Test]
        public void Enemy_Damage_NotScaledByResistance() {
            var fighter = MakeHealth("fighter", Team.Enemy, 200f);
            fighter.ApplyDamage(10f, DamageSource.Bullet);
            Assert.AreEqual(5f, fighter.CurrentDamagePercent, 1e-4f, "10 of 200 hp = 5%");
            fighter.ApplyDamage(20f, DamageSource.Bomb);
            Assert.AreEqual(15f, fighter.CurrentDamagePercent, 1e-4f);

            var rammed = MakeHealth("bomber", Team.Enemy, 200f);
            rammed.ApplyDamage(1f, DamageSource.PlaneCollision);
            Assert.IsTrue(rammed.IsDead, "a plane collision still kills the enemy");
        }
    }
}
