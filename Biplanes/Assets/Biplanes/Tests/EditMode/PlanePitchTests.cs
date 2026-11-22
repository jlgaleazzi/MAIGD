using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Missions;
using Biplanes.Player;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// T942/B029: GDD "Flight attitude (pitch)". Every airplane noses up while it climbs and
    /// down while it dives, eased (pitch.easeDegreesPerSecond) and clamped (pitch.maxDegrees);
    /// the body (sprite and hitboxes) turns with Rigidbody2D.MoveRotation; machine-gun bullets
    /// leave along the nose.
    ///
    /// EditMode runs no physics step, so a MoveRotation is never applied to the transform.
    /// The tests therefore read the body angle each plane drives MoveRotation with (BodyAngle)
    /// and rebuild the drawn nose from it: the art faces right, so the nose is local +x, or
    /// local -x when SpriteRenderer.flipX mirrors it; world nose = Euler(0,0,BodyAngle) * that.
    /// Rigidbody2D.position also stays where the test put it, so each PlayerPlane.OnMove tick
    /// reports the same climb/dive velocity. Awake/FixedUpdate are invoked by reflection where
    /// needed (same pattern as PlayerFacingTests/EnemyFireTests).
    /// </summary>
    public class PlanePitchTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const float Dt = 0.02f;

        private GameObject _camGo;
        private Camera _cam;
        private ScrollController _sc;
        private PlayerPlane _plane;
        private readonly List<Camera> _disabledCameras = new List<Camera>();
        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp() {
            _disabledCameras.Clear();
            foreach (var c in Camera.allCameras) {
                if (c != null && c.CompareTag("MainCamera")) {
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
            }

            _camGo = new GameObject("T942_TestCamera");
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
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();

            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (p != null) Object.DestroyImmediate(p.gameObject);
            }
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go == null) continue;
                bool hiddenPool = go.hideFlags == HideFlags.HideAndDontSave && go.name.StartsWith("Pool<");
                bool enemyPool = (go.name == "EnemyBulletPool" || go.name == "Pool<EnemyBullet>") && go.scene.IsValid();
                if (hiddenPool || enemyPool) Object.DestroyImmediate(go);
            }
            typeof(EnemyBullet).GetField("_cam", PrivateStatic)?.SetValue(null, null);

            foreach (var c in _disabledCameras) {
                if (c != null) c.enabled = true;
            }
            _disabledCameras.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static PitchData Pitch => PitchData.Resolve(GameBalance.Get());

        /// <summary>World direction of the drawn nose for a body turned by `bodyAngle`: local
        /// +x, or -x when the sprite is flipped.</summary>
        private static Vector2 DrawnNose(float bodyAngle, SpriteRenderer sr) {
            Vector3 local = sr != null && sr.flipX ? Vector3.left : Vector3.right;
            return (Vector2)(Quaternion.Euler(0f, 0f, bodyAngle) * local);
        }

        private Vector2 PlayerDrawnNose() {
            return DrawnNose(_plane.BodyAngle, _plane.GetComponent<SpriteRenderer>());
        }

        /// <summary>Ticks for a nose to swing `degrees` at the Balance ease rate, plus margin.</summary>
        private static int TicksToTurn(float degrees) {
            return Mathf.CeilToInt(degrees / Pitch.easeDegreesPerSecond / Dt) + 2;
        }

        private void MakePlane(Vector2 position) {
            _plane = PlayerPlane.Create(new Vector3(position.x, position.y, 0f), false);
            _plane.transform.position = new Vector3(position.x, position.y, 0f);
            _plane.GetComponent<Rigidbody2D>().position = position;
            Physics2D.SyncTransforms();
        }

        private void Move(int ticks, bool left = false, bool right = false, bool up = false, bool down = false) {
            for (int i = 0; i < ticks; i++) {
                _plane.OnMove(new InputState { left = left, right = right, up = up, down = down });
            }
        }

        private Bullet FirePlayer() {
            var before = new HashSet<Bullet>(Object.FindObjectsByType<Bullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            var onFire = typeof(PlayerPlane).GetMethod("OnFire", Private);
            Assert.IsNotNull(onFire, "PlayerPlane.OnFire not found by reflection");
            onFire.Invoke(_plane, null);
            Bullet fired = null;
            foreach (var b in Object.FindObjectsByType<Bullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) {
                if (!before.Contains(b)) fired = b;
            }
            Assert.IsNotNull(fired, "OnFire did not spawn a bullet");
            return fired;
        }

        private static void InvokeAwake(Component c) {
            var awake = c.GetType().GetMethod("Awake", Private);
            Assert.IsNotNull(awake, $"{c.GetType().Name}.Awake not found by reflection");
            awake.Invoke(c, null);
        }

        private static object Invoke(Component c, string method, params object[] args) {
            var m = c.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{c.GetType().Name}.{method} not found by reflection");
            return m.Invoke(c, args);
        }

        private static void SetField(object target, string name, object value) {
            var f = target.GetType().GetField(name, Private);
            Assert.IsNotNull(f, $"{target.GetType().Name}.{name} not found by reflection");
            f.SetValue(target, value);
        }

        /// <summary>A player stand-in for enemy fire: TeamTag(Player) + Health + PlayerPlane
        /// with Awake not run, at `position`.</summary>
        private PlayerPlane MakeTarget(Vector2 position) {
            var go = new GameObject("player");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<TeamTag>().team = Team.Player;
            InvokeAwake(go.AddComponent<Health>());
            return go.AddComponent<PlayerPlane>();
        }

        /// <summary>A fighter as it is in play after Start: Awake run, sprite mirrored to fly left.</summary>
        private FighterAI MakeFighter(Vector2 position, PlayerPlane target) {
            var f = FighterAI.Spawn(position);
            _created.Add(f.gameObject);
            InvokeAwake(f.GetComponent<Health>());
            InvokeAwake(f);
            f.GetComponent<SpriteRenderer>().flipX = true; // FighterAI.Start
            if (target != null) {
                var prop = typeof(FighterAI).GetProperty("PlayerRef", Private);
                Assert.IsNotNull(prop, "FighterAI.PlayerRef not found by reflection");
                prop.SetValue(f, target);
            }
            return f;
        }

        /// <summary>Lets a fighter in level flight track the player for `ticks` physics ticks.</summary>
        private static void TrackPlayer(FighterAI f, int ticks) {
            for (int i = 0; i < ticks; i++) Invoke(f, "UpdatePitch", 0f, Dt);
        }

        private static Vector2 FighterDrawnNose(FighterAI f) {
            return DrawnNose(f.BodyAngle, f.GetComponent<SpriteRenderer>());
        }

        private static EnemyBullet SingleActiveEnemyBullet() {
            var shots = Object.FindObjectsByType<EnemyBullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Assert.AreEqual(1, shots.Length, "expected exactly one enemy shot");
            return shots[0];
        }

        // ── PlanePitch (pure) ───────────────────────────────────────────

        [Test]
        public void Step_Climb_RaisesNose_Dive_LowersNose() {
            Assert.Greater(PlanePitch.Step(0f, 5f, 15f, Dt), 0f, "climbing must raise the nose");
            Assert.Less(PlanePitch.Step(0f, -5f, 15f, Dt), 0f, "diving must lower the nose");
        }

        [Test]
        public void Step_IsEased_NotASnap() {
            float a = PlanePitch.Step(0f, 5f, 15f, Dt);
            Assert.Less(a, 15f, "one tick must not jump to the full angle");
        }

        [Test]
        public void Step_ClampedToMax_BothWays() {
            float up = 0f, down = 0f;
            for (int i = 0; i < 1000; i++) {
                up = PlanePitch.Step(up, 50f, 15f, Dt);
                down = PlanePitch.Step(down, -50f, 15f, Dt);
                Assert.LessOrEqual(up, 15f + 1e-4f);
                Assert.GreaterOrEqual(down, -15f - 1e-4f);
            }
            Assert.AreEqual(15f, up, 1e-3f, "sustained climb settles at +max");
            Assert.AreEqual(-15f, down, 1e-3f, "sustained dive settles at -max");
        }

        [Test]
        public void Step_LevelFlight_ReturnsToZero() {
            float a = 15f;
            for (int i = 0; i < 1000; i++) a = PlanePitch.Step(a, 0f, 15f, Dt);
            Assert.AreEqual(0f, a, 1e-3f);
        }

        [Test]
        public void Step_ZeroDt_NoChange() {
            Assert.AreEqual(7f, PlanePitch.Step(7f, -5f, 15f, 0f), 1e-5f);
        }

        [Test]
        public void Step_EasesAtTheBalanceRate_FullPitchWithinAQuarterSecond() {
            // P2-1: the rate is pitch.easeDegreesPerSecond (60), not maxDegrees per second (15).
            var p = Pitch;
            float a = PlanePitch.Step(0f, 50f, p, Dt);
            Assert.AreEqual(Mathf.Min(p.maxDegrees, p.easeDegreesPerSecond * Dt), a, 1e-4f, "one tick turns rate*dt");
            Assert.LessOrEqual(p.maxDegrees / p.easeDegreesPerSecond, 0.3f,
                "the nose reaches full pitch within 0.3 s of W/S");
        }

        [Test]
        public void Step_TargetScalesWithClimbSpeed() {
            var p = Pitch;
            float half = PlanePitch.TargetFromVerticalSpeed(p.fullPitchSpeed * 0.5f, p.maxDegrees, p.fullPitchSpeed);
            Assert.AreEqual(p.maxDegrees * 0.5f, half, 1e-3f, "half the full-pitch speed, half the angle");
            Assert.AreEqual(p.maxDegrees,
                PlanePitch.TargetFromVerticalSpeed(p.fullPitchSpeed * 4f, p.maxDegrees, p.fullPitchSpeed), 1e-4f);
        }

        [Test]
        public void NoseDirection_And_BodyAngle_MirrorByFacing() {
            Vector2 right = PlanePitch.NoseDirection(10f, 1);
            Vector2 left = PlanePitch.NoseDirection(10f, -1);
            Assert.Greater(right.x, 0f);
            Assert.Less(left.x, 0f, "facing left, the nose points left");
            Assert.Greater(right.y, 0f);
            Assert.Greater(left.y, 0f, "nose up is up in both facings");
            Assert.AreEqual(10f, PlanePitch.BodyAngle(10f, 1), 1e-5f);
            Assert.AreEqual(-10f, PlanePitch.BodyAngle(10f, -1), 1e-5f);

            // The drawn nose of a flipped sprite at BodyAngle matches NoseDirection.
            Vector2 drawnLeft = (Vector2)(Quaternion.Euler(0f, 0f, PlanePitch.BodyAngle(10f, -1)) * Vector3.left);
            Assert.Less(Vector2.Angle(drawnLeft, left), 0.01f);
        }

        [Test]
        public void PitchData_Resolve_DefaultsWhenMissing_SharedFallback() {
            // P2-4: one rule for every plane: a missing block or non-positive key -> PlanePitch.Default*.
            var none = PitchData.Resolve(null);
            Assert.AreEqual(PlanePitch.DefaultMaxDegrees, none.maxDegrees);
            Assert.AreEqual(PlanePitch.DefaultFighterMaxDegrees, none.fighterMaxDegrees);
            Assert.AreEqual(PlanePitch.DefaultEaseDegreesPerSecond, none.easeDegreesPerSecond);
            Assert.AreEqual(PlanePitch.DefaultFullPitchSpeed, none.fullPitchSpeed);

            var empty = PitchData.Resolve(new GameBalance { pitch = new PitchData() });
            Assert.AreEqual(PlanePitch.DefaultMaxDegrees, empty.maxDegrees, "an empty pitch block must not disable pitch");
        }

        [Test]
        public void Balance_HasPitchBlock() {
            var g = GameBalance.Get();
            Assume.That(g != null, "Balance.json not loadable");
            Assert.IsNotNull(g.pitch, "Balance.json pitch block");
            Assert.Greater(g.pitch.maxDegrees, 0f);
            Assert.Greater(g.pitch.fighterMaxDegrees, 0f);
            Assert.Greater(g.pitch.easeDegreesPerSecond, 0f);
            Assert.Greater(g.pitch.fullPitchSpeed, 0f);
        }

        [Test]
        public void LowestPointOfBox_PitchedBox_DropsAboutPoint19_At15Degrees() {
            // P2-5: documented, accepted difference. A 1.6x0.8 box pitched 15 degrees reaches
            // 0.5*(1.6 sin15 + 0.8 cos15) = 0.593 below its centre, against 0.40 level.
            Vector2 size = new Vector2(1.6f, 0.8f);
            float level = PlanePitch.LowestPointOfBox(Vector2.zero, size, Vector2.zero, 0f);
            float up = PlanePitch.LowestPointOfBox(Vector2.zero, size, Vector2.zero, 15f);
            float down = PlanePitch.LowestPointOfBox(Vector2.zero, size, Vector2.zero, -15f);
            Assert.AreEqual(-0.4f, level, 1e-4f);
            Assert.AreEqual(-0.5934f, up, 1e-3f);
            Assert.AreEqual(up, down, 1e-5f, "nose up and nose down drop the same");
            Assert.AreEqual(0.193f, level - up, 2e-3f);
        }

        // ── Player ──────────────────────────────────────────────────────

        [Test]
        public void Player_NewPlane_StartsLevel() {
            MakePlane(Vector2.zero);
            Assert.AreEqual(0f, _plane.PitchAngle, 1e-4f);
            Assert.AreEqual(0f, _plane.BodyAngle, 1e-4f);
            Assert.AreEqual(1f, _plane.NoseDirection.x, 1e-4f);
            Assert.AreEqual(0f, _plane.NoseDirection.y, 1e-4f);
        }

        [Test]
        public void Player_FacingRight_Climb_NoseUp_Dive_NoseDown() {
            MakePlane(Vector2.zero);
            Move(30, up: true);
            Vector2 nose = PlayerDrawnNose();
            Assert.Greater(nose.x, 0f);
            Assert.Greater(nose.y, 0f, "climbing: the drawn nose must point up");

            Move(150, down: true);
            nose = PlayerDrawnNose();
            Assert.Less(nose.y, 0f, "diving: the drawn nose must point down");
        }

        [Test]
        public void Player_FacingLeft_Climb_DrawnNoseUp() {
            MakePlane(Vector2.zero);
            Move(1, left: true);
            Assert.AreEqual(-1, _plane.Facing);
            Move(30, up: true);
            Vector2 nose = PlayerDrawnNose();
            Assert.Less(nose.x, 0f);
            Assert.Greater(nose.y, 0f, "facing left, climbing: the drawn nose must still point up");
        }

        [Test]
        public void Player_ReachesFullPitch_WithinTheEaseTime() {
            MakePlane(Vector2.zero);
            var p = Pitch;
            Move(TicksToTurn(p.maxDegrees), up: true);
            Assert.AreEqual(p.maxDegrees, _plane.PitchAngle, 1e-3f, "W held: full nose-up after max/ease seconds");
            Move(TicksToTurn(2f * p.maxDegrees), down: true);
            Assert.AreEqual(-p.maxDegrees, _plane.PitchAngle, 1e-3f, "full reversal after 2*max/ease seconds");
        }

        [Test]
        public void Player_Pitch_NeverExceedsBalanceMax() {
            MakePlane(Vector2.zero);
            float max = Pitch.maxDegrees;
            for (int i = 0; i < 200; i++) {
                Move(1, up: true);
                Assert.LessOrEqual(Mathf.Abs(_plane.PitchAngle), max + 1e-3f);
                Assert.LessOrEqual(Mathf.Abs(_plane.BodyAngle), max + 1e-3f);
            }
            Move(1, left: true);
            for (int i = 0; i < 300; i++) {
                Move(1, down: true);
                Assert.LessOrEqual(Mathf.Abs(_plane.PitchAngle), max + 1e-3f);
                Assert.LessOrEqual(Mathf.Abs(_plane.BodyAngle), max + 1e-3f);
            }
        }

        [Test]
        public void Player_Hitbox_RotatesWithTheSprite() {
            // P2-3: the body turns through Rigidbody2D.MoveRotation. The hitbox and the sprite
            // sit on the body's own GameObject, so the rotation turns both together.
            MakePlane(Vector2.zero);
            Move(30, up: true);
            var rb = _plane.GetComponent<Rigidbody2D>();
            var col = _plane.GetComponent<Collider2D>();
            var sr = _plane.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(col);
            Assert.AreSame(rb.gameObject, col.gameObject, "the hitbox is on the rotated body");
            Assert.AreSame(rb.gameObject, sr.gameObject, "the sprite is on the rotated body");
            Assert.AreNotEqual(0f, _plane.BodyAngle, "the body must actually be pitched");
        }

        [Test]
        public void Player_NoseDirection_MatchesDrawnNose_BothFacings() {
            MakePlane(Vector2.zero);
            Move(30, up: true);
            Vector2 drawn = PlayerDrawnNose();
            Assert.Less(Vector2.Angle(drawn, _plane.NoseDirection), 0.5f, "facing right");

            Move(1, left: true);
            Move(30, up: true);
            drawn = PlayerDrawnNose();
            Assert.Less(Vector2.Angle(drawn, _plane.NoseDirection), 0.5f,
                $"facing left: NoseDirection {_plane.NoseDirection} vs drawn nose {drawn}");
        }

        [Test]
        public void Player_FacingRight_Climbing_BulletLeavesAlongTheNose() {
            MakePlane(Vector2.zero);
            Move(30, up: true);
            Vector2 drawn = PlayerDrawnNose();

            var b = FirePlayer();
            Vector2 v = b.GetComponent<Rigidbody2D>().linearVelocity;
            Assert.Greater(v.y, 0f, "a climbing plane's shot rises");
            Assert.Less(Vector2.Angle(drawn, v), 0.5f, "bullet velocity along the nose");
            Vector2 offset = (Vector2)(b.transform.position - _plane.transform.position);
            Assert.Less(Vector2.Angle(drawn, offset), 0.5f, "muzzle on the nose axis, ahead of the centre");
            Assert.Less(Vector2.Angle(drawn, (Vector2)(b.transform.rotation * Vector3.right)), 0.5f,
                "bullet sprite turned to its flight direction");
        }

        [Test]
        public void Player_FacingLeft_Climbing_BulletLeavesAlongTheNose() {
            MakePlane(Vector2.zero);
            Move(1, left: true);
            Move(30, up: true);
            Vector2 drawn = PlayerDrawnNose();

            var b = FirePlayer();
            Vector2 v = b.GetComponent<Rigidbody2D>().linearVelocity;
            Assert.Less(v.x, 0f, "facing left, the shot must fly left");
            Assert.Greater(v.y, 0f, "a climbing plane's shot rises");
            Assert.Less(Vector2.Angle(drawn, v), 0.5f, "bullet velocity along the nose");
            Assert.Less(b.transform.position.x, _plane.transform.position.x, "muzzle at the nose (left)");
            Vector2 offset = (Vector2)(b.transform.position - _plane.transform.position);
            Assert.Less(Vector2.Angle(drawn, offset), 0.5f, "muzzle on the nose axis, ahead of the centre");
        }

        [Test]
        public void Player_FacingLeft_Level_MuzzleIsTheMirroredGunOffset() {
            // P0-1: level and facing left, the bullet leaves GunOffset.x (0.7) to the LEFT of
            // the centre and flies straight left, exactly as before pitch existed (T924/B002).
            MakePlane(Vector2.zero);
            Move(1, left: true);
            Assert.AreEqual(0f, _plane.PitchAngle, 1e-4f);

            var b = FirePlayer();
            Vector2 offset = (Vector2)(b.transform.position - _plane.transform.position);
            Assert.AreEqual(-0.7f, offset.x, 1e-4f, "gun offset mirrored to the nose");
            Assert.AreEqual(0f, offset.y, 1e-4f);
            Vector2 v = b.GetComponent<Rigidbody2D>().linearVelocity;
            Assert.Less(v.x, 0f);
            Assert.AreEqual(0f, v.y, 1e-4f);
        }

        [Test]
        public void Player_LevelFlight_FiresHorizontally_OnceEased() {
            MakePlane(Vector2.zero);
            Move(30, up: true);
            Move(TicksToTurn(Pitch.maxDegrees));
            var b = FirePlayer();
            Vector2 v = b.GetComponent<Rigidbody2D>().linearVelocity;
            Assert.AreEqual(0f, v.y, 1e-3f, "level flight keeps the B021 horizontal line of fire");
        }

        [Test]
        public void Player_GroundCheck_UsesTheLowestCornerOfTheRotatedHitbox() {
            // P2-5: the terrain check reads HitboxLowestPoint, computed from the box and the
            // body angle (not Collider2D.bounds, which would lag MoveRotation by a step).
            MakePlane(Vector2.zero);
            var box = _plane.GetComponent<BoxCollider2D>();
            Assert.IsNotNull(box);
            float level = _plane.HitboxLowestPoint().y;
            Assert.AreEqual(box.offset.y - box.size.y * 0.5f, level, 1e-3f, "level: the bottom edge");

            Move(30, up: true);
            float pitched = _plane.HitboxLowestPoint().y;
            float expected = PlanePitch.LowestPointOfBox(Vector2.zero, box.size, box.offset, _plane.BodyAngle);
            Assert.AreEqual(expected, pitched, 1e-3f);
            Assert.Less(pitched, level, "pitched, a box corner reaches lower (accepted, about 0.19 at 15 degrees)");
        }

        [Test]
        public void Player_RunwayTakeoff_FirstPitchedTick_DoesNotTouchTheGround() {
            // T943: MissionRunner.PlaceOnGround rests a runway plane RunwayClearance (0.02) above
            // the ground. On the takeoff tick the nose already pitches ease*dt (1.2 degrees),
            // which lowers the rotated hitbox's tail corner by about 0.017 while the body has not
            // risen yet (MovePosition is applied in the physics step after every FixedUpdate;
            // EditMode never applies it, which is exactly that worst case). The terrain check
            // must not count it as a ground crash. A faster pitch.easeDegreesPerSecond (above
            // about 71) would make every runway takeoff an instant crash.
            var terrain = TerrainStrip.Create(null, 100f);
            _created.Add(terrain.gameObject);
            float ground = terrain.HeightAt(0f);
            var clearanceField = typeof(MissionRunner).GetField("RunwayClearance", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(clearanceField, "MissionRunner.RunwayClearance not found by reflection");
            float clearance = (float)clearanceField.GetValue(null);

            _plane = PlayerPlane.Create(new Vector3(0f, ground, 0f), true);
            InvokeAwake(_plane.GetComponent<Health>());
            var box = _plane.GetComponent<BoxCollider2D>();
            Assert.IsNotNull(box);
            // Same placement as MissionRunner.PlaceOnGround (level box: bounds.extents.y = size.y / 2).
            var start = new Vector2(0f, ground + box.size.y * 0.5f + clearance);
            _plane.transform.position = new Vector3(start.x, start.y, 0f);
            _plane.GetComponent<Rigidbody2D>().position = start;
            Physics2D.SyncTransforms();

            Invoke(_plane, "FixedUpdate");
            Assert.IsFalse(_plane.HasCrashed, "resting on the runway is not a crash");

            _plane.OnMove(new InputState { up = true });   // takeoff tick
            Assert.IsFalse(_plane.OnRunway, "W takes off");
            Assert.Greater(_plane.PitchAngle, 0f, "the nose starts to rise on the takeoff tick");

            Invoke(_plane, "FixedUpdate");                 // body still at the runway height
            Assert.Greater(_plane.HitboxLowestPoint().y, terrain.HeightAt(0f),
                $"pitched {_plane.PitchAngle} degrees on the runway, the tail corner must stay above the ground");
            Assert.AreEqual(0f, _plane.GetComponent<Health>().CurrentDamagePercent, 1e-4f);
            Assert.IsFalse(_plane.HasCrashed, "the first pitched tick of a takeoff must not crash the plane");
        }

        [Test]
        public void Player_Crash_TumblesFromItsCurrentAttitude() {
            MakePlane(Vector2.zero);
            Move(30, up: true);
            float body = _plane.BodyAngle;
            Assert.AreNotEqual(0f, body);
            _plane.ForceCrash();
            Assert.IsTrue(_plane.HasCrashed);
            var crashAngle = (float)typeof(PlayerPlane).GetField("_crashAngle", Private).GetValue(_plane);
            Assert.AreEqual(body, crashAngle, 1e-4f, "the wreck starts tumbling from the pitched attitude");
        }

        // ── Fighter ─────────────────────────────────────────────────────

        [Test]
        public void Fighter_Climbing_DrawnNoseUp() {
            // B059 (QA T966): with no player the chase keeps the fighter's heading at cruise speed
            // and adds no climb, so the 5 u/s climb comes only from _prevY below (EditMode:
            // MovePosition doesn't move the transform). The pitch then follows the climb.
            var f = MakeFighter(new Vector2(6f, 0f), null);
            f.Configure("straight", "");
            for (int i = 0; i < 30; i++) {
                SetField(f, "_prevY", f.transform.position.y - 0.1f); // 5 u/s climb
                Invoke(f, "FixedUpdate");
            }
            Assert.AreEqual(-1, f.FlightDirection, "no player: no turn, keeps flying left");
            Assert.IsFalse(f.IsTurning);
            Vector2 nose = FighterDrawnNose(f);
            Assert.Greater(f.PitchAngle, 0f);
            Assert.Less(nose.x, 0f);
            Assert.Greater(nose.y, 0f, "a climbing fighter noses up");
            Assert.Less(Vector2.Angle(nose, f.NoseDirection), 0.5f, "NoseDirection is the drawn nose");
        }

        [Test]
        public void Fighter_PlayerAbove_NoseUp_PlayerBelow_NoseDown() {
            // P1-1: the nose turns TOWARD the player, never mirrored about level.
            var above = MakeTarget(new Vector2(0f, 2f));
            var f = MakeFighter(new Vector2(6f, 0f), above);
            TrackPlayer(f, TicksToTurn(40f));
            Assert.Greater(f.PitchAngle, 0f);
            Assert.Greater(FighterDrawnNose(f).y, 0f, "player above: drawn nose up");

            var below = MakeTarget(new Vector2(0f, -2f));
            var g = MakeFighter(new Vector2(6f, 0f), below);
            TrackPlayer(g, TicksToTurn(40f));
            Assert.Less(g.PitchAngle, 0f);
            Assert.Less(FighterDrawnNose(g).y, 0f, "player below: drawn nose down");
        }

        [Test]
        public void Fighter_ShotUpward_NoseAgreesWithBullet() {
            // Player about 9.5 degrees up-left.
            var player = MakeTarget(new Vector2(0f, 1f));
            var f = MakeFighter(new Vector2(6f, 0f), player);
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            TrackPlayer(f, TicksToTurn(10f));
            Assert.IsTrue((bool)Invoke(f, "TickFire", fire.firstShotDelayMax + 0.01f));

            var shot = SingleActiveEnemyBullet();
            Vector2 v = shot.GetComponent<Rigidbody2D>().linearVelocity;
            Vector2 nose = FighterDrawnNose(f);
            Assert.Greater(nose.y, 0f, $"the drawn nose must point up with the shot (nose {nose}, shot {v})");
            Assert.LessOrEqual(Vector2.Angle(nose, v), fire.spreadDegrees + 0.05f,
                "bullet leaves along the nose, within the spread");
        }

        [Test]
        public void Fighter_ShotAtConeEdge_StillLeavesAlongTheNose() {
            // Player about 30 degrees up-left: inside the 40 degree fire cone and the 40 degree
            // fighter pitch (P1-2 decision: fighters pitch to aim, then fire along the nose).
            var player = MakeTarget(new Vector2(0f, 3.5f));
            var f = MakeFighter(new Vector2(6f, 0f), player);
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            TrackPlayer(f, TicksToTurn(40f));
            Assert.IsTrue((bool)Invoke(f, "TickFire", fire.firstShotDelayMax + 0.01f));

            var shot = SingleActiveEnemyBullet();
            Vector2 v = shot.GetComponent<Rigidbody2D>().linearVelocity;
            Vector2 nose = FighterDrawnNose(f);
            Assert.LessOrEqual(Vector2.Angle(nose, v), fire.spreadDegrees + 0.05f,
                $"GDD: bullets leave along the nose (nose {nose}, shot {v})");
            Vector2 muzzle = (Vector2)(shot.transform.position - f.transform.position);
            Assert.Less(Vector2.Angle(nose, muzzle), 0.5f, "the shot leaves from the nose");
        }

        [Test]
        public void Fighter_AimPitch_ClampedToFighterMax_AndEasedBackToFlightLimit() {
            var p = Pitch;
            // Bearing just inside the cone: the nose may go past pitch.maxDegrees, never past fighterMaxDegrees.
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            float bearing = Mathf.Min(fire.fighterConeDegrees, p.fighterMaxDegrees) - 1f;
            float r = bearing * Mathf.Deg2Rad;
            var player = MakeTarget(new Vector2(6f - 5f * Mathf.Cos(r), 5f * Mathf.Sin(r)));
            var f = MakeFighter(new Vector2(6f, 0f), player);
            for (int i = 0; i < 200; i++) {
                Invoke(f, "UpdatePitch", 0f, Dt);
                Assert.LessOrEqual(Mathf.Abs(f.PitchAngle), p.fighterMaxDegrees + 1e-3f);
            }
            Assert.AreEqual(bearing, f.PitchAngle, 0.05f, "the nose settles on the player");

            // Player gone: the nose returns to the level-flight attitude.
            player.GetComponent<Health>().ApplyDamage(1f, DamageSource.Collision);
            TrackPlayer(f, TicksToTurn(bearing));
            Assert.AreEqual(0f, f.PitchAngle, 1e-3f);
        }

        // ── Bomber ──────────────────────────────────────────────────────

        [Test]
        public void Bomber_Climbing_DrawnNoseUp() {
            var b = BomberAI.Spawn(new Vector3(6f, 0f, 0f));
            _created.Add(b.gameObject);
            InvokeAwake(b.GetComponent<Health>());
            InvokeAwake(b);
            b.GetComponent<SpriteRenderer>().flipX = true; // BomberAI.Start (FlipsToFlyLeft)
            SetField(b, "_speed", 1.5f);                    // BomberAI.Start

            // An escort target above makes FollowEscortTarget climb (the only path that moves y).
            var lead = new GameObject("T942_lead");
            _created.Add(lead);
            lead.transform.position = new Vector3(6f, 3f, 0f);
            SetField(b, "_escortTarget", lead.transform);

            for (int i = 0; i < 60; i++) Invoke(b, "FixedUpdate");
            Assert.AreEqual(-1, BomberAI.Facing, "the bomber flies left");
            Assert.Greater(b.PitchAngle, 0f);
            Vector2 nose = DrawnNose(b.BodyAngle, b.GetComponent<SpriteRenderer>());
            Assert.Less(nose.x, 0f);
            Assert.Greater(nose.y, 0f, $"a climbing bomber noses up (nose {nose})");
            Assert.Less(Vector2.Angle(nose, b.NoseDirection), 0.5f);
        }
    }
}
