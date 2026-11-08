using UnityEngine;
using Biplanes.Core;
using Biplanes.Weapons;
using Biplanes.Missions;
using Biplanes.Enemies;

namespace Biplanes.Player {

    /// <summary>
    /// Player biplane MonoBehaviour.
    ///
    /// Handles:
    ///  - Takeoff state (mission 1 starts on the runway).
    ///  - Movement via Rigidbody2D.Kinematic + MovePosition in FixedUpdate.
    ///  - Riding along with the scrolling camera, clamped inside the camera rectangle
    ///    minus a 0.5 margin (ARCHITECTURE §3).
    ///  - Machine gun (PlayerInput.OnFire → Bullet.Spawn) and bombs
    ///    (PlayerInput.OnDropBomb → BombBay.TryDropBomb).
    ///  - Smoke trail visual when damage ≥ smokeThreshold (default 70%).
    ///  - Crash handling at 100% damage — notifies GameManager and raises GameEvents.
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

        [SerializeField] private Transform _smokeTrailRoot;

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

        // ── Cached components ────────────────────────────────────────────

        private Rigidbody2D _rb;
        private SpriteRenderer _sr;
        private Health _health;
        private BombBay _bombBay;
        private Collider2D _collider;

        // ── Balance values (cached once in Awake) ────────────────────────

        private float _speed;
        private float _smokeThreshold;

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
            sr.sprite = Resources.Load<Sprite>("Sprites/player/player_biplane") ?? CreateSolidSprite(Color.white, 1, 1, 1f);
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
            pp._speed      = g != null ? g.player.speed : 5f;
            float rawSmoke = pp._smokeThresholdOverride >= 0f
                ? pp._smokeThresholdOverride
                : (g != null ? g.player.smokeThreshold : 70f);
            pp._smokeThreshold = rawSmoke;

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

        /// <summary>
        /// (Re)creates the shared Bullet and Bomb pools. Called once per mission from Create,
        /// so pools never point at objects destroyed by a scene reload.
        /// </summary>
        private static void InitWeaponPools() {
            var bulletSprite = CreateSolidSprite(Color.white, 6, 2, 32f);
            var bombSprite   = CreateSolidSprite(Color.white, 4, 6, 32f);

            Bullet.InitPool(() => MakeProjectile<Bullet>("Bullet", bulletSprite, 0.08f), 40);
            Bomb.InitPool(() => MakeProjectile<Bomb>("Bomb", bombSprite, 0.12f), 10);
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
        }

        private void OnDestroy() {
            UnsubscribeInput();
        }

        /// <summary>Called every FixedUpdate to check for ground contact and crash.</summary>
        private void FixedUpdate() {
            // Terrain has no collider (ARCHITECTURE §3 Physics): compare against HeightAt.
            // Health multiplies by Balance damageTaken.collision (100), so 1 = full crash.
            var terrain = TerrainStrip.Instance;
            if (!_onRunway && !_hasCrashed && terrain != null && _health != null && _collider != null) {
                var b = _collider.bounds;
                if (b.min.y <= terrain.HeightAt(b.center.x)) {
                    _health.ApplyDamage(1f, DamageSource.Collision);
                }
            }

            // Check damage against 100% (Health raises events but we need the local trigger).
            if (_health != null && _health.CurrentDamagePercent >= 100f && !_hasCrashed) {
                TriggerCrash();
            }
        }

        /// <summary>
        /// Mid-air collision with an enemy plane (fighter or bomber) destroys both.
        /// Health multiplies by Balance damageTaken.collision (100), so 1 = full damage.
        /// </summary>
        private void OnTriggerEnter2D(Collider2D other) {
            if (_hasCrashed || _health == null) return;
            if (other.GetComponentInParent<FighterAI>() == null
                && other.GetComponentInParent<BomberAI>() == null) return;

            var enemy = other.GetComponentInParent<Health>();
            if (enemy == null || enemy.IsDead) return;

            enemy.ApplyDamage(1f, DamageSource.Collision);
            _health.ApplyDamage(1f, DamageSource.Collision);
            TriggerCrash();
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
            if (_hasCrashed || _rb == null) return;

            // How far the camera scrolled since the last tick.
            var cam = Camera.main;
            float camX = cam != null ? cam.transform.position.x : 0f;
            float scrollDx = _hasLastCamX ? camX - _lastCamX : 0f;
            _lastCamX = camX;
            _hasLastCamX = true;

            ComputeScreenBounds();

            Vector2 start = _rb.position;
            Vector2 target = start;

            // ── Takeoff logic (mission 1: start on runway) ──────────────────
            if (_onRunway) {
                // Pressing W (climb) or D (forward-then-climb) triggers takeoff.
                if (state.up || state.right) {
                    _onRunway = false;
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

            if (!_onRunway) {
                // Airborne: ride along with the scrolling camera…
                target.x += scrollDx;

                // …plus the player's own input.
                float h = state.left ? -1f : (state.right ? 1f : 0f);
                float v = state.up    ? 1f : (state.down ? -1f : 0f);
                if (h != 0f || v != 0f) {
                    var move = new Vector2(h, v);
                    // Normalise diagonal so it doesn't move faster along diagonals.
                    if (h != 0f && v != 0f) move *= 0.70710678f;
                    target += move * _speed * Time.fixedDeltaTime;
                }
            }

            // ── Clamp to screen bounds every tick ───────────────────────────
            // On the runway the plane stays put until the camera's left edge reaches it.
            target = Clamp(target);
            _velocity = (target - start) / Time.fixedDeltaTime;
            _rb.MovePosition(target);

            // ── Update smoke trail visual ───────────────────────────────────
            UpdateSmokeTrail();
        }

        // ── Weapons ───────────────────────────────────────────────────────

        /// <summary>Machine gun: one bullet per PlayerInput.OnFire (autofire is paced there).</summary>
        private void OnFire() {
            if (_hasCrashed || _onRunway) return;
            Bullet.Spawn(transform.position + GunOffset, Quaternion.identity);
        }

        /// <summary>Drop one bomb per Enter press; bombs inherit the plane's x-velocity.</summary>
        private void OnDropBomb() {
            if (_hasCrashed || _onRunway || _bombBay == null) return;
            _bombBay.TryDropBomb(transform.position + BombOffset, new Vector2(_velocity.x, 0f));
        }

        // ── Smoke trail (≥ 70% damage per GDD) ───────────────────────────

        /// <summary>Activates/deactivates the smoke trail visual based on current damage.</summary>
        private void UpdateSmokeTrail() {
            if (_health == null) return;

            float dmg = _health.CurrentDamagePercent;
            bool needsSmoke = dmg >= _smokeThreshold;

            // Toggle smoke trail root visibility.
            if (_smokeTrailRoot != null) {
                _smokeTrailRoot.gameObject.SetActive(needsSmoke);
            }

            // Tint the plane sprite amber when in danger zone so the player sees they're damaged.
            if (_sr != null) {
                var col = _sr.color;
                if (needsSmoke) {
                    col = new Color(0.9f, 0.55f, 0.2f, col.a); // deep amber
                } else if (dmg >= _smokeThreshold * 0.7f) {
                    col = new Color(1f, 0.85f, 0.3f, col.a);   // light warning tint
                } else {
                    col = new Color(1f, 1f, 1f, col.a);          // normal white
                }
                _sr.color = col;
            }
        }

        // ── Crash handling ────────────────────────────────────────────────

        /// <summary>Triggers the crash flow: sets lost state and raises mission-end event.</summary>
        private void TriggerCrash() {
            if (_hasCrashed) return;   // guard against double-trigger
            _hasCrashed = true;

            Debug.Log("[PlayerPlane] Crashed! — setting GameState.Lost");

            // Try GameManager first (may not exist yet in dev).
            var gm = GameManager.Instance;
            if (gm != null) {
                gm.SetState(GameManager.GameState.Lost);
            } else {
                // Fallback: raise the mission-ended event so missions can react.
                GameEvents.RaiseMissionEnded(false, "Player crashed");
            }

            // Disable all input while in lost state.
            UnsubscribeInput();
        }

        /// <summary>Direct crash trigger (used if a collision component calls this).</summary>
        public void ForceCrash() {
            TriggerCrash();
        }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>Whether the player is still on the runway (takeoff not yet triggered).</summary>
        public bool OnRunway => _onRunway;

        /// <summary>Whether the plane has already been processed as crashed.</summary>
        public bool HasCrashed => _hasCrashed;
    }
}
