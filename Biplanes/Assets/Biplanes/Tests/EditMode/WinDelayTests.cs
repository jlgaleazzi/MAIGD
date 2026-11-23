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
    /// B069: a win decided by a kill opens the result screen killWinDelaySeconds (real time)
    /// later through GameManager.WinMission(reason, delay), so the bomber's explosion, fall
    /// and ground burst play out. The state is Won at once (no later loss, no second end),
    /// the player keeps control until the deadline, and other win paths stay instant.
    ///
    /// EditMode: ShowResultView opens no UI, so the tests check state, events and the
    /// deferred control end (MissionRunner.TickWinControlEnd by reflection).
    /// </summary>
    public class WinDelayTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        // ── Pure helpers ────────────────────────────────────────────────

        [Test]
        public void WinResultDelayFor_KillPathsDelay_OthersInstant() {
            Assert.AreEqual(2.5f, MissionRunner.WinResultDelayFor(MissionRunner.WinFromKill, 2.5f), 1e-6f);
            Assert.AreEqual(2.5f, MissionRunner.WinResultDelayFor(MissionRunner.WinFromBombTarget, 2.5f), 1e-6f);
            Assert.AreEqual(0f, MissionRunner.WinResultDelayFor("survive_to_end", 2.5f));
            Assert.AreEqual(0f, MissionRunner.WinResultDelayFor(null, 2.5f));
            Assert.AreEqual(0f, MissionRunner.WinResultDelayFor(MissionRunner.WinFromKill, -1f), "never negative");
        }

        [Test]
        public void KillWinDelay_BalanceIs2_5_DefaultWhenMissing() {
            var g = GameBalance.Get();
            Assume.That(g != null, "Balance.json not loadable");
            Assert.AreEqual(2.5f, g.killWinDelaySeconds, 1e-6f, "Balance.json killWinDelaySeconds");
            Assert.AreEqual(2.5f, GameManager.KillWinDelayOrDefault(g), 1e-6f);
            Assert.AreEqual(GameManager.DefaultKillWinDelaySeconds, GameManager.KillWinDelayOrDefault(null));
            Assert.AreEqual(GameManager.DefaultKillWinDelaySeconds, GameManager.KillWinDelayOrDefault(new GameBalance()));
        }

        [Test]
        public void ControlEndDue_OnlyAtOrAfterTheDeadline() {
            Assert.IsFalse(MissionRunner.ControlEndDue(10f, -1f), "nothing pending");
            Assert.IsFalse(MissionRunner.ControlEndDue(9.9f, 10f));
            Assert.IsTrue(MissionRunner.ControlEndDue(10f, 10f));
            Assert.IsTrue(MissionRunner.ControlEndDue(12f, 10f));
        }

        // ── GameManager.WinMission ──────────────────────────────────────

        [Test]
        public void WinMission_WonAtOnce_LaterLossAndSecondWinIgnored() {
            var go = new GameObject("B069_GameManager");
            var won = new List<bool>();
            Action<bool, string> onEnded = (w, r) => won.Add(w);
            GameEvents.MissionEnded += onEnded;
            var field = typeof(GameManager).GetField("_instance", PrivateStatic);
            var prev = field.GetValue(null);
            try {
                var gm = go.AddComponent<GameManager>();
                field.SetValue(null, gm);
                gm.SetState(GameManager.GameState.Playing);

                gm.WinMission("", 2.5f);
                Assert.AreEqual(GameManager.GameState.Won, gm.CurrentState, "Won is decided at once");
                CollectionAssert.AreEqual(new[] { true }, won);

                gm.LoseMission(GameManager.DefaultLossReason, 1f);   // a crash during the delay
                gm.WinMission("again", 2.5f);
                Assert.AreEqual(GameManager.GameState.Won, gm.CurrentState);
                CollectionAssert.AreEqual(new[] { true }, won, "the mission ends once");
            } finally {
                GameEvents.MissionEnded -= onEnded;
                field.SetValue(null, prev);
                Object.DestroyImmediate(go);
            }
        }

        // ── MissionRunner: delayed control end ─────────────────────────

        [Test]
        public void Runner_M2_BomberKill_WonAtOnce_ControlKeptUntilTheDeadline() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                var input = fx.Player.GetComponent<PlayerInput>();
                Assert.IsTrue(input.enabled);

                var bomber = fx.SpawnMissionBomber();
                var h = bomber.GetComponent<Health>();
                float bullet = GameBalance.Get().player.bulletDamage;
                while (!h.IsDead) h.ApplyDamage(bullet, DamageSource.Bullet);

                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { true }, fx.Won);
                Assert.IsTrue(input.enabled, "the player keeps flying while the wreck falls");
                Assert.Greater(ScrollController.Instance.ScrollSpeed, 0f, "the world keeps scrolling");

                fx.InvokeUpdate();   // real time has not reached the deadline
                Assert.IsTrue(input.enabled);

                // A crash during the delay can't flip the win.
                fx.Gm.LoseMission(GameManager.DefaultLossReason, 1f);
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                Assert.AreEqual(1, fx.Won.Count);

                Assert.IsTrue(fx.TickControlEnd(Time.realtimeSinceStartup + 10f), "deadline passed");
                Assert.IsFalse(input.enabled, "controls end when the result screen opens");
                Assert.AreEqual(0f, ScrollController.Instance.ScrollSpeed);
                Assert.IsFalse(fx.TickControlEnd(Time.realtimeSinceStartup + 20f), "only once");
            }
        }

        [Test]
        public void Runner_SurviveToEnd_StillEndsControlAtOnce() {
            using (var fx = new Fixture()) {
                fx.Start(1);
                fx.Tracker.Init(new[] { new ObjectiveEntry { id = "survive", type = "surviveToEnd" } }, 0f);
                var text = Resources.Load<TextAsset>("Missions/mission_01");
                fx.SetCamX(JsonUtility.FromJson<MissionData>(text.text).length);
                fx.InvokeUpdate();
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                Assert.IsFalse(fx.Player.GetComponent<PlayerInput>().enabled, "no delay on a non-kill win");
            }
        }

        [Test]
        public void Runner_NextMission_ClearsAPendingControlEnd() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                var bomber = fx.SpawnMissionBomber();
                bomber.GetComponent<Health>().ApplyDamage(1f, DamageSource.Collision);
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);

                fx.Gm.SetState(GameManager.GameState.Playing);
                fx.Start(1);   // Next without a reload
                var input = fx.Player.GetComponent<PlayerInput>();
                Assert.IsFalse(fx.TickControlEnd(Time.realtimeSinceStartup + 10f), "teardown dropped the deadline");
                Assert.IsTrue(input.enabled, "the new mission's plane keeps its controls");
            }
        }

        // ── Fixture (pattern of MissionEndRulesTests / BomberCrashQaTests) ─

        private sealed class Fixture : IDisposable {
            public readonly MissionRunner Runner;
            public readonly GameObject RunnerGo;
            public readonly Camera Cam;
            public readonly GameManager Gm;
            public readonly List<bool> Won = new List<bool>();

            private readonly GameObject _camGo, _gmGo, _fxGo, _skGo;
            private readonly List<Camera> _disabled = new List<Camera>();
            private readonly Action<bool, string> _onEnded;
            private readonly object _prevSk;
            private readonly float _timeScale;
            private readonly Dictionary<string, int?> _prefs = new Dictionary<string, int?>();
            private static readonly string[] PrefKeys = { "unlocked_mission", "best_score_1", "best_score_2" };

            public Fixture() {
                foreach (var k in PrefKeys) _prefs[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : (int?)null;
                _timeScale = Time.timeScale;

                foreach (var c in Camera.allCameras) {
                    if (c != null && c.CompareTag("MainCamera")) { c.enabled = false; _disabled.Add(c); }
                }
                _camGo = new GameObject("B069_Camera");
                _camGo.tag = "MainCamera";
                Cam = _camGo.AddComponent<Camera>();
                Cam.orthographic = true;
                Cam.orthographicSize = 5f;
                Cam.aspect = 16f / 9f;
                _camGo.transform.position = new Vector3(0f, 0f, -10f);

                _gmGo = new GameObject("B069_GameManager");
                Gm = _gmGo.AddComponent<GameManager>();
                typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, Gm);

                _fxGo = new GameObject("B069_Fx");
                typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, _fxGo.AddComponent<FxManager>());

                var skField = typeof(ScoreKeeper).GetField("_instance", PrivateStatic);
                _prevSk = skField.GetValue(null);
                _skGo = new GameObject("B069_ScoreKeeper");
                skField.SetValue(null, _skGo.AddComponent<ScoreKeeper>());

                _onEnded = (won, reason) => Won.Add(won);
                GameEvents.MissionEnded += _onEnded;

                ScrollController.Instance = null;
                RunnerGo = new GameObject("B069_MissionRunner");
                Runner = RunnerGo.AddComponent<MissionRunner>();
            }

            public ObjectiveTracker Tracker => RunnerGo.GetComponent<ObjectiveTracker>();

            public PlayerPlane Player => (PlayerPlane)typeof(MissionRunner).GetField("_playerPlane", Private).GetValue(Runner);

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

            public bool TickControlEnd(float now) {
                var m = typeof(MissionRunner).GetMethod("TickWinControlEnd", Private);
                Assert.IsNotNull(m, "MissionRunner.TickWinControlEnd not found");
                return (bool)m.Invoke(Runner, new object[] { now });
            }

            public void SetCamX(float x) {
                var p = Cam.transform.position;
                Cam.transform.position = new Vector3(x, p.y, p.z);
            }

            /// <summary>Mission 2's bomber_1 spawned through the runner, Awake run.</summary>
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
                typeof(Health).GetMethod("Awake", Private).Invoke(bomber.GetComponent<Health>(), null);
                typeof(BomberAI).GetMethod("Awake", Private).Invoke(bomber, null);
                return bomber;
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
            }

            private static void DestroyAll<T>() where T : Component {
                foreach (var c in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                    if (c != null) Object.DestroyImmediate(c.gameObject);
                }
            }
        }
    }
}
