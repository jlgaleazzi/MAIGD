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
    /// B066: the player's turn-around look. Facing (scroll, gun, bombs, screen anchor) switches
    /// on the key press; only the sprite eases round over TurnSeconds with the fighters' maths
    /// (FighterAI.StepHeading / FacingFromHeading / TurnScaleX): x scale squashed to
    /// FighterAI.MinTurnScale at the midpoint, flipX and the pitch mirror there, the
    /// BoxCollider2D compensated so the world hitbox never changes.
    ///
    /// PlayerPlane.OnMove is called directly, one physics tick per call (no physics step in
    /// EditMode).
    /// </summary>
    public class PlayerFlipTests {

        private const float Dt = 0.02f;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _camGo;
        private Camera _cam;
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

            _camGo = new GameObject("B066_TestCamera");
            _camGo.tag = "MainCamera";
            _cam = _camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 5f;
            _cam.aspect = 16f / 9f;
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

            // PlayerPlane.Create builds the bullet and bomb pools.
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

        /// <summary>World hitbox width: local box width times the x scale.</summary>
        private float WorldBoxWidth {
            get {
                var box = _plane.GetComponent<BoxCollider2D>();
                return box.size.x * Mathf.Abs(_plane.transform.localScale.x);
            }
        }

        private int TicksForTurn => Mathf.CeilToInt(_plane.TurnSeconds / Time.fixedDeltaTime) + 2;

        // ── Pure helpers (shared with FighterAI) ────────────────────────

        [Test]
        public void StepHeading_IsMonotone_AndReachesTheTargetInTurnSeconds() {
            const float turn = 0.6f;
            int ticks = Mathf.RoundToInt(turn / Dt);
            float h = 1f;
            for (int i = 0; i < ticks - 1; i++) {
                float next = FighterAI.StepHeading(h, -1, turn, Dt);
                Assert.LessOrEqual(next, h, "the heading never moves away from the target");
                Assert.Greater(next, -1f, $"not done before turnSeconds (tick {i + 1})");
                h = next;
            }
            h = FighterAI.StepHeading(h, -1, turn, Dt);
            h = FighterAI.StepHeading(h, -1, turn, Dt); // one tick of float slack
            Assert.AreEqual(-1f, h, 1e-6f, "a full turn-around takes turnSeconds");
            Assert.AreEqual(-1f, FighterAI.StepHeading(h, -1, turn, Dt), "stays on the target");
        }

        [Test]
        public void TurnScaleX_IsMinAtMidTurn_AndFullAtTheEnds() {
            Assert.AreEqual(FighterAI.MinTurnScale, FighterAI.TurnScaleX(0f), 1e-6f);
            Assert.AreEqual(0.15f, FighterAI.MinTurnScale, 1e-6f, "squash to 0.15 like the fighters");
            Assert.AreEqual(1f, FighterAI.TurnScaleX(1f), 1e-6f);
            Assert.AreEqual(1f, FighterAI.TurnScaleX(-1f), 1e-6f);

            // Half of turnSeconds in: heading about 0, width about 0.15.
            float h = 1f;
            for (int i = 0; i < 15; i++) h = FighterAI.StepHeading(h, -1, 0.6f, Dt);
            Assert.AreEqual(0f, h, 1e-3f);
            Assert.AreEqual(0.15f, FighterAI.TurnScaleX(h), 0.01f);
        }

        [Test]
        public void StepHeading_IsInstant_WhenTurnSecondsIsNotPositive() {
            Assert.AreEqual(-1f, FighterAI.StepHeading(1f, -1, 0f, Dt));
            Assert.AreEqual(1f, FighterAI.StepHeading(-0.4f, 1, -1f, Dt));
        }

        [Test]
        public void FacingFromHeading_FollowsTheSign() {
            Assert.AreEqual(1, FighterAI.FacingFromHeading(0.01f, -1));
            Assert.AreEqual(-1, FighterAI.FacingFromHeading(-0.01f, 1));
            Assert.AreEqual(-1, FighterAI.FacingFromHeading(0f, -1), "exactly 0 keeps the current");
        }

        [Test]
        public void TurnSecondsOrDefault_FallsBackToTheFighterTurn() {
            Assert.AreEqual(FighterChaseData.DefaultTurnSeconds, PlayerData.TurnSecondsOrDefault(null), 1e-6f);

            var g = new GameBalance { player = new PlayerData { turnSeconds = 0f } };
            Assert.AreEqual(FighterChaseData.Resolve(g).turnSeconds, PlayerData.TurnSecondsOrDefault(g), 1e-6f);

            g.player.turnSeconds = 0.9f;
            Assert.AreEqual(0.9f, PlayerData.TurnSecondsOrDefault(g), 1e-6f);
        }

        // ── Player plane ────────────────────────────────────────────────

        [Test]
        public void Create_StartsUnsquashed_Unmirrored() {
            MakePlane();
            Assert.AreEqual(1f, ScaleX, 1e-6f);
            Assert.IsFalse(FlipX);
            Assert.AreEqual(1f, _plane.TurnHeading, 1e-6f);
            Assert.IsFalse(_plane.IsTurning);
            Assert.AreEqual(1, _plane.VisualFacing);
            Assert.Greater(_plane.TurnSeconds, 0f);
        }

        [Test]
        public void Turn_FollowsTheFighterCurve_SquashesTo015_ThenMirrors() {
            MakePlane();
            float baseWidth = WorldBoxWidth;
            float expected = 1f;
            float minScale = 1f;
            bool sawFlip = false;

            Move(left: true);
            Assert.AreEqual(-1, _plane.Facing, "gameplay facing switches on the key press");
            Assert.AreEqual(-1, _sc.Direction, "the scroll turns on the key press");
            expected = FighterAI.StepHeading(expected, -1, _plane.TurnSeconds, Time.fixedDeltaTime);
            for (int i = 0; i < TicksForTurn; i++) {
                Assert.AreEqual(expected, _plane.TurnHeading, 1e-5f, $"tick {i}: same StepHeading curve as fighters");
                Assert.AreEqual(FighterAI.TurnScaleX(expected), ScaleX, 1e-5f, $"tick {i}: x scale = TurnScaleX");
                Assert.AreEqual(_plane.VisualFacing < 0, FlipX, $"tick {i}: flipX follows the drawn facing");
                Assert.AreEqual(baseWidth, WorldBoxWidth, 1e-4f, $"tick {i}: the world hitbox is not squashed");
                minScale = Mathf.Min(minScale, ScaleX);
                sawFlip |= FlipX;
                Move(left: true);
                expected = FighterAI.StepHeading(expected, -1, _plane.TurnSeconds, Time.fixedDeltaTime);
            }
            Assert.AreEqual(FighterAI.MinTurnScale, minScale, 0.02f, "squashes to about 0.15 at the midpoint");
            Assert.IsTrue(sawFlip);
            Assert.IsFalse(_plane.IsTurning);
            Assert.IsTrue(FlipX, "drawn mirrored (art faces right) after turning left");
            Assert.AreEqual(1f, ScaleX, 1e-6f);
            Assert.AreEqual(-1, _plane.VisualFacing);
        }

        [Test]
        public void ReverseMidTurn_TurnsBackSmoothlyFromTheCurrentHeading() {
            MakePlane();
            float rate = 2f / _plane.TurnSeconds * Time.fixedDeltaTime;
            for (int i = 0; i < 5; i++) Move(left: true);
            float h = _plane.TurnHeading;
            Assert.Less(h, 1f);
            Assert.Greater(h, 0f);

            Move(right: true);
            Assert.AreEqual(1, _plane.Facing);
            Assert.AreEqual(1, _sc.Direction);
            Assert.AreEqual(h + rate, _plane.TurnHeading, 1e-5f, "no jump: one step back toward +1");
            for (int i = 0; i < TicksForTurn; i++) Move();
            Assert.AreEqual(1f, _plane.TurnHeading, 1e-6f);
            Assert.AreEqual(1f, ScaleX, 1e-6f);
            Assert.IsFalse(FlipX);
        }

        [Test]
        public void Pitch_MatchesTheDrawnFacing_ThroughTheTurn_AndInBothFacings() {
            MakePlane();
            for (int i = 0; i < 30; i++) Move(up: true);
            Assert.Greater(_plane.PitchAngle, 0f);
            Assert.Greater(_plane.BodyAngle, 0f, "facing right, climbing: nose up = positive z");

            Move(left: true, up: true);
            for (int i = 0; i < TicksForTurn; i++) {
                Assert.AreEqual(PlanePitch.BodyAngle(_plane.PitchAngle, _plane.VisualFacing), _plane.BodyAngle, 1e-4f,
                    "the body rotation mirrors with the sprite, not before it");
                Vector3 local = FlipX ? Vector3.left : Vector3.right;
                Vector2 drawn = Quaternion.Euler(0f, 0f, _plane.BodyAngle) * local;
                Assert.Greater(drawn.y, 0f, $"tick {i}: climbing, the drawn nose stays up during the turn");
                Move(up: true);
            }
            Assert.Less(_plane.BodyAngle, 0f, "facing left, climbing: the mirrored body angle");
            Assert.Less(Vector2.Angle(Quaternion.Euler(0f, 0f, _plane.BodyAngle) * Vector3.left, _plane.NoseDirection), 0.5f);
        }

        [Test]
        public void Gun_FiresTheNewWay_OnTheFirstTickOfTheTurn() {
            MakePlane();
            Move(left: true);
            Assert.IsTrue(_plane.IsTurning);

            var before = new HashSet<Bullet>(Object.FindObjectsByType<Bullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            var onFire = typeof(PlayerPlane).GetMethod("OnFire", Private);
            Assert.IsNotNull(onFire, "PlayerPlane.OnFire not found by reflection");
            onFire.Invoke(_plane, null);
            Bullet fired = null;
            foreach (var b in Object.FindObjectsByType<Bullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) {
                if (!before.Contains(b)) fired = b;
            }
            Assert.IsNotNull(fired, "the player keeps firing while turning");
            Assert.Less(fired.GetComponent<Rigidbody2D>().linearVelocity.x, 0f);
            Vector2 offset = (Vector2)(fired.transform.position - _plane.transform.position);
            Assert.AreEqual(-0.7f, offset.x, 1e-4f, "muzzle not scaled by the squash");
        }

        [Test]
        public void Crash_MidTurn_FinishesTheTurnOnTheWreck() {
            MakePlane();
            for (int i = 0; i < 5; i++) Move(left: true);
            Assert.IsTrue(_plane.IsTurning);
            _plane.ForceCrash();
            Assert.IsTrue(_plane.HasCrashed);

            var fixedUpdate = typeof(PlayerPlane).GetMethod("FixedUpdate", Private);
            Assert.IsNotNull(fixedUpdate, "PlayerPlane.FixedUpdate not found by reflection");
            for (int i = 0; i < TicksForTurn; i++) fixedUpdate.Invoke(_plane, null);
            Assert.IsFalse(_plane.IsTurning);
            Assert.AreEqual(1f, ScaleX, 1e-6f, "the wreck isn't left squashed");
            Assert.IsTrue(FlipX);
        }
    }
}
