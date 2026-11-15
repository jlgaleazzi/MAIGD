using System;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Fx;

namespace Biplanes.Enemies {

    /// <summary>
    /// Enemy hangar base: mission 4's bomb target. Ground-bound (its bottom edge rests on the
    /// ground point passed to Spawn), trigger collider, Team.Enemy, destroyable by
    /// DamageSource.Bomb only. Bullets and flak are ignored, like AAAGun. Health comes from
    /// Balance enemyHealth.enemyBase (damage points, as Health enemy mode).
    ///
    /// Self-contained IDamageable (no Health component), so the damage-source filter can never
    /// be bypassed. On death it raises GameEvents.EntityDestroyed("enemyBase", Team.Enemy)
    /// exactly once (ScoreKeeper awards score.enemyBase from it), raises the static
    /// <see cref="Destroyed"/> event carrying its spawn <see cref="Id"/>, explodes and is removed.
    /// Touching the base crashes the player, as buildings do (GDD).
    /// </summary>
    public class EnemyBase : MonoBehaviour, IDamageable {

        /// <summary>Balance score key; also the GameObject name (ARCHITECTURE §3 Core).</summary>
        public const string TypeKey = "enemyBase";

        private const string SpritePath = "Sprites/targets/enemy_base";

        /// <summary>Collider size used only when the sprite is missing (sprite is 2.6 x 1.2).</summary>
        private static readonly Vector2 FallbackSize = new Vector2(2.6f, 1.2f);

        /// <summary>Raised once when a base is destroyed (after EntityDestroyed). Carries the
        /// instance, so listeners can read <see cref="Id"/> and <see cref="KilledByBomb"/>
        /// (e.g. MissionRunner completing a bombTarget objective by targetId).</summary>
        public static event Action<EnemyBase> Destroyed;

        // ── State ──────────────────────────────────────────────────────────

        private float _damagePercent;                 // 0 → 100
        private bool _dead;                           // guards the single death / score event

        /// <summary>The mission JSON spawn entry id (e.g. "enemy_base_01"); may be empty.</summary>
        public string Id { get; private set; } = string.Empty;

        /// <summary>True once the base was destroyed by a bomb (the only damage it accepts).</summary>
        public bool KilledByBomb { get; private set; }

        /// <summary>Current damage percentage, 0..100.</summary>
        public float CurrentDamagePercent => _damagePercent;

        // ── IDamageable ────────────────────────────────────────────────────

        public Team Team => Team.Enemy;

        public bool IsDead => _dead;

        /// <summary>Only DamageSource.Bomb counts; every other source is ignored.
        /// `percent` is damage points (bombDamage), turned into percent of enemyHealth.enemyBase.</summary>
        public void ApplyDamage(float percent, DamageSource src) {
            if (_dead || src != DamageSource.Bomb) return;
            if (percent <= 0f) return;

            _damagePercent = Mathf.Clamp(_damagePercent + percent * PercentPerPoint(), 0f, 100f);
            if (_damagePercent >= 100f) Die();
        }

        /// <summary>The base is not healable.</summary>
        public void Heal(float amount) { /* enemy bases are not healable */ }

        // ── Static factory ─────────────────────────────────────────────────

        /// <summary>Create an enemy base. `position` is the ground point it stands on
        /// (x = spawn x, y = TerrainStrip HeightAt(x) + entry.y); the sprite and collider are
        /// raised so the base's bottom edge sits on that y. `id` is the spawn entry id.</summary>
        public static EnemyBase Spawn(Vector3 position, string id) {
            var go = new GameObject(TypeKey);

            var tag = go.AddComponent<TeamTag>();
            tag.team = Team.Enemy;

            var sr = go.AddComponent<SpriteRenderer>();
            var sprite = Resources.Load<Sprite>(SpritePath);
            if (sprite != null) {
                sr.sprite = sprite;
            } else {
                Debug.LogWarning($"EnemyBase: missing sprite at '{SpritePath}' — using 1x1 white.");
                sr.sprite = CreateFallbackSprite();
                go.transform.localScale = new Vector3(FallbackSize.x, FallbackSize.y, 1f);
            }
            sr.sortingOrder = 5;

            // Local-space size of the visual (the fallback sprite is 1x1, scaled up above).
            Bounds localBounds = sr.sprite.bounds;
            Vector2 localSize = localBounds.size;
            Vector2 localCenter = localBounds.center;
            float worldHeight = localSize.y * go.transform.localScale.y;
            float worldCenterY = localCenter.y * go.transform.localScale.y;

            // Bottom edge on the ground point.
            go.transform.position = new Vector3(position.x,
                position.y + worldHeight * 0.5f - worldCenterY, position.z);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = localSize;
            col.offset = localCenter;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var b = go.AddComponent<EnemyBase>();
            b.Id = id ?? string.Empty;
            return b;
        }

        // ── Trigger: the player crashes into the base like into a building ──

        private void OnTriggerEnter2D(Collider2D other) {
            if (_dead) return;
            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null || damageable.Team != Team.Player || damageable.IsDead) return;
            // Player Health scales by damageTaken.collision (100): 1 = instant crash (ARCHITECTURE §3).
            damageable.ApplyDamage(1f, DamageSource.Collision);
        }

        // ── Internals ──────────────────────────────────────────────────────

        /// <summary>Percent of max health per damage point, from Balance enemyHealth.enemyBase.</summary>
        private static float PercentPerPoint() {
            var balance = GameBalance.Get();
            float hp = balance != null && balance.enemyHealth != null ? balance.enemyHealth.enemyBase : 0f;
            if (hp <= 0f) {
                Debug.LogWarning("EnemyBase: Balance enemyHealth.enemyBase missing or <= 0 — one bomb kills.");
                return 100f;
            }
            return 100f / hp;
        }

        private void Die() {
            if (_dead) return;
            _dead = true;
            KilledByBomb = true;   // Bomb is the only source that reaches Die().

            // Score is awarded once, by ScoreKeeper on this event (no AddPoints here).
            GameEvents.RaiseEntityDestroyed(TypeKey, Team.Enemy);
            Destroyed?.Invoke(this);

            FxManager.Instance.TriggerExplosion(transform.position);
            Destroy(gameObject);
        }

        private static Sprite CreateFallbackSprite() {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply(false);
            tex.name = "EnemyBase_fallback";
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
