using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Fx;

namespace Biplanes.Tests {

    /// <summary>
    /// B051: damaged enemy fighters trail pencil smoke (GDD Enemies item 1). Pure helpers
    /// (SmokeInterval, HitsFromDamage, SmokeTailPoint), the internal EmitSmoke driven with
    /// explicit time steps, and the separate FxManager enemy-smoke cap.
    ///
    /// EditMode doesn't run Awake/Update, so Awake is invoked by reflection, FxManager is
    /// seeded into _instance by reflection (as FxManagerTests does), and Tick is never run
    /// (it would Destroy finished effects, which EditMode rejects).
    /// </summary>
    public class FighterSmokeTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();
        private GameObject _fxGo;
        private FxManager _fx;

        [SetUp]
        public void SetUp() {
            DestroyPuffs();
            _fxGo = new GameObject("B051_Fx");
            _fx = _fxGo.AddComponent<FxManager>();
            InstanceField().SetValue(null, _fx);
        }

        [TearDown]
        public void TearDown() {
            InstanceField().SetValue(null, null);
            if (_fxGo != null) Object.DestroyImmediate(_fxGo);
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
            DestroyPuffs();
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static FieldInfo InstanceField() {
            var f = typeof(FxManager).GetField("_instance", PrivateStatic);
            Assert.IsNotNull(f, "FxManager._instance not found by reflection");
            return f;
        }

        private static void DestroyPuffs() {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go == null || !go.scene.IsValid()) continue;
                if (go.name == "SmokePuff") Object.DestroyImmediate(go);
            }
        }

        private static List<GameObject> Puffs() {
            var list = new List<GameObject>();
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go != null && go.name == "SmokePuff" && go.scene.IsValid()) list.Add(go);
            }
            return list;
        }

        private static void AssumeSmokeSprite() {
            Assume.That(Resources.Load<Sprite>("Sprites/fx/smoke_puff") != null,
                "smoke_puff sprite not importable in this editor");
        }

        private static void InvokeAwake(Component c) {
            var awake = c.GetType().GetMethod("Awake", Private);
            Assert.IsNotNull(awake, $"{c.GetType().Name}.Awake not found by reflection");
            awake.Invoke(c, null);
        }

        private FighterAI MakeFighter(Vector3 position) {
            var f = FighterAI.Spawn(position);
            _created.Add(f.gameObject);
            InvokeAwake(f.GetComponent<Health>());
            InvokeAwake(f);
            return f;
        }

        private static bool Emit(FighterAI f, float dt) {
            var m = typeof(FighterAI).GetMethod("EmitSmoke", Private);
            Assert.IsNotNull(m, "FighterAI.EmitSmoke not found by reflection");
            return (bool)m.Invoke(f, new object[] { dt });
        }

        private static float BulletDamage() {
            var g = GameBalance.Get();
            return g != null && g.player != null && g.player.bulletDamage > 0f ? g.player.bulletDamage : 25f;
        }

        private int EnemyPuffs => _fx.CountActive(FxManager.Kind.EnemySmokePuff);

        // ── Pure helpers ────────────────────────────────────────────────

        [Test]
        public void SmokeInterval_ByHitCount() {
            Assert.AreEqual(0f, FighterAI.SmokeInterval(0, false), "healthy: no smoke");
            Assert.AreEqual(FighterAI.SmokeIntervalLight, FighterAI.SmokeInterval(1, false), "1 hit: slow");
            Assert.AreEqual(FighterAI.SmokeIntervalHeavy, FighterAI.SmokeInterval(2, false), "2 hits: fast");
            Assert.AreEqual(FighterAI.SmokeIntervalHeavy, FighterAI.SmokeInterval(5, false), "more hits: still fast");
            Assert.AreEqual(0f, FighterAI.SmokeInterval(1, true), "dead: no smoke");
            Assert.AreEqual(0f, FighterAI.SmokeInterval(2, true), "dead: no smoke");
            Assert.Greater(FighterAI.SmokeIntervalLight, FighterAI.SmokeIntervalHeavy, "second hit is thicker");
            Assert.AreEqual(0.25f, FighterAI.SmokeIntervalLight, 1e-6f);
            Assert.AreEqual(0.12f, FighterAI.SmokeIntervalHeavy, 1e-6f);
        }

        [Test]
        public void HitsFromDamage_CountsBulletsRoundedUp() {
            Assert.AreEqual(0, FighterAI.HitsFromDamage(0f, 75f, 25f));
            Assert.AreEqual(1, FighterAI.HitsFromDamage(100f / 3f, 75f, 25f));
            Assert.AreEqual(2, FighterAI.HitsFromDamage(200f / 3f, 75f, 25f));
            Assert.AreEqual(3, FighterAI.HitsFromDamage(100f, 75f, 25f));
            Assert.AreEqual(1, FighterAI.HitsFromDamage(1f, 75f, 25f), "any damage is at least one hit");
            Assert.AreEqual(1, FighterAI.HitsFromDamage(50f, 75f, 0f), "unknown hit size: one hit");
        }

        [Test]
        public void SmokeTailPoint_BehindTail_LeftRightAndPitch() {
            var c = new Vector3(2f, 1f, 0f);
            float o = FighterAI.SmokeTailOffset;

            Vector3 left = FighterAI.SmokeTailPoint(c, 0f, -1);
            Assert.AreEqual(c.x + o, left.x, 1e-5f, "left-flying: tail on the right");
            Assert.AreEqual(c.y, left.y, 1e-5f);

            Vector3 right = FighterAI.SmokeTailPoint(c, 0f, 1);
            Assert.AreEqual(c.x - o, right.x, 1e-5f, "right-flying: tail on the left");
            Assert.AreEqual(c.y, right.y, 1e-5f);

            // Nose up 30 degrees: the tail drops below the centre on either side.
            float cos = Mathf.Cos(30f * Mathf.Deg2Rad), sin = Mathf.Sin(30f * Mathf.Deg2Rad);
            Vector3 leftUp = FighterAI.SmokeTailPoint(c, 30f, -1);
            Assert.AreEqual(c.x + o * cos, leftUp.x, 1e-5f);
            Assert.AreEqual(c.y - o * sin, leftUp.y, 1e-5f);
            Vector3 rightUp = FighterAI.SmokeTailPoint(c, 30f, 1);
            Assert.AreEqual(c.x - o * cos, rightUp.x, 1e-5f);
            Assert.AreEqual(c.y - o * sin, rightUp.y, 1e-5f);
        }

        // ── EmitSmoke ───────────────────────────────────────────────────

        [Test]
        public void EmitSmoke_HealthyNone_FirstHitSlow_SecondHitFast_PausedNone() {
            AssumeSmokeSprite();
            var f = MakeFighter(new Vector3(3f, 2f, 0f));

            Assert.IsFalse(Emit(f, 1f), "healthy fighter never smokes");
            Assert.AreEqual(0, EnemyPuffs);

            f.ApplyDamage(BulletDamage(), DamageSource.Bullet);
            Assume.That(!f.IsDead, "one bullet killed the fighter (Balance changed)");
            Assert.AreEqual(1, f.HitCount);

            Assert.IsTrue(Emit(f, 0.016f), "first puff at once after the first hit");
            Assert.AreEqual(1, EnemyPuffs);
            Assert.AreEqual(0, _fx.CountActive(FxManager.Kind.SmokePuff), "enemy smoke is its own kind");
            Vector3 expected = FighterAI.SmokeTailPoint(f.transform.position, f.PitchAngle, f.FlightDirection);
            Vector3 puff = Puffs()[0].transform.position;
            Assert.AreEqual(expected.x, puff.x, 1e-4f, "behind the tail");
            Assert.AreEqual(expected.y, puff.y, 1e-4f);
            Assert.Greater(puff.x, f.transform.position.x, "left-flying fighter: tail on the right");

            Assert.IsFalse(Emit(f, 0.2f), "not before 0.25 s");
            Assert.IsTrue(Emit(f, 0.06f), "next puff after 0.25 s");
            Assert.AreEqual(2, EnemyPuffs);

            Assert.IsFalse(Emit(f, 0f), "paused: no puff");

            f.ApplyDamage(BulletDamage(), DamageSource.Bullet);
            Assume.That(!f.IsDead, "two bullets killed the fighter (Balance changed)");
            Assert.AreEqual(2, f.HitCount);
            Assert.IsFalse(Emit(f, 0.016f), "not before 0.12 s");
            Assert.IsTrue(Emit(f, 0.11f), "thicker at once: puff within 0.12 s of the second hit");
            Assert.IsFalse(Emit(f, 0.1f));
            Assert.IsTrue(Emit(f, 0.03f), "every 0.12 s after the second hit");
            Assert.AreEqual(4, EnemyPuffs);
        }

        [Test]
        public void EmitSmoke_DeadFighterNeverSmokes() {
            AssumeSmokeSprite();
            var f = MakeFighter(new Vector3(3f, 2f, 0f));
            f.ApplyDamage(BulletDamage(), DamageSource.Bullet);
            Assert.IsTrue(Emit(f, 0.016f));
            f.ApplyDamage(1000f, DamageSource.Bullet);
            Assert.IsTrue(f.IsDead);
            Assert.IsFalse(Emit(f, 1f), "dead: no smoke");
            Assert.AreEqual(1, EnemyPuffs);
        }

        [Test]
        public void EmitSmoke_RightFlyingAndReEntered_KeepsSmokingBehindTail() {
            AssumeSmokeSprite();
            var f = MakeFighter(new Vector3(0f, 1f, 0f));
            f.ApplyDamage(BulletDamage(), DamageSource.Bullet);
            Assert.IsTrue(Emit(f, 0.016f));

            f.ReEnter(new Vector3(-10f, 2f, 0f), 1);
            Assert.AreEqual(1, f.FlightDirection);
            DestroyPuffs();
            Assert.IsTrue(Emit(f, FighterAI.SmokeIntervalLight), "a damaged re-entering fighter keeps smoking");
            Vector3 puff = Puffs()[0].transform.position;
            Vector3 expected = FighterAI.SmokeTailPoint(f.transform.position, f.PitchAngle, 1);
            Assert.AreEqual(expected.x, puff.x, 1e-4f);
            Assert.AreEqual(expected.y, puff.y, 1e-4f);
            Assert.Less(puff.x, f.transform.position.x, "right-flying fighter: tail on the left");
        }

        // ── FxManager caps ──────────────────────────────────────────────

        [Test]
        public void EnemySmoke_OwnCap_NeverStarvesPlayerSmoke() {
            AssumeSmokeSprite();
            for (int i = 0; i < 24; i++) _fx.SpawnSmokePuff(new Vector3(i, 0f, 0f));
            for (int i = 0; i < 50; i++) _fx.SpawnEnemySmokePuff(new Vector3(i, 5f, 0f));
            Assert.AreEqual(24, _fx.CountActive(FxManager.Kind.SmokePuff), "player trail untouched");
            Assert.AreEqual(32, _fx.CountActive(FxManager.Kind.EnemySmokePuff), "enemy smoke capped at 32");
            Assert.AreEqual(56, Puffs().Count, "recycled, not duplicated");
        }
    }
}
