using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Missions;
using Biplanes.Player;
using Biplanes.UI;

namespace Biplanes.Missions {

    /// <summary>
    /// Central game orchestrator for a single mission.
    ///
    /// Lifecycle:
    ///   1. StartMission(missionIndex) — loads JSON, creates all systems (ScrollController,
    ///      ObjectiveTracker, ScoreKeeper, PlayerPlane, HUD). Called BY GameManager.StartMission(),
    ///      which then transitions the state machine into Playing.
    ///   2. Update() — checks camera x against each SpawnEntry's atX threshold; when reached,
    ///      spawns that group of entities via their static Spawn() factory and marks it done.
    ///   3. OnEntityDestroyed(type, team) — ObjectiveTracker receives this event from
    ///      MissionRunner; after evaluation checks if all objectives are complete.
    ///   4. HandleObjectiveComplete(objectiveId) — called when a tracked objective finishes;
    ///      the last one triggers GameManager.SetState(Won or Lost).
    ///
    /// Architecture contract (ARCHITECTURE.md §5):
    ///   - Loads mission JSON from Resources/Missions/mission_0N.
    ///   - Sets scrolling via ScrollController.SetScrollSpeed(data.scrollSpeed).
    ///   - Instantiates all entity types from the spawns array using their existing Spawn()
    ///     static factories — no prefab assets.
    ///   - Wires ObjectiveTracker to GameEvents.EntityDestroyed for tracking kill counts.
    ///   - Is driven by GameManager.StartMission(); never calls it back.
    /// </summary>
    public class MissionRunner : MonoBehaviour {

        // ── Serialized references (wired in editor / ProjectBuilder) ────────

        [SerializeField, Tooltip("Camera transform for the scrolling viewport.")]
        private Transform _cameraTransform;

        // ── Singleton ─────────────────────────────────────────────────────

        private static MissionRunner _instance;

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        // ── Mission data ────────────────────────────────────────────────────

        private MissionData _missionData;
        private bool[] _spawnedFlags;
        private int _spawnCount;

        /// <summary>Current mission JSON data (read-only for other systems)."</summary>
        public MissionData MissionData => _missionData;

        // ── Systems ────────────────────────────────────────────────────────

        private ObjectiveTracker _objectiveTracker;
        private PlayerPlane _playerPlane;
        private HudView _hud;
        private TerrainStrip _terrain;
        private ParallaxBackground _background;

        /// <summary>
        /// Lowest airborne start, in world units above the ground. Mission JSONs give
        /// playerStart.y = -4 (meant as an absolute y), which as "height above terrain"
        /// would put the plane underground and crash it on the first tick.
        /// </summary>
        private const float MinAirborneStartHeight = 1.5f;

        /// <summary>Gap left between the runway and the plane's collider at mission start.</summary>
        private const float RunwayClearance = 0.02f;

        // ── Enemy plane entry (fighter, bomber) ─────────────────────────
        /// <summary>Ground objects spawn when their atX is this far past the right screen edge.</summary>
        private const float GroundEntryMarginX = 2f;
        /// <summary>Planes appear this far past the right screen edge.</summary>
        private const float PlaneEntryMarginX = 1.5f;
        /// <summary>Minimum x gap between planes of one group.</summary>
        private const float MinPlaneGapX = 2f;
        /// <summary>Extra random x per plane (0..this). Planes close at ~5 u/s, so 6 ≈ 1.2 s.</summary>
        private const float MaxPlaneStaggerX = 6f;
        /// <summary>Lowest spawn height above the terrain.</summary>
        private const float MinPlaneHeight = 1.5f;
        /// <summary>Keep planes at least this far below the top of the screen.</summary>
        private const float PlaneTopMargin = 1f;
        /// <summary>Sine fighters weave ±2 (FighterAI.sineAmplitude); keep the dip above ground.</summary>
        private const float SineDipAllowance = 2f;

        /// <summary>List of entity GameObjects spawned this mission (for cleanup)."</summary>
        private readonly List<GameObject> _spawnedEntities = new List<GameObject>();

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Start a mission given its index (1-5). Loads JSON, creates all subsystems,
        /// spawns the player, sets scrolling speed and initializes objectives.
        /// Called by GameManager.StartMission(), which handles the Playing state transition.
        /// </summary>
        public void StartMission(int missionIndex) {
            if (missionIndex < 1 || missionIndex > 5) {
                Debug.LogError($"MissionRunner: invalid mission index {missionIndex}. Must be 1-5.");
                return;
            }

            // Reset tracking lists.
            _spawnedEntities.Clear();

            // Load the mission JSON from Resources/Missions/mission_0N.json.
            string resourcePath = $"Missions/mission_{missionIndex:D2}";
            var textAsset = Resources.Load<TextAsset>(resourcePath);
            if (textAsset == null) {
                Debug.LogError($"MissionRunner: missing mission file at 'Resources/{resourcePath}.json'.");
                return;
            }

            _missionData = JsonUtility.FromJson<MissionData>(textAsset.text);
            if (_missionData == null || _missionData.spawns == null) {
                Debug.LogError($"MissionRunner: failed to parse mission JSON — data is incomplete. Check file at 'Resources/{resourcePath}.json'.");
                return;
            }
            Debug.Log($"MissionRunner: loaded mission {_missionData.id} — {_missionData.title}");
            _spawnCount = _missionData.spawns.Length;
            // --- Create ScrollController and set speed from JSON ---
            ScrollController sc = ScrollController.Create(_missionData.scrollSpeed);
            if (_cameraTransform != null) {
                // Camera is already at x = 0; we need to clamp it properly.
            }

            // --- Background and ground (ARCHITECTURE §3 Missions) ---
            // StartMission can run again without a scene reload (ResultView "Next"),
            // so replace the previous mission's world first.
            if (_background != null) Destroy(_background.gameObject);
            if (_terrain != null) Destroy(_terrain.gameObject);
            _background = ParallaxBackground.Create(_missionData.background, Camera.main);
            _terrain = TerrainStrip.Create(_missionData.terrain, _missionData.length);

            // --- Create the player plane at the mission-defined start position ---
            if (_missionData.playerStart == null) {
                Debug.LogError("MissionRunner: playerStart data is missing from JSON! Using runway start at x=0.");
                _playerPlane = PlayerPlane.Create(new Vector3(0f, GetTerrainHeightAt(0f), 0f), true);
                PlaceOnGround(_playerPlane);
            } else {
                var ps = _missionData.playerStart;
                float ground = GetTerrainHeightAt(ps.x);
                float y = ground + ps.y; // y is height above terrain (§5)
                if (!ps.onRunway && y < ground + MinAirborneStartHeight) {
                    Debug.LogWarning($"MissionRunner: playerStart.y={ps.y} is below the ground for an airborne start; " +
                                     $"using {MinAirborneStartHeight} above the terrain instead.");
                    y = ground + MinAirborneStartHeight;
                }
                _playerPlane = PlayerPlane.Create(new Vector3(ps.x, y, 0f), ps.onRunway);
                if (ps.onRunway) PlaceOnGround(_playerPlane);
            }

            // --- Wire AAAGun with player reference after all spawns ---
            _aaagunsToInit.Clear();

            // --- Spawn entities as the camera advances (handled in Update) ---
            int spawnCount = _missionData.spawns != null ? _missionData.spawns.Length : 0;
            _spawnedFlags = new bool[spawnCount];

            // --- Create ObjectiveTracker and wire to GameEvents ---
            _objectiveTracker = gameObject.AddComponent<ObjectiveTracker>();
            _objectiveTracker.Init(_missionData.objectives, 0f);

            // Subscribe ObjectiveTracker to the EntityDestroyed event so it receives kills.
            GameEvents.EntityDestroyed += OnEntityDestroyed;

            // --- Create HUD ---
            CreateHud();

            // NOTE: Do NOT call GameManager.StartMission() here. GameManager owns the
            // state machine and calls this method; calling back caused infinite recursion
            // (stack overflow crash on launch). GameManager sets Playing after we return.

            // Set initial objective on HUD.
            SetActiveObjectiveFromJson();

            Debug.Log($"MissionRunner: mission {missionIndex} started — {_spawnCount} spawn groups, {_missionData?.objectives?.Length ?? 0} objectives");
        }

        /// <summary>
        /// Spawn all entities from the given SpawnEntry using its type-specific static factory.
        /// Called internally by StartUpdate() when the camera reaches the entry's atX threshold.
        /// Each group spawns `count` entities spaced by `spacing` along the x-axis, starting
        /// at the entry's `atX` position with height `y` above the local terrain.
        /// </summary>
        public void SpawnEntities(SpawnEntry entry) {
            if (entry == null) return;

            float spawnBaseX = entry.atX;
            float spawnYBase = GetTerrainHeightAt(spawnBaseX) + entry.y; // terrain height + y offset above ground
            string typeLower = entry.type.ToLowerInvariant();

            // Enemy planes enter from off the right edge, staggered in x (so they arrive at
            // random times) and at random heights in the open sky.
            bool isPlane = typeLower == "fighter" || typeLower == "bomber";
            var cam = ScrollController.Instance != null && ScrollController.Instance.Camera != null
                ? ScrollController.Instance.Camera : Camera.main;
            float planeX = cam != null
                ? cam.transform.position.x + cam.orthographicSize * cam.aspect + PlaneEntryMarginX
                : spawnBaseX;

            for (int i = 0; i < entry.count; i++) {
                float posX = spawnBaseX + entry.spacing * i;
                Vector3 position = new Vector3(posX, spawnYBase, 0f);
                if (isPlane) {
                    if (i > 0) planeX += Mathf.Max(entry.spacing, MinPlaneGapX);
                    planeX += Random.Range(0f, MaxPlaneStaggerX);
                    position = new Vector3(planeX, RandomPlaneY(cam, planeX, entry.pattern), 0f);
                }

                GameObject entityGO = null;

                switch (typeLower) {
                    case "fighter": {
                        var fighter = FighterAI.Spawn(position);
                        // Before its Start runs, so sine/dive use the random start height.
                        fighter.Configure(entry.pattern, entry.escortOf);
                        entityGO = fighter.gameObject;
                        break;
                    }

                    case "bomber": {
                        var bomber = BomberAI.Spawn(position);
                        // targetX is set via [SerializeField] private float — MissionRunner
                        // cannot write it directly at runtime without a setter method.
                        entityGO = bomber.gameObject;
                        break;
                    }

                    case "aaa": {
                        // AAA guns are ground-bound (y = -4.5 per ARCHITECTURE §3).
                        var gun = AAAGun.Spawn(new Vector3(posX, GetTerrainHeightAt(posX), 0f));
                        _aaagunsToInit.Add(gun);
                        entityGO = gun.gameObject;
                        break;
                    }

                    case "healthballoon": {
                        var balloon = HealthBalloon.Spawn(position);
                        entityGO = balloon.gameObject;
                        break;
                    }

                    case "barrageballoon": {
                        var balloon = BarrageBalloon.Spawn(position);
                        entityGO = balloon.gameObject;
                        break;
                    }

                    case "building":
                    case "tree": {
                        var obstacle = StaticObstacle.Spawn(position, entry.type);
                        entityGO = obstacle.gameObject;
                        break;
                    }

                    default:
                        Debug.LogWarning($"MissionRunner: unknown spawn type '{entry.type}' — skipping.");
                        break;
                }

                if (entityGO != null) {
                    _spawnedEntities.Add(entityGO);
                }
            }

            Debug.Log($"MissionRunner: spawned {entry.count}x {typeLower} at x≈{spawnBaseX}");
        }

        /// <summary>
        /// Called when an entity is destroyed. Delegates to ObjectiveTracker for objective
        /// evaluation and checks if the last objective just completed — triggering mission end.
        /// </summary>
        public void HandleObjectiveComplete(string objectiveId) {
            Debug.Log($"MissionRunner: objective '{objectiveId}' completed");

            // Check if ALL objectives are now complete.
            if (_objectiveTracker != null && _objectiveTracker.AllComplete) {
                Debug.Log("MissionRunner: all objectives complete — mission won!");
                GameManager.Instance?.SetState(GameManager.GameState.Won);

                // Persist score and unlock next mission via ScoreKeeper.
                var sk = ScoreKeeper.Instance;
                if (sk != null && _missionData != null) {
                    sk.OnMissionEnded(_missionData.id, true);
                }
            } else {
                GameEvents.RaiseObjectiveCompleted(objectiveId);
            }
        }

        // ── Internal event handler ─────────────────────────────────────────

        /// <summary>
        /// Called when any entity is destroyed (subscribed from StartMission).
        /// Delegates to ObjectiveTracker, then checks for mission-end conditions.
        /// </summary>
        private void OnEntityDestroyed(string entityType, Team team) {
            if (_objectiveTracker != null) {
                _objectiveTracker.OnEntityDestroyed(entityType, team);

                // If the objective tracker marks completion, forward to MissionRunner.
                if (_objectiveTracker.AllComplete) {
                    HandleObjectiveComplete("all_objectives");
                }
            }
        }

        // ── Core loop (Update in MonoBehaviour) ────────────────────────────

        private void Update() {
            if (_missionData == null || _spawnedFlags == null) return;

            float cameraX = ScrollController.Instance != null
                ? ScrollController.Instance.MissionDistanceX
                : -999f;

            // Ground objects (AAA, balloons, buildings, trees) sit at world x = atX. Spawn them
            // once that spot is just past the right screen edge, so they scroll into view
            // instead of popping in mid-screen. Planes still trigger at camera x >= atX and
            // place themselves off-screen (SpawnEntities).
            var cam = ScrollController.Instance != null && ScrollController.Instance.Camera != null
                ? ScrollController.Instance.Camera : Camera.main;
            float groundLookAhead = cam != null
                ? cam.orthographicSize * cam.aspect + GroundEntryMarginX
                : 0f;

            // Check each spawn entry: has the camera reached its threshold?
            for (int i = 0; i < _spawnedFlags.Length && i < _missionData.spawns.Length; i++) {
                var spawn = _missionData.spawns[i];
                string spawnType = spawn.type != null ? spawn.type.ToLowerInvariant() : "";
                bool isPlane = spawnType == "fighter" || spawnType == "bomber";
                float trigger = isPlane ? spawn.atX : spawn.atX - groundLookAhead;
                if (!_spawnedFlags[i] && cameraX >= trigger) {
                    SpawnEntities(_missionData.spawns[i]);
                    _spawnedFlags[i] = true;

                    // Wire AAAGun player reference after all spawns for this entry.
                    foreach (var gun in _aaagunsToInit) {
                        if (_playerPlane != null) {
                            gun.Init(_playerPlane);
                        }
                    }
                    _aaagunsToInit.Clear();
                }
            }

            // Check survive-to-end objective via camera progress.
            if (_objectiveTracker != null && _missionData.length > 0f) {
                _objectiveTracker.OnCameraProgress(cameraX, _missionData.length);
                if (_objectiveTracker.AllComplete) {
                    HandleObjectiveComplete("survive_to_end");
                }
            }
        }

        // ── HUD creation (from code — no prefabs per ARCHITECTURE §0) ───────

        /// <summary>
        /// Build the in-game HUD from scratch using uGUI primitives.
        /// The canvas is created as a child of this MissionRunner's GameObject.
        /// </summary>
        private void CreateHud() {
            // Canvas (EventSystem should already exist; we just add the canvas).
            var canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(transform, false);

            // Use an existing EventSystem if available — otherwise the scene must have one.
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Scale modifier for uGUI scaling.
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // HUD view component (builds all widgets).
            _hud = canvasGO.AddComponent<HudView>();
        }

        /// <summary>
        /// Set the active objective text on the HUD from the loaded mission data.
        /// Looks for a "destroyAllOfType" objective as the primary one to display.
        /// </summary>
        private void SetActiveObjectiveFromJson() {
            if (_hud == null || _missionData?.objectives == null) return;

            // Find the first destroy-all objective to display on HUD.
            foreach (var obj in _missionData.objectives) {
                if (obj.type == "destroyAllOfType") {
                    _hud.SetObjective(obj.targetType, CountSpawnedOfType(obj.targetType));
                    return;
                }
            }

            // If no destroy-all objective, show the first one's description.
            if (_missionData.objectives.Length > 0) {
                var firstObj = _missionData.objectives[0];
                _hud.SetObjective(firstObj.targetType ?? "target", 1);
            }
        }

        /// <summary>Count how many SpawnEntries of the given target type exist in this mission.</summary>
        private int CountSpawnedOfType(string targetType) {
            if (_missionData?.spawns == null) return 0;
            int count = 0;
            string lower = targetType.ToLowerInvariant();
            foreach (var spawn in _missionData.spawns) {
                if (spawn.type.ToLowerInvariant() == lower) {
                    count += spawn.count;
                }
            }
            return count;
        }

        // ── Terrain helper ────────────────────────────────────────────────

        /// <summary>World y of the ground at x. TerrainStrip owns the maths (ARCHITECTURE §3).</summary>
        private float GetTerrainHeightAt(float x) {
            return _terrain != null ? _terrain.HeightAt(x) : TerrainStrip.GroundBaseY;
        }

        /// <summary>
        /// Random spawn height for an enemy plane: anywhere between MinPlaneHeight above the
        /// terrain and PlaneTopMargin below the top of the screen. Sine fighters start higher
        /// so their weave never dips into the ground.
        /// </summary>
        private float RandomPlaneY(Camera cam, float x, string pattern) {
            float low = GetTerrainHeightAt(x) + MinPlaneHeight;
            if (pattern == "sine") low += SineDipAllowance;
            float high = cam != null
                ? cam.transform.position.y + cam.orthographicSize - PlaneTopMargin
                : low + 6f;
            if (high < low) high = low;
            return Random.Range(low, high);
        }

        /// <summary>Sets a runway-start plane so its collider rests just on the ground.</summary>
        private void PlaceOnGround(PlayerPlane plane) {
            if (plane == null) return;
            var col = plane.GetComponent<Collider2D>();
            var pos = plane.transform.position;
            float ground = GetTerrainHeightAt(pos.x);
            float halfHeight = col != null ? col.bounds.extents.y : 0f;
            plane.transform.position = new Vector3(pos.x, ground + halfHeight + RunwayClearance, pos.z);
        }

        // ── Cleanup ───────────────────────────────────────────────────────

        private void OnDestroy() {
            if (_objectiveTracker != null) {
                GameEvents.EntityDestroyed -= OnEntityDestroyed;
            }
            _spawnedEntities.Clear();
        }

        // ── AAAGun init queue (populated during SpawnEntities, wired after all spawns) ─

        private readonly List<AAAGun> _aaagunsToInit = new List<AAAGun>();
    }
}
