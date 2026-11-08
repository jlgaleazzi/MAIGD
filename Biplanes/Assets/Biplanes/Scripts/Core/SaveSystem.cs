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

        /// <summary>Saves the best score for *missionIndex* if it exceeds the current record.</summary>
        public static void SaveBestScore(int missionIndex, int score) {
            if (missionIndex < 1 || missionIndex > 5) return;
            string key = KeyPrefix + missionIndex;
            int current = PlayerPrefs.GetInt(key, 0);
            if (score > current) {
                PlayerPrefs.SetInt(key, score);
                PlayerPrefs.Save();
            }
        }
    }
}
