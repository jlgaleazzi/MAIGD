using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Player;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// T969 QA for B066 (player turn-around look): gaps not covered by PlayerFlipTests.
    /// Reversing twice mid-turn, the world hitbox (size, offset, lowest point) through the
    /// squash, bombs leaving the new way on the first tick of a turn (B057), a fresh plane
    /// after a half-done turn, and the wreck's drawn nose staying continuous when a crash
    /// lands in the first half of a turn.
    ///
    /// PlayerPlane.OnMove is called directly, one physics tick per call (no physics step in
    /// EditMode).
    /// </summary>
    public class PlayerFlipQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _camGo;
        private ScrollController _sc;
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

            _camGo = new GameObject("T969_TestCamera");
            _camGo.tag = "MainCamera";
            var cam = _camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.aspect = 16f / 9f;
            _camGo.transform.position = new Vector3(0f, 0f, -10f);

            ScrollController.Instance = null;
            _sc = ScrollController.Create(2f);
            ScrollController.Instance = _sc; // Awake does not run in EditMode.
        }

        [TearDown]
        public void TearDown() {
            ScrollController.Instance = null;
            if (_plane != null) Object.DestroyImmediate(_plane.gameObject);
            if (_sc != null) Object.DestroyImmediate(_sc.gameObject);
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

        private PlayerPlane MakePlane() {
            _plane = PlayerPlane.Create(Vector3.zero, false);
            _plane.transform.position = Vector3.zero;
            _plane.GetComponent<Rigidbody2D>().position = Vector2.zero;
            Physics2D.SyncTransforms();
            return _plane;
        }

        private void Move(bool left = false, bool right = false, bool up = false, bool down = false) {
            _plane.OnMove(new InputState { left = left, right = right, up = up, down = down });
        }

        private bool FlipX => _plane.GetComponent<SpriteRenderer>().flipX;
        private float ScaleX => _plane.transform.localScale.x;
        private float Rate => 2f / _plane.TurnSeconds * Time.fixedDeltaTime;
        private int TicksForTurn => Mathf.CeilToInt(_plane.TurnSeconds / Time.fixedDeltaTime) + 2;

        private Vector2 WorldBoxSize {
            get {
                var box = _plane.GetComponent<BoxCollider2D>();
                Vector3 s = _plane.transform.localScale;
                return new Vector2(box.size.x * Mathf.Abs(s.x), box.size.y * Mathf.Abs(s.y));
            }
        }

        private Vector2 WorldBoxOffset {
            get {
                var box = _plane.GetComponent<BoxCollider2D>();
                Vector3 s = _plane.transform.localScale;
                return new Vector2(box.offset.x * s.x, box.offset.y * s.y);
            }
        }

        /// <summary>Asserts the per-tick invariants of the turn look.</summary>
        private void AssertTickInvariants(string where, Vector2 baseSize, Vector2 baseOffset) {
            Assert.AreEqual(FighterAI.TurnScaleX(_plane.TurnHeading), ScaleX, 1e-5f, $"{where}: x scale = TurnScaleX(heading)");
            Assert.GreaterOrEqual(ScaleX, FighterAI.MinTurnScale - 1e-6f, $"{where}: never thinner than 0.15");
            Assert.AreEqual(1f, _plane.transform.localScale.y, 1e-6f, $"{where}: y scale untouched");
            Assert.AreEqual(_plane.VisualFacing < 0, FlipX, $"{where}: flipX = drawn facing");
            Assert.AreEqual(baseSize.x, WorldBoxSize.x, 1e-4f, $"{where}: world hitbox width constant");
            Assert.AreEqual(baseSize.y, WorldBoxSize.y, 1e-4f, $"{where}: world hitbox height constant");
            Assert.AreEqual(baseOffset.x, WorldBoxOffset.x, 1e-4f, $"{where}: world hitbox offset constant");
        }

        // ── Reversals ───────────────────────────────────────────────────

        [Test]
        public void ReverseTwiceMidTurn_HeadingNeverJumps_AndEndsOnTheLastKey() {
            MakePlane();
            Vector2 baseSize = WorldBoxSize;
            Vector2 baseOffset = WorldBoxOffset;
            float prev = _plane.TurnHeading;

            // A for 20 ticks (past the midpoint), D for 8, A again: two reversals mid-turn.
            var script = new List<int>();
            for (int i = 0; i < 20; i++) script.Add(-1);
            for (int i = 0; i < 8; i++) script.Add(1);
            for (int i = 0; i < TicksForTurn + 10; i++) script.Add(-1);

            for (int i = 0; i < script.Count; i++) {
                bool left = script[i] < 0;
                Move(left: left, right: !left);
                Assert.AreEqual(script[i], _plane.Facing, $"tick {i}: Facing follows the key at once");
                Assert.AreEqual(script[i], _sc.Direction, $"tick {i}: scroll follows the key at once");
                Assert.LessOrEqual(Mathf.Abs(_plane.TurnHeading - prev), Rate + 1e-5f, $"tick {i}: no heading jump");
                AssertTickInvariants($"tick {i}", baseSize, baseOffset);
                prev = _plane.TurnHeading;
            }

            Assert.IsFalse(_plane.IsTurning);
            Assert.AreEqual(-1f, _plane.TurnHeading, 1e-6f);
            Assert.AreEqual(1f, ScaleX, 1e-6f);
            Assert.IsTrue(FlipX);
            Assert.AreEqual(-1, _plane.VisualFacing);
        }

        [Test]
        public void ReverseBeforeTheMidpoint_NeverFlipsTheSprite() {
            MakePlane();
            for (int i = 0; i < 5; i++) Move(left: true);   // heading still > 0
            Assert.Greater(_plane.TurnHeading, 0f);
            Assert.IsFalse(FlipX);
            for (int i = 0; i < TicksForTurn; i++) {
                Move(right: true);
                Assert.IsFalse(FlipX, $"tick {i}: turned back before the midpoint, never drawn mirrored");
                Assert.AreEqual(1, _plane.VisualFacing);
            }
            Assert.IsFalse(_plane.IsTurning);
            Assert.AreEqual(1f, ScaleX, 1e-6f);
        }

        // ── Hitbox ──────────────────────────────────────────────────────

        [Test]
        public void Hitbox_LowestPointAndWorldSize_UnchangedAtTheSqueezeMidpoint() {
            MakePlane();
            Vector2 baseSize = WorldBoxSize;
            Vector2 baseOffset = WorldBoxOffset;
            float baseLowDy = _plane.HitboxLowestPoint().y - _plane.GetComponent<Rigidbody2D>().position.y;

            float minScale = 1f;
            for (int i = 0; i < TicksForTurn; i++) {
                Move(left: true);
                AssertTickInvariants($"tick {i}", baseSize, baseOffset);
                // Level flight: the lowest hitbox point keeps its height under the centre.
                float dy = _plane.HitboxLowestPoint().y - _plane.GetComponent<Rigidbody2D>().position.y;
                Assert.AreEqual(baseLowDy, dy, 1e-4f, $"tick {i}: terrain check unaffected by the squash");
                minScale = Mathf.Min(minScale, ScaleX);
            }
            Assert.AreEqual(FighterAI.MinTurnScale, minScale, 0.02f, "the turn passed through the 0.15 squash");
        }

        // ── Bombs (B057 still right while turning) ──────────────────────

        [Test]
        public void Bomb_DroppedOnTheFirstTickOfATurn_LeavesTheNewWay_AtTheUnscaledOffset() {
            MakePlane();
            Move(left: true);
            Assert.IsTrue(_plane.IsTurning);
            Assert.AreEqual(1, _plane.VisualFacing, "still drawn facing right on the first tick");

            var before = new HashSet<Bomb>(Object.FindObjectsByType<Bomb>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            var onBomb = typeof(PlayerPlane).GetMethod("OnDropBomb", Private);
            Assert.IsNotNull(onBomb, "PlayerPlane.OnDropBomb not found by reflection");
            onBomb.Invoke(_plane, null);

            Bomb dropped = null;
            foreach (var b in Object.FindObjectsByType<Bomb>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) {
                if (!before.Contains(b)) dropped = b;
            }
            Assert.IsNotNull(dropped, "a bomb drops while the plane is turning");
            Assert.Less(dropped.Velocity.x, 0f, "B057: the bomb leaves the way the plane now faces (left)");
            Vector2 offset = (Vector2)(dropped.transform.position - _plane.transform.position);
            Assert.AreEqual(0f, offset.x, 1e-4f, "bomb offset not touched by the squash");
            Assert.AreEqual(-0.35f, offset.y, 1e-4f);
        }

        [Test]
        public void Gun_AtTheSqueezeMidpoint_MuzzleIsUnscaled() {
            MakePlane();
            for (int i = 0; i < 15; i++) Move(left: true);
            Assert.Less(ScaleX, 0.3f, "near the midpoint");

            var before = new HashSet<Bullet>(Object.FindObjectsByType<Bullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            typeof(PlayerPlane).GetMethod("OnFire", Private).Invoke(_plane, null);
            Bullet fired = null;
            foreach (var b in Object.FindObjectsByType<Bullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) {
                if (!before.Contains(b)) fired = b;
            }
            Assert.IsNotNull(fired);
            Vector2 offset = (Vector2)(fired.transform.position - _plane.transform.position);
            Assert.AreEqual(-0.7f, offset.x, 1e-4f, "muzzle stays 0.7 ahead along Facing mid-squash");
        }

        // ── Reset ───────────────────────────────────────────────────────

        [Test]
        public void NewPlane_AfterAHalfDoneTurn_StartsFullWidth_Unmirrored_AtTheBaseHitbox() {
            MakePlane();
            Vector2 baseSize = WorldBoxSize;
            Vector2 localBase = _plane.GetComponent<BoxCollider2D>().size;
            for (int i = 0; i < 20; i++) Move(left: true);   // past the midpoint: mirrored, squashed
            Assert.IsTrue(FlipX);
            Assert.Less(ScaleX, 1f);
            Object.DestroyImmediate(_plane.gameObject);

            MakePlane();   // what MissionRunner does on Retry/Next
            Assert.AreEqual(1f, ScaleX, 1e-6f);
            Assert.IsFalse(FlipX);
            Assert.AreEqual(1f, _plane.TurnHeading, 1e-6f);
            Assert.AreEqual(1, _plane.Facing);
            Assert.AreEqual(1, _plane.VisualFacing);
            Assert.IsFalse(_plane.IsTurning);
            Assert.AreEqual(localBase.x, _plane.GetComponent<BoxCollider2D>().size.x, 1e-5f, "local box at its base size");
            Assert.AreEqual(baseSize.x, WorldBoxSize.x, 1e-5f);
        }

        // ── Wreck ───────────────────────────────────────────────────────

        /// <summary>World direction of the drawn nose of the wreck: the crash rotation applied
        /// to the art's nose (+x, or -x when flipX).</summary>
        private Vector2 WreckNose(FieldInfo crashAngle) {
            float a = (float)crashAngle.GetValue(_plane);
            return Quaternion.Euler(0f, 0f, a) * new Vector3(FlipX ? -1f : 1f, 0f, 0f);
        }

        [Test]
        public void Crash_InTheFirstHalfOfATurn_WreckNoseDoesNotPopAtTheFlip() {
            MakePlane();
            for (int i = 0; i < 40; i++) Move(up: true);       // climbing: pitch nose up
            for (int i = 0; i < 3; i++) Move(left: true, up: true); // early in the turn, heading > 0
            Assert.Greater(_plane.TurnHeading, 0f);
            Assert.IsFalse(FlipX);

            _plane.ForceCrash();
            var fixedUpdate = typeof(PlayerPlane).GetMethod("FixedUpdate", Private);
            var crashAngle = typeof(PlayerPlane).GetField("_crashAngle", Private);
            Assert.IsNotNull(fixedUpdate);
            Assert.IsNotNull(crashAngle, "PlayerPlane._crashAngle not found by reflection");

            // The wreck spins 300 deg/s = 6 deg per 0.02 s tick, so the drawn nose's vertical
            // component moves by at most sin(6 deg) ~ 0.105 per tick. At the flip the nose
            // mirrors left/right (x changes sign, that is the turn) but must keep its up/down
            // (y), exactly as BodyAngle mirrors in flight. A sign flip of y is a nose pop.
            float maxDy = Mathf.Sin(300f * Time.fixedDeltaTime * Mathf.Deg2Rad) + 0.02f;
            Vector2 prev = WreckNose(crashAngle);
            bool sawFlip = false;
            for (int i = 0; i < TicksForTurn; i++) {
                fixedUpdate.Invoke(_plane, null);
                Vector2 nose = WreckNose(crashAngle);
                sawFlip |= FlipX;
                Assert.LessOrEqual(Mathf.Abs(nose.y - prev.y), maxDy,
                    $"tick {i}: the wreck's drawn nose popped up/down (flipX {FlipX}, angle {crashAngle.GetValue(_plane)}, nose.y {prev.y:F3} -> {nose.y:F3})");
                prev = nose;
            }
            Assert.IsTrue(sawFlip, "the turn finished on the wreck");
            Assert.AreEqual(1f, ScaleX, 1e-6f);
        }

        /// <summary>T970 (B067 regression): a wreck that has already landed when the turn
        /// reaches its midpoint mirrors its angle, so the drawn nose keeps exactly the same
        /// up/down (no more spin once landed) and only its left/right flips. It stays landed
        /// and does not move.</summary>
        [Test]
        public void Crash_LandedBeforeTheFlip_WreckMirrorsAndStaysPut() {
            MakePlane();
            for (int i = 0; i < 3; i++) Move(left: true);   // early in the turn, heading > 0
            Assert.Greater(_plane.TurnHeading, 0f);
            Assert.IsFalse(FlipX);

            // Put the plane just above its rest height so the wreck lands on the first tick.
            var terrain = Biplanes.Missions.TerrainStrip.Instance;
            float ground = terrain != null ? terrain.HeightAt(0f) : Biplanes.Missions.TerrainStrip.GroundBaseY;
            var rb = _plane.GetComponent<Rigidbody2D>();
            rb.position = new Vector2(0f, ground + 0.2f + 0.001f);

            // UpdateCrashFall calls FxManager.Instance on landing, which would create one with
            // DontDestroyOnLoad (throws outside Play mode). Seed the singleton for the test.
            var fxField = typeof(Biplanes.Fx.FxManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(fxField, "FxManager._instance not found by reflection");
            var fxPrev = fxField.GetValue(null);
            var fxGo = new GameObject("T970_FxManager");
            fxField.SetValue(null, fxGo.AddComponent<Biplanes.Fx.FxManager>());
            try {
                RunLandedWreckFlip();
            } finally {
                fxField.SetValue(null, fxPrev);
                Object.DestroyImmediate(fxGo);
            }
        }

        private void RunLandedWreckFlip() {
            _plane.ForceCrash();
            var fixedUpdate = typeof(PlayerPlane).GetMethod("FixedUpdate", Private);
            var crashAngle = typeof(PlayerPlane).GetField("_crashAngle", Private);
            var crashLanded = typeof(PlayerPlane).GetField("_crashLanded", Private);
            Assert.IsNotNull(crashLanded, "PlayerPlane._crashLanded not found by reflection");

            fixedUpdate.Invoke(_plane, null);
            Assert.IsTrue((bool)crashLanded.GetValue(_plane), "the wreck landed on the first tick");
            Assert.IsFalse(FlipX, "landed before the flip");
            Vector2 landedNose = WreckNose(crashAngle);
            float landedAngle = (float)crashAngle.GetValue(_plane);

            bool sawFlip = false;
            for (int i = 0; i < TicksForTurn; i++) {
                fixedUpdate.Invoke(_plane, null);
                Assert.IsTrue((bool)crashLanded.GetValue(_plane), $"tick {i}: still landed");
                Vector2 nose = WreckNose(crashAngle);
                Assert.AreEqual(landedNose.y, nose.y, 1e-4f, $"tick {i}: landed wreck nose keeps its up/down");
                if (FlipX && !sawFlip) {
                    sawFlip = true;
                    Assert.AreEqual(-landedAngle, (float)crashAngle.GetValue(_plane), 1e-4f, "angle mirrored once at the flip");
                    Assert.AreEqual(-landedNose.x, nose.x, 1e-4f, "only left/right mirrors");
                }
            }
            Assert.IsTrue(sawFlip, "the turn finished on the landed wreck");
            Assert.AreEqual(-landedAngle, (float)crashAngle.GetValue(_plane), 1e-4f, "mirrored exactly once");
            Assert.AreEqual(1f, ScaleX, 1e-6f);
        }
    }
}
