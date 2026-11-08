using UnityEngine;
using Biplanes.Core;

namespace Biplanes.Weapons {

    /// <summary>
    /// Anti-aircraft shell fired by AAA guns toward the player's predicted position.
    /// Flies upward from the ground with a slight horizontal drift, dealing flak damage on hit.
    /// Pooled lifecycle — no Instantiate or Destroy at runtime.
    /// </summary>
    public class FlakShell : Projectile {

        /// <summary>Shared pool instance — initialised once by AAAGun at mission start.</summary>
        private static ObjectPool<FlakShell> _pool;

        // --- serialisable tunables (persisted in editor, overridden by Balance.json) ---

        [SerializeField] private float verticalSpeed = 6f;
        [SerializeField] private float horizontalDriftRatio = 0.35f;

        /// <summary>Initialises the shared flak shell pool with `count` entries created via `factory`.
        /// Call once during mission setup so FlakShell.Spawn can pop immediately.
        /// </summary>
        public static void InitPool(System.Func<FlakShell> factory, int count = 20) {
            _pool = new ObjectPool<FlakShell>(factory, count);
        }

        /// <summary>Spawn a flak shell from the shared pool at `position` aimed toward `targetXDirection`.
        /// The shell flies upward with strong negative Y velocity and small horizontal drift.
        /// </summary>
        /// <param name="position">World position to spawn (ground level of AAA gun).</param>
        /// <param name="targetXDirection">Horizontal drift direction toward the player's predicted
        ///   position — typically +x or -x, used to lead shots ahead of the moving target.</param>
        public static FlakShell Spawn(Vector3 position, Vector2 targetXDirection) {
            if (_pool == null) {
                Debug.LogError("FlakShell pool not initialised — call InitPool first.");
                return null;
            }

            var shell = _pool.Pop();
            if (shell == null) return null;

            // Compute horizontal drift from the target direction.
            float driftX = Mathf.Approximately(targetXDirection.x, 0f) ? 0f :
                           Mathf.Sign(targetXDirection.x) * 0.35f;

            // Direction vector: strong upward (negative Y) with slight horizontal drift.
            // Init() will scale this by `speed`, so the ratio is preserved regardless of tuning.
            Vector2 direction = new Vector2(driftX, -1f);

            shell.Init(position, Quaternion.identity, direction, _pool);
            return shell;
        }

        private void Awake() {
            // Enemy projectiles belong to Team.Enemy (to skip own-team hits).
            team = Team.Enemy;
        }

        private void Start() {
            // Override any inspector / balance.json tunables at runtime.
            var g = GameBalance.Get();
            if (g != null) {
                speed       = verticalSpeed;                 // ~6 world units/s (slower than bullet for visual clarity)
                damage      = g.damageTaken.flak;           // 15 from Balance.json
            }
        }

        /// <summary>On collision: apply flak damage to target, then recycle.</summary>
        protected override void OnHit(IDamageable target) {
            if (target == null) {
                Recycle();
                return;
            }

            // Apply raw damage value through the Flak source — Health.ApplyDamage routes it correctly.
            target.ApplyDamage(damage, Core.DamageSource.Flak);

            // Flak shell is single-use — recycle back to pool regardless of outcome.
            Recycle();
        }
    }
}
