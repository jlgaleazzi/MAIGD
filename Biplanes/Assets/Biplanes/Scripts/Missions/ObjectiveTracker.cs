using System;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Missions;

namespace Biplanes.Missions {

    /// <summary>
    /// Evaluates mission objectives against incoming game events.
    /// Hosted by MissionRunner.  Does NOT create or destroy game objects —
    /// pure logic driven by GameEvents.EntityDestroyed and camera position checks.
    /// </summary>
    public class ObjectiveTracker : MonoBehaviour {

        private ObjectiveEntry[] _objectives;
        private int _completedCount;

        // ── Lifecycle ────────────────────────────────────────────────

        public void Init(ObjectiveEntry[] objectives, float cameraX) {
            _objectives = objectives;
            _completedCount = 0;
            Debug.Log($"ObjectiveTracker: initialized with {_objectives.Length} objectives");
        }

        // ── Objective evaluation ─────────────────────────────────────

        public void OnEntityDestroyed(string entityType, Team team) {
            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (obj.evaluated) continue;

                switch (obj.type) {
                    case "destroyAllOfType":
                        if (obj.targetType == entityType && team == Team.Enemy) {
                            // Track remaining count (simplified — real impl tracks per-type pool)
                            Debug.Log($"ObjectiveTracker: destroyed {entityType} [{team}] for objective {obj.id}");
                        }
                        break;

                    case "destroyById":
                        if (!string.IsNullOrEmpty(obj.targetId)) {
                            // Check by target ID from spawn data
                            Debug.Log($"ObjectiveTracker: entity {entityType} with id check for {obj.targetId}");
                        }
                        break;

                    case "bombTarget":
                        // BombTarget handled separately via ApplyDamage source check
                        break;
                }
            }
        }

        public void OnBombApplied(string entityType) {
            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (!obj.evaluated && obj.type == "bombTarget" && obj.targetType == entityType) {
                    Debug.Log($"ObjectiveTracker: bomb hit on {entityType} — objective {obj.id} complete");
                    GameEvents.RaiseObjectiveCompleted(obj.id);
                    _completedCount++;
                }
            }
        }

        public void OnCameraProgress(float cameraX, float length) {
            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (!obj.evaluated && obj.type == "surviveToEnd" && cameraX >= length) {
                    Debug.Log($"ObjectiveTracker: survived to end — objective {obj.id} complete");
                    GameEvents.RaiseObjectiveCompleted(obj.id);
                    _completedCount++;
                }
            }
        }

        // ── Fail check (target reached failAtX) ──────────────────────

        public void CheckFailAtX(string entityType, float targetX) {
            if (targetX < 0f) return;
            for (int i = 0; i < _objectives.Length; i++) {
                var obj = _objectives[i];
                if (obj.failAtX >= 0f && string.IsNullOrEmpty(obj.targetId)) {
                    // Generic failAtX check — specific entity type handled by caller
                } else if (!string.IsNullOrEmpty(obj.targetId) && obj.failAtX >= 0f) {
                    Debug.Log($"ObjectiveTracker: checking failAtX={obj.failAtX} for target {obj.targetId}");
                }
            }
        }

        public bool AllComplete => _completedCount == (_objectives?.Length ?? 0);

        // ── HUD description helper ───────────────────────────────────

        public string GetDescription(int index) {
            if (index < 0 || index >= (_objectives?.Length ?? 0)) return "";
            var obj = _objectives[index];
            switch (obj.type) {
                case "destroyAllOfType":
                    return $"Destroy all {obj.targetType}";
                case "destroyById":
                    return $"Destroy {obj.targetId}";
                case "bombTarget":
                    return $"Bomb target {obj.targetId}";
                case "surviveToEnd":
                    return "Survive to the end";
                default:
                    return obj.id;
            }
        }
    }
}
