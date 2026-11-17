using UnityEngine;
using Biplanes.Audio;
using Biplanes.Core;
using Biplanes.Weapons;
using Biplanes.Fx;
using Biplanes.Player;

namespace Biplanes.Enemies {

    /// <summary>
    /// Enemy bomber airplane AI.
    ///
    /// • Moves left across the screen at a slower speed than fighters (enough to be
    ///   threatening but give the player time to react).
    /// • targetX (B018): the WORLD x the bomber flies (left) toward, e.g. the player's
    ///   airfield; -1 = none. MissionRunner sets it with SetTargetX from the destroyById
    ///   objective's failAtX and loses the mission when the bomber's own x reaches it — the
    ///   camera position plays no part. The bomber never despawns, so it always gets there.
    /// • Carries bombs: once its own x is within BombRunLeadX of targetX it starts its bomb
    ///   run, calling EnemyBomb.Spawn(bombPosition, planeVelocity) — a Team.Enemy bomb from
    ///   its own pool (never the player's Bomb pool) that only damages the player.
    /// • Supports escort pattern: optional "escortOf" string means this bomber follows
    ///   the entity with matching id from the mission JSON.
    /// • Has Health (TeamTag = Enemy); reads enemy health from GameBalance.enemyHealth.bomber.
    /// • Has a gunner (B023): an occasional EnemyBullet aimed at the player in any direction,
    ///   only while the bomber is on screen, the player is alive and within enemyFire.range.
    ///   Hits report DamageSource.Bomb, which the player's Health maps to
    ///   damageTaken.bomberGunner. Cadence from Balance enemyFire.bomberInterval*. The gunner
    ///   never changes the flight path or the bomb run.
    ///
    /// Spawned by MissionRunner from mission JSON via static factory Spawn(position).
    /// </summary>
    public class BomberAI : MonoBehaviour {

        // ── Serialized tunables (set from mission JSON) ───────────────────

        /// <summary>World x the bomber flies toward (B018). If ≥ 0 it starts bombing
        /// BombRunLeadX before it; if &lt; 0 it simply flies through without bombing.</summary>
        [SerializeField] private float targetX = -1f;

        /// <summary>World units before targetX at which the bomb run (first drop, then one
        /// every ~1 s) starts.</summary>
        private const float BombRunLeadX = 5f;

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
        // ── Pitch (GDD Flight attitude, T942/B029) ───────────────────────
        private float _pitchAngle = 0f;                  // current nose pitch in degrees, nose up +
        private PitchData _pitch;                        // resolved Balance pitch block
        private int _bombFireCooldownFrames = 0;        // frames until next bomb drop at targetX
        private Transform _escortTarget;                // cached escort target (for performance)

        /// <summary>Mission spawn id ("" when none). FighterAI escortOf finds the bomber by it
        /// (B020); the GameObject name stays the "bomber" type key.</summary>
        public string SpawnId { get; private set; } = "";

        // ── Gunner (B023) ─────────────────────────────────────────────────

        /// <summary>Source of the gunner's shots. Health maps DamageSource.Bomb to
        /// damageTaken.bomberGunner (every bomber attack scales the same way), so no new
        /// DamageSource value is needed.</summary>
        public const DamageSource GunnerShotSource = DamageSource.Bomb;

        /// <summary>The gunner swivels: any direction (half-angle 180) toward the player.</summary>
        private const float GunnerConeDegrees = 180f;

        private EnemyFireData _fire;
        private float _gunnerCooldown;                  // seconds on screen until the next shot
        private Camera _cam;
        private PlayerPlane _player;
        private Health _playerHealth;

        /// <summary>The player the gunner shoots at. Found once in Start when not set.</summary>
        internal PlayerPlane PlayerRef {
            get => _player;
            set {
                _player = value;
                _playerHealth = value != null ? value.GetComponent<Health>() : null;
            }
        }

        /// <summary>World x the bomber flies toward; -1 when none.</summary>
        public float TargetX => targetX;

        /// <summary>+1 = art as drawn (flies right), -1 = mirrored to fly left (FlipsToFlyLeft).</summary>
        public static int Facing => FlipsToFlyLeft ? -1 : 1;

        /// <summary>Current pitch in degrees, nose up positive.</summary>
        public float PitchAngle => _pitchAngle;

        /// <summary>The z rotation (degrees) the body is turned to with MoveRotation: PitchAngle
        /// mirrored by Facing, so a climbing bomber's drawn nose points up.</summary>
        public float BodyAngle => PlanePitch.BodyAngle(_pitchAngle, Facing);

        /// <summary>World unit vector of the drawn nose at the current pitch.</summary>
        public Vector2 NoseDirection => PlanePitch.NoseDirection(_pitchAngle, Facing);

        /// <summary>True when a targetX (≥ 0) is set.</summary>
        public bool HasTargetX => targetX >= 0f;

        /// <summary>True once the bomber's world x is at or past its targetX (flying left).</summary>
        public bool ReachedTarget => HasTargetX && transform.position.x <= targetX;

        /// <summary>Set the world x the bomber flies toward (negative = none). Called by
        /// MissionRunner right after Spawn (B018).</summary>
        public void SetTargetX(float x) {
            targetX = x >= 0f ? x : -1f;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────

        void Awake() {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) {
                _rb = gameObject.AddComponent<Rigidbody2D>();
                _rb.bodyType = RigidbodyType2D.Kinematic;
            }

            _collider = GetComponent<Collider2D>();
            if (_collider == null) {
                // Fallback for a bomber not built through Spawn: same hitboxes as Spawn.
                AddHitboxes(gameObject);
                _collider = GetComponent<Collider2D>();
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

            _fire = EnemyFireData.Resolve(balance);
            _gunnerCooldown = Random.Range(_fire.firstShotDelayMin, _fire.firstShotDelayMax);
            _pitch = PitchData.Resolve(balance);
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
            // Aircraft sprites face right (ART_STYLE); bombers fly left, so mirror them.
            // The hitboxes are already mirrored to match (see AddHitboxes / FlipsToFlyLeft).
            _sr.flipX = FlipsToFlyLeft;
            _sr.sortingOrder = 5; // ARCHITECTURE §3 draw order: enemies

            // Resolve escort target once at start (no per-frame Find* calls).
            // Whole-id match (B020), shared with FighterAI.
            _escortTarget = FighterAI.FindEscortTarget(escortOf);

            // Gunner targets, resolved once (no per-frame Find* calls).
            _cam = Camera.main;
            if (_player == null) PlayerRef = FindFirstObjectByType<PlayerPlane>();
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

            // ── Pitch: nose follows the climb/dive; the body (sprite and hitboxes) turns
            // through the Rigidbody2D, mirrored by the facing (B029) ─────
            if (_pitch == null) _pitch = PitchData.Resolve(GameBalance.Get());
            _pitchAngle = PlanePitch.Step(_pitchAngle, _velocity.y, _pitch, Time.fixedDeltaTime);
            _rb.MoveRotation(BodyAngle);

            // ── Bombing: bomb run once the bomber nears its targetX ──────
            if (HasTargetX) {
                CheckAndDropBomb();
            }

            // ── Gunner: independent of movement and the bomb run ─────────
            TickGunner(Time.fixedDeltaTime);
        }

        /// <summary>
        /// Advances the gunner by `dt` seconds and fires one EnemyBullet at the player when it
        /// may. The cooldown only runs while the bomber is on screen and the player is alive;
        /// once it has run out the gunner fires as soon as the player is in range.
        /// Returns true when a shot was fired. A dead bomber never fires.
        /// </summary>
        internal bool TickGunner(float dt) {
            if (_fire == null || (_health != null && _health.IsDead)) return false;
            if (_player == null || _player.HasCrashed || (_playerHealth != null && _playerHealth.IsDead)) return false;

            Vector2 from = transform.position;
            if (!EnemyBullet.IsOnScreen(_cam, from, 0f)) return false;

            _gunnerCooldown -= dt;
            if (_gunnerCooldown > 0f) return false;

            Vector2 target = _player.transform.position;
            if (!EnemyBullet.InFiringCone(from, target, Vector2.left, GunnerConeDegrees, _fire.range)) return false;

            Vector2 dir = EnemyBullet.AimWithinCone(from, target, Vector2.left,
                GunnerConeDegrees, _fire.spreadDegrees);
            // Pool exhausted: keep the expired cooldown and try again next tick.
            if (EnemyBullet.Spawn(transform.position, dir, GunnerShotSource) == null) return false;

            // EnemyGun cue once per bullet fired, at the shooter (T950).
            var audio = AudioManager.GetOrNull();
            if (audio != null) audio.Play(SfxId.EnemyGun, transform.position);

            _gunnerCooldown = Random.Range(_fire.bomberIntervalMin, _fire.bomberIntervalMax);
            return true;
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

        /// <summary>Check the bomber's own x vs targetX and drop a bomb when appropriate.</summary>
        private void CheckAndDropBomb() {
            float x = transform.position.x;

            // First drop when the bomber comes within BombRunLeadX of its target.
            if (!_hasDroppedBomb && x <= targetX + BombRunLeadX) {
                _hasDroppedBomb = true;

                // Drop a bomb from the bomber's current position.
                EnemyBomb.Spawn(transform.position, _velocity);

                // After initial drop, fire periodic bombs for the rest of the run.
                _bombFireCooldownFrames = 30;
                return;
            }

            // Periodic bomb firing once the bomb run has started.
            if (_hasDroppedBomb) {
                _bombFireCooldownFrames--;
                if (_bombFireCooldownFrames <= 0) {
                    _bombFireCooldownFrames = 60; // every ~1 second
                    EnemyBomb.Spawn(transform.position, _velocity);
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
        public static BomberAI Spawn(Vector3 position, string id = null) {
            // Name = mission type key, used by ScoreKeeper/ObjectiveTracker via EntityDestroyed
            // (Health raises it with gameObject.name), so the spawn id lives in SpawnId instead.
            var go = new GameObject("bomber");
            go.transform.position = position;

            // Kinematic Rigidbody2D (ARCHITECTURE §3 requirement).
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            // Trigger hitboxes fitted to the enemy_bomber sprite (fuselage, wing block, tail fin).
            AddHitboxes(go);

            // SpriteRenderer (sprite loaded at Start from Resources).
            go.AddComponent<SpriteRenderer>();

            // Team tag.
            var teamTag = go.AddComponent<TeamTag>();
            teamTag.team = Team.Enemy;

            // Health component (provides IDamageable for projectile collision logic).
            go.AddComponent<Health>();

            BomberAI bomber = go.AddComponent<BomberAI>();
            bomber.SpawnId = id ?? "";
            return bomber;
        }

        // ── Hitboxes (B012) ───────────────────────────────────────────────
        // Fitted to Sprites/enemies/enemy_bomber.png: 320×140 px, 100 ppu, pivot at the
        // centre, transform scale 1, art facing RIGHT. Local units = (px - 160) / 100 in x
        // and (70 - py) / 100 in y (image y is down). Each box is inset a few px inside
        // the opaque ink so near-misses don't count; the empty corners (above the
        // tail-to-wing gap, below the tail, above the engine cowling) carry no box.
        // Collider geometry is sprite data, not a gameplay tunable, so it lives here
        // rather than in Balance.json.

        /// <summary>The bomber always flies left, so its right-facing art is mirrored with
        /// SpriteRenderer.flipX and every hitbox x offset is negated to match.</summary>
        private const bool FlipsToFlyLeft = true;

        /// <summary>Fuselage band, px x 40..304, y 58..98 (tail cone to engine nose).</summary>
        private static readonly Vector2 FuselageSize   = new Vector2(2.64f, 0.40f);
        private static readonly Vector2 FuselageOffset = new Vector2(0.12f, -0.08f);

        /// <summary>Stacked wing block with struts and lower wing, px x 194..264, y 6..108.</summary>
        private static readonly Vector2 WingSize   = new Vector2(0.70f, 1.02f);
        private static readonly Vector2 WingOffset = new Vector2(0.69f, 0.13f);

        /// <summary>Tall tail fin, px x 10..56, y 36..86.</summary>
        private static readonly Vector2 TailSize   = new Vector2(0.46f, 0.50f);
        private static readonly Vector2 TailOffset = new Vector2(-1.27f, 0.09f);

        /// <summary>
        /// Adds the bomber's trigger hitboxes to `go`: fuselage, wing block and tail fin,
        /// all on the root object, so every hit resolves to the same Health through
        /// GetComponentInParent. A projectile overlapping two boxes damages once, because
        /// Projectile ignores trigger callbacks after it has been recycled (B012).
        /// </summary>
        private static void AddHitboxes(GameObject go) {
            float sign = FlipsToFlyLeft ? -1f : 1f;
            AddBox(go, FuselageSize, FuselageOffset, sign);
            AddBox(go, WingSize, WingOffset, sign);
            AddBox(go, TailSize, TailOffset, sign);
        }

        private static void AddBox(GameObject go, Vector2 size, Vector2 offset, float xSign) {
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = size;
            col.offset = new Vector2(offset.x * xSign, offset.y);
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
