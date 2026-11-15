using UnityEngine;
using Biplanes.Core;

namespace Biplanes.Weapons {

    /// <summary>
    /// Machine-gun round fired by enemy fighters and bomber gunners (B023). Team.Enemy; hits
    /// only the player with one hit unit tagged with the shooter's DamageSource, so the
    /// player's Health scales it by damageTaken (Bullet = fighterBullet, Bomb = bomberGunner).
    /// Enemies are skipped by Projectile's team check and neutral objects are ignored.
    /// Recycles on a hit, at the end of its lifetime, or when it leaves the screen.
    /// Speed and lifetime come from Balance enemyFire (EnemyFireData.Resolve).
    /// Pooled: the pool is built lazily on the first Spawn under a scene root and rebuilt only
    /// after that root was destroyed (scene reload), like FlakShell, so it never leaks.
    /// Also hosts the shared aiming helpers FighterAI and BomberAI use.
    /// </summary>
    public class EnemyBullet : Projectile {

        private const int PoolSize = 32;
        private const float ColliderRadius = 0.08f;
        /// <summary>World units past the screen edge before a bullet counts as off screen.</summary>
        private const float OffScreenMargin = 0.5f;

        /// <summary>Hostile tint (#E4572E) on the same 6x2 white rect the player's bullet uses.</summary>
        private static readonly Color HostileTint = new Color(0xE4 / 255f, 0x57 / 255f, 0x2E / 255f, 1f);

        private static ObjectPool<EnemyBullet> _pool;
        /// <summary>Scene object that owns every enemy bullet. Unity-null after a scene unload → rebuild.</summary>
        private static GameObject _root;
        private static Sprite _sprite;
        /// <summary>Camera for the screen-exit check. Refreshed on Spawn (never per frame).</summary>
        private static Camera _cam;

        /// <summary>Damage source reported on a hit. Set on every Spawn (pooled bullets are reused).</summary>
        private DamageSource _source = DamageSource.Bullet;

        /// <summary>Source this bullet reports when it hits the player.</summary>
        public DamageSource Source => _source;

        /// <summary>Fire an enemy bullet from `position` along `direction` (normalised; a zero
        /// direction flies left). Hits report `source`. Returns null only if the pool is exhausted.</summary>
        public static EnemyBullet Spawn(Vector3 position, Vector2 direction, DamageSource source) {
            EnsurePool();
            if (_cam == null) _cam = Camera.main;

            var bullet = _pool.Pop();
            if (bullet == null) return null;

            Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.left;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            bullet._source = source;
            bullet.Init(position, Quaternion.Euler(0f, 0f, angle), dir, _pool);
            return bullet;
        }

        // ── Aiming helpers (shared by FighterAI and BomberAI) ─────────────

        /// <summary>True when `target` is within `range` of `from` and no more than
        /// `coneHalfAngle` degrees off `forward`. A half-angle of 180 accepts any direction.</summary>
        public static bool InFiringCone(Vector2 from, Vector2 target, Vector2 forward,
                                        float coneHalfAngle, float range) {
            Vector2 d = target - from;
            float dist = d.magnitude;
            if (dist > range) return false;
            if (dist < 0.0001f) return true;
            return Vector2.Angle(forward, d) <= coneHalfAngle;
        }

        /// <summary>Unit direction from `from` toward `target`, turned by a random error of up
        /// to ± `spreadDegrees`, then clamped to within `coneHalfAngle` of `forward` so the
        /// shot never goes backwards (pass 180 for no clamp).</summary>
        public static Vector2 AimWithinCone(Vector2 from, Vector2 target, Vector2 forward,
                                            float coneHalfAngle, float spreadDegrees) {
            Vector2 fwd = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector2.left;
            Vector2 d = target - from;
            Vector2 dir = d.sqrMagnitude > 0.0001f ? d.normalized : fwd;

            if (spreadDegrees > 0f) {
                dir = Rotate(dir, Random.Range(-spreadDegrees, spreadDegrees));
            }

            float off = Vector2.SignedAngle(fwd, dir);
            if (Mathf.Abs(off) > coneHalfAngle) {
                dir = Rotate(fwd, Mathf.Sign(off) * coneHalfAngle);
            }
            return dir;
        }

        /// <summary>True when `position` is inside `cam`'s orthographic view widened by
        /// `margin` world units. With no camera there is no screen to leave: true.</summary>
        public static bool IsOnScreen(Camera cam, Vector2 position, float margin) {
            if (cam == null) return true;
            float halfHeight = cam.orthographicSize + margin;
            float halfWidth = cam.orthographicSize * cam.aspect + margin;
            Vector3 c = cam.transform.position;
            return Mathf.Abs(position.x - c.x) <= halfWidth && Mathf.Abs(position.y - c.y) <= halfHeight;
        }

        private static Vector2 Rotate(Vector2 v, float degrees) {
            float r = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(r);
            float sin = Mathf.Sin(r);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        // ── Pool ──────────────────────────────────────────────────────────

        /// <summary>Builds the pool once per scene. Safe to call any number of times.</summary>
        private static void EnsurePool() {
            if (_pool != null && _root != null) return;

            // Rebuild after a scene reload: dispose the stale pool first so it unsubscribes from
            // Application.quitting (its items and root died with the old scene and are skipped).
            _pool?.Dispose();
            _root = new GameObject("EnemyBulletPool");
            _cam = null;
            if (_sprite == null) _sprite = CreateSolidSprite(Color.white, 6, 2, 32f);

            var rootTransform = _root.transform;
            _pool = new ObjectPool<EnemyBullet>(() => MakeBullet(rootTransform), PoolSize, rootTransform);
            // ObjectPool parents its own hidden root under ours; drop its DontSave flags so it is
            // destroyed with our scene root instead of surviving scene loads.
            for (int i = 0; i < rootTransform.childCount; i++) {
                rootTransform.GetChild(i).gameObject.hideFlags = HideFlags.None;
            }
        }

        private static EnemyBullet MakeBullet(Transform parent) {
            var go = new GameObject("EnemyBullet");
            go.transform.SetParent(parent, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _sprite;
            sr.color = HostileTint;
            sr.sortingOrder = 15;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = ColliderRadius;

            return go.AddComponent<EnemyBullet>();
        }

        private static Sprite CreateSolidSprite(Color color, int width, int height, float pixelsPerUnit) {
            var tex = new Texture2D(width, height);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void Awake() {
            // Set in Awake, not Start: Projectile.Init applies speed on the first Pop, before Start.
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            team = Team.Enemy;
            speed = fire.bulletSpeed;
            lifetime = fire.bulletLifetime;
            // One hit unit. The player's Health multiplies it by the damageTaken value of _source.
            damage = 1f;
        }

        private void Update() {
            if (!IsOnScreen(_cam, transform.position, OffScreenMargin)) Recycle();
        }

        /// <summary>Damage the player only; neutral objects (pickups, obstacles) are ignored.</summary>
        protected override void OnHit(IDamageable target) {
            if (target == null || target.Team != Team.Player) return;

            target.ApplyDamage(damage, _source);
            Recycle();
        }
    }
}
