using System;
using UnityEngine;

namespace Biplanes.Core {

    /// <summary>
    /// Static event bus for decoupled communication between gameplay systems.
    /// UI, mission logic and FX subscribe to events rather than holding
    /// direct references (ARCHITECTURE §3).
    /// </summary>
    public static class GameEvents {
        public static event Action<int> ScoreChanged;
        public static event Action<float> PlayerDamageChanged;
        public static event Action<int> BombsChanged;
        public static event Action<string, Team> EntityDestroyed;
        public static event Action<string> ObjectiveCompleted;
        public static event Action<bool, string> MissionEnded;

        public static void RaiseScoreChanged(int score) { var h = ScoreChanged; if (h != null) h(score); }
        public static void RaisePlayerDamageChanged(float damage) { var h = PlayerDamageChanged; if (h != null) h(damage); }
        public static void RaiseBombsChanged(int bombs) { var h = BombsChanged; if (h != null) h(bombs); }
        public static void RaiseEntityDestroyed(string type, Team team) { var h = EntityDestroyed; if (h != null) h(type, team); }
        public static void RaiseObjectiveCompleted(string id) { var h = ObjectiveCompleted; if (h != null) h(id); }
        public static void RaiseMissionEnded(bool won, string reason) { var h = MissionEnded; if (h != null) h(won, reason); }
    }
}
