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
    /// QA T972 (verifies B069): gaps not covered by WinDelayTests / BomberRamTests.
    ///  - Mission 2 through MissionRunner: after the killing hit the bomber's own FixedUpdate
    ///    runs BeginCrash (time is not frozen), and its wreck lands (ground burst) well
    ///    inside killWinDelaySeconds, both from mission 2's spawn height and from the top
    ///    of the screen.
    ///  - The deferred control end is set killWinDelaySeconds after the kill.
    ///  - During the window: the map end, a pause request and a player crash change nothing
    ///    (still Won, one MissionEnded, controls kept until the deadline).
    ///  - A mutual ram (the 4th ram kills the bomber and crashes the player) is a win.
    /// </summary>
    public class WinDelayQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const float FixedDt = 0.02f;

        // ── Crash plays before the result screen ────────────────────────

        [Test]
        public void M2_Kill_BomberFixedUpdateCrashes_WreckLandsBeforeTheResultOpens() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                var bomber = fx.SpawnMissionBomber();
                var h = bomber.GetComponent<Health>();
                float bullet = GameBalance.Get().player.bulletDamage;

                float killedAt = Time.realtimeSinceStartup;
                while (!h.IsDead) h.ApplyDamage(bullet, DamageSource.Bullet);
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState, "won at the killing hit");
                Assert.AreEqual(1f, Time.timeScale, "time keeps running during the win delay");

                float delay = GameManager.KillWinDelayOrDefault(GameBalance.Get());
                float endAt = fx.EndControlAt;
                Assert.AreEqual(killedAt + delay, endAt, 0.25f, "controls end killWinDelaySeconds after the kill");

                // Next physics tick: the bomber's own FixedUpdate starts the crash.
                fx.Invoke(bomber, "FixedUpdate");
                Assert.IsTrue(bomber == null, "BeginCrash removed the bomber");
                var wrecks = Object.FindObjectsByType<BomberWreck>(FindObjectsSortMode.None);
                Assert.AreEqual(1, wrecks.Length, "one falling wreck");

                float t = FixedDt;   // the crash tick itself
                bool landed = false;
                while (t < 10f && !landed) {
                    landed = (bool)typeof(BomberWreck).GetMethod("Step", Private).Invoke(wrecks[0], new object[] { FixedDt });
                    t += FixedDt;
                }
                Assert.IsTrue(landed, "the wreck reached the ground");
                Assert.Less(t, delay - 0.5f, "ground burst plays with at least 0.5 s left before the result screen");
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                CollectionAssert.AreEqual(new[] { true }, fx.Won, "crash and landing don't end the mission again");
            }
        }

        [Test]
        public void Wreck_FromTheTopOfTheScreen_LandsInsideTheWinDelay() {
            // Worst case: camera top (ortho 5 around y 0) down to the bare ground base.
            var w = BomberWreck.Create(new Vector3(0f, 5f, 0f), null, true, 5, 0f, -1, new Vector2(-1.5f, 0f));
            try {
                float t = 0f;
                bool landed = false;
                while (t < 10f && !landed) {
                    landed = (bool)typeof(BomberWreck).GetMethod("Step", Private).Invoke(w, new object[] { FixedDt });
                    t += FixedDt;
                }
                Assert.IsTrue(landed);
                Assert.Less(t, GameManager.KillWinDelayOrDefault(GameBalance.Get()),
                    "even a wreck from the top of the screen lands before the result opens");
            } finally {
                BomberWreck.ClearAll();
                if (w != null) Object.DestroyImmediate(w.gameObject);
            }
        }

        // ── Nothing ends the mission again during the window ───────────

        [Test]
        public void DuringTheWindow_MapEnd_Pause_PlayerCrash_ChangeNothing() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                var bomber = fx.SpawnMissionBomber();
                bomber.GetComponent<Health>().ApplyDamage(1000f, DamageSource.Bullet);
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState);
                var input = fx.Player.GetComponent<PlayerInput>();

                // Esc while Won: PauseGame only acts in Playing.
                fx.Gm.PauseGame();
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState, "no pause over a decided win");
                Assert.AreEqual(1f, Time.timeScale);
                Assert.IsTrue(input.enabled, "pause did not take the controls");

                // The camera reaches the map end with the window still open.
                var text = Resources.Load<TextAsset>("Missions/mission_02");
                fx.SetCamX(JsonUtility.FromJson<MissionData>(text.text).length + 5f);
                fx.InvokeUpdate();
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState, "map end can't lose a won mission");

                // The player crashes into the ground during the window.
                fx.Player.GetComponent<Health>().ApplyDamage(1f, DamageSource.Collision);
                fx.Invoke(fx.Player, "FixedUpdate");
                Assert.IsTrue(fx.Player.HasCrashed);
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState, "a crash after the win is ignored");

                CollectionAssert.AreEqual(new[] { true }, fx.Won, "one MissionEnded(true), nothing else");
            }
        }

        [Test]
        public void MutualRam_FourthRamKillsBomberAndCrashesPlayer_IsAWin() {
            using (var fx = new Fixture()) {
                fx.Start(2);
                var bomber = fx.SpawnMissionBomber();
                var h = bomber.GetComponent<Health>();
                float ram = PlayerPlane.RamDamageFor(GameBalance.Get());
                h.ApplyDamage(h.MaxHitPoints - ram, DamageSource.Bullet);   // three rams' worth
                Assert.IsFalse(h.IsDead);

                var ph = fx.Player.GetComponent<Health>();
                ph.ApplyDamage(1f, DamageSource.PlaneCollision);
                ph.ApplyDamage(1f, DamageSource.PlaneCollision);   // two contacts taken already
                Assert.Less(ph.CurrentDamagePercent, 100f);

                fx.Invoke(fx.Player, "OnTriggerEnter2D", bomber.GetComponent<Collider2D>());

                Assert.IsTrue(h.IsDead, "the 4th ram kills the bomber");
                Assert.IsTrue(fx.Player.HasCrashed, "and the player's third contact crashes it");
                Assert.AreEqual(GameManager.GameState.Won, fx.Gm.CurrentState, "the kill lands first: a win");
                CollectionAssert.AreEqual(new[] { true }, fx.Won);
            }
        }

        // ── Fixture (pattern of WinDelayTests) ─────────────────────────

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
            private static readonly string[] PrefKeys = { "unlocked_mission", "best_score_1", "best_score_2", "best_score_3" };

            public Fixture() {
                foreach (var k in PrefKeys) _prefs[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : (int?)null;
                _timeScale = Time.timeScale;
                Time.timeScale = 1f;

                foreach (var c in Camera.allCameras) {
                    if (c != null && c.CompareTag("MainCamera")) { c.enabled = false; _disabled.Add(c); }
                }
                _camGo = new GameObject("T972_Camera");
                _camGo.tag = "MainCamera";
                Cam = _camGo.AddComponent<Camera>();
                Cam.orthographic = true;
                Cam.orthographicSize = 5f;
                Cam.aspect = 16f / 9f;
                _camGo.transform.position = new Vector3(0f, 0f, -10f);

                _gmGo = new GameObject("T972_GameManager");
                Gm = _gmGo.AddComponent<GameManager>();
                typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, Gm);
                Gm.SetState(GameManager.GameState.Playing);

                _fxGo = new GameObject("T972_Fx");
                typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, _fxGo.AddComponent<FxManager>());

                var skField = typeof(ScoreKeeper).GetField("_instance", PrivateStatic);
                _prevSk = skField.GetValue(null);
                _skGo = new GameObject("T972_ScoreKeeper");
                skField.SetValue(null, _skGo.AddComponent<ScoreKeeper>());

                _onEnded = (won, reason) => Won.Add(won);
                GameEvents.MissionEnded += _onEnded;

                ScrollController.Instance = null;
                RunnerGo = new GameObject("T972_MissionRunner");
                Runner = RunnerGo.AddComponent<MissionRunner>();
            }

            public PlayerPlane Player => (PlayerPlane)typeof(MissionRunner).GetField("_playerPlane", Private).GetValue(Runner);

            public float EndControlAt => (float)typeof(MissionRunner).GetField("_endControlAt", Private).GetValue(Runner);

            public void Start(int mission) {
                Runner.StartMission(mission);
                if (ScrollController.Instance == null) {
                    foreach (var sc in Object.FindObjectsByType<ScrollController>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                        if (sc != null && sc.Camera == Cam) { ScrollController.Instance = sc; break; }
                    }
                }
                Assert.IsNotNull(ScrollController.Instance, "StartMission creates a ScrollController");
            }

            public void Invoke(object target, string method, params object[] args) {
                var m = target.GetType().GetMethod(method, Private);
                Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found by reflection");
                m.Invoke(target, args.Length == 0 ? null : args);
            }

            public void InvokeUpdate() {
                typeof(MissionRunner).GetMethod("Update", Private).Invoke(Runner, null);
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
                Invoke(bomber.GetComponent<Health>(), "Awake");
                Invoke(bomber, "Awake");
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
                DestroyAll<BomberWreck>();
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
