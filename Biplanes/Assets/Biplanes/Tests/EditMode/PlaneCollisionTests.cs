using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Missions;
using Biplanes.Player;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// B025: mid-air contact with an enemy fighter or bomber destroys the enemy and costs the
    /// player damageTaken.planeCollision (50%) instead of an instant crash. Terrain and static
    /// obstacles stay fatal.
    ///
    /// EditMode doesn't run Awake/Start/FixedUpdate or physics callbacks, so the tests invoke
    /// PlayerPlane.OnTriggerEnter2D, FixedUpdate and TickPlaneCollisionGrace by reflection,
    /// and run the enemies' Awake by reflection (as EnemyFireTests does).
    /// </summary>
    public class PlaneCollisionTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<Camera> _disabledCameras = new List<Camera>();
        private readonly List<string> _enemyDestroyed = new List<string>();
        private PlayerPlane _plane;

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
            var camGo = new GameObject("B025_TestCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.aspect = 16f / 9f;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            _created.Add(camGo);

            _enemyDestroyed.Clear();
            GameEvents.EntityDestroyed += OnEntityDestroyed;

            // T941 (QA): a crash reaches GameManager.Instance, whose lazy getter calls
            // DontDestroyOnLoad and throws outside Play mode. Seed an un-awakened instance in
            // Playing, as MissionEndRulesTests does, so the crash tests don't error.
            _timeScale = Time.timeScale;
            var gmGo = new GameObject("B025_GameManager");
            _created.Add(gmGo);
            _gm = gmGo.AddComponent<GameManager>();
            typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, _gm);
            _gm.SetState(GameManager.GameState.Playing);
        }

        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private GameManager _gm;
        private float _timeScale;

        [TearDown]
        public void TearDown() {
            GameEvents.EntityDestroyed -= OnEntityDestroyed;

            if (_plane != null) Object.DestroyImmediate(_plane.gameObject);
            _plane = null;
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

        /// <summary>An airborne player plane at `position`, built by PlayerPlane.Create.</summary>
        private PlayerPlane MakePlane(Vector2 position) {
            _plane = PlayerPlane.Create(new Vector3(position.x, position.y, 0f), false);
            InvokeAwake(_plane.GetComponent<Health>());
            _plane.transform.position = new Vector3(position.x, position.y, 0f);
            _plane.GetComponent<Rigidbody2D>().position = position;
            Physics2D.SyncTransforms();
            return _plane;
        }

        private Health PlayerHealth => _plane.GetComponent<Health>();

        private FighterAI MakeFighter(Vector2 position) {
            var f = FighterAI.Spawn(position);
            _created.Add(f.gameObject);
            InvokeAwake(f.GetComponent<Health>());
            InvokeAwake(f);
            return f;
        }

        private BomberAI MakeBomber(Vector2 position) {
            var b = BomberAI.Spawn(position);
            _created.Add(b.gameObject);
            InvokeAwake(b.GetComponent<Health>());
            InvokeAwake(b);
            return b;
        }

        private void Touch(Collider2D enemyCollider) {
            Assert.IsNotNull(enemyCollider, "enemy has no Collider2D");
            Invoke(_plane, "OnTriggerEnter2D", enemyCollider);
        }

        private void PassGrace() {
            Invoke(_plane, "TickPlaneCollisionGrace", PlayerPlane.PlaneCollisionGraceSeconds + 0.1f);
        }

        private static float PlaneCollisionPercent =>
            DamageTakenData.PlaneCollisionOrDefault(GameBalance.Get() != null ? GameBalance.Get().damageTaken : null);

        // ── Balance ─────────────────────────────────────────────────────

        [Test]
        public void Balance_PlaneCollisionIs50_DefaultIs50WhenMissing() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.damageTaken != null, "Balance.json not loadable");
            Assert.AreEqual(50f, g.damageTaken.planeCollision, 1e-4f, "Balance.json damageTaken.planeCollision");
            Assert.AreEqual(50f, DamageTakenData.PlaneCollisionOrDefault(new DamageTakenData()), 1e-4f, "missing -> 50");
            Assert.AreEqual(50f, DamageTakenData.PlaneCollisionOrDefault(null), 1e-4f, "no balance -> 50");
        }

        [Test]
        public void PlaneCollisionSource_IsFatalForEnemies() {
            var go = new GameObject("fighter");
            _created.Add(go);
            go.AddComponent<TeamTag>().team = Team.Enemy;
            var h = go.AddComponent<Health>();
            InvokeAwake(h);
            h.SetMaxHitPoints(200f);
            h.ApplyDamage(1f, DamageSource.PlaneCollision);
            Assert.IsTrue(h.IsDead);
        }

        // ── Mid-air contact ─────────────────────────────────────────────

        [Test]
        public void FighterContact_At0_Takes50_NotCrashed_FighterDestroyed() {
            MakePlane(new Vector2(0f, 1f));
            var fighter = MakeFighter(new Vector2(0.5f, 1f));

            Touch(fighter.GetComponent<Collider2D>());

            Assert.AreEqual(PlaneCollisionPercent, PlayerHealth.CurrentDamagePercent, 1e-4f, "player takes planeCollision");
            Assert.AreEqual(50f, PlayerHealth.CurrentDamagePercent, 1e-4f);
            Assert.IsFalse(_plane.HasCrashed, "50% is survivable");
            Assert.IsTrue(fighter.IsDead, "the fighter is destroyed");
            CollectionAssert.AreEqual(new[] { "fighter" }, _enemyDestroyed, "EntityDestroyed once, with the score key");
        }

        [Test]
        public void SecondContact_AfterGrace_Reaches100_AndCrashes() {
            MakePlane(new Vector2(0f, 1f));
            Touch(MakeFighter(new Vector2(0.5f, 1f)).GetComponent<Collider2D>());
            Assert.IsFalse(_plane.HasCrashed);

            PassGrace();
            var second = MakeFighter(new Vector2(0.5f, 1f));
            Touch(second.GetComponent<Collider2D>());

            Assert.AreEqual(100f, PlayerHealth.CurrentDamagePercent, 1e-4f);
            Assert.IsTrue(_plane.HasCrashed, "100% total damage crashes the plane");
            Assert.IsTrue(second.IsDead);
            Assert.AreEqual(2, _enemyDestroyed.Count);
        }

        [Test]
        public void SameFighter_ReEntry_DoesNotDamageTwice() {
            MakePlane(new Vector2(0f, 1f));
            var col = MakeFighter(new Vector2(0.5f, 1f)).GetComponent<Collider2D>();

            Touch(col);
            PassGrace();
            Touch(col);

            Assert.AreEqual(50f, PlayerHealth.CurrentDamagePercent, 1e-4f, "a dead enemy can't hit again");
            Assert.IsFalse(_plane.HasCrashed);
            Assert.AreEqual(1, _enemyDestroyed.Count);
        }

        [Test]
        public void SecondEnemy_WithinGrace_IsDestroyed_PlayerNotDamagedAgain() {
            MakePlane(new Vector2(0f, 1f));
            Touch(MakeFighter(new Vector2(0.5f, 1f)).GetComponent<Collider2D>());
            var second = MakeFighter(new Vector2(0.6f, 1f));
            Touch(second.GetComponent<Collider2D>());

            Assert.AreEqual(50f, PlayerHealth.CurrentDamagePercent, 1e-4f, "one contact costs 50% once");
            Assert.IsFalse(_plane.HasCrashed);
            Assert.IsTrue(second.IsDead, "enemies touched during the grace window still die");
        }

        [Test]
        public void BomberContact_AllHitboxes_Takes50Once_BomberDestroyed() {
            MakePlane(new Vector2(0f, 1f));
            var bomber = MakeBomber(new Vector2(0.5f, 1f));
            var hitboxes = bomber.GetComponents<Collider2D>();
            Assert.Greater(hitboxes.Length, 1, "the bomber has several hitboxes");

            foreach (var col in hitboxes) Touch(col);

            Assert.AreEqual(50f, PlayerHealth.CurrentDamagePercent, 1e-4f);
            Assert.IsFalse(_plane.HasCrashed);
            Assert.IsTrue(bomber.GetComponent<Health>().IsDead, "a collision destroys the bomber, as before");
            CollectionAssert.AreEqual(new[] { "bomber" }, _enemyDestroyed);
        }

        // ── Still fatal ─────────────────────────────────────────────────

        [Test]
        public void TerrainContact_StillCrashes() {
            var terrain = TerrainStrip.Create(null, 100f);
            _created.Add(terrain.gameObject);
            MakePlane(new Vector2(0f, TerrainStrip.GroundBaseY));

            Invoke(_plane, "FixedUpdate");

            Assert.AreEqual(100f, PlayerHealth.CurrentDamagePercent, 1e-4f);
            Assert.IsTrue(_plane.HasCrashed, "terrain contact is an instant crash");
        }

        [Test]
        public void TerrainContact_At50_StillCrashes() {
            var terrain = TerrainStrip.Create(null, 100f);
            _created.Add(terrain.gameObject);
            MakePlane(new Vector2(0f, TerrainStrip.GroundBaseY));
            PlayerHealth.ApplyDamage(1f, DamageSource.PlaneCollision);
            Assert.IsFalse(_plane.HasCrashed);

            Invoke(_plane, "FixedUpdate");

            Assert.IsTrue(_plane.HasCrashed);
        }

        [Test]
        public void BuildingContact_StillCrashes() {
            MakePlane(new Vector2(0f, 1f));
            var building = StaticObstacle.Spawn(new Vector3(0f, 1f, 0f), "house");
            _created.Add(building.gameObject);

            Invoke(building, "OnTriggerEnter2D", _plane.GetComponent<Collider2D>());
            Invoke(_plane, "FixedUpdate");

            Assert.AreEqual(100f, PlayerHealth.CurrentDamagePercent, 1e-4f);
            Assert.IsTrue(_plane.HasCrashed, "buildings stay fatal");
        }

        [Test]
        public void ForceCrash_ZeppelinRam_StillCrashesAtOnce() {
            MakePlane(new Vector2(0f, 1f));
            _plane.ForceCrash();
            Assert.IsTrue(_plane.HasCrashed);
        }
    }
}
