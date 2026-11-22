using UnityEngine;

namespace Biplanes.Weapons {

    /// <summary>
    /// Pure ballistic maths for the player's bombs (B036, ARCHITECTURE §3 "Bomb flight").
    /// A bomb starts with the plane's velocity and then falls under constant gravity, with a
    /// light horizontal drag, so it flies a parabola ahead of (or behind) the plane. No Unity
    /// state is touched, so EditMode tests can drive it directly.
    /// </summary>
    public static class BombBallistics {

        /// <summary>Angle (degrees) used for a bomb with no velocity: nose straight down.</summary>
        public const float RestAngleDegrees = -90f;

        /// <summary>
        /// One semi-implicit Euler step: v.y -= gravity*dt, v.x *= max(0, 1 - drag*dt), then
        /// pos += v*dt (the new velocity moves the bomb, which keeps the arc stable at any frame
        /// rate). gravity is a positive acceleration in units/s^2, drag a fraction of the
        /// horizontal speed lost per second. A non-positive dt returns the inputs unchanged.
        /// </summary>
        public static (Vector2 pos, Vector2 vel) Step(Vector2 pos, Vector2 vel, float dt, float gravity, float drag) {
            if (dt <= 0f) return (pos, vel);
            vel.y -= gravity * dt;
            vel.x *= Mathf.Max(0f, 1f - drag * dt);
            pos += vel * dt;
            return (pos, vel);
        }

        /// <summary>
        /// Initial velocity of a bomb released by a plane (B057, ARCHITECTURE §3 "Bomb flight").
        /// y is the plane's world climb/dive speed. x is the plane's world x velocity, but its
        /// forward component (along `facing`) is never less than `cruiseSpeed`, the plane's forward
        /// airspeed (the mission scrollSpeed): during the camera turn ease and the post-turn slide
        /// toward the trailing screen anchor the plane's world x can still point backwards (that
        /// slide is a camera artefact, always opposite to the facing), and a bomb must leave in the
        /// direction the plane faces. In steady flight world x equals facing * cruiseSpeed, so the
        /// throw is the same both ways. The result is scaled by `inheritFactor` (negative = 0).
        /// A facing of 0 counts as +1.
        /// </summary>
        public static Vector2 InheritedVelocity(Vector2 planeVelocity, int facing, float cruiseSpeed,
                                                float inheritFactor) {
            float f = facing < 0 ? -1f : 1f;
            float forward = Mathf.Max(planeVelocity.x * f, Mathf.Max(0f, cruiseSpeed));
            return new Vector2(f * forward, planeVelocity.y) * Mathf.Max(0f, inheritFactor);
        }

        /// <summary>
        /// Z-rotation (degrees) that points a sprite drawn nose-along-+x along `vel`:
        /// atan2(v.y, v.x). A bomb travelling left gets an angle near ±180 and turns downward
        /// as it falls. A (near) zero velocity returns RestAngleDegrees (nose down).
        /// </summary>
        public static float AngleDegrees(Vector2 vel) {
            if (vel.sqrMagnitude < 1e-8f) return RestAngleDegrees;
            return Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg;
        }
    }
}
