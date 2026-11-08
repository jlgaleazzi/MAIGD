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

        /// <summary>Initialises the shared bomb pool with `count` entries created via `factory`.
        /// Call once during mission setup so Bomb.Spawn can pop immediately.
        /// </summary>
        public static void InitPool(System.Func<Bomb> factory, int count = 10) {
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

            // Use a custom Init overload that accepts an explicit initial velocity
            // rather than direction * speed, so we can pass the full parent momentum.
            bomb.Init(position, Quaternion.identity, parentVelocity, _pool);
            // Init sets velocity = direction * speed; a bomb must start with exactly the
            // parent's velocity (ARCHITECTURE §3), then gravity in Update takes over.
            if (bomb._rb != null) bomb._rb.linearVelocity = parentVelocity;
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
            if (terrain != null && transform.position.y <= terrain.HeightAt(transform.position.x)) {
                FxManager.Instance.TriggerExplosion(transform.position);
                Recycle();
            }
        }

        /// <summary>On collision: apply damage to enemies or terrain, then recycle.
        /// If the hit target is ground/neutral (StaticObstacle, etc.), also trigger
        /// a VFX explosion hook.
        /// </summary>
        protected override void OnHit(IDamageable target) {
            if (target == null) {
                Recycle();
                return;
            }

            bool isGround = IsGroundTarget(target);

            // Apply damage — Bomb source deals full damage per GDD / Balance.
            target.ApplyDamage(damage, DamageSource.Bomb);

            // VFX hook: trigger explosion effect for ground hits (and enemies).
            FxManager.Instance.TriggerExplosion(transform.position);

            Recycle();
        }

        /// <summary>Check if a hit target counts as ground / terrain.</summary>
        private bool IsGroundTarget(IDamageable target) {
            return target.Team == Team.Neutral;
        }
    }
}
