using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Missions;
using Biplanes.Player;
using Biplanes.Weapons;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// B078: the mission-5 HQ cheat. Typing H then Q (each key within 1.5 s, a wrong key
    /// resets) jumps the player and the camera to the player HQ. Covers the pure matcher, the
    /// gate, the spawn-skip rule, the placement maths, best-score suppression and one
    /// MissionRunner.JumpToHq run on mission 5 (EditMode: Update/FixedUpdate never run, so
    /// nothing spawns and the jump is called directly).
    /// </summary>
    public class CheatCodeTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        // ── CheatMatcher ────────────────────────────────────────────────

        [Test]
        public void Matcher_HThenQ_Completes() {
            var m = new CheatCodes.CheatMatcher(CheatCodes.HqSequence);
            Assert.IsFalse(m.Feed('H', 0f));
            Assert.AreEqual(1, m.Progress);
            Assert.IsTrue(m.Feed('Q', 0.5f), "H then Q completes the code");
            Assert.AreEqual(0, m.Progress, "the matcher resets after a match");
        }

        [Test]
        public void Matcher_LowerCase_Completes() {
            var m = new CheatCodes.CheatMatcher("HQ");
            m.Feed('h', 0f);
            Assert.IsTrue(m.Feed('q', 0.2f));
        }

        [Test]
        public void Matcher_WrongOrder_OrQAlone_DoesNotComplete() {
            var m = new CheatCodes.CheatMatcher("HQ");
            Assert.IsFalse(m.Feed('Q', 0f));
            Assert.IsFalse(m.Feed('H', 0.1f));
            Assert.AreEqual(1, m.Progress);
            Assert.IsFalse(m.Feed('H', 0.2f), "a repeated H restarts the attempt");
            Assert.IsTrue(m.Feed('Q', 0.3f), "HHQ still matches (the second H starts over)");
        }

        [Test]
        public void Matcher_WrongKeyInBetween_Resets() {
            var m = new CheatCodes.CheatMatcher("HQ");
            m.Feed('H', 0f);
            Assert.IsFalse(m.Feed('W', 0.1f));
            Assert.AreEqual(0, m.Progress, "a wrong key resets");
            Assert.IsFalse(m.Feed('Q', 0.2f));
            m.Feed('H', 0.3f);
            Assert.IsFalse(m.Feed(CheatCodes.OtherKey, 0.4f), "a non-letter key resets too");
            Assert.IsFalse(m.Feed('Q', 0.5f));
        }

        [Test]
        public void Matcher_GapLimit_1_5Seconds() {
            var m = new CheatCodes.CheatMatcher("HQ");
            m.Feed('H', 10f);
            Assert.IsTrue(m.Feed('Q', 11.5f), "exactly 1.5 s is still in time");

            m.Feed('H', 20f);
            Assert.IsFalse(m.Feed('Q', 21.6f), "more than 1.5 s after H: too late");
            Assert.AreEqual(0, m.Progress);
            Assert.AreEqual(1.5f, CheatCodes.CheatMatcher.DefaultMaxGap);
        }

        [Test]
        public void Matcher_Reset_ForgetsPartialInput() {
            var m = new CheatCodes.CheatMatcher("HQ");
            m.Feed('H', 0f);
            m.Reset();
            Assert.IsFalse(m.Feed('Q', 0.1f));
        }

        [Test]
        public void Sequence_KeysAreNotBoundToGameplay() {
            // W/A/S/D move, Space bombs, Escape pauses, the mouse fires: H and Q are free.
            foreach (char c in CheatCodes.HqSequence) {
                StringAssert.DoesNotContain(c.ToString(), "WASD");
            }
            Assert.AreEqual(5, CheatCodes.HqCheatMissionId);
        }

        // ── Gate ────────────────────────────────────────────────────────

        [Test]
        public void Gate_OnlyMission5WhilePlayingAirborne() {
            Assert.IsTrue(CheatCodes.CanUseCheat(5, true, true, true, false, false, false));
            for (int mission = 1; mission <= 4; mission++) {
                Assert.IsFalse(CheatCodes.CanUseCheat(mission, true, true, true, false, false, false),
                    $"ignored in mission {mission}");
            }
            Assert.IsFalse(CheatCodes.CanUseCheat(5, false, true, true, false, false, false), "paused/briefing/result/menu");
            Assert.IsFalse(CheatCodes.CanUseCheat(5, true, false, true, false, false, false), "before the start gate");
            Assert.IsFalse(CheatCodes.CanUseCheat(5, true, true, false, false, false, false), "no player");
            Assert.IsFalse(CheatCodes.CanUseCheat(5, true, true, true, true, false, false), "on the runway");
            Assert.IsFalse(CheatCodes.CanUseCheat(5, true, true, true, false, true, false), "crashed");
            Assert.IsFalse(CheatCodes.CanUseCheat(5, true, true, true, false, false, true), "mission ended");
        }

        // ── Spawn-skip rule ─────────────────────────────────────────────

        private static SpawnEntry Entry(string type, int atX, string id = "", string escortOf = "") {
            return new SpawnEntry { type = type, atX = atX, count = 1, id = id, escortOf = escortOf, pattern = "straight" };
        }

        [Test]
        public void Skip_PassedFillerSkipped_NeededKept_AheadUntouched() {
            const float half = 8.9f;
            var spawns = new[] {
                Entry("fighter", 100),                       // 0 passed: skipped
                Entry("aaa", 120),                           // 1 passed: skipped
                Entry("healthBalloon", 130),                 // 2 passed: skipped
                Entry("bombBalloon", 140),                   // 3 passed: skipped
                Entry("playerHQ", 252, "hq_01"),             // 4 passed (ground trigger 252-10.9): kept
                Entry("zeppelin", 200, "airship_01"),        // 5 passed: kept
                Entry("fighter", 210, "", "airship_01"),     // 6 escort passed: kept
                Entry("fighter", 300),                       // 7 ahead: untouched
                Entry("fighter", 90),                        // 8 already spawned
            };
            var flags = new bool[spawns.Length];
            flags[8] = true;
            var objectives = new[] {
                new ObjectiveEntry { id = "destroy_zeppelin", type = "destroyById", targetType = "zeppelin",
                                     targetId = "airship_01", failAtX = 252f }
            };

            int skipped = CheatCodes.SkipPassedSpawns(spawns, flags, objectives, 255.6f, half);

            Assert.AreEqual(4, skipped);
            Assert.IsTrue(flags[0] && flags[1] && flags[2] && flags[3], "passed filler spawns are skipped");
            Assert.IsFalse(flags[4], "the HQ still spawns");
            Assert.IsFalse(flags[5], "the zeppelin still spawns (normal atX)");
            Assert.IsFalse(flags[6], "its escorts still spawn");
            Assert.IsFalse(flags[7], "entries ahead of the new camera trigger normally later");
            Assert.IsTrue(flags[8]);
        }

        [Test]
        public void Keep_ObjectiveTargets_AndDestroyAllTypes() {
            var objectives = new[] {
                new ObjectiveEntry { id = "a", type = "destroyAllOfType", targetType = "aaa" },
                new ObjectiveEntry { id = "b", type = "bombTarget", targetType = "enemyBase", targetId = "base_01" },
            };
            Assert.IsTrue(CheatCodes.KeepOnJump(Entry("AAA", 10), objectives), "destroyAll type counts");
            Assert.IsTrue(CheatCodes.KeepOnJump(Entry("enemyBase", 10, "base_01"), objectives), "objective id");
            Assert.IsFalse(CheatCodes.KeepOnJump(Entry("fighter", 10), objectives));
            Assert.IsFalse(CheatCodes.KeepOnJump(Entry("tree", 10), null));
            Assert.IsFalse(CheatCodes.KeepOnJump(null, objectives));
        }

        [Test]
        public void Skip_SecondJump_SkipsNothingNew() {
            var spawns = new[] { Entry("fighter", 100), Entry("fighter", 200) };
            var flags = new bool[2];
            Assert.AreEqual(2, CheatCodes.SkipPassedSpawns(spawns, flags, null, 250f, 8.9f));
            Assert.AreEqual(0, CheatCodes.SkipPassedSpawns(spawns, flags, null, 250f, 8.9f));
        }

        // ── Placement maths ─────────────────────────────────────────────

        [Test]
        public void Facing_KeepsZeppelinAhead() {
            Assert.AreEqual(1, CheatCodes.FacingTowardThreat(252f, 294f));
            Assert.AreEqual(-1, CheatCodes.FacingTowardThreat(252f, 200f));
        }

        [Test]
        public void CameraX_PutsPlaneOnTrailingAnchor() {
            // Facing right with anchor 0.3: the plane sits 0.2 of the view width left of centre.
            Assert.AreEqual(252f + 0.4f * 10f, CheatCodes.CameraXForAnchor(252f, 1, 0.3f, 10f), 1e-4f);
            // Facing left: anchor 0.7, right of centre.
            Assert.AreEqual(252f - 0.4f * 10f, CheatCodes.CameraXForAnchor(252f, -1, 0.3f, 10f), 1e-4f);
        }

        [Test]
        public void WarpY_InsideFlightBand() {
            Assert.AreEqual(-3.5f + CheatCodes.WarpHeightAboveGround, CheatCodes.WarpY(-3.5f, 5f), 1e-4f);
            Assert.AreEqual(4f, CheatCodes.WarpY(2f, 5f), 1e-4f, "kept under the view top");
            Assert.AreEqual(4.5f + CheatCodes.WarpMinHeightAboveGround, CheatCodes.WarpY(4.5f, 5f), 1e-4f,
                "never below the minimum height above the ground");
        }

        // ── Best-score suppression ──────────────────────────────────────

        [Test]
        public void ShouldSaveBest_LockedNeverSaves() {
            Assert.IsTrue(SaveSystem.ShouldSaveBest(500, 100, false));
            Assert.IsFalse(SaveSystem.ShouldSaveBest(500, 100, true));
            Assert.IsFalse(SaveSystem.ShouldSaveBest(50, 100, false));
        }

        [Test]
        public void SaveBestScore_LockedRun_KeepsOldBest() {
            const string key = "best_score_5";
            bool had = PlayerPrefs.HasKey(key);
            int backup = PlayerPrefs.GetInt(key, 0);
            try {
                PlayerPrefs.SetInt(key, 100);
                SaveSystem.BestScoreLocked = true;
                SaveSystem.SaveBestScore(5, 99999);
                Assert.AreEqual(100, SaveSystem.GetBestScore(5), "a cheat run never sets a best score");

                SaveSystem.BestScoreLocked = false;
                SaveSystem.SaveBestScore(5, 200);
                Assert.AreEqual(200, SaveSystem.GetBestScore(5), "unlocked runs save as before");
            } finally {
                SaveSystem.BestScoreLocked = false;
                if (had) PlayerPrefs.SetInt(key, backup); else PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        // ── MissionRunner.JumpToHq on mission 5 ─────────────────────────

        private GameObject _camGo;
        private Camera _cam;
        private GameObject _runnerGo;
        private MissionRunner _runner;
        private readonly List<Camera> _disabledCameras = new List<Camera>();

        [SetUp]
        public void SetUp() {
            _disabledCameras.Clear();
            foreach (var c in Camera.allCameras) {
                if (c != null && c.CompareTag("MainCamera")) {
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
            }
            _camGo = new GameObject("B078_TestCamera");
            _camGo.tag = "MainCamera";
            _cam = _camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 5f;
            _cam.aspect = 16f / 9f;
            _camGo.transform.position = new Vector3(0f, 0f, -10f);
            ScrollController.Instance = null;
            SaveSystem.BestScoreLocked = false;
        }

        [TearDown]
        public void TearDown() {
            SaveSystem.BestScoreLocked = false;
            if (_runner != null) {
                typeof(MissionRunner).GetMethod("OnDestroy", Private)?.Invoke(_runner, null);
            }
            foreach (var p in Object.FindObjectsByType<PlayerPlane>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (p != null) Object.DestroyImmediate(p.gameObject);
            }
            foreach (var t in Object.FindObjectsByType<TerrainStrip>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            foreach (var b in Object.FindObjectsByType<ParallaxBackground>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (b != null) Object.DestroyImmediate(b.gameObject);
            }
            if (ScrollController.Instance != null) Object.DestroyImmediate(ScrollController.Instance.gameObject);
            ScrollController.Instance = null;
            if (_runnerGo != null) Object.DestroyImmediate(_runnerGo);
            _runner = null;
            if (_camGo != null) Object.DestroyImmediate(_camGo);

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
        }

        private void StartRunner(int mission) {
            if (_runner == null) {
                _runnerGo = new GameObject("B078_MissionRunner");
                _runner = _runnerGo.AddComponent<MissionRunner>();
            }
            _runner.StartMission(mission);
            Assert.IsNotNull(_runner.Player, "StartMission built no player");
            ScrollController.Instance.Release(); // past the B049 start gate
        }

        private int IndexOf(string type, int atX) {
            var spawns = _runner.MissionData.spawns;
            for (int i = 0; i < spawns.Length; i++) {
                if (spawns[i].type == type && spawns[i].atX == atX) return i;
            }
            Assert.Fail($"mission 5 has no {type} at {atX}");
            return -1;
        }

        [Test]
        public void JumpToHq_Mission5_PlacesPlaneCameraAndSkipsPassedSpawns() {
            StartRunner(5);
            var plane = _runner.Player;
            Assert.IsFalse(_runner.CheatUsed);

            Assert.IsTrue(_runner.JumpToHq());

            float half = _cam.orthographicSize * _cam.aspect;
            float expectedCam = CheatCodes.CameraXForAnchor(252f, 1, plane.ScreenAnchor, half);
            Assert.AreEqual(expectedCam, _cam.transform.position.x, 1e-3f, "HQ on the plane's screen anchor");
            Assert.AreEqual(252f, plane.transform.position.x, 1e-3f, "plane above the HQ");
            Assert.AreEqual(1, plane.Facing, "faces the zeppelin (to the right)");
            Assert.AreEqual(1, ScrollController.Instance.Direction, "scroll follows the new facing");
            float ground = TerrainStrip.Instance.HeightAt(252f);
            Assert.GreaterOrEqual(plane.transform.position.y, ground + CheatCodes.WarpMinHeightAboveGround - 1e-3f,
                "safe height above the terrain and the HQ");
            Assert.LessOrEqual(plane.transform.position.y, _cam.transform.position.y + _cam.orthographicSize,
                "inside the view");

            Assert.IsFalse(_runner.IsSpawnPending(IndexOf("fighter", 46)), "passed fighter groups are skipped");
            Assert.IsFalse(_runner.IsSpawnPending(IndexOf("aaa", 165)), "passed AAA is skipped");
            Assert.IsTrue(_runner.IsSpawnPending(IndexOf("playerHQ", 252)), "the HQ still spawns");
            Assert.IsTrue(_runner.IsSpawnPending(IndexOf("zeppelin", 294)), "the zeppelin still spawns");
            Assert.IsTrue(_runner.IsSpawnPending(IndexOf("fighter", 298)), "its escorts still spawn");
            Assert.IsTrue(_runner.IsSpawnPending(IndexOf("fighter", 277)), "groups ahead of the view trigger later");

            Assert.IsTrue(_runner.CheatUsed, "the run is flagged");
            Assert.IsTrue(SaveSystem.BestScoreLocked, "its score can't become a best score");

            // Idempotent: a second jump lands on the same spot.
            Assert.IsTrue(_runner.JumpToHq());
            Assert.AreEqual(expectedCam, _cam.transform.position.x, 1e-3f);
            Assert.AreEqual(252f, plane.transform.position.x, 1e-3f);
        }

        [Test]
        public void StartMission_ClearsCheatFlagAndLock() {
            StartRunner(5);
            Assert.IsTrue(_runner.JumpToHq());
            StartRunner(5);
            Assert.IsFalse(_runner.CheatUsed, "a new run starts clean");
            Assert.IsFalse(SaveSystem.BestScoreLocked);
        }

        [Test]
        public void JumpToHq_OnRunway_DoesNothing() {
            StartRunner(1); // runway start
            Assert.IsTrue(_runner.Player.OnRunway);
            Assert.IsFalse(_runner.JumpToHq());
            Assert.IsFalse(_runner.CheatUsed);
        }

        [Test]
        public void TryHqCheat_IgnoredOutsideMission5() {
            StartRunner(1);
            Assert.IsFalse(_runner.CanUseHqCheat, "missions 1-4 ignore the cheat");
            Assert.IsFalse(_runner.TryHqCheat());
            Assert.IsFalse(_runner.CheatUsed);
        }

        [Test]
        public void JumpToHq_NoHqInMission_DoesNothing() {
            StartRunner(2); // airborne start, no playerHQ spawn
            float camX = _cam.transform.position.x;
            Assert.IsFalse(_runner.JumpToHq(), "mission 2 has no playerHQ");
            Assert.AreEqual(camX, _cam.transform.position.x);
            Assert.IsFalse(_runner.CheatUsed);
        }
    }
}
