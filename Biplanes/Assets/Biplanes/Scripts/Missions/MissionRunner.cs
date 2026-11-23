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
    ///   2. Update() — checks ScrollController.FarthestX against each SpawnEntry's atX threshold; when reached,
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

        /// <summary>
        /// Set once the mission is won or lost, so the end (state change, best-score save,
        /// unlock) is handled exactly once. Cleared by StartMission (B006c).
        /// </summary>
        private bool _missionEnded;

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
        /// <summary>A zeppelin (placed at atX, 9 units long) spawns once its nose is this far
        /// past the right screen edge: look-ahead = half screen + ZeppelinAI.HalfLength + this (B021).</summary>
        private const float ZeppelinEntryMarginX = 0.5f;
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
        /// <summary>Extra lowest spawn height for "sine" fighters. Since B059 the sine pattern is
        /// only a ±0.5 wobble on the chase height (FighterAI.sineAmplitude), and the chase keeps
        /// every fighter at least FighterAI.ChaseGroundClearance above the terrain, so 2 is more
        /// than the dip needs: sine fighters just spawn a little higher (harmless).</summary>
        private const float SineDipAllowance = 2f;

        /// <summary>List of entity GameObjects spawned this mission (for cleanup)."</summary>
        private readonly List<GameObject> _spawnedEntities = new List<GameObject>();

        /// <summary>Enemy aircraft spawned this mission (FighterAI, BomberAI, ZeppelinAI, and
        /// EnemyPressure reinforcements). Destroyed ones turn Unity-null and are pruned on the
        /// next spawn. EnemyPressure reads it instead of a per-frame scene search (B035).</summary>
        private readonly List<MonoBehaviour> _enemyAircraft = new List<MonoBehaviour>();

        /// <summary>Enemy-pressure safety net (B035), one per mission.</summary>
        private EnemyPressure _pressure;

        /// <summary>Mission-5 HQ cheat listener (B078), one per mission.</summary>
        private CheatCodes _cheats;

        /// <summary>The HQ cheat was used this run (B078): its score is never saved as a best
        /// score (SaveSystem.BestScoreLocked). Cleared by StartMission.</summary>
        public bool CheatUsed { get; private set; }

        /// <summary>Movement pattern of EnemyPressure reinforcement fighters.</summary>
        private const string ReinforcementPattern = "straight";

        /// <summary>Enemy aircraft spawned this mission (may hold destroyed entries: null-check).</summary>
        public IReadOnlyList<MonoBehaviour> EnemyAircraft => _enemyAircraft;

        /// <summary>Ground enemies spawned this mission (AAAGun, BarrageBalloon, EnemyBase).
        /// Destroyed ones turn Unity-null and are pruned on the next spawn. Read by the HUD
        /// minimap (B044) instead of a scene search.</summary>
        private readonly List<MonoBehaviour> _groundEnemies = new List<MonoBehaviour>();

        /// <summary>Ground enemies spawned this mission (may hold destroyed entries: null-check).</summary>
        public IReadOnlyList<MonoBehaviour> GroundEnemies => _groundEnemies;

        // ── Recharge bomb balloons (B064) ───────────────────────────────

        /// <summary>Bomb balloons spawned this mission (JSON-placed and auto). Pickups only: never
        /// in _enemyAircraft / _groundEnemies, so no objective, counter or EnemyPressure sees them.</summary>
        private readonly List<BombBalloon> _bombBalloons = new List<BombBalloon>();

        /// <summary>Bomb balloons spawned this mission (may hold destroyed entries: null-check).</summary>
        public IReadOnlyList<BombBalloon> BombBalloons => _bombBalloons;

        private BombBalloonData _bombBalloonData = BombBalloonData.Resolve(null);
        private BombBalloon.RechargeTimer _bombTimer = new BombBalloon.RechargeTimer(BombBalloonData.DefaultDelay);

        /// <summary>Lowest auto bomb balloon height above the terrain (same as enemy planes).</summary>
        private const float BombBalloonMinHeight = MinPlaneHeight;

        /// <summary>Mission length in world units (0 outside a mission).</summary>
        public float MissionLength => _missionData != null ? _missionData.length : 0f;

        /// <summary>Number of spawn entries this mission (MissionData.spawns), 0 outside a mission.</summary>
        public int SpawnEntryCount => _spawnedFlags != null ? _spawnedFlags.Length : 0;

        /// <summary>True while spawn entry `index` (MissionData.spawns) hasn't triggered yet.
        /// False for an out-of-range index or outside a mission (B044 minimap).</summary>
        public bool IsSpawnPending(int index) {
            return _spawnedFlags != null && index >= 0 && index < _spawnedFlags.Length && !_spawnedFlags[index];
        }

        /// <summary>The mission's player plane (null outside a mission).</summary>
        public PlayerPlane Player => _playerPlane;

        /// <summary>A mission is running and has an objective still open (not won, lost or
        /// otherwise ended).</summary>
        public bool ObjectivesOpen => _missionData != null && !_missionEnded
            && _objectiveTracker != null && !_objectiveTracker.AllComplete;

        /// <summary>The mission is loaded and the player's first input has started it (B049).
        /// False while waiting: the scroll is held, the plane holds still and EnemyPressure's
        /// timer stays reset. Stays true after the mission ends.</summary>
        public bool HasStarted => _missionData != null
            && (ScrollController.Instance == null || !ScrollController.Instance.IsHeld);

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

            // StartMission can run again without a scene reload (ResultView "Next"): clear the
            // previous mission's player, entities, tracker and event subscription, and put the
            // camera back at the mission start before ScrollController reads its x.
            TearDownPreviousMission();
            ResetCameraToMissionStart();
            _missionEnded = false;

            // Recharge bomb balloon (B064): fresh timer; the new BombBay's Start raises its full
            // count, which keeps the timer idle until the bay runs empty.
            _bombBalloonData = BombBalloonData.Resolve(GameBalance.Get());
            _bombTimer = new BombBalloon.RechargeTimer(_bombBalloonData.delay);
            _lastBombs = -1;
            GameEvents.BombsChanged -= OnBombsChanged;
            GameEvents.BombsChanged += OnBombsChanged;

            // B078: every run starts clean; the mission-5 HQ cheat flags it again if used.
            CheatUsed = false;
            SaveSystem.BestScoreLocked = false;

            // Each mission scores on its own: without a scene reload ("Next") the previous
            // mission's points would otherwise be saved as this mission's best (B006b).
            var scoreKeeper = ScoreKeeper.Instance;
            if (scoreKeeper != null) {
                scoreKeeper.ResetScore();
                // The result's "Best:" is the best before this run (B070): a kill win saves the
                // new best up to killWinDelaySeconds before the result screen reads it.
                scoreKeeper.CapturePreviousBest(_missionData.id);
            }

            // --- Create ScrollController and set speed from JSON ---
            ScrollController sc = ScrollController.Create(_missionData.scrollSpeed);
            // The player can turn back (Defender-style): the camera never scrolls left of the
            // mission start or right of the mission length. SetLimits collapses max < min, so
            // a length <= start pins the camera at the start instead of leaving it unbounded.
            float startCameraX = sc.MissionDistanceX;
            sc.SetLimits(startCameraX, _missionData.length);
            // Mission start gate (B049): the world holds still until the player's first input
            // (PlayerPlane releases the scroll). Retry and Next run StartMission again, so they
            // wait again too.
            sc.Hold();

            // --- Background and ground (ARCHITECTURE §3 Missions) ---
            // StartMission can run again without a scene reload (ResultView "Next"),
            // so replace the previous mission's world first.
            // DestroyNow: DestroyImmediate outside Play mode (EditMode tests), Destroy in Play.
            if (_background != null) DestroyNow(_background.gameObject);
            if (_terrain != null) DestroyNow(_terrain.gameObject);
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
            // TearDownPreviousMission left at most one tracker on this object; reuse it.
            _objectiveTracker = GetComponent<ObjectiveTracker>();
            if (_objectiveTracker == null) _objectiveTracker = gameObject.AddComponent<ObjectiveTracker>();
            _objectiveTracker.Init(_missionData.objectives, 0f, _missionData.spawns);

            // Subscribe ObjectiveTracker to the EntityDestroyed event so it receives kills.
            // Unsubscribe first so a second StartMission never counts each kill twice.
            GameEvents.EntityDestroyed -= OnEntityDestroyed;
            GameEvents.EntityDestroyed += OnEntityDestroyed;
            // bombTarget: EnemyBase reports its id and whether a bomb killed it.
            EnemyBase.Destroyed -= OnEnemyBaseDestroyed;
            EnemyBase.Destroyed += OnEnemyBaseDestroyed;
            // B080: a zeppelin that loses its last turret calls in its defense wave.
            ZeppelinAI.ShieldDropped -= OnZeppelinShieldDropped;
            ZeppelinAI.ShieldDropped += OnZeppelinShieldDropped;
            // A loss (player crash) also ends the mission: no win may follow it.
            GameEvents.MissionEnded -= OnMissionEnded;
            GameEvents.MissionEnded += OnMissionEnded;

            // --- Create HUD ---
            CreateHud();

            // --- Enemy pressure (B035): reinforcements when the sky stays empty ---
            _pressure = GetComponent<EnemyPressure>();
            if (_pressure == null) _pressure = gameObject.AddComponent<EnemyPressure>();
            _pressure.Init(this, PressureData.Resolve(GameBalance.Get()));

            // --- Mission-5 HQ cheat listener (B078); idle in every other mission ---
            _cheats = GetComponent<CheatCodes>();
            if (_cheats == null) _cheats = gameObject.AddComponent<CheatCodes>();
            _cheats.Init(this);

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

            // Enemy planes enter from off the RIGHT edge (B074: always, whatever the player's
            // facing or scroll direction), staggered further right in x (so they arrive at
            // random times) and at random heights in the open sky.
            bool isPlane = typeLower == "fighter" || typeLower == "bomber";
            var cam = ScrollController.Instance != null && ScrollController.Instance.Camera != null
                ? ScrollController.Instance.Camera : Camera.main;
            float planeX = cam != null
                ? EnemyPressure.EntryX(cam.transform.position.x, cam.orthographicSize * cam.aspect, PlaneEntryMargin(typeLower))
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
                        var fighter = SpawnFighter(position, entry.pattern, entry.escortOf);
                        // Catches a left-scrolling camera while it enters (B074).
                        fighter.BeginEntry(CameraVelocityX);
                        entityGO = fighter.gameObject;
                        break;
                    }

                    case "bomber": {
                        // The id lets escorts find it (FighterAI.FindEscortTarget, B020).
                        var bomber = BomberAI.Spawn(position, entry.id);
                        RegisterAircraft(bomber);
                        // targetX = the world x it must not reach (objective failAtX, B018).
                        bool watch = ResolveTargetX(entry, position, out float bomberTargetX);
                        bomber.SetTargetX(bomberTargetX);
                        // The bomber's Health (named "bomber") raises EntityDestroyed.
                        TrackIdTarget(entry.id, "bomber", bomber.GetComponent<Health>(), watch);
                        entityGO = bomber.gameObject;
                        break;
                    }

                    case "aaa": {
                        // AAA guns are ground-bound (y = -4.5 per ARCHITECTURE §3).
                        var gun = AAAGun.Spawn(new Vector3(posX, GetTerrainHeightAt(posX), 0f));
                        _aaagunsToInit.Add(gun);
                        RegisterGroundEnemy(gun);
                        entityGO = gun.gameObject;
                        break;
                    }

                    case "healthballoon": {
                        var balloon = HealthBalloon.Spawn(position);
                        entityGO = balloon.gameObject;
                        break;
                    }

                    case "bombballoon": {
                        // Pickup (B064): tracked only for cleanup and the auto-spawn check.
                        var balloon = BombBalloon.Spawn(position);
                        _bombBalloons.Add(balloon);
                        entityGO = balloon.gameObject;
                        break;
                    }

                    case "barrageballoon": {
                        var balloon = BarrageBalloon.Spawn(position);
                        RegisterGroundEnemy(balloon);
                        entityGO = balloon.gameObject;
                        break;
                    }

                    case "building":
                    case "tree": {
                        var obstacle = StaticObstacle.Spawn(position, entry.type);
                        entityGO = obstacle.gameObject;
                        break;
                    }

                    case "enemybase": {
                        // Ground-bound: the factory rests its bottom edge on `position`.
                        var enemyBase = EnemyBase.Spawn(position, entry.id);
                        TrackIdTarget(entry.id, EnemyBase.TypeKey, enemyBase);
                        RegisterGroundEnemy(enemyBase);
                        entityGO = enemyBase.gameObject;
                        break;
                    }

                    case "playerhq": {
                        entityGO = PlayerHQ.Spawn(position, entry.id).gameObject;
                        break;
                    }

                    case "zeppelin": {
                        // Not a random-placement plane: keeps atX and its JSON y (the hull
                        // clamps itself below the screen top in Start).
                        var zeppelin = ZeppelinAI.Spawn(position, entry.id);
                        RegisterAircraft(zeppelin);
                        if (_playerPlane != null) zeppelin.Init(_playerPlane);
                        // targetX = the world x it must not reach (objective failAtX, B018);
                        // while it has one it never despawns behind the camera.
                        bool watch = ResolveTargetX(entry, position, out float zeppelinTargetX);
                        zeppelin.SetTargetX(zeppelinTargetX);
                        TrackIdTarget(entry.id, ZeppelinAI.ScoreKey, zeppelin, watch);
                        entityGO = zeppelin.gameObject;
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
        /// The one fighter factory path (JSON fighters and reinforcements): FighterAI.Spawn,
        /// then Configure before its Start runs (so sine/dive use the start height), then
        /// tracked for cleanup and for EnemyPressure.
        /// </summary>
        private FighterAI SpawnFighter(Vector3 position, string pattern, string escortOf) {
            var fighter = FighterAI.Spawn(position);
            fighter.Configure(pattern, escortOf);
            RegisterAircraft(fighter);
            return fighter;
        }

        /// <summary>Adds an enemy aircraft to the EnemyPressure list, dropping destroyed ones.</summary>
        private void RegisterAircraft(MonoBehaviour aircraft) {
            _enemyAircraft.RemoveAll(a => a == null);
            if (aircraft != null) _enemyAircraft.Add(aircraft);
        }

        /// <summary>Adds a ground enemy to the minimap list, dropping destroyed ones.</summary>
        private void RegisterGroundEnemy(MonoBehaviour enemy) {
            _groundEnemies.RemoveAll(e => e == null);
            if (enemy != null) _groundEnemies.Add(enemy);
        }

        /// <summary>
        /// EnemyPressure reinforcement (B035, B040): plane `index` of a `count`-plane wave. It
        /// enters just past the RIGHT screen edge whatever the player's facing (B074:
        /// EnemyPressure.EntryX / EntryOffset: plane 0 within half a unit of being on screen,
        /// later planes EntryStagger further out), flies left (EntryFlightDirection) with the
        /// entry boost (FighterAI.BeginEntry) and the straight pattern, toward the player, at a
        /// height in slice `index` of the flight band (terrain + MinPlaneHeight .. screen top -
        /// PlaneTopMargin). It is named ScoreKeeper.ReinforcementFighterKey: it scores like a
        /// fighter and its kill counts toward destroyAllOfType "fighter" (B050; the required
        /// total stays the JSON fighter count). Returns null when no
        /// camera exists.
        /// </summary>
        public FighterAI SpawnReinforcementFighter(int index, int count) {
            return SpawnEnteringReinforcement(index, index, count, ReinforcementPattern, "");
        }

        /// <summary>
        /// One fighter of a zeppelin's defense wave (B080): plane `index` of a `count`-plane
        /// wave, built exactly like a reinforcement (named ScoreKeeper.ReinforcementFighterKey:
        /// fighter score, counts toward destroyAllOfType "fighter" without raising the required
        /// total; enters just past the RIGHT edge flying left with the entry boost, B074) but
        /// with the "escort" pattern and escortOf = the zeppelin's spawn id, so it takes a
        /// FighterAI formation slot on that hull (or chases the player inside the escort leash).
        /// Each one enters at the first-plane entry offset (the wave is staggered in time, not
        /// in x) at a height in slice `index` of the flight band. Returns null when no camera
        /// exists or the zeppelin is gone or dead.
        /// </summary>
        public FighterAI SpawnZeppelinDefender(ZeppelinAI zeppelin, int index, int count) {
            if (zeppelin == null || zeppelin.IsDead) return null;
            return SpawnEnteringReinforcement(0, index, count, ZeppelinDefensePattern, zeppelin.Id);
        }

        /// <summary>Shared reinforcement build (B035/B074/B080): x = entry offset `entryIndex`
        /// past the right edge, y = slice `sliceIndex` of `sliceCount` in the flight band.</summary>
        private FighterAI SpawnEnteringReinforcement(int entryIndex, int sliceIndex, int sliceCount,
                                                     string pattern, string escortOf) {
            var cam = ScrollController.Instance != null && ScrollController.Instance.Camera != null
                ? ScrollController.Instance.Camera : Camera.main;
            if (cam == null) return null;

            float x = EnemyPressure.EntryX(cam.transform.position.x, cam.orthographicSize * cam.aspect,
                EnemyPressure.EntryOffset(entryIndex, Random.value));
            float low = GetTerrainHeightAt(x) + MinPlaneHeight;
            float high = cam.transform.position.y + cam.orthographicSize - PlaneTopMargin;
            float y = EnemyPressure.SliceHeight(low, high, sliceIndex, sliceCount, Random.value);

            var fighter = SpawnFighter(new Vector3(x, y, 0f), pattern, escortOf);
            fighter.gameObject.name = ScoreKeeper.ReinforcementFighterKey;
            fighter.SetFlightDirection(EnemyPressure.EntryFlightDirection);
            fighter.BeginEntry(CameraVelocityX);
            _spawnedEntities.Add(fighter.gameObject);
            return fighter;
        }

        // ── Zeppelin defense wave (B080) ─────────────────────────────────

        /// <summary>Movement pattern of zeppelin defense fighters (FighterAI escort formation).</summary>
        public const string ZeppelinDefensePattern = "escort";

        /// <summary>
        /// The queued defense wave of one zeppelin (B080). Pure bookkeeping (EditMode-tested):
        /// Total fighters, Pending still to send; TryTake says whether one may go now: the
        /// zeppelin is alive, the stagger time has come and the live fighters counting toward
        /// the cap are below maxLive. When the zeppelin is no longer alive the rest of the wave
        /// is dropped. While at the cap the wave waits (queued) and sends as slots free.
        /// </summary>
        public sealed class DefenseWave {
            public readonly ZeppelinAI Target;
            public readonly int Total;
            public readonly float StaggerSeconds;
            public int Pending { get; private set; }
            public float NextAt { get; private set; }

            /// <summary>Index (0-based) of the next fighter to send.</summary>
            public int NextIndex => Total - Pending;

            /// <summary>Nothing left to send.</summary>
            public bool Done => Pending <= 0;

            public DefenseWave(ZeppelinAI target, int count, float staggerSeconds, float now) {
                Target = target;
                Total = Mathf.Max(0, count);
                Pending = Total;
                StaggerSeconds = Mathf.Max(0f, staggerSeconds);
                NextAt = now;
            }

            /// <summary>True when one fighter should spawn now (and counts it as sent).</summary>
            public bool TryTake(float now, bool targetAlive, int liveFighters, int maxLiveFighters) {
                if (Pending <= 0) return false;
                if (!targetAlive) { Pending = 0; return false; }
                if (now < NextAt) return false;
                if (liveFighters >= maxLiveFighters) return false;   // at the cap: stay queued
                Pending--;
                NextAt = now + StaggerSeconds;
                return true;
            }
        }

        /// <summary>Defense waves still sending this mission (cleared in teardown).</summary>
        private readonly List<DefenseWave> _defenseWaves = new List<DefenseWave>();

        /// <summary>Defense waves still queued (B080; for tests and debugging).</summary>
        public IReadOnlyList<DefenseWave> DefenseWaves => _defenseWaves;

        /// <summary>ZeppelinAI.ShieldDropped (B080): queues the defense wave for a zeppelin this
        /// mission spawned (once per zeppelin; none once the mission has ended or with the wave
        /// disabled). Generic: any mission with a zeppelin gets it.</summary>
        private void OnZeppelinShieldDropped(ZeppelinAI zeppelin) {
            if (zeppelin == null || zeppelin.IsDead || _missionEnded || _missionData == null) return;
            if (!_enemyAircraft.Contains(zeppelin)) return;
            for (int i = 0; i < _defenseWaves.Count; i++) {
                if (_defenseWaves[i].Target == zeppelin) return;
            }
            var data = ZeppelinDefenseData.Resolve(GameBalance.Get());
            if (!data.enabled || data.waveCount <= 0) return;
            _defenseWaves.Add(new DefenseWave(zeppelin, data.waveCount, data.staggerSeconds, Time.time));
            Debug.Log($"MissionRunner: zeppelin '{zeppelin.Id}' lost its turrets; {data.waveCount} defense fighter(s) queued.");
        }

        /// <summary>Sends due defense fighters (B080), at most one per wave per frame, while the
        /// live fighters near the view (EnemyPressure.CountsTowardCap) stay under
        /// pressure.maxLiveFighters. Finished waves are dropped.</summary>
        private void TickDefenseWaves(Camera cam) {
            if (_defenseWaves.Count == 0) return;
            if (_missionEnded || cam == null) {
                _defenseWaves.Clear();
                return;
            }
            var gm = GameManager.GetOrNull();
            if (gm != null && gm.CurrentState != GameManager.GameState.Playing) return;

            int maxLive = _pressure != null && _pressure.Settings != null
                ? _pressure.Settings.maxLiveFighters
                : PressureData.Resolve(GameBalance.Get()).maxLiveFighters;
            int live = CountFightersTowardCap(cam);
            float now = Time.time;
            for (int i = _defenseWaves.Count - 1; i >= 0; i--) {
                var w = _defenseWaves[i];
                bool alive = w.Target != null && !w.Target.IsDead;
                int index = w.NextIndex;
                if (w.TryTake(now, alive, live, maxLive)) {
                    if (SpawnZeppelinDefender(w.Target, index, w.Total) != null) live++;
                }
                if (w.Done) _defenseWaves.RemoveAt(i);
            }
        }

        /// <summary>Live fighters that count toward pressure.maxLiveFighters (the EnemyPressure
        /// rule: on screen, or off screen within a screen width and closing on the view).</summary>
        private int CountFightersTowardCap(Camera cam) {
            Rect view = EnemyPressure.ViewRect(cam);
            float range = view.width * EnemyPressure.CapRangeScreens;
            float camVelX = ScrollController.Instance != null ? ScrollController.Instance.Velocity : 0f;
            int n = 0;
            for (int i = 0; i < _enemyAircraft.Count; i++) {
                if (!(_enemyAircraft[i] is FighterAI f) || f == null || f.IsDead) continue;
                if (EnemyPressure.CountsTowardCap(f.transform.position.x, f.WorldVelocityX - camVelX, view, range)) n++;
            }
            return n;
        }

        /// <summary>The way the player faces (+1 right, -1 left): the scroll direction, else the
        /// plane's Facing, else right. Since B074 it no longer picks an entry side: every
        /// aircraft enters from the right edge.</summary>
        public int PlayerFacing {
            get {
                if (ScrollController.Instance != null) return ScrollController.Instance.Direction < 0 ? -1 : 1;
                if (_playerPlane != null) return _playerPlane.Facing < 0 ? -1 : 1;
                return 1;
            }
        }

        /// <summary>
        /// True while the mission runs and an unevaluated destroyAllOfType objective counts
        /// entities of `typeKey` (case-insensitive, the ObjectiveTracker match). EnemyPressure
        /// asks it for escaped fighters (B040): needed ones re-enter, the rest are removed.
        /// </summary>
        public bool OpenDestroyAllCounts(string typeKey) {
            if (!ObjectivesOpen || string.IsNullOrEmpty(typeKey) || _missionData.objectives == null) return false;
            foreach (var obj in _missionData.objectives) {
                if (obj == null || obj.evaluated || obj.type != "destroyAllOfType") continue;
                if (string.Equals(obj.targetType, typeKey, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Escaped-fighter re-entry (B040, GDD "Enemy pressure"): the same fighter (same name
        /// and damage, so it still counts for its objective) re-enters just past the RIGHT screen
        /// edge whatever the player's facing (B074), flying left with the entry boost
        /// (FighterAI.BeginEntry), at a random height in the flight band (RandomPlaneY, so sine
        /// fighters keep their dip allowance). Returns false without a camera.
        /// </summary>
        public bool ReEnterFighter(FighterAI fighter) {
            if (fighter == null) return false;
            var cam = ScrollController.Instance != null && ScrollController.Instance.Camera != null
                ? ScrollController.Instance.Camera : Camera.main;
            if (cam == null) return false;

            float x = EnemyPressure.EntryX(cam.transform.position.x, cam.orthographicSize * cam.aspect,
                EnemyPressure.EntryOffset(0, Random.value));
            float y = RandomPlaneY(cam, x, fighter.Pattern);
            fighter.ReEnter(new Vector3(x, y, 0f), EnemyPressure.EntryFlightDirection);
            fighter.BeginEntry(CameraVelocityX);
            return true;
        }

        /// <summary>Camera x velocity for the entry boost (units/s; 0 without a ScrollController):
        /// of the current velocity and the one it eases toward, the farther left (B075,
        /// FighterAI.BoostCameraVelocityNow), so a wave sent mid-turn starts fast enough.</summary>
        private static float CameraVelocityX =>
            FighterAI.BoostCameraVelocityNow(EnemyPressure.EntryFlightDirection);

        /// <summary>Distance past the right edge at which a JSON plane of type `typeLower` is
        /// centred before its stagger (B075, QA T975 BUG-4): PlaneEntryMarginX, but for a bomber
        /// at least its half width + EntryGap, so its nose never starts inside the view.</summary>
        public static float PlaneEntryMargin(string typeLower) {
            return typeLower == "bomber"
                ? Mathf.Max(PlaneEntryMarginX, EnemyPressure.BomberHalfWidth + EnemyPressure.EntryGap)
                : PlaneEntryMarginX;
        }

        /// <summary>
        /// Farthest camera x at which a spawn entry of type `typeLower` (lower case) triggers, for
        /// a view of half width `halfWidth` (B074 pure helper, the Update rule). Planes (fighter,
        /// bomber) trigger at atX and place themselves past the right edge (EntryX); a zeppelin
        /// (kept at atX) once its nose is ZeppelinEntryMarginX past the right edge; everything
        /// else (ground objects, balloons) once atX is GroundEntryMarginX past it. Spawns trigger
        /// on ScrollController.FarthestX, which only grows while the camera moves right, so a
        /// spawn placed at atX is always off the right edge when it appears.
        /// </summary>
        public static float SpawnTriggerX(string typeLower, float atX, float halfWidth) {
            if (typeLower == "fighter" || typeLower == "bomber") return atX;
            if (typeLower == "zeppelin") return atX - (halfWidth + ZeppelinAI.HalfLength + ZeppelinEntryMarginX);
            return atX - (halfWidth + GroundEntryMarginX);
        }

        /// <summary>
        /// Called when an entity is destroyed. Delegates to ObjectiveTracker for objective
        /// evaluation and checks if the last objective just completed — triggering mission end.
        /// </summary>
        public void HandleObjectiveComplete(string objectiveId) {
            // The mission end is handled once: Update and late kills keep reporting
            // AllComplete while the result screen is up (B006c).
            if (_missionEnded) return;

            Debug.Log($"MissionRunner: objective '{objectiveId}' completed");

            // Check if ALL objectives are now complete.
            if (_objectiveTracker != null && _objectiveTracker.AllComplete) {
                _missionEnded = true;
                Debug.Log("MissionRunner: all objectives complete — mission won!");
                // B069: a win decided by a kill opens the result screen killWinDelaySeconds
                // (real time) later, so the explosion / bomber wreck plays out; the player keeps
                // flying meanwhile (OnMissionEnded defers EndPlayerControl) but can't lose.
                _winResultDelay = WinResultDelayFor(objectiveId, GameManager.KillWinDelayOrDefault(GameBalance.Get()));
                // B070: the engine and smoke loops run on until the controls end.
                if (_playerPlane != null) _playerPlane.HoldAudioOnWin = _winResultDelay > 0f;
                // GetOrNull: never builds a GameManager outside Play mode (B047).
                GameManager.GetOrNull()?.WinMission(GameManager.DefaultWinReason, _winResultDelay);

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

                // destroyById: EntityDestroyed carries no id, so match the type key against
                // the id-tracked spawns and check which one is now dead. The dying object is
                // still alive in memory here (Destroy is deferred); a silently despawned one
                // is Unity-null and never counts as destroyed.
                for (int i = 0; i < _idTargets.Count; i++) {
                    var t = _idTargets[i];
                    if (t.Reported || t.Target == null || t.TypeKey != entityType) continue;
                    if (!((IDamageable)t.Target).IsDead) continue;
                    t.Reported = true;
                    _idTargets[i] = t;
                    _objectiveTracker.OnTargetDestroyed(t.Id);
                }

                // If the objective tracker marks completion, forward to MissionRunner.
                if (_objectiveTracker.AllComplete) {
                    HandleObjectiveComplete("all_objectives");
                }
            }
        }

        /// <summary>EnemyBase.Destroyed: completes a matching bombTarget when a bomb killed it.
        /// No points here: ScoreKeeper already scored the base from EntityDestroyed.</summary>
        private void OnEnemyBaseDestroyed(EnemyBase enemyBase) {
            if (_objectiveTracker == null || enemyBase == null || !enemyBase.KilledByBomb) return;
            _objectiveTracker.OnBombTargetDestroyed(enemyBase.Id, EnemyBase.TypeKey);
            if (_objectiveTracker.AllComplete) {
                HandleObjectiveComplete("bomb_target");
            }
        }

        /// <summary>
        /// An objective can no longer be met (its target reached failAtX, or the map end with it still
        /// open): the mission is lost once with `reason` through GameManager.LoseMission, which
        /// shows the reason on the result screen and raises MissionEnded(false, reason) (B016).
        /// </summary>
        private void HandleObjectiveFailed(string objectiveId, string reason) {
            if (_missionEnded) return;
            _missionEnded = true;
            Debug.Log($"MissionRunner: objective '{objectiveId}' failed — mission lost ({reason}).");

            var gm = GameManager.GetOrNull();
            if (gm != null) {
                gm.LoseMission(reason);
            } else {
                GameEvents.RaiseMissionEnded(false, reason);
            }
            // Normally OnMissionEnded already did this; covers a GameManager that refused the
            // loss because the state was already Won/Lost from a stale mission.
            EndPlayerControl();
        }

        /// <summary>Result-screen text for an objective whose target entity reached its
        /// failAtX (B018).</summary>
        private string TargetReachedReason(string objectiveId, string typeKey) {
            string desc = _objectiveTracker != null ? _objectiveTracker.GetDescriptionFor(objectiveId) : objectiveId;
            string who = string.IsNullOrEmpty(typeKey) ? "target" : typeKey;
            return $"Objective failed: {desc} (the {who} reached its target)";
        }

        /// <summary>
        /// targetX for a bomber/zeppelin spawn (B018). The failAtX of the open destroyById
        /// objective that targets this spawn's id wins, and returns true: MissionRunner then
        /// watches the entity and loses the mission when its world x reaches it. Without such
        /// an objective the spawn entry's own targetX (bomber bomb run only, no loss) is used and
        /// false is returned. A target at or right of the spawn x would lose the mission the
        /// moment the entity appears (it flies left): that is a mission data error, so it is
        /// logged and ignored (targetX -1, not watched); the map end still ends the mission.
        /// </summary>
        private bool ResolveTargetX(SpawnEntry entry, Vector3 spawnPosition, out float targetX) {
            float objectiveX = _objectiveTracker != null ? _objectiveTracker.GetFailAtXFor(entry.id) : -1f;
            bool fromObjective = objectiveX >= 0f;
            targetX = fromObjective ? objectiveX : entry.targetX;
            if (targetX < 0f) {
                targetX = -1f;
                return false;
            }
            if (spawnPosition.x <= targetX) {
                Debug.LogWarning($"MissionRunner: {entry.type} '{entry.id}' spawns at x={spawnPosition.x:0.#}, " +
                                 $"already at or past its targetX {targetX:0.#} (it flies left), so targetX is ignored. " +
                                 "Fix the mission JSON (failAtX / atX).");
                targetX = -1f;
                return false;
            }
            return fromObjective;
        }

        /// <summary>Result-screen text for an objective still open at the end of the map.</summary>
        private string MapEndReason(string objectiveId) {
            string desc = _objectiveTracker != null ? _objectiveTracker.GetDescriptionFor(objectiveId) : objectiveId;
            return $"Reached the end of the map with an objective open: {desc}";
        }

        /// <summary>Remembers a spawn with a non-empty id for destroyById. `target` must be
        /// an IDamageable component; null targets are ignored. `watchTargetX`: poll its world x
        /// against the objective's failAtX each Update (B018).</summary>
        private void TrackIdTarget(string id, string typeKey, MonoBehaviour target, bool watchTargetX = false) {
            if (string.IsNullOrEmpty(id) || target == null || !(target is IDamageable)) return;
            _idTargets.Add(new IdTarget { Id = id, TypeKey = typeKey, Target = target, WatchTargetX = watchTargetX });
        }

        /// <summary>A spawned entity with a JSON id, watched for destroyById.</summary>
        private struct IdTarget {
            public string Id;
            public string TypeKey;
            public MonoBehaviour Target;
            public bool Reported;
            /// <summary>An objective gave this entity a failAtX (its targetX) to watch.</summary>
            public bool WatchTargetX;
        }

        /// <summary>
        /// Entity-based failAtX (B018): the first live, watched id target whose world x has
        /// reached its objective's failAtX loses the mission with "... reached its target".
        /// Destroyed (IsDead), despawned (Unity-null) and already-reported targets are skipped,
        /// and a completed objective never fails (CheckTargetReached).
        /// </summary>
        private void CheckTargetsReached() {
            for (int i = 0; i < _idTargets.Count; i++) {
                var t = _idTargets[i];
                if (!t.WatchTargetX || t.Reported || t.Target == null) continue;
                if (((IDamageable)t.Target).IsDead) continue;
                string failedId = _objectiveTracker.CheckTargetReached(t.Id, t.Target.transform.position.x);
                if (failedId == null) continue;
                HandleObjectiveFailed(failedId, TargetReachedReason(failedId, t.TypeKey));
                return;
            }
        }

        /// <summary>Spawns with a non-empty id this mission (cleared in teardown).</summary>
        private readonly List<IdTarget> _idTargets = new List<IdTarget>();

        /// <summary>GameEvents.MissionEnded (win or loss): stop handling mission-end conditions
        /// and take the controls away from the player (B016). A delayed win (B069) keeps the
        /// controls and the scroll until its result screen opens (real-time deadline, checked
        /// in Update through TickWinControlEnd).</summary>
        private void OnMissionEnded(bool won, string reason) {
            _missionEnded = true;
            if (won && _winResultDelay > 0f) {
                _endControlAt = Time.realtimeSinceStartup + _winResultDelay;
                _winResultDelay = 0f;
                return;
            }
            _winResultDelay = 0f;
            _endControlAt = -1f;
            EndPlayerControl();
        }

        /// <summary>Win delay in real seconds for a win reported as `objectiveId` (B069): the
        /// kill paths ("all_objectives" from EntityDestroyed, "bomb_target" from a bombed
        /// base) get `killDelay`; any other path (survive_to_end at the map end) opens the
        /// result at once, as before. Pure.</summary>
        public static float WinResultDelayFor(string objectiveId, float killDelay) {
            bool fromKill = objectiveId == WinFromKill || objectiveId == WinFromBombTarget;
            return fromKill ? Mathf.Max(0f, killDelay) : 0f;
        }

        /// <summary>HandleObjectiveComplete ids of the kill-driven win paths.</summary>
        public const string WinFromKill = "all_objectives";
        public const string WinFromBombTarget = "bomb_target";

        /// <summary>True once a deferred control end at `endAt` (real seconds; negative = none)
        /// is due at `now`. Pure.</summary>
        public static bool ControlEndDue(float now, float endAt) {
            return endAt >= 0f && now >= endAt;
        }

        /// <summary>Ends the player's control once a delayed win's deadline has passed (B069).
        /// Returns true when it did.</summary>
        internal bool TickWinControlEnd(float now) {
            if (!ControlEndDue(now, _endControlAt)) return false;
            _endControlAt = -1f;
            EndPlayerControl();
            return true;
        }

        /// <summary>Real-time result delay of the win being raised (B069), read by
        /// OnMissionEnded; 0 = none.</summary>
        private float _winResultDelay;

        /// <summary>Time.realtimeSinceStartup at which a delayed win takes the controls away;
        /// negative = nothing pending.</summary>
        private float _endControlAt = -1f;

        /// <summary>
        /// Any mission end: the player's PlayerInput stops polling (no more move, fire or bomb
        /// events) and the camera stops scrolling, so the ended world holds still behind the
        /// result screen. StartMission (Next) builds a new plane and ScrollController.Create
        /// restores the speed; Retry reloads the scene.
        /// </summary>
        private void EndPlayerControl() {
            if (_playerPlane != null) {
                var input = _playerPlane.GetComponent<PlayerInput>();
                if (input != null) input.enabled = false;
                // Engine / smoke loops stop with the controls (B070; idempotent, and a
                // crashing plane keeps its wind-down).
                _playerPlane.EndMissionAudio();
            }
            if (ScrollController.Instance != null) ScrollController.Instance.SetScrollSpeed(0f);
        }

        // ── Core loop (Update in MonoBehaviour) ────────────────────────────

        private void Update() {
            TickWinControlEnd(Time.realtimeSinceStartup);
            if (_missionData == null || _spawnedFlags == null) return;

            float cameraX = ScrollController.Instance != null
                ? ScrollController.Instance.MissionDistanceX
                : -999f;
            // Spawns trigger on the farthest x reached, so flying back over ground already
            // covered never re-triggers a group or brings one in early.
            float farthestX = ScrollController.Instance != null
                ? ScrollController.Instance.FarthestX
                : -999f;

            // Ground objects (AAA, balloons, buildings, trees) sit at world x = atX. Spawn them
            // once that spot is just past the right screen edge, so they scroll into view
            // instead of popping in mid-screen. Planes still trigger at camera x >= atX and
            // place themselves off-screen (SpawnEntities).
            // Zeppelins are long: the hull (centred on atX) must be wholly off screen when it
            // appears, so its look-ahead adds half the hull length (B021). SpawnTriggerX holds
            // the rule; without a camera every entry triggers at its atX.
            var cam = ScrollController.Instance != null && ScrollController.Instance.Camera != null
                ? ScrollController.Instance.Camera : Camera.main;
            float halfWidth = cam != null ? cam.orthographicSize * cam.aspect : 0f;

            // Check each spawn entry: has the camera reached its threshold?
            for (int i = 0; i < _spawnedFlags.Length && i < _missionData.spawns.Length; i++) {
                var spawn = _missionData.spawns[i];
                string spawnType = spawn.type != null ? spawn.type.ToLowerInvariant() : "";
                float trigger = cam != null ? SpawnTriggerX(spawnType, spawn.atX, halfWidth) : spawn.atX;
                if (!_spawnedFlags[i] && farthestX >= trigger) {
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

            UpdateBombRecharge(cam);
            TickDefenseWaves(cam);

            // failAtX (B018): the target entity itself (mission 2 bomber, mission 5 zeppelin)
            // reaching its failAtX world x loses the mission. The camera position plays no part,
            // so the player can't dodge it by turning back.
            if (!_missionEnded && _objectiveTracker != null) {
                CheckTargetsReached();
            }

            // Check survive-to-end objective via camera progress.
            if (!_missionEnded && _objectiveTracker != null && _missionData.length > 0f) {
                _objectiveTracker.OnCameraProgress(cameraX, _missionData.length);
                if (_objectiveTracker.AllComplete) {
                    HandleObjectiveComplete("survive_to_end");
                }
            }

            // Map end (GDD change log 2026-11-13): the camera reached the mission length and an
            // objective other than surviveToEnd is still open — it can't be met any more, so
            // the mission is lost instead of running on forever (T936 P1-1).
            if (!_missionEnded && _objectiveTracker != null && _missionData.length > 0f) {
                string openId = _objectiveTracker.CheckMapEnd(cameraX, _missionData.length);
                if (openId != null) HandleObjectiveFailed(openId, MapEndReason(openId));
            }
        }

        // ── Mission-5 HQ cheat (B078) ────────────────────────────────────

        /// <summary>The HQ cheat may fire now (CheatCodes.CanUseCheat): mission 5, GameManager
        /// Playing, started (B049 gate), plane airborne and not crashed, mission not ended.
        /// CheatCodes also checks that no UI screen is up.</summary>
        public bool CanUseHqCheat {
            get {
                if (_missionData == null || _missionData.id != CheatCodes.HqCheatMissionId) return false;
                var gm = GameManager.GetOrNull();
                bool playing = gm != null && gm.CurrentState == GameManager.GameState.Playing;
                return CheatCodes.CanUseCheat(_missionData.id, playing, HasStarted, _playerPlane != null,
                    _playerPlane != null && _playerPlane.OnRunway,
                    _playerPlane != null && _playerPlane.HasCrashed, _missionEnded);
            }
        }

        /// <summary>The typed cheat (CheatCodes): JumpToHq when CanUseHqCheat, else nothing.</summary>
        public bool TryHqCheat() {
            return CanUseHqCheat && JumpToHq();
        }

        /// <summary>
        /// Jumps the player and the camera to the player HQ (B078; ungated, TryHqCheat gates it):
        ///  1. HQ x = the mission's playerHQ spawn entry atX (no entry: nothing happens);
        ///  2. facing = toward the zeppelin (live one, else its spawn entry; none: right), so it
        ///     comes from ahead (CheatCodes.FacingTowardThreat);
        ///  3. camera x = the one that puts the HQ on the plane's trailing screen anchor for that
        ///     facing, clamped to the mission limits;
        ///  4. pending spawns the jump passes are skipped, except the HQ, the zeppelin, escorts and
        ///     objective targets (CheatCodes.SkipPassedSpawns): those spawn normally next Update;
        ///  5. the camera moves, then the plane warps above the HQ (CheatCodes.WarpY, inside the
        ///     view) facing that way (PlayerPlane.WarpTo also sets the scroll direction), and the
        ///     terrain window is rebuilt at once;
        ///  6. the run is flagged CheatUsed and best-score saves are locked.
        /// Idempotent: at the HQ already, it lands on the same spot and skips nothing new.
        /// Returns false when nothing happened.
        /// </summary>
        public bool JumpToHq() {
            if (_missionData == null || _spawnedFlags == null || _playerPlane == null) return false;
            if (_playerPlane.HasCrashed || _playerPlane.OnRunway) return false;
            if (!TryGetHqX(out float hqX)) {
                Debug.LogWarning("MissionRunner: HQ cheat — this mission has no playerHQ spawn; ignored.");
                return false;
            }
            var sc = ScrollController.Instance;
            var cam = sc != null && sc.Camera != null ? sc.Camera : Camera.main;
            if (cam == null) return false;

            float halfWidth = cam.orthographicSize * cam.aspect;
            int facing = CheatCodes.FacingTowardThreat(hqX, ThreatX(hqX));
            float maxX = Mathf.Max(MissionStartCameraX, _missionData.length);
            float camX = Mathf.Clamp(CheatCodes.CameraXForAnchor(hqX, facing, _playerPlane.ScreenAnchor, halfWidth),
                MissionStartCameraX, maxX);

            int skipped = CheatCodes.SkipPassedSpawns(_missionData.spawns, _spawnedFlags, _missionData.objectives,
                camX, halfWidth);

            Vector3 camPos = cam.transform.position;
            cam.transform.position = new Vector3(camX, camPos.y, camPos.z);

            float planeX = Mathf.Clamp(hqX, camX - halfWidth + 1f, camX + halfWidth - 1f);
            float y = CheatCodes.WarpY(GetTerrainHeightAt(planeX), camPos.y + cam.orthographicSize);
            _playerPlane.WarpTo(new Vector2(planeX, y), facing);
            if (_terrain != null) _terrain.RefreshWindowNow();

            CheatUsed = true;
            SaveSystem.BestScoreLocked = true;
            Debug.Log($"MissionRunner: HQ cheat — jumped to x={planeX:0.#} (camera {camX:0.#}), facing {facing}, " +
                      $"skipped {skipped} spawn group(s). Best score locked for this run.");
            return true;
        }

        /// <summary>The first playerHQ spawn entry's atX.</summary>
        private bool TryGetHqX(out float hqX) {
            hqX = 0f;
            if (_missionData?.spawns == null) return false;
            foreach (var s in _missionData.spawns) {
                if (s == null || s.type == null || s.type.ToLowerInvariant() != "playerhq") continue;
                hqX = s.atX;
                return true;
            }
            return false;
        }

        /// <summary>World x of the threat to face at the HQ: a live zeppelin, else the first
        /// zeppelin spawn entry's atX, else just right of the HQ.</summary>
        private float ThreatX(float hqX) {
            foreach (var a in _enemyAircraft) {
                if (a is ZeppelinAI && EnemyPressure.IsLiveAircraft(a)) return a.transform.position.x;
            }
            if (_missionData?.spawns != null) {
                foreach (var s in _missionData.spawns) {
                    if (s != null && s.type != null && s.type.ToLowerInvariant() == "zeppelin") return s.atX;
                }
            }
            return hqX + 1f;
        }

        // ── Recharge bomb balloon (B064) ─────────────────────────────────

        /// <summary>GameEvents.BombsChanged: an empty bay arms the recharge timer, a refill cancels it.</summary>
        private void OnBombsChanged(int bombs) {
            _lastBombs = bombs;
            if (_bombTimer != null) _bombTimer.OnBombsChanged(bombs);
        }

        /// <summary>Last BombsChanged value this mission (-1 = none yet).</summary>
        private int _lastBombs = -1;

        /// <summary>
        /// B065 re-arm rule (pure): the bay is still empty, the timer is idle (its balloon was
        /// already spawned) and no live or pending bomb balloon serves the player any more (the
        /// auto one was missed, scrolled off behind, or was recycled), so a new countdown starts.
        /// </summary>
        public static bool ShouldRearmRecharge(int bombsLeft, bool timerArmed, bool balloonServesPlayer) {
            return bombsLeft == 0 && !timerArmed && !balloonServesPlayer;
        }

        /// <summary>
        /// Drives the empty-bay recharge timer (B064). It only counts in scaled time (pauses with
        /// the game) once the mission has started (not during the scroll hold), and is cancelled
        /// by a crash or the mission end. When due, one BombBalloon spawns `lead` ahead of the
        /// player, unless an uncollected bomb balloon (spawned or still pending in the JSON)
        /// already serves the player: then it retries every frame until that one is gone.
        /// B065: while the bay stays empty and no bomb balloon serves the player any more (the
        /// auto one was missed), the timer re-arms, so another spawns bombBalloonDelay later.
        /// </summary>
        private void UpdateBombRecharge(Camera cam) {
            if (_bombTimer == null) return;
            if (_missionEnded || _playerPlane == null || _playerPlane.HasCrashed) {
                if (_bombTimer.Armed) _bombTimer.Cancel();
                return;
            }
            if (!HasStarted) return;

            Vector3 p = _playerPlane.transform.position;
            int facing = _playerPlane.Facing < 0 ? -1 : 1;

            if (!_bombTimer.Armed) {
                // Cheap check first: only an empty bay can need a re-arm.
                if (_lastBombs != 0) return;
                if (ShouldRearmRecharge(_lastBombs, false, BombBalloonServesPlayer(cam, p.x, facing))) {
                    _bombTimer.Reset();
                    _bombTimer.OnBombsChanged(0);
                }
                return;
            }

            if (!_bombTimer.Tick(Time.deltaTime)) return;
            if (BombBalloonServesPlayer(cam, p.x, facing)) return;

            float maxX = _missionData.length > 0f ? _missionData.length : float.MaxValue;
            float x = BombBalloon.AutoSpawnX(p.x, facing, _bombBalloonData.lead, MissionStartCameraX, maxX);
            float low = GetTerrainHeightAt(x) + BombBalloonMinHeight;
            float high = cam != null ? cam.transform.position.y + cam.orthographicSize - PlaneTopMargin : low + 6f;
            float y = BombBalloon.AutoSpawnY(p.y, low, high);

            var balloon = BombBalloon.Spawn(new Vector3(x, y, 0f), true);
            _bombBalloons.Add(balloon);
            _spawnedEntities.Add(balloon.gameObject);
            _bombTimer.MarkSpawned();
        }

        /// <summary>True when a live uncollected bomb balloon is on screen or ahead of the player
        /// within the lead, or a not-yet-spawned JSON bombBalloon entry lies in that span.</summary>
        private bool BombBalloonServesPlayer(Camera cam, float playerX, int facing) {
            float camX = cam != null ? cam.transform.position.x : playerX;
            float halfWidth = cam != null ? cam.orthographicSize * cam.aspect : 0f;
            float lead = _bombBalloonData.lead;

            _bombBalloons.RemoveAll(b => b == null);
            foreach (var b in _bombBalloons) {
                if (b.IsDead || !b.gameObject.activeInHierarchy) continue;
                if (BombBalloon.BlocksAutoSpawn(b.transform.position.x, playerX, facing, lead, camX, halfWidth)) return true;
            }

            if (_missionData.spawns != null && _spawnedFlags != null) {
                for (int i = 0; i < _missionData.spawns.Length && i < _spawnedFlags.Length; i++) {
                    var s = _missionData.spawns[i];
                    if (_spawnedFlags[i] || s == null || s.type == null) continue;
                    if (s.type.ToLowerInvariant() != "bombballoon") continue;
                    if (BombBalloon.BlocksAutoSpawn(s.atX, playerX, facing, lead, camX, halfWidth)) return true;
                }
            }
            return false;
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

        /// <summary>Camera x every mission starts at (the scene camera sits at x = 0).</summary>
        private const float MissionStartCameraX = 0f;

        /// <summary>Moves Camera.main back to the mission start x, keeping its y and z.</summary>
        private static void ResetCameraToMissionStart() {
            var cam = Camera.main;
            if (cam == null) return;
            var p = cam.transform.position;
            cam.transform.position = new Vector3(MissionStartCameraX, p.y, p.z);
        }

        /// <summary>
        /// Removes everything the previous StartMission created on this runner: the player
        /// plane, every spawned entity, the HUD canvas, extra ObjectiveTrackers and the
        /// EntityDestroyed subscription. Safe to call when nothing was started yet.
        /// </summary>
        private void TearDownPreviousMission() {
            GameEvents.EntityDestroyed -= OnEntityDestroyed;
            GameEvents.MissionEnded -= OnMissionEnded;
            EnemyBase.Destroyed -= OnEnemyBaseDestroyed;
            GameEvents.BombsChanged -= OnBombsChanged;
            ZeppelinAI.ShieldDropped -= OnZeppelinShieldDropped;
            _idTargets.Clear();
            _bombBalloons.Clear();
            // B080: queued defense fighters never outlive their mission. Sent ones and a dying
            // zeppelin (its death sequence) are in _spawnedEntities and go below.
            _defenseWaves.Clear();
            _winResultDelay = 0f;
            _endControlAt = -1f;
            if (_bombTimer != null) _bombTimer.Reset();

            if (_playerPlane != null) DestroyNow(_playerPlane.gameObject);
            _playerPlane = null;

            foreach (var go in _spawnedEntities) {
                if (go != null) DestroyNow(go);
            }
            _spawnedEntities.Clear();
            // Falling bomber wrecks (B068) are scene-root visuals outside _spawnedEntities.
            BomberWreck.ClearAll();
            _enemyAircraft.Clear();
            _groundEnemies.Clear();
            _aaagunsToInit.Clear();

            if (_hud != null) DestroyNow(_hud.gameObject);
            _hud = null;

            // Keep one tracker (StartMission re-Inits it), remove any extras.
            var trackers = GetComponents<ObjectiveTracker>();
            for (int i = 1; i < trackers.Length; i++) DestroyNow(trackers[i]);
            _objectiveTracker = null;
        }

        /// <summary>
        /// Deactivates then destroys an object, so it stops updating and reacting to input
        /// this frame (Destroy itself is deferred). Uses DestroyImmediate outside Play mode.
        /// </summary>
        private static void DestroyNow(Object obj) {
            if (obj == null) return;
            if (obj is GameObject go) go.SetActive(false);
            else if (obj is Behaviour b) b.enabled = false;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private void OnDestroy() {
            GameEvents.EntityDestroyed -= OnEntityDestroyed;
            GameEvents.MissionEnded -= OnMissionEnded;
            EnemyBase.Destroyed -= OnEnemyBaseDestroyed;
            GameEvents.BombsChanged -= OnBombsChanged;
            ZeppelinAI.ShieldDropped -= OnZeppelinShieldDropped;
            _defenseWaves.Clear();
            _spawnedEntities.Clear();
            _bombBalloons.Clear();
            _enemyAircraft.Clear();
            _groundEnemies.Clear();
            _idTargets.Clear();
        }

        // ── AAAGun init queue (populated during SpawnEntities, wired after all spawns) ─

        private readonly List<AAAGun> _aaagunsToInit = new List<AAAGun>();
    }
}
