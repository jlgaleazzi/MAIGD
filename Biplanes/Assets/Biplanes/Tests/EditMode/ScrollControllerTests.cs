using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;

namespace Biplanes.Tests {

    /// <summary>
    /// T925: EditMode tests for the T924 reversible scroll (ScrollController).
    /// FixedUpdate is private, so it is driven by reflection, one physics tick per call.
    /// Each test gets its own MainCamera; any other enabled MainCamera in the open scene is
    /// disabled for the test and restored afterwards, so Camera.main is ours.
    /// </summary>
    public class ScrollControllerTests {

        private const float Speed = 2f;

        private GameObject _camGo;
        private Camera _cam;
        private ScrollController _sc;
        private readonly List<Camera> _disabledCameras = new List<Camera>();

        private static readonly MethodInfo FixedUpdateMethod =
            typeof(ScrollController).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp() {
            _disabledCameras.Clear();
            foreach (var c in Camera.allCameras) {
                if (c != null && c.CompareTag("MainCamera")) {
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
            }

            _camGo = new GameObject("T925_TestCamera");
            _camGo.tag = "MainCamera";
            _cam = _camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 5f;
            _cam.aspect = 16f / 9f;
            _camGo.transform.position = new Vector3(0f, 0f, -10f);

            ScrollController.Instance = null;
            _sc = ScrollController.Create(Speed);
            Assert.IsNotNull(_sc, "ScrollController.Create returned null");
            Assert.AreSame(_cam, _sc.Camera, "ScrollController did not pick up the test MainCamera");
        }

        [TearDown]
        public void TearDown() {
            ScrollController.Instance = null;
            if (_sc != null) Object.DestroyImmediate(_sc.gameObject);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            foreach (var c in _disabledCameras) {
                if (c != null) c.enabled = true;
            }
            _disabledCameras.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private void Tick(int count = 1) {
            Assert.IsNotNull(FixedUpdateMethod, "ScrollController.FixedUpdate not found by reflection");
            for (int i = 0; i < count; i++) FixedUpdateMethod.Invoke(_sc, null);
        }

        private float CamX => _cam.transform.position.x;

        private void SetCamX(float x) {
            var p = _cam.transform.position;
            p.x = x;
            _cam.transform.position = p;
        }

        private static float TurnEaseSeconds() {
            var g = GameBalance.Get();
            float ease = g != null && g.player != null ? g.player.turnEaseSeconds : 0f;
            return ease > 0f ? ease : 0.6f;
        }

        // ── Create ──────────────────────────────────────────────────────

        [Test]
        public void Create_StartsRightwardAtFullSpeed() {
            Assert.AreEqual(1, _sc.Direction);
            Assert.AreEqual(Speed, _sc.ScrollSpeed);
            Assert.AreEqual(Speed, _sc.Velocity);
            Assert.AreEqual(0f, _sc.FarthestX);
        }

        [Test]
        public void SteadyScroll_AdvancesAtScrollSpeed() {
            float dt = Time.fixedDeltaTime;
            Tick(50);
            Assert.AreEqual(Speed * dt * 50f, CamX, 1e-3f);
            Assert.AreEqual(Speed, _sc.Velocity);
            Assert.AreEqual(CamX, _sc.FarthestX, 1e-5f);
        }

        [Test]
        public void SetScrollSpeed_NegativeClampsToZero() {
            _sc.SetScrollSpeed(-3f);
            Assert.AreEqual(0f, _sc.ScrollSpeed);
        }

        [Test]
        public void SetDirection_InvalidValueIgnored() {
            _sc.SetDirection(0);
            Assert.AreEqual(1, _sc.Direction);
            _sc.SetDirection(-1);
            Assert.AreEqual(-1, _sc.Direction);
            _sc.SetDirection(5);
            Assert.AreEqual(-1, _sc.Direction);
        }

        // ── Turn easing ─────────────────────────────────────────────────

        [Test]
        public void Reversal_EasesThroughZero_ReachesExactlyMinusSpeed_InTurnEaseSeconds() {
            float dt = Time.fixedDeltaTime;
            int expectedTicks = Mathf.CeilToInt(TurnEaseSeconds() / dt - 1e-3f);

            _sc.SetDirection(-1);
            float prev = _sc.Velocity;
            bool crossedZero = false;
            int ticksToTarget = -1;
            for (int i = 1; i <= expectedTicks + 5; i++) {
                Tick();
                float v = _sc.Velocity;
                Assert.LessOrEqual(v, prev, $"velocity rose during a leftward turn at tick {i}");
                Assert.GreaterOrEqual(v, -Speed, $"velocity overshot -scrollSpeed at tick {i}");
                if (v <= 0f) crossedZero = true;
                if (ticksToTarget < 0 && v == -Speed) ticksToTarget = i;
                prev = v;
            }

            Assert.IsTrue(crossedZero, "velocity never passed through 0");
            Assert.AreEqual(-Speed, _sc.Velocity, "velocity did not settle exactly on -scrollSpeed");
            Assert.That(ticksToTarget, Is.InRange(expectedTicks - 1, expectedTicks + 1),
                "a full reversal should take player.turnEaseSeconds");
        }

        [Test]
        public void Reversal_DoesNotSnap() {
            _sc.SetDirection(-1);
            Tick();
            Assert.Greater(_sc.Velocity, 0f, "one tick after a turn the camera should still be moving right");
            Assert.Less(_sc.Velocity, Speed);
        }

        [Test]
        public void MidTurnReversal_ReturnsExactlyToPlusSpeed_WithoutOscillation() {
            _sc.SetDirection(-1);
            Tick(5);
            _sc.SetDirection(1);
            float prev = _sc.Velocity;
            for (int i = 0; i < 60; i++) {
                Tick();
                Assert.GreaterOrEqual(_sc.Velocity, prev, "velocity fell while easing back to the right");
                Assert.LessOrEqual(_sc.Velocity, Speed, "velocity overshot +scrollSpeed");
                prev = _sc.Velocity;
            }
            Assert.AreEqual(Speed, _sc.Velocity);
        }

        [Test]
        public void SpeedDrop_EasesDownToNewSpeedExactly() {
            _sc.SetScrollSpeed(1f);
            Tick(60);
            Assert.AreEqual(1f, _sc.Velocity);
        }

        // ── Limits ──────────────────────────────────────────────────────

        [Test]
        public void MinLimit_PinsCameraWithZeroVelocity_NoJitter() {
            _sc.SetLimits(0f, 100f);
            SetCamX(0.5f);
            _sc.SetDirection(-1);
            Tick(200);
            Assert.AreEqual(0f, CamX, "camera should be pinned exactly at minX");
            Assert.AreEqual(0f, _sc.Velocity);
            for (int i = 0; i < 20; i++) {
                Tick();
                Assert.AreEqual(0f, CamX, "camera left minX while pinned");
                Assert.AreEqual(0f, _sc.Velocity, "velocity non-zero while pinned at minX");
            }
        }

        [Test]
        public void MinLimit_TurningAwayUnsticks() {
            _sc.SetLimits(0f, 100f);
            SetCamX(0f);
            _sc.SetDirection(-1);
            Tick(100);
            _sc.SetDirection(1);
            Tick();
            Assert.Greater(CamX, 0f, "camera stuck at minX after turning right");
            Tick(100);
            Assert.AreEqual(Speed, _sc.Velocity);
        }

        [Test]
        public void MaxLimit_PinsCameraExactly_AndFarthestXIsTheLimit() {
            _sc.SetLimits(0f, 10f);
            SetCamX(9.9f);
            Tick(100);
            Assert.AreEqual(10f, CamX, "camera should stop exactly at maxX (surviveToEnd uses >= length)");
            Assert.AreEqual(0f, _sc.Velocity);
            Assert.AreEqual(10f, _sc.FarthestX);
        }

        [Test]
        public void MaxLimit_TurningAwayUnsticks() {
            _sc.SetLimits(0f, 10f);
            SetCamX(10f);
            Tick(10);
            _sc.SetDirection(-1);
            Tick();
            Assert.Less(CamX, 10f, "camera stuck at maxX after turning left");
        }

        [Test]
        public void SetLimits_MaxBelowMin_CollapsesToMin() {
            _sc.SetLimits(5f, 1f);
            SetCamX(0f);
            Tick();
            Assert.AreEqual(5f, CamX);
            _sc.SetDirection(-1);
            Tick(100);
            Assert.AreEqual(5f, CamX);
        }

        // ── FarthestX ───────────────────────────────────────────────────

        [Test]
        public void FarthestX_NeverDecreasesWhenFlyingLeft() {
            Tick(50);
            Assert.AreEqual(CamX, _sc.FarthestX, 1e-5f);

            // The camera still drifts right while it decelerates, so track the true peak.
            _sc.SetDirection(-1);
            float peak = CamX;
            for (int i = 0; i < 200; i++) {
                Tick();
                if (CamX > peak) peak = CamX;
                Assert.GreaterOrEqual(_sc.FarthestX, peak - 1e-5f, $"FarthestX fell below the peak at tick {i}");
            }
            Assert.Less(CamX, peak, "camera should have moved back left");
            Assert.AreEqual(peak, _sc.FarthestX, 1e-5f,
                "FarthestX must keep the peak camera x, not follow the camera back");
        }

        // ── Create reuse (mission restart without scene reload) ─────────

        [Test]
        public void Create_ReusesLiveInstanceAndResetsState() {
            ScrollController.Instance = _sc;
            _sc.SetLimits(0f, 3f);
            _sc.SetDirection(-1);
            Tick(40);
            SetCamX(7f);

            var again = ScrollController.Create(3f);

            Assert.AreSame(_sc, again, "Create must reuse the live Instance");
            Assert.AreEqual(1, again.Direction);
            Assert.AreEqual(3f, again.ScrollSpeed);
            Assert.AreEqual(3f, again.Velocity);
            Assert.AreEqual(7f, again.FarthestX, "FarthestX resets to the current camera x");

            // Old limits [0, 3] are gone: the camera is free above 3 and below 0.
            Tick();
            Assert.Greater(CamX, 7f);
            again.SetDirection(-1);
            SetCamX(-50f);
            Tick(100);
            Assert.Less(CamX, -50f, "old minX survived Create");
        }
    }
}
