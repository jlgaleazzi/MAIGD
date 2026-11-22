using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// B036: GDD "Flight attitude (pitch)" (bombs fall ballistically) and ARCHITECTURE §3
    /// "Bomb flight". The player's bomb starts with the plane's velocity, then each physics
    /// tick BombBallistics.Step applies v.y -= g*dt, v.x *= (1 - drag*dt), pos += v*dt
    /// (semi-implicit Euler). Its sprite turns along atan2(v.y, v.x). Balance `bomb` block
    /// defaults: gravity 10, drag 0.15, inheritFactor 1, alignToVelocity true.
    /// </summary>
    public class BombBallisticsTests {

        private const float G = 10f;
        private const float Drag = 0.15f;
        private const float Eps = 1e-4f;

        /// <summary>Steps from `start` with `vel` until y falls to `groundY`; returns the
        /// landing position and the flight time.</summary>
        private static Vector2 FlyToGround(Vector2 start, Vector2 vel, float groundY, float dt,
                                           float drag, out float time) {
            Vector2 pos = start;
            time = 0f;
            for (int i = 0; i < 100000 && pos.y > groundY; i++) {
                (pos, vel) = BombBallistics.Step(pos, vel, dt, G, drag);
                time += dt;
            }
            return pos;
        }

        [Test]
        public void ZeroVelocityDrop_FallsStraight_AndAccelerates() {
            Vector2 pos = new Vector2(3f, 5f);
            Vector2 vel = Vector2.zero;
            float lastDrop = 0f;
            for (int i = 0; i < 20; i++) {
                Vector2 before = pos;
                (pos, vel) = BombBallistics.Step(pos, vel, 0.02f, G, Drag);
                float drop = before.y - pos.y;
                Assert.AreEqual(3f, pos.x, Eps, "no sideways drift without horizontal speed");
                Assert.Greater(drop, lastDrop, $"step {i}: each step falls further than the last");
                lastDrop = drop;
            }
            Assert.AreEqual(-G * 0.4f, vel.y, Eps, "v.y = -g*t after 0.4 s");
        }

        [Test]
        public void ForwardDrop_LandsAheadByAboutVxTimesT() {
            const float vx = 5f;
            Vector2 land = FlyToGround(new Vector2(0f, 5f), new Vector2(vx, 0f), 0f, 0.005f, 0f, out float t);
            Assert.AreEqual(1f, t, 0.02f, "falls 5 units in about sqrt(2h/g) = 1 s");
            Assert.AreEqual(vx * t, land.x, 0.05f, "no drag: lands vx*t ahead");

            Vector2 dragged = FlyToGround(new Vector2(0f, 5f), new Vector2(vx, 0f), 0f, 0.005f, Drag, out float td);
            Assert.Less(dragged.x, vx * td, "drag shortens the throw");
            Assert.Greater(dragged.x, 0.9f * vx * td, "light drag: still lands well ahead (parabola)");
        }

        [Test]
        public void LeftwardDrop_LandsBehindTheReleasePoint() {
            Vector2 land = FlyToGround(new Vector2(0f, 5f), new Vector2(-4f, 0f), 0f, 0.005f, Drag, out _);
            Assert.Less(land.x, -3f, "a plane facing left throws its bomb to the left");
        }

        [Test]
        public void DivingDrop_LandsSoonerThanLevelDrop() {
            FlyToGround(new Vector2(0f, 5f), new Vector2(5f, 0f), 0f, 0.005f, Drag, out float level);
            FlyToGround(new Vector2(0f, 5f), new Vector2(5f, -3f), 0f, 0.005f, Drag, out float dive);
            FlyToGround(new Vector2(0f, 5f), new Vector2(5f, 3f), 0f, 0.005f, Drag, out float climb);
            Assert.Less(dive, level, "a diving plane's bomb hits the ground first");
            Assert.Greater(climb, level, "a climbing plane's bomb rises first and lands last");
        }

        [Test]
        public void Drag_OnlySlowsHorizontalSpeed() {
            var (_, noGravity) = BombBallistics.Step(Vector2.zero, new Vector2(6f, 2f), 0.1f, 0f, 0.5f);
            Assert.AreEqual(6f * (1f - 0.05f), noGravity.x, Eps, "x loses drag*dt of its speed");
            Assert.AreEqual(2f, noGravity.y, Eps, "drag never touches y");

            var (_, withDrag) = BombBallistics.Step(Vector2.zero, new Vector2(6f, 2f), 0.1f, G, 0.5f);
            var (_, noDrag) = BombBallistics.Step(Vector2.zero, new Vector2(6f, 2f), 0.1f, G, 0f);
            Assert.AreEqual(noDrag.y, withDrag.y, Eps, "vertical speed is the same with or without drag");
            Assert.AreEqual(6f, noDrag.x, Eps, "no drag: x speed kept");

            var (_, huge) = BombBallistics.Step(Vector2.zero, new Vector2(6f, 0f), 0.1f, G, 50f);
            Assert.AreEqual(0f, huge.x, Eps, "drag*dt > 1 clamps to a stop, never reverses");
        }

        [Test]
        public void Step_IsFrameRateIndependent_WithinTolerance() {
            Vector2 start = new Vector2(0f, 10f);
            Vector2 v0 = new Vector2(5f, 2f);

            // 10 small steps vs 1 large step over 0.1 s.
            Vector2 pSmall = start, vSmall = v0;
            for (int i = 0; i < 10; i++) (pSmall, vSmall) = BombBallistics.Step(pSmall, vSmall, 0.01f, G, Drag);
            var (pLarge, vLarge) = BombBallistics.Step(start, v0, 0.1f, G, Drag);
            Assert.AreEqual(vSmall.y, vLarge.y, Eps, "vertical speed after 0.1 s is exact either way");
            Assert.AreEqual(vSmall.x, vLarge.x, 0.01f, "horizontal speed after 0.1 s");
            Assert.AreEqual(pSmall.x, pLarge.x, 0.01f, "x after 0.1 s");
            Assert.AreEqual(pSmall.y, pLarge.y, 0.06f, "y after 0.1 s");

            // A full second at 100 Hz vs 10 Hz stays within 10% of the 5-unit gravity drop.
            pSmall = start; vSmall = v0;
            Vector2 pBig = start, vBig = v0;
            for (int i = 0; i < 100; i++) (pSmall, vSmall) = BombBallistics.Step(pSmall, vSmall, 0.01f, G, Drag);
            for (int i = 0; i < 10; i++) (pBig, vBig) = BombBallistics.Step(pBig, vBig, 0.1f, G, Drag);
            Assert.AreEqual(vSmall.y, vBig.y, Eps, "v.y after 1 s");
            Assert.AreEqual(pSmall.x, pBig.x, 0.05f, "x after 1 s");
            Assert.AreEqual(pSmall.y, pBig.y, 0.5f, "y after 1 s");
        }

        [Test]
        public void Step_NonPositiveDt_ReturnsInputsUnchanged() {
            var (p, v) = BombBallistics.Step(new Vector2(1f, 2f), new Vector2(3f, 4f), 0f, G, Drag);
            Assert.AreEqual(new Vector2(1f, 2f), p);
            Assert.AreEqual(new Vector2(3f, 4f), v);
        }

        [Test]
        public void AngleDegrees_PointsNoseAlongVelocity() {
            Assert.AreEqual(0f, BombBallistics.AngleDegrees(new Vector2(4f, 0f)), Eps, "level, right");
            Assert.AreEqual(180f, Mathf.Abs(BombBallistics.AngleDegrees(new Vector2(-4f, 0f))), Eps, "level, left");
            Assert.AreEqual(-90f, BombBallistics.AngleDegrees(new Vector2(0f, -3f)), Eps, "straight down");
            Assert.AreEqual(-45f, BombBallistics.AngleDegrees(new Vector2(2f, -2f)), Eps, "down-right");
            Assert.AreEqual(-135f, BombBallistics.AngleDegrees(new Vector2(-2f, -2f)), Eps, "down-left");
            Assert.AreEqual(BombBallistics.RestAngleDegrees, BombBallistics.AngleDegrees(Vector2.zero), Eps,
                "no velocity: nose down");
            Assert.AreEqual(-90f, BombBallistics.RestAngleDegrees, Eps);
        }

        [Test]
        public void AngleDegrees_LeftDrop_TurnsDownwardAsItFalls() {
            Vector2 pos = Vector2.zero, vel = new Vector2(-5f, 0f);
            float lastAbs = 180f;
            for (int i = 0; i < 50; i++) {
                (pos, vel) = BombBallistics.Step(pos, vel, 0.02f, G, Drag);
                float a = BombBallistics.AngleDegrees(vel);
                Assert.Less(a, -90f, $"step {i}: nose stays on the left-and-down side");
                Assert.Greater(a, -180f, $"step {i}");
                Assert.Less(Mathf.Abs(a), lastAbs + Eps, $"step {i}: the nose keeps turning down");
                lastAbs = Mathf.Abs(a);
            }
        }

        [Test]
        public void BombData_Resolve_FallsBackToDefaults() {
            var d = BombData.Resolve(null);
            Assert.AreEqual(10f, d.gravity, Eps);
            Assert.AreEqual(0.15f, d.drag, Eps);
            Assert.AreEqual(1f, d.inheritFactor, Eps);
            Assert.IsTrue(d.alignToVelocity);

            var b = new GameBalance();
            b.bomb = new BombData { gravity = 0f, drag = -1f, inheritFactor = -2f, alignToVelocity = false };
            var r = BombData.Resolve(b);
            Assert.AreEqual(BombData.DefaultGravity, r.gravity, Eps, "non-positive gravity -> default");
            Assert.AreEqual(BombData.DefaultDrag, r.drag, Eps, "negative drag -> default");
            Assert.AreEqual(BombData.DefaultInheritFactor, r.inheritFactor, Eps, "negative inheritFactor -> default");
            Assert.IsFalse(r.alignToVelocity, "alignToVelocity is read as given");

            b.bomb = new BombData { gravity = 12f, drag = 0f, inheritFactor = 0.5f };
            r = BombData.Resolve(b);
            Assert.AreEqual(12f, r.gravity, Eps);
            Assert.AreEqual(0f, r.drag, Eps, "zero drag is valid");
            Assert.AreEqual(0.5f, r.inheritFactor, Eps);
        }

        [Test]
        public void BalanceJson_HasBombBlock_AndAaaHealth125() {
            var b = GameBalance.Load();
            Assume.That(b != null, "Resources/Balance.json not loadable");
            Assert.IsNotNull(b.bomb, "Balance.json has a bomb block");
            var r = BombData.Resolve(b);
            Assert.AreEqual(10f, r.gravity, Eps);
            Assert.AreEqual(0.15f, r.drag, Eps);
            Assert.AreEqual(1f, r.inheritFactor, Eps);
            Assert.IsTrue(r.alignToVelocity);
            Assert.AreEqual(125f, b.enemyHealth.aaa, Eps, "AAA takes 5 player bullets of 25");
        }
    }
}
