using UnityEngine;
using Biplanes.Core;
using Biplanes.Audio;

namespace Biplanes.Enemies {

    /// <summary>
    /// Health-balloon pickup: spawns at mission-defined positions, drifts gently in place.
    /// When the player flies through it (any Collider2D with TeamTag == Player), the
    /// player receives a heal of GameBalance.healthBalloonRestore points (100 = full repair,
    /// B062) and the balloon
    /// recycles itself.
    ///
    /// Implements IDamageable so it plugs into the same trigger-collision system as other
    /// entities, but only responds to the Player team — enemy touches are silently ignored.
    /// </summary>
    public class HealthBalloon : MonoBehaviour, IDamageable {

        // ── Constants ─────────────────────────────────────────────────────

        /// <summary>Ink white (#E8F1FA): white-only palette (GDD Style Guide, 2026-11-18).</summary>
        private static readonly Color Ink = new Color(0xE8 / 255f, 0xF1 / 255f, 0xFA / 255f, 1f);

        private const float DRIFT_AMPLITUDE = 0.4f;
        private const float DRIFT_SPEED = 1.2f;

        /// <summary>Heal used when Balance.json can't be read: a full repair (GDD, B062).</summary>
        public const float FallbackRestore = 100f;

        // ── IDamageable contract ──────────────────────────────────────────

        public Team Team => Team.Neutral;

        /// <summary>True once the balloon has been collected and recycled.</summary>
        public bool IsDead { get; private set; }

        void Awake() {
            // Build visual: ink-white sprite via SpriteRenderer.
            var sr = GetComponent<SpriteRenderer>();
            if (sr == null) {
                sr = gameObject.AddComponent<SpriteRenderer>();
                Debug.LogWarning($"HealthBalloon '{name}': missing SpriteRenderer — added one.");
            }

            // Load the pickup sprite from Resources path declared in ART_STYLE.md asset list.
            var sprite = Resources.Load<Sprite>("Sprites/pickups/health_balloon");
            if (sprite != null) {
                sr.sprite = sprite;
            } else {
                // Graceful fallback: a 1×1 white sprite — never crash the game over a missing asset.
                Debug.LogWarning($"HealthBalloon '{name}': 'Sprites/pickups/health_balloon' not found "
                    + "in Resources — using 1×1 white placeholder.");
                sr.sprite = CreateWhiteSprite();
            }

            // White-only palette: no heal-green tint; the cross reads by its bold shape (art).
            sr.color = Ink;

            // Ensure trigger collider exists (collider size set by caller via Spawn).
            var col = GetComponent<Collider2D>();
            if (col == null) {
                col = gameObject.AddComponent<BoxCollider2D>();
                ((BoxCollider2D)col).isTrigger = true;
                ((BoxCollider2D)col).size = new Vector2(1.0f, 1.0f);
            }

            // Cache rb once.
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) {
                _rb = gameObject.AddComponent<Rigidbody2D>();
                _rb.bodyType = RigidbodyType2D.Kinematic;
            }
        }

        // ── Lifetime (per-frame drift animation + pickup check) ───────────

        private Rigidbody2D _rb;
        private float _spawnTime;
        private int _id;                         // unique drift-phase seed per instance

        void Start() {
            IsDead = false;
            _spawnTime = Time.time;
            _id = Random.Range(0, 999);
        }

        void FixedUpdate() {
            if (IsDead) return;
            // Gentle sinusoidal drift: bob up-and-down as it moves.
            var t = (Time.time - _spawnTime) * DRIFT_SPEED + _id;
            float offsetY = Mathf.Sin(t) * DRIFT_AMPLITUDE;
            Vector3 pos = transform.position;
            pos.y += offsetY * Time.fixedDeltaTime * 0.5f; // scaled bob
            _rb.MovePosition(pos);
        }

        // ── Trigger handling: only player team can collect ────────────────

        void OnTriggerEnter2D(Collider2D other) {
            if (IsDead) return;

            var tag = other.GetComponentInParent<TeamTag>();
            if (tag != null && tag.Team == Team.Player) {
                Collect(tag);
            }
        }

        /// <summary>
        /// IDamageable implementation: always no-op.
        /// HealthBalloon does not accumulate damage — it disappears only when the player
        /// overlaps via OnTriggerEnter2D, which triggers the heal and recycles itself.
        /// </summary>
        public void ApplyDamage(float percent, DamageSource src) {
            // Balloons are immune to all damage sources. Healing is handled by
            // the collision overlap system (OnTriggerEnter2D → Collect), not by this method.
        }

        // ── Collection logic ──────────────────────────────────────────────

        private void Collect(TeamTag playerTag) {
            IsDead = true;

            // Restore the player's health through the Heal source (no multiplier, no resistance).
            // Balance healthBalloonRestore is 100: a full repair (B062, GDD 2026-11-20). Health
            // clamps the result at 0% damage, so a balloon taken at full health is still used up.
            var balance = GameBalance.Get();
            float restoreAmount = balance != null ? balance.healthBalloonRestore : FallbackRestore;

            var damageable = playerTag.GetComponentInParent<IDamageable>();
            if (damageable != null) {
                var health = damageable as Health;
                if (health != null) health.ApplyDamage(-restoreAmount, DamageSource.Heal);
            }

            var audio = AudioManager.GetOrNull();
            if (audio != null) audio.Play(SfxId.Pickup, transform.position);

            Recycle();
        }

        /// <summary>Recycle this instance back to the pool.</summary>
        private void Recycle() {
            // Signal destruction so MissionRunner can remove it from tracking lists.
            GameEvents.RaiseEntityDestroyed(nameof(HealthBalloon), Team.Neutral);

            gameObject.SetActive(false);
            // Pool manager should handle actual return; just deactivate here for safety.
        }

        // ── Static factory (for MissionRunner) ────────────────────────────

        /// <summary>
        /// Spawn a HealthBalloon at the given world position, returning it for pool tracking.
        /// Caller should manage pooling externally or store the returned reference.
        /// </summary>
        public static HealthBalloon Spawn(Vector3 position) {
            var go = new GameObject("HealthBalloon");
            go.transform.position = position;

            var balloon = go.AddComponent<HealthBalloon>();

            // Set up trigger collider size (1×1 unit pickup area).
            var col = go.GetComponent<Collider2D>() as BoxCollider2D;
            if (col == null) {
                col = go.AddComponent<BoxCollider2D>();
            }
            col.isTrigger = true;
            ((BoxCollider2D)col).size = new Vector2(1.0f, 1.0f);
            col.offset = Vector2.zero;

            return balloon;
        }

        /// <summary>Creates a minimal 1×1 white Sprite — never crash on missing assets.</summary>
        private static Sprite CreateWhiteSprite() {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, new Color(0xE8 / 255f, 0xF1 / 255f, 0xFA / 255f, 1f)); // ink #E8F1FA
            tex.Apply();
            return Sprite.Create(tex, Rect.zero, Vector2.one * 0.5f);
        }

        /// <summary>IDamageable: a pickup isn't healable, so this is a harmless no-op.</summary>
        public void Heal(float amount) { /* health balloons are not healable */ }
    }
}
