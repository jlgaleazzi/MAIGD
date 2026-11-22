using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Player;

namespace Biplanes.Tests {

    /// <summary>
    /// QA T967: re-verification of the T966 P1 (a fighter overtaken by the player clipped it)
    /// after B061, with QA's own scenarios on top of FighterOvertakeTests: the player turning
    /// back onto a fighter that trails it at its standoff (eased camera reversal and anchor
    /// slide like ScrollController/PlayerPlane, and an instant reversal as the worst case), and
    /// head-on passes with the player near the floor or the ceiling of the fighter's flight
    /// band (camera 10 u tall at y 0). Each tick checks the pitched hitboxes (OBB vs OBB) at
    /// player body angles 0 and ±15 degrees, and that the fighter never flies tail-first.
    /// GDD Enemies item 1, ARCHITECTURE §3 "Fighter chase" (B061).
    ///
    /// EditMode runs no Awake/FixedUpdate: they are invoked by reflection, each ChaseStep
    /// result is written back to the transform and UpdatePitch is ticked with the measured
    /// climb, like FixedUpdate does.
    /// </summary>
    public class FighterPassQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const float Dt = 0.02f;

        private static readonly Vector2 FighterHalf = new Vector2(0.75f, 0.5f);
        private static readonly Vector2 PlayerHalf = new Vector2(0.8f, 0.4f);
        private static readonly float[] PlayerAngles = { -15f, 0f, 15f };

        /// <summary>Camera half-width for a 16:9 view 10 u tall.</summary>
        private const float HalfW = 8.8889f;

        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp() {
            FighterAttackSlots.Shared.Reset();
        }

        [TearDown]
        public void TearDown() {
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

        private FighterAI MakeFighter(Vector2 position, PlayerPlane player, int dir, float velX, float standoff,
                float offsetY, Camera cam) {
            var f = FighterAI.Spawn(position);
            _created.Add(f.gameObject);
            Invoke(f.GetComponent<Health>(), "Awake");
            Invoke(f, "Awake");
            var prop = typeof(FighterAI).GetProperty("PlayerRef", Private);
            Assert.IsNotNull(prop, "FighterAI.PlayerRef not found by reflection");
            prop.SetValue(f, player);
            f.SetFlightDirection(dir);
            SetField(f, "_velX", velX);
            SetField(f, "_standoff", standoff);
            SetField(f, "_offsetY", offsetY);
            if (cam != null) SetField(f, "_cam", cam);
            return f;
        }

        private Camera MakeCamera() {
            var go = new GameObject("qa_camera");
            _created.Add(go);
            go.transform.position = new Vector3(0f, 0f, -10f);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            return cam;
        }

        private static Vector3 Tick(FighterAI f) {
            float y0 = f.transform.position.y;
            var next = (Vector3)Invoke(f, "ChaseStep", f.transform.position, Dt);
            f.transform.position = next;
            Invoke(f, "UpdatePitch", (next.y - y0) / Dt, Dt);
            return next;
        }

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

        /// <summary>Per-tick checks: no pitched contact, no tail-first flight outside a turn.
        /// Returns the first failure or null.</summary>
        private static string Check(FighterAI f, Vector2 p, Vector2 pp, string at) {
            var fh = new Vector2(FighterHalf.x * Mathf.Abs(f.transform.localScale.x), FighterHalf.y);
            foreach (float pa in PlayerAngles) {
                if (ObbOverlap(p, fh, f.BodyAngle, pp, PlayerHalf, pa)) {
                    return $"pitched hitboxes touch (fighter {f.BodyAngle:0.0}°, player {pa}°), {at} dx {pp.x - p.x:0.00} dy {p.y - pp.y:0.00}";
                }
            }
            if (!f.IsTurning && f.FlightDirection * f.WorldVelocityX < FighterAI.MinForwardSpeed - 1e-3f) {
                return $"tail-first (facing {f.FlightDirection}, vx {f.WorldVelocityX:0.000}), {at}";
            }
            return null;
        }

        // ── Turn back onto a trailing fighter ───────────────────────────

        [Test]
        public void PlayerTurnsBackOntoATrailingFighter_EasedOrInstant_PassesWithoutPitchedContact() {
            var g = GameBalance.Get();
            var chase = FighterChaseData.Resolve(g);
            float anchor = g.player.screenAnchor > 0f ? g.player.screenAnchor : 0.3f;
            float ease = g.player.turnEaseSeconds > 0f ? g.player.turnEaseSeconds : 0.6f;
            float slide = g.player.speed;
            foreach (bool instant in new[] { false, true }) {
                foreach (float cruise in new[] { 2.6f, 3.25f, 3.9f }) {
                    foreach (float offset in new[] { 0f, 0.5f, -0.5f, 1f, -1f }) {
                        foreach (float standoff in new[] { chase.standoff, chase.standoffMax }) {
                            // Player flying right at its trailing anchor, the fighter trailing at its
                            // standoff and the cruise; at t = 0 the player turns left.
                            float camX = 0f, camV = cruise;
                            float screenX = (anchor - 0.5f) * 2f * HalfW;
                            float targetScreenX = (1f - anchor - 0.5f) * 2f * HalfW;
                            var player = MakePlayer(new Vector2(camX + screenX, 0f), new Vector2(cruise, 0f));
                            var f = MakeFighter(new Vector2(camX + screenX - standoff, offset), player, 1, cruise,
                                standoff, offset, null);
                            string label = $"{(instant ? "instant" : "eased")} cruise {cruise}, offset {offset}, standoff {standoff}";
                            string failure = null;
                            for (int i = 0; i < 2000 && failure == null; i++) {
                                // ScrollController: linear reversal over turnEaseSeconds; PlayerPlane:
                                // the screen x slides to the other anchor at player.speed.
                                float accel = 2f * Mathf.Max(cruise, Mathf.Abs(camV)) / ease;
                                camV = instant ? -cruise : Mathf.MoveTowards(camV, -cruise, accel * Dt);
                                camX += camV * Dt;
                                screenX = instant ? targetScreenX : Mathf.MoveTowards(screenX, targetScreenX, slide * Dt);
                                Vector3 prev = player.transform.position;
                                player.transform.position = new Vector3(camX + screenX, 0f, 0f);
                                SetField(player, "_velocity", (Vector2)(player.transform.position - prev) / Dt);
                                Vector3 p = Tick(f);
                                failure = Check(f, p, player.transform.position, $"{label}, tick {i}");
                            }
                            Assert.IsNull(failure, failure);
                            Assert.AreEqual(-1, f.FlightDirection, $"{label}: passed, turned and chases the player");
                            if (cruise < 3f) {
                                Assert.AreEqual(standoff, f.transform.position.x - player.transform.position.x, 0.15f,
                                    $"{label}: holds its standoff behind the player");
                            }
                        }
                    }
                }
            }
        }

        // ── Head-on pass at the band edges ──────────────────────────────

        [Test]
        public void HeadOnPass_PlayerNearTheBandFloorOrCeiling_ClearsWithoutPitchedContact() {
            var chase = FighterChaseData.Resolve(GameBalance.Get());
            var cam = MakeCamera();
            // Band (no terrain): GroundBaseY + 1.25 = -3.25 up to 5 - ChaseTopMargin = 4. The
            // player may fly from just above the ground up to 4.5 (camera minus the 0.5 margin).
            foreach (float py in new[] { -3.8f, -3.25f, 2.6f, 3.0f, 3.5f, 4.0f, 4.5f }) {
                foreach (float cruise in new[] { 2.6f, 3.25f, 3.9f, -3.9f }) {
                    foreach (float offset in new[] { 0f, 1f, -1f }) {
                        foreach (float standoff in new[] { chase.standoff, chase.standoffMax }) {
                            int pdir = cruise > 0f ? 1 : -1;
                            var player = MakePlayer(new Vector2(0f, py), new Vector2(cruise, 0f));
                            float y0 = Mathf.Clamp(py + offset, -3.25f, 4f);
                            var f = MakeFighter(new Vector2(pdir * 13.5f, y0), player, -pdir,
                                -pdir * GameBalance.Get().enemySpeed.fighter, standoff, offset, cam);
                            string label = $"player y {py}, cruise {cruise}, offset {offset}, standoff {standoff}";
                            string failure = null;
                            bool passed = false;
                            for (int i = 0; i < 2000 && failure == null; i++) {
                                player.transform.position += new Vector3(cruise * Dt, 0f, 0f);
                                Vector3 p = Tick(f);
                                failure = Check(f, p, player.transform.position, $"{label}, tick {i}");
                                if (pdir * (player.transform.position.x - p.x) > 2f) passed = true;
                            }
                            Assert.IsNull(failure, failure);
                            Assert.IsTrue(passed, $"{label}: the fighter was passed");
                            Assert.AreEqual(pdir, f.FlightDirection, $"{label}: turned to chase");
                        }
                    }
                }
            }
        }
    }
}
