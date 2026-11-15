using System;
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
    /// T926: EditMode tests for B001. MissionRunner.StartMission can run again without a scene
    /// reload (ResultView "Next"); the second start must tear the first mission down and start
    /// clean: one player, one ObjectiveTracker, one HUD canvas, one EntityDestroyed subscription,
    /// the camera back at x = 0, FarthestX = 0, and limits [0, length].
    ///
    /// Outside Play mode MissionRunner tears down with DestroyImmediate, so these tests can't
    /// see Play mode's deferred Destroy; they check the end state of the teardown.
    /// </summary>
    public class MissionRestartTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

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

            _camGo = new GameObject("T926_TestCamera");
            _camGo.tag = "MainCamera";
            _cam = _camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 5f;
            _cam.aspect = 16f / 9f;
            _camGo.transform.position = new Vector3(0f, 0f, -10f);

            ScrollController.Instance = null;
            _runnerGo = new GameObject("T926_MissionRunner");
            _runner = _runnerGo.AddComponent<MissionRunner>();
        }

        [TearDown]
        public void TearDown() {
            // OnDestroy doesn't run for DestroyImmediate in EditMode; call it so the static
            // GameEvents subscription doesn't leak into other tests.
            if (_runner != null) {
                var onDestroy = typeof(MissionRunner).GetMethod("OnDestroy", Private);
                onDestroy?.Invoke(_runner, null);
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
            foreach (var o in Object.FindObjectsByType<Biplanes.Enemies.StaticObstacle>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (o != null) Object.DestroyImmediate(o.gameObject);
            }
            if (ScrollController.Instance != null) Object.DestroyImmediate(ScrollController.Instance.gameObject);
            ScrollController.Instance = null;
            if (_runnerGo != null) Object.DestroyImmediate(_runnerGo);
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

        // ── Helpers ─────────────────────────────────────────────────────

        private static float MissionLength(int index) {
            var text = Resources.Load<TextAsset>($"Missions/mission_{index:D2}");
            Assert.IsNotNull(text, $"mission_{index:D2}.json not found in Resources/Missions");
            var data = JsonUtility.FromJson<MissionData>(text.text);
            Assert.IsNotNull(data);
            return data.length;
        }

        private static float ScrollField(string name) {
            var f = typeof(ScrollController).GetField(name, Private);
            Assert.IsNotNull(f, $"ScrollController.{name} not found by reflection");
            return (float)f.GetValue(ScrollController.Instance);
        }

        /// <summary>How many times this runner's handler is subscribed to GameEvents.EntityDestroyed.</summary>
        private int RunnerSubscriptions() {
            var f = typeof(GameEvents).GetField("EntityDestroyed", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "GameEvents.EntityDestroyed backing field not found by reflection");
            var d = f.GetValue(null) as Delegate;
            if (d == null) return 0;
            int n = 0;
            foreach (var h in d.GetInvocationList()) {
                if (ReferenceEquals(h.Target, _runner)) n++;
            }
            return n;
        }

        private int HudCanvases() {
            int n = 0;
            foreach (Transform child in _runnerGo.transform) {
                if (child.GetComponent<Canvas>() != null) n++;
            }
            return n;
        }

        private void SetCamX(float x) {
            var p = _cam.transform.position;
            _cam.transform.position = new Vector3(x, p.y, p.z);
        }

        private void AssertCleanStart(int missionIndex) {
            float length = MissionLength(missionIndex);
            var players = Object.FindObjectsByType<PlayerPlane>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.AreEqual(1, players.Length, "exactly one PlayerPlane");
            Assert.AreEqual(1, _runnerGo.GetComponents<ObjectiveTracker>().Length, "exactly one ObjectiveTracker");
            Assert.AreEqual(1, HudCanvases(), "exactly one HUD canvas");
            Assert.AreEqual(1, RunnerSubscriptions(), "exactly one EntityDestroyed subscription");
            Assert.AreEqual(1, Object.FindObjectsByType<ScrollController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                "exactly one ScrollController");

            Assert.AreEqual(0f, _cam.transform.position.x, 1e-5f, "camera back at the mission start");
            Assert.AreEqual(0f, _cam.transform.position.y, 1e-5f, "camera y kept");
            Assert.AreEqual(-10f, _cam.transform.position.z, 1e-5f, "camera z kept");
            Assert.AreEqual(0f, ScrollController.Instance.FarthestX, 1e-5f, "FarthestX = start");
            Assert.AreEqual(0f, ScrollField("_minX"), 1e-5f, "minX = start");
            Assert.AreEqual(length, ScrollField("_maxX"), 1e-5f, "maxX = mission length");
            Assert.AreEqual(1, ScrollController.Instance.Direction);
            Assert.AreEqual(1, players[0].Facing);
        }

        // ── Tests ───────────────────────────────────────────────────────

        [Test]
        public void FirstStart_NothingToTearDown_StartsClean() {
            _runner.StartMission(1);
            AssertCleanStart(1);
            Assert.IsTrue(Object.FindObjectsByType<PlayerPlane>(FindObjectsSortMode.None)[0].OnRunway,
                "mission 1 starts on the runway");
        }

        [Test]
        public void Next_SecondStart_TearsDownFirstMission() {
            _runner.StartMission(1);
            var firstPlane = Object.FindObjectsByType<PlayerPlane>(FindObjectsSortMode.None)[0];
            var firstTracker = _runnerGo.GetComponent<ObjectiveTracker>();

            // Some spawned entities from mission 1, and the camera at mission 1's end.
            _runner.SpawnEntities(new SpawnEntry { type = "tree", atX = 5, count = 2, spacing = 1f });
            Assert.AreEqual(2, Object.FindObjectsByType<Biplanes.Enemies.StaticObstacle>(FindObjectsSortMode.None).Length);
            SetCamX(MissionLength(1));

            _runner.StartMission(2);

            AssertCleanStart(2);
            Assert.IsTrue(firstPlane == null, "mission 1's PlayerPlane must be destroyed");
            Assert.AreEqual(0, Object.FindObjectsByType<Biplanes.Enemies.StaticObstacle>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                "mission 1's spawned entities must be destroyed");
            Assert.AreSame(firstTracker, _runnerGo.GetComponent<ObjectiveTracker>(), "the tracker is reused, not duplicated");
            Assert.IsFalse(Object.FindObjectsByType<PlayerPlane>(FindObjectsSortMode.None)[0].OnRunway,
                "mission 2 starts airborne");
        }

        [Test]
        public void ThirdStart_StillOneOfEverything() {
            _runner.StartMission(1);
            SetCamX(150f);
            _runner.StartMission(2);
            SetCamX(400f);
            _runner.StartMission(3);
            AssertCleanStart(3);
        }

        // ── T927: B006b score reset, B006c mission-end guard ───────────

        private bool MissionEnded() {
            var f = typeof(MissionRunner).GetField("_missionEnded", Private);
            Assert.IsNotNull(f, "MissionRunner._missionEnded not found by reflection");
            return (bool)f.GetValue(_runner);
        }

        // B006b: a mission reached via Next starts from score 0.
        [Test]
        public void Next_ResetsScoreToZero() {
            var skGo = new GameObject("T927_ScoreKeeper");
            var sk = skGo.AddComponent<ScoreKeeper>();
            var instanceField = typeof(ScoreKeeper).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(instanceField, "ScoreKeeper._instance not found by reflection");
            var previous = instanceField.GetValue(null);
            instanceField.SetValue(null, sk); // Awake doesn't run in EditMode.
            try {
                _runner.StartMission(1);
                sk.AddPoints(500);
                Assert.AreEqual(500, sk.CurrentScore);

                int lastRaised = -1;
                Action<int> onScore = s => lastRaised = s;
                GameEvents.ScoreChanged += onScore;
                try {
                    _runner.StartMission(2);
                } finally {
                    GameEvents.ScoreChanged -= onScore;
                }
                Assert.AreEqual(0, sk.CurrentScore, "StartMission must reset the score");
                Assert.AreEqual(0, lastRaised, "the reset must raise ScoreChanged(0) for the HUD");
            } finally {
                instanceField.SetValue(null, previous);
                Object.DestroyImmediate(skGo);
            }
        }

        // B006c: a loss (GameEvents.MissionEnded) blocks any later win handling; StartMission clears it.
        [Test]
        public void Loss_BlocksObjectiveHandling_UntilNextStart() {
            _runner.StartMission(2);
            Assert.IsFalse(MissionEnded(), "a fresh mission is not ended");

            GameEvents.RaiseMissionEnded(false, "T927 test crash");
            Assert.IsTrue(MissionEnded(), "a loss must set the mission-end guard");

            int handled = 0;
            Application.LogCallback onLog = (msg, trace, type) => {
                if (msg.Contains("objective 'T927_probe' completed")) handled++;
            };
            Application.logMessageReceived += onLog;
            try {
                _runner.HandleObjectiveComplete("T927_probe");
            } finally {
                Application.logMessageReceived -= onLog;
            }
            Assert.AreEqual(0, handled, "HandleObjectiveComplete must return early after a loss");

            _runner.StartMission(3);
            Assert.IsFalse(MissionEnded(), "StartMission must clear the guard");
        }

        // B006c: the MissionEnded subscription is not duplicated by Next, and teardown removes it.
        [Test]
        public void Next_KeepsOneMissionEndedSubscription() {
            _runner.StartMission(1);
            _runner.StartMission(2);
            var f = typeof(GameEvents).GetField("MissionEnded", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "GameEvents.MissionEnded backing field not found by reflection");
            var d = f.GetValue(null) as Delegate;
            int n = 0;
            if (d != null) {
                foreach (var h in d.GetInvocationList()) {
                    if (ReferenceEquals(h.Target, _runner)) n++;
                }
            }
            Assert.AreEqual(1, n, "exactly one MissionEnded subscription after Next");
        }
    }
}
