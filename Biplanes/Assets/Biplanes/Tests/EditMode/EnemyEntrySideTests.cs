using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Missions;

namespace Biplanes.Tests {

    /// <summary>
    /// B074: enemy aircraft never enter from the left. Every aircraft (JSON fighters and
    /// bombers, zeppelins, EnemyPressure reinforcements, re-entering fighters) appears wholly
    /// past the RIGHT edge of the camera view and flies left, whether the player faces right or
    /// left; a fighter wholly past the left edge has escaped at once (re-enter from the right or
    /// removal, never back in from the left); while entering, a fighter adds the camera's
    /// leftward speed to its top speed so it still reaches the view in time when the player
    /// scrolls left.
    ///
    /// EditMode doesn't run Awake on AddComponent: ScrollController.Create sets Instance itself,
    /// and the FighterAI boost test invokes Awake by reflection.
    /// </summary>
    public class EnemyEntrySideTests {

        private const float Eps = 1e-4f;
        private const float HalfW = 8.8889f;
        private const float Dt = 0.02f;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<Camera> _disabledCameras = new List<Camera>();
        private MissionRunner _runner;

        private static Rect ViewAt(float camX) => new Rect(camX - HalfW, -5f, 2f * HalfW, 10f);

        [TearDown]
        public void TearDown() {
            if (_runner != null) {
                foreach (var a in _runner.EnemyAircraft) {
                    if (a != null) Object.DestroyImmediate(a.gameObject);
                }
                Object.DestroyImmediate(_runner.gameObject);
                _runner = null;
            }
            if (ScrollController.Instance != null) Object.DestroyImmediate(ScrollController.Instance.gameObject);
            ScrollController.Instance = null;
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
            foreach (var c in _disabledCameras) {
                if (c != null) c.enabled = true;
            }
            _disabledCameras.Clear();
        }

        // ── Pure helpers ─────────────────────────────────────────────────

        [Test]
        public void EntryX_IsAlwaysPastTheRightEdge_ForEveryOffsetAndCamera() {
            Assert.AreEqual(-1, EnemyPressure.EntryFlightDirection, "entering aircraft fly left");
            foreach (float camX in new[] { 0f, 37.5f, 100f, 812f }) {
                Rect view = ViewAt(camX);
                foreach (float offset in new[] { -5f, 0f, 0.5f, 1.5f, 7.5f }) {
                    float x = EnemyPressure.EntryX(camX, HalfW, offset);
                    Assert.GreaterOrEqual(x, view.xMax - Eps, $"cam {camX} offset {offset}: never left of the right edge");
                }
                for (int i = 0; i < 4; i++) {
                    foreach (float t in new[] { 0f, 0.5f, 1f }) {
                        float x = EnemyPressure.EntryX(camX, HalfW, EnemyPressure.EntryOffset(i, t));
                        Assert.IsTrue(EnemyPressure.EntersFromRight(x, EnemyPressure.FighterHalfWidth, view),
                            $"cam {camX} plane {i}: the whole hitbox starts past the right edge");
                        Assert.IsFalse(EnemyPressure.InView(new Vector2(x, 0f), EnemyPressure.FighterHalfWidth,
                            EnemyPressure.AircraftHalfHeight, view), "never placed in view");
                    }
                }
            }
        }

        [Test]
        public void EntersFromRight_RejectsInViewAndLeftPositions() {
            Rect view = ViewAt(100f);
            Assert.IsFalse(EnemyPressure.EntersFromRight(view.xMin - 2f, EnemyPressure.FighterHalfWidth, view), "left of the view");
            Assert.IsFalse(EnemyPressure.EntersFromRight(100f, EnemyPressure.FighterHalfWidth, view), "in view");
            Assert.IsFalse(EnemyPressure.EntersFromRight(view.xMax + 0.5f, EnemyPressure.FighterHalfWidth, view),
                "hitbox still overlaps the right edge");
            Assert.IsTrue(EnemyPressure.EntersFromRight(view.xMax + 0.76f, EnemyPressure.FighterHalfWidth, view));
        }

        [Test]
        public void LeftExit_IsAnEscape_EvenWhileClosing() {
            Rect view = ViewAt(100f);
            float justOff = view.xMin - EnemyPressure.LeftExitMargin - 0.01f;
            float partly = view.xMin - EnemyPressure.LeftExitMargin + 0.01f;
            Assert.IsTrue(EnemyPressure.HasLeftView(justOff, view));
            Assert.IsFalse(EnemyPressure.HasLeftView(partly, view), "a fighter partly on screen has not left");
            // Catching up from the left (closing at +5 u/s in the camera's frame): it would come
            // back from the left, so it counts as escaped (B074).
            Assert.IsTrue(EnemyPressure.IsEscapedOrLeft(justOff, 5f, view, EnemyPressure.EscapeMargin));
            Assert.IsTrue(EnemyPressure.IsEscapedOrLeft(justOff, 5f, view, EnemyPressure.DespawnBehindMargin),
                "reinforcements too: no 20-unit grace on the left");
            Assert.IsFalse(EnemyPressure.IsEscapedOrLeft(partly, 5f, view, EnemyPressure.EscapeMargin));
            // The right side keeps the B040 rule: closing = still entering.
            Assert.IsFalse(EnemyPressure.IsEscapedOrLeft(view.xMax + 10f, -3f, view, EnemyPressure.EscapeMargin));
            Assert.IsTrue(EnemyPressure.IsEscapedOrLeft(view.xMax + 10f, 0.5f, view, EnemyPressure.EscapeMargin));
            // The decision is unchanged: needed JSON fighters re-enter (from the right), others go.
            Assert.AreEqual(EnemyPressure.EscapeAction.ReEnter, EnemyPressure.DecideEscape(true, false, true));
            Assert.AreEqual(EnemyPressure.EscapeAction.Remove, EnemyPressure.DecideEscape(true, true, true));
            Assert.AreEqual(EnemyPressure.EscapeAction.Remove, EnemyPressure.DecideEscape(true, false, false));
        }

        [Test]
        public void EntryBoost_OnlyWhenTheCameraRunsAway() {
            Assert.AreEqual(3.9f, FighterAI.EntryBoost(-1, -3.9f), Eps, "player scrolls left: add the camera speed");
            Assert.AreEqual(0f, FighterAI.EntryBoost(-1, 3.9f), Eps, "player scrolls right: closing anyway");
            Assert.AreEqual(0f, FighterAI.EntryBoost(-1, 0f), Eps);
            Assert.AreEqual(2f, FighterAI.EntryBoost(1, 2f), Eps);
            Assert.AreEqual(2.6f, FighterAI.EntryBoost(-7, -2.6f), Eps, "any negative direction = left");
        }

        [Test]
        public void EntryComplete_WhenTheHitboxOverlapsTheView() {
            Rect view = ViewAt(100f);
            Assert.IsFalse(FighterAI.EntryComplete(view.xMax + EnemyPressure.FighterHalfWidth + 0.01f, view.xMin, view.xMax));
            Assert.IsTrue(FighterAI.EntryComplete(view.xMax + EnemyPressure.FighterHalfWidth - 0.01f, view.xMin, view.xMax));
            Assert.IsTrue(FighterAI.EntryComplete(100f, view.xMin, view.xMax));
        }

        [Test]
        public void SpawnTrigger_EveryAircraftAppearsPastTheRightEdge() {
            // Spawns trigger on FarthestX, which only grows while the camera moves right; at the
            // trigger the camera is at FarthestX.
            const float atX = 400f;
            float camX = MissionRunner.SpawnTriggerX("zeppelin", atX, HalfW);
            Assert.GreaterOrEqual(atX - ZeppelinAI.HalfLength, camX + HalfW - Eps, "zeppelin hull wholly off the right edge");
            Assert.AreEqual(atX, MissionRunner.SpawnTriggerX("fighter", atX, HalfW), Eps, "planes: at atX, placed by EntryX");
            Assert.AreEqual(atX, MissionRunner.SpawnTriggerX("bomber", atX, HalfW), Eps);
            float groundCam = MissionRunner.SpawnTriggerX("aaa", atX, HalfW);
            Assert.Greater(atX, groundCam + HalfW, "ground objects unchanged: atX past the right edge");
        }

        // ── Facing left: the entry still reaches the view in time ────────

        [Test]
        public void FacingLeft_EveryMission_ReinforcementWaveReachesTheView_WithinTheQuietRule() {
            var g = GameBalance.Get();
            float fighter = g.enemySpeed.fighter;
            var chase = FighterChaseData.Resolve(g);
            float anchor = g.player.screenAnchor > 0f ? g.player.screenAnchor : 0.3f;
            float quiet = PressureData.Resolve(g).maxQuietSeconds;

            for (int mission = 1; mission <= 5; mission++) {
                float scroll = LoadMission(mission).scrollSpeed;
                foreach (int facing in new[] { 1, -1 }) {
                    for (int plane = 0; plane < 2; plane++) {
                        foreach (float standoff in new[] { chase.standoff, chase.standoffMax }) {
                            float t = SecondsToView(EnemyPressure.EntryOffset(plane, 1f), facing, scroll, fighter,
                                anchor, standoff, chase, 10f);
                            Assert.GreaterOrEqual(t, 0f, $"M{mission} facing {facing} plane {plane}: reaches the view");
                            if (plane == 0) {
                                Assert.LessOrEqual(t, 0.25f, $"M{mission} facing {facing}: plane 0 on screen at once");
                            }
                            Assert.Less(t, quiet, $"M{mission} facing {facing} plane {plane}: well inside the quiet limit");
                        }
                    }
                }
            }
        }

        // ── MissionRunner: every spawn path enters from the right ────────

        [Test]
        public void Runner_FacingLeft_ReinforcementsAndReEntry_EnterFromTheRight() {
            Camera cam = SetUpCameraAndScroll(250f, -1);
            Assert.AreEqual(-1, _runner.PlayerFacing, "precondition: the player faces left");
            Rect view = EnemyPressure.ViewRect(cam);

            for (int i = 0; i < 2; i++) {
                var f = _runner.SpawnReinforcementFighter(i, 2);
                Assert.IsNotNull(f);
                Assert.IsTrue(EnemyPressure.EntersFromRight(f.transform.position.x, EnemyPressure.FighterHalfWidth, view),
                    $"reinforcement {i} at x={f.transform.position.x} enters past the right edge {view.xMax}");
                Assert.AreEqual(-1, f.FlightDirection, "flies left, toward the view");
                Assert.IsTrue(f.IsEntering);
            }

            // A JSON fighter that left the view on the left re-enters on the right.
            var escaped = FighterAI.Spawn(new Vector3(view.xMin - 5f, 1f, 0f));
            _created.Add(escaped.gameObject);
            escaped.SetFlightDirection(1);
            Assert.IsTrue(_runner.ReEnterFighter(escaped));
            Assert.IsTrue(EnemyPressure.EntersFromRight(escaped.transform.position.x, EnemyPressure.FighterHalfWidth, view));
            Assert.AreEqual(-1, escaped.FlightDirection);
            Assert.IsTrue(escaped.IsEntering);
        }

        [Test]
        public void Runner_FacingRight_ReinforcementsAndReEntry_EnterFromTheRight() {
            Camera cam = SetUpCameraAndScroll(250f, 1);
            Assert.AreEqual(1, _runner.PlayerFacing, "precondition: the player faces right");
            Rect view = EnemyPressure.ViewRect(cam);
            var f = _runner.SpawnReinforcementFighter(0, 1);
            Assert.IsTrue(EnemyPressure.EntersFromRight(f.transform.position.x, EnemyPressure.FighterHalfWidth, view));
            Assert.AreEqual(-1, f.FlightDirection);
            Assert.IsTrue(_runner.ReEnterFighter(f));
            Assert.IsTrue(EnemyPressure.EntersFromRight(f.transform.position.x, EnemyPressure.FighterHalfWidth, view));
        }

        [Test]
        public void Runner_JsonFightersAndBombers_EnterFromTheRight_EitherFacing() {
            foreach (int facing in new[] { 1, -1 }) {
                TearDown();
                Camera cam = SetUpCameraAndScroll(250f, facing);
                Rect view = EnemyPressure.ViewRect(cam);
                // atX far to the left of the camera must not pull a plane onto the left side.
                _runner.SpawnEntities(new SpawnEntry { type = "fighter", atX = 10, count = 3, spacing = 2f, pattern = "sine", id = "" });
                _runner.SpawnEntities(new SpawnEntry { type = "bomber", atX = 10, count = 2, spacing = 4f, id = "" });

                int fighters = 0, bombers = 0;
                foreach (var a in _runner.EnemyAircraft) {
                    if (a == null) continue;
                    float half = a is BomberAI ? EnemyPressure.BomberHalfWidth : EnemyPressure.FighterHalfWidth;
                    Assert.GreaterOrEqual(a.transform.position.x - half, view.xMax - Eps,
                        $"facing {facing}: {a.GetType().Name} at x={a.transform.position.x} starts past the right edge");
                    if (a is FighterAI f) {
                        fighters++;
                        Assert.AreEqual(-1, f.FlightDirection, "JSON fighters fly left");
                        Assert.IsTrue(f.IsEntering);
                    } else if (a is BomberAI) {
                        bombers++;
                    }
                }
                Assert.AreEqual(3, fighters);
                Assert.AreEqual(2, bombers);
            }
        }

        [Test]
        public void Fighter_BeginEntry_AddsTheLeftScrollToItsStartSpeed() {
            var f = FighterAI.Spawn(new Vector3(0f, 1f, 0f));
            _created.Add(f.gameObject);
            InvokeAwake(f.GetComponent<Health>());
            InvokeAwake(f);
            float speed = GameBalance.Get().enemySpeed.fighter;
            Assert.AreEqual(-speed, f.WorldVelocityX, Eps, "precondition: cruise, flying left");

            f.BeginEntry(3.9f); // camera scrolls right: no boost
            Assert.AreEqual(-speed, f.WorldVelocityX, Eps);
            Assert.IsTrue(f.IsEntering);

            f.BeginEntry(-3.9f); // camera scrolls left at 3.9: closes at cruise speed relative to it
            Assert.AreEqual(-(speed + 3.9f), f.WorldVelocityX, Eps);
            Assert.AreEqual(-speed, f.WorldVelocityX - (-3.9f), Eps, "relative to the camera: cruise speed, leftward");
        }

        // ── B075 (QA T975 BUG-1..4) ──────────────────────────────────────

        [Test]
        public void B075_BoostCameraVelocity_UsesTheFartherOfCurrentAndTarget() {
            // Mid-turn right -> left: the camera still moves right (+1) but eases toward -3.9.
            Assert.AreEqual(-3.9f, FighterAI.BoostCameraVelocity(-1, 1f, -3.9f), Eps);
            Assert.AreEqual(3.9f, FighterAI.EntryBoost(-1, FighterAI.BoostCameraVelocity(-1, 1f, -3.9f)), Eps);
            // Turning back right: the target no longer runs left, the lagging velocity still does.
            Assert.AreEqual(-2f, FighterAI.BoostCameraVelocity(-1, -2f, 3.9f), Eps);
            // Steady right scroll: no boost for a left flier.
            Assert.AreEqual(0f, FighterAI.EntryBoost(-1, FighterAI.BoostCameraVelocity(-1, 3.9f, 3.9f)), Eps);
            Assert.AreEqual(3.9f, FighterAI.BoostCameraVelocity(1, 1f, 3.9f), Eps, "symmetric for a right flier");
        }

        [Test]
        public void B075_BehindBoost_OnlyFlyingLeftWithThePlayerAhead() {
            Assert.IsTrue(FighterAI.BehindBoostApplies(-1, -4f), "behind a left-flying player");
            Assert.IsFalse(FighterAI.BehindBoostApplies(-1, 4f), "player behind it: no boost");
            Assert.IsFalse(FighterAI.BehindBoostApplies(1, 4f), "facing right: the B059 chase is unchanged");
            Assert.IsFalse(FighterAI.BehindBoostApplies(1, -4f));
        }

        [Test]
        public void B075_LeftExit_AtTheHitboxHalfWidth() {
            Assert.AreEqual(EnemyPressure.FighterHalfWidth, EnemyPressure.LeftExitMargin, Eps);
            Rect view = ViewAt(100f);
            // The sprite's visible half width is 0.74: once the centre is past 0.75 it is gone.
            Assert.IsTrue(EnemyPressure.HasLeftView(view.xMin - 0.76f, view));
            Assert.IsFalse(EnemyPressure.HasLeftView(view.xMin - 0.74f, view));
        }

        [Test]
        public void B075_BomberEntryMargin_KeepsTheNoseOffScreen() {
            float bomber = MissionRunner.PlaneEntryMargin("bomber");
            Assert.GreaterOrEqual(bomber, EnemyPressure.BomberHalfWidth + EnemyPressure.EntryGap - Eps);
            Rect view = ViewAt(100f);
            float x = EnemyPressure.EntryX(100f, HalfW, bomber); // stagger 0: the closest case
            Assert.GreaterOrEqual(x - 1.54f, view.xMax, "visible nose (1.54 u) starts off screen");
            Assert.IsFalse(EnemyPressure.InView(new Vector2(x, 0f), EnemyPressure.BomberHalfWidth,
                EnemyPressure.AircraftHalfHeight, view), "hitbox not in view on its spawn frame");
            Assert.AreEqual(1.5f, MissionRunner.PlaneEntryMargin("fighter"), Eps, "fighters unchanged");
        }

        [Test]
        public void B075_PressureLoop_WithTurns_KeepsTheCapAndTheQuietRule() {
            // Model of the game loop with the B075 rules (behind boost, target-velocity boost,
            // 3-unit right removal, 0.75 left exit): turns every 3-10 s, nobody shoots.
            var g = GameBalance.Get();
            var p = PressureData.Resolve(g);
            float fighter = g.enemySpeed.fighter;
            var chase = FighterChaseData.Resolve(g);
            float ease = g.player.turnEaseSeconds > 0f ? g.player.turnEaseSeconds : 0.6f;
            foreach (float scroll in new[] { 2.6f, 3.25f, 3.9f }) {
                SimulateWithTurns(scroll, 300f, fighter, chase, p, ease, 11, out int worstInView, out float worstGap);
                Assert.LessOrEqual(worstInView, p.maxLiveFighters, $"scroll {scroll}: fighters on screen at once");
                Assert.LessOrEqual(worstGap, p.maxQuietSeconds + 0.25f, $"scroll {scroll}: longest stretch without an enemy");
            }
        }

        /// <summary>1-D pressure loop with turns (B075): camera eases between ±scroll at random
        /// 3-10 s intervals; fighters steer with the chase helpers plus the entry and behind boosts
        /// (BoostCameraVelocity of the current and target camera velocity); IsEscapedOrLeft
        /// removes them (DespawnBehindMargin); waves follow WaveSize over CountsTowardCap.</summary>
        private static void SimulateWithTurns(float scroll, float total, float fighter, FighterChaseData chase,
                PressureData p, float ease, int seed, out int worstInView, out float worstGap) {
            var rng = new System.Random(seed);
            var xs = new List<float>(); var vs = new List<float>(); var dirs = new List<int>();
            var entering = new List<bool>(); var standoffs = new List<float>();
            float camX = 0f, camV = scroll, quiet = 0f, gap = 0f;
            float target = scroll, nextTurn = 3f + 7f * (float)rng.NextDouble();
            worstInView = 0; worstGap = 0f;
            int ticks = Mathf.RoundToInt(total / Dt);
            for (int i = 0; i < ticks; i++) {
                float t = i * Dt;
                if (t >= nextTurn) { target = -target; nextTurn = t + 3f + 7f * (float)rng.NextDouble(); }
                camV = Mathf.MoveTowards(camV, target, 2f * Mathf.Max(scroll, Mathf.Abs(camV)) / ease * Dt);
                camX += camV * Dt;
                float px = camX + ((target > 0f ? 0.3f : 0.7f) - 0.5f) * 2f * HalfW;
                var view = new Rect(camX - HalfW, -5f, 2f * HalfW, 10f);
                for (int k = 0; k < xs.Count; k++) {
                    if (entering[k] && FighterAI.EntryComplete(xs[k], view.xMin, view.xMax)) entering[k] = false;
                    dirs[k] = FighterAI.DesiredFacing(dirs[k], px - xs[k], chase.turnDeadzone);
                    float boost = FighterAI.EntryBoost(dirs[k], FighterAI.BoostCameraVelocity(dirs[k], camV, target));
                    bool boosted = entering[k] || FighterAI.BehindBoostApplies(dirs[k], px - xs[k]);
                    vs[k] = FighterAI.HorizontalVelocityStep(xs[k], vs[k], px, camV, dirs[k], standoffs[k],
                        fighter + (boosted ? boost : 0f), chase.accel, Dt, FighterAI.MinForwardSpeed);
                    xs[k] += vs[k] * Dt;
                }
                for (int k = xs.Count - 1; k >= 0; k--) {
                    if (EnemyPressure.IsEscapedOrLeft(xs[k], vs[k] - camV, view, EnemyPressure.DespawnBehindMargin)) {
                        xs.RemoveAt(k); vs.RemoveAt(k); dirs.RemoveAt(k); entering.RemoveAt(k); standoffs.RemoveAt(k);
                    }
                }
                int inView = 0, near = 0;
                for (int k = 0; k < xs.Count; k++) {
                    if (EnemyPressure.InView(new Vector2(xs[k], 0f), EnemyPressure.FighterHalfWidth, 0.5f, view)) inView++;
                    if (EnemyPressure.CountsTowardCap(xs[k], vs[k] - camV, view, view.width * EnemyPressure.CapRangeScreens)) near++;
                }
                worstInView = Mathf.Max(worstInView, inView);
                gap = inView > 0 ? 0f : gap + Dt;
                worstGap = Mathf.Max(worstGap, gap);
                quiet = EnemyPressure.Step(quiet, Dt, true, true, inView > 0);
                if (!EnemyPressure.ShouldSpawn(quiet, p.maxQuietSeconds)) continue;
                int wave = EnemyPressure.WaveSize(p.fillerCount, p.maxLiveFighters, near);
                if (wave <= 0) continue;
                for (int w = 0; w < wave; w++) {
                    int dir = EnemyPressure.EntryFlightDirection;
                    xs.Add(EnemyPressure.EntryX(camX, HalfW, EnemyPressure.EntryOffset(w, (float)rng.NextDouble())));
                    vs.Add(dir * (fighter + FighterAI.EntryBoost(dir, FighterAI.BoostCameraVelocity(dir, camV, target))));
                    dirs.Add(dir);
                    entering.Add(true);
                    standoffs.Add(Mathf.Lerp(chase.standoff, chase.standoffMax, (float)rng.NextDouble()));
                }
                quiet = 0f;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>1-D model of an entering fighter (B074): camera and player cruise at `scroll`
        /// along `facing`, the player at its trailing screen anchor; the fighter starts `offset`
        /// past the right edge flying left at cruise + EntryBoost, steering like ChaseStep with the
        /// boost until EntryComplete. Returns the seconds until its hitbox overlaps the view, -1
        /// if it never does within `seconds`.</summary>
        private static float SecondsToView(float offset, int facing, float scroll, float fighter, float anchor,
                float standoff, FighterChaseData chase, float seconds) {
            float camX = 100f;
            float camV = facing * scroll;
            float anchorViewport = facing > 0 ? anchor : 1f - anchor;
            float x = EnemyPressure.EntryX(camX, HalfW, offset);
            int dir = EnemyPressure.EntryFlightDirection;
            float v = dir * (fighter + FighterAI.EntryBoost(dir, camV));
            int ticks = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < ticks; i++) {
                camX += camV * Dt;
                float px = camX + (anchorViewport - 0.5f) * 2f * HalfW;
                float maxSpeed = fighter + FighterAI.EntryBoost(dir, camV);
                dir = FighterAI.DesiredFacing(dir, px - x, chase.turnDeadzone);
                v = FighterAI.HorizontalVelocityStep(x, v, px, camV, dir, standoff, maxSpeed, chase.accel, Dt,
                    FighterAI.MinForwardSpeed);
                x += v * Dt;
                if (FighterAI.EntryComplete(x, camX - HalfW, camX + HalfW)) return (i + 1) * Dt;
            }
            return -1f;
        }

        /// <summary>A test MainCamera at x `camX` (16:9, size 5), a ScrollController scrolling
        /// `direction`, and a MissionRunner (no mission loaded).</summary>
        private Camera SetUpCameraAndScroll(float camX, int direction) {
            foreach (var c in Camera.allCameras) {
                if (c != null && c.CompareTag("MainCamera")) {
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
            }
            var camGo = new GameObject("T_B074_Camera");
            camGo.tag = "MainCamera";
            _created.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.aspect = 16f / 9f;
            camGo.transform.position = new Vector3(camX, 0f, -10f);

            ScrollController.Instance = null;
            var sc = ScrollController.Create(3f);
            Assert.AreSame(cam, sc.Camera, "ScrollController did not pick up the test camera");
            sc.SetDirection(direction);

            var runnerGo = new GameObject("T_B074_Runner");
            _runner = runnerGo.AddComponent<MissionRunner>();
            return cam;
        }

        private static void InvokeAwake(Component c) {
            var awake = c.GetType().GetMethod("Awake", Private);
            Assert.IsNotNull(awake, $"{c.GetType().Name}.Awake not found by reflection");
            awake.Invoke(c, null);
        }

        private static MissionData LoadMission(int index) {
            var text = Resources.Load<TextAsset>($"Missions/mission_{index:D2}");
            Assert.IsNotNull(text, $"mission_{index:D2}.json not found in Resources/Missions");
            var data = JsonUtility.FromJson<MissionData>(text.text);
            Assert.IsNotNull(data);
            return data;
        }
    }
}
