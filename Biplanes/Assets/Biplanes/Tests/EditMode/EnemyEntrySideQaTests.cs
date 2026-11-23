using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Missions;

namespace Biplanes.Tests {

    /// <summary>
    /// QA T975 (verifies B074): gap tests on top of EnemyEntrySideTests.
    ///   - Every aircraft spawn entry of all 5 missions (fighters, bombers, escorts, the M5
    ///     zeppelin) appears wholly past the RIGHT edge, both facings.
    ///   - A wave sent mid-turn (camera reversing from right to left) still reaches the view.
    ///   - The entry boost ends once in view and the extra speed bleeds off at the chase accel.
    ///   - Pressure loop while flying left in a mission that cruises faster than a fighter, then
    ///     turning right: no more than maxLiveFighters fighters on screen at once (T975 BUG-1).
    /// The simulations are 1-D models built from the game's own pure helpers (EnemyPressure,
    /// FighterAI chase step, ScrollController ease rule).
    /// </summary>
    public class EnemyEntrySideQaTests {

        private const float Eps = 1e-4f;
        private const float HalfW = 8.8889f;
        private const float Dt = 0.02f;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<Camera> _disabledCameras = new List<Camera>();
        private MissionRunner _runner;

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

        // ── Every mission spawn entry ────────────────────────────────────

        [Test]
        public void AllMissions_EveryAircraftEntry_SpawnsPastTheRightEdge_BothFacings() {
            int checkedPlanes = 0;
            for (int mission = 1; mission <= 5; mission++) {
                var data = LoadMission(mission);
                foreach (int facing in new[] { 1, -1 }) {
                    foreach (var entry in data.spawns) {
                        string t = entry.type.ToLowerInvariant();
                        if (t != "fighter" && t != "bomber" && t != "zeppelin") continue;
                        TearDown();
                        // The spawn triggers when FarthestX reaches the trigger; the camera is
                        // there (facing right) or behind it (turned back left: farther left).
                        float trigger = MissionRunner.SpawnTriggerX(t, entry.atX, HalfW);
                        float camX = facing > 0 ? trigger : trigger - 5f;
                        Camera cam = SetUpCameraAndScroll(camX, facing);
                        Rect view = EnemyPressure.ViewRect(cam);
                        // Escorts look for their target: spawn it first, as the JSON order does.
                        if (!string.IsNullOrEmpty(entry.escortOf)) {
                            foreach (var target in data.spawns) {
                                if (target.id == entry.escortOf) _runner.SpawnEntities(target);
                            }
                        }
                        int before = CountLive();
                        _runner.SpawnEntities(entry);
                        Assert.AreEqual(before + entry.count, CountLive(), $"M{mission} {t}@{entry.atX}: all spawned");

                        var list = _runner.EnemyAircraft;
                        for (int n = before; n < list.Count; n++) {
                            var a = list[n]; // only this entry's aircraft (not an escort's target)
                            if (a == null) continue;
                            if (a is ZeppelinAI) {
                                Assert.GreaterOrEqual(a.transform.position.x - ZeppelinAI.HalfLength, view.xMax - Eps,
                                    $"M{mission} facing {facing}: zeppelin hull wholly past the right edge");
                                continue;
                            }
                            float half = a is BomberAI ? EnemyPressure.BomberHalfWidth : EnemyPressure.FighterHalfWidth;
                            Assert.IsTrue(EnemyPressure.EntersFromRight(a.transform.position.x, half, view),
                                $"M{mission} facing {facing} {t}@{entry.atX}: {a.GetType().Name} at x={a.transform.position.x:0.00} vs right edge {view.xMax:0.00}");
                            Assert.IsFalse(EnemyPressure.HasLeftView(a.transform.position.x, view));
                            if (a is FighterAI f) {
                                Assert.AreEqual(-1, f.FlightDirection, $"M{mission}: JSON fighter flies left");
                                Assert.IsTrue(f.IsEntering, $"M{mission}: JSON fighter gets the entry boost");
                            }
                            checkedPlanes++;
                        }
                    }
                }
            }
            Assert.Greater(checkedPlanes, 300, "sanity: every mission's aircraft were checked");
        }

        // ── Mid-turn entry ───────────────────────────────────────────────

        [Test]
        public void MidTurn_WaveSentWhileTheCameraReverses_StillReachesTheView() {
            var g = GameBalance.Get();
            float fighter = g.enemySpeed.fighter;
            var chase = FighterChaseData.Resolve(g);
            float ease = g.player.turnEaseSeconds > 0f ? g.player.turnEaseSeconds : 0.6f;
            for (int mission = 1; mission <= 5; mission++) {
                float scroll = LoadMission(mission).scrollSpeed;
                // Spawn at every phase of a right-to-left turn (camera velocity +scroll .. -scroll).
                for (float phase = 0f; phase <= 1.0001f; phase += 0.1f) {
                    float camV = Mathf.Lerp(scroll, -scroll, phase);
                    float worst = 0f;
                    foreach (float standoff in new[] { chase.standoff, chase.standoffMax }) {
                        float camX = 100f;
                        float x = EnemyPressure.EntryX(camX, HalfW, EnemyPressure.EntryOffset(0, 1f));
                        int dir = EnemyPressure.EntryFlightDirection;
                        float v = dir * (fighter + FighterAI.EntryBoost(dir, camV));
                        float t = -1f;
                        for (int i = 0; i < 500; i++) {
                            camV = Mathf.MoveTowards(camV, -scroll, 2f * Mathf.Max(scroll, Mathf.Abs(camV)) / ease * Dt);
                            camX += camV * Dt;
                            float px = camX + (0.7f - 0.5f) * 2f * HalfW;
                            float maxSpeed = fighter + FighterAI.EntryBoost(dir, camV);
                            dir = FighterAI.DesiredFacing(dir, px - x, chase.turnDeadzone);
                            v = FighterAI.HorizontalVelocityStep(x, v, px, camV, dir, standoff, maxSpeed, chase.accel, Dt,
                                FighterAI.MinForwardSpeed);
                            x += v * Dt;
                            if (FighterAI.EntryComplete(x, camX - HalfW, camX + HalfW)) { t = (i + 1) * Dt; break; }
                        }
                        Assert.GreaterOrEqual(t, 0f, $"M{mission} turn phase {phase:0.0}: plane 0 reaches the view");
                        worst = Mathf.Max(worst, t);
                        camV = Mathf.Lerp(scroll, -scroll, phase);
                    }
                    // Quiet rule: 2 s + entry. Mid-turn the fighter's accel (4) lags the camera's
                    // reversal, so allow 1 s (the B074 handoff claims 0.2 s for steady scrolls).
                    Assert.LessOrEqual(worst, 1.0f, $"M{mission} turn phase {phase:0.0}: entry delay {worst:0.00}s");
                }
            }
        }

        // ── Entry boost ends in view ─────────────────────────────────────

        [Test]
        public void EntryBoost_EndsInView_AndTheExtraSpeedBleedsOffAtTheChaseAccel() {
            var g = GameBalance.Get();
            float fighter = g.enemySpeed.fighter;
            var chase = FighterChaseData.Resolve(g);
            foreach (float scroll in new[] { 2.6f, 3.25f, 3.9f }) {
                float camV = -scroll, camX = 100f;
                float x = EnemyPressure.EntryX(camX, HalfW, EnemyPressure.EntryOffset(0, 1f));
                int dir = -1;
                float v = dir * (fighter + FighterAI.EntryBoost(dir, camV));
                bool entering = true;
                float enteredAt = -1f;
                for (int i = 0; i < 300; i++) {
                    camX += camV * Dt;
                    float px = camX + 0.4f * HalfW;
                    if (entering && FighterAI.EntryComplete(x, camX - HalfW, camX + HalfW)) {
                        entering = false;
                        enteredAt = i * Dt;
                    }
                    float maxSpeed = fighter + (entering ? FighterAI.EntryBoost(dir, camV) : 0f);
                    dir = FighterAI.DesiredFacing(dir, px - x, chase.turnDeadzone);
                    v = FighterAI.HorizontalVelocityStep(x, v, px, camV, dir, 3.5f, maxSpeed, chase.accel, Dt,
                        FighterAI.MinForwardSpeed);
                    x += v * Dt;
                    Assert.LessOrEqual(Mathf.Abs(v), fighter + scroll + Eps, "never above cruise + boost");
                    if (!entering) {
                        float since = i * Dt - enteredAt;
                        // After entry the top speed is cruise again; the boosted velocity only
                        // decays at chase.accel (HorizontalVelocityStep does not clamp it).
                        float bound = fighter + Mathf.Max(0f, scroll - chase.accel * since) + chase.accel * Dt + Eps;
                        Assert.LessOrEqual(Mathf.Abs(v), bound, $"scroll {scroll}: {since:0.00}s after entry");
                    }
                }
                Assert.GreaterOrEqual(enteredAt, 0f, $"scroll {scroll}: entered");
                Assert.LessOrEqual(enteredAt, 0.25f, $"scroll {scroll}: on screen at once");
                Assert.LessOrEqual(Mathf.Abs(v), fighter + Eps, $"scroll {scroll}: back to cruise top speed");
            }
        }

        // ── BUG-1: reinforcements pile up behind a left-flying player ────

        [Test]
        public void FlyingLeftThenTurningRight_NeverMoreThanMaxLiveFightersOnScreen() {
            var g = GameBalance.Get();
            var p = PressureData.Resolve(g);
            float fighter = g.enemySpeed.fighter;
            var chase = FighterChaseData.Resolve(g);
            float ease = g.player.turnEaseSeconds > 0f ? g.player.turnEaseSeconds : 0.6f;
            for (int mission = 1; mission <= 5; mission++) {
                float scroll = LoadMission(mission).scrollSpeed;
                int worst = SimulatePressure(scroll, 60f, 75f, fighter, chase, p, ease, mission);
                Assert.LessOrEqual(worst, p.maxLiveFighters,
                    $"M{mission} (scroll {scroll}): 60 s flying left, then a right turn brings {worst} fighters on screen at once");
            }
        }

        /// <summary>The player cruises left until `turnAt`, then right; nobody shoots. Returns the
        /// most fighters in view at once after the turn. Mirrors EnemyPressure.Update: quiet timer,
        /// WaveSize over CountsTowardCap, EntryX/EntryOffset + BeginEntry boost, IsEscapedOrLeft
        /// with DespawnBehindMargin for reinforcements.</summary>
        private static int SimulatePressure(float scroll, float turnAt, float total, float fighter,
                FighterChaseData chase, PressureData p, float ease, int seed) {
            var rng = new System.Random(seed);
            var xs = new List<float>();
            var vs = new List<float>();
            var dirs = new List<int>();
            var entering = new List<bool>();
            var standoffs = new List<float>();
            float camX = 0f, camV = -scroll, quiet = 0f;
            int worst = 0;
            int ticks = Mathf.RoundToInt(total / Dt);
            for (int i = 0; i < ticks; i++) {
                float t = i * Dt;
                float target = t >= turnAt ? scroll : -scroll;
                camV = Mathf.MoveTowards(camV, target, 2f * Mathf.Max(scroll, Mathf.Abs(camV)) / ease * Dt);
                camX += camV * Dt;
                float anchor = target > 0f ? 0.3f : 0.7f;
                float px = camX + (anchor - 0.5f) * 2f * HalfW;
                var view = new Rect(camX - HalfW, -5f, 2f * HalfW, 10f);

                for (int k = 0; k < xs.Count; k++) {
                    if (entering[k] && FighterAI.EntryComplete(xs[k], view.xMin, view.xMax)) entering[k] = false;
                    float maxSpeed = fighter + (entering[k] ? FighterAI.EntryBoost(dirs[k], camV) : 0f);
                    dirs[k] = FighterAI.DesiredFacing(dirs[k], px - xs[k], chase.turnDeadzone);
                    vs[k] = FighterAI.HorizontalVelocityStep(xs[k], vs[k], px, camV, dirs[k], standoffs[k], maxSpeed,
                        chase.accel, Dt, FighterAI.MinForwardSpeed);
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
                if (t >= turnAt + ease) worst = Mathf.Max(worst, inView);
                quiet = EnemyPressure.Step(quiet, Dt, true, true, inView > 0);
                if (!EnemyPressure.ShouldSpawn(quiet, p.maxQuietSeconds)) continue;
                int wave = EnemyPressure.WaveSize(p.fillerCount, p.maxLiveFighters, near);
                if (wave <= 0) continue;
                for (int w = 0; w < wave; w++) {
                    int dir = EnemyPressure.EntryFlightDirection;
                    xs.Add(EnemyPressure.EntryX(camX, HalfW, EnemyPressure.EntryOffset(w, (float)rng.NextDouble())));
                    vs.Add(dir * (fighter + FighterAI.EntryBoost(dir, camV)));
                    dirs.Add(dir);
                    entering.Add(true);
                    standoffs.Add(Mathf.Lerp(chase.standoff, chase.standoffMax, (float)rng.NextDouble()));
                }
                quiet = 0f;
            }
            return worst;
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private int CountLive() {
            int n = 0;
            foreach (var a in _runner.EnemyAircraft) if (a != null) n++;
            return n;
        }

        private Camera SetUpCameraAndScroll(float camX, int direction) {
            foreach (var c in Camera.allCameras) {
                if (c != null && c.CompareTag("MainCamera")) {
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
            }
            var camGo = new GameObject("T975_Camera");
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

            var runnerGo = new GameObject("T975_Runner");
            _runner = runnerGo.AddComponent<MissionRunner>();
            return cam;
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
