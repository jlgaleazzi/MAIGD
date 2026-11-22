using System.Collections;
using UnityEngine;
using Biplanes.Audio;
using Biplanes.Core;
using Biplanes.Fx;
using Biplanes.Missions;

namespace Biplanes.Weapons {

    /// <summary>
    /// Gravity-affected bomb dropped by the player for ground attacks.
    /// Starts with the throwing plane's full velocity (x and y) and falls ballistically
    /// (BombBallistics.Step, Balance `bomb` block, B036), its nose turned along its path,
    /// and deals high damage (bombDamage from Balance.json) on impact.
    /// The kinematic body is moved with MovePosition in FixedUpdate, so Rigidbody2D gravity
    /// never applies and linearVelocity stays zero; the ballistic velocity lives in Velocity.
    /// </summary>
    public class Bomb : Projectile {

        /// <summary>Shared pool instance — initialised once by BombBay at mission start.</summary>
        private static ObjectPool<Bomb> _pool;

        // --- ballistic tunables (Balance `bomb` block via BombData.Resolve, read in Start) ---

        private float _gravity = BombData.DefaultGravity;
        private float _drag = BombData.DefaultDrag;
        private bool _alignToVelocity = BombData.DefaultAlignToVelocity;

        /// <summary>Current ballistic velocity (world units/s), integrated in FixedUpdate.</summary>
        private Vector2 _vel;

        /// <summary>Ballistic position, integrated in FixedUpdate and handed to MovePosition.
        /// Kept here rather than read back from the body, so a pooled bomb never starts from
        /// a stale (not yet synced) Rigidbody2D.position.</summary>
        private Vector2 _pos;

        /// <summary>Current ballistic velocity in world units per second.</summary>
        public Vector2 Velocity => _vel;

        /// <summary>Cached in Awake; turned along the path (flipY while travelling left).</summary>
        private SpriteRenderer _sr;

        /// <summary>Radius of the ground burst that damages enemies near the impact point (B008).
        /// Fast bombs can step past a thin ground trigger between physics ticks, so the burst
        /// guarantees that a bomb landing on a ground target (AAA) counts.</summary>
        [SerializeField] private float burstRadius = 0.5f;

        /// <summary>Shared non-allocating buffer for burst overlap queries.</summary>
        private static readonly Collider2D[] BurstHits = new Collider2D[16];

        /// <summary>Damageables already hit by the current burst (a target with several colliders
        /// is damaged once).</summary>
        private static readonly System.Collections.Generic.List<IDamageable> BurstDamaged =
            new System.Collections.Generic.List<IDamageable>(16);

        /// <summary>Target this bomb already damaged through its trigger, skipped by the burst.
        /// Cleared on every Spawn.</summary>
        private IDamageable _directHit;

        /// <summary>Position at the end of the previous Update (reset in Spawn); the ground
        /// burst is placed where the segment from here crosses the terrain (B008).</summary>
        private Vector2 _prevPos;

        /// <summary>Initialises the shared bomb pool with `count` entries created via `factory`.
        /// Call once during mission setup so Bomb.Spawn can pop immediately.
        /// </summary>
        public static void InitPool(System.Func<Bomb> factory, int count = 10) {
            // Free the previous mission's pool (root and every item) before replacing it,
            // so Next/Retry don't leak pooled objects (B006a).
            _pool?.Dispose();
            _pool = new ObjectPool<Bomb>(factory, count);
        }

        /// <summary>Spawn a bomb from the shared pool at `position`, inheriting
        /// the parent plane's velocity so it arcs forward naturally.
        /// </summary>
        /// <param name="position">World position to spawn (typically player plane centre).</param>
        /// <param name="parentVelocity">The throwing plane's velocity (PlayerPlane.Velocity x
        /// Balance bomb.inheritFactor), x and y.</param>
        public static Bomb Spawn(Vector3 position, Vector2 parentVelocity) {
            if (_pool == null) {
                Debug.LogError("Bomb pool not initialised — call Bomb.InitPool first.");
                return null;
            }

            var bomb = _pool.Pop();
            if (bomb == null) return null;
            bomb._directHit = null;

            // Use a custom Init overload that accepts an explicit initial velocity
            // rather than direction * speed, so we can pass the full parent momentum.
            bomb.Init(position, Quaternion.identity, parentVelocity, _pool);
            // Init sets linearVelocity = direction * speed; the bomb is moved by FixedUpdate
            // (MovePosition) from its own ballistic velocity instead, so the body's velocity is
            // zeroed to keep physics from moving it twice (ARCHITECTURE §3 "Bomb flight").
            if (bomb._rb != null) {
                bomb._rb.linearVelocity = Vector2.zero;
                bomb._rb.gravityScale = 0f;
            }
            bomb._vel = parentVelocity;
            bomb._pos = position;
            if (bomb._rb != null) bomb._rb.position = position;
            bomb._prevPos = position;
            bomb.ApplyRotation(true);
            return bomb;
        }

        private void Awake() {
            // Player projectiles belong to Team.Player (to skip own-team hits).
            team = Team.Player;
            _sr = GetComponent<SpriteRenderer>();
        }

        private void Start() {
            // Override any inspector values with balance.json tunables at runtime.
            var g = GameBalance.Get();
            if (g != null && g.player != null) {
                damage = g.player.bombDamage; // default 100 from Balance.json
            }
            var b = BombData.Resolve(g);
            _gravity = b.gravity;
            _drag = b.drag;
            _alignToVelocity = b.alignToVelocity;
            ApplyRotation(true);
        }

        /// <summary>Ballistic flight (B036): one BombBallistics.Step per physics tick, moved with
        /// MovePosition, then the nose is turned along the new velocity.</summary>
        private void FixedUpdate() {
            if (_rb == null) return;
            (_pos, _vel) = BombBallistics.Step(_pos, _vel, Time.fixedDeltaTime, _gravity, _drag);
            _rb.MovePosition(_pos);
            ApplyRotation(false);
        }

        /// <summary>Turns the sprite's nose (+x) along the velocity: z = atan2(v.y, v.x). While the
        /// bomb travels left the sprite is flipped on y so its fins stay upright (mirrored, not
        /// upside down). `immediate` sets the transform (spawn); otherwise the body's MoveRotation
        /// is used so the turn lands with the physics move.</summary>
        private void ApplyRotation(bool immediate) {
            if (!_alignToVelocity) return;
            float angle = BombBallistics.AngleDegrees(_vel);
            if (_sr != null) _sr.flipY = _vel.x < 0f;
            if (immediate || _rb == null) {
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
                if (_rb != null) _rb.rotation = angle;
            } else {
                _rb.MoveRotation(angle);
            }
        }

        private void Update() {
            if (_rb == null) return;

            // Terrain has no collider (ARCHITECTURE §3 Physics): burst on reaching the ground.
            var terrain = TerrainStrip.Instance;
            Vector2 pos = transform.position;
            if (terrain != null && pos.y <= terrain.HeightAt(pos.x)) {
                // Burst on the surface where the bomb crossed it, not at its below-ground
                // position, so a fast fall or a low frame rate can't sink the burst (B008).
                Vector2 p = ImpactPoint(terrain, _prevPos, pos);
                BurstDamage(p);
                // Ground impact: the BombImpact thump replaces the generic explosion cue (T950).
                FxManager.Instance.TriggerExplosion(p, false);
                var audio = AudioManager.GetOrNull();
                if (audio != null) audio.Play(SfxId.BombImpact, (Vector3)p);
                Recycle();
                return;
            }
            _prevPos = pos;
        }

        /// <summary>Point on the terrain surface where the segment prev → cur crossed it.
        /// Uses the linear crossing of (y - HeightAt(x)) between the two positions; if prev was
        /// not above ground, the surface point under cur is used.</summary>
        private static Vector2 ImpactPoint(TerrainStrip terrain, Vector2 prev, Vector2 cur) {
            float above = prev.y - terrain.HeightAt(prev.x);   // > 0 while prev is above ground
            float below = cur.y - terrain.HeightAt(cur.x);     // <= 0 at/under the ground
            float x = cur.x;
            if (above > 0f && above - below > 1e-6f) {
                x = Mathf.Lerp(prev.x, cur.x, above / (above - below));
            }
            return new Vector2(x, terrain.HeightAt(x));
        }

        /// <summary>On collision: apply damage to an enemy, explode, then recycle.
        /// Neutral objects (health-balloon pickups) are flown through untouched (B011).
        /// </summary>
        protected override void OnHit(IDamageable target) {
            if (target == null) {
                Recycle();
                return;
            }

            // A pickup is not a target: don't spend the bomb on it (B011).
            if (target.Team == Team.Neutral) return;

            // Apply damage — Bomb source deals full damage per GDD / Balance.
            _directHit = target;
            target.ApplyDamage(damage, DamageSource.Bomb);

            // VFX hook: explosion effect on a direct hit (T950). When this hit killed the target,
            // the victim's own death explosion owns the Explosion cue, so this one is silent
            // and a kill plays exactly one cue (B033 / T953 B4).
            FxManager.Instance.TriggerExplosion(transform.position, !target.IsDead);

            Recycle();
        }

        /// <summary>Apply this bomb's damage once to every live Team.Enemy IDamageable whose
        /// collider overlaps a circle of burstRadius around `point`. The player and Neutral
        /// objects (health-balloon pickups) are never touched; a target already hit through the
        /// trigger, or reached through several colliders, is damaged only once.</summary>
        private void BurstDamage(Vector2 point) {
            var filter = new ContactFilter2D();
            filter.useTriggers = true;   // every hittable collider in the game is a trigger

            int count = Physics2D.OverlapCircle(point, burstRadius, filter, BurstHits);
            BurstDamaged.Clear();
            for (int i = 0; i < count; i++) {
                var col = BurstHits[i];
                BurstHits[i] = null;
                if (col == null) continue;

                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || target.Team != Team.Enemy || target.IsDead) continue;
                if (ReferenceEquals(target, _directHit) || BurstDamaged.Contains(target)) continue;

                BurstDamaged.Add(target);
                target.ApplyDamage(damage, DamageSource.Bomb);
            }
            BurstDamaged.Clear();
        }
    }
}
