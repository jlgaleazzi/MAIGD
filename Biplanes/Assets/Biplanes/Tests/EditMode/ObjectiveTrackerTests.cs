using System;
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
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// T936: EditMode tests for the T935 ObjectiveTracker rewrite and MissionRunner's objective
    /// wiring (destroyAllOfType counts, destroyById through id-tracked spawns, bombTarget through
    /// EnemyBase.Destroyed, failAtX, surviveToEnd, one subscription per event after Next).
    ///
    /// The tracker tests are pure logic. The MissionRunner tests reach GameManager.Instance and
    /// FxManager.Instance, whose lazy getters call DontDestroyOnLoad (Play mode only), so both
    /// singletons are seeded by reflection with un-awakened components. ScoreKeeper is seeded the
    /// same way, and "unlocked_mission" is backed up and restored, so a win never changes the
    /// user's save.
    /// </summary>
    public class ObjectiveTrackerTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<string> _completed = new List<string>();
        private Action<string> _onCompleted;

        [SetUp]
        public void SetUpTracker() {
            _completed.Clear();
            _onCompleted = id => _completed.Add(id);
            GameEvents.ObjectiveCompleted += _onCompleted;
        }

        [TearDown]
        public void TearDownTracker() {
            GameEvents.ObjectiveCompleted -= _onCompleted;
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static MissionData LoadMission(int index) {
            var text = Resources.Load<TextAsset>($"Missions/mission_{index:D2}");
            Assert.IsNotNull(text, $"mission_{index:D2}.json not found in Resources/Missions");
            var data = JsonUtility.FromJson<MissionData>(text.text);
            Assert.IsNotNull(data);
            Assert.IsNotNull(data.objectives);
            return data;
        }

        /// <summary>T938: the spawn id M2's destroyById objective targets, read from the JSON
        /// (V003 renamed it from bomber_01 to bomber_1), so the tests follow the data.</summary>
        private static string M2BomberId => LoadMission(2).objectives[0].targetId;

        /// <summary>T938: the spawn id M5's destroyById objective targets (V003: "zep").</summary>
        private static string M5ZeppelinId => LoadMission(5).objectives[0].targetId;

        /// <summary>T962: how many entities of `type` the mission JSON spawns (sum of `count`,
        /// case-insensitive), the same total ObjectiveTracker requires for destroyAllOfType.
        /// Tests use it instead of hard-coded totals so mission data changes (V005-V011 grew
        /// M1 from 12 to 37 fighters) don't break them.</summary>
        private static int SpawnTotal(MissionData data, string type) {
            int n = 0;
            if (data.spawns == null) return 0;
            foreach (var s in data.spawns) {
                if (s != null && string.Equals(s.type, type, StringComparison.OrdinalIgnoreCase)) n += Mathf.Max(0, s.count);
            }
            return n;
        }

        private static int SpawnTotal(int missionIndex, string type) => SpawnTotal(LoadMission(missionIndex), type);

        /// <summary>T962: the EntityDestroyed key an EnemyPressure reinforcement fighter raises
        /// (its GameObject name). Since B050 it counts toward destroyAllOfType "fighter" like a
        /// JSON fighter; the required number stays the JSON spawn total (GDD "Enemy pressure").</summary>
        private const string ReinforcementKey = ScoreKeeper.ReinforcementFighterKey;

        /// <summary>T962: points ScrollController.Instance at the controller that drives `cam`.
        /// In EditMode ScrollController.Awake never runs, so Instance stays null and a plain
        /// FindFirstObjectByType can return a controller leaked by another fixture whose camera
        /// is destroyed (MissionDistanceX 0), which made the map-end check never fire.</summary>
        private static void BindScroll(Camera cam) {
            ScrollController pick = null;
            foreach (var sc in Object.FindObjectsByType<ScrollController>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (sc != null && sc.Camera == cam) { pick = sc; break; }
            }
            Assert.IsNotNull(pick, "StartMission creates a ScrollController bound to the test camera");
            ScrollController.Instance = pick;
        }

        private ObjectiveTracker MakeTracker() {
            var go = new GameObject("T936_Tracker");
            _created.Add(go);
            return go.AddComponent<ObjectiveTracker>();
        }

        private ObjectiveTracker TrackerFor(int missionIndex) {
            var data = LoadMission(missionIndex);
            var t = MakeTracker();
            t.Init(data.objectives, 0f, data.spawns);
            return t;
        }

        private static int[] IntArray(ObjectiveTracker t, string field) {
            var f = typeof(ObjectiveTracker).GetField(field, Private);
            Assert.IsNotNull(f, $"ObjectiveTracker.{field} not found by reflection");
            return (int[])f.GetValue(t);
        }

        private static ObjectiveEntry Obj(string id, string type, string targetType = "", string targetId = "", float failAtX = -1f) {
            return new ObjectiveEntry { id = id, type = type, targetType = targetType, targetId = targetId, failAtX = failAtX };
        }

        // ── destroyAllOfType ────────────────────────────────────────────

        [Test]
        public void DestroyAllOfType_RequiredCount_IsTheSpawnTotal_PerMission() {
            // T962: every destroyAllOfType objective in every mission requires exactly the JSON
            // spawn total of its type, and that total is positive (else it can't complete).
            int checkedObjectives = 0;
            for (int m = 1; m <= 5; m++) {
                var data = LoadMission(m);
                var required = IntArray(TrackerFor(m), "_required");
                for (int i = 0; i < data.objectives.Length; i++) {
                    var o = data.objectives[i];
                    if (o.type != "destroyAllOfType") continue;
                    int total = SpawnTotal(data, o.targetType);
                    Assert.Greater(total, 0, $"M{m} '{o.id}': the mission spawns no '{o.targetType}'");
                    Assert.AreEqual(total, required[i], $"M{m} '{o.id}': required = sum of '{o.targetType}' spawn counts");
                    checkedObjectives++;
                }
            }
            Assert.GreaterOrEqual(checkedObjectives, 3, "M1 fighters, M3 AAA and M4 balloons are destroyAllOfType");
        }

        [Test]
        public void DestroyAllOfType_M1_JsonAndReinforcementKillsCount_CompletesOnce() {
            // B050 (GDD "Enemy pressure", change log 2026-11-20): reinforcement kills count
            // toward "fighter" like JSON fighters; the required number is the JSON total.
            int total = SpawnTotal(1, "fighter");
            Assert.Greater(total, 3, "M1 spawns fighters");
            var t = TrackerFor(1);
            Assert.AreEqual(total, IntArray(t, "_required")[0], "required = JSON total, reinforcements add nothing");
            for (int i = 0; i < total - 3; i++) t.OnEntityDestroyed("fighter", Team.Enemy);

            // Kills that must not count: other types, other teams.
            t.OnEntityDestroyed("aaa", Team.Enemy);
            t.OnEntityDestroyed("bomber", Team.Enemy);
            t.OnEntityDestroyed("fighter", Team.Player);
            t.OnEntityDestroyed(ReinforcementKey, Team.Player);
            t.OnEntityDestroyed("HealthBalloon", Team.Neutral);
            Assert.AreEqual(total - 3, IntArray(t, "_kills")[0]);

            // Two reinforcement kills count like two fighters.
            t.OnEntityDestroyed(ReinforcementKey, Team.Enemy);
            t.OnEntityDestroyed(ReinforcementKey, Team.Enemy);
            Assert.AreEqual(total - 1, IntArray(t, "_kills")[0]);
            Assert.AreEqual(1, t.RemainingKills(0));
            Assert.IsFalse(t.AllComplete, $"{total - 1} of {total} fighters is not a win");
            Assert.AreEqual(0, _completed.Count);

            // The last kill may be a reinforcement.
            t.OnEntityDestroyed(ReinforcementKey, Team.Enemy);
            Assert.IsTrue(t.AllComplete, $"kill {total} of {total} (a reinforcement) completes M1");
            CollectionAssert.AreEqual(new[] { "kill_all_fighters" }, _completed);

            t.OnEntityDestroyed("fighter", Team.Enemy);
            t.OnEntityDestroyed(ReinforcementKey, Team.Enemy);
            Assert.AreEqual(1, _completed.Count, "a completed objective is never raised again");
        }

        [Test]
        public void DestroyAllOfType_TypeMatchIsCaseInsensitive() {
            var t = MakeTracker();
            t.Init(new[] { Obj("o", "destroyAllOfType", "barrageBalloon") }, 0f,
                new[] { new SpawnEntry { type = "barrageballoon", count = 2 } });
            t.OnEntityDestroyed("BarrageBalloon", Team.Enemy);
            Assert.IsFalse(t.AllComplete);
            t.OnEntityDestroyed("barrageBalloon", Team.Enemy);
            Assert.IsTrue(t.AllComplete);
        }

        [Test]
        public void DestroyAllOfType_WithoutSpawnList_NeverCompletes() {
            var t = MakeTracker();
            t.Init(new[] { Obj("o", "destroyAllOfType", "fighter") }, 0f);   // no spawns argument
            for (int i = 0; i < 50; i++) t.OnEntityDestroyed("fighter", Team.Enemy);
            Assert.IsFalse(t.AllComplete, "required 0 = can't complete (warning logged)");
        }

        // ── destroyById ─────────────────────────────────────────────────

        [Test]
        public void DestroyById_M2_CompletesOnlyForBomber01_Once() {
            var t = TrackerFor(2);
            string id = M2BomberId;
            Assert.IsFalse(t.OnTargetDestroyed("esc_01"));
            Assert.IsFalse(t.OnTargetDestroyed(""));
            Assert.IsFalse(t.OnTargetDestroyed(null));
            Assert.IsFalse(t.OnTargetDestroyed(id.ToUpperInvariant()), "ids are matched exactly");

            // destroyById never completes from a type kill alone.
            t.OnEntityDestroyed("bomber", Team.Enemy);
            Assert.IsFalse(t.AllComplete);

            Assert.IsTrue(t.OnTargetDestroyed(id));
            Assert.IsTrue(t.AllComplete);
            Assert.IsFalse(t.OnTargetDestroyed(id), "a second report completes nothing");
            CollectionAssert.AreEqual(new[] { "destroy_bomber" }, _completed);
        }

        // ── bombTarget ──────────────────────────────────────────────────

        [Test]
        public void BombTarget_ById_MatchesOnlyThatId() {
            var t = MakeTracker();
            t.Init(new[] { Obj("bomb_base", "bombTarget", "enemyBase", "enemy_base_01") }, 0f);
            Assert.IsFalse(t.OnBombTargetDestroyed("enemy_base_02", "enemyBase"), "other id");
            Assert.IsFalse(t.OnBombTargetDestroyed("", "enemyBase"), "a targetId objective ignores the type");
            Assert.IsTrue(t.OnBombTargetDestroyed("enemy_base_01", "enemyBase"));
            Assert.IsTrue(t.AllComplete);
            Assert.IsFalse(t.OnBombTargetDestroyed("enemy_base_01", "enemyBase"), "once");
            Assert.AreEqual(1, _completed.Count);
        }

        [Test]
        public void BombTarget_EmptyId_MatchesByType() {
            var t = MakeTracker();
            t.Init(new[] { Obj("bomb_any", "bombTarget", "enemyBase", "") }, 0f);
            Assert.IsFalse(t.OnBombTargetDestroyed("x", "aaa"), "other type");
            Assert.IsTrue(t.OnBombTargetDestroyed("whatever", "EnemyBase"), "type match, case-insensitive");
            Assert.IsTrue(t.AllComplete);
        }

        [Test]
        public void M4_NeedsBothBalloonsAndBaseBomb_InEitherOrder() {
            int balloons = SpawnTotal(4, "barrageBalloon");
            Assert.Greater(balloons, 0);
            var t = TrackerFor(4);
            Assert.IsTrue(t.OnBombTargetDestroyed("enemy_base_01", "enemyBase"));
            Assert.IsFalse(t.AllComplete, "the base alone is not a win");
            for (int i = 0; i < balloons - 1; i++) t.OnEntityDestroyed("barrageBalloon", Team.Enemy);
            t.OnEntityDestroyed("enemyBase", Team.Enemy);   // the base's own EntityDestroyed: no effect
            Assert.IsFalse(t.AllComplete);
            t.OnEntityDestroyed("barrageBalloon", Team.Enemy);
            Assert.IsTrue(t.AllComplete);
            CollectionAssert.AreEquivalent(new[] { "bomb_base", "pop_all_balloons" }, _completed);
        }

        // ── failAtX ─────────────────────────────────────────────────────

        // B018: failAtX is the WORLD x the target entity must not reach (flying left).

        [Test]
        public void GetFailAtXFor_ReturnsTheObjectiveFailAtX_ForItsTargetIdOnly() {
            var data = LoadMission(2);
            float failAtX = data.objectives[0].failAtX;
            var t = TrackerFor(2);
            string id = M2BomberId;
            Assert.AreEqual(failAtX, t.GetFailAtXFor(id));
            Assert.AreEqual(-1f, t.GetFailAtXFor("esc_01"), "other ids have no target");
            Assert.AreEqual(-1f, t.GetFailAtXFor(""));
            Assert.AreEqual(-1f, t.GetFailAtXFor(null));
            t.OnTargetDestroyed(id);
            Assert.AreEqual(-1f, t.GetFailAtXFor(id), "a completed objective gives no target");

            var none = MakeTracker();
            none.Init(new[] { Obj("kill", "destroyById", "bomber", "b1") }, 0f);   // failAtX -1
            Assert.AreEqual(-1f, none.GetFailAtXFor("b1"));
        }

        [Test]
        public void CheckTargetReached_FiresWhenTheEntityXReachesFailAtX() {
            var t = MakeTracker();
            t.Init(new[] { Obj("stop_zep", "destroyById", "zeppelin", "zep_01", 40f) }, 0f);
            Assert.IsNull(t.CheckTargetReached("zep_01", 40.01f), "still right of the target");
            Assert.AreEqual("stop_zep", t.CheckTargetReached("zep_01", 40f));
            Assert.AreEqual("stop_zep", t.CheckTargetReached("zep_01", -3f), "past it");
            Assert.IsNull(t.CheckTargetReached("other", 0f), "another entity's x is not the target's");
            Assert.IsNull(t.CheckTargetReached("", 0f));
        }

        [Test]
        public void CheckTargetReached_DestroyedFirst_NeverFails() {
            var t = MakeTracker();
            t.Init(new[] { Obj("stop_bomber", "destroyById", "bomber", "bomber_01", 20f) }, 0f);
            Assert.IsTrue(t.OnTargetDestroyed("bomber_01"));
            Assert.IsNull(t.CheckTargetReached("bomber_01", -1000f), "a completed objective can't fail");
        }

        [Test]
        public void CheckTargetReached_NeverFiresForMissionsWithoutFailAtX() {
            foreach (int m in new[] { 1, 3, 4 }) {
                var t = TrackerFor(m);
                foreach (var o in LoadMission(m).objectives) {
                    Assert.IsNull(t.CheckTargetReached(o.targetId, -100000f), $"mission {m} has failAtX -1 only");
                }
            }
        }

        // ── surviveToEnd ────────────────────────────────────────────────

        [Test]
        public void SurviveToEnd_CompletesOnce_AtLength() {
            var t = MakeTracker();
            t.Init(new[] { Obj("survive", "surviveToEnd") }, 0f);
            t.OnCameraProgress(299.9f, 300f);
            Assert.IsFalse(t.AllComplete);
            for (int i = 0; i < 5; i++) t.OnCameraProgress(300f, 300f);
            Assert.IsTrue(t.AllComplete);
            Assert.AreEqual(1, _completed.Count, "completed once, not every frame");
            var f = typeof(ObjectiveTracker).GetField("_completedCount", Private);
            Assert.AreEqual(1, (int)f.GetValue(t));
        }

        [Test]
        public void Init_ResetsEvaluatedAndCounts() {
            var data = LoadMission(2);
            var t = MakeTracker();
            t.Init(data.objectives, 0f, data.spawns);
            string id = data.objectives[0].targetId;
            t.OnTargetDestroyed(id);
            Assert.IsTrue(t.AllComplete);

            t.Init(data.objectives, 0f, data.spawns);   // same entries reused
            Assert.IsFalse(data.objectives[0].evaluated);
            Assert.IsFalse(t.AllComplete);
            Assert.AreEqual("destroy_bomber", t.CheckTargetReached(id, data.objectives[0].failAtX));
        }

        [Test]
        public void EmptyObjectives_AreTriviallyComplete() {
            var t = MakeTracker();
            t.Init(null, 0f);
            Assert.IsTrue(t.AllComplete, "documents current behaviour: no objectives = complete");
            Assert.IsNull(t.CheckTargetReached("anything", -1e6f));
            Assert.AreEqual(-1f, t.GetFailAtXFor("anything"));
        }

        // ══ MissionRunner wiring ════════════════════════════════════════

        /// <summary>A MissionRunner with a test camera and seeded GameManager / FxManager /
        /// ScoreKeeper singletons. Dispose restores everything.</summary>
        private sealed class RunnerFixture : IDisposable {
            public readonly MissionRunner Runner;
            public readonly GameObject RunnerGo;
            public readonly Camera Cam;
            public readonly GameManager Gm;
            public readonly ScoreKeeper Sk;
            public readonly List<bool> Ended = new List<bool>();

            private readonly GameObject _camGo, _gmGo, _fxGo, _skGo;
            private readonly List<Camera> _disabled = new List<Camera>();
            private readonly Action<bool, string> _onEnded;
            private readonly bool _hadUnlocked;
            private readonly int _unlocked;
            private readonly float _timeScale;
            private readonly object _prevSk;

            public RunnerFixture() {
                _hadUnlocked = PlayerPrefs.HasKey("unlocked_mission");
                _unlocked = PlayerPrefs.GetInt("unlocked_mission", 1);
                _timeScale = Time.timeScale;

                foreach (var c in Camera.allCameras) {
                    if (c != null && c.CompareTag("MainCamera")) { c.enabled = false; _disabled.Add(c); }
                }
                _camGo = new GameObject("T936_Camera");
                _camGo.tag = "MainCamera";
                Cam = _camGo.AddComponent<Camera>();
                Cam.orthographic = true;
                Cam.orthographicSize = 5f;
                Cam.aspect = 16f / 9f;
                _camGo.transform.position = new Vector3(0f, 0f, -10f);

                _gmGo = new GameObject("T936_GameManager");
                Gm = _gmGo.AddComponent<GameManager>();   // no Awake in EditMode: state Menu, no scene hook
                typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, Gm);

                _fxGo = new GameObject("T936_Fx");
                typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, _fxGo.AddComponent<FxManager>());

                var skField = typeof(ScoreKeeper).GetField("_instance", PrivateStatic);
                _prevSk = skField.GetValue(null);
                _skGo = new GameObject("T936_ScoreKeeper");
                Sk = _skGo.AddComponent<ScoreKeeper>();   // not subscribed (no Awake): score stays 0
                skField.SetValue(null, Sk);

                _onEnded = (won, reason) => Ended.Add(won);
                GameEvents.MissionEnded += _onEnded;

                // T962: controllers leaked by another fixture (EditMode never runs their Awake,
                // so Instance stays null) would otherwise be picked up instead of this one.
                DestroyAll<ScrollController>();
                ScrollController.Instance = null;
                RunnerGo = new GameObject("T936_MissionRunner");
                Runner = RunnerGo.AddComponent<MissionRunner>();
            }

            public bool MissionEnded => (bool)typeof(MissionRunner).GetField("_missionEnded", Private).GetValue(Runner);

            public ObjectiveTracker Tracker => RunnerGo.GetComponent<ObjectiveTracker>();

            public void InvokeUpdate() {
                typeof(MissionRunner).GetMethod("Update", Private).Invoke(Runner, null);
            }

            public void SetCamX(float x) {
                var p = Cam.transform.position;
                Cam.transform.position = new Vector3(x, p.y, p.z);
            }

            public void Dispose() {
                GameEvents.MissionEnded -= _onEnded;
                typeof(MissionRunner).GetMethod("OnDestroy", Private)?.Invoke(Runner, null);

                DestroyAll<PlayerPlane>();
                DestroyAll<TerrainStrip>();
                DestroyAll<ParallaxBackground>();
                DestroyAll<StaticObstacle>();
                DestroyAll<BomberAI>();
                DestroyAll<FighterAI>();
                DestroyAll<ZeppelinAI>();
                DestroyAll<EnemyBase>();
                DestroyAll<PlayerHQ>();
                if (ScrollController.Instance != null) Object.DestroyImmediate(ScrollController.Instance.gameObject);
                DestroyAll<ScrollController>();
                ScrollController.Instance = null;
                Object.DestroyImmediate(RunnerGo);

                foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                    if (p != null) Object.DestroyImmediate(p.gameObject);
                }
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                    if (go == null) continue;
                    bool pool = go.hideFlags == HideFlags.HideAndDontSave && go.name.StartsWith("Pool<");
                    bool fx = go.name == "Explosion" && go.scene.IsValid();
                    if (pool || fx) Object.DestroyImmediate(go);
                }

                typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, null);
                typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, null);
                typeof(ScoreKeeper).GetField("_instance", PrivateStatic).SetValue(null, _prevSk);
                Object.DestroyImmediate(_gmGo);
                Object.DestroyImmediate(_fxGo);
                Object.DestroyImmediate(_skGo);
                Object.DestroyImmediate(_camGo);
                foreach (var c in _disabled) if (c != null) c.enabled = true;

                if (_hadUnlocked) PlayerPrefs.SetInt("unlocked_mission", _unlocked);
                else PlayerPrefs.DeleteKey("unlocked_mission");
                PlayerPrefs.Save();
                Time.timeScale = _timeScale;
            }

            private static void DestroyAll<T>() where T : Component {
                foreach (var c in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                    if (c != null) Object.DestroyImmediate(c.gameObject);
                }
            }
        }

        private static int StaticSubscriptions(Type owner, string eventField, object target) {
            var f = owner.GetField(eventField, PrivateStatic);
            Assert.IsNotNull(f, $"{owner.Name}.{eventField} backing field not found by reflection");
            var d = f.GetValue(null) as Delegate;
            if (d == null) return 0;
            int n = 0;
            foreach (var h in d.GetInvocationList()) if (ReferenceEquals(h.Target, target)) n++;
            return n;
        }

        [Test]
        public void Runner_Next_OneSubscriptionEach_AndKillsCountedOnce_M1WinsOnce() {
            using (var fx = new RunnerFixture()) {
                fx.Runner.StartMission(1);
                fx.Runner.StartMission(2);
                fx.Runner.StartMission(1);   // Next/Retry without a scene reload
                Assert.AreEqual(1, StaticSubscriptions(typeof(GameEvents), "EntityDestroyed", fx.Runner));
                Assert.AreEqual(1, StaticSubscriptions(typeof(GameEvents), "MissionEnded", fx.Runner));
                Assert.AreEqual(1, StaticSubscriptions(typeof(EnemyBase), "Destroyed", fx.Runner));

                int total = SpawnTotal(1, "fighter");
                // B050: reinforcement kills count too, each exactly once.
                for (int i = 0; i < total - 4; i++) GameEvents.RaiseEntityDestroyed("fighter", Team.Enemy);
                for (int i = 0; i < 3; i++) GameEvents.RaiseEntityDestroyed(ReinforcementKey, Team.Enemy);
                Assert.AreEqual(total - 1, IntArray(fx.Tracker, "_kills")[0], "each fighter and reinforcement kill counted exactly once");
                Assert.IsFalse(fx.MissionEnded);

                GameEvents.RaiseEntityDestroyed("fighter", Team.Enemy);
                Assert.IsTrue(fx.MissionEnded);
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { true }, fx.Ended, "MissionEnded(won) raised once");
                Assert.GreaterOrEqual(SaveSystem.GetUnlockedMission(), 2, "a M1 win unlocks M2");

                // Late kills and the per-frame checks after the win change nothing.
                GameEvents.RaiseEntityDestroyed("fighter", Team.Enemy);
                fx.InvokeUpdate();
                Assert.AreEqual(1, fx.Ended.Count);
            }
        }

        [Test]
        public void Runner_Teardown_RemovesEnemyBaseSubscription() {
            using (var fx = new RunnerFixture()) {
                fx.Runner.StartMission(4);
                Assert.AreEqual(1, StaticSubscriptions(typeof(EnemyBase), "Destroyed", fx.Runner));
                typeof(MissionRunner).GetMethod("OnDestroy", Private).Invoke(fx.Runner, null);
                Assert.AreEqual(0, StaticSubscriptions(typeof(EnemyBase), "Destroyed", fx.Runner));
                Assert.AreEqual(0, StaticSubscriptions(typeof(GameEvents), "EntityDestroyed", fx.Runner));
            }
        }

        [Test]
        public void Runner_M2_DestroyById_OnlyTheTrackedBomberWins() {
            using (var fx = new RunnerFixture()) {
                fx.Runner.StartMission(2);
                fx.Runner.SpawnEntities(new SpawnEntry { type = "bomber", atX = 50, count = 1, id = M2BomberId, pattern = "straight" });
                fx.Runner.SpawnEntities(new SpawnEntry { type = "bomber", atX = 60, count = 1, id = "", pattern = "straight" });
                var bombers = Object.FindObjectsByType<BomberAI>(FindObjectsSortMode.None);
                Assert.AreEqual(2, bombers.Length);

                // The tracked one is the first spawned: find it through the runner's id list.
                var idTargets = (System.Collections.IList)typeof(MissionRunner).GetField("_idTargets", Private).GetValue(fx.Runner);
                Assert.AreEqual(1, idTargets.Count, "only spawns with an id are tracked");
                var tracked = (Health)idTargets[0].GetType().GetField("Target").GetValue(idTargets[0]);
                Health other = null;
                foreach (var b in bombers) {
                    var h = b.GetComponent<Health>();
                    if (h != tracked) other = h;
                }
                Assert.IsNotNull(other);
                foreach (var h in new[] { tracked, other }) {
                    typeof(Health).GetMethod("Awake", Private).Invoke(h, null);   // reads TeamTag Enemy
                    h.SetMaxHitPoints(200f);
                }

                other.ApplyDamage(1f, DamageSource.Collision);
                Assert.IsTrue(other.IsDead);
                Assert.IsFalse(fx.MissionEnded, "an untracked bomber's death is not bomber_01's");

                tracked.ApplyDamage(1f, DamageSource.Collision);
                Assert.IsTrue(fx.MissionEnded);
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { true }, fx.Ended);

                // failAtX after the win: nothing more happens.
                fx.SetCamX(380f);
                fx.InvokeUpdate();
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                Assert.AreEqual(1, fx.Ended.Count);
            }
        }

        // ── B018: entity-based failAtX ──────────────────────────────────

        /// <summary>Spawns the tracked bomber "bomber_01" off the right screen edge (camera at
        /// x 0, so its x is about 10.4..16.4) after replacing M2's objective with one whose
        /// failAtX is `failAtX`. Returns its BomberAI.</summary>
        private static BomberAI SpawnTrackedBomber(RunnerFixture fx, float failAtX) {
            fx.Runner.StartMission(2);
            fx.Tracker.Init(new[] { Obj("destroy_bomber", "destroyById", "bomber", "bomber_01", failAtX) }, 0f);
            fx.Runner.SpawnEntities(new SpawnEntry { type = "bomber", atX = 50, count = 1, id = "bomber_01", pattern = "straight" });
            var bomber = Object.FindFirstObjectByType<BomberAI>();
            Assert.IsNotNull(bomber);
            Assert.Greater(bomber.transform.position.x, failAtX, "the bomber spawns right of its target");
            return bomber;
        }

        private static void MoveX(Component c, float x) {
            var p = c.transform.position;
            c.transform.position = new Vector3(x, p.y, p.z);
        }

        [Test]
        public void Runner_M2_CameraAtOrPastFailAtX_DoesNotLose() {
            using (var fx = new RunnerFixture()) {
                var data = LoadMission(2);
                float failAtX = data.objectives[0].failAtX;
                fx.Runner.StartMission(2);
                BindScroll(fx.Cam);

                fx.SetCamX(failAtX);
                fx.InvokeUpdate();
                fx.SetCamX(Mathf.Min(failAtX + 10f, data.length - 1f));
                fx.InvokeUpdate();
                fx.SetCamX(0f);   // turned back: nothing changes either way
                fx.InvokeUpdate();
                Assert.IsFalse(fx.MissionEnded, "the camera reaching failAtX no longer loses the mission");
                Assert.AreEqual(0, fx.Ended.Count);
            }
        }

        [Test]
        public void Runner_BomberReachingTargetX_LosesOnce_CameraIrrelevant() {
            using (var fx = new RunnerFixture()) {
                var bomber = SpawnTrackedBomber(fx, 5f);
                Assert.AreEqual(5f, bomber.TargetX, "MissionRunner gives the bomber its objective failAtX");
                Assert.IsTrue(bomber.HasTargetX);

                fx.SetCamX(300f);   // the camera is far past x 5: irrelevant
                MoveX(bomber, 5.5f);
                fx.InvokeUpdate();
                Assert.IsFalse(fx.MissionEnded, "the bomber is still right of its target");
                Assert.IsFalse(bomber.ReachedTarget);

                fx.SetCamX(0f);     // turning back doesn't save the player either
                MoveX(bomber, 5f);
                Assert.IsTrue(bomber.ReachedTarget);
                fx.InvokeUpdate();
                Assert.IsTrue(fx.MissionEnded);
                Assert.AreEqual(GameManager.GameState.Lost, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { false }, fx.Ended);

                fx.InvokeUpdate();
                fx.Runner.HandleObjectiveComplete("late");
                Assert.AreEqual(1, fx.Ended.Count, "the loss is handled once");
            }
        }

        [Test]
        public void Runner_BomberDestroyedBeforeTargetX_WinsAndNeverLoses() {
            using (var fx = new RunnerFixture()) {
                var bomber = SpawnTrackedBomber(fx, 5f);
                var h = bomber.GetComponent<Health>();
                typeof(Health).GetMethod("Awake", Private).Invoke(h, null);   // reads TeamTag Enemy
                h.SetMaxHitPoints(200f);

                h.ApplyDamage(1f, DamageSource.Collision);
                Assert.IsTrue(h.IsDead);
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);

                // The dead bomber (Destroy is deferred) drifting past its target loses nothing.
                MoveX(bomber, 0f);
                fx.InvokeUpdate();
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { true }, fx.Ended);
            }
        }

        [Test]
        public void Runner_ZeppelinReachingTargetX_Loses() {
            using (var fx = new RunnerFixture()) {
                fx.Runner.StartMission(5);
                fx.Tracker.Init(new[] { Obj("destroy_zeppelin", "destroyById", "zeppelin", "zeppelin_01", 40f) }, 0f);
                fx.Runner.SpawnEntities(new SpawnEntry { type = "zeppelin", atX = 60, y = 1f, count = 1, id = "zeppelin_01", pattern = "straight" });
                var zep = Object.FindFirstObjectByType<ZeppelinAI>();
                Assert.IsNotNull(zep);
                Assert.AreEqual(40f, zep.TargetX);

                MoveX(zep, 40.5f);
                fx.InvokeUpdate();
                Assert.IsFalse(fx.MissionEnded);

                MoveX(zep, 40f);
                fx.InvokeUpdate();
                Assert.IsTrue(fx.MissionEnded);
                Assert.AreEqual(GameManager.GameState.Lost, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { false }, fx.Ended);
            }
        }

        [Test]
        public void Runner_SpawnAlreadyPastTargetX_IsIgnored_NoInstantLoss() {
            using (var fx = new RunnerFixture()) {
                fx.Runner.StartMission(5);
                // failAtX 100 is right of the spawn at 60: a data error, warned and ignored.
                fx.Tracker.Init(new[] { Obj("destroy_zeppelin", "destroyById", "zeppelin", "zeppelin_01", 100f) }, 0f);
                fx.Runner.SpawnEntities(new SpawnEntry { type = "zeppelin", atX = 60, y = 1f, count = 1, id = "zeppelin_01", pattern = "straight" });
                var zep = Object.FindFirstObjectByType<ZeppelinAI>();
                Assert.IsNotNull(zep);
                Assert.IsFalse(zep.HasTargetX);
                Assert.AreEqual(-1f, zep.TargetX);

                MoveX(zep, 0f);
                fx.InvokeUpdate();
                Assert.IsFalse(fx.MissionEnded, "no instant loss from a target behind the spawn");
            }
        }

        [Test]
        public void Zeppelin_WithTargetAhead_NeverDespawnsBehindCamera_WithoutTargetItDoes() {
            using (var fx = new RunnerFixture()) {
                LogAssert.ignoreFailingMessages = true;   // Destroy() in edit mode logs an error
                var fixedUpdate = typeof(ZeppelinAI).GetMethod("FixedUpdate", Private);
                var destroyed = typeof(ZeppelinAI).GetField("_destroyed", Private);

                // Camera at 200: left edge ~191, despawn line ~171, the zeppelin at 60 is far behind.
                fx.SetCamX(200f);

                var withTarget = ZeppelinAI.Spawn(new Vector3(60f, 1f, 0f), "z_target");
                withTarget.GetComponent<Rigidbody2D>().position = new Vector2(60f, 1f);
                withTarget.SetTargetX(40f);
                fixedUpdate.Invoke(withTarget, null);
                Assert.IsFalse((bool)destroyed.GetValue(withTarget), "en route to its target: kept alive");

                var noTarget = ZeppelinAI.Spawn(new Vector3(60f, 1f, 0f), "z_free");
                noTarget.GetComponent<Rigidbody2D>().position = new Vector2(60f, 1f);
                noTarget.SetTargetX(-1f);
                fixedUpdate.Invoke(noTarget, null);
                Assert.IsTrue((bool)destroyed.GetValue(noTarget), "no target: despawned behind the camera as before");
            }
        }

        [Test]
        public void Runner_M5_DespawnedZeppelinNeverCounts_MapEndLoses() {
            using (var fx = new RunnerFixture()) {
                var data = LoadMission(5);
                fx.Runner.StartMission(5);
                BindScroll(fx.Cam);
                fx.Tracker.Init(new[] { Obj("destroy_zeppelin", "destroyById", "zeppelin", "zeppelin_01", 40f) }, 0f);
                fx.Runner.SpawnEntities(new SpawnEntry { type = "zeppelin", atX = 60, y = 5f, count = 1, id = "zeppelin_01", pattern = "straight" });
                var zep = Object.FindFirstObjectByType<ZeppelinAI>();
                Assert.IsNotNull(zep);
                Assert.AreEqual("zeppelin [zeppelin_01]", zep.gameObject.name, "FighterAI escortOf finds it by name");

                // Silent despawn (Unity-null), then some other "zeppelin" event: must not count.
                Object.DestroyImmediate(zep.gameObject);
                GameEvents.RaiseEntityDestroyed(ZeppelinAI.ScoreKey, Team.Enemy);
                Assert.IsFalse(fx.MissionEnded, "a despawned zeppelin is not a kill");

                fx.SetCamX(data.length - 1f);
                fx.InvokeUpdate();
                Assert.IsFalse(fx.MissionEnded, "a despawned target can't reach its targetX");

                fx.SetCamX(data.length);
                fx.InvokeUpdate();
                Assert.IsTrue(fx.MissionEnded, "the map end still ends the mission");
                Assert.AreEqual(GameManager.GameState.Lost, fx.Gm.CurrentState);
            }
        }

        [Test]
        public void Runner_M5_ZeppelinKill_WinsOnce_TurretKillsDontCount() {
            using (var fx = new RunnerFixture()) {
                fx.Runner.StartMission(5);
                fx.Runner.SpawnEntities(new SpawnEntry { type = "zeppelin", atX = 60, y = 5f, count = 1, id = M5ZeppelinId, pattern = "straight" });
                var zep = Object.FindFirstObjectByType<ZeppelinAI>();
                Assert.IsNotNull(zep);
                LogAssert.ignoreFailingMessages = true;   // Destroy() in edit mode logs an error; not under test

                var turrets = zep.GetComponentsInChildren<ZeppelinTurret>();
                Assert.AreEqual(3, turrets.Length);
                foreach (var t in turrets) t.ApplyDamage(1f, DamageSource.Collision);
                Assert.AreEqual(0, zep.TurretsRemaining);
                Assert.IsFalse(fx.MissionEnded, "turret kills are not the zeppelin");

                zep.ApplyDamage(1f, DamageSource.Collision);
                Assert.IsTrue(zep.IsDead);
                Assert.IsTrue(fx.MissionEnded);
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { true }, fx.Ended);
            }
        }

        [Test]
        public void Runner_M4_BaseBombedFirst_ThenLastBalloonWins() {
            int balloons = SpawnTotal(4, "barrageBalloon");
            Assert.Greater(balloons, 0);
            using (var fx = new RunnerFixture()) {
                fx.Runner.StartMission(4);
                fx.Runner.SpawnEntities(new SpawnEntry { type = "enemyBase", atX = 460, count = 1, id = "enemy_base_01", pattern = "static" });
                var b = Object.FindFirstObjectByType<EnemyBase>();
                Assert.IsNotNull(b);
                Assert.AreEqual("enemy_base_01", b.Id);
                LogAssert.ignoreFailingMessages = true;   // Destroy() in edit mode logs an error

                b.ApplyDamage(25f, DamageSource.Bullet);
                Assert.IsFalse(b.IsDead, "bullets don't hurt the base");
                var g = GameBalance.Get();
                b.ApplyDamage(g != null && g.player != null ? g.player.bombDamage : 100f, DamageSource.Bomb);
                Assert.IsTrue(b.IsDead && b.KilledByBomb);
                Assert.IsFalse(fx.MissionEnded, "balloons still up");

                for (int i = 0; i < balloons - 1; i++) GameEvents.RaiseEntityDestroyed("barrageBalloon", Team.Enemy);
                Assert.IsFalse(fx.MissionEnded);
                GameEvents.RaiseEntityDestroyed("barrageBalloon", Team.Enemy);
                Assert.IsTrue(fx.MissionEnded);
                CollectionAssert.AreEqual(new[] { true }, fx.Ended);
            }
        }
    }
}
