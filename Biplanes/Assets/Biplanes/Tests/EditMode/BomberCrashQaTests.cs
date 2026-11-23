using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Fx;
using Biplanes.Missions;
using Biplanes.Weapons;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// QA T971 (verifies B068): gaps not covered by BomberDamageTests.
    ///  - Mission 2 through MissionRunner: destroy_bomber completes at the killing hit, the
    ///    bomber scores 500 once, and neither the crash nor the wreck landing counts again.
    ///  - A bomber killed just right of failAtX whose wreck drifts past it is still a win.
    ///  - After the crash the bomber is gone from escorts, EnemyPressure and escort lookup,
    ///    and no wreck is registered as an aircraft.
    ///  - MissionRunner teardown (Next / StartMission) removes falling wrecks.
    ///  - Smoke thresholds at the exact bullet / bomb counts, puffs only when damaged, and
    ///    only through the enemy smoke kind.
    ///  - Wreck: no Rigidbody2D, white tint, the bomber's sprite, maxFallSeconds safety net,
    ///    never raises EntityDestroyed.
    ///  - DamageSource.Collision (the terrain or a fighter ram) is still fatal for the 1000 HP
    ///    bomber's Health with one event. A player ram on a bomber no longer uses Collision since
    ///    B069 (250 HP per ram, see BomberRamTests).
    /// </summary>
    public class BomberCrashQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<string> _destroyed = new List<string>();

        [SetUp]
        public void SetUp() {
            _destroyed.Clear();
            GameEvents.EntityDestroyed += OnEntityDestroyed;
        }

        [TearDown]
        public void TearDown() {
            GameEvents.EntityDestroyed -= OnEntityDestroyed;
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
            BomberWreck.ClearAll();
            DestroyFxLeftovers();
        }

        private void OnEntityDestroyed(string type, Team team) {
            if (team == Team.Enemy) _destroyed.Add(type);
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static void Invoke(object target, string method, params object[] args) {
            var m = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found by reflection");
            m.Invoke(target, args.Length == 0 ? null : args);
        }

        private static void Wake(BomberAI b) {
            Invoke(b.GetComponent<Health>(), "Awake");
            Invoke(b, "Awake");
        }

        private BomberAI MakeBomber(Vector3 position) {
            var b = BomberAI.Spawn(position, "bomber_1");
            _created.Add(b.gameObject);
            Wake(b);
            return b;
        }

        private static BomberWreck BeginCrash(BomberAI b) {
            var m = typeof(BomberAI).GetMethod("BeginCrash", Private);
            Assert.IsNotNull(m, "BomberAI.BeginCrash not found");
            return (BomberWreck)m.Invoke(b, null);
        }

        private static bool Step(BomberWreck w, float dt) {
            var m = typeof(BomberWreck).GetMethod("Step", Private);
            Assert.IsNotNull(m, "BomberWreck.Step not found");
            return (bool)m.Invoke(w, new object[] { dt });
        }

        private static bool EmitSmoke(BomberAI b, float dt) {
            var m = typeof(BomberAI).GetMethod("EmitSmoke", Private);
            Assert.IsNotNull(m, "BomberAI.EmitSmoke not found");
            return (bool)m.Invoke(b, new object[] { dt });
        }

        private static float BulletDamage => GameBalance.Get().player.bulletDamage;
        private static float BombDamage => GameBalance.Get().player.bombDamage;

        private static void Shoot(BomberAI b, int bullets) {
            var h = b.GetComponent<Health>();
            for (int i = 0; i < bullets; i++) h.ApplyDamage(BulletDamage, DamageSource.Bullet);
        }

        private static void Bomb(BomberAI b, int bombs) {
            var h = b.GetComponent<Health>();
            for (int i = 0; i < bombs; i++) h.ApplyDamage(BombDamage, DamageSource.Bomb);
        }

        private static bool LandFully(BomberWreck w) {
            for (int i = 0; i < 5000; i++) {
                if (Step(w, 0.02f)) return true;
            }
            return false;
        }

        private static void DestroyFxLeftovers() {
            foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (sr == null) continue;
                string n = sr.gameObject.name;
                if (n == "SmokePuff" || n == "Explosion" || n == "FlakBurst" || n == "bomberWreck") {
                    Object.DestroyImmediate(sr.gameObject);
                }
            }
        }

        // ── MissionRunner fixture (pattern of MissionEndRulesTests) ─────

        private sealed class Fixture : IDisposable {
            public readonly MissionRunner Runner;
            public readonly GameObject RunnerGo;
            public readonly Camera Cam;
            public readonly GameManager Gm;
            public readonly ScoreKeeper Score;
            public readonly List<bool> Won = new List<bool>();
            public readonly List<string> Reasons = new List<string>();

            private readonly GameObject _camGo, _gmGo, _fxGo, _skGo;
            private readonly List<Camera> _disabled = new List<Camera>();
            private readonly Action<bool, string> _onEnded;
            private readonly object _prevSk;
            private readonly float _timeScale;
            private readonly Dictionary<string, int?> _prefs = new Dictionary<string, int?>();
            private static readonly string[] PrefKeys = { "unlocked_mission", "best_score_2" };

            public Fixture() {
                foreach (var k in PrefKeys) _prefs[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : (int?)null;
                _timeScale = Time.timeScale;

                foreach (var c in Camera.allCameras) {
                    if (c != null && c.CompareTag("MainCamera")) { c.enabled = false; _disabled.Add(c); }
                }
                _camGo = new GameObject("T971_Camera");
                _camGo.tag = "MainCamera";
                Cam = _camGo.AddComponent<Camera>();
                Cam.orthographic = true;
                Cam.orthographicSize = 5f;
                Cam.aspect = 16f / 9f;
                _camGo.transform.position = new Vector3(0f, 0f, -10f);

                _gmGo = new GameObject("T971_GameManager");
                Gm = _gmGo.AddComponent<GameManager>();
                typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, Gm);

                _fxGo = new GameObject("T971_Fx");
                typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, _fxGo.AddComponent<FxManager>());

                var skField = typeof(ScoreKeeper).GetField("_instance", PrivateStatic);
                _prevSk = skField.GetValue(null);
                skField.SetValue(null, null);
                _skGo = new GameObject("T971_ScoreKeeper");
                Score = _skGo.AddComponent<ScoreKeeper>();
                // Awake sets the singleton and subscribes to EntityDestroyed (not run in EditMode).
                typeof(ScoreKeeper).GetMethod("Awake", Private).Invoke(Score, null);

                _onEnded = (won, reason) => { Won.Add(won); Reasons.Add(reason); };
                GameEvents.MissionEnded += _onEnded;

                ScrollController.Instance = null;
                RunnerGo = new GameObject("T971_MissionRunner");
                Runner = RunnerGo.AddComponent<MissionRunner>();
            }

            public bool MissionEnded => (bool)typeof(MissionRunner).GetField("_missionEnded", Private).GetValue(Runner);

            public ObjectiveTracker Tracker => RunnerGo.GetComponent<ObjectiveTracker>();

            public void Start(int mission) {
                Runner.StartMission(mission);
                if (ScrollController.Instance == null) {
                    foreach (var sc in Object.FindObjectsByType<ScrollController>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                        if (sc != null && sc.Camera == Cam) { ScrollController.Instance = sc; break; }
                    }
                }
                Assert.IsNotNull(ScrollController.Instance, "StartMission creates a ScrollController");
            }

            public void InvokeUpdate() {
                typeof(MissionRunner).GetMethod("Update", Private).Invoke(Runner, null);
            }

            public void SetCamX(float x) {
                var p = Cam.transform.position;
                Cam.transform.position = new Vector3(x, p.y, p.z);
            }

            /// <summary>Spawns mission 2's bomber_1 entry through the runner, with the camera
            /// at its trigger x, and runs Awake on its Health and BomberAI.</summary>
            public BomberAI SpawnMissionBomber() {
                var text = Resources.Load<TextAsset>("Missions/mission_02");
                Assert.IsNotNull(text, "mission_02.json in Resources/Missions");
                var data = JsonUtility.FromJson<MissionData>(text.text);
                SpawnEntry entry = null;
                foreach (var s in data.spawns) if (s.type == "bomber" && s.id == "bomber_1") entry = s;
                Assert.IsNotNull(entry, "mission 2 has bomber_1");
                SetCamX(entry.atX);
                Runner.SpawnEntities(entry);
                BomberAI bomber = null;
                foreach (var b in Object.FindObjectsByType<BomberAI>(FindObjectsSortMode.None)) {
                    if (b.SpawnId == "bomber_1") bomber = b;
                }
                Assert.IsNotNull(bomber);
                Wake(bomber);
                return bomber;
            }

            public void Dispose() {
                GameEvents.MissionEnded -= _onEnded;
                typeof(MissionRunner).GetMethod("OnDestroy", Private)?.Invoke(Runner, null);
                typeof(ScoreKeeper).GetMethod("OnDestroy", Private)?.Invoke(Score, null);

                DestroyAll<Player.PlayerPlane>();
                DestroyAll<TerrainStrip>();
                DestroyAll<ParallaxBackground>();
                DestroyAll<StaticObstacle>();
                DestroyAll<BomberAI>();
                DestroyAll<FighterAI>();
                DestroyAll<ZeppelinAI>();
                DestroyAll<EnemyBase>();
                DestroyAll<PlayerHQ>();
                DestroyAll<BombBalloon>();
                BomberWreck.ClearAll();
                if (ScrollController.Instance != null) Object.DestroyImmediate(ScrollController.Instance.gameObject);
                DestroyAll<ScrollController>();
                ScrollController.Instance = null;
                Object.DestroyImmediate(RunnerGo);

                foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                    if (p != null) Object.DestroyImmediate(p.gameObject);
                }

                typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, null);
                typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, null);
                typeof(ScoreKeeper).GetField("_instance", PrivateStatic).SetValue(null, _prevSk);
                Object.DestroyImmediate(_gmGo);
                Object.DestroyImmediate(_fxGo);
                Object.DestroyImmediate(_skGo);
                Object.DestroyImmediate(_camGo);
                foreach (var c in _disabled) if (c != null) c.enabled = true;

                foreach (var kv in _prefs) {
                    if (kv.Value.HasValue) PlayerPrefs.SetInt(kv.Key, kv.Value.Value);
                    else PlayerPrefs.DeleteKey(kv.Key);
                }
                PlayerPrefs.Save();
                Time.timeScale = _timeScale;
                DestroyFxLeftovers();
            }

            private static void DestroyAll<T>() where T : Component {
                foreach (var c in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                    if (c != null) Object.DestroyImmediate(c.gameObject);
                }
            }
        }

        // ── Mission 2 through MissionRunner ─────────────────────────────

        [Test]
        public void Runner_M2_KillingHitWinsAtOnce_ScoresOnce_NoRecountOnCrashOrLanding() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                var bomber = fx.SpawnMissionBomber();
                Assert.AreEqual(1000f, bomber.GetComponent<Health>().MaxHitPoints);
                Assert.IsTrue(bomber.HasTargetX, "failAtX 10 is the bomber's watched target");
                int scoreBefore = fx.Score.CurrentScore;

                Shoot(bomber, 39);
                fx.InvokeUpdate();
                Assert.IsFalse(fx.MissionEnded, "39 bullets: the bomber still flies, mission open");
                Assert.AreEqual(scoreBefore, fx.Score.CurrentScore);

                Shoot(bomber, 1);
                Assert.IsTrue(fx.MissionEnded, "destroy_bomber completes at the killing hit");
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { true }, fx.Won);
                int bomberPoints = GameBalance.Get().score.bomber;
                Assert.AreEqual(scoreBefore + bomberPoints, fx.Score.CurrentScore, "bomber scored once");

                Shoot(bomber, 5);   // bullets already in flight hit the dead bomber
                var wreck = BeginCrash(bomber);
                Assert.IsNotNull(wreck);
                fx.InvokeUpdate();
                Assert.IsTrue(LandFully(wreck), "the wreck lands");
                fx.InvokeUpdate();

                Assert.AreEqual(scoreBefore + bomberPoints, fx.Score.CurrentScore, "no second score");
                CollectionAssert.AreEqual(new[] { "bomber" }, _destroyed, "one EntityDestroyed in total");
                Assert.AreEqual(1, fx.Won.Count, "the mission end is raised once");
            }
        }

        [Test]
        public void Runner_M2_KilledJustBeforeFailAtX_WreckDriftsPastIt_StillWon() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                var bomber = fx.SpawnMissionBomber();
                float failAtX = bomber.TargetX;
                Assert.AreEqual(10f, failAtX, 1e-4f);
                var p = bomber.transform.position;
                bomber.transform.position = new Vector3(failAtX + 0.2f, p.y, p.z);
                fx.InvokeUpdate();
                Assert.IsFalse(fx.MissionEnded, "not reached yet");

                Shoot(bomber, 40);
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);

                // Before the crash tick the dead bomber object could still be moved past failAtX.
                bomber.transform.position = new Vector3(failAtX - 1f, p.y, p.z);
                fx.InvokeUpdate();
                var wreck = BeginCrash(bomber);
                for (int i = 0; i < 20; i++) { Step(wreck, 0.05f); fx.InvokeUpdate(); }

                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { true }, fx.Won, "never flipped to a loss");
                foreach (var r in fx.Reasons) StringAssert.DoesNotContain("reached its target", r);
            }
        }

        [Test]
        public void Runner_M2_AfterCrash_BomberIsGoneFromAircraftAndEscorts() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                var bomber = fx.SpawnMissionBomber();
                fx.Runner.SpawnEntities(new SpawnEntry {
                    type = "fighter", atX = 80, y = 2.5f, count = 1, spacing = 0,
                    pattern = "escort", id = "", escortOf = "bomber_1"
                });
                FighterAI escort = null;
                foreach (var f in Object.FindObjectsByType<FighterAI>(FindObjectsSortMode.None)) escort = f;
                Assert.IsNotNull(escort);
                // FighterAI.Start resolves this in Play mode; EditMode sets it the same way.
                typeof(FighterAI).GetField("_escortTarget", Private)
                    .SetValue(escort, FighterAI.FindEscortTarget("bomber_1"));
                Assert.IsTrue(escort.IsEscorting, "escort holds formation on the live bomber");
                Assert.IsTrue(EnemyPressure.IsLiveAircraft(bomber));

                Shoot(bomber, 40);
                Assert.IsFalse(EnemyPressure.IsLiveAircraft(bomber), "dead at the hit: not a live aircraft");

                var wreck = BeginCrash(bomber);
                Assert.IsNotNull(wreck);
                Assert.IsFalse(escort.IsEscorting, "the escort lost its target and chases instead");
                Assert.IsNull(FighterAI.FindEscortTarget("bomber_1"), "the wreck is not an escort target");

                int live = 0;
                foreach (var a in fx.Runner.EnemyAircraft) {
                    Assert.IsFalse(a is BomberWreck);
                    if (EnemyPressure.IsLiveAircraft(a) && !(a is FighterAI)) live++;
                }
                Assert.AreEqual(0, live, "no live bomber left in EnemyAircraft");
            }
        }

        [Test]
        public void Runner_NextMission_TearsDownFallingWrecks() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                var bomber = fx.SpawnMissionBomber();
                Shoot(bomber, 40);
                var wreck = BeginCrash(bomber);
                var go = wreck.gameObject;
                Assert.AreEqual(1, BomberWreck.LiveCount);

                fx.Start(2);   // Next / StartMission without a scene reload
                Assert.AreEqual(0, BomberWreck.LiveCount, "teardown clears the wreck");
                Assert.IsTrue(go == null);
            }
        }

        // ── Smoke ───────────────────────────────────────────────────────

        [Test]
        public void Smoke_ExactThresholdCounts_Bullets() {
            var b = MakeBomber(new Vector3(50f, 2f, 0f));
            Shoot(b, 11);
            Assert.AreEqual(BomberAI.SmokeLevelNone, b.SmokeLevel, "11 bullets: 72.5% left");
            Shoot(b, 1);
            Assert.AreEqual(BomberAI.SmokeLevelLight, b.SmokeLevel, "12 bullets: exactly 70% left");
            Shoot(b, 11);
            Assert.AreEqual(BomberAI.SmokeLevelLight, b.SmokeLevel, "23 bullets: 42.5% left");
            Shoot(b, 1);
            Assert.AreEqual(BomberAI.SmokeLevelThick, b.SmokeLevel, "24 bullets: exactly 40% left");
            Shoot(b, 9);
            Assert.AreEqual(BomberAI.SmokeLevelThick, b.SmokeLevel, "33 bullets: 17.5% left");
            Shoot(b, 1);
            Assert.AreEqual(BomberAI.SmokeLevelHeavy, b.SmokeLevel, "34 bullets: exactly 15% left");
        }

        [Test]
        public void Smoke_ExactThresholdCounts_Bombs() {
            var b = MakeBomber(new Vector3(50f, 2f, 0f));
            Bomb(b, 2);
            Assert.AreEqual(BomberAI.SmokeLevelNone, b.SmokeLevel, "2 bombs: 80% left");
            Bomb(b, 1);
            Assert.AreEqual(BomberAI.SmokeLevelLight, b.SmokeLevel, "3 bombs: 70% left");
            Bomb(b, 3);
            Assert.AreEqual(BomberAI.SmokeLevelThick, b.SmokeLevel, "6 bombs: 40% left");
            Bomb(b, 3);
            Assert.AreEqual(BomberAI.SmokeLevelHeavy, b.SmokeLevel, "9 bombs: 10% left");
        }

        [Test]
        public void Smoke_PuffsOnlyWhenDamaged_AsEnemySmoke() {
            var fxGo = new GameObject("T971_Fx_Smoke");
            _created.Add(fxGo);
            var fxField = typeof(FxManager).GetField("_instance", PrivateStatic);
            var prevFx = fxField.GetValue(null);
            var fxm = fxGo.AddComponent<FxManager>();
            fxField.SetValue(null, fxm);
            try {
                var b = MakeBomber(new Vector3(50f, 2f, 0f));
                Assert.IsFalse(EmitSmoke(b, 0.5f), "undamaged: no smoke");
                Assert.AreEqual(0, fxm.CountActive(FxManager.Kind.EnemySmokePuff));

                Shoot(b, 12);
                Assert.IsFalse(EmitSmoke(b, 0f), "paused: no puff");
                Assert.IsTrue(EmitSmoke(b, 0.02f), "first puff at once when smoking starts");
                Assert.AreEqual(1, fxm.CountActive(FxManager.Kind.EnemySmokePuff));
                Assert.AreEqual(0, fxm.CountActive(FxManager.Kind.SmokePuff), "never the player's smoke kind");

                Shoot(b, 28);   // dead
                Assert.IsFalse(EmitSmoke(b, 1f), "a dead bomber emits nothing (the wreck smokes)");
            } finally {
                fxField.SetValue(null, prevFx);
            }
        }

        // ── Wreck ───────────────────────────────────────────────────────

        [Test]
        public void Wreck_UsesBomberSprite_WhiteTint_NoRigidbody() {
            var b = MakeBomber(new Vector3(50f, 2f, 0f));
            Invoke(b, "Start");   // loads Sprites/enemies/enemy_bomber, flipX, sortingOrder 5
            var bomberSprite = b.GetComponent<SpriteRenderer>().sprite;
            Assert.IsNotNull(bomberSprite);

            Shoot(b, 40);
            var w = BeginCrash(b);
            Assert.IsNotNull(w);
            _created.Add(w.gameObject);
            var sr = w.GetComponent<SpriteRenderer>();
            Assert.AreSame(bomberSprite, sr.sprite, "the wreck shows the bomber's own art");
            Assert.AreEqual(Color.white, sr.color, "white-only: no tint on the wreck");
            Assert.IsTrue(sr.flipX);
            Assert.AreEqual(5, sr.sortingOrder);
            Assert.IsNull(w.GetComponent<Rigidbody2D>(), "visual only: no physics body");
            Assert.AreEqual(0, w.GetComponentsInChildren<Collider2D>(true).Length);
        }

        [Test]
        public void Wreck_MaxFallSeconds_SafetyNet() {
            var w = BomberWreck.Create(new Vector3(0f, 100000f, 0f), null, true, 5, 0f, -1, Vector2.zero);
            var go = w.gameObject;
            _created.Add(go);
            float max = BomberCrashData.Resolve(GameBalance.Get()).maxFallSeconds;
            int steps = 0;
            bool landed = false;
            while (!landed && steps < 10000) { landed = Step(w, 0.1f); steps++; }
            Assert.IsTrue(landed, "a wreck that never meets the ground still goes");
            Assert.AreEqual(max, steps * 0.1f, 0.11f, "after maxFallSeconds");
            Assert.IsTrue(go == null);
        }

        [Test]
        public void Wreck_Landing_NeverRaisesEntityDestroyed() {
            var w = BomberWreck.Create(new Vector3(0f, 6f, 0f), null, true, 5, 0f, -1, new Vector2(-1.5f, 0f));
            _created.Add(w.gameObject);
            Assert.IsTrue(LandFully(w));
            CollectionAssert.IsEmpty(_destroyed);
        }

        // ── Collision source ────────────────────────────────────────────

        [Test]
        public void Collision_IsFatalForEnemyHealth() {
            var b = MakeBomber(new Vector3(50f, 2f, 0f));
            var h = b.GetComponent<Health>();
            h.ApplyDamage(1f, DamageSource.Collision);
            Assert.IsTrue(h.IsDead, "DamageSource.Collision is fatal for enemy Health (B025), 1000 HP or not; player rams on bombers use ramDamage since B069");
            h.ApplyDamage(1f, DamageSource.Collision);
            Shoot(b, 3);
            CollectionAssert.AreEqual(new[] { "bomber" }, _destroyed);
        }
    }
}
