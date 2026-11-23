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
    /// B069 (design call): ramming the 1000 HP bomber is no longer fatal for it. A contact
    /// takes Balance ramDamage.bomber (250) hit points off it, once per pass (the player's
    /// plane-collision grace window covers its other hitboxes), and the player takes the usual
    /// planeCollision hit. Fighters still die at once.
    ///
    /// EditMode doesn't run Awake or physics callbacks, so the tests invoke
    /// PlayerPlane.OnTriggerEnter2D and TickPlaneCollisionGrace by reflection (as
    /// PlaneCollisionTests does).
    /// </summary>
    public class BomberRamTests {

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
            var camGo = new GameObject("B069_TestCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.aspect = 16f / 9f;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            _created.Add(camGo);

            _enemyDestroyed.Clear();
            GameEvents.EntityDestroyed += OnEntityDestroyed;

            // A crash reaches GameManager.Instance: seed an un-awakened one in Playing.
            _timeScale = Time.timeScale;
            var gmGo = new GameObject("B069_GameManager");
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

        private static void Invoke(object target, string method, params object[] args) {
            var m = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found by reflection");
            m.Invoke(target, args);
        }

        private PlayerPlane MakePlane(Vector2 position) {
            _plane = PlayerPlane.Create(new Vector3(position.x, position.y, 0f), false);
            _plane.transform.position = new Vector3(position.x, position.y, 0f);
            _plane.GetComponent<Rigidbody2D>().position = position;
            Physics2D.SyncTransforms();
            return _plane;
        }

        private Health PlayerHealth => _plane.GetComponent<Health>();

        private BomberAI MakeBomber(Vector2 position) {
            var b = BomberAI.Spawn(position);
            _created.Add(b.gameObject);
            Invoke(b.GetComponent<Health>(), "Awake");
            Invoke(b, "Awake");
            return b;
        }

        private FighterAI MakeFighter(Vector2 position) {
            var f = FighterAI.Spawn(position);
            _created.Add(f.gameObject);
            Invoke(f.GetComponent<Health>(), "Awake");
            Invoke(f, "Awake");
            return f;
        }

        private void Touch(Collider2D enemyCollider) {
            Assert.IsNotNull(enemyCollider, "enemy has no Collider2D");
            Invoke(_plane, "OnTriggerEnter2D", enemyCollider);
        }

        private void PassGrace() {
            Invoke(_plane, "TickPlaneCollisionGrace", PlayerPlane.PlaneCollisionGraceSeconds + 0.1f);
        }

        private static float RamPercentOf(Health bomberHealth) {
            return PlayerPlane.RamDamageFor(GameBalance.Get()) / bomberHealth.MaxHitPoints * 100f;
        }

        private static float ContactPercent {
            get {
                var g = GameBalance.Get();
                return DamageTakenData.PlaneCollisionOrDefault(g != null ? g.damageTaken : null)
                    / PlayerData.ResistanceOrDefault(g != null ? g.player : null);
            }
        }

        // ── Balance ─────────────────────────────────────────────────────

        [Test]
        public void Balance_RamDamageBomberIs250_DefaultWhenMissing() {
            var g = GameBalance.Get();
            Assume.That(g != null, "Balance.json not loadable");
            Assert.IsNotNull(g.ramDamage, "Balance.json has a ramDamage block");
            Assert.AreEqual(250f, g.ramDamage.bomber, 1e-4f);
            Assert.AreEqual(250f, PlayerPlane.RamDamageFor(g), 1e-4f);
            Assert.AreEqual(RamDamageData.DefaultBomber, PlayerPlane.RamDamageFor(null));
            Assert.AreEqual(RamDamageData.DefaultBomber, RamDamageData.BomberOrDefault(new RamDamageData()));
            Assert.AreEqual(250f, RamDamageData.DefaultBomber);
        }

        // ── Bomber ram ──────────────────────────────────────────────────

        [Test]
        public void OneRam_Takes250_BomberSurvives_PlayerTakesOneContact() {
            MakePlane(new Vector2(0f, 1f));
            var bomber = MakeBomber(new Vector2(0.5f, 1f));
            var h = bomber.GetComponent<Health>();

            Touch(bomber.GetComponent<Collider2D>());

            Assert.IsFalse(h.IsDead, "a ram is not fatal for the bomber any more");
            Assert.AreEqual(25f, h.CurrentDamagePercent, 1e-3f, "250 of 1000 HP");
            Assert.AreEqual(RamPercentOf(h), h.CurrentDamagePercent, 1e-3f);
            Assert.AreEqual(ContactPercent, PlayerHealth.CurrentDamagePercent, 1e-4f);
            CollectionAssert.IsEmpty(_enemyDestroyed);
        }

        [Test]
        public void SamePass_OtherHitboxesInsideGrace_NoExtraDamage() {
            MakePlane(new Vector2(0f, 1f));
            var bomber = MakeBomber(new Vector2(0.5f, 1f));
            var h = bomber.GetComponent<Health>();
            var hitboxes = bomber.GetComponents<Collider2D>();
            Assert.Greater(hitboxes.Length, 1);

            foreach (var col in hitboxes) Touch(col);
            foreach (var col in hitboxes) Touch(col);

            Assert.AreEqual(RamPercentOf(h), h.CurrentDamagePercent, 1e-3f, "one ram per contact, not per hitbox or frame");
            Assert.AreEqual(ContactPercent, PlayerHealth.CurrentDamagePercent, 1e-4f, "the player is hit once too");
        }

        [Test]
        public void FourRams_KillTheBomber_OneEvent() {
            MakePlane(new Vector2(0f, 1f));
            var bomber = MakeBomber(new Vector2(0.5f, 1f));
            var h = bomber.GetComponent<Health>();
            var col = bomber.GetComponent<Collider2D>();
            int ramsToKill = Mathf.CeilToInt(h.MaxHitPoints / PlayerPlane.RamDamageFor(GameBalance.Get()));
            Assert.AreEqual(4, ramsToKill);

            for (int i = 0; i < ramsToKill; i++) {
                Assert.IsFalse(h.IsDead, $"alive before ram {i + 1}");
                PlayerHealth.Heal(100f);   // keep the player flying between rams
                Touch(col);
                PassGrace();
            }

            Assert.IsTrue(h.IsDead, "the 4th ram empties the bomber's Health");
            CollectionAssert.AreEqual(new[] { "bomber" }, _enemyDestroyed);

            PlayerHealth.Heal(100f);
            Touch(col);   // a dead bomber is ignored
            CollectionAssert.AreEqual(new[] { "bomber" }, _enemyDestroyed);
            Assert.AreEqual(0f, PlayerHealth.CurrentDamagePercent, 1e-4f);
        }

        [Test]
        public void BomberAfterFighter_InsideGrace_NotRammed() {
            MakePlane(new Vector2(0f, 1f));
            var fighter = MakeFighter(new Vector2(0.3f, 1f));
            var bomber = MakeBomber(new Vector2(0.5f, 1f));

            Touch(fighter.GetComponent<Collider2D>());
            Touch(bomber.GetComponent<Collider2D>());

            Assert.IsTrue(fighter.IsDead);
            Assert.AreEqual(0f, bomber.GetComponent<Health>().CurrentDamagePercent, "the grace window covers the bomber too");
            Assert.AreEqual(ContactPercent, PlayerHealth.CurrentDamagePercent, 1e-4f);
        }

        // ── Fighters unchanged ──────────────────────────────────────────

        [Test]
        public void Fighter_StillDiesAtOnce_EvenInsideGrace() {
            MakePlane(new Vector2(0f, 1f));
            var bomber = MakeBomber(new Vector2(0.5f, 1f));
            var fighter = MakeFighter(new Vector2(0.3f, 1f));

            Touch(bomber.GetComponent<Collider2D>());   // starts the grace window
            Touch(fighter.GetComponent<Collider2D>());

            Assert.IsTrue(fighter.IsDead, "fighters keep the instant ram death (B025)");
            Assert.AreEqual(ContactPercent, PlayerHealth.CurrentDamagePercent, 1e-4f, "no second player hit inside grace");
            CollectionAssert.AreEqual(new[] { "fighter" }, _enemyDestroyed);
        }
    }
}
