using System;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Weapons;
using Biplanes.Player;
using Biplanes.Fx;

namespace Biplanes.Enemies {

    /// <summary>
    /// Enemy fighter airplane AI — fast, aggressive interceptor.
    ///
    /// • Flies left at a set speed (architecturally: faster than BomberAI).
    /// • Supports movement patterns from the mission JSON: static, straight, sine, dive, escort.
    /// • Direct-hit damage model per GDD: "fighters take direct hits only" — one player bullet kills it.
    ///   Health loaded from Balance.json.enemyHealth.fighter.
    ///
    /// Spawned by MissionRunner from mission JSON via the static factory Spawn(position).
    /// </summary>
    public class FighterAI : MonoBehaviour, IDamageable {

        // ── Serialized tunables (set from mission JSON at runtime) ────────

        [SerializeField] private float speed = 3f;                          // world units / sec
        [SerializeField] private string pattern = "straight";              // static|straight|sine|dive|escort
        [SerializeField] private string escortOf = "";                     // name/id to follow
        [SerializeField] private float sineAmplitude = 2f;                 // sine wave amplitude
        [SerializeField] private float sineSpeed = 2f;                     // sine wave frequency

        // ── Cached components (populated in Awake / Start) ──────────────

        private Health _health;
        private Rigidbody2D _rb;
        private SpriteRenderer _sr;
        private TeamTag _teamTag;

        // ── Runtime state ───────────────────────────────────────────────

        private Transform _escortTarget;                                  // cached escort target
        private float _driftPhase;                                        // sine wave phase offset
        private float _originalY;                                         // starting y for dive pattern

        public Team Team { get; private set; }
        public bool IsDead => _health != null && _health.IsDead;

        // ── Lifecycle ───────────────────────────────────────────────────

        void Awake() {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) {
                _rb = gameObject.AddComponent<Rigidbody2D>();
                _rb.bodyType = RigidbodyType2D.Kinematic;
            }

            _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) {
                _sr = gameObject.AddComponent<SpriteRenderer>();
            }

            _health = GetComponent<Health>();
            if (_health == null) {
                _health = gameObject.AddComponent<Health>();
            }
            var balance = GameBalance.Get();
            _health.SetMaxHitPoints(balance != null && balance.enemyHealth != null
                ? balance.enemyHealth.fighter : 25f);
            // Flight speed from Balance enemySpeed.fighter; keeps the serialized 3 if missing.
            if (balance != null && balance.enemySpeed != null && balance.enemySpeed.fighter > 0f) {
                speed = balance.enemySpeed.fighter;
            }

            _teamTag = GetComponent<TeamTag>();
        }

        void Start() {
            // Ensure team tag is set.
            if (_teamTag == null) {
                _teamTag = gameObject.AddComponent<TeamTag>();
            }
            _teamTag.team = Team.Enemy;
            Team = Team.Enemy;

            // Zero damage on Start (full health).
            _health.ApplyDamage(0f, DamageSource.Bullet);

            // Load fighter sprite.
            var sprite = Resources.Load<Sprite>("Sprites/enemies/enemy_fighter");
            if (sprite != null) {
                _sr.sprite = sprite;
            } else {
                Debug.LogWarning($"FighterAI: 'Sprites/enemies/enemy_fighter' not found — "
                    + "using 1×1 white placeholder.");
                _sr.sprite = CreateFallbackSprite();
            }
            // Aircraft sprites face right (ART_STYLE); fighters fly left, so mirror them.
            _sr.flipX = true;
            _sr.sortingOrder = 5; // ARCHITECTURE §3 draw order: enemies

            // Resolve escort target once at start (no per-frame Find* calls).
            if (!string.IsNullOrEmpty(escortOf)) {
                var allTags = FindObjectsOfType<TeamTag>();
                foreach (var tag in allTags) {
                    if (tag.gameObject.name.IndexOf(escortOf, StringComparison.OrdinalIgnoreCase) >= 0) {
                        _escortTarget = tag.transform;
                        break;
                    }
                }
            }

            // Store original y for dive pattern.
            _originalY = transform.position.y;

            // Randomise sine phase.
            _driftPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        }

        void FixedUpdate() {
            if (_health != null && _health.IsDead) {
                // Health already raised EntityDestroyed (score + objectives). Burst and remove.
                FxManager.Instance.TriggerExplosion(transform.position);
                Destroy(gameObject);
                return;
            }
            if (_rb == null) return;

            Vector3 pos = transform.position;

            switch (pattern) {
                case "static":
                    // Stay still — MissionRunner places it, camera scrolls past.
                    break;

                case "straight":
                    pos.x -= speed * Time.fixedDeltaTime;
                    _rb.MovePosition(pos);
                    break;

                case "sine":
                    float sineOffset = Mathf.Sin(Time.time * sineSpeed + _driftPhase)
                                       * sineAmplitude;
                    pos.y = _originalY + sineOffset;
                    pos.x -= speed * Time.fixedDeltaTime;
                    _rb.MovePosition(pos);
                    break;

                case "dive":
                    // Dive: move left and gradually descend toward ground.
                    float diveFactor = Mathf.PingPong(Time.time * 0.5f, 1f);
                    pos.y = Mathf.Lerp(_originalY, -3.5f, diveFactor);
                    pos.x -= speed * Time.fixedDeltaTime;
                    _rb.MovePosition(pos);
                    break;

                case "escort":
                    if (_escortTarget != null) {
                        float t = Mathf.Min(Time.fixedDeltaTime * speed, 1f);
                        pos.x = Mathf.Lerp(pos.x, _escortTarget.position.x, t * 0.3f);
                        pos.y = Mathf.Lerp(pos.y, _escortTarget.position.y, t * 0.3f);
                        _rb.MovePosition(pos);
                    } else {
                        // No escort target found — fall back to straight flight.
                        pos.x -= speed * Time.fixedDeltaTime;
                        _rb.MovePosition(pos);
                    }
                    break;

                default:
                    // Unknown pattern — treat as straight.
                    pos.x -= speed * Time.fixedDeltaTime;
                    _rb.MovePosition(pos);
                    break;
            }
        }

        /// <summary>
        /// Sets the movement pattern and escort id from the mission spawn entry. Call right
        /// after Spawn (before Start). Empty or null values keep the defaults.
        /// </summary>
        public void Configure(string movePattern, string escortId) {
            if (!string.IsNullOrEmpty(movePattern)) pattern = movePattern;
            escortOf = escortId ?? "";
        }

        // ── IDamageable ───────────────────────────────────────────────────

        public void ApplyDamage(float percent, DamageSource src) {
            if (_health != null && !IsDead) {
                _health.ApplyDamage(percent, src);
            }
        }

        public void Heal(float amount) {
            if (_health != null) {
                _health.Heal(amount);
            }
        }

        // ── Static factory (for MissionRunner) ───────────────────────────

        /// <summary>
        /// Create a fighter airplane at `position` and return the FighterAI component.
        /// The GameObject is built from code with all required components attached.
        /// </summary>
        public static FighterAI Spawn(Vector3 position) {
            // Health raises EntityDestroyed with the GameObject name, and ScoreKeeper and
            // ObjectiveTracker look it up by the mission type key ("fighter").
            var go = new GameObject("fighter");
            go.transform.position = position;

            // Kinematic Rigidbody2D (ARCHITECTURE §3 requirement).
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            // Trigger collider.
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.5f, 1f);

            // SpriteRenderer (sprite loaded at Start from Resources).
            go.AddComponent<SpriteRenderer>();

            // Team tag.
            var teamTag = go.AddComponent<TeamTag>();
            teamTag.team = Team.Enemy;

            // Health component.
            go.AddComponent<Health>();

            FighterAI fighter = go.AddComponent<FighterAI>();
            return fighter;
        }

        /// <summary>Creates a minimal 1×1 white Sprite — never crash on missing assets.</summary>
        private static Sprite CreateFallbackSprite() {
            Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply(false);
            tex.name = "FighterAI_fallback";
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
        }
    }
}
