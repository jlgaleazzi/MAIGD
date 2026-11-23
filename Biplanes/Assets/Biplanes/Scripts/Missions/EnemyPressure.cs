using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Player;

namespace Biplanes.Missions {

    /// <summary>
    /// Enemy-pressure safety net (B035, GDD "Enemy pressure", ARCHITECTURE §3): play never
    /// goes more than pressure.maxQuietSeconds without an enemy aircraft (FighterAI, BomberAI,
    /// ZeppelinAI) inside the camera view.
    ///
    /// One per mission, created and Init'ed by MissionRunner.StartMission. Each frame (scaled
    /// time) it advances a quiet timer:
    ///   - GameManager not Playing (paused, briefing, result): the timer holds.
    ///   - Mission not started yet (B049), player on the runway, crashed or missing, or no
    ///     objective left open: it resets.
    ///   - Any live enemy aircraft in view: it resets.
    /// At the limit it asks MissionRunner for a wave of pressure.fillerCount reinforcement
    /// fighters (same FighterAI factory as JSON fighters), trimmed so the live fighters that
    /// count toward the cap (on screen, or off screen and closing on it: CountsTowardCap, B041)
    /// never exceed pressure.maxLiveFighters (at the cap it waits), then resets.
    /// B074: the wave always enters just past the RIGHT screen edge and flies left (EntryX /
    /// EntryOffset / EntryFlightDirection), whichever way the player faces or the camera scrolls,
    /// close enough to be on screen within about 0.2 s (FighterAI.BeginEntry adds the camera's
    /// leftward speed while it enters, so it also catches a left-scrolling camera).
    /// Reinforcements are named ScoreKeeper.ReinforcementFighterKey: they score like fighters,
    /// and each kill counts toward a destroyAllOfType "fighter" objective (B050,
    /// ObjectiveTracker.CountsAsKillOf); the required total is still the JSON fighter count.
    ///
    /// Escaped fighters (B040, GDD "Enemy pressure"): a fighter that is outside the view and
    /// not closing on it in the camera's frame (it flew past the player, or the player turned
    /// away faster than it flies) has escaped once it is more than a margin beyond the edge.
    /// B074: a fighter wholly past the LEFT edge (more than LeftExitMargin) has escaped at once,
    /// closing or not, so no fighter ever comes back into view from the left.
    ///   - Reinforcement: removed silently (left exit, or past DespawnBehindMargin = EscapeMargin
    ///     on the right, B075).
    ///   - JSON fighter (left exit, or past EscapeMargin on the right): while an open
    ///     destroyAllOfType objective counts its type it re-enters just past the RIGHT edge
    ///     (same object, same damage); otherwise it is removed silently (no score, no credit).
    /// Escorts holding formation on a live target are left alone (they move with the
    /// bomber/zeppelin); an escort that left formation to chase obeys the left-exit rule.
    ///
    /// The decision maths are pure static helpers, covered by EnemyPressureTests and
    /// EnemyPressureFixTests.
    /// </summary>
    public class EnemyPressure : MonoBehaviour {

        // ── Geometry (sprite/hitbox sizes, not gameplay tunables) ─────────

        /// <summary>Half width of a fighter (1.5-unit hitbox) for the in-view test.</summary>
        public const float FighterHalfWidth = 0.75f;
        /// <summary>Half width of a bomber for the in-view test.</summary>
        public const float BomberHalfWidth = 1.5f;
        /// <summary>Half height used for every aircraft in the in-view test.</summary>
        public const float AircraftHalfHeight = 0.5f;

        /// <summary>
        /// Fighters count toward maxLiveFighters only while within this many screen widths
        /// beyond either screen edge (and, off screen, only while closing on the view: see
        /// CountsTowardCap), so fighters far away never hold reinforcements back.
        /// </summary>
        public const float CapRangeScreens = 1f;

        /// <summary>A reinforcement that has escaped (not closing) this far beyond the RIGHT screen
        /// edge is removed. B075 (QA T975 BUG-1): was 20 (the B035 rule), which let reinforcements
        /// that fell behind a left-flying player linger just off the right edge, outside the
        /// maxLiveFighters cap (they don't close), and all come back at once after a right turn;
        /// now the same as EscapeMargin. The left side uses LeftExitMargin (B074).</summary>
        public const float DespawnBehindMargin = EscapeMargin;

        /// <summary>A JSON fighter that has escaped this far beyond the screen edge re-enters or
        /// is removed (B040). Larger than the entry offset of a default 2-plane wave (plane 1:
        /// at most 2.5), and an entering fighter is always closing (FighterAI.BeginEntry), so a
        /// fighter that just entered is never caught by it.</summary>
        public const float EscapeMargin = 3f;

        /// <summary>Camera-frame speed (units/s) below which a fighter outside the view counts
        /// as not closing on it.</summary>
        public const float ClosingSpeedEpsilon = 0.1f;

        /// <summary>Gap between the first entering fighter's hitbox and the screen edge.</summary>
        public const float EntryGap = 0.25f;
        /// <summary>Extra x per later plane of a wave, so a wave enters one after another.</summary>
        public const float EntryStagger = 1.25f;
        /// <summary>Random extra x per plane (0..this).</summary>
        public const float EntryJitter = 0.25f;

        /// <summary>B074: a fighter whose centre is this far past the left edge is wholly off
        /// screen and has left the view for good: it re-enters from the right or is removed,
        /// never back in from the left. B075 (QA T975 BUG-3): the hitbox half width (the sprite's
        /// visible half width is 0.74), so it is handled the moment it clears the edge; with the
        /// old extra EntryGap a camera turning back could reveal it again from the left.</summary>
        public const float LeftExitMargin = FighterHalfWidth;

        /// <summary>B074: every entering aircraft flies left (toward the view from the right).</summary>
        public const int EntryFlightDirection = -1;

        /// <summary>What to do with an escaped fighter.</summary>
        public enum EscapeAction { None, ReEnter, Remove }

        // ── State ────────────────────────────────────────────────────────

        private MissionRunner _runner;
        private PressureData _data;
        private Camera _cam;
        private float _quiet;

        /// <summary>Seconds of eligible play since an enemy aircraft was last in view.</summary>
        public float QuietSeconds => _quiet;

        /// <summary>Resolved pressure settings (null before Init).</summary>
        public PressureData Settings => _data;

        /// <summary>Starts (or restarts) the safety net for a mission and clears the timer
        /// (MissionRunner's teardown destroys earlier reinforcements).</summary>
        public void Init(MissionRunner runner, PressureData data) {
            _runner = runner;
            _data = data ?? PressureData.Resolve(null);
            _quiet = 0f;
            _cam = ScrollController.Instance != null && ScrollController.Instance.Camera != null
                ? ScrollController.Instance.Camera : Camera.main;
        }

        private void Update() {
            if (_runner == null || _data == null) return;
            if (_cam == null) {
                _cam = ScrollController.Instance != null && ScrollController.Instance.Camera != null
                    ? ScrollController.Instance.Camera : Camera.main;
                if (_cam == null) return;
            }

            Rect view = ViewRect(_cam);
            // Escapes are handled even with pressure disabled: they keep destroy-all winnable
            // and stop fighters piling up off screen (BUG 2 / BUG 6).
            HandleEscapedFighters(view);
            if (!_data.enabled) return;

            var gm = GameManager.Instance;
            bool playing = gm != null && gm.CurrentState == GameManager.GameState.Playing;
            PlayerPlane player = _runner.Player;
            // Waiting for the player's first input (B049) counts like the runway: reset.
            bool eligible = _runner.HasStarted
                && IsEligible(player != null, player != null && player.OnRunway,
                    player != null && player.HasCrashed, _runner.ObjectivesOpen);
            bool visible = playing && eligible && AnyEnemyAircraftInView(view);

            _quiet = Step(_quiet, Time.deltaTime, playing, eligible, visible);
            if (!playing || !eligible || !ShouldSpawn(_quiet, _data.maxQuietSeconds)) return;

            int wave = WaveSize(_data.fillerCount, _data.maxLiveFighters, CountLiveFightersNear(view));
            if (wave <= 0) return; // at the cap: wait (the timer stays at the limit)

            for (int i = 0; i < wave; i++) _runner.SpawnReinforcementFighter(i, wave);
            Debug.Log($"EnemyPressure: {_quiet:0.0}s without enemy aircraft on screen; sent {wave} reinforcement fighter(s).");
            _quiet = 0f;
        }

        // ── Scene queries (no Find*: MissionRunner's aircraft list) ──────

        private bool AnyEnemyAircraftInView(Rect view) {
            var list = _runner.EnemyAircraft;
            for (int i = 0; i < list.Count; i++) {
                var body = list[i];
                if (!IsLiveAircraft(body)) continue;
                if (InView(body.transform.position, HalfWidthOf(body), AircraftHalfHeight, view)) return true;
            }
            return false;
        }

        /// <summary>Live fighters that count toward maxLiveFighters (B041): on screen, or off
        /// screen within the cap range and closing on the view in the camera's frame (the same
        /// relative velocity HandleEscapedFighters uses).</summary>
        private int CountLiveFightersNear(Rect view) {
            float range = view.width * CapRangeScreens;
            float cameraVelocityX = ScrollController.Instance != null ? ScrollController.Instance.Velocity : 0f;
            var list = _runner.EnemyAircraft;
            int n = 0;
            for (int i = 0; i < list.Count; i++) {
                if (!(list[i] is FighterAI f) || f == null || f.IsDead) continue;
                if (CountsTowardCap(f.transform.position.x, f.WorldVelocityX - cameraVelocityX, view, range)) n++;
            }
            return n;
        }

        /// <summary>Re-enters or removes every escaped fighter (B040). Destroying a fighter only
        /// turns its list entry Unity-null, so iterating the runner's list here is safe.</summary>
        private void HandleEscapedFighters(Rect view) {
            float cameraVelocityX = ScrollController.Instance != null ? ScrollController.Instance.Velocity : 0f;
            var list = _runner.EnemyAircraft;
            for (int i = 0; i < list.Count; i++) {
                if (!(list[i] is FighterAI f) || f == null) continue;
                if (f.IsDead) continue; // dying: FighterAI removes itself
                // Escorts in formation move with their bomber/zeppelin: exempt. An escort chasing
                // the player only obeys the left-exit rule (B074).
                if (f.IsEscorting && !f.IsEscortChasing) continue;
                bool reinforcement = f.gameObject.name == ScoreKeeper.ReinforcementFighterKey;
                float margin = reinforcement ? DespawnBehindMargin : EscapeMargin;
                float x = f.transform.position.x;
                bool escaped = f.IsEscorting
                    ? HasLeftView(x, view)
                    : IsEscapedOrLeft(x, f.WorldVelocityX - cameraVelocityX, view, margin);
                bool needed = !reinforcement && _runner.OpenDestroyAllCounts(f.gameObject.name);
                switch (DecideEscape(escaped, reinforcement, needed)) {
                    case EscapeAction.ReEnter:
                        _runner.ReEnterFighter(f);
                        break;
                    case EscapeAction.Remove:
                        Destroy(f.gameObject); // silent: no score, no objective
                        break;
                }
            }
        }

        private static float HalfWidthOf(MonoBehaviour body) {
            if (body is ZeppelinAI) return ZeppelinAI.HalfLength;
            if (body is BomberAI) return BomberHalfWidth;
            return FighterHalfWidth;
        }

        /// <summary>World rect the orthographic camera shows.</summary>
        public static Rect ViewRect(Camera cam) {
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            Vector3 c = cam.transform.position;
            return new Rect(c.x - halfW, c.y - halfH, halfW * 2f, halfH * 2f);
        }

        // ── Pure helpers (EditMode-tested) ───────────────────────────────

        /// <summary>
        /// A registered enemy aircraft that is still alive (BUG 1). Not every aircraft is an
        /// IDamageable (BomberAI isn't), so: an IDamageable answers itself; anything else asks
        /// its Health component; with neither it counts as alive. Null or destroyed: false.
        /// </summary>
        public static bool IsLiveAircraft(MonoBehaviour body) {
            if (body == null) return false;
            if (body is IDamageable d) return !d.IsDead;
            var health = body.GetComponent<Health>();
            return health == null || !health.IsDead;
        }

        /// <summary>The player is flying: present, off the runway and not crashed, and the
        /// mission still has an objective open.</summary>
        public static bool IsEligible(bool hasPlayer, bool onRunway, bool crashed, bool objectivesOpen) {
            return hasPlayer && !onRunway && !crashed && objectivesOpen;
        }

        /// <summary>
        /// One quiet-timer step of `dt` scaled seconds. Not playing (paused etc.): held.
        /// Not eligible, or an enemy aircraft in view: 0. Otherwise quiet + dt.
        /// </summary>
        public static float Step(float quiet, float dt, bool playing, bool eligible, bool enemyVisible) {
            if (!playing) return quiet;
            if (!eligible || enemyVisible) return 0f;
            return quiet + Mathf.Max(0f, dt);
        }

        /// <summary>The quiet time has reached the limit.</summary>
        public static bool ShouldSpawn(float quiet, float maxQuietSeconds) {
            return quiet >= maxQuietSeconds;
        }

        /// <summary>Fighters to send now: fillerCount, trimmed so live + wave never exceeds
        /// maxLiveFighters. 0 = at the cap (wait).</summary>
        public static int WaveSize(int fillerCount, int maxLiveFighters, int liveFighters) {
            int room = maxLiveFighters - Mathf.Max(0, liveFighters);
            return Mathf.Max(0, Mathf.Min(fillerCount, room));
        }

        /// <summary>Height for plane `index` of a `count`-plane wave: the band [low, high] is cut
        /// into `count` equal slices and the plane takes slice `index` at `t01` (0..1) inside it,
        /// so a wave is spread over the band instead of bunching.</summary>
        public static float SliceHeight(float low, float high, int index, int count, float t01) {
            if (high < low) high = low;
            if (count < 1) count = 1;
            index = Mathf.Clamp(index, 0, count - 1);
            float slice = (high - low) / count;
            return low + slice * (index + Mathf.Clamp01(t01));
        }

        /// <summary>A body at `pos` with the given half extents overlaps `view`.</summary>
        public static bool InView(Vector2 pos, float halfWidth, float halfHeight, Rect view) {
            return pos.x + halfWidth >= view.xMin && pos.x - halfWidth <= view.xMax
                && pos.y + halfHeight >= view.yMin && pos.y - halfHeight <= view.yMax;
        }

        /// <summary>x lies within `range` beyond either side of `view`.</summary>
        public static bool IsNear(float x, Rect view, float range) {
            return x >= view.xMin - range && x <= view.xMax + range;
        }

        /// <summary>
        /// Cap rule (B041, QA T958 N1): a fighter at world x counts toward maxLiveFighters when
        /// its hitbox (FighterHalfWidth) overlaps the view horizontally, or when it is off screen
        /// within `range` of the view and closing on it in the camera's frame
        /// (`relativeVelocityX` = fighter world x speed minus the camera's; left of the view it
        /// must move right faster than ClosingSpeedEpsilon, right of the view move left). A
        /// fighter off screen that holds its distance or recedes can never be seen again before
        /// it escapes, so it doesn't block a reinforcement wave.
        /// </summary>
        public static bool CountsTowardCap(float x, float relativeVelocityX, Rect view, float range) {
            if (!IsNear(x, view, range)) return false;
            if (x + FighterHalfWidth >= view.xMin && x - FighterHalfWidth <= view.xMax) return true;
            if (x < view.xMin) return relativeVelocityX > ClosingSpeedEpsilon;
            return relativeVelocityX < -ClosingSpeedEpsilon;
        }

        /// <summary>
        /// World x of an entering aircraft (B074): always `offset` units (clamped at 0) past the
        /// RIGHT edge of the view (camera centre x + half view width), whatever the player's
        /// facing or the scroll direction. Used for JSON planes, reinforcements and re-entries.
        /// </summary>
        public static float EntryX(float cameraX, float halfWidth, float offset) {
            return cameraX + Mathf.Abs(halfWidth) + Mathf.Max(0f, offset);
        }

        /// <summary>B074 check: a body of `halfWidth` at `x` starts wholly at or beyond the right
        /// edge of `view` (never inside it, never on the left).</summary>
        public static bool EntersFromRight(float x, float halfWidth, Rect view) {
            return x - Mathf.Abs(halfWidth) >= view.xMax;
        }

        /// <summary>B074: the fighter's centre is more than LeftExitMargin past the left edge of
        /// `view`, so it is wholly off screen on the left.</summary>
        public static bool HasLeftView(float x, Rect view) {
            return x < view.xMin - LeftExitMargin;
        }

        /// <summary>
        /// Escape test used by HandleEscapedFighters (B074): wholly off the left edge (always
        /// escaped, closing or not, so it can never come back from the left), or IsEscaped with
        /// `margin` (the B040 right-side rule; its left branch is covered by HasLeftView since
        /// every margin is larger than LeftExitMargin).
        /// </summary>
        public static bool IsEscapedOrLeft(float x, float relativeVelocityX, Rect view, float margin) {
            return HasLeftView(x, view) || IsEscaped(x, relativeVelocityX, view, margin);
        }

        /// <summary>
        /// Distance past the right screen edge for plane `index` of an entering wave (B040, BUG 7):
        /// the hitbox starts EntryGap off screen, later planes EntryStagger apart, plus
        /// t01 (0..1) * EntryJitter. Plane 0 is at most EntryGap + EntryJitter (0.5 units) from
        /// being on screen.
        /// </summary>
        public static float EntryOffset(int index, float t01) {
            return FighterHalfWidth + EntryGap + EntryStagger * Mathf.Max(0, index)
                + EntryJitter * Mathf.Clamp01(t01);
        }

        /// <summary>
        /// Escape test in the camera's frame (B040). `relativeVelocityX` = the fighter's world x
        /// speed minus the camera's. The fighter has escaped when it is more than `margin`
        /// beyond the left edge and not moving right relative to the view, or more than
        /// `margin` beyond the right edge and not moving left relative to it. A fighter that
        /// is still closing on the view (about to enter, or catching up) never counts.
        /// </summary>
        public static bool IsEscaped(float x, float relativeVelocityX, Rect view, float margin) {
            if (x < view.xMin - margin) return relativeVelocityX <= ClosingSpeedEpsilon;
            if (x > view.xMax + margin) return relativeVelocityX >= -ClosingSpeedEpsilon;
            return false;
        }

        /// <summary>
        /// What happens to a fighter (B040, GDD "Enemy pressure"): not escaped → None; an
        /// escaped reinforcement → Remove; an escaped JSON fighter that an open destroyAllOfType
        /// objective still counts → ReEnter; any other escaped fighter → Remove.
        /// </summary>
        public static EscapeAction DecideEscape(bool escaped, bool isReinforcement, bool neededByObjective) {
            if (!escaped) return EscapeAction.None;
            if (isReinforcement) return EscapeAction.Remove;
            return neededByObjective ? EscapeAction.ReEnter : EscapeAction.Remove;
        }
    }
}
