using UnityEngine;

namespace Biplanes.Core {

    /// <summary>
    /// Drives the camera's horizontal scroll during a mission and exposes the
    /// current "mission distance" (camera x) plus the farthest x reached so far.
    ///
    /// The world scrolls in the player's facing Direction (+1 right, -1 left) at
    /// scrollSpeed. A turn does not snap: the camera's x velocity eases toward
    /// Direction * scrollSpeed over Balance player.turnEaseSeconds, so it slows
    /// down, stops and speeds up the other way (ARCHITECTURE §3).
    ///
    /// The camera x is clamped to [minX, maxX] (SetLimits); while it is pinned at
    /// a limit its velocity is 0.
    ///
    /// Mission start gate (B049): MissionRunner calls Hold() when a mission starts. While
    /// held the velocity is exactly 0 and the camera (so FarthestX, the parallax and the
    /// minimap) does not move. PlayerPlane calls Release() on the player's first input; the
    /// velocity then eases up from 0 like a turn.
    ///
    /// Created by MissionRunner on mission start via Create().
    /// </summary>
    public class ScrollController : MonoBehaviour {

        // ── Singleton ───────────────────────────────────────────────────

        public static ScrollController Instance { get; set; }

        // ── Tunable (set per-mission from JSON) ────────────────────────

        [SerializeField, Tooltip("Camera scroll speed in world units per second.")]
        private float scrollSpeed;

        /// <summary>Current scroll speed — read-only accessor for inspectors/tests.</summary>
        public float ScrollSpeed => scrollSpeed;

        /// <summary>Used when Balance.json has no (or a zero) player.turnEaseSeconds.</summary>
        private const float FallbackTurnEaseSeconds = 0.6f;

        // ── Camera reference ───────────────────────────────────────────

        [SerializeField]
        private Camera mainCamera;

        /// <summary>Read-only camera reference (set in Awake).</summary>
        public Camera Camera => mainCamera;

        // ── Scroll state ───────────────────────────────────────────────

        private int _direction = 1;
        private float _velocity;
        private float _turnEaseSeconds = FallbackTurnEaseSeconds;
        private float _minX = float.NegativeInfinity;
        private float _maxX = float.PositiveInfinity;
        private float _farthestX;
        private bool _held;

        /// <summary>Scroll direction: +1 = right, -1 = left. Set by PlayerPlane from its Facing.</summary>
        public int Direction => _direction;

        /// <summary>Current camera x velocity in world units per second (eases on a turn).</summary>
        public float Velocity => _velocity;

        /// <summary>The largest camera x reached so far this mission (spawn triggers use it).</summary>
        public float FarthestX => _farthestX;

        /// <summary>True while the mission waits for the player's first input (B049): the
        /// camera stands still with velocity 0.</summary>
        public bool IsHeld => _held;

        // ── Mission distance ───────────────────────────────────────────

        /// <summary>
        /// The camera's current x position — the "mission distance". Used for
        /// surviveToEnd, the HUD and bomber targetX checks.
        /// </summary>
        public float MissionDistanceX => mainCamera != null ? mainCamera.transform.position.x : 0f;

        // ── Lifetime ───────────────────────────────────────────────────

        private void Awake() {
            if (Instance != null && Instance != this) {
                Debug.LogWarning($"ScrollController: duplicate instance at x={transform.position.x}; destroying extra.");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Cache camera reference if not wired in the editor.
            if (mainCamera == null) {
                mainCamera = Camera.main;
            }
        }

        private void OnDestroy() {
            if (Instance == this) {
                Instance = null;
            }
        }

        // ── Public API ─────────────────────────────────────────────────

        /// <summary>
        /// Sets the scroll speed for the current mission. The camera velocity eases
        /// toward the new Direction * speed.
        /// </summary>
        public void SetScrollSpeed(float speed) {
            scrollSpeed = Mathf.Max(0f, speed);
        }

        /// <summary>Sets the scroll direction (-1 = left, +1 = right). Other values are ignored.</summary>
        public void SetDirection(int dir) {
            if (dir != 1 && dir != -1) {
                Debug.LogWarning($"ScrollController.SetDirection: expected -1 or +1, got {dir}; ignored.");
                return;
            }
            _direction = dir;
        }

        /// <summary>Holds the scroll (B049): velocity 0 at once, the camera stays put until
        /// Release.</summary>
        public void Hold() {
            _held = true;
            _velocity = 0f;
        }

        /// <summary>Ends the hold (the player's first input). The velocity eases up from 0
        /// toward Direction * scrollSpeed. No effect when not held.</summary>
        public void Release() {
            _held = false;
        }

        /// <summary>
        /// Clamps the camera x to [minX, maxX]. While the camera is pinned at a limit
        /// its velocity is 0.
        /// </summary>
        public void SetLimits(float minX, float maxX) {
            if (maxX < minX) maxX = minX;
            _minX = minX;
            _maxX = maxX;
        }

        /// <summary>
        /// Factory helper: creates the ScrollController GameObject (no prefab needed —
        /// ARCHITECTURE §2 rule 4). If one already exists (a mission started without a
        /// scene reload) it is reset and reused, so callers always get the live Instance.
        /// The scroll starts rightward at full speed and not held (MissionRunner holds it
        /// until the player's first input, B049).
        /// </summary>
        public static ScrollController Create(float initialScrollSpeed = 2f, Transform parent = null) {
            ScrollController sc = Instance;
            if (sc == null) {
                var go = new GameObject("ScrollController");
                if (parent != null) {
                    go.transform.SetParent(parent);
                }
                sc = go.AddComponent<ScrollController>();
                // Awake sets this in Play mode; it doesn't run outside Play mode (EditMode
                // tests), where a missing Instance made every StartMission add another one (B047).
                Instance = sc;
            }

            // Wire up the camera if we have one.
            sc.mainCamera = Camera.main;
            if (sc.mainCamera == null) {
                Debug.LogWarning("ScrollController.Create: no active Camera found. "
                    + "Ensure a MainCamera exists before this is called, or pass 'mainCamera' after creation.");
            }

            var g = GameBalance.Get();
            float ease = g != null && g.player != null ? g.player.turnEaseSeconds : 0f;
            sc._turnEaseSeconds = ease > 0f ? ease : FallbackTurnEaseSeconds;

            sc.SetScrollSpeed(initialScrollSpeed);
            sc._direction = 1;
            sc._held = false;
            sc._velocity = sc.scrollSpeed;
            sc._minX = float.NegativeInfinity;
            sc._maxX = float.PositiveInfinity;
            sc._farthestX = sc.MissionDistanceX;

            return sc;
        }

        // ── Core loop (physics time) ───────────────────────────────────

        /// <summary>
        /// Moves the camera each physics tick. Using FixedUpdate so scroll speed is
        /// consistent regardless of frame rate — critical for spawn threshold
        /// reliability in MissionRunner.
        /// </summary>
        private void FixedUpdate() {
            if (mainCamera == null) return;

            // Waiting for the player's first input (B049): nothing moves.
            if (_held) {
                _velocity = 0f;
                return;
            }

            float dt = Time.fixedDeltaTime;

            // Ease linearly toward the target: a full reversal (+speed → -speed) takes
            // turnEaseSeconds, decelerating through 0 before accelerating the other way.
            float target = _direction * scrollSpeed;
            float accel = 2f * Mathf.Max(scrollSpeed, Mathf.Abs(_velocity)) / _turnEaseSeconds;
            _velocity = Mathf.MoveTowards(_velocity, target, accel * dt);

            Vector3 pos = mainCamera.transform.position;
            pos.x += _velocity * dt;

            // Mission limits: pin the camera and stop it at either end.
            if (pos.x <= _minX) {
                pos.x = _minX;
                if (_velocity < 0f) _velocity = 0f;
            } else if (pos.x >= _maxX) {
                pos.x = _maxX;
                if (_velocity > 0f) _velocity = 0f;
            }

            mainCamera.transform.position = pos;
            if (pos.x > _farthestX) _farthestX = pos.x;
        }
    }
}
