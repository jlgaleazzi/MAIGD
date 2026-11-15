using UnityEngine;

namespace Biplanes.Core {
    /// <summary>
    /// Serializable data class matching the "pitch" section of Balance.json (GDD "Flight
    /// attitude (pitch)"). Angles in degrees, speeds in world units per second. A missing block
    /// or a key that is missing or not positive falls back to the PlanePitch.Default* const;
    /// read it through Resolve.
    /// </summary>
    [System.Serializable]
    public class PitchData {
        /// <summary>Flight-attitude limit for every plane (climb/dive).</summary>
        public float maxDegrees;
        /// <summary>How far a fighter may pitch toward the player to aim its gun.</summary>
        public float fighterMaxDegrees;
        /// <summary>How fast a nose turns toward its target pitch.</summary>
        public float easeDegreesPerSecond;
        /// <summary>Vertical speed at which the flight attitude reaches maxDegrees.</summary>
        public float fullPitchSpeed;

        /// <summary>A sanitised copy of balance.pitch: missing or non-positive values become
        /// the PlanePitch defaults and the limits stay below 90 degrees. Never null.</summary>
        public static PitchData Resolve(GameBalance balance) {
            var src = balance != null ? balance.pitch : null;
            var r = new PitchData();
            r.maxDegrees = Mathf.Min(PlanePitch.AbsoluteMaxDegrees,
                Pick(src != null ? src.maxDegrees : 0f, PlanePitch.DefaultMaxDegrees));
            r.fighterMaxDegrees = Mathf.Min(PlanePitch.AbsoluteMaxDegrees,
                Pick(src != null ? src.fighterMaxDegrees : 0f, PlanePitch.DefaultFighterMaxDegrees));
            r.easeDegreesPerSecond = Pick(src != null ? src.easeDegreesPerSecond : 0f,
                PlanePitch.DefaultEaseDegreesPerSecond);
            r.fullPitchSpeed = Pick(src != null ? src.fullPitchSpeed : 0f, PlanePitch.DefaultFullPitchSpeed);
            return r;
        }

        private static float Pick(float value, float fallback) {
            return value > 0f ? value : fallback;
        }
    }

    /// <summary>
    /// Serializable data class matching the "player" section of Balance.json.
    /// See ARCHITECTURE.md §4 for the full schema.
    /// </summary>
    [System.Serializable]
    public class PlayerData {
        public float speed;
        public float fireRate;
        public float bulletSpeed;
        public float bulletDamage;
        public int bombCapacity;
        public float bombDamage;
        public float smokeThreshold;
        /// <summary>Seconds for the scroll to swing from full speed one way to full speed the other.</summary>
        public float turnEaseSeconds;
        /// <summary>Viewport x the plane eases toward when facing right (1 - this when facing left).</summary>
        public float screenAnchor;
    }

    /// <summary>
    /// Serializable data class matching the "damageTaken" section of Balance.json.
    /// All values are percentage damage applied per hit.
    /// See ARCHITECTURE.md §4 for the full schema.
    /// </summary>
    [System.Serializable]
    public class DamageTakenData {
        public float fighterBullet;
        public float flak;
        public float bomberGunner;
        public float zeppelinTurret;
        public float collision;
        /// <summary>Mid-air contact with an enemy fighter or bomber (B025). Read it through
        /// PlaneCollisionOrDefault: a missing or non-positive value means DefaultPlaneCollision.</summary>
        public float planeCollision;

        /// <summary>Used when Balance.json has no (or a non-positive) damageTaken.planeCollision.</summary>
        public const float DefaultPlaneCollision = 50f;

        /// <summary>damageTaken.planeCollision, or DefaultPlaneCollision when it is missing.
        /// `data` may be null (Balance.json not loaded).</summary>
        public static float PlaneCollisionOrDefault(DamageTakenData data) {
            return data != null && data.planeCollision > 0f ? data.planeCollision : DefaultPlaneCollision;
        }
    }

    /// <summary>
    /// Serializable data class matching the "enemyHealth" section of Balance.json.
    /// Values are health points (absolute, not percentage).
    /// See ARCHITECTURE.md §4 for the full schema.
    /// </summary>
    [System.Serializable]
    public class EnemyHealthData {
        public float fighter;
        public float bomber;
        public float aaa;
        public float barrageBalloon;
        public float zeppelinTurret;
        public float zeppelinHull;
        public float enemyBase;
    }

    /// <summary>
    /// Serializable data class matching the "score" section of Balance.json.
    /// Values are points awarded per kill.
    /// See ARCHITECTURE.md §4 for the full schema.
    /// </summary>
    [System.Serializable]
    public class ScoreData {
        public int fighter;
        public int barrageBalloon;
        public int aaa;
        public int zeppelinTurret;
        public int bomber;
        public int enemyBase;
        public int zeppelin;
    }

    /// <summary>
    /// Serializable data class matching the "background" section of Balance.json:
    /// parallax factors for ParallaxBackground (0 = fixed to the screen, 1 = moves with the world).
    /// </summary>
    [System.Serializable]
    public class BackgroundData {
        public float farFactor;
        public float midFactor;
        public float nearFactor;
    }

    /// <summary>
    /// Mirrors the "enemySpeed" block in Balance.json: how fast each enemy flyer flies left,
    /// in world units per second (the camera scrolls right at the mission's scrollSpeed).
    /// A missing key reads as 0; callers then use their own fallback.
    /// </summary>
    [System.Serializable]
    public class EnemySpeedData {
        public float fighter;
        public float bomber;
        public float zeppelin;
    }

    /// <summary>
    /// Mirrors the "enemyFire" block in Balance.json (B023): how enemy fighters and bomber
    /// gunners shoot at the player with EnemyBullet. Times in seconds, speed in world units
    /// per second, range in world units, angles in degrees. A missing block or a key that is
    /// missing or not positive falls back to the Default* value; read it through Resolve.
    /// </summary>
    [System.Serializable]
    public class EnemyFireData {
        public const float DefaultFighterIntervalMin = 1.8f;
        public const float DefaultFighterIntervalMax = 2.5f;
        public const float DefaultFighterConeDegrees = 40f;
        public const float DefaultBomberIntervalMin = 2.5f;
        public const float DefaultBomberIntervalMax = 3.0f;
        public const float DefaultFirstShotDelayMin = 0.4f;
        public const float DefaultFirstShotDelayMax = 1.2f;
        public const float DefaultBulletSpeed = 8f;
        public const float DefaultBulletLifetime = 1.6f;
        public const float DefaultRange = 11f;
        public const float DefaultSpreadDegrees = 4f;
        public const float DefaultAimToleranceDegrees = 10f;

        /// <summary>Seconds between one fighter's shots: random in [min, max] per shot.</summary>
        public float fighterIntervalMin;
        public float fighterIntervalMax;
        /// <summary>Half-angle of the cone ahead of a fighter (its flight direction, left) the
        /// player must be inside for it to fire. Capped below 90 so it never fires backwards.</summary>
        public float fighterConeDegrees;
        /// <summary>Seconds between a bomber gunner's shots: random in [min, max] per shot.</summary>
        public float bomberIntervalMin;
        public float bomberIntervalMax;
        /// <summary>Random delay before the first shot, counted only while the shooter is on
        /// screen, so a wave entering together does not volley.</summary>
        public float firstShotDelayMin;
        public float firstShotDelayMax;
        /// <summary>EnemyBullet speed (the player's bullet flies at player.bulletSpeed, 14).</summary>
        public float bulletSpeed;
        /// <summary>EnemyBullet lifetime before it recycles.</summary>
        public float bulletLifetime;
        /// <summary>Max distance to the player at which fighters and gunners fire.</summary>
        public float range;
        /// <summary>Random aim error, ± this many degrees around the exact aim.</summary>
        public float spreadDegrees;
        /// <summary>B029: a fighter fires along its nose, and only while the player is within
        /// this many degrees of the nose direction (it pitches toward the player meanwhile).</summary>
        public float aimToleranceDegrees;

        /// <summary>A sanitised copy of balance.enemyFire: every missing or non-positive value
        /// becomes its default, each max is at least its min, and the fighter cone stays
        /// below 90 degrees. Never null.</summary>
        public static EnemyFireData Resolve(GameBalance balance) {
            var src = balance != null ? balance.enemyFire : null;
            var r = new EnemyFireData();
            r.fighterIntervalMin = Pick(src != null ? src.fighterIntervalMin : 0f, DefaultFighterIntervalMin);
            r.fighterIntervalMax = Mathf.Max(r.fighterIntervalMin,
                Pick(src != null ? src.fighterIntervalMax : 0f, DefaultFighterIntervalMax));
            r.fighterConeDegrees = Mathf.Min(89f,
                Pick(src != null ? src.fighterConeDegrees : 0f, DefaultFighterConeDegrees));
            r.bomberIntervalMin = Pick(src != null ? src.bomberIntervalMin : 0f, DefaultBomberIntervalMin);
            r.bomberIntervalMax = Mathf.Max(r.bomberIntervalMin,
                Pick(src != null ? src.bomberIntervalMax : 0f, DefaultBomberIntervalMax));
            r.firstShotDelayMin = Pick(src != null ? src.firstShotDelayMin : 0f, DefaultFirstShotDelayMin);
            r.firstShotDelayMax = Mathf.Max(r.firstShotDelayMin,
                Pick(src != null ? src.firstShotDelayMax : 0f, DefaultFirstShotDelayMax));
            r.bulletSpeed = Pick(src != null ? src.bulletSpeed : 0f, DefaultBulletSpeed);
            r.bulletLifetime = Pick(src != null ? src.bulletLifetime : 0f, DefaultBulletLifetime);
            r.range = Pick(src != null ? src.range : 0f, DefaultRange);
            r.spreadDegrees = Pick(src != null ? src.spreadDegrees : 0f, DefaultSpreadDegrees);
            r.aimToleranceDegrees = Pick(src != null ? src.aimToleranceDegrees : 0f, DefaultAimToleranceDegrees);
            return r;
        }

        private static float Pick(float value, float fallback) {
            return value > 0f ? value : fallback;
        }
    }

    /// <summary>
    /// Root serialisable class matching Balance.json. Loaded once from Resources at
    /// runtime and cached in the static _instance field for fast access by all systems.
    ///
    /// Usage:  GameBalance.Get().player.speed   — or any other tunable.
    ///
    /// See ARCHITECTURE.md §4 (line 123-131) for the JSON schema this class mirrors.
    /// </summary>
    [System.Serializable]
    public class GameBalance {

        // JsonUtility requires public fields whose names exactly match the JSON keys.
        // Balance.json uses snake_case / pascal-case keys — these are set verbatim.
        // Gameplay code should read via properties below (e.g. Get().player.speed).

        /// <summary>Mirrors the "player" key in Balance.json.</summary>
        public PlayerData player;

        /// <summary>Mirrors the "damageTaken" key in Balance.json.</summary>
        public DamageTakenData damageTaken;

        /// <summary>Mirrors the "healthBalloonRestore" key in Balance.json.</summary>
        public float healthBalloonRestore;

        /// <summary>Mirrors the "enemyHealth" key in Balance.json.</summary>
        public EnemyHealthData enemyHealth;

        /// <summary>Mirrors the "score" key in Balance.json.</summary>
        public ScoreData score;

        /// <summary>Mirrors the "hullVulnerableEarly" key in Balance.json.</summary>
        public bool hullVulnerableEarly;

        /// <summary>Mirrors the "background" key in Balance.json (parallax factors).</summary>
        public BackgroundData background;

        /// <summary>Mirrors the "enemySpeed" key in Balance.json (world units per second).</summary>
        public EnemySpeedData enemySpeed;

        /// <summary>Mirrors the "enemyFire" key in Balance.json (B023). Read it through
        /// EnemyFireData.Resolve(GameBalance.Get()), which fills in defaults.</summary>
        public EnemyFireData enemyFire;

        /// <summary>Mirrors the "pitch" key in Balance.json (T942/B029). Read it through
        /// PitchData.Resolve(GameBalance.Get()), which fills in defaults.</summary>
        public PitchData pitch;

        // --- cached instance for Get() singleton pattern ---

        /// <summary>Cached by Get(); never use directly — call GameBalance.Get().</summary>
        private static GameBalance _instance;

        // --- accessor properties (allows future read-only wrapping) ---

        public PlayerData playerData   => player;
        public DamageTakenData damageTakenData   => damageTaken;
        public EnemyHealthData enemyHealthData   => enemyHealth;
        public ScoreData scoreData         => score;

        /// <summary>
        /// Returns the singleton GameBalance, loading it from Resources on first access.
        /// </summary>
        public static GameBalance Get() {
            if (_instance == null) {
                _instance = Load();
            }
            return _instance;
        }

        /// <summary>
        /// Loads Balance.json (as a TextAsset) from the Resources folder and deserialises it.
        /// Returns null with a warning if the asset is missing — callers must handle this.
        /// </summary>
        public static GameBalance Load() {
            var textAsset = Resources.Load<TextAsset>("Balance");
            if (textAsset == null) {
                Debug.LogWarning("GameBalance: Resources/Balance.json not found. "
                    + "The game will run with default values if callers provide fallbacks.");
                return null;
            }
            var balance = JsonUtility.FromJson<GameBalance>(textAsset.text);
            return balance;
        }
    }
}
