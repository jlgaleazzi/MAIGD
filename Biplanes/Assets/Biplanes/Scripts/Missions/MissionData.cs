using System;

namespace Biplanes.Missions {

    /// <summary>
    /// Position where the player begins a mission. Uses a class because JsonUtility
    /// cannot reliably deserialize structs — all nested objects must be classes.
    /// </summary>
    [Serializable]
    public class PlayerStartData {
        public float x;
        public float y;
        public bool onRunway;
    }

    // ---- terrain helpers ----

    /// <summary>
    /// A single terrain height point (x, world-up-height) used by TerrainStrip to
    /// interpolate the ground profile.
    /// </summary>
    [Serializable]
    public class TerrainPoint {
        /// <summary>X position in world units.</summary>
        public int atX;

        /// <summary>Height in world units above sea level (y = -4.5 is ground baseline).</summary>
        public float height;
    }

    // ---- spawn helpers ----

    /// <summary>
    /// Describes one group of entities to spawn during a mission, loaded from the
    /// "spawns" array in mission JSON.
    /// </summary>
    [Serializable]
    public class SpawnEntry {
        /// <summary>X position where this group is spawned (camera x ≥ atX).</summary>
        public int atX;

        /// <summary>Entity type: fighter, bomber, aaa, zeppelin, barrageBalloon,
        /// healthBalloon, building, tree, enemyBase, airfield, playerHQ.</summary>
        public string type;

        /// <summary>Base height above local terrain (world y = terrainHeight + y).</summary>
        public float y;

        /// <summary>Number of entities to spawn in this group.</summary>
        public int count;

        /// <summary>Horizontal spacing between each entity in the group.</summary>
        public float spacing;

        /// <summary>Movement pattern: static, straight, sine, dive, escort, drift.</summary>
        public string pattern;

        /// <summary>Unique id for this spawn group (used by escortOf / targetId).</summary>
        public string id;

        /// <summary>If non-empty, this entity follows the entity whose id matches.</summary>
        public string escortOf;

        /// <summary>Optional: X position the entity moves toward (bomber target, zeppelin goal).
        /// Default -1 means no targetX is set for this spawn.</summary>
        public float targetX = -1f;
    }

    // ---- objective helpers ----

    /// <summary>
    /// Describes one mission objective, loaded from the "objectives" array.
    /// ObjectiveTracker evaluates these at runtime.
    /// </summary>
    [Serializable]
    public class ObjectiveEntry {
        /// <summary>Unique identifier for this objective.</summary>
        public string id;

        /// <summary>Objective type: destroyAllOfType, destroyById, bombTarget, surviveToEnd.</summary>
        public string type;

        /// <summary>For destroyAllOfType / destroyById / bombTarget: entity type to target.</summary>
        public string targetType;

        /// <summary>For destroyById / bombTarget: the unique spawn id of the target entity.</summary>
        public string targetId;

        /// <summary>If ≥ 0 and the camera reaches this X before the objective completes,
        /// the mission is lost (used for bomber/zeppelin arrival checks).</summary>
        public float failAtX = -1f;

        /// <summary>Internal tracking — set true when objective is completed. Not serialised.</summary>
        [System.NonSerialized]
        public bool evaluated;

        /// <summary>Mark this objective as completed.</summary>
        public void MarkComplete() => evaluated = true;
    }

    // ---- root mission data ----

    /// <summary>
    /// Root serialisable class matching a mission JSON file (mission_01.json … mission_05.json).
    /// Loaded at the start of each mission via JsonUtility.FromJson&lt;MissionData&gt;.
    ///
    /// See ARCHITECTURE.md §5 (lines 137-171) for the JSON schema this class mirrors.
    /// </summary>
    [Serializable]
    public class MissionData {
        /// <summary>Mission number (1-5).</summary>
        public int id;

        /// <summary>Brief title displayed in UI.</summary>
        public string title;

        /// <summary>Breifing paragraph shown in the briefing screen.</summary>
        public string briefing;

        /// <summary>Scroll speed of the mission distance camera (world units / second).</summary>
        public float scrollSpeed;

        /// <summary>Total mission length in world units. SurviveToEnd objective triggers here.</summary>
        public float length;

        /// <summary>Where the player starts the mission.</summary>
        public PlayerStartData playerStart;

        /// <summary>Background sprite category (e.g. "farmland", "mountains").</summary>
        public string background;

        /// <summary>Terrain height keyframes used by TerrainStrip for ground interpolation.</summary>
        public TerrainPoint[] terrain;

        /// <summary>Spawn groups to create as the camera advances through the mission.</summary>
        public SpawnEntry[] spawns;

        /// <summary>Objectives that must be fulfilled (or avoided) during this mission.</summary>
        public ObjectiveEntry[] objectives;
    }
}
