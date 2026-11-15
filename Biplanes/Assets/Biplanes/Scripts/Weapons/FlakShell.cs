using UnityEngine;
using Biplanes.Core;

namespace Biplanes.Weapons {

    /// <summary>
    /// Anti-aircraft shell. Team.Enemy; hits only the player with one hit unit, tagged with
    /// the shell's DamageSource so the player's Health picks the damageTaken multiplier.
    /// • Spawn(position, dir): AAA guns. Always climbs; DamageSource.Flak (damageTaken.flak).
    /// • Spawn(position, dir, source): fires along `dir` exactly (down included) with the
    ///   given source, e.g. ZeppelinTurret turrets (damageTaken.zeppelinTurret).
    /// Pooled: the pool is built lazily on the first Spawn and rebuilt only after its root
    /// was destroyed (scene reload), so it never leaks.
    /// </summary>
    public class FlakShell : Projectile {

        /// <summary>Shell speed in world units per second (AAAGun uses it to lead its shots).</summary>
        public const float ShellSpeed = 6f;

        /// <summary>Minimum upward component of the firing direction, so shells always climb.</summary>
        private const float MinUpward = 0.5f;

        private const int PoolSize = 24;
        private const float ShellLifetime = 3f;
        private const float ColliderRadius = 0.12f;

        private static ObjectPool<FlakShell> _pool;
        /// <summary>Scene object that owns every shell. Unity-null after a scene unload → rebuild.</summary>
        private static GameObject _root;
        private static Sprite _sprite;

        /// <summary>Damage source reported on a hit. Set on every Spawn (pooled shells are reused).</summary>
        private DamageSource _source = DamageSource.Flak;

        /// <summary>Source this shell reports when it hits the player.</summary>
        public DamageSource Source => _source;

        /// <summary>Spawn an AAA flak shell at `position`, flying toward `targetDirection`
        /// (any vector toward the predicted target; it is normalised and forced upward).
        /// Hits use DamageSource.Flak. Returns null only if the pool is exhausted.</summary>
        public static FlakShell Spawn(Vector3 position, Vector2 targetDirection) {
            Vector2 dir = targetDirection.sqrMagnitude > 0.0001f ? targetDirection.normalized : Vector2.up;
            if (dir.y < MinUpward) {
                dir.y = MinUpward;
                dir.Normalize();
            }
            return SpawnInternal(position, dir, DamageSource.Flak);
        }

        /// <summary>Spawn a shell at `position` flying exactly along `direction` (normalised;
        /// no forced climb, so it can fire downward). A zero direction flies straight down.
        /// Hits report `source` (e.g. DamageSource.ZeppelinTurret), which the player's Health
        /// maps to its damageTaken multiplier. Returns null only if the pool is exhausted.</summary>
        public static FlakShell Spawn(Vector3 position, Vector2 direction, DamageSource source) {
            Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
            return SpawnInternal(position, dir, source);
        }

        private static FlakShell SpawnInternal(Vector3 position, Vector2 unitDirection, DamageSource source) {
            EnsurePool();

            var shell = _pool.Pop();
            if (shell == null) return null;

            shell._source = source;
            shell.Init(position, Quaternion.identity, unitDirection, _pool);
            return shell;
        }

        /// <summary>Builds the pool once per scene. Safe to call any number of times.</summary>
        private static void EnsurePool() {
            if (_pool != null && _root != null) return;

            // Rebuild after a scene reload: dispose the stale pool first so it unsubscribes from
            // Application.quitting (its items and root died with the old scene and are skipped) (B009c).
            _pool?.Dispose();
            _root = new GameObject("FlakShellPool");
            if (_sprite == null) _sprite = CreateSolidSprite(new Color(1f, 0.85f, 0.4f), 4, 4, 32f);

            var rootTransform = _root.transform;
            _pool = new ObjectPool<FlakShell>(() => MakeShell(rootTransform), PoolSize, rootTransform);
            ClearDontSaveFlags(rootTransform);
        }

        /// <summary>ObjectPool parents its own hidden root under ours; drop its DontSave flags so
        /// it is destroyed with our scene root instead of surviving scene loads.</summary>
        private static void ClearDontSaveFlags(Transform root) {
            for (int i = 0; i < root.childCount; i++) {
                root.GetChild(i).gameObject.hideFlags = HideFlags.None;
            }
        }

        private static FlakShell MakeShell(Transform parent) {
            var go = new GameObject("FlakShell");
            go.transform.SetParent(parent, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _sprite;
            sr.sortingOrder = 15;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = ColliderRadius;

            return go.AddComponent<FlakShell>();
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
            // Set in Awake, not Start: Projectile.Init applies speed on the first Pop, before Start.
            team = Team.Enemy;
            speed = ShellSpeed;
            lifetime = ShellLifetime;
            // One hit unit. The player's Health multiplies it by the damageTaken value of
            // _source (flak for AAA, zeppelinTurret for zeppelin turrets).
            damage = 1f;
        }

        /// <summary>Damage the player only; other teams' objects (neutral pickups, obstacles) are ignored.</summary>
        protected override void OnHit(IDamageable target) {
            if (target == null || target.Team != Team.Player) return;

            target.ApplyDamage(damage, _source);
            Recycle();
        }
    }
}
