using UnityEngine;

namespace Biplanes.Core {

    /// <summary>
    /// MonoBehaviour component that tracks percent-based damage on any entity.
    /// Implements IDamageable and raises core game events (PlayerDamageChanged,
    /// EntityDestroyed) when state changes.
    ///
    /// Damage values are resolved through GameBalance.damageTaken using the
    /// DamageSource enum — no magic numbers.
    ///
    /// Attached by GameManager / spawner code to every hittable entity.
    /// </summary>
    public class Health : MonoBehaviour, IDamageable {

        // ── Serialized fields ─────────────────────────────────────────────

        /// <summary>0 = player mode (damage is a percentage scaled by damageTaken).
        /// &gt; 0 = enemy mode: damage is in points out of this many hit points
        /// (Balance.json enemyHealth.*, ARCHITECTURE §4).</summary>
        [SerializeField] private float maxHitPoints;

        // ── Fields (per brief) ────────────────────────────────────────────

        private float currentDamage = 0f;
        TeamTag tag;

        // ── IDamageable implementation ────────────────────────────────────

        public Team Team { get; private set; }

        public bool IsDead => currentDamage >= 100f;

        /// <summary>Current damage percentage (0..100). Read-only for smoke-tint logic.</summary>
        public float CurrentDamagePercent => currentDamage;

        /// <summary>Hit points for enemy mode; 0 means player (percent) mode.</summary>
        public float MaxHitPoints => maxHitPoints;

        /// <summary>Switch to enemy mode with `hp` hit points (from Balance enemyHealth).
        /// Call right after AddComponent, before any damage.</summary>
        public void SetMaxHitPoints(float hp) {
            maxHitPoints = Mathf.Max(0f, hp);
        }

        void Awake() {
            tag = GetComponent<TeamTag>();
            if (tag == null) {
                Debug.LogWarning($"Health: missing TeamTag on '{name}' — using Neutral.", gameObject);
                Team = Team.Neutral;
            } else {
                Team = tag.Team;
            }
        }

        /// <summary>Apply a hit of the given DamageSource. Resolves the actual
        /// percentage from GameBalance.damageTaken, then clamps and checks death.</summary>
        public void ApplyDamage(float percent, DamageSource src) {
            // Already destroyed: further hits in the same frame must not raise EntityDestroyed again.
            if (IsDead) return;

            float actualDamage;
            if (maxHitPoints > 0f) {
                // Enemy: the value is damage points (bulletDamage, bombDamage). A mid-air or
                // ground collision always destroys the enemy.
                actualDamage = src == DamageSource.Collision ? 100f : percent / maxHitPoints * 100f;
            } else {
                // Player: percentage scaled by Balance damageTaken for this source.
                actualDamage = percent * GetDamageMultiplier(src);
            }

            currentDamage = Mathf.Clamp(currentDamage + actualDamage, 0f, 100f);

            // Notify listeners if the entity is a player-side one
            if (Team == Team.Player) {
                GameEvents.RaisePlayerDamageChanged(currentDamage);
            }

            // Check for death
            if (IsDead) {
                GameEvents.RaiseEntityDestroyed(gameObject.name, Team);
                OnDestroyed();
            }
        }

        /// <summary>Restore health by healing the given percentage points. No damage-source
        /// multiplier is applied — the amount is used directly as a clamp(0..100) restore.</summary>
        public void Heal(float amount) {
            currentDamage = Mathf.Clamp(currentDamage - amount, 0f, 100f);

            if (Team == Team.Player) {
                GameEvents.RaisePlayerDamageChanged(currentDamage);
            }
        }

        /// <summary>
        /// Protected virtual hook called when the entity reaches 100% damage.
        /// Subclasses can override to trigger VFX, play sound, etc.
        /// </summary>
        protected virtual void OnDestroyed() {
            // Default: do nothing. Subclasses override.
        }

        // ── Internal helpers ──────────────────────────────────────────────

        private float GetDamageMultiplier(DamageSource src) {
            var balance = GameBalance.Get();
            if (balance == null || balance.damageTaken == null) {
                Debug.LogWarning("Health: GameBalance not loaded — using default multipliers.");
                return 1f; // fallback: raw percent
            }

            switch (src) {
                case DamageSource.Bullet:
                    return balance.damageTaken.fighterBullet;
                case DamageSource.Bomb:
                    return balance.damageTaken.fighterBullet; // fallback to default bullet multiplier
                case DamageSource.Flak:
                    return balance.damageTaken.flak;
                case DamageSource.Collision:
                    return balance.damageTaken.collision;
                default:
                    Debug.LogWarning($"Health: unknown DamageSource '{src}', using 1x.");
                    return 1f;
            }
        }
    }
}
