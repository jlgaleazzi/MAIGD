using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Player;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// T925: EditMode tests for the T924 Defender-style facing on PlayerPlane: A/D facing
    /// rules, the sprite flip, ScrollController.SetDirection, the screen-anchor easing target,
    /// the takeoff climb-out, and the bullet direction / mirrored gun offset.
    ///
    /// PlayerPlane.OnMove is public and is called directly, one physics tick per call. No
    /// physics step runs in EditMode, so Rigidbody2D.position stays where the test put it and
    /// each OnMove is measured through the private _velocity field ((target - start) / dt).
    /// </summary>
    public class PlayerFacingTests {

        private GameObject _camGo;
        private Camera _cam;
        private ScrollController _sc;
        private PlayerPlane _plane;
        private readonly List<Camera> _disabledCameras = new List<Camera>();

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

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

        private PlayerPlane MakePlane(Vector2 position, bool onRunway) {
            _plane = PlayerPlane.Create(new Vector3(position.x, position.y, 0f), onRunway);
            PlaceAt(position);
            return _plane;
        }

        private void PlaceAt(Vector2 position) {
            _plane.transform.position = new Vector3(position.x, position.y, 0f);
            _plane.GetComponent<Rigidbody2D>().position = position;
            Physics2D.SyncTransforms();
        }

        private T Field<T>(string name) {
            var f = typeof(PlayerPlane).GetField(name, Private);
            Assert.IsNotNull(f, $"PlayerPlane.{name} not found by reflection");
            return (T)f.GetValue(_plane);
        }

        /// <summary>Runs one OnMove tick and returns the plane's world velocity for that tick.</summary>
        private Vector2 Move(bool left = false, bool right = false, bool up = false, bool down = false) {
            _plane.OnMove(new InputState { left = left, right = right, up = up, down = down });
            return Field<Vector2>("_velocity");
        }

        private float Speed => Field<float>("_speed");

        private float AnchorX(int facing) {
            float a = Field<float>("_screenAnchor");
            float viewport = facing > 0 ? a : 1f - a;
            float halfWidth = _cam.orthographicSize * _cam.aspect;
            return _cam.transform.position.x + (viewport - 0.5f) * 2f * halfWidth;
        }

        private bool FlipX => _plane.GetComponent<SpriteRenderer>().flipX;

        /// <summary>B066: the sprite turns round over TurnSeconds (eased squash, flipX at the
        /// midpoint) while Facing switches at once. Ticks with no keys until the look is done.</summary>
        private void FinishTurn() {
            for (int i = 0; i < 500 && _plane.IsTurning; i++) Move();
            Assert.IsFalse(_plane.IsTurning, "the turn-around must finish");
        }

        // ── Facing rules ────────────────────────────────────────────────

        [Test]
        public void StartsFacingRight() {
            MakePlane(Vector2.zero, false);
            Assert.AreEqual(1, _plane.Facing);
            Assert.IsFalse(FlipX);
        }

        [Test]
        public void A_TurnsLeft_FlipsSprite_AndScrollsLeft() {
            MakePlane(Vector2.zero, false);
            Move(left: true);
            Assert.AreEqual(-1, _plane.Facing);
            Assert.AreEqual(-1, _sc.Direction, "the scroll turns on the key press");
            Assert.IsTrue(_plane.IsTurning, "B066: the sprite eases round, no instant flip");
            Assert.IsFalse(FlipX, "B066: still drawn facing right on the first tick of the turn");
            FinishTurn();
            Assert.IsTrue(FlipX);
            Assert.AreEqual(1f, _plane.transform.localScale.x, 1e-5f, "full width after the turn");
        }

        [Test]
        public void D_TurnsBackRight() {
            MakePlane(Vector2.zero, false);
            Move(left: true);
            Move(right: true);
            Assert.AreEqual(1, _plane.Facing);
            Assert.IsFalse(FlipX);
            Assert.AreEqual(1, _sc.Direction);
            FinishTurn();
            Assert.IsFalse(FlipX);
        }

        [Test]
        public void NeitherHeld_KeepsFacing() {
            MakePlane(Vector2.zero, false);
            Move(left: true);
            Move();
            Move(up: true);
            Assert.AreEqual(-1, _plane.Facing);
            Assert.AreEqual(-1, _sc.Direction);
        }

        [Test]
        public void BothHeld_LastPressedWins_ReleaseFallsBackToHeldKey() {
            MakePlane(Vector2.zero, false);
            Move(right: true);
            Move(left: true, right: true);          // A pressed while D held
            Assert.AreEqual(-1, _plane.Facing, "A pressed last should win");
            Move(left: true, right: true);          // both still held
            Assert.AreEqual(-1, _plane.Facing, "holding both must not toggle");
            Move(right: true);                      // A released
            Assert.AreEqual(1, _plane.Facing, "releasing A should fall back to the held D");

            Move(left: true);
            Move(left: true, right: true);          // D pressed while A held
            Assert.AreEqual(1, _plane.Facing, "D pressed last should win");
        }

        [Test]
        public void BothPressedOnSameTick_KeepsFacing() {
            MakePlane(Vector2.zero, false);
            Move();
            Move(left: true, right: true);
            Assert.AreEqual(1, _plane.Facing);
            Assert.AreEqual(1, _sc.Direction);
        }

        // ── Runway / takeoff ────────────────────────────────────────────

        [Test]
        public void Runway_A_DoesNothing() {
            MakePlane(Vector2.zero, true);
            Move(left: true);
            Assert.IsTrue(_plane.OnRunway);
            Assert.AreEqual(1, _plane.Facing);
            Assert.AreEqual(1, _sc.Direction);
        }

        [Test]
        public void Runway_D_TakesOffFacingRight_WithClimbOut() {
            MakePlane(Vector2.zero, true);
            Vector2 v = Move(right: true);
            Assert.IsFalse(_plane.OnRunway);
            Assert.AreEqual(1, _plane.Facing);
            Assert.AreEqual(Speed, v.y, 1e-3f, "climb-out should force a climb on the takeoff tick");
        }

        [Test]
        public void ClimbOut_StillForcesUpAfterTakeoff_UnlessDiving() {
            MakePlane(Vector2.zero, true);
            Move(up: true);
            Vector2 v = Move();
            Assert.AreEqual(Speed, v.y, 1e-3f, "climb-out should keep climbing with no keys held");
            v = Move(down: true);
            Assert.AreEqual(-Speed, v.y, 1e-3f, "S must override the climb-out");
        }

        // T927 (B003): W+A on the runway takes off facing right, and the A held through takeoff
        // stays ignored on the following ticks until it is released and pressed again.
        [Test]
        public void Runway_WPlusA_HeldA_IgnoredUntilReleasedAndRepressed() {
            MakePlane(Vector2.zero, true);
            Move(left: true, up: true);
            Assert.IsFalse(_plane.OnRunway, "W should take off");
            Assert.AreEqual(1, _plane.Facing, "the takeoff tick must face right");
            Assert.IsFalse(FlipX);
            Assert.AreEqual(1, _sc.Direction);

            Move(left: true, up: true);
            Move(left: true);
            Move(left: true);
            Assert.AreEqual(1, _plane.Facing, "an A held since takeoff must not turn the plane");
            Assert.IsFalse(FlipX);
            Assert.AreEqual(1, _sc.Direction);

            Move();                                 // A released
            Assert.AreEqual(1, _plane.Facing);
            Move(left: true);                       // fresh A press
            Assert.AreEqual(-1, _plane.Facing, "a fresh A after the release turns left");
            FinishTurn();
            Assert.IsTrue(FlipX);
            Assert.AreEqual(-1, _sc.Direction);
        }

        // T927 (B003): A pressed alone on the runway does nothing; W pressed while A is still held
        // takes off facing right, and that A is ignored until re-pressed.
        [Test]
        public void Runway_AHeldThenW_TakesOffRight_AndAStaysIgnored() {
            MakePlane(Vector2.zero, true);
            Move(left: true);
            Move(left: true);
            Assert.IsTrue(_plane.OnRunway, "A alone must not take off");
            Assert.AreEqual(1, _plane.Facing);

            Move(left: true, up: true);
            Assert.IsFalse(_plane.OnRunway);
            Move(left: true);
            Assert.AreEqual(1, _plane.Facing, "A held from before takeoff must stay ignored");

            Move();
            Move(left: true);
            Assert.AreEqual(-1, _plane.Facing);
        }

        // T927 (B003): D+A held at takeoff faces right; D still turns right and a fresh A still turns left.
        [Test]
        public void Runway_DPlusA_TakesOffRight_DStillWorks_FreshATurnsLeft() {
            MakePlane(Vector2.zero, true);
            Move(left: true, right: true);
            Assert.IsFalse(_plane.OnRunway, "D should take off");
            Assert.AreEqual(1, _plane.Facing);

            Move(left: true, right: true);
            Move(left: true);                        // D released, held A still ignored
            Assert.AreEqual(1, _plane.Facing, "the A held through takeoff must not turn the plane");

            Move();
            Move(left: true);
            Assert.AreEqual(-1, _plane.Facing, "fresh A turns left");
            Move(right: true);
            Assert.AreEqual(1, _plane.Facing, "D turns back right");
        }

        // T927 (B003): an airborne start never ignores A: a held A turns left on the first tick.
        [Test]
        public void AirborneStart_HeldA_TurnsLeftImmediately() {
            MakePlane(Vector2.zero, false);
            Move(left: true);
            Assert.AreEqual(-1, _plane.Facing);
            Assert.IsFalse(Field<bool>("_ignoreHeldLeft"));
        }

        // T926 (B002a): after takeoff, a fresh A press turns left as usual.
        [Test]
        public void AfterTakeoff_FreshA_TurnsLeft() {
            MakePlane(Vector2.zero, true);
            Move(up: true);
            Move();
            Move(left: true);
            Assert.AreEqual(-1, _plane.Facing);
            Assert.AreEqual(-1, _sc.Direction);
            Assert.IsFalse(Field<bool>("_ignoreHeldLeft"), "D/W takeoff without A must not arm the ignore");
        }

        // T926 (B001): a plane deactivated by MissionRunner's teardown ignores input until it dies.
        [Test]
        public void InactivePlane_IgnoresMoveAndFire() {
            MakePlane(Vector2.zero, false);
            _plane.gameObject.SetActive(false);
            Move(left: true);
            Assert.AreEqual(1, _plane.Facing, "an inactive plane must not turn");
            Assert.AreEqual(1, _sc.Direction, "an inactive plane must not steer the ScrollController");

            int before = Object.FindObjectsByType<Bullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            var onFire = typeof(PlayerPlane).GetMethod("OnFire", Private);
            Assert.IsNotNull(onFire, "PlayerPlane.OnFire not found by reflection");
            onFire.Invoke(_plane, null);
            int after = Object.FindObjectsByType<Bullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            Assert.AreEqual(before, after, "an inactive plane must not fire");
        }

        // ── Screen anchor easing ────────────────────────────────────────

        [Test]
        public void FacingRight_EasesTowardLeftAnchor_AtPlayerSpeed() {
            MakePlane(Vector2.zero, false);
            Assert.Less(AnchorX(1), -Speed * Time.fixedDeltaTime, "test needs the anchor more than one tick away");
            Vector2 v = Move();
            Assert.AreEqual(-Speed, v.x, 1e-3f);
        }

        [Test]
        public void FacingLeft_EasesTowardRightAnchor_AtPlayerSpeed() {
            MakePlane(Vector2.zero, false);
            Vector2 v = Move(left: true);
            Assert.Greater(AnchorX(-1), Speed * Time.fixedDeltaTime);
            Assert.AreEqual(Speed, v.x, 1e-3f);
        }

        [Test]
        public void Anchor_LandsExactlyWithoutOvershoot() {
            MakePlane(Vector2.zero, false);
            float dt = Time.fixedDeltaTime;
            float anchor = AnchorX(1);

            PlaceAt(new Vector2(anchor + 0.05f, 0f));
            Vector2 v = Move();
            Assert.AreEqual(-0.05f, v.x * dt, 1e-4f, "should stop on the anchor, not pass it");

            PlaceAt(new Vector2(anchor, 0f));
            v = Move();
            Assert.AreEqual(0f, v.x, 1e-2f, "no jitter once sitting on the anchor");
        }

        [Test]
        public void Anchors_AreMirrored() {
            MakePlane(Vector2.zero, false);
            Assert.AreEqual(-AnchorX(1), AnchorX(-1), 1e-4f);
            float a = Field<float>("_screenAnchor");
            Assert.That(a, Is.GreaterThan(0f).And.LessThan(0.5f), "screenAnchor should be in the left half");
        }

        // ── Bullets ─────────────────────────────────────────────────────

        [Test]
        public void BulletSpawn_DirectionSetsVelocitySign() {
            MakePlane(Vector2.zero, false); // initialises the bullet pool

            var left = Bullet.Spawn(Vector3.zero, Quaternion.identity, -1);
            Assert.IsNotNull(left);
            Vector2 lv = left.GetComponent<Rigidbody2D>().linearVelocity;
            Assert.Less(lv.x, 0f);
            Assert.AreEqual(0f, lv.y);

            var right = Bullet.Spawn(Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(right);
            Assert.Greater(right.GetComponent<Rigidbody2D>().linearVelocity.x, 0f,
                "the 2-argument overload must keep firing +x");
        }

        // T926 (B002c): Bullet loads Balance speed/damage in Awake, so the first Pop already flies
        // at bulletSpeed. EditMode doesn't run Awake on AddComponent, so the factory invokes it the
        // way Play mode does (AddComponent on an active GameObject runs Awake immediately).
        [Test]
        public void Bullet_FirstSpawn_UsesBalanceSpeedAndDamage() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.player != null, "Balance.json not loadable");
            Assert.IsNull(typeof(Bullet).GetMethod("Start", Private | BindingFlags.Public),
                "Bullet.Start would re-apply tunables after Init; they belong in Awake");
            var awake = typeof(Bullet).GetMethod("Awake", Private);
            Assert.IsNotNull(awake, "Bullet.Awake not found by reflection");

            Bullet.InitPool(() => {
                var go = new GameObject("T926_Bullet");
                go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                go.AddComponent<CircleCollider2D>().isTrigger = true;
                var b = go.AddComponent<Bullet>();
                awake.Invoke(b, null);
                return b;
            }, 2);

            var first = Bullet.Spawn(Vector3.zero, Quaternion.identity, 1);
            Assert.IsNotNull(first);
            Assert.AreEqual(g.player.bulletSpeed, first.Speed, 1e-4f);
            Assert.AreEqual(g.player.bulletDamage, first.Damage, 1e-4f);
            Assert.AreEqual(g.player.bulletSpeed, first.GetComponent<Rigidbody2D>().linearVelocity.x, 1e-3f,
                "the first shot must already fly at Balance bulletSpeed");
        }

        [Test]
        public void OnFire_FacingLeft_SpawnsAtMirroredGunAndFliesLeft() {
            MakePlane(Vector2.zero, false);
            Move(left: true);

            var before = new HashSet<Bullet>(Object.FindObjectsByType<Bullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            var onFire = typeof(PlayerPlane).GetMethod("OnFire", Private);
            Assert.IsNotNull(onFire, "PlayerPlane.OnFire not found by reflection");
            onFire.Invoke(_plane, null);

            Bullet fired = null;
            foreach (var b in Object.FindObjectsByType<Bullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) {
                if (!before.Contains(b)) fired = b;
            }
            Assert.IsNotNull(fired, "OnFire did not spawn a bullet");
            Assert.Less(fired.transform.position.x, _plane.transform.position.x, "gun offset should be mirrored to the nose (left)");
            Assert.Less(fired.GetComponent<Rigidbody2D>().linearVelocity.x, 0f);
        }

        [Test]
        public void OnFire_FacingLeftOrRight_WhileClimbing_BulletLeavesTheNose() {
            // B029 (P0-1): with pitch, the shot still leaves ahead of the plane in its facing.
            MakePlane(Vector2.zero, false);
            var onFire = typeof(PlayerPlane).GetMethod("OnFire", Private);
            Assert.IsNotNull(onFire, "PlayerPlane.OnFire not found by reflection");

            foreach (int facing in new[] { 1, -1 }) {
                if (facing < 0) Move(left: true); else Move(right: true);
                for (int i = 0; i < 30; i++) Move(up: true);
                Assert.AreEqual(facing, _plane.Facing);

                var before = new HashSet<Bullet>(Object.FindObjectsByType<Bullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
                onFire.Invoke(_plane, null);
                Bullet fired = null;
                foreach (var b in Object.FindObjectsByType<Bullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) {
                    if (!before.Contains(b)) fired = b;
                }
                Assert.IsNotNull(fired, "OnFire did not spawn a bullet");
                Vector2 v = fired.GetComponent<Rigidbody2D>().linearVelocity;
                float dx = fired.transform.position.x - _plane.transform.position.x;
                Assert.AreEqual(facing, (int)Mathf.Sign(v.x), $"facing {facing}: the shot flies the way the plane faces");
                Assert.AreEqual(facing, (int)Mathf.Sign(dx), $"facing {facing}: the muzzle is at the nose, not the tail");
                Assert.Greater(v.y, 0f, "climbing: the shot rises with the nose");
            }
        }
    }
}
