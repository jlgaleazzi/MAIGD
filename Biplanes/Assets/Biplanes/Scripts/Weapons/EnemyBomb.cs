using UnityEngine;
using Biplanes.Core;
using Biplanes.Fx;
using Biplanes.Missions;

namespace Biplanes.Weapons {

    /// <summary>
    /// Bomb dropped by enemy bombers. Team.Enemy, with its own pool (never the player's Bomb
    /// pool). Falls under gravity exactly like the player's Bomb, bursts on reaching the
    /// terrain, and damages only the player (one DamageSource.Bomb hit; the player's Health
    /// scales it by Balance damageTaken). Enemies are skipped by Projectile's team check and
    /// neutral objects are ignored.
    /// The pool is built lazily on the first Spawn and rebuilt only after its root was
    /// destroyed (scene reload), so it never leaks.
    /// </summary>
    public class EnemyBomb : Projectile {

        /// <summary>Same fall as the player's Bomb (Bomb.gravityMultiplier).</summary>
        private const float GravityMultiplier = 12f;

        private const int PoolSize = 12;
        private const float BombLifetime = 3f;
        private const float ColliderRadius = 0.12f;

        private static ObjectPool<EnemyBomb> _pool;
        /// <summary>Scene object that owns every enemy bomb. Unity-null after a scene unload → rebuild.</summary>
        private static GameObject _root;
        private static Sprite _sprite;
        /// <summary>Pencil (#9DB4CC): white-only palette (GDD Style Guide, 2026-11-18).</summary>
        private static readonly Color Pencil = new Color(0x9D / 255f, 0xB4 / 255f, 0xCC / 255f, 1f);

        /// <summary>Drop an enemy bomb at `position` with the bomber's velocity as its start
        /// velocity. Returns null only if the pool is exhausted.</summary>
        public static EnemyBomb Spawn(Vector3 position, Vector2 parentVelocity) {
            EnsurePool();

            var bomb = _pool.Pop();
            if (bomb == null) return null;

            bomb.Init(position, Quaternion.identity, parentVelocity, _pool);
            // Init sets velocity = direction * speed; a bomb starts with exactly the parent's velocity.
            if (bomb._rb != null) bomb._rb.linearVelocity = parentVelocity;
            return bomb;
        }

        /// <summary>Builds the pool once per scene. Safe to call any number of times.</summary>
        private static void EnsurePool() {
            if (_pool != null && _root != null) return;

            // Rebuild after a scene reload: dispose the stale pool first so it unsubscribes from
            // Application.quitting (its items and root died with the old scene and are skipped) (B009c).
            _pool?.Dispose();
            _root = new GameObject("EnemyBombPool");
            if (_sprite == null) _sprite = CreateSolidSprite(Pencil, 4, 6, 32f);

            var rootTransform = _root.transform;
            _pool = new ObjectPool<EnemyBomb>(() => MakeBomb(rootTransform), PoolSize, rootTransform);
            // ObjectPool parents its own hidden root under ours; drop its DontSave flags so it is
            // destroyed with our scene root instead of surviving scene loads.
            for (int i = 0; i < rootTransform.childCount; i++) {
                rootTransform.GetChild(i).gameObject.hideFlags = HideFlags.None;
            }
        }

        private static EnemyBomb MakeBomb(Transform parent) {
            var go = new GameObject("EnemyBomb");
            go.transform.SetParent(parent, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _sprite;
            sr.sortingOrder = 15;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = ColliderRadius;

            return go.AddComponent<EnemyBomb>();
        }

        private static Sprite CreateSolidSprite(Color color, int width, int height, float pixelsPerUnit) {
            var tex = new Texture2D(width, height);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        }

        private void Awake() {
            team = Team.Enemy;
            lifetime = BombLifetime;
            // One enemy-bomb hit; the player's Health multiplies it by Balance damageTaken.
            damage = 1f;
        }

        private void Update() {
            if (_rb == null) return;

            float gravityY = Physics2D.gravity.y * GravityMultiplier * Time.deltaTime;
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, _rb.linearVelocity.y + gravityY);

            // Terrain has no collider (ARCHITECTURE §3 Physics): burst on reaching the ground.
            var terrain = TerrainStrip.Instance;
            if (terrain != null && transform.position.y <= terrain.HeightAt(transform.position.x)) {
                FxManager.Instance.TriggerExplosion(transform.position);
                Recycle();
            }
        }

        /// <summary>Damage the player only; neutral objects (pickups, obstacles) are ignored.</summary>
        protected override void OnHit(IDamageable target) {
            if (target == null || target.Team != Team.Player) return;

            target.ApplyDamage(damage, DamageSource.Bomb);
            FxManager.Instance.TriggerExplosion(transform.position);
            Recycle();
        }
    }
}
