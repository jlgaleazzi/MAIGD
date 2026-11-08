using System.Collections.Generic;
using UnityEngine;
using Biplanes.Core;

namespace Biplanes.Weapons {

    /// <summary>
    /// Abstract base for all projectiles (Bullet, Bomb, FlakShell).
    /// Handles pooled lifecycle, movement at `speed`, and `lifetime` expiry.
    /// Subclasses implement OnHit to define damage behavior per projectile type.
    /// </summary>
    public abstract class Projectile : MonoBehaviour {

        [SerializeField] protected Team team = Team.Neutral;
        /// <summary>Which team this projectile belongs to (used to skip own-team hits).</summary>
        public Team Team => team;

        [SerializeField] protected float speed = 10f;
        /// <summary>Movement speed in world units per second.</summary>
        public float Speed => speed;

        [SerializeField] protected float damage = 25f;
        /// <summary>Damage value passed to IDamageable.ApplyDamage (in DamageSource units).</summary>
        public float Damage => damage;

        [SerializeField] protected float lifetime = 3f;
        /// <summary>Lifetime in seconds before the projectile is recycled.</summary>
        public float Lifetime => lifetime;

        // Cached components for performance (protected for subclass access).
        protected Rigidbody2D _rb;
        protected Collider2D _collider;

        // Pool reference (set when popped; stored as object for generic compatibility).
        private object _pool;

        /// <summary>Lifetime countdown for pooled-objects safety (stop previous on re-init).</summary>
        private System.Collections.IEnumerator _lifetimeCoroutine;

        /// <summary>Call to activate the projectile from the pool.</summary>
        public void Init(Vector3 position, Quaternion rotation, Vector2 direction, object pool) {
            // Stop any running lifetime coroutine from a previous activation.
            if (_lifetimeCoroutine != null) {
                StopCoroutine(_lifetimeCoroutine);
                _lifetimeCoroutine = null;
            }

            transform.SetPositionAndRotation(position, rotation);

            // Cache component references once (Awake fires on pool construction; guard avoids redundant calls).
            if (_rb == null) {
                _rb = GetComponent<Rigidbody2D>();
                _collider = GetComponent<Collider2D>();
            }
            // Ensure trigger collider for physics overlap detection.
            if (_collider != null) {
                _collider.isTrigger = true;
            }
            // Every plane is kinematic too; without full kinematic contacts Unity reports no
            // kinematic-vs-kinematic overlaps, so bullets would pass through enemies.
            _rb.useFullKinematicContacts = true;
            // Set initial linear velocity.
            _rb.linearVelocity = direction * speed;
            _pool = pool;

            // Start lifetime timer as coroutine — pooled objects reuse the same mono behavior.
            _lifetimeCoroutine = LifetimeCoroutine();
            StartCoroutine(_lifetimeCoroutine);
        }

        /// <summary>Called when this projectile hits something.</summary>
        protected abstract void OnHit(IDamageable target);

        /// <summary>Called when lifetime expires before any hit; recycle back to pool.</summary>
        public virtual void OnLifetimeEnd() {
            if (_pool != null) {
                var method = _pool.GetType().GetMethod("PushBase");
                method?.Invoke(_pool, new object[] { this });
            }
        }

        private System.Collections.IEnumerator LifetimeCoroutine() {
            yield return new WaitForSeconds(lifetime);
            OnLifetimeEnd();
        }

        /// <summary>Trigger collision — the single physics entry point for pooled projectiles.</summary>
        private void OnTriggerEnter2D(Collider2D other) {
            var target = other.GetComponentInParent<IDamageable>();
            if (target == null) return;

            // Skip own-team hits.
            if (target.Team == team) return;

            OnHit(target);
        }

        /// <summary>Recycle this projectile back to its pool. Called internally after lifetime expires.</summary>
        public void Recycle() {
            if (_pool != null) {
                // Stop any running lifetime coroutine before recycling.
                if (_lifetimeCoroutine != null) {
                    StopCoroutine(_lifetimeCoroutine);
                    _lifetimeCoroutine = null;
                }
                var method = _pool.GetType().GetMethod("PushBase");
                method?.Invoke(_pool, new object[] { this });
            }
        }

        /// <summary>Called on destroy to clean up coroutines (safety net).</summary>
        private void OnDestroy() {
            if (_lifetimeCoroutine != null) {
                StopCoroutine(_lifetimeCoroutine);
            }
        }
    }
}
