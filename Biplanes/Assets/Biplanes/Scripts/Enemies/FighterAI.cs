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
    /// • Shoots at the player (B023): an EnemyBullet (DamageSource.Bullet, scaled by
    ///   damageTaken.fighterBullet) only while the fighter is on screen, the player is alive,
    ///   and the player is within enemyFire.range and fighterConeDegrees of its flight
    ///   direction (left). Cadence and the random first-shot delay come from Balance
    ///   enemyFire; the cooldown runs only on screen.
    /// • Pitch (GDD Flight attitude, B029): with the player in that cone and range, the nose
    ///   eases toward the bearing to the player (pitch.easeDegreesPerSecond, clamped to
    ///   pitch.fighterMaxDegrees); otherwise it follows the climb/dive (pitch.maxDegrees).
    ///   Shots fly ALONG THE NOSE (± enemyFire.spreadDegrees) and leave from it, and the
    ///   fighter only fires while the player is within enemyFire.aimToleranceDegrees of the
    ///   nose. The body turns with Rigidbody2D.MoveRotation, so the hitbox turns too.
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

        // ── Pitch tracking ───────────────────────────────────────────────
        private float _prevY;          // Y position from previous FixedUpdate
        private float _pitchAngle;     // Current pitch angle (degrees, nose up positive)
        private PitchData _pitch;      // resolved Balance pitch block

        /// <summary>Fighters always fly left: their right-facing art is mirrored (flipX).</summary>
        private const int Facing = -1;

        // ── Runtime state ───────────────────────────────────────────────

        private Transform _escortTarget;                                  // cached escort target
        private float _driftPhase;                                        // sine wave phase offset
        private float _originalY;                                         // starting y for dive pattern

        // ── Gun (B023) ──────────────────────────────────────────────────

        /// <summary>Source of a fighter's shots; the player's Health maps it to damageTaken.fighterBullet.</summary>
        public const DamageSource ShotSource = DamageSource.Bullet;

        /// <summary>Fighters fly left, so "ahead" is -x.</summary>
        private static readonly Vector2 Forward = Vector2.left;

        /// <summary>Muzzle distance ahead of the centre: half the 1.5-unit hitbox, so the round
        /// leaves from the nose. Sprite geometry, not a gameplay tunable.</summary>
        private const float MuzzleOffset = 0.75f;

        /// <summary>Current pitch in degrees, nose up positive (B029).</summary>
        public float PitchAngle => _pitchAngle;

        /// <summary>The z rotation (degrees) the body is turned to with MoveRotation: -PitchAngle,
        /// because the sprite is mirrored to fly left.</summary>
        public float BodyAngle => PlanePitch.BodyAngle(_pitchAngle, Facing);

        /// <summary>World unit vector of the drawn nose at the current pitch: (-cos p, sin p).
        /// Shots leave along it (± spread).</summary>
        public Vector2 NoseDirection => PlanePitch.NoseDirection(_pitchAngle, Facing);

        private EnemyFireData _fire;
        private float _fireCooldown;                                      // seconds on screen until the next shot
        private Camera _cam;
        private PlayerPlane _player;
        private Health _playerHealth;

        /// <summary>The player this fighter shoots at. Found once in Start when not set.</summary>
        internal PlayerPlane PlayerRef {
            get => _player;
            set {
                _player = value;
                _playerHealth = value != null ? value.GetComponent<Health>() : null;
            }
        }

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

            _fire = EnemyFireData.Resolve(balance);
            _fireCooldown = UnityEngine.Random.Range(_fire.firstShotDelayMin, _fire.firstShotDelayMax);

            // Initialise pitch tracking.
            _pitch = PitchData.Resolve(balance);
            _prevY = transform.position.y;
            _pitchAngle = 0f;
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
            _escortTarget = FindEscortTarget(escortOf);

            // Store original y for dive pattern.
            _originalY = transform.position.y;

            // Randomise sine phase.
            _driftPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            // Gun targets, resolved once (no per-frame Find* calls).
            _cam = Camera.main;
            if (_player == null) PlayerRef = FindFirstObjectByType<PlayerPlane>();
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

            // ----- Pitch: aim at the player in the cone, else follow the climb/dive -----
            float verticalVelocity = (pos.y - _prevY) / Time.fixedDeltaTime;
            _prevY = pos.y;
            UpdatePitch(verticalVelocity, Time.fixedDeltaTime);
            _rb.MoveRotation(BodyAngle);

            TickFire(Time.fixedDeltaTime);
        }

        /// <summary>
        /// Eases the pitch by one tick of `dt` seconds. With a live player inside the firing
        /// cone and range, the nose turns toward the bearing to the player (limit
        /// pitch.fighterMaxDegrees); otherwise toward the flight attitude for
        /// `verticalVelocity` (limit pitch.maxDegrees). Rate: pitch.easeDegreesPerSecond.
        /// </summary>
        internal void UpdatePitch(float verticalVelocity, float dt) {
            if (_pitch == null) _pitch = PitchData.Resolve(GameBalance.Get());
            if (TryGetAimPitch(out float aimPitch)) {
                _pitchAngle = PlanePitch.EaseToward(_pitchAngle, aimPitch, _pitch.fighterMaxDegrees,
                    _pitch.easeDegreesPerSecond, dt);
            } else {
                // Coming back from an aim pitch beyond maxDegrees: ease down at the same rate.
                float target = PlanePitch.TargetFromVerticalSpeed(verticalVelocity, _pitch.maxDegrees,
                    _pitch.fullPitchSpeed);
                float limit = Mathf.Max(_pitch.maxDegrees, Mathf.Abs(_pitchAngle));
                _pitchAngle = PlanePitch.EaseToward(_pitchAngle, target, limit, _pitch.easeDegreesPerSecond, dt);
            }
        }

        /// <summary>True when a live player is inside the firing cone and range; `pitch` is
        /// then the bearing to the player as a pitch (nose up positive).</summary>
        private bool TryGetAimPitch(out float pitch) {
            pitch = 0f;
            if (IsDead || _fire == null || !PlayerTargetable()) return false;
            Vector2 from = transform.position;
            Vector2 target = _player.transform.position;
            if (!EnemyBullet.InFiringCone(from, target, Forward, _fire.fighterConeDegrees, _fire.range)) return false;
            pitch = PlanePitch.PitchToward(target - from);
            return true;
        }

        /// <summary>A player exists, hasn't crashed and isn't dead.</summary>
        private bool PlayerTargetable() {
            return _player != null && !_player.HasCrashed && (_playerHealth == null || !_playerHealth.IsDead);
        }

        // ── Gun (B023) ──────────────────────────────────────────────────

        /// <summary>
        /// Advances the gun by `dt` seconds and fires one EnemyBullet when it may. The cooldown
        /// only runs while the fighter is on screen and the player is alive; once it has run out
        /// the fighter fires as soon as the player is in its forward cone, in range, and within
        /// enemyFire.aimToleranceDegrees of the nose. The shot flies along the nose
        /// ± spreadDegrees and leaves from it; TickFire never changes the pitch (UpdatePitch
        /// does). Returns true when a shot was fired. Dead fighters never fire.
        /// </summary>
        internal bool TickFire(float dt) {
            if (IsDead || _fire == null) return false;
            if (!PlayerTargetable()) return false;

            Vector2 from = transform.position;
            if (!EnemyBullet.IsOnScreen(_cam, from, 0f)) return false;

            _fireCooldown -= dt;
            if (_fireCooldown > 0f) return false;

            Vector2 target = _player.transform.position;
            if (!EnemyBullet.InFiringCone(from, target, Forward, _fire.fighterConeDegrees, _fire.range)) return false;

            // Only fire when the nose is on the player (B029).
            Vector2 nose = NoseDirection;
            Vector2 toPlayer = target - from;
            if (toPlayer.sqrMagnitude > 0.0001f && Vector2.Angle(nose, toPlayer) > _fire.aimToleranceDegrees) return false;

            // Along the nose ± spread (180 = no cone clamp; the nose is already inside the cone).
            Vector2 dir = EnemyBullet.AimWithinCone(from, from + nose, nose, 180f, _fire.spreadDegrees);

            // Fire from the nose.
            Vector3 muzzle = transform.position + (Vector3)(nose * MuzzleOffset);
            // Pool exhausted: keep the expired cooldown and try again next tick.
            if (EnemyBullet.Spawn(muzzle, dir, ShotSource) == null) return false;

            _fireCooldown = UnityEngine.Random.Range(_fire.fighterIntervalMin, _fire.fighterIntervalMax);
            return true;
        }

        /// <summary>
        /// Sets the movement pattern and escort id from the mission spawn entry. Call right
        /// after Spawn (before Start). Empty or null values keep the defaults.
        /// </summary>
        public void Configure(string movePattern, string escortId) {
            if (!string.IsNullOrEmpty(movePattern)) pattern = movePattern;
            escortOf = escortId ?? "";
        }

        // ── Escort lookup (B020) ──────────────────────────────────────────

        /// <summary>
        /// True when `go` is the entity with mission spawn id `id` (ARCHITECTURE §5 escortOf).
        /// Whole ids only: the name must carry the exact "[id]" token (ZeppelinAI, EnemyBase,
        /// PlayerHQ name themselves "type [id]"), or the object is a BomberAI spawned with that
        /// id (its name stays "bomber" because Health raises EntityDestroyed with the name).
        /// So "zep" never matches "zeppelinTurret" or "zeppelin [zep2]".
        /// </summary>
        public static bool IsEscortTarget(GameObject go, string id) {
            if (go == null || string.IsNullOrEmpty(id)) return false;
            if (go.name.IndexOf("[" + id + "]", StringComparison.Ordinal) >= 0) return true;
            var bomber = go.GetComponent<BomberAI>();
            return bomber != null && bomber.SpawnId == id;
        }

        /// <summary>The live Team.Enemy-tagged entity whose spawn id is `id`, or null. Runs a
        /// scene search, so call it once (Start), never per frame.</summary>
        public static Transform FindEscortTarget(string id) {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var tag in FindObjectsOfType<TeamTag>()) {
                if (IsEscortTarget(tag.gameObject, id)) return tag.transform;
            }
            return null;
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
