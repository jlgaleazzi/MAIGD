using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Missions;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// B080: the zeppelin defense wave (when the last turret dies, zeppelinDefense.waveCount
    /// escort fighters of that zeppelin enter from the right, queued under the fighter cap) and
    /// the hull's death sequence (DeathBurstPlan: staggered bursts along the envelope, tail and
    /// gondolas while the hull sinks and fades, then removal; it must fit the B069 win delay).
    ///
    /// EditMode doesn't run Awake on AddComponent: tests invoke it by reflection where Balance
    /// values matter, and drive internals (NotifyTurretDestroyed, StepDeath, the runner's
    /// ShieldDropped handler) by reflection. No FxManager exists outside Play mode, so the
    /// bursts are counted but not drawn.
    /// </summary>
    public class ZeppelinDefenseTests {

        private const float Eps = 1e-4f;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

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

        // ── Balance ──────────────────────────────────────────────────────

        [Test]
        public void Balance_HasDefenseAndExplosionBlocks_WithSaneValues() {
            var g = GameBalance.Get();
            Assert.IsNotNull(g, "Balance.json loads");
            Assert.IsNotNull(g.zeppelinDefense, "Balance.json has zeppelinDefense");
            Assert.IsNotNull(g.zeppelinExplosion, "Balance.json has zeppelinExplosion");
            var d = ZeppelinDefenseData.Resolve(g);
            Assert.IsTrue(d.enabled);
            Assert.AreEqual(4, d.waveCount);
            Assert.Greater(d.staggerSeconds, 0f);
            var e = ZeppelinExplosionData.Resolve(g);
            Assert.GreaterOrEqual(e.burstCount, 10, "10+ bursts along the hull");
            Assert.That(e.tailBurstCount, Is.InRange(2, 3), "2-3 bursts on the tail / gondolas");
            Assert.That(e.duration, Is.InRange(1.5f, 2f), "the hull stays about 1.5-2 s");
        }

        [Test]
        public void Resolve_MissingBlocks_GiveDefaults() {
            var d = ZeppelinDefenseData.Resolve(null);
            Assert.IsTrue(d.enabled);
            Assert.AreEqual(ZeppelinDefenseData.DefaultWaveCount, d.waveCount);
            Assert.AreEqual(ZeppelinDefenseData.DefaultStaggerSeconds, d.staggerSeconds, Eps);
            var e = ZeppelinExplosionData.Resolve(new GameBalance());
            Assert.AreEqual(ZeppelinExplosionData.DefaultBurstCount, e.burstCount);
            Assert.AreEqual(ZeppelinExplosionData.DefaultTailBurstCount, e.tailBurstCount);
            Assert.AreEqual(ZeppelinExplosionData.DefaultDuration, e.duration, Eps);
            Assert.LessOrEqual(e.fadeSeconds, e.duration);
        }

        [Test]
        public void DeathSequence_FitsInsideTheWinDelay() {
            var g = GameBalance.Get();
            float seq = ZeppelinAI.DeathSequenceSeconds(ZeppelinExplosionData.Resolve(g));
            Assert.LessOrEqual(seq, GameManager.KillWinDelayOrDefault(g),
                "the result screen must open only after the last burst has played (B069 delay)");
            Assert.LessOrEqual(ZeppelinAI.DeathSequenceSeconds(null), GameManager.DefaultKillWinDelaySeconds);
        }

        // ── Shield drop: trigger once ────────────────────────────────────

        [Test]
        public void ShouldRaiseShieldDropped_Rule() {
            Assert.IsTrue(ZeppelinAI.ShouldRaiseShieldDropped(false, false, 3, 0));
            Assert.IsFalse(ZeppelinAI.ShouldRaiseShieldDropped(false, false, 3, 1), "a turret is left");
            Assert.IsFalse(ZeppelinAI.ShouldRaiseShieldDropped(true, false, 3, 0), "already raised");
            Assert.IsFalse(ZeppelinAI.ShouldRaiseShieldDropped(false, true, 3, 0), "hull dead or despawned");
            Assert.IsFalse(ZeppelinAI.ShouldRaiseShieldDropped(false, false, 0, 0), "built without turrets");
        }

        [Test]
        public void ShieldDropped_RaisedOnce_WhenTheLastTurretDies() {
            var z = SpawnZeppelin(new Vector3(60f, 1f, 0f), "airship_t");
            var turrets = z.GetComponentsInChildren<ZeppelinTurret>();
            Assert.AreEqual(3, turrets.Length);
            int raised = 0;
            ZeppelinAI from = null;
            Action<ZeppelinAI> h = zz => { raised++; from = zz; };
            ZeppelinAI.ShieldDropped += h;
            try {
                NotifyTurret(z, turrets[0]);
                NotifyTurret(z, turrets[1]);
                Assert.AreEqual(0, raised, "two turrets down: still shielded, no wave");
                NotifyTurret(z, turrets[2]);
                Assert.AreEqual(1, raised, "the last turret raises it");
                Assert.AreSame(z, from);
                Assert.IsFalse(z.IsShielded);
                NotifyTurret(z, turrets[2]);   // a repeat report
                Assert.AreEqual(1, raised, "never again for this zeppelin");
            } finally {
                ZeppelinAI.ShieldDropped -= h;
            }
        }

        [Test]
        public void ShieldDropped_NotRaised_AfterTheHullDied() {
            var z = SpawnZeppelin(new Vector3(60f, 1f, 0f), "airship_t");
            var turrets = z.GetComponentsInChildren<ZeppelinTurret>();
            NotifyTurret(z, turrets[0]);
            NotifyTurret(z, turrets[1]);
            typeof(ZeppelinAI).GetField("_destroyed", Private).SetValue(z, true);   // dead / despawned
            int raised = 0;
            Action<ZeppelinAI> h = zz => raised++;
            ZeppelinAI.ShieldDropped += h;
            try {
                NotifyTurret(z, turrets[2]);
                Assert.AreEqual(0, raised);
            } finally {
                ZeppelinAI.ShieldDropped -= h;
            }
        }

        // ── Runner hook and escort tagging ───────────────────────────────

        [Test]
        public void Runner_QueuesOneWave_PerZeppelin_WhenItsShieldDrops() {
            SetUpCameraAndScroll(300f);
            SetMissionLoaded();
            _runner.SpawnEntities(new SpawnEntry { type = "zeppelin", atX = 310, y = 4f, count = 1, id = "airship_t" });
            var z = FindZeppelin();
            Assert.IsNotNull(z);

            // The runner subscribes in StartMission; here the handler is called as the event would.
            var handler = typeof(MissionRunner).GetMethod("OnZeppelinShieldDropped", Private);
            Assert.IsNotNull(handler);
            handler.Invoke(_runner, new object[] { z });
            Assert.AreEqual(1, _runner.DefenseWaves.Count);
            Assert.AreEqual(ZeppelinDefenseData.Resolve(GameBalance.Get()).waveCount, _runner.DefenseWaves[0].Total);
            Assert.AreSame(z, _runner.DefenseWaves[0].Target);
            handler.Invoke(_runner, new object[] { z });
            Assert.AreEqual(1, _runner.DefenseWaves.Count, "once per zeppelin");

            // A zeppelin this runner didn't spawn gets nothing.
            var stranger = SpawnZeppelin(new Vector3(320f, 1f, 0f), "other");
            handler.Invoke(_runner, new object[] { stranger });
            Assert.AreEqual(1, _runner.DefenseWaves.Count);
        }

        [Test]
        public void Defender_IsAnEscortOfTheZeppelin_EnteringFromTheRight_CountedAsReinforcement() {
            Camera cam = SetUpCameraAndScroll(300f);
            Rect view = EnemyPressure.ViewRect(cam);
            var z = SpawnZeppelin(new Vector3(305f, 2f, 0f), "airship_t");

            var f = _runner.SpawnZeppelinDefender(z, 1, 4);
            Assert.IsNotNull(f);
            Assert.AreEqual(ScoreKeeper.ReinforcementFighterKey, f.gameObject.name,
                "scores like a fighter and counts for destroy-all without raising the required total");
            Assert.AreEqual(MissionRunner.ZeppelinDefensePattern, f.Pattern);
            Assert.AreEqual("escort", f.Pattern);
            Assert.AreEqual("airship_t", (string)typeof(FighterAI).GetField("escortOf", Private).GetValue(f),
                "escortOf = the zeppelin's id, so the FighterAI formation slots apply");
            Assert.IsTrue(FighterAI.IsEscortTarget(z.gameObject, "airship_t"));
            Assert.IsTrue(EnemyPressure.EntersFromRight(f.transform.position.x, EnemyPressure.FighterHalfWidth, view));
            Assert.AreEqual(EnemyPressure.EntryFlightDirection, f.FlightDirection);
            Assert.IsTrue(f.IsEntering, "with the entry boost");
            bool registered = false;
            foreach (var a in _runner.EnemyAircraft) registered |= a == f;
            Assert.IsTrue(registered, "registered for EnemyPressure, the minimap and teardown");
        }

        [Test]
        public void Defender_NotSpawned_ForADeadZeppelin() {
            SetUpCameraAndScroll(300f);
            var z = SpawnZeppelin(new Vector3(305f, 2f, 0f), "airship_t");
            typeof(ZeppelinAI).GetField("_damagePercent", Private).SetValue(z, 100f);
            Assert.IsTrue(z.IsDead);
            Assert.IsNull(_runner.SpawnZeppelinDefender(z, 0, 4));
            Assert.IsNull(_runner.SpawnZeppelinDefender(null, 0, 4));
        }

        // ── Cap queue (pure) ─────────────────────────────────────────────

        [Test]
        public void Wave_Staggered_OneAtATime() {
            var w = new MissionRunner.DefenseWave(null, 4, 0.5f, 10f);
            Assert.AreEqual(4, w.Pending);
            Assert.IsTrue(w.TryTake(10f, true, 0, 6));
            Assert.IsFalse(w.TryTake(10.2f, true, 0, 6), "within the stagger");
            Assert.IsTrue(w.TryTake(10.5f, true, 0, 6));
            Assert.AreEqual(2, w.NextIndex);
            Assert.IsTrue(w.TryTake(11f, true, 0, 6));
            Assert.IsTrue(w.TryTake(11.5f, true, 0, 6));
            Assert.IsTrue(w.Done);
            Assert.IsFalse(w.TryTake(20f, true, 0, 6), "the wave never sends more than its count");
        }

        [Test]
        public void Wave_AtTheCap_StaysQueued_AndSendsAsSlotsFree() {
            var w = new MissionRunner.DefenseWave(null, 4, 0.5f, 0f);
            Assert.IsTrue(w.TryTake(0f, true, 4, 6));
            Assert.IsTrue(w.TryTake(0.5f, true, 5, 6));
            Assert.IsFalse(w.TryTake(1f, true, 6, 6), "at the cap: wait");
            Assert.IsFalse(w.TryTake(5f, true, 7, 6), "over the cap: wait");
            Assert.AreEqual(2, w.Pending, "the rest stay queued");
            Assert.IsTrue(w.TryTake(6f, true, 5, 6), "a slot freed");
            Assert.IsFalse(w.TryTake(6f, true, 5, 6), "next one waits for the stagger");
            Assert.IsTrue(w.TryTake(6.5f, true, 4, 6));
            Assert.IsTrue(w.Done);
        }

        [Test]
        public void Wave_Dropped_OnceTheZeppelinIsGone() {
            var w = new MissionRunner.DefenseWave(null, 4, 0.5f, 0f);
            Assert.IsTrue(w.TryTake(0f, true, 0, 6));
            Assert.IsFalse(w.TryTake(1f, false, 0, 6), "the zeppelin died: nothing more spawns");
            Assert.IsTrue(w.Done);
            Assert.AreEqual(0, w.Pending);
        }

        [Test]
        public void Wave_ZeroOrNegativeCount_IsEmpty() {
            Assert.IsTrue(new MissionRunner.DefenseWave(null, 0, 0.5f, 0f).Done);
            Assert.IsTrue(new MissionRunner.DefenseWave(null, -3, 0.5f, 0f).Done);
        }

        // ── Explosion plan (pure) ────────────────────────────────────────

        [Test]
        public void Plan_CountsTimesAndSounds() {
            var plan = ZeppelinAI.DeathBurstPlan(12, 3, 3.4f, 1.8f);
            Assert.AreEqual(15, plan.Length);
            Assert.AreEqual(0f, plan[0].Time, Eps, "the first burst plays at the killing hit");
            Assert.IsTrue(plan[0].PlaySound, "a sound layers on the ExplosionBig cue");
            for (int i = 1; i < plan.Length; i++) {
                Assert.GreaterOrEqual(plan[i].Time, plan[i - 1].Time, "sorted by time");
            }
            Assert.AreEqual(1.8f, plan[plan.Length - 1].Time, Eps, "the last at the end of the duration");
            int sounds = 0;
            foreach (var b in plan) if (b.PlaySound) sounds++;
            Assert.AreEqual(4, sounds, "envelope bursts 0, 3, 6, 9");

            // No two bursts at the same instant for the default plan: a real stagger.
            for (int i = 1; i < plan.Length; i++) {
                Assert.Greater(plan[i].Time - plan[i - 1].Time, 0.01f);
            }
        }

        [Test]
        public void Plan_CoversTheWholeHull_AndTheTailAndGondolas() {
            float spread = 3.4f;
            var plan = ZeppelinAI.DeathBurstPlan(12, 3, spread, 1.8f);
            // Envelope centre (art facing right): x 0.575, y 0.10 (sprite geometry).
            float cx = 0.575f;
            float minX = float.MaxValue, maxX = float.MinValue;
            int aft = 0, fore = 0, tailish = 0;
            foreach (var b in plan) {
                minX = Mathf.Min(minX, b.Offset.x);
                maxX = Mathf.Max(maxX, b.Offset.x);
                if (b.Offset.x < cx - 0.1f) aft++;
                if (b.Offset.x > cx + 0.1f) fore++;
                if (b.Offset.y < -0.6f || b.Offset.x < -3f) tailish++;
                Assert.LessOrEqual(Mathf.Abs(b.Offset.x), 4.5f, "inside the 9-unit hull");
                Assert.LessOrEqual(Mathf.Abs(b.Offset.y), 1.1f, "inside the hull's height");
            }
            Assert.LessOrEqual(minX, cx - spread + Eps, "reaches the aft end");
            Assert.GreaterOrEqual(maxX, cx + spread - Eps, "reaches the nose end");
            Assert.GreaterOrEqual(aft, 5);
            Assert.GreaterOrEqual(fore, 5);
            Assert.GreaterOrEqual(tailish, 3, "tail and gondola bursts");
        }

        [Test]
        public void Plan_Degenerate_Inputs_AreSafe() {
            var one = ZeppelinAI.DeathBurstPlan(0, -1, -2f, -1f);
            Assert.AreEqual(1, one.Length);
            Assert.AreEqual(0f, one[0].Time, Eps);
            Assert.AreEqual(12 + 4, ZeppelinAI.DeathBurstPlan(12, 99, 3f, 2f).Length, "tail bursts capped at 4 points");
        }

        [Test]
        public void DeathAlpha_FadesOverTheLastSeconds() {
            Assert.AreEqual(1f, ZeppelinAI.DeathAlpha(0f, 1.8f, 0.4f), Eps);
            Assert.AreEqual(1f, ZeppelinAI.DeathAlpha(1.4f, 1.8f, 0.4f), Eps);
            Assert.AreEqual(0.5f, ZeppelinAI.DeathAlpha(1.6f, 1.8f, 0.4f), Eps);
            Assert.AreEqual(0f, ZeppelinAI.DeathAlpha(1.8f, 1.8f, 0.4f), Eps);
            Assert.AreEqual(1f, ZeppelinAI.DeathAlpha(1f, 1.8f, 0f), Eps);
            Assert.AreEqual(0f, ZeppelinAI.DeathAlpha(1.8f, 1.8f, 0f), Eps);
        }

        // ── Death sequence on a live hull ────────────────────────────────

        [Test]
        public void HullDeath_CountsAtTheHit_ThenPlaysAndRemovesItself() {
            var z = SpawnZeppelin(new Vector3(60f, 1f, 0f), "airship_t");
            typeof(ZeppelinAI).GetField("_hullVulnerableEarly", Private).SetValue(z, true);
            var destroyed = new List<string>();
            Action<string, Team> onDestroyed = (type, team) => destroyed.Add(type);
            GameEvents.EntityDestroyed += onDestroyed;
            try {
                z.ApplyDamage(100000f, DamageSource.Bomb);
                Assert.IsTrue(z.IsDead);
                Assert.IsTrue(z.IsDying, "the death sequence runs");
                Assert.AreEqual(1, destroyed.FindAll(s => s == ZeppelinAI.ScoreKey).Count, "the kill counts at the hit");
                Assert.AreEqual(0, z.GetComponentsInChildren<ZeppelinTurret>().Length, "turrets left were removed");
                foreach (var col in z.GetComponents<Collider2D>()) Assert.IsFalse(col.enabled, "no colliders on the visual");
                Assert.IsFalse(z.GetComponent<Rigidbody2D>().simulated);

                var data = ZeppelinExplosionData.Resolve(GameBalance.Get());
                int planned = ZeppelinAI.DeathBurstPlan(data.burstCount, data.tailBurstCount, data.spread, data.duration).Length;
                var step = typeof(ZeppelinAI).GetMethod("StepDeath", Private);
                var fired = typeof(ZeppelinAI).GetProperty("DeathBurstsFired", Private);
                Assert.AreEqual(1, (int)fired.GetValue(z), "the first burst at the hit");
                Assert.IsFalse((bool)step.Invoke(z, new object[] { 0f }), "paused: nothing happens");

                float startY = z.transform.position.y;
                float t = 0f;
                bool done = false;
                const float dt = 0.05f;
                for (int guard = 0; !done && guard < 1000; guard++) {
                    done = (bool)step.Invoke(z, new object[] { dt });
                    t += dt;
                    if (!done && t > 0.5f) Assert.Less(z.transform.position.y, startY, "the hull sinks");
                }
                Assert.IsTrue(done, "the hull is removed at the end");
                Assert.That(t, Is.InRange(data.duration - Eps, data.duration + dt + Eps));
                Assert.AreEqual(planned, (int)fired.GetValue(z), "every planned burst played");
                Assert.IsTrue(z == null, "removed (DestroyImmediate in EditMode)");
                Assert.AreEqual(1, destroyed.FindAll(s => s == ZeppelinAI.ScoreKey).Count, "still one kill");
            } finally {
                GameEvents.EntityDestroyed -= onDestroyed;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private ZeppelinAI SpawnZeppelin(Vector3 pos, string id) {
            var z = ZeppelinAI.Spawn(pos, id);
            _created.Add(z.gameObject);
            var awake = typeof(ZeppelinAI).GetMethod("Awake", Private);
            awake.Invoke(z, null);
            return z;
        }

        private static void NotifyTurret(ZeppelinAI z, ZeppelinTurret t) {
            var m = typeof(ZeppelinAI).GetMethod("NotifyTurretDestroyed", Private);
            Assert.IsNotNull(m);
            m.Invoke(z, new object[] { t });
        }

        private ZeppelinAI FindZeppelin() {
            foreach (var a in _runner.EnemyAircraft) {
                if (a is ZeppelinAI z) return z;
            }
            return null;
        }

        /// <summary>The ShieldDropped handler only acts during a mission: give the bare runner
        /// an empty MissionData.</summary>
        private void SetMissionLoaded() {
            typeof(MissionRunner).GetField("_missionData", Private).SetValue(_runner,
                new MissionData { spawns = new SpawnEntry[0], objectives = new ObjectiveEntry[0] });
        }

        /// <summary>A test MainCamera at x `camX` (16:9, size 5), a ScrollController scrolling
        /// right, and a MissionRunner (no mission loaded).</summary>
        private Camera SetUpCameraAndScroll(float camX) {
            foreach (var c in Camera.allCameras) {
                if (c != null && c.CompareTag("MainCamera")) {
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
            }
            var camGo = new GameObject("T_B080_Camera");
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
            sc.SetDirection(1);

            var runnerGo = new GameObject("T_B080_Runner");
            _runner = runnerGo.AddComponent<MissionRunner>();
            return cam;
        }
    }
}
