using System;
using System.Collections.Generic;
using UnityEngine;
using Biplanes.Audio;
using Biplanes.Core;
using Biplanes.Weapons;
using Biplanes.Player;
using Biplanes.Fx;
using Biplanes.Missions;

namespace Biplanes.Enemies {

    /// <summary>
    /// Enemy fighter airplane AI — fast, aggressive interceptor.
    ///
    /// • Chases the player (B059, GDD Enemies item 1, ARCHITECTURE §3 "Fighter chase"): it
    ///   faces the player (FlightDirection), turning around with an eased squash over
    ///   fighterChase.turnSeconds once the player is more than fighterChase.turnDeadzone behind
    ///   it; its world x velocity steers toward a point fighterChase.standoff short of the
    ///   player (max enemySpeed.fighter, acceleration fighterChase.accel, braking so it never
    ///   overshoots); it climbs/dives toward the player's height plus a per-fighter offset at up
    ///   to fighterChase.climbRate inside the flight band; fighters closer than
    ///   fighterChase.separation push apart vertically, and inside the standoff box it steers
    ///   vertically out of the player's way. The steering maths are pure static helpers.
    /// • B061 (QA T966): it never flies tail-first: its velocity along its facing stays at or
    ///   above MinForwardSpeed (a turn is held just short of done until it is), so a fighter
    ///   ahead of the player flies at it, passes above/below and turns to chase. The avoid box
    ///   widens with the closing speed and has hysteresis: once in, the fighter holds
    ///   AvoidClearance above/below the player until |dx| leaves the box, and its nose doesn't
    ///   aim at the player meanwhile. Escorts hold their own formation slot (FormationOffset).
    ///   JSON fighters, reinforcements and re-entered fighters all enter past the right screen
    ///   edge flying left (B074; SetFlightDirection / ReEnter, B040), then chase like the rest.
    ///   While entering (BeginEntry) the camera's leftward speed is added to its top speed until
    ///   it is in view, so it reaches the view even when the player scrolls left. The sprite
    ///   mirror, nose, gun cone, pitch and smoke tail follow the flight direction.
    /// • Mission JSON patterns are only flavour on the chase height: sine adds a small wobble,
    ///   dive an initial dip; straight and static add nothing (static starts at MinForwardSpeed).
    /// • Escorts hold formation (own slot above/below and behind, kept inside the flight band:
    ///   FormationSlotY, B063) on their bomber/zeppelin until the player comes within
    ///   fighterChase.escortLeash of it, chase, and return beyond twice the leash.
    /// • While the player is dead, crashed or missing it keeps its last heading at cruise
    ///   speed and never fires.
    /// • Direct-hit damage model per GDD: "fighters take direct hits only". Three player
    ///   bullets kill it (Balance enemyHealth.fighter 75 / player.bulletDamage 25).
    /// • Shoots at the player (B023): an EnemyBullet (DamageSource.Bullet, scaled by
    ///   damageTaken.fighterBullet) only while the fighter is on screen, not mid-turn, the
    ///   player is alive, and the player is within enemyFire.range and fighterConeDegrees of
    ///   its nose, which points along its flight direction (FlightDirection). Cadence and the
    ///   random first-shot delay come from Balance enemyFire; the cooldown runs only on screen.
    /// • Pitch (GDD Flight attitude, B029): with the player in that cone and range, the nose
    ///   eases toward the bearing to the player (pitch.easeDegreesPerSecond, clamped to
    ///   pitch.fighterMaxDegrees); otherwise it follows the climb/dive (pitch.maxDegrees).
    ///   Shots fly ALONG THE NOSE (± enemyFire.spreadDegrees) and leave from it, and the
    ///   fighter only fires while the player is within enemyFire.aimToleranceDegrees of the
    ///   nose. The body turns with Rigidbody2D.MoveRotation, so the hitbox turns too.
    /// • Attack slots and standoff spread (B060, ARCHITECTURE §3 "Fighter chase"): a fighter
    ///   only fires while it holds one of the fighterChase.maxAttackers slots of
    ///   FighterAttackSlots.Shared (nearest fighters with the nose on the player first; released
    ///   on death, removal, re-entry, more than fighterChase.slotGraceSeconds unable to shoot, or
    ///   the player's death). Without a slot it keeps chasing and aiming but holds fire. Each
    ///   fighter holds its own standoff, picked once uniformly in [standoff, standoffMax].
    /// • Smoke trail (B051, GDD Enemies item 1): a live fighter that has been hit trails pencil
    ///   smoke through FxManager.SpawnEnemySmokePuff, SmokeTailOffset behind its tail (tail side
    ///   follows FlightDirection and pitch): one puff every SmokeIntervalLight after the first
    ///   hit, every SmokeIntervalHeavy from the second. Healthy and dead fighters never smoke;
    ///   a re-entering (ReEnter) fighter keeps its damage and so its smoke.
    ///
    /// Spawned by MissionRunner from mission JSON via the static factory Spawn(position).
    /// </summary>
    public class FighterAI : MonoBehaviour, IDamageable {

        // ── Serialized tunables (set from mission JSON at runtime) ────────

        [SerializeField] private float speed = DefaultSpeed;                // world units / sec
        [SerializeField] private string pattern = "straight";              // static|straight|sine|dive|escort
        [SerializeField] private string escortOf = "";                     // name/id to follow
        [SerializeField] private float sineAmplitude = 0.5f;               // sine flavour on the chase height (B059)
        [SerializeField] private float sineSpeed = 2f;                     // sine flavour frequency (rad/s)

        // ── Cached components (populated in Awake / Start) ──────────────

        private Health _health;
        private Rigidbody2D _rb;
        private SpriteRenderer _sr;
        private TeamTag _teamTag;

        // ── Pitch tracking ───────────────────────────────────────────────
        private float _prevY;          // Y position from previous FixedUpdate
        private float _pitchAngle;     // Current pitch angle (degrees, nose up positive)
        private PitchData _pitch;      // resolved Balance pitch block

        /// <summary>Flight direction: -1 = left (default; the right-facing art is mirrored with
        /// flipX), +1 = right (B040). It follows the chase turn (B059).</summary>
        private int _direction = -1;

        // ── Runtime state ───────────────────────────────────────────────

        private Transform _escortTarget;                                  // cached escort target
        private float _driftPhase;                                        // sine flavour phase offset

        /// <summary>Hit points when Balance.json is missing: three 25-point bullets (B040).</summary>
        private const float FallbackHitPoints = 75f;

        /// <summary>Serialized speed default (enemySpeed.fighter fallback).</summary>
        private const float DefaultSpeed = 3f;

        // ── Chase (B059) ────────────────────────────────────────────────

        private FighterChaseData _chase;
        /// <summary>World x velocity of its own flight (units/s). Starts at FlightDirection x
        /// speed (0 for "static"); the chase steers it.</summary>
        private float _velX = -DefaultSpeed;
        /// <summary>Measured x velocity while holding escort formation (0 before it moved).</summary>
        private float _formationVelX;
        /// <summary>Turn state, -1..+1: its sign is the facing; |value| &lt; 1 means mid-turn.</summary>
        private float _heading = -1f;
        /// <summary>The facing the current turn heads for (-1 or +1).</summary>
        private int _turnTarget = -1;
        /// <summary>Per-fighter height offset from the player's y, so wingmen spread.</summary>
        private float _offsetY;
        /// <summary>Seconds of chase since spawn or re-entry (clock for the pattern flavour).</summary>
        private float _age;
        /// <summary>An escort that left formation to chase the player (leash rule).</summary>
        private bool _escortChasing;
        /// <summary>This fighter's own standoff distance, picked once in [standoff, standoffMax]
        /// (B060) so a pack spreads out horizontally.</summary>
        private float _standoff;
        /// <summary>Avoid-box hysteresis (B061): holding the AvoidClearance height until |dx|
        /// leaves the box.</summary>
        private bool _avoiding;
        /// <summary>Side of the player held while avoiding: +1 above, -1 below.</summary>
        private int _avoidSide = 1;
        /// <summary>Formation slot index on the escorted plane (B061); -1 = not assigned yet.</summary>
        private int _escortSlot = -1;
        /// <summary>B074: entering from past the right screen edge (BeginEntry) and not yet in
        /// view; while set its top speed gets EntryBoost so it reaches the view even when the
        /// camera scrolls left faster than a fighter flies.</summary>
        private bool _entering;
        /// <summary>Half-height of the escorted plane's hitboxes (measured once with the slot).</summary>
        private float _escortHalfHeight = FormationDefaultHalfHeight;
        /// <summary>Escorted plane's x last tick, and its travel sign (-1 left, default).</summary>
        private float _escortPrevX = float.NaN;
        private int _escortTravel = -1;

        /// <summary>Live (enabled) fighters, for separation. No scene search per frame.</summary>
        private static readonly List<FighterAI> s_live = new List<FighterAI>();
        /// <summary>Spawn serial, the tie-break for level fighters in the separation push.</summary>
        private static int s_nextSerial;
        private readonly int _serial = ++s_nextSerial;

        // Steering shape (B059). Not Balance tunables: they shape the curves that the
        // fighterChase values drive.
        /// <summary>Lowest chase height above the terrain.</summary>
        public const float ChaseGroundClearance = 1.25f;
        /// <summary>Highest chase height below the top of the camera view.</summary>
        public const float ChaseTopMargin = 1f;
        /// <summary>The per-fighter height offset is random in ± this.</summary>
        public const float MaxChaseOffsetY = 1f;
        /// <summary>Vertical approach gain (1/s): desired climb = gain x height error, capped at
        /// climbRate, so it eases into the target height.</summary>
        public const float VerticalGain = 2f;
        /// <summary>Share of accel the braking curve assumes, so it always stops in time.</summary>
        public const float BrakeFactor = 0.8f;
        /// <summary>Horizontal approach gain (1/s) near the standoff point (smooth arrival).</summary>
        public const float ApproachGain = 1.2f;
        /// <summary>The avoidance box reaches this share of standoff in x.</summary>
        public const float AvoidBoxFraction = 0.85f;
        /// <summary>The avoidance box reaches this far above and below the player.</summary>
        public const float AvoidHalfHeight = 1f;
        /// <summary>Inside the box the fighter heads for this far above or below the player.</summary>
        public const float AvoidClearance = 1.5f;
        /// <summary>Dive flavour: depth of the initial dip below the chase height.</summary>
        public const float DiveFlavourDepth = 2f;
        /// <summary>Dive flavour: seconds the initial dip lasts.</summary>
        public const float DiveFlavourSeconds = 1.5f;
        /// <summary>Narrowest the sprite gets (x scale) in the middle of a turn-around.</summary>
        public const float MinTurnScale = 0.15f;

        // Steering shape (B061, QA T966). Not Balance tunables.
        /// <summary>Slowest flight along the facing (units/s): a fighter never flies tail-first
        /// or hovers, so one ahead of the player flies at it, passes and turns to chase.</summary>
        public const float MinForwardSpeed = 0.5f;
        /// <summary>The avoid box widens in x by the closing speed times this (s), so a head-on
        /// pass starts its climb/dive early enough to reach AvoidClearance.</summary>
        public const float AvoidLeadSeconds = 0.6f;
        /// <summary>Hysteresis: once avoiding, the fighter keeps the clearance height until
        /// |dx| is this much beyond the box edge.</summary>
        public const float AvoidExitMargin = 0.25f;
        /// <summary>A turn is held just short of done (still "turning", no fire) while the
        /// velocity along the new facing is below MinForwardSpeed.</summary>
        public const float TurnHoldHeading = 0.99f;
        /// <summary>Formation: gap between the escorted plane's hitbox and the escort's centre.</summary>
        public const float FormationGapY = 0.7f;
        /// <summary>Formation: x distance behind the escorted plane's centre of the first pair.</summary>
        public const float FormationBehindX = 1f;
        /// <summary>Formation: extra x per further pair (more than the 1.5-unit fighter width).</summary>
        public const float FormationRowSpacingX = 1.8f;
        /// <summary>Formation: half-height used when the escorted plane has no box collider.</summary>
        public const float FormationDefaultHalfHeight = 0.6f;
        /// <summary>Formation (B063): y step between further tiers on one side of the escorted
        /// plane (fighter hitbox height 1 + FormationGapY), used when a slot leaves the band.</summary>
        public const float FormationTierGapY = 1.7f;
        /// <summary>Formation (B063): tiers per side tried before falling back to a clamp.</summary>
        public const int FormationMaxTiers = 3;

        // ── Gun (B023) ──────────────────────────────────────────────────

        /// <summary>Source of a fighter's shots; the player's Health maps it to damageTaken.fighterBullet.</summary>
        public const DamageSource ShotSource = DamageSource.Bullet;

        /// <summary>"Ahead" along the flight direction: -x when flying left, +x when right.</summary>
        private Vector2 Forward => new Vector2(_direction, 0f);

        /// <summary>Muzzle distance ahead of the centre: half the 1.5-unit hitbox, so the round
        /// leaves from the nose. Sprite geometry, not a gameplay tunable.</summary>
        private const float MuzzleOffset = 0.75f;

        /// <summary>Current pitch in degrees, nose up positive (B029).</summary>
        public float PitchAngle => _pitchAngle;

        /// <summary>The z rotation (degrees) the body is turned to with MoveRotation: -PitchAngle
        /// while flying left (the sprite is mirrored), PitchAngle while flying right.</summary>
        public float BodyAngle => PlanePitch.BodyAngle(_pitchAngle, _direction);

        /// <summary>World unit vector of the drawn nose at the current pitch: (dir * cos p, sin p).
        /// Shots leave along it (± spread).</summary>
        public Vector2 NoseDirection => PlanePitch.NoseDirection(_pitchAngle, _direction);

        // ── Smoke trail (B051) ───────────────────────────────────────────
        // Visual only (not gameplay tunables), matching the player's trail (PlayerPlane).

        /// <summary>Seconds between puffs after one hit (a thin trail).</summary>
        public const float SmokeIntervalLight = 0.25f;
        /// <summary>Seconds between puffs from the second hit on (thicker), the player's rate.</summary>
        public const float SmokeIntervalHeavy = 0.12f;
        /// <summary>Distance behind the centre, along the body, where puffs appear: half the
        /// 1.5-unit hitbox, the tail end (same as the muzzle offset at the nose).</summary>
        public const float SmokeTailOffset = 0.75f;

        /// <summary>Hits observed so far (each damage increase seen by EmitSmoke counts one).</summary>
        private int _hitsSeen;
        /// <summary>Damage percent at the last EmitSmoke, to spot new hits.</summary>
        private float _lastDamagePercent;
        /// <summary>Seconds until the next puff; ≤ 0 emits at once.</summary>
        private float _smokeTimer;
        /// <summary>player.bulletDamage, for counting hits from the damage total (0 = unknown).</summary>
        private float _hitDamage;

        /// <summary>Hits taken so far: the larger of the damage increases seen and the damage
        /// total in player bullets (so two bullets in one frame still count as two).</summary>
        public int HitCount {
            get {
                if (_health == null) return 0;
                return Mathf.Max(_hitsSeen,
                    HitsFromDamage(_health.CurrentDamagePercent, _health.MaxHitPoints, _hitDamage));
            }
        }

        private EnemyFireData _fire;
        private float _fireCooldown;                                      // seconds on screen until the next shot
        private Camera _cam;
        private PlayerPlane _player;
        private Health _playerHealth;

        /// <summary>The player this fighter chases and shoots at. Found once in Start when not set.</summary>
        internal PlayerPlane PlayerRef {
            get => _player;
            set {
                _player = value;
                _playerHealth = value != null ? value.GetComponent<Health>() : null;
            }
        }

        public Team Team { get; private set; }
        public bool IsDead => _health != null && _health.IsDead;

        /// <summary>Flight direction (facing): -1 = left, +1 = right (B040, B059).</summary>
        public int FlightDirection => _direction;

        /// <summary>Movement pattern from the mission JSON (static, straight, sine, dive, escort).</summary>
        public string Pattern => pattern;

        /// <summary>Tied to a live escort target: holding formation on it, or chasing the player
        /// on the escort leash (B059). EnemyPressure leaves these alone.</summary>
        public bool IsEscorting => pattern == "escort" && _escortTarget != null;

        /// <summary>An escort that left formation to chase the player (B059 leash).</summary>
        public bool IsEscortChasing => IsEscorting && _escortChasing;

        /// <summary>Mid turn-around (B059): the facing is easing between left and right.</summary>
        public bool IsTurning => Mathf.Abs(_heading) < 1f;

        /// <summary>Per-fighter height offset from the player's y (B059).</summary>
        public float ChaseOffsetY => _offsetY;

        /// <summary>Inside the avoid box, holding the AvoidClearance height (B061 hysteresis).
        /// The nose doesn't aim at the player meanwhile.</summary>
        public bool IsAvoiding => _avoiding;

        /// <summary>Formation slot on the escorted plane (B061), -1 before it is assigned.</summary>
        public int EscortSlot => _escortSlot;

        /// <summary>This fighter's standoff distance (B060), fixed for its life, in
        /// [fighterChase.standoff, fighterChase.standoffMax].</summary>
        public float Standoff => _standoff;

        /// <summary>Holds an attack slot (B060): only holders fire.</summary>
        public bool HasAttackSlot => FighterAttackSlots.Shared.Holds(this);

        /// <summary>World x speed (units/s) of its own flight (B059): the chase velocity, or the
        /// measured formation velocity while holding formation on an escort target.
        /// EnemyPressure uses it for the escape test and the cap (B040, B041).</summary>
        public float WorldVelocityX => IsEscorting && !_escortChasing ? _formationVelX : _velX;

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
                ? balance.enemyHealth.fighter : FallbackHitPoints);
            // Flight speed from Balance enemySpeed.fighter; keeps the serialized 3 if missing.
            if (balance != null && balance.enemySpeed != null && balance.enemySpeed.fighter > 0f) {
                speed = balance.enemySpeed.fighter;
            }

            _teamTag = GetComponent<TeamTag>();

            _hitDamage = balance != null && balance.player != null ? balance.player.bulletDamage : 0f;

            _fire = EnemyFireData.Resolve(balance);
            _fireCooldown = UnityEngine.Random.Range(_fire.firstShotDelayMin, _fire.firstShotDelayMax);

            // Chase (B059): starts flying along its direction at cruise speed.
            _chase = FighterChaseData.Resolve(balance);
            _heading = _direction;
            _turnTarget = _direction;
            _velX = _direction * (pattern == "static" ? MinForwardSpeed : speed);
            _offsetY = UnityEngine.Random.Range(-MaxChaseOffsetY, MaxChaseOffsetY);
            _standoff = PickStandoff(_chase.standoff, _chase.standoffMax, UnityEngine.Random.value);

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
            // Aircraft sprites face right (ART_STYLE); a left-flying fighter is mirrored.
            _sr.flipX = _direction < 0;
            _sr.sortingOrder = 5; // ARCHITECTURE §3 draw order: enemies

            // Resolve escort target once at start (no per-frame Find* calls).
            _escortTarget = FindEscortTarget(escortOf);

            // Randomise sine phase.
            _driftPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            // Gun and chase targets, resolved once (no per-frame Find* calls).
            _cam = Camera.main;
            if (_player == null) PlayerRef = FindFirstObjectByType<PlayerPlane>();
        }

        void OnEnable() {
            if (!s_live.Contains(this)) s_live.Add(this);
        }

        void OnDisable() {
            s_live.Remove(this);
            FighterAttackSlots.Shared.Release(this); // died or removed: free its slot (B060)
        }

        /// <summary>Clears the live list and the attack slots when play starts without a
        /// domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() {
            s_live.Clear();
            FighterAttackSlots.Shared.Reset();
        }

        void Update() {
            EmitSmoke(Time.deltaTime);
        }

        void FixedUpdate() {
            if (_health != null && _health.IsDead) {
                // Health already raised EntityDestroyed (score + objectives). Burst and remove.
                FxManager.Instance.TriggerExplosion(transform.position);
                Destroy(gameObject);
                return;
            }
            if (_rb == null) return;

            // Chase (B059): face, pursue and keep clear of the player.
            Vector3 pos = ChaseStep(transform.position, Time.fixedDeltaTime);
            _rb.MovePosition(pos);

            // ----- Pitch: aim at the player in the cone, else follow the climb/dive -----
            float verticalVelocity = (pos.y - _prevY) / Time.fixedDeltaTime;
            _prevY = pos.y;
            UpdatePitch(verticalVelocity, Time.fixedDeltaTime);
            _rb.MoveRotation(BodyAngle);

            TickFire(Time.fixedDeltaTime);
        }

        // ── Chase (B059) ────────────────────────────────────────────────

        /// <summary>
        /// One chase tick of `dt` seconds from `pos`; returns the next position and updates the
        /// velocity, turn and escort state (FixedUpdate moves the body there; tests drive it
        /// directly). Order: escort leash (formation returns early), facing/turn, then the
        /// horizontal pursuit with standoff braking and the vertical pursuit (player y + offset
        /// + pattern flavour, or the avoidance height inside the standoff box) inside the flight
        /// band, plus the separation push. With no live player it finishes any turn and keeps
        /// its heading at cruise speed.
        /// </summary>
        internal Vector3 ChaseStep(Vector3 pos, float dt) {
            if (dt <= 0f) return pos;
            if (_chase == null) _chase = FighterChaseData.Resolve(GameBalance.Get());
            if (_standoff <= 0f) _standoff = PickStandoff(_chase.standoff, _chase.standoffMax, UnityEngine.Random.value);
            _age += dt;
            float maxSpeed = speed + EntryBoostNow(pos.x);

            bool targetable = PlayerTargetable();
            Vector2 playerPos = targetable ? (Vector2)_player.transform.position : Vector2.zero;
            Vector2 playerVel = targetable ? _player.Velocity : Vector2.zero;

            // Escort leash: hold formation until the player nears the escorted plane.
            if (IsEscorting) {
                float d = targetable ? Vector2.Distance(playerPos, _escortTarget.position) : float.PositiveInfinity;
                bool chasing = EscortShouldChase(_escortChasing, d, _chase.escortLeash);
                if (chasing && !_escortChasing) _velX = _formationVelX; // leave formation smoothly
                _escortChasing = chasing;
                if (!chasing) {
                    _avoiding = false;
                    Vector3 target = _escortTarget.position;
                    if (_escortSlot < 0) AssignEscortSlot();
                    if (!float.IsNaN(_escortPrevX) && Mathf.Abs(target.x - _escortPrevX) > 1e-5f) {
                        _escortTravel = target.x > _escortPrevX ? 1 : -1;
                    }
                    _escortPrevX = target.x;
                    // Its own slot around the escorted plane (B061): above/below it, behind it.
                    Vector2 offset = FormationOffset(_escortSlot, -_escortTravel, _escortHalfHeight);
                    // Kept inside the flight band (B063): an upper slot with no room above the
                    // escorted plane flips below it (next free tier), so the escort stays visible.
                    float slotX = target.x + offset.x;
                    BandLimits(slotX, out float slotLow, out float slotHigh);
                    float slotY = FormationSlotY(_escortSlot, target.y, _escortHalfHeight, slotLow, slotHigh);
                    Vector3 slot = new Vector3(slotX, slotY, pos.z);
                    // Face the way it travels (never tail-first, B061); when (almost) still,
                    // face the player as before.
                    int formationFacing = Mathf.Abs(_formationVelX) >= MinForwardSpeed
                        ? (_formationVelX > 0f ? 1 : -1)
                        : targetable
                            ? DesiredFacing(_turnTarget, playerPos.x - pos.x, _chase.turnDeadzone)
                            : _turnTarget;
                    StepTurn(formationFacing, dt, false);
                    Vector3 next = Vector3.MoveTowards(pos, slot, speed * dt);
                    _formationVelX = (next.x - pos.x) / dt;
                    _velX = _formationVelX;
                    return next;
                }
            } else {
                _escortChasing = false;
            }

            if (!targetable) {
                // Player dead, crashing or missing: finish any turn and keep the heading.
                _avoiding = false;
                StepTurn(_turnTarget, dt, true);
                _velX = Mathf.MoveTowards(_velX, _turnTarget * speed, _chase.accel * dt);
                pos.x += _velX * dt;
                return pos;
            }

            int facing = DesiredFacing(_turnTarget, playerPos.x - pos.x, _chase.turnDeadzone);
            StepTurn(facing, dt, true);
            // B075: behind a left-flying player it keeps the camera's leftward speed on top of its
            // own, so it holds its standoff instead of falling off the right edge.
            if (BehindBoostApplies(facing, playerPos.x - pos.x)) {
                maxSpeed = Mathf.Max(maxSpeed, speed + EntryBoost(facing, BoostCameraVelocityNow(facing)));
            }

            // Pursuit along the facing, never slower than MinForwardSpeed (no tail-first flight).
            _velX = HorizontalVelocityStep(pos.x, _velX, playerPos.x, playerVel.x, facing,
                _standoff, maxSpeed, _chase.accel, dt, MinForwardSpeed);
            float newX = pos.x + _velX * dt;

            BandLimits(newX, out float low, out float high);
            float dx = playerPos.x - pos.x;
            float dy = pos.y - playerPos.y;
            float halfWidth = AvoidHalfWidth(_standoff, ClosingSpeed(dx, _velX, playerVel.x));
            bool wasAvoiding = _avoiding;
            _avoiding = AvoidHold(_avoiding, Mathf.Abs(dx), Mathf.Abs(dy), halfWidth);
            if (_avoiding) {
                // Hysteresis (B061): pick a side on entry and hold AvoidClearance on it until
                // |dx| leaves the box; climb/dive at full climbRate, no separation push.
                if (!wasAvoiding) _avoidSide = dy > 0f ? 1 : dy < 0f ? -1 : (_offsetY >= 0f ? 1 : -1);
                float avoidY = AvoidTargetY(playerPos.y, _avoidSide, low, high);
                _avoidSide = avoidY >= playerPos.y ? 1 : -1;
                float yMove = Mathf.MoveTowards(pos.y, Mathf.Clamp(avoidY, low, Mathf.Max(low, high)),
                    _chase.climbRate * dt);
                return new Vector3(newX, yMove, pos.z);
            }

            float targetY = playerPos.y + _offsetY
                + FlavourOffset(pattern, _age, sineAmplitude, sineSpeed, _driftPhase);
            float newY = VerticalStep(pos.y, targetY, _chase.climbRate, low, high, dt);
            newY += SeparationTotal(pos) * _chase.climbRate * dt;
            return new Vector3(newX, newY, pos.z);
        }

        /// <summary>Picks this escort's formation slot (B061): the lowest index no other live
        /// escort of the same plane holds, and measures the escorted plane's half-height from
        /// its own box colliders (size and offset, so it works before the physics sync). Runs
        /// once per escort (first formation tick), not per frame.</summary>
        private void AssignEscortSlot() {
            int index = 0;
            for (bool taken = true; taken; ) {
                taken = false;
                for (int i = 0; i < s_live.Count; i++) {
                    var other = s_live[i];
                    if (other == null || other == this || other._escortTarget != _escortTarget) continue;
                    if (other._escortSlot == index) { taken = true; index++; break; }
                }
            }
            _escortSlot = index;

            float half = 0f;
            foreach (var box in _escortTarget.GetComponents<BoxCollider2D>()) {
                half = Mathf.Max(half, Mathf.Abs(box.offset.y) + box.size.y * 0.5f);
            }
            _escortHalfHeight = half > 0f ? half : FormationDefaultHalfHeight;
        }

        /// <summary>Advances the turn toward `facing`: eases the turn state, flips the sprite
        /// and the flight direction when it crosses zero, and squashes the body (x scale)
        /// edge-on mid-turn. With `holdForSpeed`, a turn in progress is held just short of done
        /// (TurnHoldHeading, still IsTurning) while the velocity along the new facing is below
        /// MinForwardSpeed, so a finished turn never flies tail-first (B061).</summary>
        private void StepTurn(int facing, float dt, bool holdForSpeed) {
            _turnTarget = facing < 0 ? -1 : 1;
            float turnSeconds = _chase != null ? _chase.turnSeconds : FighterChaseData.DefaultTurnSeconds;
            bool wasTurning = Mathf.Abs(_heading) < 1f || (_heading > 0f ? 1 : -1) != _turnTarget;
            _heading = StepHeading(_heading, _turnTarget, turnSeconds, dt);
            if (holdForSpeed && wasTurning && Mathf.Abs(_heading) >= 1f
                    && _turnTarget * _velX < MinForwardSpeed - 1e-4f) {
                _heading = _turnTarget * TurnHoldHeading;
            }
            int dir = FacingFromHeading(_heading, _direction);
            if (dir != _direction) {
                _direction = dir;
                if (_sr != null) _sr.flipX = _direction < 0;
            }
            Vector3 scale = transform.localScale;
            scale.x = TurnScaleX(_heading);
            transform.localScale = scale;
        }

        /// <summary>Flight band at world x: ChaseGroundClearance above the terrain up to
        /// ChaseTopMargin below the top of the view (no ceiling without a camera).</summary>
        private void BandLimits(float x, out float low, out float high) {
            var terrain = TerrainStrip.Instance;
            low = (terrain != null ? terrain.HeightAt(x) : TerrainStrip.GroundBaseY) + ChaseGroundClearance;
            high = _cam != null
                ? _cam.transform.position.y + _cam.orthographicSize - ChaseTopMargin
                : float.PositiveInfinity;
            if (high < low) high = low;
        }

        /// <summary>Summed vertical separation push (-1..1) from the other live fighters.</summary>
        private float SeparationTotal(Vector2 self) {
            if (_chase == null || s_live.Count < 2) return 0f;
            float push = 0f;
            for (int i = 0; i < s_live.Count; i++) {
                var other = s_live[i];
                if (other == null || other == this || other.IsDead) continue;
                int tie = _serial > other._serial ? 1 : -1;
                push += SeparationPush(self, other.transform.position, _chase.separation, tie);
            }
            return Mathf.Clamp(push, -1f, 1f);
        }

        // ── Chase helpers (pure, EditMode-tested) ───────────────────────

        /// <summary>
        /// Facing the fighter should turn to: it keeps `currentFacing` unless the player is
        /// more than `deadzone` behind it (`dx` = player x - fighter x), then the opposite.
        /// </summary>
        public static int DesiredFacing(int currentFacing, float dx, float deadzone) {
            int f = currentFacing < 0 ? -1 : 1;
            return f * dx < -Mathf.Max(0f, deadzone) ? -f : f;
        }

        /// <summary>Moves the turn state `heading` (-1..1) toward `facing` so a full
        /// turn-around takes `turnSeconds` (≤ 0: instant).</summary>
        public static float StepHeading(float heading, int facing, float turnSeconds, float dt) {
            float target = facing < 0 ? -1f : 1f;
            if (turnSeconds <= 0f) return target;
            return Mathf.MoveTowards(Mathf.Clamp(heading, -1f, 1f), target, 2f / turnSeconds * Mathf.Max(0f, dt));
        }

        /// <summary>Facing from the turn state: its sign; exactly 0 keeps `current`.</summary>
        public static int FacingFromHeading(float heading, int current) {
            if (heading > 0f) return 1;
            if (heading < 0f) return -1;
            return current < 0 ? -1 : 1;
        }

        /// <summary>Body x scale for the turn state: eased |sin(heading x 90°)|, never below
        /// MinTurnScale (1 when not turning).</summary>
        public static float TurnScaleX(float heading) {
            float eased = Mathf.Abs(Mathf.Sin(Mathf.Clamp(heading, -1f, 1f) * Mathf.PI * 0.5f));
            return Mathf.Max(MinTurnScale, eased);
        }

        /// <summary>
        /// Per-fighter standoff (B060): uniform in [min, max] for `t01` in [0, 1] (clamped);
        /// a max below min gives min.
        /// </summary>
        public static float PickStandoff(float min, float max, float t01) {
            if (max < min) max = min;
            return Mathf.Lerp(min, max, Mathf.Clamp01(t01));
        }

        /// <summary>
        /// One step of the horizontal pursuit. The aim point is `standoff` short of the player
        /// on the side the fighter faces it from (playerX - facing x standoff). The desired
        /// velocity is the player's x velocity plus a closing speed that can still brake to a
        /// stop at the aim point (min(sqrt(2 x BrakeFactor x accel x distance),
        /// ApproachGain x distance)), capped at ±maxSpeed; the velocity moves toward it by at
        /// most accel x dt. This overload has no forward floor (the B059 pursuit shape); the
        /// game uses the one with `minForward` (B061).
        /// </summary>
        public static float HorizontalVelocityStep(float x, float velX, float playerX, float playerVelX, int facing,
                float standoff, float maxSpeed, float accel, float dt) {
            return HorizontalVelocityStep(x, velX, playerX, playerVelX, facing, standoff, maxSpeed, accel, dt,
                float.NegativeInfinity);
        }

        /// <summary>
        /// The pursuit step with a forward floor (B061, GDD Enemies 1: never flies backwards):
        /// the desired velocity along `facing` is at least `minForward` (capped at maxSpeed), so
        /// a fighter ahead of a player that flies at it can't back off tail-first: it flies at
        /// the player (slowing to minForward near its standoff point) and is passed. Standoff
        /// braking behind the player works as before down to minForward.
        /// </summary>
        public static float HorizontalVelocityStep(float x, float velX, float playerX, float playerVelX, int facing,
                float standoff, float maxSpeed, float accel, float dt, float minForward) {
            float f = facing < 0 ? -1f : 1f;
            float aimX = playerX - f * Mathf.Max(0f, standoff);
            float error = aimX - x;
            float dist = Mathf.Abs(error);
            float closing = Mathf.Min(Mathf.Sqrt(2f * BrakeFactor * Mathf.Max(0f, accel) * dist), ApproachGain * dist);
            float desired = Mathf.Clamp(playerVelX + Mathf.Sign(error) * closing, -maxSpeed, maxSpeed);
            float floor = Mathf.Min(minForward, maxSpeed);
            if (f * desired < floor) desired = f * floor;
            return Mathf.MoveTowards(velX, desired, Mathf.Max(0f, accel) * Mathf.Max(0f, dt));
        }

        /// <summary>
        /// One step of the vertical pursuit: `targetY` is clamped into [low, high], and y moves
        /// toward it at VerticalGain x error, capped at `climbRate`. A fighter outside the
        /// band eases back in (no snap).
        /// </summary>
        public static float VerticalStep(float y, float targetY, float climbRate, float low, float high, float dt) {
            if (high < low) high = low;
            float t = Mathf.Clamp(targetY, low, high);
            float rate = Mathf.Min(Mathf.Max(0f, climbRate), Mathf.Abs(t - y) * VerticalGain);
            return Mathf.MoveTowards(y, t, rate * Mathf.Max(0f, dt));
        }

        /// <summary>
        /// Vertical push (-1..1) on a fighter at `self` from one at `other`: 0 at or beyond
        /// `separation`, rising linearly to 1 as they meet, away from the other (up when above
        /// it, down when below; level ties use the sign of `tieBreak`).
        /// </summary>
        public static float SeparationPush(Vector2 self, Vector2 other, float separation, int tieBreak) {
            if (separation <= 0f) return 0f;
            Vector2 d = self - other;
            float dist = d.magnitude;
            if (dist >= separation) return 0f;
            float side = d.y > 0f ? 1f : d.y < 0f ? -1f : (tieBreak < 0 ? -1f : 1f);
            return side * (1f - dist / separation);
        }

        /// <summary>True when the fighter is inside the B059 base standoff box around the player
        /// (|dx| &lt; AvoidBoxFraction x standoff and |dy| &lt; AvoidHalfHeight). ChaseStep now
        /// uses the wider, hysteretic AvoidHalfWidth + AvoidHold (B061).</summary>
        public static bool InAvoidBox(float dx, float dy, float standoff) {
            return Mathf.Abs(dx) < AvoidBoxFraction * Mathf.Max(0f, standoff) && Mathf.Abs(dy) < AvoidHalfHeight;
        }

        /// <summary>
        /// Half-width (x) of the avoid box (B061): AvoidBoxFraction x standoff, plus the
        /// closing speed (≥ 0) x AvoidLeadSeconds, so a fast head-on pass starts avoiding early.
        /// </summary>
        public static float AvoidHalfWidth(float standoff, float closingSpeed) {
            return AvoidBoxFraction * Mathf.Max(0f, standoff) + AvoidLeadSeconds * Mathf.Max(0f, closingSpeed);
        }

        /// <summary>Speed (units/s) at which |dx| shrinks for a fighter at velocity `velX` and a
        /// player at `playerVelX`, `dx` = player x - fighter x; negative when they separate.</summary>
        public static float ClosingSpeed(float dx, float velX, float playerVelX) {
            return (dx >= 0f ? 1f : -1f) * (velX - playerVelX);
        }

        /// <summary>
        /// Avoid-box hysteresis (B061): a fighter enters when |dx| &lt; `halfWidth` and |dy| &lt;
        /// AvoidClearance; once `avoiding` it stays until |dx| ≥ halfWidth + AvoidExitMargin,
        /// whatever |dy| is (so reaching the clearance height doesn't drop it back toward the
        /// player mid-pass).
        /// </summary>
        public static bool AvoidHold(bool avoiding, float absDx, float absDy, float halfWidth) {
            if (avoiding) return absDx < halfWidth + AvoidExitMargin;
            return absDx < halfWidth && absDy < AvoidClearance;
        }

        /// <summary>
        /// Avoidance height: AvoidClearance above (`preferSide` ≥ 0) or below the player. When
        /// the band [low, high] can't hold the full clearance on that side and the other side
        /// gives more, the other side.
        /// </summary>
        public static float AvoidTargetY(float playerY, int preferSide, float low, float high) {
            float side = preferSide < 0 ? -1f : 1f;
            float t = playerY + side * AvoidClearance;
            float other = playerY - side * AvoidClearance;
            if (high < low) high = low;
            float clear = Mathf.Abs(Mathf.Clamp(t, low, high) - playerY);
            if (clear < AvoidClearance - 1e-4f && Mathf.Abs(Mathf.Clamp(other, low, high) - playerY) > clear) t = other;
            return t;
        }

        /// <summary>
        /// Formation slot offset of escort number `index` (B061) from the escorted plane's
        /// centre: pairs above/below it (even index above, odd below), clear of its hitbox
        /// (`targetHalfHeight` + FormationGapY), the first pair FormationBehindX behind it and
        /// each further pair FormationRowSpacingX farther back. `behindSign` is the x side
        /// behind the plane (+1 for a plane flying left).
        /// </summary>
        public static Vector2 FormationOffset(int index, int behindSign, float targetHalfHeight) {
            if (index < 0) index = 0;
            int row = index / 2;
            float side = index % 2 == 0 ? 1f : -1f;
            float behind = behindSign < 0 ? -1f : 1f;
            return new Vector2(behind * (FormationBehindX + row * FormationRowSpacingX),
                side * (Mathf.Max(0f, targetHalfHeight) + FormationGapY));
        }

        /// <summary>
        /// World y of formation slot `index` (B063) on a plane at `targetY` with hitbox
        /// half-height `targetHalfHeight`, kept inside the flight band [`low`, `high`]. The
        /// heights a row can use are tiers above/below the plane: tier k (1..FormationMaxTiers)
        /// sits targetHalfHeight + FormationGapY + (k-1) x FormationTierGapY from its centre, so
        /// every tier clears the plane's hitbox. The odd (lower) slot of a row keeps below tier 1
        /// when it is in the band, else takes the first in-band tier (above 1, below 1, above 2,
        /// below 2, ...); the even (upper) slot keeps above tier 1 when it is in the band and
        /// free, else takes the first in-band tier its partner doesn't hold: a high plane's
        /// upper slot flips below, a low plane's lower slot flips above. With no room for both
        /// it clamps the preferred height into the band. Pure function of its inputs, so a slot
        /// is stable for a fixed plane height and the two slots of a row never coincide;
        /// rows are FormationRowSpacingX apart in x (FormationOffset).
        /// </summary>
        public static float FormationSlotY(int index, float targetY, float targetHalfHeight, float low, float high) {
            if (index < 0) index = 0;
            float baseGap = Mathf.Max(0f, targetHalfHeight) + FormationGapY;
            bool upper = index % 2 == 0;
            float preferred = targetY + (upper ? baseGap : -baseGap);
            if (high < low) high = low;

            float lowerPick = float.NaN;
            float lowerPreferred = targetY - baseGap;
            if (InBand(lowerPreferred, low, high)) lowerPick = lowerPreferred;
            else lowerPick = FirstFreeTier(targetY, baseGap, low, high, float.NaN);
            if (!upper) return float.IsNaN(lowerPick) ? Mathf.Clamp(preferred, low, high) : lowerPick;

            float upperPreferred = targetY + baseGap;
            if (InBand(upperPreferred, low, high) && !SameY(upperPreferred, lowerPick)) return upperPreferred;
            float upperPick = FirstFreeTier(targetY, baseGap, low, high, lowerPick);
            return float.IsNaN(upperPick) ? Mathf.Clamp(preferred, low, high) : upperPick;
        }

        /// <summary>First in-band tier height (above 1, below 1, above 2, ...) other than
        /// `taken`; NaN when there is none.</summary>
        private static float FirstFreeTier(float targetY, float baseGap, float low, float high, float taken) {
            for (int k = 0; k < FormationMaxTiers; k++) {
                float off = baseGap + k * FormationTierGapY;
                float above = targetY + off;
                if (InBand(above, low, high) && !SameY(above, taken)) return above;
                float below = targetY - off;
                if (InBand(below, low, high) && !SameY(below, taken)) return below;
            }
            return float.NaN;
        }

        private static bool InBand(float y, float low, float high) {
            return y >= low - 1e-4f && y <= high + 1e-4f;
        }

        private static bool SameY(float a, float b) {
            return !float.IsNaN(b) && Mathf.Abs(a - b) < 1e-3f;
        }

        /// <summary>
        /// Escort leash: a formation escort starts chasing once the player is within `leash`
        /// of the escorted plane; a chasing escort returns when the player is farther than
        /// twice the leash.
        /// </summary>
        public static bool EscortShouldChase(bool chasing, float playerDistance, float leash) {
            return chasing ? playerDistance <= 2f * leash : playerDistance <= leash;
        }

        /// <summary>
        /// Pattern flavour on the chase height after `age` seconds: sine = `sineAmp` x
        /// sin(age x sineSpeed + phase); dive = an initial dip of DiveFlavourDepth over
        /// DiveFlavourSeconds, then 0; anything else 0.
        /// </summary>
        public static float FlavourOffset(string pattern, float age, float sineAmp, float sineSpeed, float phase) {
            switch (pattern) {
                case "sine":
                    return sineAmp * Mathf.Sin(age * sineSpeed + phase);
                case "dive":
                    if (age <= 0f || age >= DiveFlavourSeconds) return 0f;
                    return -DiveFlavourDepth * Mathf.Sin(Mathf.PI * age / DiveFlavourSeconds);
                default:
                    return 0f;
            }
        }

        // ── Pitch (B029) ────────────────────────────────────────────────

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
            if (_avoiding) return false; // passing the player: no nose-down at it (B061)
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
        /// enemyFire.aimToleranceDegrees of the nose, the fighter is not mid turn-around (B059),
        /// and it holds an attack slot (B060). Every tick it reports to FighterAttackSlots.Shared
        /// whether it could shoot; a fighter that could asks for a slot (nearest first; one
        /// taken over from another fighter keeps the slot's shot gap of fighterIntervalMin).
        /// The shot flies along the nose ± spreadDegrees and leaves from it; TickFire never
        /// changes the pitch (UpdatePitch does). Returns true when a shot was fired. Dead
        /// fighters never fire and give their slot up; a dead or crashed player frees all slots.
        /// </summary>
        internal bool TickFire(float dt) {
            var slots = FighterAttackSlots.Shared;
            if (IsDead || _fire == null || _player == null) {
                slots.Release(this);
                return false;
            }
            if (!PlayerTargetable()) {
                slots.ReleaseAll(); // the player died or crashed (B060)
                return false;
            }
            if (_chase == null) _chase = FighterChaseData.Resolve(GameBalance.Get());
            slots.Configure(_chase.maxAttackers, _chase.slotGraceSeconds);
            slots.BindTarget(_player); // a new player (next mission, Retry) resets the slots
            float now = SlotClock();

            Vector2 from = transform.position;
            Vector2 target = _player.transform.position;
            bool onScreen = EnemyBullet.IsOnScreen(_cam, from, 0f);
            bool aimed = onScreen && CanShootAt(from, target);
            slots.Report(this, aimed, (target - from).magnitude, now);
            if (!onScreen) return false;

            _fireCooldown -= dt;
            if (_fireCooldown > 0f || !aimed) return false;

            if (!slots.TryAcquire(this, now, _fire.fighterIntervalMin, out float wait)) return false;
            if (wait > 0f) {
                // Took over a slot that fired recently: keep that slot's shot gap.
                _fireCooldown = wait;
                return false;
            }

            Vector2 nose = NoseDirection;
            // Along the nose ± spread (180 = no cone clamp; the nose is already inside the cone).
            Vector2 dir = EnemyBullet.AimWithinCone(from, from + nose, nose, 180f, _fire.spreadDegrees);

            // Fire from the nose.
            Vector3 muzzle = transform.position + (Vector3)(nose * MuzzleOffset);
            // Pool exhausted: keep the expired cooldown and try again next tick.
            if (EnemyBullet.Spawn(muzzle, dir, ShotSource) == null) return false;
            slots.NotifyShot(this, now);

            // EnemyGun cue once per bullet fired, at the shooter (T950).
            var audio = AudioManager.GetOrNull();
            if (audio != null) audio.Play(SfxId.EnemyGun, transform.position);

            _fireCooldown = UnityEngine.Random.Range(_fire.fighterIntervalMin, _fire.fighterIntervalMax);
            return true;
        }

        /// <summary>True when the fighter could hit the player at `target` from `from` right
        /// now: not mid turn-around (B059), the player inside the forward cone and range, and
        /// within enemyFire.aimToleranceDegrees of the nose (B029).</summary>
        private bool CanShootAt(Vector2 from, Vector2 target) {
            if (IsTurning) return false; // mid turn-around the nose is edge-on (B059)
            if (!EnemyBullet.InFiringCone(from, target, Forward, _fire.fighterConeDegrees, _fire.range)) return false;
            Vector2 toPlayer = target - from;
            return toPlayer.sqrMagnitude <= 0.0001f
                || Vector2.Angle(NoseDirection, toPlayer) <= _fire.aimToleranceDegrees;
        }

        /// <summary>Clock for the attack slots: physics time in Play mode; frozen at 0 in
        /// EditMode, where tests drive the gun tick by tick (grace and hand-over gaps are
        /// covered by the FighterAttackSlots tests with explicit time).</summary>
        private static float SlotClock() {
            return Application.isPlaying ? Time.fixedTime : 0f;
        }

        // ── Smoke trail (B051) ──────────────────────────────────────────

        /// <summary>
        /// Smoke trail: counts new hits (damage went up since the last call), then while the
        /// fighter is alive and hit, spawns one FxManager enemy smoke puff at SmokeTailPoint
        /// every SmokeInterval(HitCount). The first puff comes at once after the first hit.
        /// Puffs live in world space (FxManager rises, grows, fades and caps them). A paused
        /// game (dt 0) and EditMode without a seeded FxManager emit nothing. Returns true when
        /// a puff was spawned.
        /// </summary>
        internal bool EmitSmoke(float dt) {
            if (_health == null) return false;
            float damage = _health.CurrentDamagePercent;
            if (damage > _lastDamagePercent) _hitsSeen++;
            _lastDamagePercent = damage;

            if (dt <= 0f) return false;
            float interval = SmokeInterval(HitCount, IsDead);
            if (interval <= 0f) {
                _smokeTimer = 0f;
                return false;
            }

            // The second hit thickens the trail at once (no waiting out the slow interval).
            if (_smokeTimer > interval) _smokeTimer = interval;
            _smokeTimer -= dt;
            if (_smokeTimer > 0f) return false;
            _smokeTimer = interval;

            var fx = FxManager.GetOrNull();
            if (fx == null) return false;
            fx.SpawnEnemySmokePuff(SmokeTailPoint(transform.position, _pitchAngle, _direction));
            return true;
        }

        /// <summary>
        /// Seconds between smoke puffs for a fighter with `hits` hits: 0 (no smoke) when it has
        /// none or is dead, SmokeIntervalLight after one, SmokeIntervalHeavy from two on.
        /// </summary>
        public static float SmokeInterval(int hits, bool dead) {
            if (dead || hits <= 0) return 0f;
            return hits == 1 ? SmokeIntervalLight : SmokeIntervalHeavy;
        }

        /// <summary>
        /// Hits implied by a damage total: `damagePercent` of `maxHitPoints` in hits of
        /// `hitDamage` points, rounded up (any damage > 0 is at least one hit). 0 for no damage;
        /// 1 for any damage when the hit size is unknown (hitDamage or maxHitPoints ≤ 0).
        /// </summary>
        public static int HitsFromDamage(float damagePercent, float maxHitPoints, float hitDamage) {
            if (damagePercent <= 0f) return 0;
            if (hitDamage <= 0f || maxHitPoints <= 0f) return 1;
            float hits = damagePercent / 100f * maxHitPoints / hitDamage;
            return Mathf.Max(1, Mathf.CeilToInt(hits - 0.001f));
        }

        /// <summary>
        /// World point SmokeTailOffset behind `center` along the drawn body: opposite the nose
        /// (PlanePitch.NoseDirection for `pitchDegrees` and flight direction `dir`), so the
        /// tail is on the right of a left-flying fighter and on the left of a right-flying one,
        /// raised or lowered with the pitch.
        /// </summary>
        public static Vector3 SmokeTailPoint(Vector3 center, float pitchDegrees, int dir) {
            Vector2 nose = PlanePitch.NoseDirection(pitchDegrees, dir);
            return center - (Vector3)(nose * SmokeTailOffset);
        }

        /// <summary>
        /// Sets the movement pattern and escort id from the mission spawn entry. Call right
        /// after Spawn (before Start). Empty or null values keep the defaults. A "static"
        /// fighter starts at its slowest flight, MinForwardSpeed (B061: never at rest).
        /// </summary>
        public void Configure(string movePattern, string escortId) {
            if (!string.IsNullOrEmpty(movePattern)) pattern = movePattern;
            escortOf = escortId ?? "";
            if (pattern == "static") _velX = _direction * MinForwardSpeed; // slowest flight (B061)
        }

        /// <summary>
        /// Sets the flight direction (B040) at once (no eased turn): any negative value = left,
        /// otherwise right. Mirrors the sprite, restores the full body width and points its
        /// current speed that way; the nose, gun cone and pitch follow from it. The chase
        /// (B059) turns it toward the player afterwards.
        /// </summary>
        public void SetFlightDirection(int dir) {
            _direction = dir < 0 ? -1 : 1;
            _heading = _direction;
            _turnTarget = _direction;
            _velX = _direction * Mathf.Abs(_velX);
            if (_sr != null) _sr.flipX = _direction < 0;
            Vector3 scale = transform.localScale;
            scale.x = 1f;
            transform.localScale = scale;
        }

        /// <summary>
        /// Escaped-fighter re-entry (B040, GDD "Enemy pressure"): moves this same fighter (same
        /// name, same damage) to `position`, flying `dir` at cruise speed, then chasing (B059).
        /// The pattern flavour clock and the pitch tracking restart there.
        /// </summary>
        public void ReEnter(Vector3 position, int dir) {
            FighterAttackSlots.Shared.Release(this); // re-entering frees its slot (B060)
            SetFlightDirection(dir);
            _velX = _direction * speed;
            _age = 0f;
            _escortChasing = false;
            _avoiding = false;
            transform.position = position;
            if (_rb != null) _rb.position = position;
            _prevY = position.y;
        }

        // ── Entry from the right edge (B074) ─────────────────────────────

        /// <summary>Entering from past the right screen edge and not yet in view (B074).</summary>
        public bool IsEntering => _entering;

        /// <summary>
        /// Marks this fighter as entering (B074): MissionRunner calls it right after placing a
        /// JSON fighter, a reinforcement or a re-entry past the right screen edge. Until its
        /// hitbox first overlaps the view, its top speed is cruise speed + EntryBoost(direction,
        /// camera x velocity), and when that boost is positive now (`cameraVelocityX`: the camera
        /// scrolls the way it flies) its velocity starts at that top speed, so it closes on the
        /// view at cruise speed in the camera's frame from the first tick. With the camera still
        /// or moving toward it the boost is 0 and nothing changes. MissionRunner passes
        /// BoostCameraVelocityNow (current or target camera velocity, whichever runs farther
        /// left, B075), and the per-tick boost uses the same rule.
        /// </summary>
        public void BeginEntry(float cameraVelocityX) {
            _entering = true;
            float boost = EntryBoost(_direction, cameraVelocityX);
            if (boost > 0f) _velX = _direction * (speed + boost);
        }

        /// <summary>Extra top speed while entering (B074, pure): the camera's speed along the
        /// fighter's flight direction `direction` (any negative = left), 0 when the camera is
        /// still or moves toward the fighter.</summary>
        public static float EntryBoost(int direction, float cameraVelocityX) {
            float d = direction < 0 ? -1f : 1f;
            return Mathf.Max(0f, d * cameraVelocityX);
        }

        /// <summary>The entry is over (B074, pure): a fighter hitbox (EnemyPressure.FighterHalfWidth)
        /// at `x` overlaps the view [viewXMin, viewXMax] horizontally.</summary>
        public static bool EntryComplete(float x, float viewXMin, float viewXMax) {
            return x - EnemyPressure.FighterHalfWidth <= viewXMax && x + EnemyPressure.FighterHalfWidth >= viewXMin;
        }

        /// <summary>
        /// Camera velocity the boost uses (B075, QA T975 BUG-2, pure): of the camera's current
        /// x velocity and the velocity it is easing toward (Direction x scrollSpeed), the one
        /// that runs farther along `direction`. Mid-turn the camera's velocity still lags its
        /// target, so a fighter boosted only by the current velocity started too slow and the
        /// reversing camera pulled the view away from it.
        /// </summary>
        public static float BoostCameraVelocity(int direction, float velocity, float targetVelocity) {
            return direction < 0 ? Mathf.Min(velocity, targetVelocity) : Mathf.Max(velocity, targetVelocity);
        }

        /// <summary>
        /// B075 (QA T975 BUG-2, pure): the boost also applies while the fighter flies left with
        /// the player ahead of it (`dxToPlayer` = player x - fighter x &lt; 0), i.e. it chases a
        /// left-flying player from behind. Entries only come from the right, so that is where
        /// they end up; without it a fighter slower than the scroll (M2-M4) fell off the right
        /// edge within 1-2 s. It ends by itself when the player turns right (the camera no longer
        /// runs left: EntryBoost 0) or the fighter turns, so no fighter keeps the extra speed.
        /// Facing right the chase is unchanged (fighters behind still fall away, B059).
        /// </summary>
        public static bool BehindBoostApplies(int facing, float dxToPlayer) {
            return facing < 0 && dxToPlayer < 0f;
        }

        /// <summary>BoostCameraVelocity for `direction` from the ScrollController (0 without one;
        /// a held scroll has no target speed).</summary>
        public static float BoostCameraVelocityNow(int direction) {
            var sc = ScrollController.Instance;
            if (sc == null) return 0f;
            float target = sc.IsHeld ? 0f : sc.Direction * sc.ScrollSpeed;
            return BoostCameraVelocity(direction, sc.Velocity, target);
        }

        /// <summary>This tick's entry boost: ends the entry once the fighter at `x` is in view
        /// (needs the camera), else EntryBoost for BoostCameraVelocityNow.</summary>
        private float EntryBoostNow(float x) {
            if (!_entering) return 0f;
            if (_cam != null) {
                Rect view = EnemyPressure.ViewRect(_cam);
                if (EntryComplete(x, view.xMin, view.xMax)) {
                    _entering = false;
                    return 0f;
                }
            }
            return EntryBoost(_direction, BoostCameraVelocityNow(_direction));
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
