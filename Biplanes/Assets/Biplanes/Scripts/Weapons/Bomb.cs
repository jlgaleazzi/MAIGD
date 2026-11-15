using System.Collections;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Fx;
using Biplanes.Missions;

namespace Biplanes.Weapons {

    /// <summary>
    /// Gravity-affected bomb dropped by the player for ground attacks.
    /// Inherits forward momentum from the throwing plane, falls in an arc,
    /// and deals high damage (bombDamage from Balance.json) on impact.
    /// </summary>
    public class Bomb : Projectile {

        /// <summary>Shared pool instance — initialised once by BombBay at mission start.</summary>
        private static ObjectPool<Bomb> _pool;

        // --- serialisable tunables (persisted in editor, overridden by Balance.json) ---

        [SerializeField] private float gravityMultiplier = 12f;

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
        /// <param name="parentVelocity">Rigidbody2D.linearVelocity of the throwing plane.</param>
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
            // Init sets velocity = direction * speed; a bomb must start with exactly the
            // parent's velocity (ARCHITECTURE §3), then gravity in Update takes over.
            if (bomb._rb != null) bomb._rb.linearVelocity = parentVelocity;
            bomb._prevPos = position;
            return bomb;
        }

        private void Awake() {
            // Player projectiles belong to Team.Player (to skip own-team hits).
            team = Team.Player;
        }

        private void Start() {
            // Override any inspector values with balance.json tunables at runtime.
            var g = GameBalance.Get();
            if (g != null) {
                damage = g.player.bombDamage; // default 100 from Balance.json
            }
        }

        private void Update() {
            if (_rb == null) return;

            // Add gravity to vertical velocity each frame so the bomb drops in an arc.
            float gravityY = Physics2D.gravity.y * gravityMultiplier * Time.deltaTime;
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x,
                                             _rb.linearVelocity.y + gravityY);

            // Terrain has no collider (ARCHITECTURE §3 Physics): burst on reaching the ground.
            var terrain = TerrainStrip.Instance;
            Vector2 pos = transform.position;
            if (terrain != null && pos.y <= terrain.HeightAt(pos.x)) {
                // Burst on the surface where the bomb crossed it, not at its below-ground
                // position, so a fast fall or a low frame rate can't sink the burst (B008).
                Vector2 p = ImpactPoint(terrain, _prevPos, pos);
                BurstDamage(p);
                FxManager.Instance.TriggerExplosion(p);
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

            // VFX hook: trigger explosion effect for ground hits (and enemies).
            FxManager.Instance.TriggerExplosion(transform.position);

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
