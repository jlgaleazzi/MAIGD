using System.Collections.Generic;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Fx;
using Biplanes.Missions;

namespace Biplanes.Enemies {

    /// <summary>
    /// The falling wreck of a shot-down bomber (B068). Visual only.
    ///
    /// BomberAI makes one with Create the physics tick after its Health reaches 0. By then the
    /// kill is already counted (Health raised EntityDestroyed at the hit). The wreck is
    /// a separate scene-root object with a SpriteRenderer and nothing else: no collider, no
    /// Rigidbody2D, no Health, no TeamTag. So it can't be hit, never damages the player, is
    /// not in MissionRunner's EnemyAircraft list (EnemyPressure, minimap), and no escort or
    /// destroyById target refers to it. The bomber object itself is removed at once.
    ///
    /// Each frame (scaled time, so pause and the result screen freeze it) the wreck keeps
    /// drifting forward at bomberCrash.driftFactor of the bomber's speed, falls under
    /// bomberCrash.gravity, tips its nose down at diveDegreesPerSecond to maxDiveDegrees and
    /// trails heavy pencil smoke from the engine. When it reaches the terrain
    /// (TerrainStrip.HeightAt, no collider) or after maxFallSeconds, it plays the ground
    /// burst, groundBurstCount explosions spread along the wreck, and removes itself.
    ///
    /// Cleanup: MissionRunner's teardown calls ClearAll, and a scene reload (Retry, menu)
    /// destroys wrecks with the scene.
    /// </summary>
    public class BomberWreck : MonoBehaviour {

        /// <summary>The wreck rests this far above the terrain height when it lands (about
        /// half the bomber's fuselage depth, so the burst sits on the ground).</summary>
        public const float RestHeight = 0.3f;

        private static readonly List<BomberWreck> s_live = new List<BomberWreck>();

        private BomberCrashData _crash;
        private Vector2 _velocity;
        private float _pitch;
        private int _facing;
        private float _elapsed;
        private float _smokeTimer;
        private bool _landed;

        /// <summary>Wrecks that are still falling.</summary>
        public static int LiveCount {
            get {
                for (int i = s_live.Count - 1; i >= 0; i--) {
                    if (s_live[i] == null) s_live.RemoveAt(i);
                }
                return s_live.Count;
            }
        }

        /// <summary>Current velocity in world units per second.</summary>
        public Vector2 Velocity => _velocity;

        /// <summary>Current nose pitch in degrees (nose up positive; negative = diving).</summary>
        public float PitchAngle => _pitch;

        /// <summary>True once it has reached the ground (it removes itself the same step).</summary>
        public bool Landed => _landed;

        /// <summary>
        /// Builds a falling wreck at `position` with the bomber's `sprite`, mirror and pitch.
        /// `velocity` is the bomber's flight velocity: the wreck keeps driftFactor of its x
        /// speed and any dive it already had. Never null.
        /// </summary>
        public static BomberWreck Create(Vector3 position, Sprite sprite, bool flipX, int sortingOrder,
                                         float pitchDegrees, int facing, Vector2 velocity) {
            var go = new GameObject("bomberWreck");
            go.transform.position = position;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.flipX = flipX;
            sr.sortingOrder = sortingOrder;

            var wreck = go.AddComponent<BomberWreck>();
            wreck.Init(pitchDegrees, facing, velocity);
            return wreck;
        }

        /// <summary>Removes every live wreck (MissionRunner teardown). EditMode-safe.</summary>
        public static void ClearAll() {
            for (int i = s_live.Count - 1; i >= 0; i--) {
                var w = s_live[i];
                if (w != null) DestroyGo(w.gameObject);
            }
            s_live.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() {
            s_live.Clear();
        }

        private void Init(float pitchDegrees, int facing, Vector2 velocity) {
            _crash = BomberCrashData.Resolve(GameBalance.Get());
            _facing = facing < 0 ? -1 : 1;
            _pitch = pitchDegrees;
            _velocity = new Vector2(velocity.x * _crash.driftFactor, Mathf.Min(velocity.y, 0f));
            _elapsed = 0f;
            _smokeTimer = 0f;
            _landed = false;
            transform.rotation = Quaternion.Euler(0f, 0f, PlanePitch.BodyAngle(_pitch, _facing));
            if (!s_live.Contains(this)) s_live.Add(this);
        }

        void Update() {
            Step(Time.deltaTime);
        }

        void OnDestroy() {
            s_live.Remove(this);
        }

        /// <summary>
        /// Advances the fall by `dt` seconds: drift, gravity, nose-down tilt and smoke. On the
        /// ground (or past maxFallSeconds) it plays the ground burst and removes itself.
        /// Returns true on the step it lands. A dt of 0 (paused) does nothing.
        /// </summary>
        internal bool Step(float dt) {
            if (_landed || dt <= 0f) return false;
            if (_crash == null) Init(_pitch, _facing, _velocity);

            _elapsed += dt;
            _velocity.y -= _crash.gravity * dt;
            Vector3 pos = transform.position + (Vector3)(_velocity * dt);
            _pitch = Mathf.Max(-_crash.maxDiveDegrees, _pitch - _crash.diveDegreesPerSecond * dt);

            var terrain = TerrainStrip.Instance;
            float ground = terrain != null ? terrain.HeightAt(pos.x) : TerrainStrip.GroundBaseY;
            bool hitGround = pos.y <= ground + RestHeight;
            if (hitGround) pos.y = ground + RestHeight;

            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 0f, PlanePitch.BodyAngle(_pitch, _facing)));

            if (hitGround || _elapsed >= _crash.maxFallSeconds) {
                Land(pos);
                return true;
            }

            EmitSmoke(dt);
            return false;
        }

        /// <summary>Heavy smoke from the engine all the way down (BomberAI's heaviest level).</summary>
        private void EmitSmoke(float dt) {
            _smokeTimer -= dt;
            if (_smokeTimer > 0f) return;
            _smokeTimer = BomberAI.SmokeInterval(BomberAI.SmokeLevelHeavy);
            var fx = FxManager.GetOrNull();
            if (fx == null) return;
            fx.SpawnEnemySmokePuff(BomberAI.EngineSmokePoint(transform.position, _pitch, _facing));
        }

        /// <summary>The ground burst (bigger than the mid-air one: several explosions along
        /// the wreck, one sound) and removal.</summary>
        private void Land(Vector3 pos) {
            _landed = true;
            var fx = FxManager.GetOrNull();
            if (fx != null) {
                int n = Mathf.Max(1, _crash.groundBurstCount);
                for (int i = 0; i < n; i++) {
                    fx.TriggerExplosion(pos + (Vector3)GroundBurstOffset(i, n, _crash.groundBurstSpread), i == 0);
                }
            }
            s_live.Remove(this);
            DestroyGo(gameObject);
        }

        /// <summary>
        /// Offset of explosion `index` of a `count`-explosion ground burst: the first at the
        /// centre, the rest alternating left/right out to `spread`, each a little higher, so
        /// the burst covers the wreck's length.
        /// </summary>
        public static Vector2 GroundBurstOffset(int index, int count, float spread) {
            if (index <= 0 || count <= 1) return Vector2.zero;
            int ring = (index + 1) / 2;                       // 1,1,2,2,...
            int rings = count / 2;                            // outermost ring
            float side = (index % 2 == 1) ? -1f : 1f;
            float x = side * spread * ring / Mathf.Max(1, rings);
            return new Vector2(x, 0.15f * ring);
        }

        private static void DestroyGo(GameObject go) {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
