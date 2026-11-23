using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Biplanes.Core;
using Biplanes.UI;

namespace Biplanes.Missions {

    /// <summary>
    /// Mission-5 cheat code (B078): typing H then Q (each key within CheatMatcher.DefaultMaxGap
    /// seconds of the previous one; any other key press resets) jumps the player plane and the
    /// camera to the player HQ (MissionRunner.JumpToHq). The run is then flagged
    /// MissionRunner.CheatUsed and its score is never saved as a best score.
    ///
    /// One per mission, added and Init'ed by MissionRunner.StartMission (like EnemyPressure).
    /// Keys are read through Keyboard.current only while the cheat is allowed
    /// (MissionRunner.CanUseHqCheat: mission 5, GameManager Playing, no UI screen up, after the
    /// start gate, plane airborne and not crashed, mission not ended); otherwise the matcher is
    /// reset, so a half-typed code never carries over a pause or a screen. H and Q are bound to
    /// nothing else (W/A/S/D, left mouse, Space, Escape), so typing the code never fires or
    /// drops a bomb. Mouse buttons are not keys and never reset the matcher.
    ///
    /// The decision maths are pure static helpers, covered by CheatCodeTests.
    /// </summary>
    public class CheatCodes : MonoBehaviour {

        /// <summary>The only mission the HQ cheat works in.</summary>
        public const int HqCheatMissionId = 5;

        /// <summary>The key sequence (letters A-Z, upper case).</summary>
        public const string HqSequence = "HQ";

        /// <summary>Height of the warped plane above the ground at the HQ (the HQ is 1 unit tall).</summary>
        public const float WarpHeightAboveGround = 3.5f;
        /// <summary>Lowest warp height above the ground (clears the HQ roof and the plane's hitbox).</summary>
        public const float WarpMinHeightAboveGround = 2f;
        /// <summary>The warped plane's centre stays at least this far below the top of the view.</summary>
        public const float WarpTopMargin = 1f;

        /// <summary>Key value fed to the matcher for any key that is not a letter.</summary>
        public const char OtherKey = '\0';

        private MissionRunner _runner;
        private readonly CheatMatcher _matcher = new CheatMatcher(HqSequence);

        /// <summary>The matcher (read-only access for tests / debugging).</summary>
        public CheatMatcher Matcher => _matcher;

        /// <summary>Starts (or restarts) the cheat listener for a mission.</summary>
        public void Init(MissionRunner runner) {
            _runner = runner;
            _matcher.Reset();
        }

        private void Update() {
            if (_runner == null || !_runner.CanUseHqCheat || !UiAllowsCheat()) {
                if (_matcher.Progress != 0) _matcher.Reset();
                return;
            }
            var kb = Keyboard.current;
            if (kb == null) return;

            float now = Time.unscaledTime;
            var keys = kb.allKeys;
            for (int i = 0; i < keys.Count; i++) {
                KeyControl k = keys[i];
                if (k == null || !k.wasPressedThisFrame) continue;
                if (_matcher.Feed(KeyToChar(k.keyCode), now)) {
                    _runner.TryHqCheat();
                }
            }
        }

        /// <summary>No menu, briefing, pause or result screen is up and the world isn't frozen.</summary>
        private static bool UiAllowsCheat() {
            return !MainMenuView.IsShowing && !BriefingView.IsShowing && !PauseView.IsShowing
                && !ResultView.IsShowing && Time.timeScale > 0f;
        }

        // ── Pure helpers (EditMode-tested) ───────────────────────────────

        /// <summary>A letter key as its upper-case char, any other key as OtherKey.</summary>
        public static char KeyToChar(Key key) {
            if (key >= Key.A && key <= Key.Z) return (char)('A' + (key - Key.A));
            return OtherKey;
        }

        /// <summary>
        /// The cheat gate (B078): only in mission HqCheatMissionId, while the game is Playing
        /// (not paused, briefing, result or menu), after the first-input start gate, with a
        /// player plane that is airborne (not on the runway) and not crashed, and while the
        /// mission has not ended (won or lost).
        /// </summary>
        public static bool CanUseCheat(int missionId, bool playing, bool started, bool hasPlayer,
                                       bool onRunway, bool crashed, bool missionEnded) {
            return missionId == HqCheatMissionId && playing && started && hasPlayer
                && !onRunway && !crashed && !missionEnded;
        }

        /// <summary>
        /// Spawn rule for the jump (B078): a pending entry the jump passes is KEPT (it spawns
        /// normally on the next MissionRunner.Update) when it is needed to finish the mission or
        /// is the destination: the playerHQ, a zeppelin, an escort (escortOf set), an entry whose
        /// id is an objective's targetId (destroyById / bombTarget), or an entry whose type an
        /// open destroyAllOfType objective counts. Everything else (fighters, AAA, balloons,
        /// pickups, obstacles) is skipped.
        /// </summary>
        public static bool KeepOnJump(SpawnEntry entry, ObjectiveEntry[] objectives) {
            if (entry == null) return false;
            string type = entry.type != null ? entry.type.ToLowerInvariant() : "";
            if (type == "playerhq" || type == "zeppelin") return true;
            if (!string.IsNullOrEmpty(entry.escortOf)) return true;
            if (objectives == null) return false;
            foreach (var obj in objectives) {
                if (obj == null) continue;
                if (!string.IsNullOrEmpty(entry.id) && obj.targetId == entry.id) return true;
                if (!obj.evaluated && obj.type == "destroyAllOfType"
                    && string.Equals(obj.targetType, entry.type, System.StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Marks as spawned (skipped) every pending entry whose trigger
        /// (MissionRunner.SpawnTriggerX for a view of half width `halfWidth`) is at or before
        /// `newCameraX` and that KeepOnJump doesn't keep. Kept entries stay pending, so their
        /// normal spawn runs on the next Update (planes off the right edge, a zeppelin at its
        /// atX). Entries ahead of the new camera x are untouched. Returns the number skipped.
        /// </summary>
        public static int SkipPassedSpawns(SpawnEntry[] spawns, bool[] spawnedFlags, ObjectiveEntry[] objectives,
                                           float newCameraX, float halfWidth) {
            if (spawns == null || spawnedFlags == null) return 0;
            int skipped = 0;
            for (int i = 0; i < spawns.Length && i < spawnedFlags.Length; i++) {
                var s = spawns[i];
                if (spawnedFlags[i] || s == null) continue;
                string type = s.type != null ? s.type.ToLowerInvariant() : "";
                if (MissionRunner.SpawnTriggerX(type, s.atX, halfWidth) > newCameraX) continue;
                if (KeepOnJump(s, objectives)) continue;
                spawnedFlags[i] = true;
                skipped++;
            }
            return skipped;
        }

        /// <summary>Facing at the HQ that keeps the threat (the zeppelin) ahead: +1 when it is
        /// at or right of the HQ, -1 when left of it.</summary>
        public static int FacingTowardThreat(float hqX, float threatX) {
            return threatX >= hqX ? 1 : -1;
        }

        /// <summary>Camera x that puts a plane at world `planeX` on its trailing screen anchor
        /// for `facing` (viewport `anchor` facing right, 1 - anchor facing left), the same
        /// anchor PlayerPlane eases toward.</summary>
        public static float CameraXForAnchor(float planeX, int facing, float anchor, float halfWidth) {
            float anchorViewport = facing > 0 ? anchor : 1f - anchor;
            return planeX - (anchorViewport - 0.5f) * 2f * halfWidth;
        }

        /// <summary>Warp height: WarpHeightAboveGround over the ground, lowered to stay
        /// WarpTopMargin under the view top, but never below WarpMinHeightAboveGround.</summary>
        public static float WarpY(float groundY, float viewTopY) {
            float y = Mathf.Min(groundY + WarpHeightAboveGround, viewTopY - WarpTopMargin);
            return Mathf.Max(y, groundY + WarpMinHeightAboveGround);
        }

        /// <summary>
        /// Pure key-sequence matcher (B078). Feed it every key press (upper-case letter, or
        /// OtherKey) with its time in seconds. A key matching the next expected one advances; a
        /// key more than maxGapSeconds after the previous accepted one starts over; any wrong
        /// key resets (but a wrong key that is the sequence's first key starts a new attempt,
        /// so "HHQ" still matches). Feed returns true on the key that completes the sequence,
        /// and the matcher resets for the next time.
        /// </summary>
        public sealed class CheatMatcher {

            /// <summary>Longest pause allowed between two keys of the code.</summary>
            public const float DefaultMaxGap = 1.5f;

            private readonly string _sequence;
            private readonly float _maxGap;
            private int _progress;
            private float _lastTime;

            public CheatMatcher(string sequence, float maxGapSeconds = DefaultMaxGap) {
                _sequence = string.IsNullOrEmpty(sequence) ? HqSequence : sequence.ToUpperInvariant();
                _maxGap = maxGapSeconds > 0f ? maxGapSeconds : DefaultMaxGap;
            }

            /// <summary>The sequence matched (upper case).</summary>
            public string Sequence => _sequence;

            /// <summary>Number of keys of the sequence typed so far.</summary>
            public int Progress => _progress;

            /// <summary>Forget any partial input.</summary>
            public void Reset() {
                _progress = 0;
            }

            /// <summary>One key press at `time` seconds. True when it completes the sequence.</summary>
            public bool Feed(char key, float time) {
                key = char.ToUpperInvariant(key);
                if (_progress > 0 && time - _lastTime > _maxGap) _progress = 0;

                if (key == _sequence[_progress]) {
                    _progress++;
                } else {
                    _progress = key == _sequence[0] ? 1 : 0;
                }
                _lastTime = time;

                if (_progress >= _sequence.Length) {
                    _progress = 0;
                    return true;
                }
                return false;
            }
        }
    }
}
