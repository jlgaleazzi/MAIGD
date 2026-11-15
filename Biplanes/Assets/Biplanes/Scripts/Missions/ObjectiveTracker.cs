using System;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Missions;

namespace Biplanes.Missions {

    /// <summary>
    /// Evaluates mission objectives against incoming game events.
    /// Hosted by MissionRunner.  Does NOT create or destroy game objects —
    /// pure logic driven by MissionRunner (EntityDestroyed, id-tracked targets, bomb-target
    /// kills and camera progress).
    ///
    /// Objective types (ARCHITECTURE §5):
    ///   - destroyAllOfType: completes when the Team.Enemy kills of targetType reach the number
    ///     of that type the mission JSON spawns (sum of `count` over matching spawn entries).
    ///   - destroyById: completes when MissionRunner reports the spawn with id == targetId
    ///     destroyed (OnTargetDestroyed).
    ///   - bombTarget: completes when MissionRunner reports a bomb kill of targetId, or of
    ///     targetType when targetId is empty (OnBombTargetDestroyed).
    ///   - surviveToEnd: completes when the camera reaches the mission length.
    /// A destroyById objective with failAtX ≥ 0 fails the mission when its target entity's
    /// world x reaches failAtX (CheckTargetReached, polled by MissionRunner; B018). The camera
    /// reaching failAtX means nothing; the map end (CheckMapEnd) catches a target that never
    /// spawned or despawned.
    /// </summary>
    public class ObjectiveTracker : MonoBehaviour {

        private ObjectiveEntry[] _objectives;
        private int _completedCount;

        /// <summary>destroyAllOfType: kills counted so far, per objective index.</summary>
        private int[] _kills;

        /// <summary>destroyAllOfType: kills needed, per objective index (from the spawn list).</summary>
        private int[] _required;

        // ── Lifecycle ────────────────────────────────────────────────

        /// <summary>
        /// Resets the tracker for a mission. `spawns` gives destroyAllOfType its target count;
        /// without it a destroyAllOfType objective can't complete.
        /// </summary>
        public void Init(ObjectiveEntry[] objectives, float cameraX, SpawnEntry[] spawns = null) {
            _objectives = objectives ?? new ObjectiveEntry[0];
            _completedCount = 0;
            _kills = new int[_objectives.Length];
            _required = new int[_objectives.Length];

            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (obj == null) continue;
                obj.evaluated = false;
                if (obj.type == "destroyAllOfType") {
                    _required[i] = CountOfType(spawns, obj.targetType);
                    if (_required[i] <= 0) {
                        Debug.LogWarning($"ObjectiveTracker: objective '{obj.id}' needs every '{obj.targetType}' " +
                                         "destroyed, but the mission spawns none — it can't complete.");
                    }
                }
            }
            Debug.Log($"ObjectiveTracker: initialized with {_objectives.Length} objectives");
        }

        // ── Objective evaluation ─────────────────────────────────────

        /// <summary>A kill reported through GameEvents.EntityDestroyed (type key, team).</summary>
        public void OnEntityDestroyed(string entityType, Team team) {
            if (_objectives == null || team != Team.Enemy) return;
            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (obj == null || obj.evaluated || obj.type != "destroyAllOfType") continue;
                if (!SameType(obj.targetType, entityType)) continue;

                _kills[i]++;
                Debug.Log($"ObjectiveTracker: destroyed {entityType} {_kills[i]}/{_required[i]} for objective {obj.id}");
                if (_required[i] > 0 && _kills[i] >= _required[i]) Complete(i);
            }
        }

        /// <summary>
        /// The spawn with this id was destroyed (MissionRunner checks the tracked instance is
        /// dead). Completes every unevaluated destroyById objective with a matching targetId.
        /// Returns true if any objective completed.
        /// </summary>
        public bool OnTargetDestroyed(string targetId) {
            if (_objectives == null || string.IsNullOrEmpty(targetId)) return false;
            bool any = false;
            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (obj == null || obj.evaluated || obj.type != "destroyById") continue;
                if (obj.targetId != targetId) continue;
                Complete(i);
                any = true;
            }
            return any;
        }

        /// <summary>
        /// A bomb destroyed the target with this spawn id and type key. Completes every
        /// unevaluated bombTarget objective whose targetId matches, or whose targetType matches
        /// when its targetId is empty. Returns true if any objective completed.
        /// </summary>
        public bool OnBombTargetDestroyed(string targetId, string entityType) {
            if (_objectives == null) return false;
            bool any = false;
            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (obj == null || obj.evaluated || obj.type != "bombTarget") continue;
                bool match = string.IsNullOrEmpty(obj.targetId)
                    ? SameType(obj.targetType, entityType)
                    : obj.targetId == targetId;
                if (!match) continue;
                Complete(i);
                any = true;
            }
            return any;
        }

        /// <summary>Legacy entry point: a bomb kill matched by targetType only.</summary>
        public void OnBombApplied(string entityType) {
            if (_objectives == null) return;
            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (obj != null && !obj.evaluated && obj.type == "bombTarget" && SameType(obj.targetType, entityType)) {
                    Complete(i);
                }
            }
        }

        public void OnCameraProgress(float cameraX, float length) {
            if (_objectives == null) return;
            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (obj != null && !obj.evaluated && obj.type == "surviveToEnd" && cameraX >= length) {
                    Complete(i);
                }
            }
        }

        // ── Fail check (target entity reached failAtX) ───────────────

        /// <summary>
        /// failAtX of the first open destroyById objective that targets `targetId` and has
        /// failAtX ≥ 0: the WORLD x that entity must not reach (it flies left toward it).
        /// Returns -1 when no open objective gives that id a failAtX. MissionRunner hands it to
        /// the spawned BomberAI / ZeppelinAI as their targetX (B018).
        /// </summary>
        public float GetFailAtXFor(string targetId) {
            if (_objectives == null || string.IsNullOrEmpty(targetId)) return -1f;
            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (!IsOpenFailAtX(obj, targetId)) continue;
                return obj.failAtX;
            }
            return -1f;
        }

        /// <summary>
        /// Entity-based failAtX (B018, ARCHITECTURE §5): the live entity with spawn id
        /// `targetId` is at world x `entityX`. Returns the id of the first open destroyById
        /// objective targeting it whose failAtX ≥ 0 the entity has reached (entityX ≤ failAtX,
        /// flying left), or null. The camera position plays no part, so the player can't dodge
        /// the loss by turning back; a destroyed target's objective is evaluated and never fails.
        /// </summary>
        public string CheckTargetReached(string targetId, float entityX) {
            if (_objectives == null || string.IsNullOrEmpty(targetId)) return null;
            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (!IsOpenFailAtX(obj, targetId)) continue;
                if (entityX <= obj.failAtX) return string.IsNullOrEmpty(obj.id) ? obj.type : obj.id;
            }
            return null;
        }

        /// <summary>An unevaluated destroyById objective on `targetId` with failAtX ≥ 0.</summary>
        private static bool IsOpenFailAtX(ObjectiveEntry obj, string targetId) {
            return obj != null && !obj.evaluated && obj.type == "destroyById"
                && obj.failAtX >= 0f && obj.targetId == targetId;
        }

        /// <summary>
        /// Map end (GDD change log 2026-11-13): once the camera x reaches the mission length,
        /// returns the id of the first objective that is still open and is not surviveToEnd
        /// (surviveToEnd completes there instead, through OnCameraProgress). Returns null when
        /// the camera hasn't reached the end, the length is not positive, or nothing is open.
        /// </summary>
        public string CheckMapEnd(float cameraX, float length) {
            if (_objectives == null || length <= 0f || cameraX < length) return null;
            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (obj == null || obj.evaluated || obj.type == "surviveToEnd") continue;
                return string.IsNullOrEmpty(obj.id) ? obj.type : obj.id;
            }
            return null;
        }

        /// <summary>HUD/result text for the objective with this id (or type, for id-less
        /// entries); the id itself when no objective matches.</summary>
        public string GetDescriptionFor(string objectiveId) {
            if (_objectives == null) return objectiveId ?? "";
            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (obj == null) continue;
                string key = string.IsNullOrEmpty(obj.id) ? obj.type : obj.id;
                if (key == objectiveId) return GetDescription(i);
            }
            return objectiveId ?? "";
        }

        public bool AllComplete => _completedCount >= (_objectives?.Length ?? 0);

        // ── Internal helpers ─────────────────────────────────────────

        /// <summary>Marks objective i complete once and raises ObjectiveCompleted.</summary>
        private void Complete(int i) {
            var obj = _objectives[i];
            if (obj.evaluated) return;
            obj.MarkComplete();
            _completedCount++;
            Debug.Log($"ObjectiveTracker: objective {obj.id} ({obj.type}) complete");
            GameEvents.RaiseObjectiveCompleted(obj.id);
        }

        private static bool SameType(string a, string b) {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
                && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static int CountOfType(SpawnEntry[] spawns, string targetType) {
            if (spawns == null || string.IsNullOrEmpty(targetType)) return 0;
            int count = 0;
            foreach (var s in spawns) {
                if (s != null && SameType(s.type, targetType)) count += Mathf.Max(0, s.count);
            }
            return count;
        }

        // ── HUD description helper ───────────────────────────────────

        public string GetDescription(int index) {
            if (index < 0 || index >= (_objectives?.Length ?? 0)) return "";
            var obj = _objectives[index];
            // Player-facing text (B020): type names, never raw spawn ids.
            switch (obj.type) {
                case "destroyAllOfType":
                    return $"Destroy all {ReadableType(obj.targetType, true)}";
                case "destroyById":
                    return string.IsNullOrEmpty(obj.targetType)
                        ? "Destroy the target"
                        : $"Destroy the enemy {ReadableType(obj.targetType, false)}";
                case "bombTarget":
                    return string.IsNullOrEmpty(obj.targetType)
                        ? "Bomb the target"
                        : $"Bomb the enemy {ReadableType(obj.targetType, false)}";
                case "surviveToEnd":
                    return "Survive to the end";
                default:
                    return obj.id;
            }
        }

        /// <summary>Display name of a mission spawn type key (ARCHITECTURE §5 types), singular
        /// or plural. Unknown keys are returned as-is (plural adds "s").</summary>
        public static string ReadableType(string typeKey, bool plural) {
            if (string.IsNullOrEmpty(typeKey)) return plural ? "targets" : "target";
            switch (typeKey.ToLowerInvariant()) {
                case "fighter":        return plural ? "fighters" : "fighter";
                case "bomber":         return plural ? "bombers" : "bomber";
                case "aaa":            return plural ? "AAA guns" : "AAA gun";
                case "zeppelin":       return plural ? "zeppelins" : "zeppelin";
                case "barrageballoon": return plural ? "barrage balloons" : "barrage balloon";
                case "healthballoon":  return plural ? "health balloons" : "health balloon";
                case "enemybase":      return plural ? "enemy bases" : "base";
                case "building":       return plural ? "buildings" : "building";
                case "tree":           return plural ? "trees" : "tree";
                case "airfield":       return plural ? "airfields" : "airfield";
                case "playerhq":       return plural ? "headquarters" : "headquarters";
                default:               return plural ? typeKey + "s" : typeKey;
            }
        }
    }
}
