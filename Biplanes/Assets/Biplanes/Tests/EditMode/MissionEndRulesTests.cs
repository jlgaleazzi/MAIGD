using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Fx;
using Biplanes.Missions;
using Biplanes.Player;
using Biplanes.Weapons;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// B017: EditMode tests for the mission end rules.
    ///  - Map end (GDD change log 2026-11-13): the camera at `length` with a non-surviveToEnd
    ///    objective open loses the mission with a reason; surviveToEnd still wins there.
    ///  - LoseMission(reason) reaches MissionEnded (B016), and any end disables PlayerInput
    ///    and stops the scroll.
    ///  - GameManager refuses Won→Lost and Lost→Won after an end (T936 P2-1).
    ///  - RetryMission queues the mission in _pendingMission before loading "Game" (B013).
    ///  - Health maps DamageSource.Heal without the unknown-source warning (T936 P2-3).
    ///
    /// GameManager, FxManager and ScoreKeeper are seeded by reflection like
    /// ObjectiveTrackerTests does, and "unlocked_mission" is backed up and restored.
    /// </summary>
    public class MissionEndRulesTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDownCreated() {
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        private ObjectiveTracker MakeTracker() {
            var go = new GameObject("B017_Tracker");
            _created.Add(go);
            return go.AddComponent<ObjectiveTracker>();
        }

        private static MissionData LoadMission(int index) {
            var text = Resources.Load<TextAsset>($"Missions/mission_{index:D2}");
            Assert.IsNotNull(text, $"mission_{index:D2}.json not found in Resources/Missions");
            var data = JsonUtility.FromJson<MissionData>(text.text);
            Assert.IsNotNull(data);
            return data;
        }

        private static ObjectiveEntry Objective(string id, string type, string targetType = "", string targetId = "") {
            return new ObjectiveEntry { id = id, type = type, targetType = targetType, targetId = targetId, failAtX = -1f };
        }

        // ══ ObjectiveTracker.CheckMapEnd ════════════════════════════════

        [Test]
        public void MapEnd_M1_OpenObjectiveFailsAtLengthOnly() {
            var data = LoadMission(1);
            var t = MakeTracker();
            t.Init(data.objectives, 0f, data.spawns);
            Assert.IsNull(t.CheckMapEnd(data.length - 0.1f, data.length), "not at the end yet");
            Assert.AreEqual("kill_all_fighters", t.CheckMapEnd(data.length, data.length));
            Assert.AreEqual("kill_all_fighters", t.CheckMapEnd(data.length + 5f, data.length));
        }

        [Test]
        public void MapEnd_CompletedObjectives_DontFail() {
            var data = LoadMission(2);
            var t = MakeTracker();
            t.Init(data.objectives, 0f, data.spawns);
            // T938: the id comes from the JSON (V003 renamed bomber_01 to bomber_1).
            t.OnTargetDestroyed(data.objectives[0].targetId);
            Assert.IsTrue(t.AllComplete);
            Assert.IsNull(t.CheckMapEnd(data.length, data.length));
        }

        [Test]
        public void MapEnd_SurviveToEndOnly_NeverFails() {
            var t = MakeTracker();
            t.Init(new[] { Objective("survive", "surviveToEnd") }, 0f);
            Assert.IsNull(t.CheckMapEnd(300f, 300f), "surviveToEnd is not an open objective at the end");
            t.OnCameraProgress(300f, 300f);
            Assert.IsTrue(t.AllComplete);
        }

        [Test]
        public void MapEnd_SurvivePlusKill_FailsOnTheKillObjective() {
            var t = MakeTracker();
            t.Init(new[] { Objective("survive", "surviveToEnd"), Objective("kill", "destroyById", "bomber", "b1") }, 0f);
            t.OnCameraProgress(300f, 300f);
            Assert.IsFalse(t.AllComplete);
            Assert.AreEqual("kill", t.CheckMapEnd(300f, 300f));
        }

        [Test]
        public void MapEnd_NoLength_NeverFails() {
            var t = MakeTracker();
            t.Init(new[] { Objective("kill", "destroyAllOfType", "fighter") }, 0f);
            Assert.IsNull(t.CheckMapEnd(1e6f, 0f));
        }

        [Test]
        public void GetDescriptionFor_UsesObjectiveText() {
            var t = MakeTracker();
            t.Init(new[] { Objective("kill", "destroyAllOfType", "fighter") }, 0f);
            Assert.AreEqual("Destroy all fighters", t.GetDescriptionFor("kill"));
            Assert.AreEqual("nope", t.GetDescriptionFor("nope"));
        }

        [Test]
        public void GetDescriptionFor_ReadsTypesNotRawIds() {
            var t = MakeTracker();
            t.Init(new[] {
                Objective("b", "destroyById", "bomber", "bomber_1"),
                Objective("z", "destroyById", "zeppelin", "airship_01"),
                Objective("a", "destroyAllOfType", "aaa"),
                Objective("base", "bombTarget", "enemyBase", "enemy_base_01"),
            }, 0f);
            Assert.AreEqual("Destroy the enemy bomber", t.GetDescriptionFor("b"));
            Assert.AreEqual("Destroy the enemy zeppelin", t.GetDescriptionFor("z"));
            Assert.AreEqual("Destroy all AAA guns", t.GetDescriptionFor("a"));
            Assert.AreEqual("Bomb the enemy base", t.GetDescriptionFor("base"));
            foreach (var key in new[] { "b", "z", "a", "base" }) {
                StringAssert.DoesNotContain("_", t.GetDescriptionFor(key), "no raw spawn id in the text");
            }
        }

        // ══ MissionRunner + GameManager ═════════════════════════════════

        /// <summary>A MissionRunner with a test camera and seeded GameManager / FxManager /
        /// ScoreKeeper singletons. Records every MissionEnded (won, reason).</summary>
        private sealed class Fixture : IDisposable {
            public readonly MissionRunner Runner;
            public readonly GameObject RunnerGo;
            public readonly Camera Cam;
            public readonly GameManager Gm;
            public readonly List<bool> Won = new List<bool>();
            public readonly List<string> Reasons = new List<string>();

            private readonly GameObject _camGo, _gmGo, _fxGo, _skGo;
            private readonly List<Camera> _disabled = new List<Camera>();
            private readonly Action<bool, string> _onEnded;
            private readonly bool _hadUnlocked;
            private readonly int _unlocked;
            private readonly float _timeScale;
            private readonly object _prevSk;

            public Fixture() {
                _hadUnlocked = PlayerPrefs.HasKey("unlocked_mission");
                _unlocked = PlayerPrefs.GetInt("unlocked_mission", 1);
                _timeScale = Time.timeScale;

                foreach (var c in Camera.allCameras) {
                    if (c != null && c.CompareTag("MainCamera")) { c.enabled = false; _disabled.Add(c); }
                }
                _camGo = new GameObject("B017_Camera");
                _camGo.tag = "MainCamera";
                Cam = _camGo.AddComponent<Camera>();
                Cam.orthographic = true;
                Cam.orthographicSize = 5f;
                Cam.aspect = 16f / 9f;
                _camGo.transform.position = new Vector3(0f, 0f, -10f);

                _gmGo = new GameObject("B017_GameManager");
                Gm = _gmGo.AddComponent<GameManager>();   // no Awake in EditMode: state Menu
                typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, Gm);

                _fxGo = new GameObject("B017_Fx");
                typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, _fxGo.AddComponent<FxManager>());

                var skField = typeof(ScoreKeeper).GetField("_instance", PrivateStatic);
                _prevSk = skField.GetValue(null);
                _skGo = new GameObject("B017_ScoreKeeper");
                skField.SetValue(null, _skGo.AddComponent<ScoreKeeper>());

                _onEnded = (won, reason) => { Won.Add(won); Reasons.Add(reason); };
                GameEvents.MissionEnded += _onEnded;

                ScrollController.Instance = null;
                RunnerGo = new GameObject("B017_MissionRunner");
                Runner = RunnerGo.AddComponent<MissionRunner>();
            }

            public bool MissionEnded => (bool)typeof(MissionRunner).GetField("_missionEnded", Private).GetValue(Runner);

            public ObjectiveTracker Tracker => RunnerGo.GetComponent<ObjectiveTracker>();

            public PlayerPlane Player => (PlayerPlane)typeof(MissionRunner).GetField("_playerPlane", Private).GetValue(Runner);

            /// <summary>StartMission, then make sure ScrollController.Instance is set (its
            /// Awake, which sets it, may not run for AddComponent outside Play mode).</summary>
            public void Start(int mission) {
                Runner.StartMission(mission);
                if (ScrollController.Instance == null) {
                    ScrollController.Instance = Object.FindFirstObjectByType<ScrollController>();
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

        [Test]
        public void Runner_M1_MapEndWithFightersLeft_LosesOnceWithReason() {
            using (var fx = new Fixture()) {
                fx.Start(1);
                float length = LoadMission(1).length;

                fx.SetCamX(length - 0.5f);
                fx.InvokeUpdate();
                Assert.IsFalse(fx.MissionEnded, "not at the map end yet");

                fx.SetCamX(length);
                fx.InvokeUpdate();
                Assert.IsTrue(fx.MissionEnded);
                Assert.AreEqual(GameManager.GameState.Lost, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { false }, fx.Won);
                StringAssert.Contains("end of the map", fx.Reasons[0]);
                StringAssert.Contains("fighter", fx.Reasons[0]);

                fx.InvokeUpdate();
                Assert.AreEqual(1, fx.Won.Count, "the loss is handled once");
            }
        }

        [Test]
        public void Runner_M3_MapEndWithAaaLeft_Loses() {
            using (var fx = new Fixture()) {
                fx.Start(3);
                fx.SetCamX(LoadMission(3).length);
                fx.InvokeUpdate();
                Assert.AreEqual(GameManager.GameState.Lost, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { false }, fx.Won);
            }
        }

        [Test]
        public void Runner_SurviveToEnd_WinsAtMapEnd() {
            using (var fx = new Fixture()) {
                fx.Start(1);
                fx.Tracker.Init(new[] { Objective("survive", "surviveToEnd") }, 0f);
                fx.SetCamX(LoadMission(1).length);
                fx.InvokeUpdate();
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { true }, fx.Won);
            }
        }

        [Test]
        public void Runner_TargetReached_ReasonIsNotPlayerCrashed() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                // B018: failAtX is the world x the bomber itself must not reach.
                fx.Tracker.Init(new[] {
                    new ObjectiveEntry { id = "destroy_bomber", type = "destroyById", targetType = "bomber", targetId = "bomber_01", failAtX = 5f }
                }, 0f);
                fx.Runner.SpawnEntities(new SpawnEntry { type = "bomber", atX = 50, count = 1, id = "bomber_01", pattern = "straight" });
                var bomber = Object.FindFirstObjectByType<BomberAI>();
                Assert.IsNotNull(bomber);
                var p = bomber.transform.position;
                bomber.transform.position = new Vector3(4f, p.y, p.z);

                fx.InvokeUpdate();
                Assert.AreEqual(GameManager.GameState.Lost, fx.Gm.CurrentState);
                Assert.AreEqual(1, fx.Reasons.Count);
                Assert.AreNotEqual(GameManager.DefaultLossReason, fx.Reasons[0]);
                StringAssert.Contains("Objective failed", fx.Reasons[0]);
                StringAssert.Contains("reached its target", fx.Reasons[0]);
            }
        }

        [Test]
        public void Runner_AnyEnd_DisablesPlayerInputAndStopsScroll() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                var input = fx.Player.GetComponent<PlayerInput>();
                Assert.IsNotNull(input);
                Assert.IsTrue(input.enabled);
                Assert.Greater(ScrollController.Instance.ScrollSpeed, 0f);

                fx.SetCamX(LoadMission(2).length);   // map-end loss, not a crash
                fx.InvokeUpdate();
                Assert.AreEqual(GameManager.GameState.Lost, fx.Gm.CurrentState);
                Assert.IsFalse(input.enabled, "no input after an objective loss");
                Assert.AreEqual(0f, ScrollController.Instance.ScrollSpeed);

                // Next (StartMission without a reload) gives a fresh plane with live input.
                fx.Start(1);
                Assert.IsTrue(fx.Player.GetComponent<PlayerInput>().enabled);
                Assert.Greater(ScrollController.Instance.ScrollSpeed, 0f);
            }
        }

        [Test]
        public void Runner_Win_AlsoDisablesInput() {
            using (var fx = new Fixture()) {
                fx.Start(1);
                fx.Tracker.Init(new[] { Objective("survive", "surviveToEnd") }, 0f);
                fx.SetCamX(LoadMission(1).length);
                fx.InvokeUpdate();
                Assert.IsFalse(fx.Player.GetComponent<PlayerInput>().enabled);
            }
        }

        [Test]
        public void GameManager_RefusesLostAfterWon_AndWonAfterLost() {
            using (var fx = new Fixture()) {
                fx.Gm.SetState(GameManager.GameState.Playing);
                fx.Gm.SetState(GameManager.GameState.Won);
                fx.Gm.SetState(GameManager.GameState.Lost);        // crash after the win
                fx.Gm.LoseMission("late loss");
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { true }, fx.Won);

                fx.Gm.SetState(GameManager.GameState.Playing);     // a new mission starts
                fx.Gm.LoseMission("Test reason");
                fx.Gm.SetState(GameManager.GameState.Won);         // late win after the loss
                Assert.AreEqual(GameManager.GameState.Lost, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { true, false }, fx.Won);
                Assert.AreEqual("Test reason", fx.Reasons[1]);
            }
        }

        [Test]
        public void GameManager_SetStateLost_KeepsCrashReason() {
            using (var fx = new Fixture()) {
                fx.Gm.SetState(GameManager.GameState.Playing);
                fx.Gm.SetState(GameManager.GameState.Lost);
                CollectionAssert.AreEqual(new[] { GameManager.DefaultLossReason }, fx.Reasons);
            }
        }

        [Test]
        public void RetryMission_QueuesPendingMissionBeforeLoadingGame() {
            using (var fx = new Fixture()) {
                fx.Gm.SetState(GameManager.GameState.Playing);
                fx.Gm.LoseMission("crash");
                Time.timeScale = 0f;
                // LoadScene can't run outside Play mode; don't fail on what it may log.
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
                try {
                    fx.Gm.RetryMission(3);
                } catch (Exception) {
                    // SceneManager.LoadScene only works in Play mode; the state before it is what we check.
                }
                Assert.AreEqual(3, (int)typeof(GameManager).GetField("_pendingMission", Private).GetValue(fx.Gm),
                    "OnSceneLoaded(\"Game\") starts the mission from _pendingMission");
                Assert.AreEqual(3, fx.Gm.ActiveMissionIndex);
                Assert.AreEqual(1f, Time.timeScale, "Retry unfreezes time");
                Assert.AreEqual(GameManager.GameState.Lost, fx.Gm.CurrentState,
                    "Playing is set by StartMission after the reload, not before it");
                typeof(GameManager).GetField("_pendingMission", Private).SetValue(fx.Gm, 0);
            }
        }

        [Test]
        public void GameManager_GameSceneName_IsGame() {
            var f = typeof(GameManager).GetField("GameSceneName", PrivateStatic);
            Assert.IsNotNull(f);
            Assert.AreEqual("Game", f.GetValue(null));
        }

        // ══ Health: Heal source ═════════════════════════════════════════

        [Test]
        public void Health_Heal_NoUnknownSourceWarning_AndRestores() {
            var go = new GameObject("B017_PlayerHealth");
            _created.Add(go);
            go.AddComponent<TeamTag>().team = Team.Player;
            var h = go.AddComponent<Health>();
            typeof(Health).GetMethod("Awake", Private).Invoke(h, null);

            h.ApplyDamage(2f, DamageSource.Bullet);
            float hurt = h.CurrentDamagePercent;
            Assert.Greater(hurt, 0f);

            var warnings = new List<string>();
            Application.LogCallback cb = (msg, stack, type) => {
                if (msg.Contains("unknown DamageSource")) warnings.Add(msg);
            };
            Application.logMessageReceived += cb;
            try {
                h.ApplyDamage(-1f, DamageSource.Heal);
            } finally {
                Application.logMessageReceived -= cb;
            }
            Assert.IsEmpty(warnings, "Heal is a known source");
            Assert.AreEqual(Mathf.Max(0f, hurt - 1f), h.CurrentDamagePercent, 1e-4f, "Heal applies 1:1");
        }
    }
}
