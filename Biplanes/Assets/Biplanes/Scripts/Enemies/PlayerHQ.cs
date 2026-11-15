using UnityEngine;
using Biplanes.Core;

namespace Biplanes.Enemies {

    /// <summary>
    /// The player's headquarters: a friendly, indestructible ground structure (mission 5 is
    /// lost if the zeppelin reaches it). Team.Neutral via TeamTag and NOT an IDamageable, so it
    /// is never hit or scored: player bullets and bombs, and enemy fire, pass through it
    /// (Projectile ignores colliders without an IDamageable; Bomb's ground burst only hits
    /// Team.Enemy IDamageables). Touching it crashes the player, as buildings do (GDD).
    ///
    /// Ground-bound: its bottom edge rests on the ground point passed to Spawn.
    /// </summary>
    public class PlayerHQ : MonoBehaviour {

        private const string SpritePath = "Sprites/targets/player_hq";

        /// <summary>Visual size used only when the sprite is missing (sprite is 4.0 x 1.0).</summary>
        private static readonly Vector2 FallbackSize = new Vector2(4f, 1f);

        /// <summary>The mission JSON spawn entry id (e.g. "hq_01"); may be empty.</summary>
        public string Id { get; private set; } = string.Empty;

        /// <summary>World x of the HQ (fixed; it never moves). Mission 5's failAtX / zeppelin
        /// logic can compare against this.</summary>
        public float WorldX { get; private set; }

        /// <summary>The ground point the HQ stands on (x = WorldX).</summary>
        public Vector3 GroundPosition { get; private set; }

        /// <summary>Create the HQ. `position` is the ground point it stands on
        /// (x = spawn x, y = TerrainStrip HeightAt(x) + entry.y); the sprite and collider are
        /// raised so the bottom edge sits on that y. `id` is the spawn entry id.</summary>
        public static PlayerHQ Spawn(Vector3 position, string id) {
            var go = new GameObject("playerHQ");

            var tag = go.AddComponent<TeamTag>();
            tag.team = Team.Neutral;

            var sr = go.AddComponent<SpriteRenderer>();
            var sprite = Resources.Load<Sprite>(SpritePath);
            if (sprite != null) {
                sr.sprite = sprite;
            } else {
                Debug.LogWarning($"PlayerHQ: missing sprite at '{SpritePath}' — using 1x1 white.");
                sr.sprite = CreateFallbackSprite();
                go.transform.localScale = new Vector3(FallbackSize.x, FallbackSize.y, 1f);
            }
            sr.sortingOrder = 5;

            Bounds localBounds = sr.sprite.bounds;
            float worldHeight = localBounds.size.y * go.transform.localScale.y;
            float worldCenterY = localBounds.center.y * go.transform.localScale.y;
            go.transform.position = new Vector3(position.x,
                position.y + worldHeight * 0.5f - worldCenterY, position.z);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = localBounds.size;
            col.offset = localBounds.center;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var hq = go.AddComponent<PlayerHQ>();
            hq.Id = id ?? string.Empty;
            hq.WorldX = position.x;
            hq.GroundPosition = position;
            return hq;
        }

        // ── Trigger: the player crashes into the HQ like into a building ──

        private void OnTriggerEnter2D(Collider2D other) {
            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null || damageable.Team != Team.Player || damageable.IsDead) return;
            // Player Health scales by damageTaken.collision (100): 1 = instant crash (ARCHITECTURE §3).
            damageable.ApplyDamage(1f, DamageSource.Collision);
        }

        private static Sprite CreateFallbackSprite() {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply(false);
            tex.name = "PlayerHQ_fallback";
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
