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

        /// <summary>Ink white (#E8F1FA): player shots are a solid ink streak (GDD Style Guide, 2026-11-18).</summary>
        private static readonly Color Ink = new Color(0xE8 / 255f, 0xF1 / 255f, 0xFA / 255f, 1f);

        /// <summary>Sprite size in pixels: 3 long (along +x, the flight direction) by 1 thick (B039).</summary>
        private const int SpriteWidth = 3;
        private const int SpriteHeight = 1;
        /// <summary>Art scale: 100 px = 1 world unit.</summary>
        private const float PixelsPerUnit = 100f;

        /// <summary>Shared 3x1 solid ink line burst. Built once, reused by every pooled bullet.</summary>
        private static Sprite _sprite;

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

            // Replace whatever placeholder PlayerPlane passes with the 3x1 ink line burst (B039).
            // The pixels are already ink, so the renderer colour stays white (no double tint).
            // The trigger collider is set by PlayerPlane (circle, radius 0.08) and is not touched.
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) {
                sr.sprite = GetSprite();
                sr.color = Color.white;
            }

            // Balance tunables are loaded here, not in Start: Projectile.Init sets the
            // velocity on the first Pop, before Start would run (B002c).
            var g = GameBalance.Get();
            if (g != null && g.player != null) {
                speed = g.player.bulletSpeed;
                damage = g.player.bulletDamage;
            }
        }

        /// <summary>The cached 3x1 solid ink line (pivot centre, long axis along +x), built on first use.</summary>
        private static Sprite GetSprite() {
            if (_sprite != null) return _sprite;
            var tex = new Texture2D(SpriteWidth, SpriteHeight, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[SpriteWidth * SpriteHeight];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Ink;
            tex.SetPixels(pixels);
            tex.Apply();
            _sprite = Sprite.Create(tex, new Rect(0, 0, SpriteWidth, SpriteHeight),
                                    new Vector2(0.5f, 0.5f), PixelsPerUnit);
            return _sprite;
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
