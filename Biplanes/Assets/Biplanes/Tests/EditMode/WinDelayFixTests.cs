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
    /// B070 (QA T972 P2s): side effects of the B069 kill-win delay.
    ///  1. The result's "Best:" is the best before this run: ScoreKeeper captures it at mission
    ///     start, so the win-time save no longer leaks into the label.
    ///  2. The player's engine/smoke loops keep running in the win window and stop when the
    ///     controls end (PlayerPlane.HoldAudioOnWin / EndMissionAudio).
    ///  3. The minimap stays visible in Won until the result screen opens.
    /// </summary>
    public class WinDelayFixTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly Dictionary<string, int?> _prefs = new Dictionary<string, int?>();
        private static readonly string[] PrefKeys = { "unlocked_mission", "best_score_1", "best_score_2", "best_score_3" };

        [SetUp]
        public void SetUp() {
            _prefs.Clear();
            foreach (var k in PrefKeys) _prefs[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : (int?)null;
        }

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
            foreach (var kv in _prefs) {
                if (kv.Value.HasValue) PlayerPrefs.SetInt(kv.Key, kv.Value.Value);
                else PlayerPrefs.DeleteKey(kv.Key);
            }
            PlayerPrefs.Save();
        }

        // ── 1. Previous best ────────────────────────────────────────────

        [Test]
        public void BestToShow_CapturedForThisMission_ElseSaved() {
            Assert.AreEqual(300, ScoreKeeper.BestToShow(2, 2, 300, 900));
            Assert.AreEqual(900, ScoreKeeper.BestToShow(3, 2, 300, 900), "captured for another mission");
            Assert.AreEqual(900, ScoreKeeper.BestToShow(2, 0, 0, 900), "nothing captured");
            Assert.AreEqual(0, ScoreKeeper.BestToShow(2, 2, 0, 500), "first run: no previous best, even after the save");
        }

        [Test]
        public void ScoreKeeper_CapturedBest_SurvivesTheWinTimeSave() {
            PlayerPrefs.SetInt("best_score_2", 300);
            PlayerPrefs.SetInt("best_score_3", 700);
            var go = new GameObject("B070_ScoreKeeper");
            _created.Add(go);
            var sk = go.AddComponent<ScoreKeeper>();

            sk.CapturePreviousBest(2);
            Assert.AreEqual(2, sk.CapturedMission);
            Assert.AreEqual(300, sk.CapturedPreviousBest);

            SaveSystem.SaveBestScore(2, 900);   // OnMissionEnded at the win moment
            Assert.AreEqual(900, SaveSystem.GetBestScore(2));
            Assert.AreEqual(300, sk.PreviousBestFor(2), "the label shows the best before this run");
            Assert.AreEqual(700, sk.PreviousBestFor(3), "other missions read the saved best");
        }

        [Test]
        public void Runner_M2_KillWin_PreviousBestUnchangedWhenTheScreenOpensLater() {
            PlayerPrefs.SetInt("best_score_2", 100);
            using (var fx = new Fixture()) {
                fx.Start(2);
                Assert.AreEqual(100, fx.Score.PreviousBestFor(2));

                KillBomber(fx.SpawnMissionBomber());
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                int score = fx.Score.CurrentScore;
                Assert.Greater(score, 100);
                Assert.AreEqual(score, SaveSystem.GetBestScore(2), "the win saves the new best at once");
                Assert.AreEqual(100, fx.Score.PreviousBestFor(2), "the delayed result still shows the old best");
            }
        }

        // ── 2. Engine / smoke loops ─────────────────────────────────────

        [Test]
        public void PlayerPlane_HeldWin_KeepsAudio_UntilEndMissionAudio() {
            var plane = MakeBarePlane();
            plane.HoldAudioOnWin = true;
            InvokeMissionEndedAudio(plane, true);
            Assert.IsFalse(plane.AudioStopped, "loops keep running in the win window");

            plane.EndMissionAudio();
            Assert.IsTrue(plane.AudioStopped, "stopped when the controls end");
            Assert.IsFalse(plane.HoldAudioOnWin);
        }

        [Test]
        public void PlayerPlane_HeldButLost_OrUnheldWin_StopsAtOnce() {
            var lost = MakeBarePlane();
            lost.HoldAudioOnWin = true;
            InvokeMissionEndedAudio(lost, false);
            Assert.IsTrue(lost.AudioStopped, "a loss stops the loops at once");

            var won = MakeBarePlane();
            InvokeMissionEndedAudio(won, true);
            Assert.IsTrue(won.AudioStopped, "an undelayed win stops at once, as before");
        }

        [Test]
        public void Runner_M2_KillWin_AudioRunsUntilControlEnds() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                KillBomber(fx.SpawnMissionBomber());
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                Assert.IsTrue(fx.Player.HoldAudioOnWin);
                Assert.IsFalse(fx.Player.AudioStopped, "engine still heard while flying on");

                Assert.IsTrue(fx.TickControlEnd(Time.realtimeSinceStartup + 10f));
                Assert.IsTrue(fx.Player.AudioStopped, "loops stop with the controls");
            }
        }

        [Test]
        public void Runner_SurviveWin_AudioStopsAtOnce() {
            using (var fx = new Fixture()) {
                fx.Start(1);
                fx.Tracker.Init(new[] { new ObjectiveEntry { id = "survive", type = "surviveToEnd" } }, 0f);
                var text = Resources.Load<TextAsset>("Missions/mission_01");
                fx.SetCamX(JsonUtility.FromJson<MissionData>(text.text).length);
                fx.InvokeUpdate();
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                Assert.IsTrue(fx.Player.AudioStopped);
            }
        }

        // ── 3. Minimap ──────────────────────────────────────────────────

        [Test]
        public void Minimap_VisibleInWonUntilTheResultOpens() {
            Assert.IsTrue(MinimapView.VisibleIn(GameManager.GameState.Playing, false));
            Assert.IsTrue(MinimapView.VisibleIn(GameManager.GameState.Paused, false));
            Assert.IsTrue(MinimapView.VisibleIn(GameManager.GameState.Won, false), "win window: still flying");
            Assert.IsFalse(MinimapView.VisibleIn(GameManager.GameState.Won, true), "hidden once the result opens");
            Assert.IsFalse(MinimapView.VisibleIn(GameManager.GameState.Lost, false));
            Assert.IsFalse(MinimapView.VisibleIn(GameManager.GameState.Lost, true));
            Assert.IsFalse(MinimapView.VisibleIn(GameManager.GameState.Menu, false));
            Assert.IsFalse(MinimapView.VisibleIn(GameManager.GameState.Briefing, false));
            // The single-argument form keeps its old meaning.
            Assert.IsFalse(MinimapView.VisibleIn(GameManager.GameState.Won));
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private PlayerPlane MakeBarePlane() {
            var go = new GameObject("B070_Plane");
            _created.Add(go);
            return go.AddComponent<PlayerPlane>();   // no Awake: not crashed, no AudioManager
        }

        private static void InvokeMissionEndedAudio(PlayerPlane plane, bool won) {
            var m = typeof(PlayerPlane).GetMethod("OnMissionEndedAudio", Private);
            Assert.IsNotNull(m, "PlayerPlane.OnMissionEndedAudio not found");
            m.Invoke(plane, new object[] { won, won ? "won" : "lost" });
        }

        private static void KillBomber(BomberAI bomber) {
            var h = bomber.GetComponent<Health>();
            float bullet = GameBalance.Get().player.bulletDamage;
            while (!h.IsDead) h.ApplyDamage(bullet, DamageSource.Bullet);
        }

        // ── Fixture (pattern of WinDelayTests / BomberCrashQaTests) ────

        private sealed class Fixture : IDisposable {
            public readonly MissionRunner Runner;
            public readonly GameObject RunnerGo;
            public readonly Camera Cam;
            public readonly GameManager Gm;
            public readonly ScoreKeeper Score;

            private readonly GameObject _camGo, _gmGo, _fxGo, _skGo;
            private readonly List<Camera> _disabled = new List<Camera>();
            private readonly object _prevSk;
            private readonly float _timeScale;

            public Fixture() {
                _timeScale = Time.timeScale;
                foreach (var c in Camera.allCameras) {
                    if (c != null && c.CompareTag("MainCamera")) { c.enabled = false; _disabled.Add(c); }
                }
                _camGo = new GameObject("B070_Camera");
                _camGo.tag = "MainCamera";
                Cam = _camGo.AddComponent<Camera>();
                Cam.orthographic = true;
                Cam.orthographicSize = 5f;
                Cam.aspect = 16f / 9f;
                _camGo.transform.position = new Vector3(0f, 0f, -10f);

                _gmGo = new GameObject("B070_GameManager");
                Gm = _gmGo.AddComponent<GameManager>();
                typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, Gm);

                _fxGo = new GameObject("B070_Fx");
                typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, _fxGo.AddComponent<FxManager>());

                var skField = typeof(ScoreKeeper).GetField("_instance", PrivateStatic);
                _prevSk = skField.GetValue(null);
                skField.SetValue(null, null);
                _skGo = new GameObject("B070_ScoreKeeper");
                Score = _skGo.AddComponent<ScoreKeeper>();
                // Awake sets the singleton and subscribes to EntityDestroyed (not run in EditMode).
                typeof(ScoreKeeper).GetMethod("Awake", Private).Invoke(Score, null);

                ScrollController.Instance = null;
                RunnerGo = new GameObject("B070_MissionRunner");
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
