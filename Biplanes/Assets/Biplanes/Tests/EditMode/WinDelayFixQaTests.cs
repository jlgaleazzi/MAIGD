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
using Biplanes.UI;
using Biplanes.Weapons;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// QA T973: gap tests for B070. Unlike WinDelayFixTests, the player's Start() runs here,
    /// so PlayerPlane really is subscribed to GameEvents.MissionEnded (OnMissionEndedAudio):
    /// the "audio keeps running in the win window" check is not trivially true.
    /// Also covers the previous-best capture on Retry/Next, a first clear, and the loss paths.
    /// </summary>
    public class WinDelayFixQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly Dictionary<string, int?> _prefs = new Dictionary<string, int?>();
        private static readonly string[] PrefKeys = { "unlocked_mission", "best_score_1", "best_score_2", "best_score_3" };

        [SetUp]
        public void SetUp() {
            _prefs.Clear();
            foreach (var k in PrefKeys) _prefs[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : (int?)null;
        }

        [TearDown]
        public void TearDown() {
            foreach (var kv in _prefs) {
                if (kv.Value.HasValue) PlayerPrefs.SetInt(kv.Key, kv.Value.Value);
                else PlayerPrefs.DeleteKey(kv.Key);
            }
            PlayerPrefs.Save();
        }

        // ── Previous best ───────────────────────────────────────────────

        [Test]
        public void FirstClear_PreviousBestIsZero_SoTheBestLineStaysHidden() {
            PlayerPrefs.DeleteKey("best_score_2");
            using (var fx = new Fixture()) {
                fx.Start(2);
                KillBomber(fx.SpawnMissionBomber());
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                Assert.Greater(SaveSystem.GetBestScore(2), 0, "the win saved this run's score");
                // ResultView.Show: _bestScoreText active only when bestScore > 0.
                Assert.AreEqual(0, fx.Score.PreviousBestFor(2), "first clear: no previous best to show");
            }
        }

        [Test]
        public void RestartSameMission_RecapturesTheNewRecord_AndAnotherMissionCapturesItsOwn() {
            PlayerPrefs.SetInt("best_score_2", 100);
            PlayerPrefs.SetInt("best_score_3", 777);
            using (var fx = new Fixture()) {
                fx.Start(2);
                KillBomber(fx.SpawnMissionBomber());
                int record = fx.Score.CurrentScore;
                Assert.Greater(record, 100);
                Assert.AreEqual(100, fx.Score.PreviousBestFor(2));

                // Retry (the scene reload ends in MissionRunner.StartMission again).
                fx.Start(2);
                Assert.AreEqual(0, fx.Score.CurrentScore, "score reset");
                Assert.AreEqual(2, fx.Score.CapturedMission);
                Assert.AreEqual(record, fx.Score.PreviousBestFor(2), "the retry's previous best is the record just set");

                // Next / a different mission.
                fx.Start(3);
                Assert.AreEqual(3, fx.Score.CapturedMission);
                Assert.AreEqual(777, fx.Score.PreviousBestFor(3));
                Assert.AreEqual(record, fx.Score.PreviousBestFor(2), "another mission reads the saved best");
            }
        }

        [Test]
        public void Loss_DoesNotSave_PreviousBestEqualsSaved() {
            PlayerPrefs.SetInt("best_score_2", 400);
            using (var fx = new Fixture()) {
                fx.Start(2);
                fx.FailObjective();
                Assert.AreEqual(GameManager.GameState.Lost, fx.Gm.CurrentState);
                Assert.AreEqual(400, fx.Score.PreviousBestFor(2));
                Assert.AreEqual(400, SaveSystem.GetBestScore(2));
            }
        }

        // ── Engine / smoke loops with the real MissionEnded subscription ─

        [Test]
        public void SubscribedPlane_KillWin_AudioHeldUntilControlEnds_ThenStopsOnce() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                fx.SubscribePlayer();
                KillBomber(fx.SpawnMissionBomber());
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                Assert.IsTrue(fx.Player.HoldAudioOnWin, "held for the kill-win window");
                Assert.IsFalse(fx.Player.AudioStopped, "MissionEnded(true) reached the plane but the loops keep running");

                Assert.IsFalse(fx.TickControlEnd(Time.realtimeSinceStartup), "not due yet");
                Assert.IsFalse(fx.Player.AudioStopped);

                Assert.IsTrue(fx.TickControlEnd(Time.realtimeSinceStartup + 10f));
                Assert.IsTrue(fx.Player.AudioStopped, "stopped with the controls");
                Assert.IsFalse(fx.Player.HoldAudioOnWin);
                Assert.IsFalse(fx.TickControlEnd(Time.realtimeSinceStartup + 20f), "the control end runs once");
                Assert.IsTrue(fx.Player.AudioStopped);
            }
        }

        [Test]
        public void SubscribedPlane_ObjectiveLoss_StopsAtOnce() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                fx.SubscribePlayer();
                fx.FailObjective();
                Assert.AreEqual(GameManager.GameState.Lost, fx.Gm.CurrentState);
                Assert.IsFalse(fx.Player.HoldAudioOnWin);
                Assert.IsTrue(fx.Player.AudioStopped, "a loss stops the loops at once");
            }
        }

        [Test]
        public void SubscribedPlane_CrashLoss_KeepsTheWindDown() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                fx.SubscribePlayer();
                fx.Player.ForceCrash();
                Assert.AreEqual(GameManager.GameState.Lost, fx.Gm.CurrentState);
                Assert.IsFalse(fx.Player.AudioStopped, "a crashing plane keeps its engine wind-down");
                Assert.IsFalse(fx.Player.HoldAudioOnWin);
                Assert.IsFalse(MinimapView.VisibleIn(fx.Gm.CurrentState, false), "minimap hides at once on a loss");
            }
        }

        [Test]
        public void SubscribedPlane_CrashInsideTheWinWindow_StaysWon_KeepsTheWindDown() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                fx.SubscribePlayer();
                KillBomber(fx.SpawnMissionBomber());
                fx.Player.ForceCrash();
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                Assert.IsTrue(fx.TickControlEnd(Time.realtimeSinceStartup + 10f));
                Assert.IsFalse(fx.Player.AudioStopped, "EndMissionAudio leaves a crashing plane's wind-down");
                Assert.IsFalse(fx.Player.HoldAudioOnWin);
            }
        }

        [Test]
        public void NextMission_NewPlaneStartsUnheld() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                fx.SubscribePlayer();
                KillBomber(fx.SpawnMissionBomber());
                Assert.IsTrue(fx.Player.HoldAudioOnWin);
                fx.Start(3);
                Assert.IsFalse(fx.Player.HoldAudioOnWin);
                Assert.IsFalse(fx.Player.AudioStopped);
                Assert.IsFalse(fx.TickControlEnd(Time.realtimeSinceStartup + 10f), "teardown cleared the pending control end");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static void KillBomber(BomberAI bomber) {
            var h = bomber.GetComponent<Health>();
            float bullet = GameBalance.Get().player.bulletDamage;
            while (!h.IsDead) h.ApplyDamage(bullet, DamageSource.Bullet);
        }

        private sealed class Fixture : IDisposable {
            public readonly MissionRunner Runner;
            public readonly GameObject RunnerGo;
            public readonly Camera Cam;
            public readonly GameManager Gm;
            public readonly ScoreKeeper Score;

            private readonly GameObject _camGo, _gmGo, _fxGo, _skGo;
            private readonly List<Camera> _disabled = new List<Camera>();
            private readonly List<PlayerPlane> _subscribed = new List<PlayerPlane>();
            private readonly object _prevSk;
            private readonly float _timeScale;

            public Fixture() {
                _timeScale = Time.timeScale;
                foreach (var c in Camera.allCameras) {
                    if (c != null && c.CompareTag("MainCamera")) { c.enabled = false; _disabled.Add(c); }
                }
                _camGo = new GameObject("T973_Camera");
                _camGo.tag = "MainCamera";
                Cam = _camGo.AddComponent<Camera>();
                Cam.orthographic = true;
                Cam.orthographicSize = 5f;
                Cam.aspect = 16f / 9f;
                _camGo.transform.position = new Vector3(0f, 0f, -10f);

                _gmGo = new GameObject("T973_GameManager");
                Gm = _gmGo.AddComponent<GameManager>();
                typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, Gm);

                _fxGo = new GameObject("T973_Fx");
                typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, _fxGo.AddComponent<FxManager>());

                var skField = typeof(ScoreKeeper).GetField("_instance", PrivateStatic);
                _prevSk = skField.GetValue(null);
                skField.SetValue(null, null);
                _skGo = new GameObject("T973_ScoreKeeper");
                Score = _skGo.AddComponent<ScoreKeeper>();
                typeof(ScoreKeeper).GetMethod("Awake", Private).Invoke(Score, null);

                ScrollController.Instance = null;
                RunnerGo = new GameObject("T973_MissionRunner");
                Runner = RunnerGo.AddComponent<MissionRunner>();
            }

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

            /// <summary>Runs PlayerPlane.Start (not called in EditMode), which subscribes
            /// OnMissionEndedAudio to GameEvents.MissionEnded.</summary>
            public void SubscribePlayer() {
                var p = Player;
                Assert.IsNotNull(p);
                typeof(PlayerPlane).GetMethod("Start", Private).Invoke(p, null);
                _subscribed.Add(p);
            }

            public void FailObjective() {
                var m = typeof(MissionRunner).GetMethod("HandleObjectiveFailed", Private);
                Assert.IsNotNull(m, "MissionRunner.HandleObjectiveFailed not found");
                m.Invoke(Runner, new object[] { "qa_fail", "QA forced loss" });
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
                // Unsubscribe the static MissionEnded / input handlers of every started plane.
                var onDestroy = typeof(PlayerPlane).GetMethod("OnDestroy", Private);
                foreach (var p in _subscribed) {
                    if (!ReferenceEquals(p, null)) onDestroy.Invoke(p, null);
                }
                _subscribed.Clear();

                typeof(MissionRunner).GetMethod("OnDestroy", Private)?.Invoke(Runner, null);
                typeof(ScoreKeeper).GetMethod("OnDestroy", Private)?.Invoke(Score, null);

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
                DestroyAll<BarrageBalloon>();
                DestroyAll<AAAGun>();
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
