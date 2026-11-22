using UnityEngine;
using Biplanes.Core;
using Biplanes.Weapons;
using Biplanes.Missions;
using Biplanes.Enemies;
using Biplanes.Fx;
using Biplanes.Audio;

namespace Biplanes.Player {

    /// <summary>
    /// Player biplane MonoBehaviour.
    ///
    /// Handles:
    ///  - Takeoff state (mission 1 starts on the runway).
    ///  - Mission start gate (B049): while the ScrollController is held the plane holds its
    ///    position; its first input (move, fire, bomb, or the takeoff) releases the scroll.
    ///  - Movement via Rigidbody2D.Kinematic + MovePosition in FixedUpdate.
    ///  - Riding along with the scrolling camera, clamped inside the camera rectangle
    ///    minus a 0.5 margin (ARCHITECTURE §3).
    ///  - Facing (Defender-style): A/D turn the plane left/right, the sprite flips and the
    ///    ScrollController scrolls in the facing direction. The plane's screen x eases toward
    ///    the trailing anchor (viewport screenAnchor facing right, 1 - screenAnchor facing left).
    ///  - Machine gun (PlayerInput.OnFire → Bullet.Spawn) and bombs
    ///    (PlayerInput.OnDropBomb → BombBay.TryDropBomb).
    ///  - Smoke trail when damage ≥ smokeThreshold (default 70%): a smoke_puff behind the
    ///    tail every SmokePuffInterval through FxManager (B030), plus the damage tint.
    ///  - Crash handling at 100% damage — input off at once, the mission is lost at once
    ///    (GameManager.LoseMission), and the plane falls and tumbles for CrashFallSeconds
    ///    (scripted, no physics) before GameManager opens the result screen (T937).
    ///
    /// Communicates through:
    ///  - PlayerInput.OnMove / OnFire / OnDropBomb events.
    ///  - Health component on the same GameObject (IDamageable).
    ///  - GameEvents for crash notification when GameManager is unavailable.
    /// </summary>
    public class PlayerPlane : MonoBehaviour {

        // ── Serialized fields ─────────────────────────────────────────────

        /// <summary>If ≥ 0, overrides the smoke-threshold from Balance.json.</summary>
        [SerializeField] private float _smokeThresholdOverride = -1f;

        // ── Runtime state ─────────────────────────────────────────────────

        private bool _onRunway;
        private bool _hasCrashed;

        /// <summary>Seconds of automatic climb left after takeoff.</summary>
        private float _climbOutTimer;
        private const float ClimbOutSeconds = 0.6f;
        private bool _inputSubscribed;

        /// <summary>Bounding rectangle corners in world space (positive X = right, positive Y = top).</summary>
        private Vector2 _boundsMin;   // bottom-left corner
        private Vector2 _boundsMax;   // top-right corner

        /// <summary>Camera x on the previous physics tick, used to ride along with scrolling.</summary>
        private float _lastCamX;
        private bool  _hasLastCamX;

        /// <summary>World-space velocity over the last physics tick (bombs inherit its x).</summary>
        private Vector2 _velocity;

        /// <summary>Flight attitude in degrees, nose up positive in either facing (T942/B029).</summary>
        private float _pitchAngle;

        /// <summary>World-space velocity over the last physics tick (scroll ride-along included).
        /// The kinematic body is moved with MovePosition, so Rigidbody2D.linearVelocity stays zero;
        /// read this instead (AAAGun leads its flak with it).</summary>
        public Vector2 Velocity => _velocity;

        /// <summary>+1 = facing right, -1 = facing left. The art faces right.</summary>
        private int _facing = 1;

        /// <summary>A/D held on the previous tick, so the last key pressed wins when both are held.</summary>
        private bool _prevLeft;
        private bool _prevRight;

        /// <summary>
        /// A was held when the plane took off. It is ignored until released and pressed
        /// again, so W+A on the runway doesn't turn the plane right after takeoff (B003).
        /// </summary>
        private bool _ignoreHeldLeft;

        // ── Crash fall (scripted, T937) ──────────────────────────────────

        /// <summary>Seconds the crashed plane falls before the result screen opens.
        /// GameManager.LoseMission(reason, CrashFallSeconds) delays the result by this much.</summary>
        public const float CrashFallSeconds = 1f;

        /// <summary>Downward acceleration of the falling wreck (world units/s²).</summary>
        private const float CrashGravity = 14f;
        /// <summary>Forward drift of the wreck (world units/s), in the facing direction.</summary>
        private const float CrashDriftSpeed = 1.5f;
        /// <summary>Nose-over spin of the wreck (degrees/s).</summary>
        private const float CrashSpinDegPerSec = 300f;
        /// <summary>Height of the wreck's centre above the terrain when it comes to rest.</summary>
        private const float CrashRestHeight = 0.2f;

        private Vector2 _crashVelocity;
        private float _crashAngle;
        private bool _crashLanded;

        // ── Mid-air plane collision (B025) ───────────────────────────────

        /// <summary>After a mid-air collision the player takes no further plane-collision
        /// damage for this long, so one contact (two overlapping enemies, re-entry) costs
        /// damageTaken.planeCollision once. Enemies touched meanwhile are still destroyed.</summary>
        public const float PlaneCollisionGraceSeconds = 0.5f;

        /// <summary>Seconds of plane-collision grace left; counted down in FixedUpdate.</summary>
        private float _planeCollisionGrace;

        // ── Smoke trail (B030) ───────────────────────────────────────────

        /// <summary>Seconds between smoke puffs while damage ≥ smokeThreshold (visual only).</summary>
        public const float SmokePuffInterval = 0.12f;
        /// <summary>Distance behind the plane centre, along the body, where puffs appear.</summary>
        private const float SmokeTailOffset = 0.75f;
        /// <summary>Seconds until the next puff; ≤ 0 emits at once.</summary>
        private float _smokeTimer;

        // ── Audio (T951, ARCHITECTURE §3 Audio) ──────────────────────────
        // Loops are mastered at the one-shot peak (-3 dBFS), so they run below 1 to sit
        // under the guns. Mix values, not gameplay tunables.

        /// <summary>AudioManager loop key for the engine drone.</summary>
        public const string EngineLoopKey = "player_engine";
        /// <summary>AudioManager loop key for the smoke crackle.</summary>
        public const string SmokeLoopKey  = "player_smoke";

        /// <summary>Engine loop volume in flight.</summary>
        public const float EngineFlightVolume = 0.65f;
        /// <summary>Engine loop volume idling on the runway.</summary>
        public const float EngineIdleVolume   = 0.35f;
        /// <summary>Engine loop pitch idling on the runway (below level flight's 1.0).</summary>
        public const float EngineIdlePitch = 0.8f;
        /// <summary>Smoke crackle loop volume while damage is at or above smokeThreshold.</summary>
        public const float SmokeCrackleVolume = 0.5f;

        /// <summary>Sputter at or above smokeThreshold: Perlin noise sampled at SputterRate per
        /// second; above SputterThreshold the engine dips by up to the pitch/volume fractions.</summary>
        private const float SputterRate = 3.5f;
        private const float SputterNoiseRow = 0.37f;
        private const float SputterThreshold = 0.6f;
        private const float SputterPitchDip = 0.12f;
        private const float SputterVolumeDip = 0.5f;

        /// <summary>Engine pitch the falling wreck winds down to before the loop stops on landing.</summary>
        private const float CrashEngineEndPitch = 0.45f;
        /// <summary>Length of the crash clip (seconds). Since B032 the clip is just the crunch
        /// and its tail; the engine loop's wind-down already gives the falling whine.</summary>
        public const float CrashClipSeconds = 1.0f;
        /// <summary>Where the ground crunch sits in the crash clip (seconds). The clip starts
        /// this long before the predicted landing, so the crunch lands with the wreck.</summary>
        public const float CrashCrunchOffset = 0.1f;

        /// <summary>The plane whose loops currently play under the shared keys, so an old plane
        /// torn down late never stops the new plane's engine.</summary>
        private static PlayerPlane _audioOwner;
        /// <summary>Set when the mission ended: no more loops from this plane.</summary>
        private bool _audioStopped;
        private bool _missionEndSubscribed;
        private bool _crashSoundPlayed;
        private float _crashElapsed;
        private float _crashStartPitch = 1f;
        private float _crashStartVolume = EngineFlightVolume;
        private float _enginePitch = EngineIdlePitch;
        private float _engineVolume = EngineIdleVolume;

        // ── Cached components ────────────────────────────────────────────

        private Rigidbody2D _rb;
        private SpriteRenderer _sr;
        private Health _health;
        private BombBay _bombBay;
        private Collider2D _collider;

        // ── Balance values (cached once in Awake) ────────────────────────

        private float _speed;
        private float _smokeThreshold;
        private float _screenAnchor;
        /// <summary>Resolved Balance pitch block (never null after Create/Awake).</summary>
        private PitchData _pitch;
        /// <summary>Resolved Balance bomb block (B036; never null after Create/Awake).</summary>
        private BombData _bomb;

        /// <summary>Used when Balance.json has no (or a zero) player.screenAnchor.</summary>
        private const float FallbackScreenAnchor = 0.3f;

        // ── Weapon offsets (local to the plane centre) ───────────────────

        private static readonly Vector3 GunOffset  = new Vector3(0.7f, 0f, 0f);
        private static readonly Vector3 BombOffset = new Vector3(0f, -0.35f, 0f);

        // ── Static factory ────────────────────────────────────────────────

        /// <summary>
        /// Static factory for scene construction. Creates the player biplane GameObject,
        /// attaches all required components, and positions it at *position*.
        /// </summary>
        public static PlayerPlane Create(Vector3 position, bool onRunway) {
            // Bullet and bomb pools must exist before the first shot.
            InitWeaponPools();

            var go = new GameObject("PlayerPlane");
            go.transform.position = position;

            var pp = go.AddComponent<PlayerPlane>();
            pp._rb = go.AddComponent<Rigidbody2D>();
            pp._rb.bodyType = RigidbodyType2D.Kinematic;
            // Enemies are kinematic too: needed so plane-vs-plane overlaps are reported.
            pp._rb.useFullKinematicContacts = true;
            pp._rb.linearVelocity = Vector2.zero;

            var sr = go.AddComponent<SpriteRenderer>();
            // Try to load the default player sprite; fall back to a 1×1 white if missing.
            sr.sprite = Resources.Load<Sprite>("Sprites/player/player_biplane");
            if (sr.sprite == null) {
                Debug.LogWarning("[PlayerPlane] Sprite 'Sprites/player/player_biplane' missing; using a solid fallback.");
                if (_fallbackPlaneSprite == null) _fallbackPlaneSprite = CreateSolidSprite(Color.white, 1, 1, 1f);
                sr.sprite = _fallbackPlaneSprite;
            }
            sr.sortingOrder = 20;
            pp._sr = sr;

            // ARCHITECTURE §3: every hittable object has a trigger Collider2D.
            // Added after the SpriteRenderer so it auto-sizes to the sprite.
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            pp._collider = col;

            // TeamTag before Health: Health reads the team in its Awake. Otherwise the plane
            // is Neutral and its own bullets would hit it.
            go.AddComponent<TeamTag>().team = Team.Player;
            pp._health = go.AddComponent<Health>();

            pp._bombBay = go.AddComponent<BombBay>();

            // Keyboard reader. Raises the static events this plane subscribes to in Start.
            go.AddComponent<PlayerInput>();

            // Initialise balance values (may be null before GameBalance is loaded)
            var g = GameBalance.Get();
            pp._onRunway   = onRunway;
            pp._speed      = g != null ? g.player.speed : 6.5f;   // fallback = Balance player.speed (B056)
            float rawSmoke = pp._smokeThresholdOverride >= 0f
                ? pp._smokeThresholdOverride
                : (g != null ? g.player.smokeThreshold : 70f);
            pp._smokeThreshold = rawSmoke;
            float anchor = g != null && g.player != null ? g.player.screenAnchor : 0f;
            pp._screenAnchor = anchor > 0f ? anchor : FallbackScreenAnchor;
            pp._facing = 1;
            pp._ignoreHeldLeft = false;
            pp._pitch = PitchData.Resolve(g);
            pp._bomb = BombData.Resolve(g);
            pp._pitchAngle = 0f;
            sr.flipX = false;

            // Clamp to initial camera bounds.
            pp._hasCrashed   = false;
            pp.ComputeScreenBounds();
            pp.ClampPosition();
            pp.UpdateSmokeTrail();

            return pp;
        }

        /// <summary>Creates a solid-colour sprite (fallback art and projectile placeholders).</summary>
        private static Sprite CreateSolidSprite(Color color, int width, int height, float pixelsPerUnit) {
            var tex = new Texture2D(width, height);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        }

        // ── Weapon pools ──────────────────────────────────────────────────

        /// <summary>Projectile and fallback sprites, built once and shared by every mission (B006a).</summary>
        private static Sprite _bulletSprite;
        private static Sprite _bombSprite;

        /// <summary>Resources path of the player bomb sprite (A-BOMB: 32x14 px at 100 px/unit,
        /// nose along +x, pivot centre, drawn untinted).</summary>
        private const string BombSpritePath = "Sprites/player/player_bomb";

        /// <summary>Bomb trigger size in world units, matching the drawn body of player_bomb
        /// (0.32 x 0.14 units minus the transparent edge).</summary>
        private static readonly Vector2 BombColliderSize = new Vector2(0.3f, 0.12f);
        private static Sprite _fallbackPlaneSprite;

        /// <summary>
        /// (Re)creates the shared Bullet and Bomb pools. Called once per mission from Create,
        /// so pools never point at objects destroyed by a scene reload. InitPool disposes the
        /// previous pool and its items; the sprites are cached, so no texture leaks per mission.
        /// </summary>
        private static void InitWeaponPools() {
            if (_bulletSprite == null) _bulletSprite = CreateSolidSprite(Color.white, 6, 2, 32f);
            if (_bombSprite == null)   _bombSprite   = LoadBombSprite();
            var bulletSprite = _bulletSprite;
            var bombSprite   = _bombSprite;

            Bullet.InitPool(() => MakeProjectile<Bullet>("Bullet", bulletSprite, 0.08f), 40);
            Bomb.InitPool(() => MakeBombProjectile(bombSprite), 10);
        }

        /// <summary>The player_bomb sprite, or (with one warning) the old solid placeholder
        /// when it is missing. Called once; the result is cached in _bombSprite.</summary>
        private static Sprite LoadBombSprite() {
            var sprite = Resources.Load<Sprite>(BombSpritePath);
            if (sprite != null) return sprite;
            Debug.LogWarning($"PlayerPlane: sprite Resources/{BombSpritePath} not found; bombs use a solid placeholder.");
            return CreateSolidSprite(Color.white, 4, 6, 32f);
        }

        /// <summary>One pooled player bomb: the shared projectile body with a horizontal capsule
        /// trigger sized to the bomb sprite instead of a circle. The capsule turns with the
        /// body, so it follows the nose along the path (B036).</summary>
        private static Bomb MakeBombProjectile(Sprite sprite) {
            var go = new GameObject("Bomb");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 15;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;

            var col = go.AddComponent<CapsuleCollider2D>();
            col.isTrigger = true;
            col.direction = CapsuleDirection2D.Horizontal;
            col.size = BombColliderSize;

            return go.AddComponent<Bomb>();
        }

        /// <summary>Builds one pooled projectile: sprite, kinematic body, trigger collider.</summary>
        private static T MakeProjectile<T>(string name, Sprite sprite, float radius) where T : Projectile {
            var go = new GameObject(name);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 15;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = radius;

            return go.AddComponent<T>();
        }

        // ── Unity lifecycle ───────────────────────────────────────────────

        private void Awake() {
            if (_rb == null) _rb = GetComponent<Rigidbody2D>();
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (_health == null) _health = GetComponent<Health>();
            if (_bombBay == null) _bombBay = GetComponent<BombBay>();
            if (_collider == null) _collider = GetComponent<Collider2D>();
            if (_pitch == null) _pitch = PitchData.Resolve(GameBalance.Get());
            if (_bomb == null) _bomb = BombData.Resolve(GameBalance.Get());

            // Compute screen bounds from the main camera (may be set up later).
            ComputeScreenBounds();

            // Initial clamping for onRunway start position.
            ClampPosition();
        }

        private void Start() {
            if (_bombBay == null) _bombBay = GetComponent<BombBay>();

            ComputeScreenBounds();
            ClampPosition();

            // Subscribe to input events (once, in Start to avoid Awake ordering issues).
            if (!_inputSubscribed) {
                PlayerInput.OnMove     += OnMove;
                PlayerInput.OnFire     += OnFire;
                PlayerInput.OnDropBomb += OnDropBomb;
                _inputSubscribed = true;
            }
            if (!_missionEndSubscribed) {
                GameEvents.MissionEnded += OnMissionEndedAudio;
                _missionEndSubscribed = true;
            }
        }

        private void OnDestroy() {
            UnsubscribeInput();
            if (_missionEndSubscribed) {
                GameEvents.MissionEnded -= OnMissionEndedAudio;
                _missionEndSubscribed = false;
            }
        }

        private void OnDisable() {
            StopPlayerLoops();
        }

        /// <summary>Visual and audio per-frame work: smoke puffs, engine and smoke loops.</summary>
        private void Update() {
            EmitSmoke(Time.deltaTime);
            UpdateAudio();
        }

        /// <summary>Called every FixedUpdate to check for ground contact and crash.</summary>
        private void FixedUpdate() {
            // A crashed plane only falls; nothing can hit or crash it again.
            if (_hasCrashed) {
                UpdateCrashFall(Time.fixedDeltaTime);
                return;
            }

            TickPlaneCollisionGrace(Time.fixedDeltaTime);

            // Terrain has no collider (ARCHITECTURE §3 Physics): compare against HeightAt.
            // Health multiplies by Balance damageTaken.collision (100), so 1 = full crash.
            var terrain = TerrainStrip.Instance;
            if (!_onRunway && !_hasCrashed && terrain != null && _health != null && _collider != null) {
                Vector2 low = HitboxLowestPoint();
                if (low.y <= terrain.HeightAt(low.x)) {
                    _health.ApplyDamage(1f, DamageSource.Collision);
                }
            }

            // Check damage against 100% (Health raises events but we need the local trigger).
            if (_health != null && _health.CurrentDamagePercent >= 100f && !_hasCrashed) {
                TriggerCrash();
            }
        }

        /// <summary>
        /// Mid-air collision with an enemy plane (fighter or bomber), B025: the enemy is
        /// destroyed (Collision is always fatal for enemies, so score and EntityDestroyed are
        /// as before) and the player takes damageTaken.planeCollision percent (50) through
        /// Health. The plane crashes only when its total damage reaches 100%.
        /// No double hit: a dead enemy is ignored (a bomber's other hitboxes, re-entry), and
        /// for PlaneCollisionGraceSeconds after a hit further contacts don't damage the player.
        /// Terrain, buildings, trees, balloons, bases and zeppelins keep their own fatal rules.
        /// </summary>
        private void OnTriggerEnter2D(Collider2D other) {
            if (_hasCrashed || _health == null) return;
            if (other.GetComponentInParent<FighterAI>() == null
                && other.GetComponentInParent<BomberAI>() == null) return;

            var enemy = other.GetComponentInParent<Health>();
            if (enemy == null || enemy.IsDead) return;

            enemy.ApplyDamage(1f, DamageSource.Collision);

            if (_planeCollisionGrace > 0f) return;
            _planeCollisionGrace = PlaneCollisionGraceSeconds;

            _health.ApplyDamage(1f, DamageSource.PlaneCollision);
            UpdateSmokeTrail();
            if (_health.CurrentDamagePercent >= 100f) TriggerCrash();
        }

        /// <summary>Counts the plane-collision grace window down by `dt` seconds.</summary>
        private void TickPlaneCollisionGrace(float dt) {
            if (_planeCollisionGrace > 0f) _planeCollisionGrace = Mathf.Max(0f, _planeCollisionGrace - dt);
        }

        private void UnsubscribeInput() {
            PlayerInput.OnMove     -= OnMove;
            PlayerInput.OnFire     -= OnFire;
            PlayerInput.OnDropBomb -= OnDropBomb;
            _inputSubscribed = false;
        }

        // ── Screen bounds (ARCHITECTURE §3: player stays inside camera minus 0.5 margin) ──

        /// <summary>Computes the clamp rectangle from Camera.main viewport corners.</summary>
        private void ComputeScreenBounds() {
            var cam = Camera.main;
            if (cam == null) {
                // Default to a size-5 orthographic camera (10 tall, ~17.8 wide @ 16:9).
                _boundsMin = new Vector2(-9f + _margin, -5f + _margin);
                _boundsMax = new Vector2( 9f - _margin,  5f - _margin);
                return;
            }

            // ViewportToWorldPoint of the bottom-left and top-right corners.
            var bottomLeft = cam.ViewportToWorldPoint(Vector3.zero);
            var topRight   = cam.ViewportToWorldPoint(new Vector3(1, 1, 0));

            _boundsMin = new Vector2(bottomLeft.x + _margin, bottomLeft.y + _margin);
            _boundsMax = new Vector2(topRight.x - _margin, topRight.y - _margin);
        }

        /// <summary>Clamps a world position to [_boundsMin, _boundsMax].</summary>
        private Vector2 Clamp(Vector2 pos) {
            pos.x = Mathf.Clamp(pos.x, _boundsMin.x, _boundsMax.x);
            pos.y = Mathf.Clamp(pos.y, _boundsMin.y, _boundsMax.y);
            return pos;
        }

        /// <summary>Clamps the transform.position to [_boundsMin, _boundsMax] (setup only).</summary>
        private void ClampPosition() {
            var clamped = Clamp(transform.position);
            transform.position = new Vector3(clamped.x, clamped.y, transform.position.z);
        }

        /// <summary>0.5 world-unit margin from camera edge (ARCHITECTURE §3).</summary>
        private const float _margin = 0.5f;

        // ── Movement: PlayerInput.OnMove fires every FixedUpdate ──────────

        /// <summary>
        /// Called by PlayerInput.OnMove every FixedUpdate.
        /// Handles takeoff logic, scroll ride-along, kinematic movement, bounds clamping
        /// and the smoke check. All position changes go through one MovePosition call.
        /// </summary>
        public void OnMove(InputState state) {
            // An old plane deactivated by MissionRunner's teardown stays subscribed to the
            // static input events until its deferred Destroy; ignore it meanwhile.
            if (_hasCrashed || _rb == null || !gameObject.activeInHierarchy) return;

            // How far the camera scrolled since the last tick.
            var cam = Camera.main;
            float camX = cam != null ? cam.transform.position.x : 0f;
            float scrollDx = _hasLastCamX ? camX - _lastCamX : 0f;
            _lastCamX = camX;
            _hasLastCamX = true;

            ComputeScreenBounds();

            Vector2 start = _rb.position;
            Vector2 target = start;

            // ── Mission start gate (B049): until the first input nothing moves ──
            // PlayerInputGate only lets real presses through (a key held through the briefing
            // is spent), so any action here is the player's first input. On the runway the
            // takeoff below is that input (same gate); airborne any action starts the mission
            // and is handled normally this tick.
            if (!_onRunway && IsWaitingForStart) {
                if (!PlayerInputGate.AnyAction(state)) {
                    _velocity = Vector2.zero;
                    _prevLeft  = state.left;
                    _prevRight = state.right;
                    return;
                }
                BeginMission();
            }

            // ── Takeoff logic (mission 1: start on runway) ──────────────────
            bool tookOffThisTick = false;
            if (_onRunway) {
                // Pressing W (climb) or D (forward-then-climb) triggers takeoff, facing right.
                // A does nothing on the runway.
                if (state.up || state.right) {
                    BeginMission();
                    _onRunway = false;
                    tookOffThisTick = true;
                    // A held through takeoff stays ignored until it is released (B003).
                    _ignoreHeldLeft = state.left;
                    _climbOutTimer = ClimbOutSeconds;
                    Debug.Log("[PlayerPlane] Takeoff — leaving the runway.");
                }
            }

            // Climb-out (ARCHITECTURE §3 Player): right after takeoff the plane climbs on its
            // own, so leaving the runway with D alone doesn't scrape the ground.
            if (_climbOutTimer > 0f) {
                _climbOutTimer -= Time.fixedDeltaTime;
                if (!state.down) state.up = true;
            }

            // An A held since takeoff counts as not pressed until it is released, so the
            // next real press is a fresh one (B003).
            if (_ignoreHeldLeft) {
                if (state.left) state.left = false;
                else _ignoreHeldLeft = false;
            }

            if (!_onRunway) {
                // Airborne: A/D turn the plane (the last key pressed wins). Not on the
                // takeoff tick, so W+A held on the runway still takes off facing right.
                if (!tookOffThisTick) UpdateFacing(state);

                // Ride along with the scrolling camera…
                target.x += scrollDx;

                // …ease the screen x toward the trailing anchor for the current facing…
                if (cam != null) {
                    float anchorViewport = _facing > 0 ? _screenAnchor : 1f - _screenAnchor;
                    float halfWidth = cam.orthographicSize * cam.aspect;
                    float anchorX = camX + (anchorViewport - 0.5f) * 2f * halfWidth;
                    target.x = Mathf.MoveTowards(target.x, anchorX, _speed * Time.fixedDeltaTime);
                }

                // …plus W/S climb and dive.
                float v = state.up ? 1f : (state.down ? -1f : 0f);
                target.y += v * _speed * Time.fixedDeltaTime;
            }
            _prevLeft  = state.left;
            _prevRight = state.right;

            // ── Clamp to screen bounds every tick ───────────────────────────
            // On the runway the plane stays put until the camera's left edge reaches it.
            target = Clamp(target);
            _velocity = (target - start) / Time.fixedDeltaTime;
            _rb.MovePosition(target);

            // ── Pitch (GDD Flight attitude): eased toward the climb/dive, the body (sprite
            // and hitbox) turned through the Rigidbody2D like the position (B029) ──
            if (_pitch == null) _pitch = PitchData.Resolve(GameBalance.Get());
            _pitchAngle = PlanePitch.Step(_pitchAngle, _velocity.y, _pitch, Time.fixedDeltaTime);
            _rb.MoveRotation(BodyAngle);

            // ── Update smoke trail visual ───────────────────────────────────
            UpdateSmokeTrail();
        }

        // ── Weapons ───────────────────────────────────────────────────────

        /// <summary>Machine gun: one bullet per PlayerInput.OnFire (autofire is paced there).</summary>
        private void OnFire() {
            if (_hasCrashed || _onRunway || !gameObject.activeInHierarchy) return;
            BeginMission(); // the fire button can be the first input (B049)

            // The gun sits GunOffset.x ahead of the centre along the drawn nose (mirrored by
            // the facing), and the round flies along the nose (GDD Flight attitude).
            Vector2 nose = NoseDirection;
            Vector3 muzzle = transform.position + (Vector3)(nose * GunOffset.x);
            float angle = Mathf.Atan2(nose.y, nose.x) * Mathf.Rad2Deg;
            Bullet bullet = Bullet.Spawn(muzzle, Quaternion.Euler(0f, 0f, angle), nose);
            if (bullet == null) return; // pool exhausted: no bullet, no rattle (B032)
            var audio = AudioManager.GetOrNull();
            if (audio != null) audio.Play(SfxId.PlayerGun, muzzle);
        }

        /// <summary>Flight attitude in degrees, nose up positive in either facing, within
        /// ±pitch.maxDegrees.</summary>
        public float PitchAngle => _pitchAngle;

        /// <summary>The z rotation (degrees) the body is turned to with MoveRotation:
        /// PitchAngle * Facing, so the drawn nose points up when climbing in either facing.</summary>
        public float BodyAngle => PlanePitch.BodyAngle(_pitchAngle, _facing);

        /// <summary>World unit vector of the drawn nose: (Facing * cos p, sin p). Facing left
        /// it points left; climbing it points up. The machine gun fires along it.</summary>
        public Vector2 NoseDirection => PlanePitch.NoseDirection(_pitchAngle, _facing);

        /// <summary>
        /// Lowest world point of the hitbox at the current pitch, for the terrain check. It is
        /// computed from the box and BodyAngle rather than Collider2D.bounds, so it doesn't lag
        /// a physics step behind MoveRotation. Pitched, a box corner (tail or nose) is lowest:
        /// at 15 degrees it reaches about 0.19 lower than level (1.6x0.8 box), accepted (B029).
        /// </summary>
        public Vector2 HitboxLowestPoint() {
            Vector2 center = _rb != null ? _rb.position : (Vector2)transform.position;
            var box = _collider as BoxCollider2D;
            if (box == null) {
                var b = _collider != null ? _collider.bounds : new Bounds(center, Vector3.zero);
                return new Vector2(b.center.x, b.min.y);
            }
            Vector3 scale = transform.lossyScale;
            Vector2 size = new Vector2(box.size.x * Mathf.Abs(scale.x), box.size.y * Mathf.Abs(scale.y));
            Vector2 offset = new Vector2(box.offset.x * scale.x, box.offset.y * scale.y);
            float y = PlanePitch.LowestPointOfBox(center, size, offset, BodyAngle);
            return new Vector2(center.x, y);
        }

        // ── Mission start gate (B049) ─────────────────────────────────────

        /// <summary>True while the mission waits for the player's first input: the
        /// ScrollController is held, and the plane holds its position (airborne) or stays on
        /// the runway. MissionRunner holds the scroll in StartMission.</summary>
        public static bool IsWaitingForStart {
            get {
                var sc = ScrollController.Instance;
                return sc != null && sc.IsHeld;
            }
        }

        /// <summary>The player's first input: release the scroll (no-op once started).</summary>
        private static void BeginMission() {
            var sc = ScrollController.Instance;
            if (sc == null || !sc.IsHeld) return;
            sc.Release();
            Debug.Log("[PlayerPlane] First input — mission start.");
        }

        // ── Facing ────────────────────────────────────────────────────────

        /// <summary>
        /// D faces right, A faces left; with both held the most recent press wins, and with
        /// neither held the facing is kept. A change flips the sprite and the scroll direction.
        /// </summary>
        private void UpdateFacing(InputState state) {
            int facing = _facing;
            if (state.left && !state.right) {
                facing = -1;
            } else if (state.right && !state.left) {
                facing = 1;
            } else if (state.left && state.right) {
                bool newLeft  = !_prevLeft;
                bool newRight = !_prevRight;
                if (newLeft && !newRight) facing = -1;
                else if (newRight && !newLeft) facing = 1;
            }
            if (facing == _facing) return;

            _facing = facing;
            if (_sr != null) _sr.flipX = _facing < 0;
            var sc = ScrollController.Instance;
            if (sc != null) sc.SetDirection(_facing);
        }

        /// <summary>Drop one bomb per Space press. The bomb starts with the plane's world
        /// velocity (x and y) times Balance bomb.inheritFactor, then falls ballistically (B036).
        /// Its forward x is at least the cruise speed (scrollSpeed) along Facing, so a bomb
        /// dropped during a turn's camera ease or anchor slide still leaves the way the plane
        /// faces (B057, BombBallistics.InheritedVelocity).</summary>
        private void OnDropBomb() {
            if (_hasCrashed || _onRunway || _bombBay == null || !gameObject.activeInHierarchy) return;
            BeginMission(); // the bomb key can be the first input (B049)
            float inherit = _bomb != null ? _bomb.inheritFactor : BombData.DefaultInheritFactor;
            var sc = ScrollController.Instance;
            float cruise = sc != null ? sc.ScrollSpeed : 0f;
            _bombBay.TryDropBomb(transform.position + BombOffset,
                BombBallistics.InheritedVelocity(_velocity, _facing, cruise, inherit));
        }

        // ── Smoke trail (≥ 70% damage per GDD) ───────────────────────────

        /// <summary>True while puffs should be emitted: damage ≥ smokeThreshold and the plane
        /// is flying, or falling as a wreck (not yet landed).</summary>
        private bool IsSmoking =>
            _health != null && _health.CurrentDamagePercent >= _smokeThreshold
            && !_crashLanded && gameObject.activeInHierarchy;

        /// <summary>
        /// Smoke trail (B030): while IsSmoking, one FxManager smoke puff every SmokePuffInterval
        /// behind the tail. Puffs live in world space (they don't follow the plane); FxManager
        /// rises, grows, fades and caps them. Below the threshold no puffs. A paused game
        /// (dt 0) emits nothing.
        /// </summary>
        private void EmitSmoke(float dt) {
            if (dt <= 0f) return;
            if (!IsSmoking) {
                _smokeTimer = 0f;   // the first puff comes at once when the threshold is crossed
                return;
            }

            _smokeTimer -= dt;
            if (_smokeTimer > 0f) return;
            _smokeTimer = SmokePuffInterval;

            var fx = FxManager.GetOrNull();
            if (fx == null) return;
            // The drawn nose in world space, from the body rotation (pitch, or the wreck's spin).
            Vector3 nose = transform.rotation * new Vector3(_facing, 0f, 0f);
            fx.SpawnSmokePuff(transform.position - nose * SmokeTailOffset);
        }

        /// <summary>Damage tint of the plane sprite, white only (no colour, per the style guide):
        /// untinted ink white below smokeThreshold; at the threshold the sprite drops halfway
        /// toward pencil grey, reaching full PencilGreyTint at 100% damage. The smoke puffs
        /// themselves are emitted by EmitSmoke from Update.</summary>
        private void UpdateSmokeTrail() {
            if (_health == null || _sr == null) return;
            var col = DamageTint(_health.CurrentDamagePercent, _smokeThreshold);
            col.a = _sr.color.a;
            _sr.color = col;
        }

        /// <summary>Pencil grey #9DB4CC, the plane sprite's tint at 100% damage.</summary>
        private static readonly Color PencilGreyTint = new Color(0.616f, 0.706f, 0.8f, 1f);

        /// <summary>Sprite tint for `damagePercent`: white below `smokeThreshold`, then from
        /// halfway to pencil grey at the threshold up to full pencil grey at 100%.</summary>
        private static Color DamageTint(float damagePercent, float smokeThreshold) {
            if (damagePercent < smokeThreshold) return Color.white;
            float span = 100f - smokeThreshold;
            float t = span > 0f ? Mathf.Clamp01((damagePercent - smokeThreshold) / span) : 1f;
            return Color.Lerp(Color.white, PencilGreyTint, 0.5f + 0.5f * t);
        }

        // ── Crash handling ────────────────────────────────────────────────

        /// <summary>
        /// Triggers the crash flow (only at 100% damage, including a terrain/obstacle crash,
        /// or ForceCrash from a zeppelin ram):
        ///  1. input off at once (unsubscribed and PlayerInput disabled), collider off;
        ///  2. the mission is lost at once through GameManager.LoseMission, so no win can
        ///     follow, but the result screen opens CrashFallSeconds later (real time);
        ///  3. meanwhile FixedUpdate drops and spins the wreck (UpdateCrashFall) while the
        ///     rest of the world keeps running.
        /// </summary>
        private void TriggerCrash() {
            if (_hasCrashed) return;   // guard against double-trigger
            _hasCrashed = true;

            Debug.Log("[PlayerPlane] Crashed! — falling, then GameState.Lost result");

            // Input is off before anything else.
            UnsubscribeInput();
            var input = GetComponent<PlayerInput>();
            if (input != null) input.enabled = false;
            if (_collider != null) _collider.enabled = false;

            // The wreck keeps a little forward drift and whatever dive it already had.
            _crashVelocity = new Vector2(_facing * CrashDriftSpeed, Mathf.Min(_velocity.y, 0f));
            // Tumble from the attitude the plane had (the pending MoveRotation target).
            _crashAngle = BodyAngle;
            _crashLanded = false;
            _crashElapsed = 0f;
            _crashSoundPlayed = false;
            _crashStartPitch = _enginePitch;
            _crashStartVolume = _engineVolume;
            // Without a body there is no fall to time the crunch against.
            if (_rb == null) PlayCrashSound();

            // GetOrNull: no GameManager (and no DontDestroyOnLoad) outside Play mode (B047).
            var gm = GameManager.GetOrNull();
            if (gm != null) {
                gm.LoseMission(GameManager.DefaultLossReason, CrashFallSeconds);
            } else {
                // Fallback: raise the mission-ended event so missions can react.
                GameEvents.RaiseMissionEnded(false, GameManager.DefaultLossReason);
            }
        }

        /// <summary>
        /// One physics tick of the scripted crash fall: gravity, forward drift and a nose-over
        /// spin, moved with MovePosition/MoveRotation. The wreck stops on the terrain
        /// (TerrainStrip.HeightAt, no collider) with one explosion.
        /// </summary>
        private void UpdateCrashFall(float dt) {
            if (_crashLanded || _rb == null || dt <= 0f) return;

            _crashElapsed += dt;
            _crashVelocity.y -= CrashGravity * dt;
            Vector2 pos = _rb.position + _crashVelocity * dt;
            _crashAngle -= _facing * CrashSpinDegPerSec * dt; // nose goes down in the facing direction

            var terrain = TerrainStrip.Instance;
            float ground = terrain != null ? terrain.HeightAt(pos.x) : TerrainStrip.GroundBaseY;

            // Start the crash clip CrashCrunchOffset before the predicted landing, so its
            // crunch lands with the wreck (or when the result screen freezes a long fall).
            if (!_crashSoundPlayed
                && ShouldStartCrashSound(
                    CrashTimeToGround(pos.y - (ground + CrashRestHeight), _crashVelocity.y, CrashGravity),
                    _crashElapsed)) {
                PlayCrashSound();
            }

            if (pos.y <= ground + CrashRestHeight) {
                pos.y = ground + CrashRestHeight;
                _crashLanded = true;
                if (!_crashSoundPlayed) PlayCrashSound();
                var fx = FxManager.Instance;
                if (fx != null) fx.TriggerExplosion(pos);
            }

            _rb.MovePosition(pos);
            _rb.MoveRotation(_crashAngle);
        }

        /// <summary>Direct crash trigger (used if a collision component calls this).</summary>
        public void ForceCrash() {
            TriggerCrash();
        }

        // ── Audio (T951) ──────────────────────────────────────────────────

        /// <summary>
        /// Engine and smoke loops, once per frame. Engine: idle pitch/volume on the runway;
        /// in flight AudioMix.EnginePitch(vy, damage, pitch.fullPitchSpeed) plus an irregular
        /// sputter at or above smokeThreshold; while the wreck falls the pitch winds down to
        /// CrashEngineEndPitch and the loop stops on landing. The smoke crackle runs while
        /// IsSmoking. Does nothing without an AudioManager (EditMode).
        /// </summary>
        private void UpdateAudio() {
            if (_audioStopped || !gameObject.activeInHierarchy) return;
            var audio = AudioManager.GetOrNull();
            if (audio == null) return;
            _audioOwner = this;

            if (_hasCrashed) {
                if (_crashLanded) {
                    audio.StopLoop(EngineLoopKey);
                } else {
                    float t = Mathf.Clamp01(_crashElapsed / CrashFallSeconds);
                    _enginePitch = Mathf.Lerp(_crashStartPitch, CrashEngineEndPitch, t);
                    _engineVolume = Mathf.Lerp(_crashStartVolume, _crashStartVolume * 0.5f, t);
                    audio.SetLoopPitch(EngineLoopKey, _enginePitch);
                    audio.SetLoopVolume(EngineLoopKey, _engineVolume);
                }
            } else {
                audio.StartLoop(SfxId.EngineLoop, EngineLoopKey);
                float damage = _health != null ? _health.CurrentDamagePercent : 0f;
                if (_onRunway) {
                    _enginePitch = EngineIdlePitch;
                    _engineVolume = EngineIdleVolume;
                } else {
                    float fullPitchSpeed = _pitch != null ? _pitch.fullPitchSpeed : PlanePitch.DefaultFullPitchSpeed;
                    _enginePitch = AudioMix.EnginePitch(_velocity.y, damage, fullPitchSpeed);
                    _engineVolume = EngineFlightVolume;
                    if (damage >= _smokeThreshold) {
                        float dip = SputterAmount(Time.time);
                        _enginePitch *= 1f - dip * SputterPitchDip;
                        _engineVolume *= 1f - dip * SputterVolumeDip;
                    }
                }
                audio.SetLoopPitch(EngineLoopKey, _enginePitch);
                audio.SetLoopVolume(EngineLoopKey, _engineVolume);
            }

            if (IsSmoking) {
                audio.StartLoop(SfxId.SmokeCrackle, SmokeLoopKey);
                audio.SetLoopVolume(SmokeLoopKey, SmokeCrackleVolume);
            } else {
                audio.StopLoop(SmokeLoopKey);
            }
        }

        /// <summary>Sputter depth 0..1 at time t: Perlin noise above SputterThreshold, so the
        /// engine gives brief irregular coughs rather than a steady wobble. Deterministic in t.</summary>
        public static float SputterAmount(float t) {
            float n = Mathf.PerlinNoise(t * SputterRate, SputterNoiseRow);
            if (n <= SputterThreshold) return 0f;
            return Mathf.Clamp01((n - SputterThreshold) / (1f - SputterThreshold));
        }

        /// <summary>Seconds until a body `height` above its rest point, moving at vy (up is
        /// positive) under `gravity`, reaches it. 0 when it is already there.</summary>
        public static float CrashTimeToGround(float height, float vy, float gravity) {
            if (height <= 0f) return 0f;
            if (gravity <= 0f) return vy < 0f ? height / -vy : float.PositiveInfinity;
            return (vy + Mathf.Sqrt(vy * vy + 2f * gravity * height)) / gravity;
        }

        /// <summary>
        /// Whether the falling wreck should start the crash clip this tick (B032). True when
        /// the predicted time to ground is within CrashCrunchOffset, so the crunch (at
        /// CrashCrunchOffset into the clip) lands with the wreck. Also true once
        /// elapsed + CrashCrunchOffset reaches CrashFallSeconds: the result screen freezes time
        /// then, and a longer fall (top altitude, about 1.12 s) would otherwise never land
        /// and stay silent, so the crunch marks the result screen instead.
        /// </summary>
        public static bool ShouldStartCrashSound(float timeToGround, float elapsed) {
            return timeToGround <= CrashCrunchOffset || elapsed + CrashCrunchOffset >= CrashFallSeconds;
        }

        /// <summary>
        /// The crash clip, once per crash. It plays on the UI pool (PlayUi) on purpose: the
        /// result screen freezes time CrashFallSeconds after the crash, which pauses the
        /// listener and would cut a gameplay voice. A scene unload (Retry, Menu) still stops
        /// it, because Crash is not a UI-screen cue (AudioManager.KeepsPlayingOnSceneUnload).
        /// </summary>
        private void PlayCrashSound() {
            if (_crashSoundPlayed) return;
            _crashSoundPlayed = true;
            var audio = AudioManager.GetOrNull();
            if (audio != null) audio.PlayUi(SfxId.Crash);
        }

        /// <summary>Mission over: a flying plane's loops stop at once. A crashing plane keeps
        /// its engine wind-down (the loss is raised at crash start) and stops it on landing.</summary>
        private void OnMissionEndedAudio(bool won, string reason) {
            if (_hasCrashed) return;
            StopPlayerLoops();
            _audioStopped = true;
        }

        /// <summary>Stops the engine and smoke loops if this plane owns them. Safe from OnDisable
        /// at quit: GetOrNull never creates a manager while the application is quitting (B032).</summary>
        private void StopPlayerLoops() {
            if (_audioOwner != this) return;
            var audio = AudioManager.GetOrNull();
            if (audio != null) {
                audio.StopLoop(EngineLoopKey);
                audio.StopLoop(SmokeLoopKey);
            }
            _audioOwner = null;
        }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>+1 = facing right, -1 = facing left. Starts at +1.</summary>
        public int Facing => _facing;

        /// <summary>Whether the player is still on the runway (takeoff not yet triggered).</summary>
        public bool OnRunway => _onRunway;

        /// <summary>Whether the plane has already been processed as crashed.</summary>
        public bool HasCrashed => _hasCrashed;
    }
}
