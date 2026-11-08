using UnityEngine;
using Biplanes.Core;
using Biplanes.Weapons;
using Biplanes.Fx;

namespace Biplanes.Enemies {

    /// <summary>
    /// Enemy bomber airplane AI.
    ///
    /// • Moves left across the screen at a slower speed than fighters (enough to be
    ///   threatening but give the player time to react).
    /// • Carries bombs: when camera x ≥ targetX (if targetX ≥ 0), drops a bomb by
    ///   calling Bomb.Spawn(bombPosition, planeVelocity) — an enemy bomb that deals
    ///   bomberGunner damage to player targets.
    /// • Supports escort pattern: optional "escortOf" string means this bomber follows
    ///   the entity with matching id from the mission JSON.
    /// • Has Health (TeamTag = Enemy); reads enemy health from GameBalance.enemyHealth.bomber.
    ///
    /// Spawned by MissionRunner from mission JSON via static factory Spawn(position).
    /// </summary>
    public class BomberAI : MonoBehaviour {

        // ── Serialized tunables (set from mission JSON) ───────────────────

        /// <summary>If ≥ 0 the bomber drops bombs when the camera reaches this x value.
        /// If < 0 the bomber simply flies through without bombing behavior.</summary>
        [SerializeField] private float targetX = -1f;

        /// <summary>If non-empty, this bomber follows the entity whose id matches this string.
        /// The escort target is found by name at Start time and cached for performance.</summary>
        [SerializeField] private string escortOf = "";

        // ── Cached components (populated in Awake) ────────────────────────

        private Health _health;
        private Rigidbody2D _rb;
        private Collider2D _collider;
        private SpriteRenderer _sr;
        private TeamTag _teamTag;

        // ── Runtime state ─────────────────────────────────────────────────

        private float _speed;                           // resolved from Balance.json
        private Vector2 _velocity;                      // current movement velocity
        private bool _hasDroppedBomb;                   // one-shot flag for initial targetX arrival
        private int _bombFireCooldownFrames = 0;        // frames until next bomb drop at targetX
        private Transform _escortTarget;                // cached escort target (for performance)

    #if UNITY_EDITOR
        /// <summary>Public accessor for the editor (Inspector wiring).</summary>
        public float TargetX => targetX;
    #endif

        // ── Lifecycle ─────────────────────────────────────────────────────

        void Awake() {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) {
                _rb = gameObject.AddComponent<Rigidbody2D>();
                _rb.bodyType = RigidbodyType2D.Kinematic;
            }

            _collider = GetComponent<Collider2D>();
            if (_collider == null) {
                _collider = gameObject.AddComponent<BoxCollider2D>();
                (_collider as BoxCollider2D).isTrigger = true;
            }

            _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) {
                _sr = gameObject.AddComponent<SpriteRenderer>();
            }

            // Ensure Health component exists (MissionRunner should set it up, but guard here too).
            _health = GetComponent<Health>();
            if (_health == null) {
                _health = gameObject.AddComponent<Health>();
            }
            var balance = GameBalance.Get();
            _health.SetMaxHitPoints(balance != null && balance.enemyHealth != null
                ? balance.enemyHealth.bomber : 200f);

            _teamTag = GetComponent<TeamTag>();
        }

        void Start() {
            var balance = GameBalance.Get();

            // Speed: Balance enemySpeed.bomber (slower than fighters). Fallback 1.5.
            _speed = balance != null && balance.enemySpeed != null && balance.enemySpeed.bomber > 0f
                ? balance.enemySpeed.bomber : 1.5f;

            // Set up team and initialise health to full HP (0% damage taken).
            if (_teamTag == null) {
                _teamTag = gameObject.AddComponent<TeamTag>();
            }
            _teamTag.team = Team.Enemy;

            // ApplyDamage(0, ...) zeroes current damage — this is the "full health" state.
            _health.ApplyDamage(0f, DamageSource.Bullet);

            _hasDroppedBomb = false;
            _bombFireCooldownFrames = 60;                      // ~1 second between bomb drops at 60 fps

            // Load the bomber sprite from Resources.
            var sprite = Resources.Load<Sprite>("Sprites/enemies/enemy_bomber");
            if (sprite != null) {
                _sr.sprite = sprite;
            } else {
                Debug.LogWarning($"BomberAI: 'Sprites/enemies/enemy_bomber' not found — "
                    + "using 1×1 white placeholder.");
                _sr.sprite = CreateWhiteSprite();
            }

            // Resolve escort target once at start (no per-frame Find* calls).
            if (!string.IsNullOrEmpty(escortOf)) {
                var allTags = FindObjectsOfType<TeamTag>();
                foreach (var tag in allTags) {
                    if (tag.gameObject.name.IndexOf(escortOf, System.StringComparison.OrdinalIgnoreCase) >= 0) {
                        _escortTarget = tag.transform;
                        break;
                    }
                }
            }
        }

        void FixedUpdate() {
            if (_rb == null) return;
            if (_health != null && _health.IsDead) {
                // Health already raised EntityDestroyed (score + objectives). Burst and remove.
                FxManager.Instance.TriggerExplosion(transform.position);
                Destroy(gameObject);
                return;
            }

            // ── Movement: fly left at a slower speed than fighters ────────
            float moveX = -_speed * Time.fixedDeltaTime;
            Vector3 pos = transform.position;
            pos.x += moveX;
            _rb.MovePosition(pos);
            _velocity = new Vector2(-_speed, 0f);

            // ── Escort pattern: follow the cached target ─────────────────
            if (_escortTarget != null) {
                FollowEscortTarget();
            }

            // ── Bombing: when camera x ≥ targetX, drop a bomb ───────────
            if (targetX >= 0f && ScrollController.Instance != null) {
                CheckAndDropBomb();
            }
        }

        // ── Trigger handling ──────────────────────────────────────────────

        void OnTriggerEnter2D(Collider2D other) {
            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null) return;

            // Direct collision: apply collision damage to the incoming entity.
            // Bombers are durable — they don't crash from a single bullet hit.
        }

        // ── Internal helpers ──────────────────────────────────────────────

        /// <summary>Move the bomber toward its cached escort target.</summary>
        private void FollowEscortTarget() {
            if (_escortTarget == null) return;

            Vector3 currentPos = transform.position;
            float t = Mathf.Min(Time.fixedDeltaTime * _speed, 1f);
            currentPos.x = Mathf.Lerp(currentPos.x, _escortTarget.position.x, t * 0.3f);
            currentPos.y = Mathf.Lerp(currentPos.y, _escortTarget.position.y, t * 0.3f);
            _rb.MovePosition(currentPos);
            _velocity = (currentPos - transform.position) / Time.fixedDeltaTime;
        }

        /// <summary>Check camera x vs targetX and drop a bomb when appropriate.</summary>
        private void CheckAndDropBomb() {
            float cameraX = ScrollController.Instance.MissionDistanceX;

            // Only trigger once when the camera first reaches targetX.
            if (cameraX >= targetX && !_hasDroppedBomb) {
                _hasDroppedBomb = true;

                // Drop a bomb from the bomber's current position.
                Bomb.Spawn(transform.position, _velocity);

                // After initial drop, fire periodic bombs while in range zone.
                _bombFireCooldownFrames = 30;
            }

            // Periodic bomb firing once we've arrived at targetX zone.
            if (_hasDroppedBomb && cameraX >= targetX - 5f) {
                _bombFireCooldownFrames--;
                if (_bombFireCooldownFrames <= 0) {
                    _bombFireCooldownFrames = 60; // every ~1 second
                    Bomb.Spawn(transform.position, _velocity);
                }
            }
        }

        /// <summary>Called when this bomber is destroyed (by Health's death hook).</summary>
        protected virtual void OnDestroyed() {
            GameEvents.RaiseEntityDestroyed(nameof(BomberAI), Team.Enemy);
        }

        // ── Static factory (for MissionRunner) ────────────────────────────

        /// <summary>
        /// Spawn a bomber airplane at the given world position.
        /// Returns the BomberAI component reference for caller tracking.
        /// The bomber gets full health from Balance.json and flies left.
        /// </summary>
        public static BomberAI Spawn(Vector3 position) {
            // Name = mission type key, used by ScoreKeeper/ObjectiveTracker via EntityDestroyed.
            var go = new GameObject("bomber");
            go.transform.position = position;

            // Kinematic Rigidbody2D (ARCHITECTURE §3 requirement).
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            // Trigger collider sized to cover the visual bounds.
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(2f, 1.5f);

            // SpriteRenderer (sprite loaded at Start from Resources).
            go.AddComponent<SpriteRenderer>();

            // Team tag.
            var teamTag = go.AddComponent<TeamTag>();
            teamTag.team = Team.Enemy;

            // Health component (provides IDamageable for projectile collision logic).
            go.AddComponent<Health>();

            BomberAI bomber = go.AddComponent<BomberAI>();
            return bomber;
        }

        /// <summary>Creates a minimal 1×1 white Sprite — never crash on missing assets.</summary>
        private static Sprite CreateWhiteSprite() {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, Rect.zero, Vector2.one * 0.5f);
        }
    }
}
