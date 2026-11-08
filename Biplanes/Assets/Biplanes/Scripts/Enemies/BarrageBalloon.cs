using UnityEngine;
using Biplanes.Core;
using Biplanes.Player;
using Biplanes.Fx;

namespace Biplanes.Enemies {

    /// <summary>
    /// Barrage balloon: a floating obstacle protected by anti-aircraft fire.
    /// The player must shoot it down to score points and clear paths through AAA zones.
    ///
    /// • Health component with TeamTag(Enemy) for damage processing.
    /// • Drifts side-to-side in a sine wave (or vertical bob) depending on pattern.
    /// • Optional tether visual: a thin line drawn from the balloon toward the ground.
    /// • Static factory Spawn(position, team) for MissionRunner to instantiate from JSON.
    /// • Sprite path "Sprites/obstacles/barrage_balloon" loaded from Resources.
    /// </summary>
    public class BarrageBalloon : MonoBehaviour, IDamageable {

        // ── Serialized fields (tunables exposed in inspector) ──────────────

        [SerializeField] private float driftAmplitude = 0.8f;
        [SerializeField] private float driftSpeed   = 1.0f;
        [SerializeField] private Vector2 driftAxis  = Vector2.right;  // "drift" = horizontal by default
        [SerializeField] private float tetherLength = 5.0f;

        // ── IDamageable contract ──────────────────────────────────────────

        public Team Team { get; private set; }

        public bool IsDead => _health != null && _health.IsDead;

        // ── Cached components (per ARCHITECTURE §3: cache in Awake) ───────

        private Rigidbody2D     _rb;
        private SpriteRenderer   _sr;
        private Collider2D       _collider;
        private TeamTag          _teamTag;
        private Health           _health;
        private LineRenderer     _tetherLine;
        private int              _driftSeed;

        // ── Lifecycle ─────────────────────────────────────────────────────

        void Awake() {
            // Cache team tag to read the Team enum set by the caller.
            _teamTag = GetComponent<TeamTag>();
            Team = _teamTag != null ? _teamTag.Team : Team.Enemy;

            // Cache or add Rigidbody2D (Kinematic).
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) {
                _rb = gameObject.AddComponent<Rigidbody2D>();
            }
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.simulated = false; // Kinematic triggers only (ARCHITECTURE §3).

            // Sprite renderer.
            _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) {
                _sr = gameObject.AddComponent<SpriteRenderer>();
            }
            var sprite = Resources.Load<Sprite>("Sprites/obstacles/barrage_balloon");
            if (sprite != null) {
                _sr.sprite = sprite;
            } else {
                Debug.LogWarning($"BarrageBalloon '{name}': "
                    + "'Sprites/obstacles/barrage_balloon' not found in Resources — "
                    + "using 1×1 white placeholder.");
                _sr.sprite = CreateWhiteSprite();
            }

            // Health component.
            _health = GetComponent<Health>();
            if (_health == null) {
                _health = gameObject.AddComponent<Health>();
            }
            var balance = GameBalance.Get();
            _health.SetMaxHitPoints(balance != null && balance.enemyHealth != null
                ? balance.enemyHealth.barrageBalloon : 50f);

            // Trigger collider (use whatever size was set during Spawn).
            _collider = GetComponent<Collider2D>();

            // Tether line.
            SetupTether();

            // Randomise drift phase so multiple balloons don't move in sync.
            _driftSeed = Random.Range(0, 999);
        }

        // ── Trigger handling ──────────────────────────────────────────────

        void OnTriggerEnter2D(Collider2D other) {
            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null) return;

            switch (damageable.Team) {
                case Team.Player:
                    // Player bullets and bombs damage the balloon themselves (Projectile.OnHit),
                    // so nothing extra here.
                    break;

                case Team.Enemy:
                    // Enemy fire ignores balloons (balloons are friendly neutral).
                    break;

                default:
                    // Neutral projectiles also ignored.
                    break;
            }
        }

        // ── Per-frame drift movement ──────────────────────────────────────

        void FixedUpdate() {
            if (_health != null && _health.IsDead) {
                OnDestroyedBalloon();
                return;
            }

            var pos = transform.position;
            float t = Time.time * driftSpeed + _driftSeed;
            float offset = Mathf.Sin(t) * driftAmplitude;
            Vector3 drift3D = new Vector3(driftAxis.x, driftAxis.y, 0f).normalized
                            * offset * Time.fixedDeltaTime * driftSpeed;
            pos += drift3D;
            _rb.MovePosition(pos);
        }

        // ── IDamageable stubs (delegated to Health component) ─────────────

        public void ApplyDamage(float percent, DamageSource src) {
            if (_health != null) {
                _health.ApplyDamage(percent, src);
            }
        }

        public void Heal(float amount) {
            if (_health != null) {
                _health.Heal(amount);
            }
        }

        // ── Death handling ────────────────────────────────────────────────

        /// <summary>Called after Health reports the balloon is dead.</summary>
        private void OnDestroyedBalloon() {
            // Health already raised EntityDestroyed("barrageBalloon"); ScoreKeeper awards the
            // score and objectives count it. Just burst and clean up here.
            FxManager.Instance.TriggerExplosion(transform.position);

            // Destroy visual tether.
            if (_tetherLine != null) {
                _tetherLine.enabled = false;
                GameObject.Destroy(_tetherLine.gameObject);
            }

            // Recycle via pool — deactivate for now.
            gameObject.SetActive(false);
        }

        // ── Tether visual ─────────────────────────────────────────────────

        private void SetupTether() {
            // Create a child GameObject for the tether line so it doesn't rotate with the parent.
            var tetherGO = new GameObject("BarrageBalloonTether");
            tetherGO.transform.SetParent(transform, false);
            _tetherLine = tetherGO.AddComponent<LineRenderer>();

            // Minimal line: two points (top → ground).
            _tetherLine.positionCount = 2;
            _tetherLine.startWidth = 0.03f;
            _tetherLine.endWidth   = 0.03f;
            _tetherLine.startColor = new Color(0.6f, 0.55f, 0.4f, 0.7f); // muted brown
            _tetherLine.endColor   = new Color(0.6f, 0.55f, 0.4f, 0.1f); // fades at bottom
            _tetherLine.material   = null; // use default UI line shader for thin lines
        }

        private void UpdateTetherVisual() {
            if (_tetherLine == null || !_tetherLine.enabled) return;

            var pos = transform.position;
            // Point 0: balloon attachment (slightly below center).
            _tetherLine.SetPosition(0, new Vector3(pos.x, pos.y - 0.4f, 0f));
            // Point 1: ground level (y = -4.5 per ARCHITECTURE §2).
            _tetherLine.SetPosition(1, new Vector3(pos.x, -4.5f, 0f));
        }

        void Update() {
            // Refresh tether visual every frame so it follows the drifting balloon.
            UpdateTetherVisual();
        }

        // ── Static factory (for MissionRunner) ────────────────────────────

        /// <summary>
        /// Spawn a BarrageBalloon at the given world position.
        /// Creates: TeamTag(Enemy), Health, Rigidbody2D(Kinematic), Collider2D(trigger),
        /// SpriteRenderer + trigger collider with default size.
        /// </summary>
        public static BarrageBalloon Spawn(Vector3 position) {
            // Name = Balance score key; Health raises EntityDestroyed with it.
            var go = new GameObject("barrageBalloon");
            go.transform.position = position;

            // TeamTag — required by Health.Awake().
            var teamTag = go.AddComponent<TeamTag>();
            teamTag.team = Team.Enemy;

            // Trigger collider (standard size for a balloon hit-box).
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.0f, 1.0f);
            col.offset = Vector2.zero;

            // Health component gets its hit points (enemyHealth.barrageBalloon) in Awake.
            go.AddComponent<Health>();

            // BarrageBalloon behaviour script.
            var balloon = go.AddComponent<BarrageBalloon>();

            return balloon;
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
