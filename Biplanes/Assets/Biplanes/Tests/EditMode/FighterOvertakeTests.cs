using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Player;

namespace Biplanes.Tests {

    /// <summary>
    /// B061 (after QA T966), GDD Enemies item 1, ARCHITECTURE §3 "Fighter chase": a fighter
    /// never flies tail-first (velocity along its facing ≥ FighterAI.MinForwardSpeed outside a
    /// turn), a head-on pass clears the player by about AvoidClearance thanks to the avoid-box
    /// hysteresis (checked with the pitched hitboxes, OBB vs OBB), a fighter ahead of a slower
    /// player (M1/M5 cruise 2.6) passes it and ends up chasing from behind, and escorts hold
    /// their own formation slots.
    ///
    /// EditMode runs no Awake/OnEnable/FixedUpdate: they are invoked by reflection, each
    /// ChaseStep result is written back to the transform and UpdatePitch is ticked with the
    /// measured climb, like FixedUpdate does.
    /// </summary>
    public class FighterOvertakeTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const float Dt = 0.02f;
        private const float Eps = 1e-3f;

        /// <summary>Half sizes of the hitboxes: fighter 1.5 x 1 (FighterAI.Spawn, x scaled by the
        /// turn squash), player about 1.6 x 0.8 (collider auto-sized to the 160 x 80 px sprite).</summary>
        private static readonly Vector2 FighterHalf = new Vector2(0.75f, 0.5f);
        private static readonly Vector2 PlayerHalf = new Vector2(0.8f, 0.4f);
        /// <summary>Player body angles checked: level and pitched to pitch.maxDegrees (15).</summary>
        private static readonly float[] PlayerAngles = { -15f, 0f, 15f };

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<FighterAI> _enabled = new List<FighterAI>();

        [SetUp]
        public void SetUp() {
            FighterAttackSlots.Shared.Reset();
        }

        [TearDown]
        public void TearDown() {
            foreach (var f in _enabled) {
                if (f != null) Invoke(f, "OnDisable");
            }
            _enabled.Clear();
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
            FighterAttackSlots.Shared.Reset();
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static object Invoke(object target, string method, params object[] args) {
            var m = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found by reflection");
            return m.Invoke(target, args);
        }

        private static void SetField(object target, string field, object value) {
            var f = target.GetType().GetField(field, Private);
            Assert.IsNotNull(f, $"{target.GetType().Name}.{field} not found by reflection");
            f.SetValue(target, value);
        }

        private PlayerPlane MakePlayer(Vector2 position, Vector2 velocity) {
            var go = new GameObject("player");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<TeamTag>().team = Team.Player;
            Invoke(go.AddComponent<Health>(), "Awake");
            var p = go.AddComponent<PlayerPlane>();
            SetField(p, "_velocity", velocity);
            return p;
        }

        private FighterAI MakeFighter(Vector2 position, PlayerPlane player) {
            var f = FighterAI.Spawn(position);
            _created.Add(f.gameObject);
            Invoke(f.GetComponent<Health>(), "Awake");
            Invoke(f, "Awake");
            var prop = typeof(FighterAI).GetProperty("PlayerRef", Private);
            Assert.IsNotNull(prop, "FighterAI.PlayerRef not found by reflection");
            prop.SetValue(f, player);
            return f;
        }

        /// <summary>One FixedUpdate-like tick: ChaseStep, written back, then the pitch from the
        /// measured climb.</summary>
        private static Vector3 Tick(FighterAI f) {
            float y0 = f.transform.position.y;
            var next = (Vector3)Invoke(f, "ChaseStep", f.transform.position, Dt);
            f.transform.position = next;
            Invoke(f, "UpdatePitch", (next.y - y0) / Dt, Dt);
            return next;
        }

        /// <summary>Separating-axis test of two oriented boxes (centre, half size, z angle).</summary>
        private static bool ObbOverlap(Vector2 ca, Vector2 ha, float angA, Vector2 cb, Vector2 hb, float angB) {
            float ra = angA * Mathf.Deg2Rad, rb = angB * Mathf.Deg2Rad;
            Vector2 ua = new Vector2(Mathf.Cos(ra), Mathf.Sin(ra)), va = new Vector2(-ua.y, ua.x);
            Vector2 ub = new Vector2(Mathf.Cos(rb), Mathf.Sin(rb)), vb = new Vector2(-ub.y, ub.x);
            Vector2 d = cb - ca;
            foreach (var axis in new[] { ua, va, ub, vb }) {
                float projA = ha.x * Mathf.Abs(Vector2.Dot(axis, ua)) + ha.y * Mathf.Abs(Vector2.Dot(axis, va));
                float projB = hb.x * Mathf.Abs(Vector2.Dot(axis, ub)) + hb.y * Mathf.Abs(Vector2.Dot(axis, vb));
                if (Mathf.Abs(Vector2.Dot(d, axis)) > projA + projB) return false;
            }
            return true;
        }

        private struct PassResult {
            public float MinDyNear;   // min |dy| while |dx| < 1.55
            public float PassedAt;    // seconds until it is behind the player, facing its way, turn done (-1 never)
            public string Failure;    // first failed check, null = none
        }

        /// <summary>
        /// The player flies level at `playerVx`; the fighter starts `gap` ahead of it, facing it
        /// (FlightDirection = -sign(playerVx)), with height offset `offsetY` and standoff
        /// `standoff`. Checks every tick: no OBB contact at any player pitch, the forward floor
        /// outside turns; records the closest |dy| near the player and when it passed.
        /// </summary>
        private PassResult RunHeadOn(float playerVx, float offsetY, float standoff, float gap, float seconds,
                out FighterAI fighter, out PlayerPlane player) {
            int pdir = playerVx >= 0f ? 1 : -1;
            player = MakePlayer(new Vector2(0f, 0f), new Vector2(playerVx, 0f));
            var f = MakeFighter(new Vector2(pdir * gap, 0f), player);
            f.SetFlightDirection(-pdir);
            SetField(f, "_offsetY", offsetY);
            SetField(f, "_standoff", standoff);
            fighter = f;

            var r = new PassResult { MinDyNear = float.MaxValue, PassedAt = -1f };
            int ticks = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < ticks; i++) {
                player.transform.position += new Vector3(playerVx * Dt, 0f, 0f);
                Vector3 p = Tick(f);
                Vector2 pp = player.transform.position;
                float dx = pp.x - p.x, dy = p.y - pp.y;
                string at = $"vx {playerVx}, offset {offsetY}, standoff {standoff}, tick {i} (dx {dx:0.00}, dy {dy:0.00})";

                if (Mathf.Abs(dx) < 1.55f) r.MinDyNear = Mathf.Min(r.MinDyNear, Mathf.Abs(dy));
                var fh = new Vector2(FighterHalf.x * Mathf.Abs(f.transform.localScale.x), FighterHalf.y);
                foreach (float pa in PlayerAngles) {
                    if (r.Failure == null && ObbOverlap(p, fh, f.BodyAngle, pp, PlayerHalf, pa)) {
                        r.Failure = $"pitched hitboxes touch (fighter body {f.BodyAngle:0.0}°, player {pa}°), {at}";
                    }
                }
                if (r.Failure == null && !f.IsTurning && f.FlightDirection * f.WorldVelocityX < FighterAI.MinForwardSpeed - Eps) {
                    r.Failure = $"tail-first: facing {f.FlightDirection}, vx {f.WorldVelocityX:0.000}, {at}";
                }
                if (r.PassedAt < 0f && pdir * dx > 0f && f.FlightDirection == pdir && !f.IsTurning) {
                    r.PassedAt = (i + 1) * Dt;
                }
            }
            return r;
        }

        // ── Pure helpers ────────────────────────────────────────────────

        [Test]
        public void HorizontalStep_WithForwardFloor_NeverBacksOff_BrakingBehindStillWorks() {
            // Ahead of a player flying at it (M1 2.6): the old step backed off at +2.6 facing left.
            float v = -3f;
            for (int i = 0; i < 500; i++) {
                v = FighterAI.HorizontalVelocityStep(13.5f, v, 10f, 2.6f, -1, 3.5f, 3f, 4f, Dt, FighterAI.MinForwardSpeed);
                Assert.LessOrEqual(v, 0f, $"tick {i}: never reverses while facing left");
            }
            Assert.AreEqual(-FighterAI.MinForwardSpeed, v, Eps, "slows to the forward floor, toward the player");
            Assert.AreEqual(2.6f, FighterAI.HorizontalVelocityStep(13.5f, 2.6f, 10f, 2.6f, -1, 3.5f, 3f, 4f, Dt),
                0.1f, "the floor-free B059 overload is unchanged (it would back off)");

            // Chasing from behind a slower player: still brakes to hold the standoff.
            float x = 0f, px = 10f;
            v = 3f;
            for (int i = 0; i < 1500; i++) {
                v = FighterAI.HorizontalVelocityStep(x, v, px, 2.6f, 1, 3.5f, 3f, 4f, Dt, FighterAI.MinForwardSpeed);
                x += v * Dt;
                px += 2.6f * Dt;
                Assert.GreaterOrEqual(v, FighterAI.MinForwardSpeed - Eps, $"tick {i}: forward floor");
            }
            Assert.AreEqual(3.5f, px - x, 0.1f, "holds the standoff behind");
            Assert.AreEqual(2.6f, v, 0.05f, "matches the player");
        }

        [Test]
        public void AvoidBox_WidensWithClosingSpeed_AndHoldsUntilDxLeaves() {
            Assert.AreEqual(FighterAI.AvoidBoxFraction * 3.5f, FighterAI.AvoidHalfWidth(3.5f, -2f), Eps,
                "separating: the plain box");
            Assert.AreEqual(FighterAI.AvoidBoxFraction * 3.5f + FighterAI.AvoidLeadSeconds * 4f,
                FighterAI.AvoidHalfWidth(3.5f, 4f), Eps, "closing: wider");
            Assert.AreEqual(3.5f, FighterAI.ClosingSpeed(5f, 1f, -2.5f), Eps, "player right, flying at each other");
            Assert.AreEqual(-0.4f, FighterAI.ClosingSpeed(-5f, 3f, 2.6f), Eps, "player left, fighter faster away");

            Assert.IsTrue(FighterAI.AvoidHold(false, 2f, 1.2f, 3f), "enters below AvoidClearance (not only below 1)");
            Assert.IsFalse(FighterAI.AvoidHold(false, 2f, 1.6f, 3f), "well above: clear");
            Assert.IsFalse(FighterAI.AvoidHold(false, 3.1f, 0f, 3f), "outside the box in x");
            Assert.IsTrue(FighterAI.AvoidHold(true, 1f, 1.5f, 3f), "at the clearance height: keeps holding it");
            Assert.IsTrue(FighterAI.AvoidHold(true, 3f + FighterAI.AvoidExitMargin - 0.01f, 0f, 3f), "exit margin");
            Assert.IsFalse(FighterAI.AvoidHold(true, 3f + FighterAI.AvoidExitMargin, 1.5f, 3f), "|dx| left the box");

            Assert.AreEqual(-FighterAI.AvoidClearance, FighterAI.AvoidTargetY(0f, 1, -10f, 1.2f), Eps,
                "only 1.2 above under the ceiling: full clearance below instead");
        }

        [Test]
        public void FormationOffset_DistinctSlots_ClearOfTheEscortedPlane() {
            var slots = new List<Vector2>();
            for (int i = 0; i < 6; i++) slots.Add(FighterAI.FormationOffset(i, 1, 0.64f));
            for (int i = 0; i < slots.Count; i++) {
                Assert.Greater(slots[i].x, 0f, $"slot {i}: behind a plane flying left");
                Assert.GreaterOrEqual(Mathf.Abs(slots[i].y), 0.64f + 0.5f, $"slot {i}: clear of the plane's hitbox");
                for (int j = i + 1; j < slots.Count; j++) {
                    Vector2 d = slots[i] - slots[j];
                    Assert.IsTrue(Mathf.Abs(d.x) >= 1.5f || Mathf.Abs(d.y) >= 1f,
                        $"slots {i} and {j} don't overlap ({slots[i]} vs {slots[j]})");
                }
            }
            Assert.Less(FighterAI.FormationOffset(0, -1, 0.64f).x, 0f, "behind a plane flying right");
        }

        // ── Head-on pass (P1, QA T966) ──────────────────────────────────

        [Test]
        public void FasterPlayer_OvertakesAFighterAhead_ClearsByAvoidClearance_NoPitchedContact() {
            var c = FighterChaseData.Resolve(GameBalance.Get());
            foreach (float cruise in new[] { 3.25f, 3.9f, -3.25f, -3.9f }) {
                foreach (float offset in new[] { 0f, 0.5f, -0.5f, 1f, -1f }) {
                    foreach (float standoff in new[] { c.standoff, c.standoffMax }) {
                        var r = RunHeadOn(cruise, offset, standoff, 13.5f, 20f, out var f, out _);
                        Assert.IsNull(r.Failure, r.Failure);
                        Assert.Less(r.MinDyNear, float.MaxValue, $"cruise {cruise}, offset {offset}: it passed the player");
                        Assert.GreaterOrEqual(r.MinDyNear, 1.3f,
                            $"cruise {cruise}, offset {offset}, standoff {standoff}: clearance while |dx| < 1.55");
                        Assert.Greater(r.PassedAt, 0f, $"cruise {cruise}, offset {offset}: turned to chase after the pass");
                        Assert.AreEqual(cruise > 0f ? 1 : -1, f.FlightDirection, "faces the player's way");
                    }
                }
            }
        }

        // ── No tail-first flight (P2, QA T966) ──────────────────────────

        [Test]
        public void SlowerPlayer_FighterAhead_FliesAtIt_Passes_ThenChasesFromBehind() {
            // M1/M5 cruise 2.6 < fighter 3: before B061 the fighter parked ahead, tail-first.
            var c = FighterChaseData.Resolve(GameBalance.Get());
            float fighter = GameBalance.Get().enemySpeed.fighter;
            foreach (float cruise in new[] { 2.6f, -2.6f }) {
                foreach (float offset in new[] { 0f, 1f, -0.5f }) {
                    foreach (float standoff in new[] { c.standoff, c.standoffMax }) {
                        var r = RunHeadOn(cruise, offset, standoff, 13.5f, 40f, out var f, out var player);
                        string at = $"cruise {cruise}, offset {offset}, standoff {standoff}";
                        Assert.IsNull(r.Failure, r.Failure);
                        Assert.GreaterOrEqual(r.MinDyNear, 1.3f, $"{at}: clearance on the pass");
                        Assert.Greater(r.PassedAt, 0f, $"{at}: passed the player");
                        Assert.Less(r.PassedAt, 10f, $"{at}: within a bounded time");

                        int pdir = cruise > 0f ? 1 : -1;
                        float gap = pdir * (player.transform.position.x - f.transform.position.x);
                        Assert.AreEqual(pdir, f.FlightDirection, $"{at}: faces the player's way");
                        Assert.AreEqual(standoff, gap, 0.15f, $"{at}: chases at its standoff behind the player");
                        Assert.AreEqual(cruise, f.WorldVelocityX, 0.05f, $"{at}: matches the cruise, nose first");
                        Assert.Less(Mathf.Abs(f.WorldVelocityX), fighter + Eps);
                        Assert.IsFalse(f.IsAvoiding, $"{at}: out of the avoid box at its standoff");
                    }
                }
            }
        }

        [Test]
        public void StaticPattern_StartsAtTheForwardFloor_NotAtRest() {
            var player = MakePlayer(new Vector2(-20f, 0f), Vector2.zero);
            var f = MakeFighter(Vector2.zero, player);
            f.Configure("static", "");
            Assert.AreEqual(-FighterAI.MinForwardSpeed, f.WorldVelocityX, Eps);
        }

        // ── Escort formation (P2, QA T966) ──────────────────────────────

        [Test]
        public void TwoEscortsOfOneBomber_HoldDistinctStableSlots_ClearOfIt() {
            var target = new GameObject("bomber");
            _created.Add(target);
            target.transform.position = new Vector3(20f, 1f, 0f);
            var fuselage = target.AddComponent<BoxCollider2D>();
            fuselage.size = new Vector2(2.64f, 0.40f);
            fuselage.offset = new Vector2(0.12f, -0.08f);
            var wing = target.AddComponent<BoxCollider2D>();
            wing.size = new Vector2(0.70f, 1.02f);
            wing.offset = new Vector2(0.69f, 0.13f); // half-height 0.64
            var player = MakePlayer(new Vector2(-60f, 1f), new Vector2(2.6f, 0f)); // beyond the leash

            var escorts = new[] { MakeFighter(new Vector2(20f, 1f), player), MakeFighter(new Vector2(20f, 1f), player) };
            foreach (var f in escorts) {
                f.Configure("escort", "bomber_1");
                SetField(f, "_escortTarget", target.transform);
                Invoke(f, "OnEnable");
                _enabled.Add(f);
            }

            for (int i = 0; i < 300; i++) {
                target.transform.position += new Vector3(-1f * Dt, 0f, 0f); // bomber flies left at 1 u/s
                foreach (var f in escorts) Tick(f);
            }

            Assert.AreEqual(0, escorts[0].EscortSlot);
            Assert.AreEqual(1, escorts[1].EscortSlot);
            Vector2 tp = target.transform.position;
            for (int k = 0; k < 2; k++) {
                var f = escorts[k];
                Assert.IsTrue(f.IsEscorting && !f.IsEscortChasing, $"escort {k}: in formation");
                Vector2 expected = tp + FighterAI.FormationOffset(k, 1, 0.64f);
                Assert.AreEqual(expected.x, f.transform.position.x, 0.05f, $"escort {k}: slot x (behind)");
                Assert.AreEqual(expected.y, f.transform.position.y, 0.05f, $"escort {k}: slot y");
                Assert.GreaterOrEqual(Mathf.Abs(f.transform.position.y - tp.y), 0.64f + 0.5f, $"escort {k}: clear of the bomber");
                Assert.AreEqual(-1, f.FlightDirection, $"escort {k}: faces the way it travels");
                Assert.GreaterOrEqual(f.FlightDirection * f.WorldVelocityX, FighterAI.MinForwardSpeed, $"escort {k}: nose first");
            }
            Vector2 d = (Vector2)(escorts[0].transform.position - escorts[1].transform.position);
            Assert.IsTrue(Mathf.Abs(d.x) >= 1.5f || Mathf.Abs(d.y) >= 1f, $"the escorts don't share a position ({d})");

            // Stable: more ticks keep the same slots.
            for (int i = 0; i < 100; i++) {
                target.transform.position += new Vector3(-1f * Dt, 0f, 0f);
                foreach (var f in escorts) Tick(f);
            }
            Assert.AreEqual(0, escorts[0].EscortSlot);
            Assert.AreEqual(1, escorts[1].EscortSlot);
        }
    }
}
