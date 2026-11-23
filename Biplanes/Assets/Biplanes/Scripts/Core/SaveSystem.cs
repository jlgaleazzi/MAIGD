using UnityEngine;

namespace Biplanes.Core {

    /// <summary>
    /// Thin persistence wrapper around PlayerPrefs for mission progression and best scores.
    /// Keys match the contract in ARCHITECTURE.md §2 line 80.
    ///
    ///   "unlocked_mission" → int 1..5   (most highly-unlocked mission index)
    ///   "best_score_N"     → int        (best score achieved on mission N)
    /// </summary>
    public static class SaveSystem {

        private const string KeyUnlocked = "unlocked_mission";
        private const string KeyPrefix   = "best_score_";

        // ── Unlocked mission ───────────────────────────────────────────────

        /// <summary>Highest mission index (1-5) the player has unlocked.</summary>
        public static int GetUnlockedMission() {
            return PlayerPrefs.GetInt(KeyUnlocked, 1);
        }

        /// <summary>Saves the highest unlocked mission index (1-5).</summary>
        public static void SaveUnlockedMission(int missionIndex) {
            if (missionIndex < 1 || missionIndex > 5) return;
            // Only promote, never demote.
            int current = GetUnlockedMission();
            if (missionIndex > current) {
                PlayerPrefs.SetInt(KeyUnlocked, missionIndex);
                PlayerPrefs.Save();
            }
        }

        // ── Best scores per mission ────────────────────────────────────────

        /// <summary>Best score achieved on mission *missionIndex* (1-5).</summary>
        public static int GetBestScore(int missionIndex) {
            if (missionIndex < 1 || missionIndex > 5) return 0;
            return PlayerPrefs.GetInt(KeyPrefix + missionIndex, 0);
        }

        /// <summary>
        /// True while the current run may not set a best score (B078: the mission-5 HQ cheat
        /// was used). Set by MissionRunner when the cheat fires, cleared by
        /// MissionRunner.StartMission for every new run (Retry, Next, menu). Unlocks are not
        /// affected.
        /// </summary>
        public static bool BestScoreLocked { get; set; }

        /// <summary>Pure best-score rule: save `score` over `current` only when it is higher
        /// and the run is not locked (B078 cheat run).</summary>
        public static bool ShouldSaveBest(int score, int current, bool locked) {
            return !locked && score > current;
        }

        /// <summary>Saves the best score for *missionIndex* if it exceeds the current record,
        /// unless BestScoreLocked (cheat run, B078).</summary>
        public static void SaveBestScore(int missionIndex, int score) {
            if (missionIndex < 1 || missionIndex > 5) return;
            string key = KeyPrefix + missionIndex;
            int current = PlayerPrefs.GetInt(key, 0);
            if (ShouldSaveBest(score, current, BestScoreLocked)) {
                PlayerPrefs.SetInt(key, score);
                PlayerPrefs.Save();
            }
        }
    }
}
