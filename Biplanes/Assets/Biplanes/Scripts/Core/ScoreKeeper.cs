using UnityEngine;

namespace Biplanes.Core {

    /// <summary>
    /// Singleton score manager that listens to GameEvents.EntityDestroyed and awards points
    /// from the balance table (GameBalance.scoreData). It also raises ScoreChanged so UI can
    /// update, and persists best scores per mission via SaveSystem.
    ///
    /// ARCHITECTURE.md §2 line 84 — core runtime class.
    /// </summary>
    public class ScoreKeeper : MonoBehaviour {

        #region Singleton

        private static ScoreKeeper _instance;

        public static ScoreKeeper Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<ScoreKeeper>();
                }
                return _instance;
            }
        }

        private void Awake() {
            if (_instance != null && _instance != this) {
                Debug.LogWarning("ScoreKeeper: duplicate instance destroyed.");
                Destroy(gameObject);
                return;
            }
            _instance = this;

            // Subscriptions happen once in awake so every system can subscribe before missions start.
            GameEvents.EntityDestroyed += OnEntityDestroyed;
        }

        private void OnDestroy() {
            if (_instance == this) _instance = null;
            GameEvents.EntityDestroyed -= OnEntityDestroyed;
        }

        #endregion

        #region Public API

        /// <summary>Current total score.</summary>
        public int CurrentScore { get; private set; }

        /// <summary>Add *points* directly to the score (for objective bonuses, etc.).</summary>
        public void AddPoints(int points) {
            if (points <= 0) return;
            CurrentScore += points;
            GameEvents.RaiseScoreChanged(CurrentScore);
        }

        #endregion

        #region EntityDestroyed handler

        /// <summary>
        /// Called by GameEvents.EntityDestroyed. When the destroyed entity belonged to Team.Player
        /// (the player plane) we do nothing — the player doesn't earn points for dying.
        /// For all other teams, look up the score table and add the corresponding points.
        /// </summary>
        public void OnEntityDestroyed(string entityType, Team team) {
            // Ignore player deaths: no points awarded to oneself.
            if (team == Team.Player) return;

            int points = GetScoreFor(entityType);
            if (points > 0) {
                CurrentScore += points;
                GameEvents.RaiseScoreChanged(CurrentScore);
            }
        }

        #endregion

        #region Mission-end scoring

        /// <summary>
        /// Call once per mission end (win or lose). Persists the current score as the best
        /// score for that mission if it exceeds the previous record, and unlocks the next
        /// mission when the player wins.
        /// </summary>
        /// <param name="missionIndex">Mission number 1-5.</param>
        /// <param name="won">True when the player completed all objectives.</param>
        public void OnMissionEnded(int missionIndex, bool won) {
            SaveSystem.SaveBestScore(missionIndex, CurrentScore);

            if (won) {
                // Unlock the next mission if this isn't already the last.
                int next = Mathf.Min(missionIndex + 1, 5);
                SaveSystem.SaveUnlockedMission(next);
            }
        }

        #endregion

        #region Internal helpers

        /// <summary>Look up the kill score for *entityType* in the balance table.</summary>
        private int GetScoreFor(string entityType) {
            GameBalance balance = GameBalance.Get();
            if (balance == null || balance.score == null) {
                Debug.LogWarning("ScoreKeeper: no balance data — cannot look up score for '" + entityType + "'.");
                return 0;
            }

            switch (entityType) {
                case "fighter":        return balance.score.fighter;
                case "barrageBalloon": return balance.score.barrageBalloon;
                case "aaa":            return balance.score.aaa;
                case "zeppelinTurret": return balance.score.zeppelinTurret;
                case "bomber":         return balance.score.bomber;
                case "enemyBase":      return balance.score.enemyBase;
                case "zeppelin":       return balance.score.zeppelin;
                default:
                    Debug.LogWarning("ScoreKeeper: unknown entityType '" + entityType + "' — no score lookup.");
                    return 0;
            }
        }

        #endregion
    }
}
