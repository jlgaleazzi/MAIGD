using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Missions;

namespace Biplanes.Tests {

    /// <summary>
    /// B035: GDD "Enemy pressure". The quiet timer and wave decision are pure helpers on
    /// EnemyPressure; PressureData.Resolve fills in defaults; reinforcements score like
    /// fighters but never count toward destroyAllOfType "fighter"; fighters take 3 bullets.
    /// </summary>
    public class EnemyPressureTests {

        private const float Eps = 1e-4f;

        // ── Quiet timer ──────────────────────────────────────────────────

        [Test]
        public void Step_CountsWhilePlayingAirborneAndQuiet() {
            float q = EnemyPressure.Step(0f, 0.5f, true, true, false);
            q = EnemyPressure.Step(q, 0.5f, true, true, false);
            Assert.AreEqual(1f, q, Eps);
        }

        [Test]
        public void Step_ResetsWhenAnEnemyIsVisible() {
            float q = EnemyPressure.Step(1.9f, 0.05f, true, true, true);
            Assert.AreEqual(0f, q, Eps);
        }

        [Test]
        public void Step_DoesNotCountOnRunway_OrCrashed_OrObjectivesDone() {
            Assert.IsFalse(EnemyPressure.IsEligible(true, true, false, true), "on the runway");
            Assert.IsFalse(EnemyPressure.IsEligible(true, false, true, true), "crashed");
            Assert.IsFalse(EnemyPressure.IsEligible(true, false, false, false), "objectives done");
            Assert.IsFalse(EnemyPressure.IsEligible(false, false, false, true), "no player");
            Assert.IsTrue(EnemyPressure.IsEligible(true, false, false, true), "airborne with open objectives");

            float q = 0f;
            for (int i = 0; i < 100; i++) {
                q = EnemyPressure.Step(q, 0.1f, true, EnemyPressure.IsEligible(true, true, false, true), false);
            }
            Assert.AreEqual(0f, q, Eps, "no quiet time builds up on the runway");
        }

        [Test]
        public void Step_HoldsWhileNotPlaying() {
            float q = EnemyPressure.Step(1.2f, 5f, false, true, false);
            Assert.AreEqual(1.2f, q, Eps, "pause / result screen: the timer neither runs nor resets");
            Assert.IsFalse(EnemyPressure.ShouldSpawn(q, 2f));
        }

        [Test]
        public void ShouldSpawn_FiresAtTheLimit() {
            float q = 0f;
            int ticks = 0;
            while (!EnemyPressure.ShouldSpawn(q, 2f) && ticks < 1000) {
                q = EnemyPressure.Step(q, 0.02f, true, true, false);
                ticks++;
            }
            Assert.That(ticks, Is.InRange(99, 101), "fires after about 2 s of quiet");
            Assert.IsFalse(EnemyPressure.ShouldSpawn(1.99f, 2f));
            Assert.IsTrue(EnemyPressure.ShouldSpawn(2f, 2f));
        }

        // ── Wave size and cap ────────────────────────────────────────────

        [Test]
        public void WaveSize_RespectsTheCap() {
            Assert.AreEqual(2, EnemyPressure.WaveSize(2, 6, 0));
            Assert.AreEqual(2, EnemyPressure.WaveSize(2, 6, 4));
            Assert.AreEqual(1, EnemyPressure.WaveSize(2, 6, 5), "trimmed to fit under the cap");
            Assert.AreEqual(0, EnemyPressure.WaveSize(2, 6, 6), "at the cap: wait");
            Assert.AreEqual(0, EnemyPressure.WaveSize(2, 6, 9));
        }

        [Test]
        public void SliceHeight_SpreadsAWaveOverTheBand() {
            float a = EnemyPressure.SliceHeight(0f, 6f, 0, 2, 0.5f);
            float b = EnemyPressure.SliceHeight(0f, 6f, 1, 2, 0.5f);
            Assert.AreEqual(1.5f, a, Eps);
            Assert.AreEqual(4.5f, b, Eps);
            for (int i = 0; i < 3; i++) {
                float y0 = EnemyPressure.SliceHeight(-2f, 4f, i, 3, 0f);
                float y1 = EnemyPressure.SliceHeight(-2f, 4f, i, 3, 1f);
                Assert.That(y0, Is.InRange(-2f, 4f));
                Assert.That(y1, Is.InRange(-2f, 4f));
            }
            Assert.AreEqual(3f, EnemyPressure.SliceHeight(3f, 1f, 0, 2, 0.7f), Eps, "inverted band collapses to low");
        }

        [Test]
        public void InView_AndIsNear() {
            var view = new Rect(-8.9f, -5f, 17.8f, 10f);
            Assert.IsTrue(EnemyPressure.InView(new Vector2(0f, 0f), 0.75f, 0.5f, view));
            Assert.IsTrue(EnemyPressure.InView(new Vector2(9.5f, 0f), 0.75f, 0.5f, view), "wing tip over the edge");
            Assert.IsFalse(EnemyPressure.InView(new Vector2(10.4f, 0f), 0.75f, 0.5f, view), "entry point past the edge");
            Assert.IsTrue(EnemyPressure.InView(new Vector2(-12f, 0f), 4.5f, 0.5f, view), "long zeppelin hull overlaps");
            Assert.IsTrue(EnemyPressure.IsNear(20f, view, 17.8f));
            Assert.IsFalse(EnemyPressure.IsNear(-40f, view, 17.8f));
        }

        // ── Balance ──────────────────────────────────────────────────────

        [Test]
        public void PressureData_Resolve_DefaultsAndDisable() {
            var d = PressureData.Resolve(null);
            Assert.IsTrue(d.enabled);
            Assert.AreEqual(2f, d.maxQuietSeconds, Eps);
            Assert.AreEqual(2, d.fillerCount);
            Assert.AreEqual(6, d.maxLiveFighters);

            var g = new GameBalance { pressure = new PressureData { enabled = false, maxQuietSeconds = -1f } };
            var r = PressureData.Resolve(g);
            Assert.IsFalse(r.enabled, "enabled=false turns it off");
            Assert.AreEqual(PressureData.DefaultMaxQuietSeconds, r.maxQuietSeconds, Eps);

            var fromJson = JsonUtility.FromJson<PressureData>("{\"maxQuietSeconds\": 3.0}");
            Assert.IsTrue(fromJson.enabled, "a missing enabled key stays true");
        }

        [Test]
        public void Balance_PressureBlock_AndThreeHitFighters() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.enemyHealth != null && g.player != null, "Balance.json not loadable");
            var p = PressureData.Resolve(g);
            Assert.IsTrue(p.enabled);
            Assert.AreEqual(2f, p.maxQuietSeconds, Eps);
            Assert.AreEqual(2, p.fillerCount);
            Assert.AreEqual(6, p.maxLiveFighters);
            Assert.AreEqual(3, Mathf.CeilToInt(g.enemyHealth.fighter / g.player.bulletDamage - Eps),
                "a fighter takes exactly 3 player bullets");
        }

        [Test]
        public void Fighter_SurvivesTwoBullets_DiesOnTheThird() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.enemyHealth != null && g.player != null, "Balance.json not loadable");
            var go = new GameObject("fighter");
            try {
                go.AddComponent<TeamTag>().team = Team.Enemy;
                var h = go.AddComponent<Health>();
                h.SetMaxHitPoints(g.enemyHealth.fighter);
                h.ApplyDamage(g.player.bulletDamage, DamageSource.Bullet);
                h.ApplyDamage(g.player.bulletDamage, DamageSource.Bullet);
                Assert.IsFalse(h.IsDead, "two bullets");
                h.ApplyDamage(g.player.bulletDamage, DamageSource.Bullet);
                Assert.IsTrue(h.IsDead, "three bullets");
            } finally {
                Object.DestroyImmediate(go);
            }
        }

        // ── Reinforcement type key ───────────────────────────────────────

        [Test]
        public void Reinforcement_ScoresLikeAFighter_AndCountsForDestroyAll() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.score != null, "Balance.json not loadable");

            var skGo = new GameObject("ScoreKeeper");
            var trGo = new GameObject("Tracker");
            try {
                var sk = skGo.AddComponent<ScoreKeeper>();
                int before = sk.CurrentScore;
                sk.OnEntityDestroyed(ScoreKeeper.ReinforcementFighterKey, Team.Enemy);
                Assert.AreEqual(before + g.score.fighter, sk.CurrentScore);

                var tracker = trGo.AddComponent<ObjectiveTracker>();
                var obj = new ObjectiveEntry { id = "o1", type = "destroyAllOfType", targetType = "fighter" };
                var spawn = new SpawnEntry { type = "fighter", count = 1 };
                tracker.Init(new[] { obj }, 0f, new[] { spawn });
                // B050 (GDD "Enemy pressure"): a reinforcement kill counts as a fighter kill;
                // the required number stays the JSON total (1), so the first one completes it.
                int completions = 0;
                System.Action<string> onDone = _ => completions++;
                GameEvents.ObjectiveCompleted += onDone;
                try {
                    tracker.OnEntityDestroyed(ScoreKeeper.ReinforcementFighterKey, Team.Enemy);
                    Assert.IsTrue(tracker.AllComplete, "a reinforcement kill counts toward destroyAllOfType fighter");
                    tracker.OnEntityDestroyed(ScoreKeeper.ReinforcementFighterKey, Team.Enemy);
                    tracker.OnEntityDestroyed("fighter", Team.Enemy);
                    Assert.AreEqual(1, completions, "completed once; later kills raise nothing");
                } finally {
                    GameEvents.ObjectiveCompleted -= onDone;
                }
            } finally {
                Object.DestroyImmediate(skGo);
                Object.DestroyImmediate(trGo);
            }
        }
    }
}
