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
    /// B049: every mission waits for the player's first input (ARCHITECTURE §3 "Mission start
    /// gate"). While waiting the ScrollController is held (velocity exactly 0, camera and
    /// FarthestX unchanged), the airborne plane holds its position and MissionRunner.HasStarted
    /// is false. The first move, fire or bomb input (through PlayerInputGate, so a key held
    /// through the briefing doesn't count) starts it; the runway takeoff is the same gate.
    /// StartMission again (Retry / Next) waits again.
    ///
    /// EditMode doesn't run FixedUpdate, so ScrollController.FixedUpdate and PlayerPlane.OnFire
    /// are invoked by reflection.
    /// </summary>
    public class MissionStartGateTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo ScrollFixedUpdate =
            typeof(ScrollController).GetMethod("FixedUpdate", Private);

        private GameObject _camGo;
        private Camera _cam;
        private GameObject _runnerGo;
        private MissionRunner _runner;
        private PlayerPlane _plane;
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

            _camGo = new GameObject("B049_TestCamera");
            _camGo.tag = "MainCamera";
            _cam = _camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 5f;
            _cam.aspect = 16f / 9f;
            _camGo.transform.position = new Vector3(0f, 0f, -10f);

            ScrollController.Instance = null;
        }

        [TearDown]
        public void TearDown() {
            if (_runner != null) {
                var onDestroy = typeof(MissionRunner).GetMethod("OnDestroy", Private);
                onDestroy?.Invoke(_runner, null);
            }

            if (_plane != null) Object.DestroyImmediate(_plane.gameObject);
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

        private static ScrollController HeldScroll(float speed = 2f) {
            var sc = ScrollController.Create(speed);
            ScrollController.Instance = sc; // Awake does not run in EditMode.
            sc.SetLimits(0f, 1000f);
            sc.Hold();
            return sc;
        }

        private static void Tick(ScrollController sc, int count) {
            Assert.IsNotNull(ScrollFixedUpdate, "ScrollController.FixedUpdate not found by reflection");
            for (int i = 0; i < count; i++) ScrollFixedUpdate.Invoke(sc, null);
        }

        private PlayerPlane MakePlane(Vector2 position, bool onRunway) {
            _plane = PlayerPlane.Create(new Vector3(position.x, position.y, 0f), onRunway);
            _plane.transform.position = new Vector3(position.x, position.y, 0f);
            _plane.GetComponent<Rigidbody2D>().position = position;
            Physics2D.SyncTransforms();
            return _plane;
        }

        private Vector2 PlaneVelocity() {
            var f = typeof(PlayerPlane).GetField("_velocity", Private);
            Assert.IsNotNull(f, "PlayerPlane._velocity not found by reflection");
            return (Vector2)f.GetValue(_plane);
        }

        private void StartRunner(int mission) {
            if (_runner == null) {
                _runnerGo = new GameObject("B049_MissionRunner");
                _runner = _runnerGo.AddComponent<MissionRunner>();
            }
            _runner.StartMission(mission);
            _plane = _runner.Player;
            Assert.IsNotNull(_plane, "StartMission built no player");
        }

        // ── ScrollController hold ───────────────────────────────────────

        [Test]
        public void Held_VelocityZero_CameraAndFarthestXStill() {
            var sc = HeldScroll();
            Assert.IsTrue(sc.IsHeld);
            Assert.AreEqual(0f, sc.Velocity, "Hold zeroes the velocity at once");

            Tick(sc, 120);

            Assert.AreEqual(0f, sc.Velocity, "velocity must stay exactly 0 while held");
            Assert.AreEqual(0f, _cam.transform.position.x, "camera must not move while held");
            Assert.AreEqual(0f, sc.FarthestX, "FarthestX must not change while held");
        }

        [Test]
        public void Release_ScrollEasesUpToFullSpeed() {
            var sc = HeldScroll(2f);
            Tick(sc, 10);
            sc.Release();
            Assert.IsFalse(sc.IsHeld);

            Tick(sc, 1);
            Assert.Greater(sc.Velocity, 0f, "scroll starts after release");
            Tick(sc, 200);
            Assert.AreEqual(2f, sc.Velocity, 1e-5f, "settles on the full scroll speed");
            Assert.Greater(_cam.transform.position.x, 0f, "camera moves after release");
            Assert.Greater(sc.FarthestX, 0f);
        }

        [Test]
        public void Create_IsNotHeld_AndResetsAReusedHeldInstance() {
            var sc = HeldScroll();
            var again = ScrollController.Create(3f);
            Assert.AreSame(sc, again);
            Assert.IsFalse(again.IsHeld, "Create starts unheld; MissionRunner holds it");
            Assert.AreEqual(3f, again.Velocity);
        }

        // ── PlayerInputGate: a key held through the briefing doesn't count ──

        [Test]
        public void Gate_KeyHeldAtSpawn_IgnoredUntilReleasedAndPressedAgain() {
            var gate = new PlayerInputGate(); // fresh gate = PlayerInput.OnEnable at spawn
            var w = new PlayerInputGate.Raw { up = true };
            Assert.IsFalse(PlayerInputGate.AnyAction(gate.Step(w, false, false)), "W held at spawn");
            Assert.IsFalse(PlayerInputGate.AnyAction(gate.Step(w, false, false)), "still the same hold");
            gate.Step(new PlayerInputGate.Raw(), false, false);
            Assert.IsTrue(gate.Step(w, false, false).up, "a fresh W counts");
        }

        [Test]
        public void Gate_KeyHeldThroughBriefing_IgnoredUntilReleased() {
            var gate = new PlayerInputGate();
            gate.Step(new PlayerInputGate.Raw(), false, false);
            var d = new PlayerInputGate.Raw { right = true };
            gate.Step(d, true, false); // briefing up, D held
            gate.Step(d, true, false);
            Assert.IsFalse(gate.Step(d, false, false).right, "D held through Begin");
            gate.Step(new PlayerInputGate.Raw(), false, false);
            Assert.IsTrue(gate.Step(d, false, false).right, "D pressed again counts");
        }

        [Test]
        public void Gate_KeyPressedAfterScreenCloses_CountsAtOnce() {
            var gate = new PlayerInputGate();
            gate.Step(new PlayerInputGate.Raw(), true, false); // briefing up, nothing held
            Assert.IsTrue(gate.Step(new PlayerInputGate.Raw { left = true }, false, false).left);
        }

        [Test]
        public void Gate_SpaceHeldThroughBegin_NoBomb_NoStart() {
            var gate = new PlayerInputGate();
            var space = new PlayerInputGate.Raw { bombHeld = true, bombPressedNow = true };
            gate.Step(space, true, false); // Space pressed Begin
            Assert.IsFalse(PlayerInputGate.AnyAction(gate.Step(new PlayerInputGate.Raw { bombHeld = true }, false, false)));
        }

        [Test]
        public void AnyAction_EachActionCounts() {
            Assert.IsFalse(PlayerInputGate.AnyAction(default(InputState)));
            Assert.IsTrue(PlayerInputGate.AnyAction(new InputState { up = true }));
            Assert.IsTrue(PlayerInputGate.AnyAction(new InputState { down = true }));
            Assert.IsTrue(PlayerInputGate.AnyAction(new InputState { left = true }));
            Assert.IsTrue(PlayerInputGate.AnyAction(new InputState { right = true }));
            Assert.IsTrue(PlayerInputGate.AnyAction(new InputState { fire = true }));
            Assert.IsTrue(PlayerInputGate.AnyAction(new InputState { dropBomb = true }));
        }

        // ── PlayerPlane ─────────────────────────────────────────────────

        [Test]
        public void Airborne_WaitsWithoutInput_HoldsPosition() {
            var sc = HeldScroll();
            MakePlane(new Vector2(-6f, 2f), false);
            Vector2 before = _plane.GetComponent<Rigidbody2D>().position;

            for (int i = 0; i < 30; i++) _plane.OnMove(default(InputState));

            Assert.IsTrue(PlayerPlane.IsWaitingForStart);
            Assert.IsTrue(sc.IsHeld, "no input: still waiting");
            Assert.AreEqual(Vector2.zero, PlaneVelocity(), "the plane holds still while waiting");
            Assert.AreEqual(before, _plane.GetComponent<Rigidbody2D>().position);
        }

        [Test]
        public void Airborne_FirstMoveInput_StartsAndMoves() {
            var sc = HeldScroll();
            MakePlane(new Vector2(-6f, 2f), false);
            _plane.OnMove(new InputState { up = true });
            Assert.IsFalse(sc.IsHeld, "the first input releases the scroll");
            Assert.IsFalse(PlayerPlane.IsWaitingForStart);
            Assert.Greater(PlaneVelocity().y, 0f, "the first input also moves the plane");
        }

        [Test]
        public void Airborne_FirstInputLeft_TurnsAndStarts() {
            var sc = HeldScroll();
            MakePlane(new Vector2(-6f, 2f), false);
            _plane.OnMove(new InputState { left = true });
            Assert.IsFalse(sc.IsHeld);
            Assert.AreEqual(-1, _plane.Facing);
        }

        [Test]
        public void Airborne_FirstInputFire_StartsAndFires() {
            var sc = HeldScroll();
            MakePlane(new Vector2(-6f, 2f), false);
            var onFire = typeof(PlayerPlane).GetMethod("OnFire", Private);
            Assert.IsNotNull(onFire, "PlayerPlane.OnFire not found by reflection");
            onFire.Invoke(_plane, null);
            Assert.IsFalse(sc.IsHeld, "fire is a first input");
        }

        [Test]
        public void Airborne_FirstInputBomb_StartsAndDrops() {
            var sc = HeldScroll();
            MakePlane(new Vector2(-6f, 2f), false);
            var bay = _plane.GetComponent<BombBay>();
            int before = bay.BombsLeft;
            var onBomb = typeof(PlayerPlane).GetMethod("OnDropBomb", Private);
            Assert.IsNotNull(onBomb, "PlayerPlane.OnDropBomb not found by reflection");
            onBomb.Invoke(_plane, null);
            Assert.IsFalse(sc.IsHeld, "bomb is a first input");
            Assert.AreEqual(before - 1, bay.BombsLeft, "the first bomb press drops a bomb");
        }

        [Test]
        public void Runway_OnlyTakeoffStarts() {
            var sc = HeldScroll();
            MakePlane(new Vector2(-6f, -3f), true);

            _plane.OnMove(new InputState { left = true });
            _plane.OnMove(new InputState { down = true });
            Assert.IsTrue(sc.IsHeld, "A or S on the runway don't take off, so nothing starts");
            Assert.IsTrue(_plane.OnRunway);

            _plane.OnMove(new InputState { up = true });
            Assert.IsFalse(_plane.OnRunway, "W takes off");
            Assert.IsFalse(sc.IsHeld, "the takeoff is the first input (same gate)");
        }

        [Test]
        public void NotHeld_MovesAsBefore() {
            var sc = ScrollController.Create(2f);
            ScrollController.Instance = sc;
            MakePlane(new Vector2(-6f, 2f), false);
            Assert.IsFalse(PlayerPlane.IsWaitingForStart);
            _plane.OnMove(new InputState { up = true });
            Assert.Greater(PlaneVelocity().y, 0f);
        }

        // ── MissionRunner ───────────────────────────────────────────────

        [Test]
        public void StartMission_WaitsUntilFirstInput() {
            StartRunner(2);
            var sc = ScrollController.Instance;
            Assert.IsTrue(sc.IsHeld, "StartMission holds the scroll");
            Assert.IsFalse(_runner.HasStarted);

            Tick(sc, 60);
            for (int i = 0; i < 10; i++) _plane.OnMove(default(InputState));
            Assert.AreEqual(0f, sc.Velocity);
            Assert.AreEqual(0f, sc.FarthestX, 1e-5f);
            Assert.IsFalse(_runner.HasStarted);

            _plane.OnMove(new InputState { right = true });
            Assert.IsTrue(_runner.HasStarted, "the first input starts the mission");
            Tick(sc, 30);
            Assert.Greater(sc.FarthestX, 0f, "the world scrolls after the start");
        }

        [Test]
        public void StartMission_Mission1Runway_WaitsUntilTakeoff() {
            StartRunner(1);
            Assert.IsTrue(_plane.OnRunway);
            Assert.IsFalse(_runner.HasStarted);
            _plane.OnMove(new InputState { right = true });
            Assert.IsTrue(_runner.HasStarted);
            Assert.IsFalse(_plane.OnRunway);
        }

        [Test]
        public void StartMissionAgain_WaitsAgain() {
            StartRunner(2);
            _plane.OnMove(new InputState { up = true });
            Assert.IsTrue(_runner.HasStarted);

            StartRunner(3); // Next (Retry reloads the scene and runs StartMission too)
            Assert.IsFalse(_runner.HasStarted, "a new start waits for input again");
            Assert.IsTrue(ScrollController.Instance.IsHeld);
            Assert.AreEqual(0f, ScrollController.Instance.Velocity);
        }
    }
}
