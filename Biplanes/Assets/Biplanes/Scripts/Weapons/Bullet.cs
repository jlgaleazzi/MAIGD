using UnityEngine;
using Biplanes.Core;
using Biplanes.Weapons;

namespace Biplanes.Weapons {

    /// <summary>
    /// Player machine-gun projectile. Pooled, fast (along the plane's facing), deals bulletDamage per hit.
    /// </summary>
    public class Bullet : Projectile {

        /// Pool singleton shared by all bullets in the scene. Set once at init time.
        private static ObjectPool<Bullet> _pool;

        /// <summary>
        /// Initialise the shared bullet pool with `count` entries created via `factory`.
        /// Call this once during level setup so Bullet.Spawn can pop immediately.
        /// </summary>
        public static void InitPool(System.Func<Bullet> factory, int count = 40) {
            // Free the previous mission's pool (root and every item) before replacing it,
            // so Next/Retry don't leak pooled objects (B006a).
            _pool?.Dispose();
            _pool = new ObjectPool<Bullet>(factory, count);
        }

        /// <summary>Spawn a bullet flying right (+x) from the shared pool.</summary>
        public static Bullet Spawn(Vector3 position, Quaternion rotation) {
            return Spawn(position, rotation, 1);
        }

        /// <summary>
        /// Spawn a bullet from the shared pool at `position` with `rotation`,
        /// flying horizontally in `direction` (+1 = right, -1 = left; the player's Facing).
        /// </summary>
        public static Bullet Spawn(Vector3 position, Quaternion rotation, int direction) {
            // Preserve original behaviour for callers that only specify horizontal direction.
            Vector2 forwardDir = new Vector2(direction < 0 ? -1f : 1f, 0f);
            return Spawn(position, rotation, forwardDir);
        }

        /// <summary>
        /// Spawn a bullet from the shared pool at `position` with `rotation`,
        /// flying along the supplied `direction` vector (will be normalized).
        /// Supports non‑horizontal fire such as the player's nose direction.
        /// </summary>
        public static Bullet Spawn(Vector3 position, Quaternion rotation, Vector2 direction) {
            if (_pool == null) {
                Debug.LogError("Bullet pool not initialised — call InitPool first.");
                return null;
            }

            var bullet = _pool.Pop();
            if (bullet == null) return null;

            Vector2 dirNorm = direction.normalized;
            bullet.Init(position, rotation, dirNorm, _pool);
            return bullet;
        }

        private void Awake() {
            // Default team for player projectiles.
            team = Team.Player;

            // Balance tunables are loaded here, not in Start: Projectile.Init sets the
            // velocity on the first Pop, before Start would run (B002c).
            var g = GameBalance.Get();
            if (g != null && g.player != null) {
                speed = g.player.bulletSpeed;
                damage = g.player.bulletDamage;
            }
        }

        /// <summary>On collision with an IDamageable: apply damage then recycle.</summary>
        protected override void OnHit(IDamageable target) {
            if (target == null) {
                Recycle();
                return;
            }

            // Neutral objects (health-balloon pickups) don't stop bullets (B011).
            if (target.Team == Core.Team.Neutral) return;

            // Apply raw damage as a percentage-like value through the DamageSource.Bullet path.
            // Health.ApplyDamage interprets it: for enemies (health-Point-based) it divides
            // by total health to get percent; for the player it is treated as direct percent.
            target.ApplyDamage(damage, Core.DamageSource.Bullet);

            // Bullet is single-use — recycle back to pool regardless of whether the target died.
            Recycle();
        }
    }
}
