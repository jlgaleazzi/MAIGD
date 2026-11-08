using UnityEngine;

namespace Biplanes.Core {

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
    /// Mirrors the "enemySpeed" block in Balance.json: how fast each enemy plane flies left,
    /// in world units per second (the camera scrolls right at the mission's scrollSpeed).
    /// </summary>
    [System.Serializable]
    public class EnemySpeedData {
        public float fighter;
        public float bomber;
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
