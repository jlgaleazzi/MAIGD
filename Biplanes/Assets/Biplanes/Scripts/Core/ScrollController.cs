using UnityEngine;

namespace Biplanes.Core {

    /// <summary>
    /// Drives the camera's rightward scroll during a mission and exposes the
    /// current "mission distance" (camera x) for spawner thresholds.
    ///
    /// World scrolls — the player stays roughly centred horizontally while the
    /// camera advances at scrollSpeed (ARCHITECTURE §3).
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

        // ── Camera reference ───────────────────────────────────────────

        [SerializeField]
        private Camera mainCamera;

        /// <summary>Read-only camera reference (set in Awake).</summary>
        public Camera Camera => mainCamera;

        // ── Mission distance ───────────────────────────────────────────

        /// <summary>
        /// The camera's current x position — the "mission distance" the player
        /// has covered. Used by MissionRunner to decide when to spawn entities.
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
        /// Sets the scroll speed for the current mission. Must be called
        /// before the first FixedUpdate where scrolling matters.
        /// </summary>
        public void SetScrollSpeed(float speed) {
            scrollSpeed = Mathf.Max(0f, speed);
        }

        /// <summary>
        /// Factory helper: creates a ScrollController GameObject with components
        /// attached at runtime (no prefab needed — ARCHITECTURE §2 rule 4).
        /// </summary>
        public static ScrollController Create(float initialScrollSpeed = 2f, Transform parent = null) {
            var go = new GameObject("ScrollController");
            if (parent != null) {
                go.transform.SetParent(parent);
            }
            var sc = go.AddComponent<ScrollController>();
            sc.SetScrollSpeed(initialScrollSpeed);

            // Wire up the camera if we have one.
            sc.mainCamera = Camera.main;
            if (sc.mainCamera == null) {
                Debug.LogWarning("ScrollController.Create: no active Camera found. "
                    + "Ensure a MainCamera exists before this is called, or pass 'mainCamera' after creation.");
            }

            return sc;
        }

        // ── Core loop (physics time) ───────────────────────────────────

        /// <summary>
        /// Moves the camera right each physics tick. Using FixedUpdate so scroll
        /// speed is consistent regardless of frame rate — critical for spawn
        /// threshold reliability in MissionRunner.
        /// </summary>
        private void FixedUpdate() {
            if (mainCamera == null) return;

            float delta = scrollSpeed * Time.fixedDeltaTime;
            Vector3 pos = mainCamera.transform.position;
            pos.x += delta;
            mainCamera.transform.position = pos;
        }
    }
}
