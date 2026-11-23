using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Fx;
using Biplanes.Player;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// B079: flying into the zeppelin hull or a turret is no longer fatal. It costs the player
    /// one plane contact (damageTaken.planeCollision / resistance = 33.3%) through
    /// DamageSource.PlaneCollision, sharing PlaneCollisionGraceSeconds with every other plane
    /// contact; the zeppelin, its turrets and its shield take nothing and raise no events.
    ///
    /// EditMode doesn't run Awake or physics callbacks, so the tests invoke Awake,
    /// PlayerPlane.OnTriggerEnter2D and TickPlaneCollisionGrace by reflection (as
    /// PlaneCollisionTests does).
    /// </summary>
    public class ZeppelinRamTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<Camera> _disabledCameras = new List<Camera>();
        private readonly List<string> _destroyed = new List<string>();
        private PlayerPlane _plane;
        private GameManager _gm;
        private float _timeScale;

        private void OnEntityDestroyed(string type, Team team) { _destroyed.Add(type); }

        [SetUp]
        public void SetUp() {
            _disabledCameras.Clear();
            foreach (var c in Camera.allCameras) {
                if (c != null && c.CompareTag("MainCamera")) {
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
            }
            var camGo = new GameObject("B079_TestCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.aspect = 16f / 9f;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            _created.Add(camGo);

            _destroyed.Clear();
            GameEvents.EntityDestroyed += OnEntityDestroyed;

            var fxGo = new GameObject("B079_Fx");
            _created.Add(fxGo);
            typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, fxGo.AddComponent<FxManager>());

            // A crash reaches GameManager.Instance (lazy getter uses DontDestroyOnLoad): seed one.
            _timeScale = Time.timeScale;
            var gmGo = new GameObject("B079_GameManager");
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
                if (go == null) continue;
                bool pool = go.name == "FlakShellPool" || go.name.StartsWith("Pool<");
                bool fx = go.name == "Explosion" || go.name == "FlakBurst" || go.name == "SmokePuff";
                if ((pool || fx) && (go.scene.IsValid() || go.hideFlags == HideFlags.HideAndDontSave)) {
                    Object.DestroyImmediate(go);
                }
            }

            foreach (var c in _disabledCameras) {
                if (c != null) c.enabled = true;
            }
            _disabledCameras.Clear();

            typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, null);
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

        private ZeppelinAI MakeZeppelin(Vector2 position) {
            var z = ZeppelinAI.Spawn(new Vector3(position.x, position.y, 0f), "b079");
            _created.Add(z.gameObject);
            InvokeAwake(z);
            foreach (var t in z.GetComponentsInChildren<ZeppelinTurret>()) InvokeAwake(t);
            return z;
        }

        private FighterAI MakeFighter(Vector2 position) {
            var f = FighterAI.Spawn(position);
            _created.Add(f.gameObject);
            InvokeAwake(f.GetComponent<Health>());
            InvokeAwake(f);
            return f;
        }

        private void Touch(Collider2D c) {
            Assert.IsNotNull(c, "no Collider2D");
            Invoke(_plane, "OnTriggerEnter2D", c);
        }

        private void PassGrace() {
            Invoke(_plane, "TickPlaneCollisionGrace", PlayerPlane.PlaneCollisionGraceSeconds + 0.1f);
        }

        private Health PlayerHealth => _plane.GetComponent<Health>();

        private static float ContactPercent {
            get {
                var g = GameBalance.Get();
                return DamageTakenData.PlaneCollisionOrDefault(g != null ? g.damageTaken : null)
                     / PlayerData.ResistanceOrDefault(g != null ? g.player : null);
            }
        }

        private static void AssertZeppelinUntouched(ZeppelinAI z, int turrets) {
            Assert.IsFalse(z.IsDead, "the zeppelin is not brought down by a ram");
            Assert.AreEqual(0f, z.CurrentDamagePercent, 1e-6f, "the hull takes no ram damage");
            Assert.AreEqual(turrets, z.TurretsRemaining, "no turret is destroyed by a ram");
            Assert.IsTrue(z.IsShielded, "the shield is unchanged");
            foreach (var t in z.GetComponentsInChildren<ZeppelinTurret>()) {
                Assert.IsFalse(t.IsDead);
                Assert.AreEqual(0f, t.CurrentDamagePercent, 1e-6f, "turrets take no ram damage");
            }
        }

        // ── Tests ───────────────────────────────────────────────────────

        [Test]
        public void HullContact_CostsOneContact_NotFatal_ZeppelinUntouched() {
            MakePlane(new Vector2(0f, 2f));
            var z = MakeZeppelin(new Vector2(0.5f, 2f));
            int turrets = z.TurretsRemaining;

            Touch(z.GetComponent<Collider2D>());

            Assert.AreEqual(ContactPercent, PlayerHealth.CurrentDamagePercent, 1e-4f, "planeCollision / resistance");
            Assert.AreEqual(100f / 3f, PlayerHealth.CurrentDamagePercent, 1e-3f, "50 / 1.5 = 33.3%");
            Assert.IsFalse(_plane.HasCrashed, "a zeppelin ram is no longer an instant crash");
            AssertZeppelinUntouched(z, turrets);
            CollectionAssert.IsEmpty(_destroyed, "no EntityDestroyed from a ram");
        }

        [Test]
        public void TurretContact_CostsOneContact_TurretUntouched() {
            MakePlane(new Vector2(0f, 2f));
            var z = MakeZeppelin(new Vector2(0.5f, 2f));
            int turrets = z.TurretsRemaining;
            var turret = z.GetComponentInChildren<ZeppelinTurret>();
            Assume.That(turret != null, "zeppelin has turrets");

            Touch(turret.GetComponent<Collider2D>());

            Assert.AreEqual(ContactPercent, PlayerHealth.CurrentDamagePercent, 1e-4f);
            Assert.IsFalse(_plane.HasCrashed);
            AssertZeppelinUntouched(z, turrets);
            CollectionAssert.IsEmpty(_destroyed);
        }

        [Test]
        public void AllHullAndTurretColliders_InOnePass_CostOneContactOnly() {
            MakePlane(new Vector2(0f, 2f));
            var z = MakeZeppelin(new Vector2(0.5f, 2f));
            int turrets = z.TurretsRemaining;

            foreach (var c in z.GetComponentsInChildren<Collider2D>()) Touch(c);

            Assert.AreEqual(ContactPercent, PlayerHealth.CurrentDamagePercent, 1e-4f, "one grace window, one charge");
            Assert.IsFalse(_plane.HasCrashed);
            AssertZeppelinUntouched(z, turrets);
        }

        [Test]
        public void ThreeContacts_AfterGrace_Reach100_AndCrash() {
            MakePlane(new Vector2(0f, 2f));
            var z = MakeZeppelin(new Vector2(0.5f, 2f));
            var hull = z.GetComponent<Collider2D>();

            Touch(hull);
            PassGrace();
            Touch(hull);
            Assert.AreEqual(2f * ContactPercent, PlayerHealth.CurrentDamagePercent, 1e-3f);
            Assert.IsFalse(_plane.HasCrashed, "two contacts (66.7%) are survivable");

            PassGrace();
            Touch(hull);
            Assert.AreEqual(100f, PlayerHealth.CurrentDamagePercent, 1e-4f);
            Assert.IsTrue(_plane.HasCrashed, "the third contact crashes the plane");
            Assert.IsFalse(z.IsDead, "the zeppelin survives");
            // B081: the only event is the player's own death (Health raises EntityDestroyed with
            // the plane's name and Team.Player at 100%); the zeppelin and turrets raise nothing.
            CollectionAssert.AreEqual(new[] { _plane.gameObject.name }, _destroyed);
            CollectionAssert.DoesNotContain(_destroyed, ZeppelinAI.ScoreKey);
            CollectionAssert.DoesNotContain(_destroyed, ZeppelinTurret.ScoreKey);
        }

        [Test]
        public void GraceWindow_IsSharedWithFighters() {
            MakePlane(new Vector2(0f, 2f));
            var fighter = MakeFighter(new Vector2(0.5f, 2f));
            var z = MakeZeppelin(new Vector2(0.5f, 2f));

            Touch(fighter.GetComponent<Collider2D>());
            Touch(z.GetComponent<Collider2D>());

            Assert.AreEqual(ContactPercent, PlayerHealth.CurrentDamagePercent, 1e-4f,
                "a zeppelin contact inside a fighter's grace window costs nothing more");
            Assert.IsTrue(fighter.IsDead);

            // And the other way round: a fighter inside the zeppelin's window only dies.
            PassGrace();
            Touch(z.GetComponent<Collider2D>());
            var second = MakeFighter(new Vector2(0.5f, 2f));
            Touch(second.GetComponent<Collider2D>());
            Assert.AreEqual(2f * ContactPercent, PlayerHealth.CurrentDamagePercent, 1e-3f);
            Assert.IsTrue(second.IsDead, "fighters touched in the window still die");
            Assert.IsFalse(_plane.HasCrashed);
        }
    }
}
