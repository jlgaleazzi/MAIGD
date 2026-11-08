using UnityEngine;
using Biplanes.Core;

namespace Biplanes.Enemies {

    /// <summary>
    /// Static terrain obstacle (building or tree) that does not move but destroys the
    /// player on contact per GDD: "touching terrain, buildings or trees = instant 100 % damage."
    ///
    /// • Collider2D with isTrigger == true + Kinematic Rigidbody2D.
    /// • OnTriggerEnter2D → checks IDamageable.Team; Player ⇒ ApplyDamage(100f, Collision).
    ///   Enemy projectiles are ignored (they simply disappear on impact).
    /// • Static factory Spawn(position, buildingType) for MissionRunner to instantiate from JSON.
    ///
    /// Resources path convention:
    ///   building → "Sprites/obstacles/building_{type}"
    ///   tree     → "Sprites/obstacles/tree_{type}"
    /// (e.g., building_barn, tree_oak)
    /// </summary>
    public class StaticObstacle : MonoBehaviour {

        // ── Constants ─────────────────────────────────────────────────────

        /// <summary>Default trigger collider size for a standard building.</summary>
        private const float DEFAULT_SIZE = 2.0f;

        /// <summary>Default trigger collider size for a tree (narrower, taller).</summary>
        private const float TREE_RADIUS = 1.5f;

        // ── Cached components ─────────────────────────────────────────────

        private Collider2D _collider;
        private Rigidbody2D _rb;

        // ── Lifecycle ─────────────────────────────────────────────────────

        void Awake() {
            _collider = GetComponent<Collider2D>();
            if (_collider == null) {
                _collider = gameObject.AddComponent<BoxCollider2D>();
                (_collider as BoxCollider2D).isTrigger = true;
            }

            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) {
                _rb = gameObject.AddComponent<Rigidbody2D>();
                _rb.bodyType = RigidbodyType2D.Kinematic;
            }
        }

        // ── Trigger handling ──────────────────────────────────────────────

        void OnTriggerEnter2D(Collider2D other) {
            // Look up the IDamageable component on the incoming object.
            var damageable = other.GetComponentInParent<IDamageable>();

            if (damageable != null) {
                switch (damageable.Team) {
                    case Team.Player:
                        // Any player contact = instant crash (ARCHITECTURE §2 line 57).
                        damageable.ApplyDamage(100f, DamageSource.Collision);
                        break;

                    case Team.Enemy:
                    default:
                        // Enemy projectiles that hit terrain simply disappear — no damage applied.
                        // The projectile's own lifetime / pooling logic will handle cleanup.
                        break;
                }
            }
        }

        // ── Static factory (for MissionRunner) ────────────────────────────

        /// <summary>
        /// Spawn a static obstacle at the given world position with the visual appearance
        /// determined by buildingType ("building", "tree", etc.).
        /// Returns the StaticObstacle component reference for caller tracking.
        /// </summary>
        public static StaticObstacle Spawn(Vector3 position, string buildingType) {
            string category;
            float colliderSize;

            if (buildingType != null && buildingType.Length > 0 && buildingType[0] == 't') {
                // Tree path and narrower hit-box.
                category = "tree";
                colliderSize = TREE_RADIUS;
            } else {
                // Default to a building.
                category = "building";
                colliderSize = DEFAULT_SIZE;
            }

            string resourceName = $"Sprites/obstacles/{category}_{buildingType}";

            var go = new GameObject($"StaticObstacle_{buildingType}");
            go.transform.position = position;

            // Sprite renderer.
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) {
                sr = go.AddComponent<SpriteRenderer>();
            }

            var sprite = Resources.Load<Sprite>(resourceName);
            if (sprite != null) {
                sr.sprite = sprite;
            } else {
                Debug.LogWarning($"StaticObstacle '{buildingType}': '{resourceName}' not found "
                    + "in Resources — using 1×1 white placeholder.");
                sr.sprite = CreateWhiteSprite();
            }

            // Trigger collider sized to cover the visual bounds.
            var col = go.GetComponent<Collider2D>() as BoxCollider2D;
            if (col == null) {
                col = go.AddComponent<BoxCollider2D>();
            }
            col.isTrigger = true;
            col.size = new Vector2(colliderSize, colliderSize);

            // Kinematic Rigidbody2D (required by ARCHITECTURE §3).
            if (!go.GetComponent<Rigidbody2D>()) {
                go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            }

            // Team tag: Neutral terrain.
            var teamTag = go.GetComponent<TeamTag>();
            if (teamTag == null) {
                teamTag = go.AddComponent<TeamTag>();
                teamTag.team = Team.Neutral;
            } else {
                teamTag.team = Team.Neutral;
            }

            StaticObstacle obstacle = go.AddComponent<StaticObstacle>();
            return obstacle;
        }

        // ── Internal helpers ──────────────────────────────────────────────

        /// <summary>Creates a minimal 1×1 white Sprite — never crash on missing assets.</summary>
        private static Sprite CreateWhiteSprite() {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, Rect.zero, Vector2.one * 0.5f);
        }
    }
}
