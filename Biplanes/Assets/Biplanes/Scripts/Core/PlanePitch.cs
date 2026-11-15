using UnityEngine;

namespace Biplanes.Core {
    /// <summary>
    /// Pitch helpers shared by the player, fighters and bombers (GDD "Flight attitude (pitch)").
    ///
    /// Conventions:
    ///  - Pitch is in degrees, nose up positive, whatever way the plane faces.
    ///  - facing is +1 (flying right, art as drawn) or -1 (flying left, sprite flipX).
    ///  - BodyAngle(pitch, facing) is the z rotation the Rigidbody2D is turned to with
    ///    MoveRotation: pitch * facing, so a climbing plane's drawn nose points up both ways.
    ///  - NoseDirection(pitch, facing) is the world unit vector of the drawn nose:
    ///    (facing * cos p, sin p). Machine-gun bullets leave along it.
    ///
    /// Every default here is a fallback for a missing or non-positive Balance pitch key; read
    /// the live values through PitchData.Resolve(GameBalance.Get()).
    /// </summary>
    public static class PlanePitch {
        /// <summary>Fallback for pitch.maxDegrees: the flight-attitude limit for every plane.</summary>
        public const float DefaultMaxDegrees = 15f;

        /// <summary>Fallback for pitch.fighterMaxDegrees: how far a fighter may pitch to aim its gun.</summary>
        public const float DefaultFighterMaxDegrees = 40f;

        /// <summary>Fallback for pitch.easeDegreesPerSecond: how fast any nose turns.</summary>
        public const float DefaultEaseDegreesPerSecond = 60f;

        /// <summary>Fallback for pitch.fullPitchSpeed: vertical speed (world units/s) at which
        /// the flight attitude reaches maxDegrees. Slower climbs pitch proportionally less.</summary>
        public const float DefaultFullPitchSpeed = 3f;

        /// <summary>Pitch limits stay below this, so a plane never stands on its tail.</summary>
        public const float AbsoluteMaxDegrees = 89f;

        /// <summary>
        /// Flight-attitude target for a vertical speed: vy / fullPitchSpeed * maxDegrees,
        /// clamped to ±maxDegrees. Climbing gives +, diving gives -.
        /// </summary>
        public static float TargetFromVerticalSpeed(float verticalVelocity, float maxDegrees, float fullPitchSpeed) {
            if (maxDegrees <= 0f) return 0f;
            float full = fullPitchSpeed > 0f ? fullPitchSpeed : DefaultFullPitchSpeed;
            return Mathf.Clamp(verticalVelocity / full * maxDegrees, -maxDegrees, maxDegrees);
        }

        /// <summary>
        /// Eases `currentAngle` toward `targetAngle` by at most easeDegreesPerSecond * dt, and
        /// keeps both inside ±limitDegrees.
        /// </summary>
        public static float EaseToward(float currentAngle, float targetAngle, float limitDegrees,
                                       float easeDegreesPerSecond, float dt) {
            float limit = Mathf.Max(0f, limitDegrees);
            float target = Mathf.Clamp(targetAngle, -limit, limit);
            if (dt <= 0f) return Mathf.Clamp(currentAngle, -limit, limit);
            float rate = easeDegreesPerSecond > 0f ? easeDegreesPerSecond : DefaultEaseDegreesPerSecond;
            float next = Mathf.MoveTowards(currentAngle, target, rate * dt);
            return Mathf.Clamp(next, -limit, limit);
        }

        /// <summary>
        /// One tick of flight attitude: eases toward TargetFromVerticalSpeed. Returns the new
        /// pitch in degrees, never beyond ±maxDegrees.
        /// </summary>
        public static float Step(float currentAngle, float verticalVelocity, float maxDegrees,
                                 float easeDegreesPerSecond, float fullPitchSpeed, float dt) {
            float target = TargetFromVerticalSpeed(verticalVelocity, maxDegrees, fullPitchSpeed);
            return EaseToward(currentAngle, target, maxDegrees, easeDegreesPerSecond, dt);
        }

        /// <summary>Step with the default ease rate and full-pitch speed (pure helper for tests
        /// and callers without a Balance file).</summary>
        public static float Step(float currentAngle, float verticalVelocity, float maxDegrees, float dt) {
            return Step(currentAngle, verticalVelocity, maxDegrees,
                DefaultEaseDegreesPerSecond, DefaultFullPitchSpeed, dt);
        }

        /// <summary>Step using the resolved Balance pitch block.</summary>
        public static float Step(float currentAngle, float verticalVelocity, PitchData pitch, float dt) {
            if (pitch == null) return Step(currentAngle, verticalVelocity, DefaultMaxDegrees, dt);
            return Step(currentAngle, verticalVelocity, pitch.maxDegrees,
                pitch.easeDegreesPerSecond, pitch.fullPitchSpeed, dt);
        }

        /// <summary>The z rotation (degrees) of a plane body with this pitch and facing.</summary>
        public static float BodyAngle(float pitchDegrees, int facing) {
            return facing < 0 ? -pitchDegrees : pitchDegrees;
        }

        /// <summary>World unit vector of the drawn nose for this pitch and facing.</summary>
        public static Vector2 NoseDirection(float pitchDegrees, int facing) {
            float r = pitchDegrees * Mathf.Deg2Rad;
            return new Vector2((facing < 0 ? -1f : 1f) * Mathf.Cos(r), Mathf.Sin(r));
        }

        /// <summary>
        /// The pitch (nose up positive) that points a plane's nose along `direction`, whichever
        /// way the plane faces (the climb angle of the direction, -90..90).
        /// </summary>
        public static float PitchToward(Vector2 direction) {
            if (direction.sqrMagnitude < 1e-8f) return 0f;
            float ahead = Mathf.Abs(direction.x);
            return Mathf.Atan2(direction.y, ahead) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Lowest world y of a box hitbox (size `size`, local `offset`) on a body at `center`
        /// turned by `bodyAngleDegrees`: the corner of the rotated box nearest the ground.
        /// </summary>
        public static float LowestPointOfBox(Vector2 center, Vector2 size, Vector2 offset, float bodyAngleDegrees) {
            float r = bodyAngleDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r);
            float s = Mathf.Sin(r);
            float offY = offset.x * s + offset.y * c;
            float halfExtentY = 0.5f * (Mathf.Abs(size.x * s) + Mathf.Abs(size.y * c));
            return center.y + offY - halfExtentY;
        }
    }
}
