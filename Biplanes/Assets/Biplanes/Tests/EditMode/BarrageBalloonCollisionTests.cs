using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Player;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// B072: flying into a barrage balloon is no longer fatal. The balloon pops (EntityDestroyed
    /// "barrageBalloon", so it scores and counts like a shot-down one) and the player takes
    /// damageTaken.barrageBalloonCollision (50) through DamageSource.PlaneCollision, divided by
    /// player.resistance (1.5) = 33.3%, once per PlaneCollisionGraceSeconds. Balloon health is
    /// enemyHealth.barrageBalloon 100 (four 25-point bullets, one bomb).
    ///
    /// EditMode doesn't run Awake or physics callbacks: they are invoked by reflection, as in
    /// PlaneCollisionTests.
    /// </summary>
    public class BarrageBalloonCollisionTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<Camera> _disabledCameras = new List<Camera>();
        private readonly List<string> _enemyDestroyed = new List<string>();
        private PlayerPlane _plane;
        private GameManager _gm;
        private float _timeScale;

        private void OnEntityDestroyed(string type, Team team) {
            if (team == Team.Enemy) _enemyDestroyed.Add(type);
        }

        [SetUp]
        public void SetUp() {
            _disabledCameras.Clear();
            foreach (var c in Camera.allCameras) {
                if (c != null && c.CompareTag("MainCamera")) {
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
            }
            var camGo = new GameObject("B072_TestCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.aspect = 16f / 9f;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            _created.Add(camGo);

            _enemyDestroyed.Clear();
            GameEvents.EntityDestroyed += OnEntityDestroyed;

            // A crash reaches GameManager.Instance; seed an un-awakened one (PlaneCollisionTests).
            _timeScale = Time.timeScale;
            var gmGo = new GameObject("B072_GameManager");
            _created.Add(gmGo);
            _gm = gmGo.AddComponent<GameManager>();
            typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, _gm);
            _gm.SetState(GameManager.GameState.Playing);
        }

        [TearDown]
        public void TearDown() {
            GameEvents.EntityDestroyed -= OnEntityDestroyed;

            if (_plane != null) Object.DestroyImmediate(_plane.gameObject);
            _plane = null;
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();

            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (p != null) Object.DestroyImmediate(p.gameObject);
            }
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go != null && go.hideFlags == HideFlags.HideAndDontSave && go.name.StartsWith("Pool<")) {
                    Object.DestroyImmediate(go);
                }
            }

            foreach (var c in _disabledCameras) {
                if (c != null) c.enabled = true;
            }
            _disabledCameras.Clear();

            typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, null);
            _gm = null;
            Time.timeScale = _timeScale;
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static void InvokeAwake(Component c) {
            var awake = c.GetType().GetMethod("Awake", Private);
            Assert.IsNotNull(awake, $"{c.GetType().Name}.Awake not found by reflection");
            awake.Invoke(c, null);
        }

        private static void Invoke(object target, string method, params object[] args) {
            var m = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found by reflection");
            m.Invoke(target, args);
        }

        private PlayerPlane MakePlane(Vector2 position) {
            _plane = PlayerPlane.Create(new Vector3(position.x, position.y, 0f), false);
            InvokeAwake(_plane.GetComponent<Health>());
            _plane.transform.position = new Vector3(position.x, position.y, 0f);
            _plane.GetComponent<Rigidbody2D>().position = position;
            Physics2D.SyncTransforms();
            return _plane;
        }

        private Health PlayerHealth => _plane.GetComponent<Health>();

        private BarrageBalloon MakeBalloon(Vector2 position) {
            var b = BarrageBalloon.Spawn(position);
            _created.Add(b.gameObject);
            InvokeAwake(b.GetComponent<Health>());
            InvokeAwake(b);
            return b;
        }

        private void Touch(Collider2D c) {
            Assert.IsNotNull(c, "no Collider2D");
            Invoke(_plane, "OnTriggerEnter2D", c);
        }

        private void PassGrace() {
            Invoke(_plane, "TickPlaneCollisionGrace", PlayerPlane.PlaneCollisionGraceSeconds + 0.1f);
        }

        // ── Balance ─────────────────────────────────────────────────────

        [Test]
        public void Balance_BalloonHealth100_AndCollision50_WithMatchingDefaults() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.enemyHealth != null && g.damageTaken != null, "Balance.json not loadable");
            Assert.AreEqual(100f, g.enemyHealth.barrageBalloon, 1e-4f, "enemyHealth.barrageBalloon");
            Assert.AreEqual(50f, g.damageTaken.barrageBalloonCollision, 1e-4f, "damageTaken.barrageBalloonCollision");

            Assert.AreEqual(100f, BarrageBalloon.MaxHitPointsFor(null), 1e-4f, "no balance -> 100");
            Assert.AreEqual(100f, EnemyHealthData.BarrageBalloonOrDefault(new EnemyHealthData()), 1e-4f, "missing -> 100");
            Assert.AreEqual(50f, DamageTakenData.BarrageBalloonCollisionOrDefault(null), 1e-4f, "no balance -> 50");
            Assert.AreEqual(50f, DamageTakenData.BarrageBalloonCollisionOrDefault(new DamageTakenData()), 1e-4f, "missing -> 50");
            Assert.AreEqual(1f, PlayerPlane.BarrageBalloonCollisionScale(null), 1e-4f, "defaults: 50 / 50");
        }

        [Test]
        public void Balloon_Takes4Bullets_Or1Bomb() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.player != null, "Balance.json not loadable");

            var a = MakeBalloon(new Vector2(0f, 2f));
            var ha = a.GetComponent<Health>();
            Assert.AreEqual(100f, ha.MaxHitPoints, 1e-4f);
            for (int i = 0; i < 3; i++) ha.ApplyDamage(g.player.bulletDamage, DamageSource.Bullet);
            Assert.IsFalse(ha.IsDead, "three bullets leave the balloon alive");
            ha.ApplyDamage(g.player.bulletDamage, DamageSource.Bullet);
            Assert.IsTrue(ha.IsDead, "the fourth bullet pops it");

            var b = MakeBalloon(new Vector2(3f, 2f));
            var hb = b.GetComponent<Health>();
            hb.ApplyDamage(g.player.bombDamage, DamageSource.Bomb);
            Assert.IsTrue(hb.IsDead, "one bomb pops it");

            CollectionAssert.AreEqual(new[] { "barrageBalloon", "barrageBalloon" }, _enemyDestroyed);
        }

        // ── Collision ───────────────────────────────────────────────────

        [Test]
        public void BalloonContact_Costs33Percent_PopsBalloon_PlaneKeepsFlying() {
            MakePlane(new Vector2(0f, 1f));
            var balloon = MakeBalloon(new Vector2(0.5f, 1f));

            Touch(balloon.GetComponent<Collider2D>());

            Assert.AreEqual(100f / 3f, PlayerHealth.CurrentDamagePercent, 1e-3f, "50 / 1.5 = 33.3%");
            Assert.IsFalse(_plane.HasCrashed, "a balloon is no longer fatal");
            Assert.IsTrue(balloon.IsDead, "the balloon is destroyed");
            CollectionAssert.AreEqual(new[] { "barrageBalloon" }, _enemyDestroyed,
                "EntityDestroyed once with the score key: scores and counts like a shot kill");
        }

        [Test]
        public void SameBalloon_ReEntry_AfterGrace_DoesNotDamageAgain() {
            MakePlane(new Vector2(0f, 1f));
            var col = MakeBalloon(new Vector2(0.5f, 1f)).GetComponent<Collider2D>();

            Touch(col);
            Touch(col);
            PassGrace();
            Touch(col);

            Assert.AreEqual(100f / 3f, PlayerHealth.CurrentDamagePercent, 1e-3f, "one contact, one hit");
            Assert.AreEqual(1, _enemyDestroyed.Count);
        }

        [Test]
        public void SecondBalloon_WithinGrace_Pops_PlayerNotDamagedAgain() {
            MakePlane(new Vector2(0f, 1f));
            Touch(MakeBalloon(new Vector2(0.5f, 1f)).GetComponent<Collider2D>());
            var second = MakeBalloon(new Vector2(0.6f, 1f));
            Touch(second.GetComponent<Collider2D>());

            Assert.AreEqual(100f / 3f, PlayerHealth.CurrentDamagePercent, 1e-3f);
            Assert.IsTrue(second.IsDead, "balloons touched during the grace window still pop");
            Assert.AreEqual(2, _enemyDestroyed.Count);
        }

        [Test]
        public void ThirdBalloon_AfterGrace_Reaches100_AndCrashes() {
            MakePlane(new Vector2(0f, 1f));
            for (int i = 0; i < 2; i++) {
                Touch(MakeBalloon(new Vector2(0.5f, 1f)).GetComponent<Collider2D>());
                PassGrace();
                Assert.IsFalse(_plane.HasCrashed, $"contact {i + 1} is survivable");
            }
            Touch(MakeBalloon(new Vector2(0.5f, 1f)).GetComponent<Collider2D>());

            Assert.AreEqual(100f, PlayerHealth.CurrentDamagePercent, 1e-4f);
            Assert.IsTrue(_plane.HasCrashed, "100% total crashes as usual");
        }

        [Test]
        public void BalloonContact_AfterTwoPlaneContacts_At67_Crashes() {
            MakePlane(new Vector2(0f, 1f));
            PlayerHealth.ApplyDamage(1f, DamageSource.PlaneCollision);
            PlayerHealth.ApplyDamage(1f, DamageSource.PlaneCollision);
            Assert.IsFalse(_plane.HasCrashed);

            Touch(MakeBalloon(new Vector2(0.5f, 1f)).GetComponent<Collider2D>());

            Assert.IsTrue(_plane.HasCrashed, "a balloon hit that reaches 100% crashes");
        }

        [Test]
        public void EnemyBulletOrFlak_DoNotHurtBalloon() {
            var balloon = MakeBalloon(new Vector2(0f, 2f));
            // Enemy projectiles only hit Team.Player (EnemyBullet/FlakShell.OnHit): the balloon
            // is Team.Enemy, so Projectile.OnTriggerEnter2D skips it as own team.
            Assert.AreEqual(Team.Enemy, balloon.Team);
            Assert.IsFalse(balloon.IsDead);
        }
    }
}
