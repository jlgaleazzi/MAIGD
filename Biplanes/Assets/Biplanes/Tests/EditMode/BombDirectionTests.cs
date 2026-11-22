using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// B057: "bombs fall to the right even when the plane faces left". A bomb starts with the
    /// plane's world velocity (BombBallistics.InheritedVelocity), but its forward x along Facing
    /// is never less than the cruise speed (scrollSpeed). Right after a turn the camera eases
    /// (turnEaseSeconds) and the plane slides toward the new trailing anchor at player.speed,
    /// opposite to its facing, so its world x still points backwards for about a second; the
    /// rule makes a bomb dropped then still leave the way the plane faces.
    /// </summary>
    public class BombDirectionTests {

        private const float G = 10f;
        private const float Drag = 0.15f;
        private const float Dt = 0.02f;
        private const float Cruise = 2.6f;      // mission scrollSpeed (2 x 1.3)
        private const float Slide = 6.5f;       // player.speed: anchor slide relative to the camera
        private const float Eps = 1e-4f;

        private static Vector2 FlyToGround(Vector2 start, Vector2 vel, float groundY, out float time) {
            Vector2 pos = start;
            time = 0f;
            for (int i = 0; i < 100000 && pos.y > groundY; i++) {
                (pos, vel) = BombBallistics.Step(pos, vel, Dt, G, Drag);
                time += Dt;
            }
            return pos;
        }

        [TearDown]
        public void TearDown() {
            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (p != null) Object.DestroyImmediate(p.gameObject);
            }
        }

        [Test]
        public void FacingRight_LevelFlight_BombGoesRight() {
            var v = BombBallistics.InheritedVelocity(new Vector2(Cruise, 0f), 1, Cruise, 1f);
            Assert.Greater(v.x, 0f);
            Assert.AreEqual(Cruise, v.x, Eps);
            Assert.AreEqual(0f, v.y, Eps);
        }

        [Test]
        public void FacingLeft_LevelFlight_BombGoesLeft_AndLandsLeftOfDrop() {
            var v = BombBallistics.InheritedVelocity(new Vector2(-Cruise, 0f), -1, Cruise, 1f);
            Assert.Less(v.x, 0f);
            Vector2 drop = new Vector2(10f, 4f);
            Vector2 land = FlyToGround(drop, v, 0f, out _);
            Assert.Less(land.x, drop.x - 1f, "a bomb dropped facing left lands well left of the drop x");
        }

        [Test]
        public void SameSpeed_SameThrowDistance_BothWays() {
            Vector2 drop = new Vector2(0f, 4f);
            var vr = BombBallistics.InheritedVelocity(new Vector2(Cruise, 0f), 1, Cruise, 1f);
            var vl = BombBallistics.InheritedVelocity(new Vector2(-Cruise, 0f), -1, Cruise, 1f);
            Vector2 lr = FlyToGround(drop, vr, 0f, out float tr);
            Vector2 ll = FlyToGround(drop, vl, 0f, out float tl);
            Assert.Greater(lr.x, 0f);
            Assert.Less(ll.x, 0f);
            Assert.AreEqual(lr.x, -ll.x, Eps, "mirror-image throws");
            Assert.AreEqual(tr, tl, Eps);
        }

        [Test]
        public void DivingLeft_LandsLeft_AndSoonerThanLevel() {
            Vector2 drop = new Vector2(0f, 4f);
            var level = BombBallistics.InheritedVelocity(new Vector2(-Cruise, 0f), -1, Cruise, 1f);
            var dive = BombBallistics.InheritedVelocity(new Vector2(-Cruise, -Slide), -1, Cruise, 1f);
            Assert.AreEqual(-Slide, dive.y, Eps, "the dive speed is inherited");
            Vector2 levelLand = FlyToGround(drop, level, 0f, out float tLevel);
            Vector2 diveLand = FlyToGround(drop, dive, 0f, out float tDive);
            Assert.Less(diveLand.x, drop.x, "diving left lands left of the drop");
            Assert.Less(tDive, tLevel, "a dive lands sooner");
            Assert.Greater(diveLand.x, levelLand.x, "and shorter than a level throw");
        }

        [Test]
        public void TurnLeft_DuringCameraEaseAndSlide_EveryBombStillGoesLeft() {
            // Mirror ScrollController (linear ease +Cruise -> -Cruise over 0.6 s) and PlayerPlane
            // (slide right at player.speed for the 0.3 -> 0.7 anchor swap, 0.4 x 17.8 = 7.1 u).
            float cam = Cruise, ease = 0.6f, slideLeft = 7.12f;
            bool sawBackwardWorldX = false;
            for (int i = 0; i < 100; i++) {
                float accel = 2f * Mathf.Max(Cruise, Mathf.Abs(cam)) / ease;
                cam = Mathf.MoveTowards(cam, -Cruise, accel * Dt);
                float slide = Mathf.Min(Slide * Dt, slideLeft);
                slideLeft -= slide;
                float worldVx = cam + slide / Dt;
                if (worldVx > 0f) sawBackwardWorldX = true;

                var v = BombBallistics.InheritedVelocity(new Vector2(worldVx, 0f), -1, Cruise, 1f);
                Assert.Less(v.x, 0f, $"tick {i}: world vx {worldVx:F2}, a bomb facing left must go left");
                Assert.LessOrEqual(v.x, -Cruise + Eps, $"tick {i}: at least the cruise throw");
            }
            Assert.IsTrue(sawBackwardWorldX, "the scenario reproduces the bug: the plane's world x points right");
            Assert.AreEqual(0f, slideLeft, 1e-3f, "the slide finished within the simulated 2 s");
        }

        [Test]
        public void ForwardWorldSpeedAboveCruise_IsKept() {
            var v = BombBallistics.InheritedVelocity(new Vector2(-5f, 1f), -1, Cruise, 1f);
            Assert.AreEqual(-5f, v.x, Eps);
            Assert.AreEqual(1f, v.y, Eps);
        }

        [Test]
        public void InheritFactor_ScalesBothAxes_NegativeIsZero() {
            var v = BombBallistics.InheritedVelocity(new Vector2(-Cruise, -2f), -1, Cruise, 0.5f);
            Assert.AreEqual(-Cruise * 0.5f, v.x, Eps);
            Assert.AreEqual(-1f, v.y, Eps);
            Assert.AreEqual(Vector2.zero, BombBallistics.InheritedVelocity(new Vector2(3f, 3f), 1, Cruise, -1f));
        }

        [Test]
        public void NoScroll_FacingLeft_BackwardWorldX_DropsStraightNotRight() {
            // ScrollController missing (cruise 0): the sign rule alone still holds.
            var v = BombBallistics.InheritedVelocity(new Vector2(4f, 0f), -1, 0f, 1f);
            Assert.AreEqual(0f, v.x, Eps);
        }

        [Test]
        public void BombSpawnedFacingLeft_PointsLeft_FinsUpright() {
            Bomb.InitPool(() => {
                var go = new GameObject("B057_Bomb");
                go.AddComponent<SpriteRenderer>();
                go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                go.AddComponent<CircleCollider2D>().isTrigger = true;
                return go.AddComponent<Bomb>();
            }, 1);
            var bomb = Bomb.Spawn(new Vector3(0f, 4f, 0f), new Vector2(-Cruise, 0f));
            Assert.IsNotNull(bomb);
            const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(Bomb).GetMethod("Awake", Private).Invoke(bomb, null);
            typeof(Bomb).GetMethod("ApplyRotation", Private).Invoke(bomb, new object[] { true });

            Assert.Less(bomb.Velocity.x, 0f);
            Assert.IsTrue(bomb.GetComponent<SpriteRenderer>().flipY, "travelling left: flipped so fins stay upright");
            float z = bomb.transform.eulerAngles.z;
            Assert.AreEqual(180f, z, 0.5f, "nose along the path (pointing left)");
        }
    }
}
