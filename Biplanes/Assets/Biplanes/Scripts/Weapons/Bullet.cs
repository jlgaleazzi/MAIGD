using UnityEngine;
using Biplanes.Core;
using Biplanes.Weapons;

namespace Biplanes.Weapons {

    /// <summary>
    /// Player machine-gun projectile. Pooled, fast (+x), deals bulletDamage per hit.
    /// </summary>
    public class Bullet : Projectile {

        /// Pool singleton shared by all bullets in the scene. Set once at init time.
        private static ObjectPool<Bullet> _pool;

        /// <summary>
        /// Initialise the shared bullet pool with `count` entries created via `factory`.
        /// Call this once during level setup so Bullet.Spawn can pop immediately.
        /// </summary>
        public static void InitPool(System.Func<Bullet> factory, int count = 40) {
            _pool = new ObjectPool<Bullet>(factory, count);
        }

        /// <summary>Spawn a bullet from the shared pool at `position` with `rotation`.</summary>
        public static Bullet Spawn(Vector3 position, Quaternion rotation) {
            if (_pool == null) {
                Debug.LogError("Bullet pool not initialised — call InitPool first.");
                return null;
            }

            var bullet = _pool.Pop();
            if (bullet == null) return null;

            // Default direction is +x (forward in the scrolling world).
            Vector2 forwardDir = new Vector2(1f, 0f);
            bullet.Init(position, rotation, forwardDir, _pool);
            return bullet;
        }

        private void Awake() {
            // Default team for player projectiles.
            team = Team.Player;
        }

        private void Start() {
            // Override any inspector values with balance.json tunables at runtime.
            var g = GameBalance.Get();
            if (g != null) {
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

            // Apply raw damage as a percentage-like value through the DamageSource.Bullet path.
            // Health.ApplyDamage interprets it: for enemies (health-Point-based) it divides
            // by total health to get percent; for the player it is treated as direct percent.
            target.ApplyDamage(damage, Core.DamageSource.Bullet);

            // Bullet is single-use — recycle back to pool regardless of whether the target died.
            Recycle();
        }
    }
}
