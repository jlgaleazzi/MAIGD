using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Fx;
using Biplanes.Missions;
using Biplanes.Player;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// T974 (QA of B072): gap tests for the barrage balloon rework. Covers what
    /// BarrageBalloonCollisionTests doesn't: the simulated kinematic trigger body (so physics
    /// queries and bomb bursts find the balloon), real Projectile trigger paths for player and
    /// enemy fire, a fighter touch followed by a balloon in the same grace window, score and
    /// objective counting exactly once, the mission 4 balloon objective, and the death cleanup
    /// (explosion, deactivate, tether removed).
    ///
    /// EditMode runs no Awake or physics callbacks: they are invoked by reflection.
    /// </summary>
    public class BarrageBalloonQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<Camera> _disabledCameras = new List<Camera>();
        private readonly List<string> _enemyDestroyed = new List<string>();
        private PlayerPlane _plane;
        private GameManager _gm;
        private float _timeScale;
        private object _prevScoreKeeper;
        private ScoreKeeper _score;
        private GameObject _fxGo;

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
            var camGo = new GameObject("T974_TestCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.aspect = 16f / 9f;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            _created.Add(camGo);

            _enemyDestroyed.Clear();
            GameEvents.EntityDestroyed += OnEntityDestroyed;

            _timeScale = Time.timeScale;
            var gmGo = new GameObject("T974_GameManager");
            _created.Add(gmGo);
            _gm = gmGo.AddComponent<GameManager>();
            typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, _gm);
            _gm.SetState(GameManager.GameState.Playing);

            _prevScoreKeeper = typeof(ScoreKeeper).GetField("_instance", PrivateStatic).GetValue(null);
        }

        [TearDown]
        public void TearDown() {
            GameEvents.EntityDestroyed -= OnEntityDestroyed;

            if (_score != null) {
                typeof(ScoreKeeper).GetMethod("OnDestroy", Private)?.Invoke(_score, null);
                Object.DestroyImmediate(_score.gameObject);
            }
            _score = null;
            typeof(ScoreKeeper).GetField("_instance", PrivateStatic).SetValue(null, _prevScoreKeeper);

            typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, null);
            if (_fxGo != null) Object.DestroyImmediate(_fxGo);
            _fxGo = null;

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
                bool pool = go.name.StartsWith("Pool<");
                bool fx = go.name == "Explosion" || go.name == "FlakBurst" || go.name == "SmokePuff";
                if ((pool || fx) && (go.scene.IsValid() || go.hideFlags == HideFlags.HideAndDontSave)) {
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

        private ScoreKeeper MakeScoreKeeper() {
            typeof(ScoreKeeper).GetField("_instance", PrivateStatic).SetValue(null, null);
            var go = new GameObject("T974_ScoreKeeper");
            _score = go.AddComponent<ScoreKeeper>();
            InvokeAwake(_score);   // sets the singleton and subscribes to EntityDestroyed
            _score.ResetScore();
            return _score;
        }

        private T MakeProjectile<T>(Vector2 at) where T : Projectile {
            var go = new GameObject("T974_" + typeof(T).Name);
            _created.Add(go);
            go.transform.position = at;
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<CircleCollider2D>().isTrigger = true;
            var p = go.AddComponent<T>();
            InvokeAwake(p);
            return p;
        }

        private static void ProjectileTrigger(Projectile p, Collider2D other) {
            var m = typeof(Projectile).GetMethod("OnTriggerEnter2D", Private);
            Assert.IsNotNull(m, "Projectile.OnTriggerEnter2D not found by reflection");
            m.Invoke(p, new object[] { other });
        }

        private static MissionData LoadMission(int index) {
            var text = Resources.Load<TextAsset>($"Missions/mission_{index:00}");
            Assert.IsNotNull(text, $"mission_{index:00}.json in Resources/Missions");
            return JsonUtility.FromJson<MissionData>(text.text);
        }

        // ── Body: simulated kinematic trigger (the B072 physics fix) ────────

        [Test]
        public void Balloon_IsSimulatedKinematicTrigger_NeverABlockingBody() {
            var b = MakeBalloon(new Vector2(0f, 2f));
            var rb = b.GetComponent<Rigidbody2D>();
            Assert.IsNotNull(rb);
            Assert.IsTrue(rb.simulated, "simulated: colliders are in the physics world (B072)");
            Assert.AreEqual(RigidbodyType2D.Kinematic, rb.bodyType, "kinematic: never pushed by or pushing planes");
            foreach (var c in b.GetComponents<Collider2D>())
                Assert.IsTrue(c.isTrigger, "every balloon collider is a trigger, so it blocks nothing");
            Assert.AreEqual(Team.Enemy, b.Team);
        }

        [Test]
        public void Balloon_AfterAwake_IsFoundByPhysicsQueries() {
            var b = MakeBalloon(new Vector2(5f, 2f));
            Physics2D.SyncTransforms();
            var col = b.GetComponent<Collider2D>();
            var filter = new ContactFilter2D { useTriggers = true };
            var hits = new Collider2D[8];
            int n = Physics2D.OverlapCircle(new Vector2(5f, 2f), 0.3f, filter, hits);
            bool found = false;
            for (int i = 0; i < n; i++) if (hits[i] == col) found = true;
            Assert.IsTrue(found, "a simulated balloon collider is visible to OverlapCircle (bomb bursts)");
        }

        [Test]
        public void BombBurst_AtBalloon_PopsIt_Once() {
            var b = MakeBalloon(new Vector2(8f, 1f));
            Physics2D.SyncTransforms();

            var bomb = MakeProjectile<Bomb>(new Vector2(8f, 1f));
            var g = GameBalance.Get();
            Assume.That(g != null && g.player != null, "Balance.json not loadable");
            typeof(Projectile).GetField("damage", Private).SetValue(bomb, g.player.bombDamage);
            var burst = typeof(Bomb).GetMethod("BurstDamage", Private);
            Assert.IsNotNull(burst, "Bomb.BurstDamage not found by reflection");
            burst.Invoke(bomb, new object[] { new Vector2(8f, 1f) });

            Assert.IsTrue(b.IsDead, "one bomb (burst) pops a 100 HP balloon");
            CollectionAssert.AreEqual(new[] { "barrageBalloon" }, _enemyDestroyed);
        }

        // ── Projectile trigger paths ────────────────────────────────────

        [Test]
        public void PlayerBullets_ThroughTrigger_FourPop() {
            var b = MakeBalloon(new Vector2(0f, 2f));
            var col = b.GetComponent<Collider2D>();
            for (int i = 1; i <= 4; i++) {
                var bullet = MakeProjectile<Bullet>(new Vector2(0f, 2f));
                ProjectileTrigger(bullet, col);
                if (i < 4) Assert.IsFalse(b.IsDead, $"alive after {i} bullet(s)");
            }
            Assert.IsTrue(b.IsDead, "the fourth bullet pops it");
            Assert.AreEqual(1, _enemyDestroyed.Count);
        }

        [Test]
        public void EnemyBullet_Flak_EnemyBomb_ThroughTrigger_LeaveBalloonUntouched() {
            var b = MakeBalloon(new Vector2(0f, 2f));
            var col = b.GetComponent<Collider2D>();
            var hp = b.GetComponent<Health>();

            ProjectileTrigger(MakeProjectile<EnemyBullet>(new Vector2(0f, 2f)), col);
            ProjectileTrigger(MakeProjectile<FlakShell>(new Vector2(0f, 2f)), col);
            ProjectileTrigger(MakeProjectile<EnemyBomb>(new Vector2(0f, 2f)), col);

            Assert.AreEqual(0f, hp.CurrentDamagePercent, 1e-5f, "enemy fire skips its own team");
            Assert.IsFalse(b.IsDead);
            Assert.AreEqual(0, _enemyDestroyed.Count);
        }

        [Test]
        public void BalloonOwnTrigger_FromAnyTeam_DoesNothing() {
            MakePlane(new Vector2(0f, 1f));
            var b = MakeBalloon(new Vector2(0.5f, 1f));
            var f = MakeFighter(new Vector2(1f, 1f));
            Invoke(b, "OnTriggerEnter2D", _plane.GetComponent<Collider2D>());
            Invoke(b, "OnTriggerEnter2D", f.GetComponent<Collider2D>());

            Assert.AreEqual(0f, PlayerHealth.CurrentDamagePercent, 1e-5f, "the balloon side never damages the player");
            Assert.IsFalse(b.IsDead, "a fighter flying through a balloon doesn't pop it");
            Assert.IsFalse(f.IsDead, "nor does the balloon hurt the fighter");
        }

        // ── Grace window shared with fighters ───────────────────────────

        [Test]
        public void FighterTouch_ThenBalloonInGrace_BalloonPops_PlayerHitOnce() {
            MakePlane(new Vector2(0f, 1f));
            var f = MakeFighter(new Vector2(0.4f, 1f));
            Touch(f.GetComponent<Collider2D>());
            float afterFighter = PlayerHealth.CurrentDamagePercent;
            Assert.AreEqual(100f / 3f, afterFighter, 1e-3f, "fighter contact 50 / 1.5");

            var b = MakeBalloon(new Vector2(0.6f, 1f));
            Touch(b.GetComponent<Collider2D>());

            Assert.IsTrue(b.IsDead, "a balloon touched inside the grace window still pops");
            Assert.AreEqual(afterFighter, PlayerHealth.CurrentDamagePercent, 1e-4f, "no second hit in the window");
            Assert.IsFalse(_plane.HasCrashed);
            Assert.AreEqual(1, _enemyDestroyed.FindAll(t => t == "barrageBalloon").Count);
        }

        [Test]
        public void BalloonTouch_ThenFighterInGrace_FighterDies_PlayerHitOnce() {
            MakePlane(new Vector2(0f, 1f));
            Touch(MakeBalloon(new Vector2(0.5f, 1f)).GetComponent<Collider2D>());
            var f = MakeFighter(new Vector2(0.6f, 1f));
            Touch(f.GetComponent<Collider2D>());

            Assert.IsTrue(f.IsDead);
            Assert.AreEqual(100f / 3f, PlayerHealth.CurrentDamagePercent, 1e-3f);
        }

        [Test]
        public void ShotDownBalloon_ThenTouched_NoPlayerDamage() {
            MakePlane(new Vector2(0f, 1f));
            var b = MakeBalloon(new Vector2(0.5f, 1f));
            b.GetComponent<Health>().ApplyDamage(100f, DamageSource.Bomb);
            Assert.IsTrue(b.IsDead);
            Touch(b.GetComponent<Collider2D>());

            Assert.AreEqual(0f, PlayerHealth.CurrentDamagePercent, 1e-5f, "a dead balloon (not yet removed) costs nothing");
            Assert.AreEqual(1, _enemyDestroyed.Count);
        }

        // ── Score and objectives, exactly once ──────────────────────────

        [Test]
        public void Ram_Scores75_Once_EvenWithRepeatedContacts() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.score != null, "Balance.json not loadable");
            var sk = MakeScoreKeeper();
            MakePlane(new Vector2(0f, 1f));
            var col = MakeBalloon(new Vector2(0.5f, 1f)).GetComponent<Collider2D>();

            Touch(col);
            Touch(col);
            PassGrace();
            Touch(col);

            Assert.AreEqual(75, g.score.barrageBalloon, "Balance score.barrageBalloon");
            Assert.AreEqual(75, sk.CurrentScore, "one ram = +75, once");
        }

        [Test]
        public void Mission4_PopAllBalloons_Needs6_RamsAndShotsBothCount_Once() {
            var m = LoadMission(4);
            var go = new GameObject("T974_Tracker");
            _created.Add(go);
            var tracker = go.AddComponent<ObjectiveTracker>();
            tracker.Init(m.objectives, 0f, m.spawns);
            int idx = -1;
            for (int i = 0; i < m.objectives.Length; i++)
                if (m.objectives[i].type == "destroyAllOfType" && m.objectives[i].targetType == "barrageBalloon") idx = i;
            Assert.GreaterOrEqual(idx, 0, "mission 4 has the pop-all-balloons objective");
            Assert.AreEqual(6, tracker.RemainingKills(idx), "3 spawn entries x 2 balloons");

            GameEvents.EntityDestroyed += tracker.OnEntityDestroyed;
            try {
                MakePlane(new Vector2(0f, 1f));
                var g = GameBalance.Get();
                Assume.That(g != null && g.player != null, "Balance.json not loadable");
                for (int i = 0; i < 6; i++) {
                    var b = MakeBalloon(new Vector2(0.5f + i, 1f));
                    if (i % 2 == 0) {
                        // Rammed: touched twice in the same pass, one kill only.
                        PlayerHealth.Heal(100f);   // keep the plane flying for the test
                        Touch(b.GetComponent<Collider2D>());
                        Touch(b.GetComponent<Collider2D>());
                        PassGrace();
                        Assert.IsFalse(_plane.HasCrashed);
                    } else {
                        var hp = b.GetComponent<Health>();
                        for (int k = 0; k < 6; k++) hp.ApplyDamage(g.player.bulletDamage, DamageSource.Bullet);
                    }
                    Assert.AreEqual(5 - i, tracker.RemainingKills(idx), $"after balloon {i + 1}");
                }
            } finally {
                GameEvents.EntityDestroyed -= tracker.OnEntityDestroyed;
            }
            Assert.AreEqual(6, _enemyDestroyed.Count, "six EntityDestroyed, no duplicates");
        }

        [Test]
        public void Mission4_Balloons_AboveTerrain_WithDriftMargin() {
            // Spawn y is height above the local terrain (MissionRunner.SpawnEntities). Drift is
            // horizontal only (driftAxis = right) and its integrated offset is bounded by
            // 2 x driftAmplitude (1.6 u), so it never moves a balloon vertically into terrain.
            var m = LoadMission(4);
            int n = 0;
            foreach (var s in m.spawns) {
                if (s == null || s.type != "barrageBalloon") continue;
                n += s.count;
                Assert.GreaterOrEqual(s.y, 2f, $"balloon at x={s.atX}: >= 2 u above terrain (1 u box + margin)");
                if (s.count > 1)
                    Assert.Greater(s.spacing, 2f * 1.6f, $"balloon pair at x={s.atX}: spacing > two drift swings, no overlap");
            }
            Assert.AreEqual(6, n);
        }

        // ── Death cleanup ───────────────────────────────────────────────

        [Test]
        public void DeadBalloon_FixedUpdate_Explodes_Deactivates_RemovesTether() {
            _fxGo = new GameObject("T974_Fx");
            typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, _fxGo.AddComponent<FxManager>());

            var b = MakeBalloon(new Vector2(0f, 2f));
            var tether = b.transform.Find("BarrageBalloonTether");
            Assert.IsNotNull(tether, "Awake builds the tether child");

            b.GetComponent<Health>().ApplyDamage(1f, DamageSource.Collision);
            Assert.IsTrue(b.IsDead);

            LogAssert.ignoreFailingMessages = true;   // Destroy() of the tether logs in EditMode
            Invoke(b, "FixedUpdate");

            Assert.IsFalse(b.gameObject.activeSelf, "the popped balloon is removed from play");
            var line = tether != null ? tether.GetComponent<LineRenderer>() : null;
            Assert.IsTrue(tether == null || line == null || !line.enabled, "the tether is hidden/destroyed with it");
            Assert.AreEqual(1, _enemyDestroyed.Count, "cleanup raises no second EntityDestroyed");
        }
    }
}
