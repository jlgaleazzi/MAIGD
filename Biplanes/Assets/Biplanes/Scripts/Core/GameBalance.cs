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
        /// <summary>Player toughness (B052): every non-fatal damageTaken hit on the player is divided
        /// by it. Read it through ResistanceOrDefault: a missing or non-positive value means 1.</summary>
        public float resistance;
        /// <summary>Seconds for the plane's eased turn-around look (B066). Read it through
        /// TurnSecondsOrDefault: missing or non-positive means fighterChase.turnSeconds.</summary>
        public float turnSeconds;

        /// <summary>player.turnSeconds, or the fighters' FighterChaseData turnSeconds (default
        /// 0.6) when it is missing or &lt;= 0. `balance` may be null.</summary>
        public static float TurnSecondsOrDefault(GameBalance balance) {
            var p = balance != null ? balance.player : null;
            if (p != null && p.turnSeconds > 0f) return p.turnSeconds;
            return FighterChaseData.Resolve(balance).turnSeconds;
        }

        /// <summary>Used when Balance.json has no (or a non-positive) player.resistance: no change.</summary>
        public const float DefaultResistance = 1f;

        /// <summary>player.resistance, or DefaultResistance when it is missing or &lt;= 0.
        /// `data` may be null (Balance.json not loaded).</summary>
        public static float ResistanceOrDefault(PlayerData data) {
            return data != null && data.resistance > 0f ? data.resistance : DefaultResistance;
        }
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

        /// <summary>Flying into a barrage balloon (B072), in the same units as planeCollision:
        /// raw percent before player.resistance (50 / 1.5 = 33.3% taken). Read it through
        /// BarrageBalloonCollisionOrDefault: a missing or non-positive value means
        /// DefaultBarrageBalloonCollision.</summary>
        public float barrageBalloonCollision;

        /// <summary>Used when Balance.json has no (or a non-positive) damageTaken.barrageBalloonCollision.</summary>
        public const float DefaultBarrageBalloonCollision = 50f;

        /// <summary>damageTaken.barrageBalloonCollision, or DefaultBarrageBalloonCollision when it
        /// is missing. `data` may be null (Balance.json not loaded).</summary>
        public static float BarrageBalloonCollisionOrDefault(DamageTakenData data) {
            return data != null && data.barrageBalloonCollision > 0f
                ? data.barrageBalloonCollision : DefaultBarrageBalloonCollision;
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

        /// <summary>Barrage balloon hit points when Balance.json has none (B072: 100, i.e. four
        /// 25-point player bullets or one 100-point bomb).</summary>
        public const float DefaultBarrageBalloon = 100f;

        /// <summary>enemyHealth.barrageBalloon, or DefaultBarrageBalloon when it is missing or
        /// non-positive. `data` may be null (Balance.json not loaded).</summary>
        public static float BarrageBalloonOrDefault(EnemyHealthData data) {
            return data != null && data.barrageBalloon > 0f ? data.barrageBalloon : DefaultBarrageBalloon;
        }
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
    /// Serializable data class matching the "audio" section of Balance.json (T949, ARCHITECTURE
    /// §3 Audio). Volumes are 0..1 multipliers; a one-shot plays at master * sfx. Read it through
    /// Resolve: a missing block, or a key that is missing or not positive, falls back to the
    /// Default* value (JsonUtility can't tell a missing key from 0, so mute with e.g. 0.001).
    /// </summary>
    [System.Serializable]
    public class AudioData {
        public const float DefaultMaster = 0.8f;
        public const float DefaultSfx = 1.0f;
        public const float DefaultMusic = 0.6f;
        public const float DefaultPitchJitter = 0.06f;
        public const float DefaultVolumeJitter = 0.15f;
        public const float DefaultOffscreenVolume = 0.4f;
        public const int DefaultMaxVoices = 12;
        /// <summary>Upper bound for maxVoices, so a typo can't allocate hundreds of sources.</summary>
        public const int AbsoluteMaxVoices = 32;

        /// <summary>Overall volume.</summary>
        public float master;
        /// <summary>Sound-effect channel volume.</summary>
        public float sfx;
        /// <summary>Music channel volume (no music yet).</summary>
        public float music;
        /// <summary>One-shot pitch is 1 +/- this.</summary>
        public float pitchJitter;
        /// <summary>One-shot volume is 1 - random(0..this).</summary>
        public float volumeJitter;
        /// <summary>Volume factor for positioned cues outside the camera view.</summary>
        public float offscreenVolume;
        /// <summary>Size of the gameplay one-shot voice pool.</summary>
        public int maxVoices;

        /// <summary>A sanitised copy of balance.audio: missing or non-positive values become the
        /// defaults, volumes and jitters are clamped to 0..1, maxVoices to 1..AbsoluteMaxVoices.
        /// Never null.</summary>
        public static AudioData Resolve(GameBalance balance) {
            var src = balance != null ? balance.audio : null;
            var r = new AudioData();
            r.master = Clamp01(Pick(src != null ? src.master : 0f, DefaultMaster));
            r.sfx = Clamp01(Pick(src != null ? src.sfx : 0f, DefaultSfx));
            r.music = Clamp01(Pick(src != null ? src.music : 0f, DefaultMusic));
            r.pitchJitter = Clamp01(Pick(src != null ? src.pitchJitter : 0f, DefaultPitchJitter));
            r.volumeJitter = Clamp01(Pick(src != null ? src.volumeJitter : 0f, DefaultVolumeJitter));
            r.offscreenVolume = Clamp01(Pick(src != null ? src.offscreenVolume : 0f, DefaultOffscreenVolume));
            int voices = src != null && src.maxVoices > 0 ? src.maxVoices : DefaultMaxVoices;
            r.maxVoices = Mathf.Clamp(voices, 1, AbsoluteMaxVoices);
            return r;
        }

        private static float Pick(float value, float fallback) {
            return value > 0f ? value : fallback;
        }

        private static float Clamp01(float value) {
            return Mathf.Clamp01(value);
        }
    }

    /// <summary>
    /// Serializable data class matching the "pressure" section of Balance.json (B035, GDD
    /// "Enemy pressure"): when no enemy aircraft has been on screen for maxQuietSeconds,
    /// EnemyPressure sends fillerCount reinforcement fighters, never letting the live fighters
    /// near the camera exceed maxLiveFighters. Read it through Resolve: a missing block, or a
    /// number that is missing or not positive, falls back to the Default* value. `enabled`
    /// defaults to true; false turns the safety net off.
    /// </summary>
    [System.Serializable]
    public class PressureData {
        public const bool DefaultEnabled = true;
        public const float DefaultMaxQuietSeconds = 2f;
        public const int DefaultFillerCount = 2;
        public const int DefaultMaxLiveFighters = 6;

        /// <summary>False turns the reinforcement waves off. Defaults to true when the key is missing.</summary>
        public bool enabled = DefaultEnabled;
        /// <summary>Longest stretch of play (seconds) without an enemy aircraft on screen.</summary>
        public float maxQuietSeconds;
        /// <summary>Fighters per reinforcement wave.</summary>
        public int fillerCount;
        /// <summary>Cap on live fighters near the camera; at the cap a wave waits.</summary>
        public int maxLiveFighters;

        /// <summary>A sanitised copy of balance.pressure (defaults for missing or non-positive
        /// numbers; a missing block is enabled with every default). Never null.</summary>
        public static PressureData Resolve(GameBalance balance) {
            var src = balance != null ? balance.pressure : null;
            var r = new PressureData();
            r.enabled = src != null ? src.enabled : DefaultEnabled;
            r.maxQuietSeconds = src != null && src.maxQuietSeconds > 0f ? src.maxQuietSeconds : DefaultMaxQuietSeconds;
            r.fillerCount = src != null && src.fillerCount > 0 ? src.fillerCount : DefaultFillerCount;
            r.maxLiveFighters = src != null && src.maxLiveFighters > 0 ? src.maxLiveFighters : DefaultMaxLiveFighters;
            return r;
        }
    }

    /// <summary>
    /// Serializable data class matching the "bomb" section of Balance.json (B036, ARCHITECTURE
    /// §3 "Bomb flight"): the player's bomb starts with inheritFactor x the plane's velocity and
    /// falls under `gravity` (units/s^2) with horizontal `drag` (fraction of x speed lost per
    /// second), its sprite turned along its path when alignToVelocity is true. Read it through
    /// Resolve: a missing block or an invalid number falls back to the Default* value.
    /// </summary>
    [System.Serializable]
    public class BombData {
        public const float DefaultGravity = 10f;
        public const float DefaultDrag = 0.15f;
        public const float DefaultInheritFactor = 1f;
        public const bool DefaultAlignToVelocity = true;

        /// <summary>Downward acceleration in world units per second squared (must be &gt; 0).</summary>
        public float gravity = DefaultGravity;
        /// <summary>Fraction of horizontal speed lost per second (0 = none; negative is invalid).</summary>
        public float drag = DefaultDrag;
        /// <summary>Share of the plane's velocity the bomb starts with (0 = drop dead, 1 = full).</summary>
        public float inheritFactor = DefaultInheritFactor;
        /// <summary>True turns the bomb sprite's nose along its velocity every physics step.</summary>
        public bool alignToVelocity = DefaultAlignToVelocity;

        /// <summary>A sanitised copy of balance.bomb (defaults for a missing block, a gravity
        /// that is not positive, or a negative drag / inheritFactor). Never null.</summary>
        public static BombData Resolve(GameBalance balance) {
            var src = balance != null ? balance.bomb : null;
            var r = new BombData();
            if (src == null) return r;
            r.gravity = src.gravity > 0f ? src.gravity : DefaultGravity;
            r.drag = src.drag >= 0f ? src.drag : DefaultDrag;
            r.inheritFactor = src.inheritFactor >= 0f ? src.inheritFactor : DefaultInheritFactor;
            r.alignToVelocity = src.alignToVelocity;
            return r;
        }
    }

    /// <summary>
    /// Serializable data class matching the "fighterChase" section of Balance.json (B059,
    /// ARCHITECTURE §3 "Fighter chase"): how every FighterAI faces and pursues the player.
    /// Read it through Resolve: a missing block or a non-positive number falls back to the
    /// Default* value.
    /// </summary>
    [System.Serializable]
    public class FighterChaseData {
        public const float DefaultTurnSeconds = 0.6f;
        public const float DefaultTurnDeadzone = 1f;
        public const float DefaultAccel = 4f;
        public const float DefaultStandoff = 3.5f;
        public const float DefaultClimbRate = 2.5f;
        public const float DefaultSeparation = 1.2f;
        public const float DefaultEscortLeash = 8f;
        public const int DefaultMaxAttackers = 3;
        public const float DefaultStandoffMax = 5f; // B063: matches Balance (B061)
        public const float DefaultSlotGraceSeconds = 0.5f;

        /// <summary>Seconds for a full eased turn-around (facing -1 to +1).</summary>
        public float turnSeconds;
        /// <summary>The player must be more than this far (x) behind the nose before it turns.</summary>
        public float turnDeadzone;
        /// <summary>Horizontal acceleration (units/s^2) toward the desired chase velocity.</summary>
        public float accel;
        /// <summary>Horizontal distance to the player the fighter brakes to hold.</summary>
        public float standoff;
        /// <summary>Max climb/dive speed (units/s) while pursuing the player's height.</summary>
        public float climbRate;
        /// <summary>Fighters closer than this to each other push apart vertically.</summary>
        public float separation;
        /// <summary>An escort chases once the player is within this of its escorted plane, and
        /// returns to formation beyond twice this.</summary>
        public float escortLeash;
        /// <summary>Attack slots (B060): at most this many fighters may fire at once.</summary>
        public int maxAttackers;
        /// <summary>Each fighter picks its own standoff once, uniformly in [standoff,
        /// standoffMax] (B060), so a pack spreads out. Below standoff it is raised to standoff.</summary>
        public float standoffMax;
        /// <summary>Attack slots (B060): a holder that can't shoot (no aim, off screen, turning)
        /// for longer than this gives its slot up.</summary>
        public float slotGraceSeconds;

        /// <summary>A sanitised copy of balance.fighterChase (defaults for a missing block or
        /// a missing / non-positive number). Never null.</summary>
        public static FighterChaseData Resolve(GameBalance balance) {
            var src = balance != null ? balance.fighterChase : null;
            var r = new FighterChaseData();
            r.turnSeconds = Pick(src != null ? src.turnSeconds : 0f, DefaultTurnSeconds);
            r.turnDeadzone = Pick(src != null ? src.turnDeadzone : 0f, DefaultTurnDeadzone);
            r.accel = Pick(src != null ? src.accel : 0f, DefaultAccel);
            r.standoff = Pick(src != null ? src.standoff : 0f, DefaultStandoff);
            r.climbRate = Pick(src != null ? src.climbRate : 0f, DefaultClimbRate);
            r.separation = Pick(src != null ? src.separation : 0f, DefaultSeparation);
            r.escortLeash = Pick(src != null ? src.escortLeash : 0f, DefaultEscortLeash);
            r.maxAttackers = src != null && src.maxAttackers > 0 ? src.maxAttackers : DefaultMaxAttackers;
            r.standoffMax = Mathf.Max(r.standoff, Pick(src != null ? src.standoffMax : 0f, DefaultStandoffMax));
            r.slotGraceSeconds = Pick(src != null ? src.slotGraceSeconds : 0f, DefaultSlotGraceSeconds);
            return r;
        }

        private static float Pick(float value, float fallback) {
            return value > 0f ? value : fallback;
        }
    }

    /// <summary>
    /// Serializable data class matching the "bomberCrash" section of Balance.json (B068):
    /// how a shot-down bomber's wreck (BomberWreck) falls. Read it through Resolve: a missing
    /// block or a non-positive number falls back to the Default* value.
    /// </summary>
    [System.Serializable]
    public class BomberCrashData {
        public const float DefaultGravity = 5f;
        public const float DefaultDriftFactor = 0.8f;
        public const float DefaultDiveDegreesPerSecond = 30f;
        public const float DefaultMaxDiveDegrees = 60f;
        public const int DefaultGroundBurstCount = 3;
        public const float DefaultGroundBurstSpread = 0.9f;
        public const float DefaultMaxFallSeconds = 10f;

        /// <summary>Downward acceleration of the wreck in world units per second squared.</summary>
        public float gravity;
        /// <summary>Share of the bomber's forward speed the wreck keeps while it falls.</summary>
        public float driftFactor;
        /// <summary>How fast (degrees per second) the nose tips down while falling.</summary>
        public float diveDegreesPerSecond;
        /// <summary>Steepest nose-down pitch (degrees) the falling wreck reaches.</summary>
        public float maxDiveDegrees;
        /// <summary>Explosions in the ground-impact burst (the "bigger" second explosion).</summary>
        public int groundBurstCount;
        /// <summary>Half the x spread (world units) of the ground-impact burst along the wreck.</summary>
        public float groundBurstSpread;
        /// <summary>Safety net: a wreck still falling after this many seconds bursts and goes.</summary>
        public float maxFallSeconds;

        /// <summary>A sanitised copy of balance.bomberCrash. Never null.</summary>
        public static BomberCrashData Resolve(GameBalance balance) {
            var src = balance != null ? balance.bomberCrash : null;
            var r = new BomberCrashData();
            r.gravity = Pick(src != null ? src.gravity : 0f, DefaultGravity);
            r.driftFactor = Pick(src != null ? src.driftFactor : 0f, DefaultDriftFactor);
            r.diveDegreesPerSecond = Pick(src != null ? src.diveDegreesPerSecond : 0f, DefaultDiveDegreesPerSecond);
            r.maxDiveDegrees = Pick(src != null ? src.maxDiveDegrees : 0f, DefaultMaxDiveDegrees);
            r.groundBurstCount = src != null && src.groundBurstCount > 0 ? src.groundBurstCount : DefaultGroundBurstCount;
            r.groundBurstSpread = Pick(src != null ? src.groundBurstSpread : 0f, DefaultGroundBurstSpread);
            r.maxFallSeconds = Pick(src != null ? src.maxFallSeconds : 0f, DefaultMaxFallSeconds);
            return r;
        }

        private static float Pick(float value, float fallback) {
            return value > 0f ? value : fallback;
        }
    }

    /// <summary>
    /// Serializable data class matching the "zeppelinDefense" section of Balance.json (B080):
    /// the moment a zeppelin's last turret dies (its hull loses the shield), MissionRunner
    /// queues waveCount extra escort fighters for it, one every staggerSeconds while the live
    /// fighters near the view stay under pressure.maxLiveFighters. Read it through Resolve: a
    /// missing block or a non-positive number falls back to the Default* value; `enabled`
    /// defaults to true and false turns the wave off.
    /// </summary>
    [System.Serializable]
    public class ZeppelinDefenseData {
        public const bool DefaultEnabled = true;
        public const int DefaultWaveCount = 4;
        public const float DefaultStaggerSeconds = 0.6f;

        /// <summary>False turns the defense wave off. Defaults to true when the key is missing.</summary>
        public bool enabled = DefaultEnabled;
        /// <summary>Extra escort fighters per zeppelin, sent once when its shield drops.</summary>
        public int waveCount;
        /// <summary>Scaled seconds between two fighters of the wave.</summary>
        public float staggerSeconds;

        /// <summary>A sanitised copy of balance.zeppelinDefense. Never null.</summary>
        public static ZeppelinDefenseData Resolve(GameBalance balance) {
            var src = balance != null ? balance.zeppelinDefense : null;
            var r = new ZeppelinDefenseData();
            r.enabled = src != null ? src.enabled : DefaultEnabled;
            r.waveCount = src != null && src.waveCount > 0 ? src.waveCount : DefaultWaveCount;
            r.staggerSeconds = src != null && src.staggerSeconds > 0f ? src.staggerSeconds : DefaultStaggerSeconds;
            return r;
        }
    }

    /// <summary>
    /// Serializable data class matching the "zeppelinExplosion" section of Balance.json (B080):
    /// the zeppelin hull's death sequence (ZeppelinAI.DeathBurstPlan). burstCount explosions
    /// walk out from the envelope centre to `spread` (local half span, world units) on each
    /// side, tailBurstCount more hit the tail and gondolas, all staggered over `duration`
    /// scaled seconds while the hull drifts on, sinks at sinkSpeed, tips its nose down to
    /// tiltDegrees and fades out over the last fadeSeconds; then it is removed. Read it through
    /// Resolve: a missing block or a non-positive number falls back to the Default* value
    /// (tailBurstCount may be 0).
    /// </summary>
    [System.Serializable]
    public class ZeppelinExplosionData {
        public const int DefaultBurstCount = 12;
        public const int DefaultTailBurstCount = 3;
        public const float DefaultSpread = 3.4f;
        public const float DefaultDuration = 1.8f;
        public const float DefaultFadeSeconds = 0.4f;
        public const float DefaultSinkSpeed = 0.6f;
        public const float DefaultTiltDegrees = 6f;
        /// <summary>Tail/gondola bursts are capped at the number of tail/gondola points.</summary>
        public const int MaxTailBurstCount = 4;

        /// <summary>Explosions spread along the envelope.</summary>
        public int burstCount;
        /// <summary>Explosions on the tail and gondolas (0..MaxTailBurstCount).</summary>
        public int tailBurstCount;
        /// <summary>Half the x span (world units) the envelope bursts cover around its centre.</summary>
        public float spread;
        /// <summary>Scaled seconds the dying hull stays on screen; the bursts span it.</summary>
        public float duration;
        /// <summary>The hull sprite fades out over this many final seconds of `duration`.</summary>
        public float fadeSeconds;
        /// <summary>Sink speed (world units per second) the hull reaches by the end.</summary>
        public float sinkSpeed;
        /// <summary>Nose-down tilt (degrees) the hull reaches by the end.</summary>
        public float tiltDegrees;

        /// <summary>A sanitised copy of balance.zeppelinExplosion. Never null.</summary>
        public static ZeppelinExplosionData Resolve(GameBalance balance) {
            var src = balance != null ? balance.zeppelinExplosion : null;
            var r = new ZeppelinExplosionData();
            r.burstCount = src != null && src.burstCount > 0 ? src.burstCount : DefaultBurstCount;
            r.tailBurstCount = src == null ? DefaultTailBurstCount
                : Mathf.Clamp(src.tailBurstCount, 0, MaxTailBurstCount);
            r.spread = Pick(src != null ? src.spread : 0f, DefaultSpread);
            r.duration = Pick(src != null ? src.duration : 0f, DefaultDuration);
            r.fadeSeconds = Mathf.Min(r.duration, Pick(src != null ? src.fadeSeconds : 0f, DefaultFadeSeconds));
            r.sinkSpeed = Pick(src != null ? src.sinkSpeed : 0f, DefaultSinkSpeed);
            r.tiltDegrees = Pick(src != null ? src.tiltDegrees : 0f, DefaultTiltDegrees);
            return r;
        }

        private static float Pick(float value, float fallback) {
            return value > 0f ? value : fallback;
        }
    }

    /// <summary>
    /// Serializable data class matching the "ramDamage" section of Balance.json (B069): hit
    /// points a mid-air ram by the player takes off an enemy that survives rams. Only the
    /// bomber does; fighters still die at once (DamageSource.Collision).
    /// </summary>
    [System.Serializable]
    public class RamDamageData {
        public const float DefaultBomber = 250f;

        /// <summary>Hit points one ram takes off a bomber (of enemyHealth.bomber).</summary>
        public float bomber;

        /// <summary>ramDamage.bomber, or DefaultBomber when it is missing or not positive.
        /// `data` may be null. Pure.</summary>
        public static float BomberOrDefault(RamDamageData data) {
            return data != null && data.bomber > 0f ? data.bomber : DefaultBomber;
        }
    }

    /// <summary>
    /// Recharge bomb balloon tunables (B064), read from the top-level Balance.json keys
    /// bombBalloonGrant, bombBalloonDelay and bombBalloonLead. Read it through Resolve: a
    /// missing or non-positive value falls back to the Default* const.
    /// </summary>
    public class BombBalloonData {
        public const int DefaultGrant = 4;
        public const float DefaultDelay = 4f;
        public const float DefaultLead = 12f;

        /// <summary>Bombs one balloon adds to the bay (clamped to capacity by BombBay.AddBombs).</summary>
        public int grant;
        /// <summary>Scaled seconds after the bay runs empty before a recharge balloon spawns.</summary>
        public float delay;
        /// <summary>World units ahead of the player where the recharge balloon appears.</summary>
        public float lead;

        /// <summary>A sanitised copy of the bomb balloon keys. Never null.</summary>
        public static BombBalloonData Resolve(GameBalance balance) {
            var r = new BombBalloonData();
            r.grant = balance != null && balance.bombBalloonGrant > 0 ? balance.bombBalloonGrant : DefaultGrant;
            r.delay = balance != null && balance.bombBalloonDelay > 0f ? balance.bombBalloonDelay : DefaultDelay;
            r.lead = balance != null && balance.bombBalloonLead > 0f ? balance.bombBalloonLead : DefaultLead;
            return r;
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

        /// <summary>Mirrors "bombBalloonGrant" (B064). Read through BombBalloonData.Resolve.</summary>
        public int bombBalloonGrant;

        /// <summary>Mirrors "bombBalloonDelay" in seconds (B064). Read through BombBalloonData.Resolve.</summary>
        public float bombBalloonDelay;

        /// <summary>Mirrors "bombBalloonLead" in world units (B064). Read through BombBalloonData.Resolve.</summary>
        public float bombBalloonLead;

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

        /// <summary>Mirrors the "audio" key in Balance.json (T949). Read it through
        /// AudioData.Resolve(GameBalance.Get()), which fills in defaults.</summary>
        public AudioData audio;

        /// <summary>Mirrors the "pressure" key in Balance.json (B035). Read it through
        /// PressureData.Resolve(GameBalance.Get()), which fills in defaults.</summary>
        public PressureData pressure;

        /// <summary>Mirrors the "bomb" key in Balance.json (B036). Read it through
        /// BombData.Resolve(GameBalance.Get()), which fills in defaults.</summary>
        public BombData bomb;

        /// <summary>Mirrors the "fighterChase" key in Balance.json (B059). Read it through
        /// FighterChaseData.Resolve(GameBalance.Get()), which fills in defaults.</summary>
        public FighterChaseData fighterChase;

        /// <summary>Mirrors the "bomberCrash" key in Balance.json (B068). Read it through
        /// BomberCrashData.Resolve(GameBalance.Get()), which fills in defaults.</summary>
        public BomberCrashData bomberCrash;

        /// <summary>Mirrors "killWinDelaySeconds" (B069): real seconds between a win decided
        /// by a kill and the result screen. Read through GameManager.KillWinDelayOrDefault.</summary>
        public float killWinDelaySeconds;

        /// <summary>Mirrors the "ramDamage" key in Balance.json (B069). Read it through
        /// RamDamageData.BomberOrDefault.</summary>
        public RamDamageData ramDamage;

        /// <summary>Mirrors the "zeppelinDefense" key in Balance.json (B080). Read it through
        /// ZeppelinDefenseData.Resolve(GameBalance.Get()), which fills in defaults.</summary>
        public ZeppelinDefenseData zeppelinDefense;

        /// <summary>Mirrors the "zeppelinExplosion" key in Balance.json (B080). Read it through
        /// ZeppelinExplosionData.Resolve(GameBalance.Get()), which fills in defaults.</summary>
        public ZeppelinExplosionData zeppelinExplosion;

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
