using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;

namespace Biplanes.Tests {

    /// <summary>
    /// B068: a far tougher bomber (enemyHealth.bomber 1000), a smoke trail that grows with
    /// damage, and a falling wreck after the kill.
    ///
    /// EditMode doesn't run Awake/Start/Update/FixedUpdate, so the tests invoke the private
    /// Awake methods and the internal BeginCrash / BomberWreck.Step by reflection. No
    /// FxManager exists outside Play mode (GetOrNull returns null), so no effect is spawned.
    /// </summary>
    public class BomberDamageTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<string> _destroyed = new List<string>();

        [SetUp]
        public void SetUp() {
            _destroyed.Clear();
            GameEvents.EntityDestroyed += OnEntityDestroyed;
        }

        [TearDown]
        public void TearDown() {
            GameEvents.EntityDestroyed -= OnEntityDestroyed;
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
            BomberWreck.ClearAll();
        }

        private void OnEntityDestroyed(string type, Team team) {
            if (team == Team.Enemy) _destroyed.Add(type);
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static void InvokeAwake(Component c) {
            var awake = c.GetType().GetMethod("Awake", Private);
            Assert.IsNotNull(awake, $"{c.GetType().Name}.Awake not found by reflection");
            awake.Invoke(c, null);
        }

        private BomberAI MakeBomber(Vector3 position) {
            var b = BomberAI.Spawn(position, "bomber_1");
            _created.Add(b.gameObject);
            InvokeAwake(b.GetComponent<Health>());
            InvokeAwake(b);
            return b;
        }

        private static BomberWreck BeginCrash(BomberAI b) {
            var m = typeof(BomberAI).GetMethod("BeginCrash", Private);
            Assert.IsNotNull(m, "BomberAI.BeginCrash not found by reflection");
            return (BomberWreck)m.Invoke(b, null);
        }

        private static bool Step(BomberWreck w, float dt) {
            var m = typeof(BomberWreck).GetMethod("Step", Private);
            Assert.IsNotNull(m, "BomberWreck.Step not found by reflection");
            return (bool)m.Invoke(w, new object[] { dt });
        }

        private static float BulletDamage => GameBalance.Get().player.bulletDamage;
        private static float BombDamage => GameBalance.Get().player.bombDamage;

        private static void Shoot(BomberAI b, int bullets) {
            var h = b.GetComponent<Health>();
            for (int i = 0; i < bullets; i++) h.ApplyDamage(BulletDamage, DamageSource.Bullet);
        }

        // ── Toughness ───────────────────────────────────────────────────

        [Test]
        public void Balance_BomberHealthIs1000_AndFallbackAgrees() {
            var balance = GameBalance.Get();
            Assert.IsNotNull(balance);
            Assert.AreEqual(1000f, balance.enemyHealth.bomber);
            Assert.AreEqual(1000f, BomberAI.FallbackHitPoints);
            Assert.AreEqual(25f, balance.player.bulletDamage, "40 bullets assumes bulletDamage 25");
            Assert.AreEqual(100f, balance.player.bombDamage, "10 bombs assumes bombDamage 100");
        }

        [Test]
        public void Bomber_AwakeSetsHitPointsFromBalance() {
            var b = MakeBomber(new Vector3(50f, 2f, 0f));
            Assert.AreEqual(1000f, b.GetComponent<Health>().MaxHitPoints);
        }

        [Test]
        public void Bomber_Survives39Bullets_DiesOnThe40th() {
            var b = MakeBomber(new Vector3(50f, 2f, 0f));
            var h = b.GetComponent<Health>();
            Shoot(b, 39);
            Assert.IsFalse(h.IsDead, "39 bullets leave the bomber flying");
            Shoot(b, 1);
            Assert.IsTrue(h.IsDead, "the 40th bullet kills it");
        }

        [Test]
        public void Bomber_Survives9Bombs_DiesOnThe10th() {
            var b = MakeBomber(new Vector3(50f, 2f, 0f));
            var h = b.GetComponent<Health>();
            for (int i = 0; i < 9; i++) h.ApplyDamage(BombDamage, DamageSource.Bomb);
            Assert.IsFalse(h.IsDead, "9 bombs leave the bomber flying");
            h.ApplyDamage(BombDamage, DamageSource.Bomb);
            Assert.IsTrue(h.IsDead, "the 10th bomb kills it");
        }

        // ── Smoke trail ─────────────────────────────────────────────────

        [Test]
        public void SmokeLevelFor_MapsHealthFraction() {
            Assert.AreEqual(BomberAI.SmokeLevelNone, BomberAI.SmokeLevelFor(1f));
            Assert.AreEqual(BomberAI.SmokeLevelNone, BomberAI.SmokeLevelFor(0.71f));
            Assert.AreEqual(BomberAI.SmokeLevelLight, BomberAI.SmokeLevelFor(BomberAI.SmokeLightAt));
            Assert.AreEqual(BomberAI.SmokeLevelLight, BomberAI.SmokeLevelFor(0.41f));
            Assert.AreEqual(BomberAI.SmokeLevelThick, BomberAI.SmokeLevelFor(BomberAI.SmokeThickAt));
            Assert.AreEqual(BomberAI.SmokeLevelThick, BomberAI.SmokeLevelFor(0.16f));
            Assert.AreEqual(BomberAI.SmokeLevelHeavy, BomberAI.SmokeLevelFor(BomberAI.SmokeHeavyAt));
            Assert.AreEqual(BomberAI.SmokeLevelHeavy, BomberAI.SmokeLevelFor(0f));
        }

        [Test]
        public void SmokeInterval_NoneIsZero_HeavierIsDenser() {
            Assert.AreEqual(0f, BomberAI.SmokeInterval(BomberAI.SmokeLevelNone));
            float light = BomberAI.SmokeInterval(BomberAI.SmokeLevelLight);
            float thick = BomberAI.SmokeInterval(BomberAI.SmokeLevelThick);
            float heavy = BomberAI.SmokeInterval(BomberAI.SmokeLevelHeavy);
            Assert.Greater(heavy, 0f);
            Assert.Greater(thick, heavy);
            Assert.Greater(light, thick);
        }

        [Test]
        public void HealthFractionFrom_IsRemainingShare() {
            Assert.AreEqual(1f, BomberAI.HealthFractionFrom(0f), 1e-6f);
            Assert.AreEqual(0.25f, BomberAI.HealthFractionFrom(75f), 1e-6f);
            Assert.AreEqual(0f, BomberAI.HealthFractionFrom(100f), 1e-6f);
            Assert.AreEqual(0f, BomberAI.HealthFractionFrom(150f), 1e-6f);
        }

        [Test]
        public void Bomber_SmokeLevelGrowsWithHits() {
            var b = MakeBomber(new Vector3(50f, 2f, 0f));
            Assert.AreEqual(BomberAI.SmokeLevelNone, b.SmokeLevel, "undamaged: no smoke");
            Shoot(b, 11); // 72.5% left
            Assert.AreEqual(BomberAI.SmokeLevelNone, b.SmokeLevel, "above 70%: no smoke");
            Shoot(b, 2);  // 67.5% left
            Assert.AreEqual(BomberAI.SmokeLevelLight, b.SmokeLevel);
            Shoot(b, 12); // 37.5% left
            Assert.AreEqual(BomberAI.SmokeLevelThick, b.SmokeLevel);
            Shoot(b, 10); // 12.5% left
            Assert.AreEqual(BomberAI.SmokeLevelHeavy, b.SmokeLevel);
            Shoot(b, 5);  // dead
            Assert.AreEqual(BomberAI.SmokeLevelNone, b.SmokeLevel, "a dead bomber's wreck smokes, not the bomber");
        }

        [Test]
        public void EngineSmokePoint_IsAheadAlongTheNose() {
            Vector3 c = new Vector3(10f, 3f, 0f);
            Vector3 left = BomberAI.EngineSmokePoint(c, 0f, -1);
            Assert.AreEqual(10f - BomberAI.EngineSmokeOffset, left.x, 1e-5f, "a left-flying bomber's engine is on the left");
            Assert.AreEqual(3f, left.y, 1e-5f);
            Vector3 diving = BomberAI.EngineSmokePoint(c, -30f, -1);
            Assert.Less(diving.y, 3f, "nose down: the engine is lower than the centre");
        }

        // ── Death and wreck ─────────────────────────────────────────────

        [Test]
        public void Kill_CountsAtTheHit_BeforeTheWreck() {
            var b = MakeBomber(new Vector3(50f, 2f, 0f));
            Shoot(b, 40);
            CollectionAssert.AreEqual(new[] { "bomber" }, _destroyed, "EntityDestroyed at the killing hit");

            var wreck = BeginCrash(b);
            Assert.IsNotNull(wreck);
            _created.Add(wreck.gameObject);
            CollectionAssert.AreEqual(new[] { "bomber" }, _destroyed, "the crash never raises a second kill");
        }

        [Test]
        public void BeginCrash_ReplacesTheBomberWithAVisualOnlyWreck() {
            var b = MakeBomber(new Vector3(50f, 2f, 0f));
            var bomberGo = b.gameObject;
            // Start sets flipX (FlipsToFlyLeft); EditMode never runs it (T971 fix).
            var start = typeof(BomberAI).GetMethod("Start", Private);
            Assert.IsNotNull(start, "BomberAI.Start not found by reflection");
            start.Invoke(b, null);
            Shoot(b, 40);

            var wreck = BeginCrash(b);
            Assert.IsNotNull(wreck);
            _created.Add(wreck.gameObject);

            Assert.IsTrue(bomberGo == null, "the dead bomber is removed at once");
            Assert.AreEqual(1, BomberWreck.LiveCount);
            Assert.AreEqual(new Vector3(50f, 2f, 0f), wreck.transform.position);
            Assert.IsNull(wreck.GetComponent<Collider2D>(), "the wreck can't be hit or ram the player");
            Assert.IsNull(wreck.GetComponent<Health>());
            Assert.IsNull(wreck.GetComponent<TeamTag>());
            Assert.IsNull(wreck.GetComponent<BomberAI>(), "no gunner, no bomb run, not an escort target");
            var sr = wreck.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(sr);
            Assert.IsTrue(sr.flipX, "mirrored like the bomber");
        }

        [Test]
        public void BeginCrash_OnALiveBomber_DoesNothing() {
            var b = MakeBomber(new Vector3(50f, 2f, 0f));
            Shoot(b, 39);
            Assert.IsNull(BeginCrash(b));
            Assert.IsTrue(b != null);
            Assert.AreEqual(0, BomberWreck.LiveCount);
        }

        [Test]
        public void Wreck_DriftsFallsAndDivesNoseDown() {
            var w = BomberWreck.Create(new Vector3(0f, 30f, 0f), null, true, 5, 5f, -1, new Vector2(-1.5f, 0f));
            _created.Add(w.gameObject);
            var crash = BomberCrashData.Resolve(GameBalance.Get());
            Assert.AreEqual(-1.5f * crash.driftFactor, w.Velocity.x, 1e-5f, "keeps drifting forward");

            Vector3 start = w.transform.position;
            for (int i = 0; i < 25; i++) Assert.IsFalse(Step(w, 0.02f));
            Assert.Less(w.transform.position.x, start.x, "drifts left");
            Assert.Less(w.transform.position.y, start.y, "falls");
            Assert.Less(w.Velocity.y, 0f, "gravity");
            Assert.Less(w.PitchAngle, 5f, "nose tips down");

            // 2.5 s in total: past the 2.2 s dive to 60 degrees, still well above the ground.
            for (int i = 0; i < 100; i++) Assert.IsFalse(Step(w, 0.02f), "still falling");
            Assert.AreEqual(-crash.maxDiveDegrees, w.PitchAngle, 1e-4f, "the dive is capped");
            // Body angle mirrors the pitch for a left-facing plane (PlanePitch.BodyAngle).
            Assert.AreEqual(PlanePitch.BodyAngle(w.PitchAngle, -1),
                Mathf.DeltaAngle(0f, w.transform.eulerAngles.z), 1e-3f);
        }

        [Test]
        public void Wreck_LandsOnTheGround_AndRemovesItself() {
            var w = BomberWreck.Create(new Vector3(0f, 5f, 0f), null, true, 5, 0f, -1, new Vector2(-1.5f, 0f));
            var go = w.gameObject;
            _created.Add(go);
            Assert.AreEqual(1, BomberWreck.LiveCount);

            bool landed = false;
            for (int i = 0; i < 2000 && !landed; i++) landed = Step(w, 0.02f);
            Assert.IsTrue(landed, "the wreck reaches the ground");
            Assert.IsTrue(w.Landed);
            Assert.IsTrue(go == null, "removed after the ground burst");
            Assert.AreEqual(0, BomberWreck.LiveCount);
        }

        [Test]
        public void Wreck_PausedStep_DoesNothing() {
            var w = BomberWreck.Create(new Vector3(0f, 5f, 0f), null, true, 5, 0f, -1, new Vector2(-1.5f, 0f));
            _created.Add(w.gameObject);
            Vector3 p = w.transform.position;
            Assert.IsFalse(Step(w, 0f));
            Assert.AreEqual(p, w.transform.position);
        }

        [Test]
        public void ClearAll_RemovesFallingWrecks() {
            var a = BomberWreck.Create(new Vector3(0f, 5f, 0f), null, true, 5, 0f, -1, Vector2.zero);
            var b = BomberWreck.Create(new Vector3(5f, 5f, 0f), null, true, 5, 0f, -1, Vector2.zero);
            var ga = a.gameObject;
            var gb = b.gameObject;
            Assert.AreEqual(2, BomberWreck.LiveCount);
            BomberWreck.ClearAll();
            Assert.AreEqual(0, BomberWreck.LiveCount);
            Assert.IsTrue(ga == null && gb == null);
        }

        [Test]
        public void GroundBurstOffset_CentreThenBothSides() {
            Assert.AreEqual(Vector2.zero, BomberWreck.GroundBurstOffset(0, 3, 0.9f));
            Vector2 l = BomberWreck.GroundBurstOffset(1, 3, 0.9f);
            Vector2 r = BomberWreck.GroundBurstOffset(2, 3, 0.9f);
            Assert.AreEqual(-0.9f, l.x, 1e-5f);
            Assert.AreEqual(0.9f, r.x, 1e-5f);
            Assert.AreEqual(Vector2.zero, BomberWreck.GroundBurstOffset(0, 1, 0.9f));
        }

        [Test]
        public void BomberCrashData_ResolveFallsBackAndReadsBalance() {
            var d = BomberCrashData.Resolve(null);
            Assert.AreEqual(BomberCrashData.DefaultGravity, d.gravity);
            Assert.AreEqual(BomberCrashData.DefaultDriftFactor, d.driftFactor);
            Assert.AreEqual(BomberCrashData.DefaultDiveDegreesPerSecond, d.diveDegreesPerSecond);
            Assert.AreEqual(BomberCrashData.DefaultMaxDiveDegrees, d.maxDiveDegrees);
            Assert.AreEqual(BomberCrashData.DefaultGroundBurstCount, d.groundBurstCount);
            Assert.AreEqual(BomberCrashData.DefaultGroundBurstSpread, d.groundBurstSpread);
            Assert.AreEqual(BomberCrashData.DefaultMaxFallSeconds, d.maxFallSeconds);

            var balance = GameBalance.Get();
            Assert.IsNotNull(balance.bomberCrash, "Balance.json has a bomberCrash block");
            var r = BomberCrashData.Resolve(balance);
            Assert.AreEqual(balance.bomberCrash.gravity, r.gravity);
            Assert.AreEqual(balance.bomberCrash.groundBurstCount, r.groundBurstCount);
        }
    }
}
