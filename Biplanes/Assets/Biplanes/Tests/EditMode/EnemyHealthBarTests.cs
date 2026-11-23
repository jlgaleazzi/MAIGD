using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;

namespace Biplanes.Tests {

    /// <summary>
    /// B076: world-space damage bar above the bomber and the zeppelin hull.
    ///  - Pure helpers: FillFraction (clamped 0..1), FillCenterX, ClampBelowTop, CompensateScale.
    ///  - BomberAI.Spawn / ZeppelinAI.Spawn add one bar child with no collider or Rigidbody2D,
    ///    white-only colours, drawn above the aircraft (sortingOrder 5).
    ///  - The fill tracks the remaining health (the zeppelin's HULL, not its turrets).
    ///  - The bar stays unrotated with a positive unit scale under a pitched / mirrored parent.
    ///  - It hides at the killing hit and is gone with the bomber; the wreck has none.
    ///
    /// EditMode doesn't run Awake/LateUpdate, so the tests invoke Awake by reflection and call
    /// EnemyHealthBar.Refresh directly.
    /// </summary>
    public class EnemyHealthBarTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
            BomberWreck.ClearAll();
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

        private ZeppelinAI MakeZeppelin(Vector3 position) {
            var z = ZeppelinAI.Spawn(position, "zeppelin_01");
            _created.Add(z.gameObject);
            InvokeAwake(z);
            return z;
        }

        private static EnemyHealthBar BarOf(Component c) {
            var bars = c.GetComponentsInChildren<EnemyHealthBar>(true);
            Assert.AreEqual(1, bars.Length, "exactly one damage bar per aircraft");
            return bars[0];
        }

        private static void DropShield(ZeppelinAI z) {
            var notify = typeof(ZeppelinAI).GetMethod("NotifyTurretDestroyed", Private);
            Assert.IsNotNull(notify, "ZeppelinAI.NotifyTurretDestroyed not found by reflection");
            foreach (var t in z.GetComponentsInChildren<ZeppelinTurret>(true)) {
                notify.Invoke(z, new object[] { t });
            }
            Assert.AreEqual(0, z.TurretsRemaining);
        }

        private static float ExpectedBarY(float targetY, float offsetY, EnemyHealthBar bar) {
            float y = targetY + offsetY;
            var cam = Camera.main;
            if (cam != null && cam.orthographic) {
                float top = cam.transform.position.y + cam.orthographicSize;
                y = EnemyHealthBar.ClampBelowTop(y, top, bar.Height * 0.5f + bar.FrameThickness,
                    EnemyHealthBar.ScreenTopMargin);
            }
            return y;
        }

        /// <summary>Inner height the bar should use: InnerHeightPx at the main camera's scale,
        /// or the world fallback without an orthographic camera (B077).</summary>
        private static float ExpectedInnerHeight(float fallback) {
            var cam = Camera.main;
            float ppu = cam != null && cam.orthographic
                ? EnemyHealthBar.PixelsPerUnit(cam.pixelHeight, cam.orthographicSize) : 0f;
            return EnemyHealthBar.PixelsToWorld(EnemyHealthBar.InnerHeightPx, ppu, fallback);
        }

        // ── Pure helpers ────────────────────────────────────────────────

        [Test]
        public void FillFraction_IsRemainingHealth_Clamped() {
            Assert.AreEqual(1f, EnemyHealthBar.FillFraction(0f), 1e-6f);
            Assert.AreEqual(0.75f, EnemyHealthBar.FillFraction(25f), 1e-6f);
            Assert.AreEqual(0.5f, EnemyHealthBar.FillFraction(50f), 1e-6f);
            Assert.AreEqual(0f, EnemyHealthBar.FillFraction(100f), 1e-6f);
            Assert.AreEqual(0f, EnemyHealthBar.FillFraction(250f), 1e-6f);
            Assert.AreEqual(1f, EnemyHealthBar.FillFraction(-30f), 1e-6f);
            Assert.AreEqual(1f, EnemyHealthBar.FillFraction(float.NaN), 1e-6f);
        }

        [Test]
        public void FillFraction_OneBulletOnA1000HpTarget_Is97Point5Percent() {
            // Player bullet 25 on 1000 HP = 2.5% damage.
            Assert.AreEqual(0.975f, EnemyHealthBar.FillFraction(2.5f), 1e-5f);
        }

        [Test]
        public void FillCenterX_AnchorsTheFillOnTheLeft() {
            Assert.AreEqual(0f, EnemyHealthBar.FillCenterX(1.6f, 1f), 1e-6f);
            Assert.AreEqual(-0.4f, EnemyHealthBar.FillCenterX(1.6f, 0.5f), 1e-6f);
            Assert.AreEqual(-0.8f, EnemyHealthBar.FillCenterX(1.6f, 0f), 1e-6f);
            Assert.AreEqual(0f, EnemyHealthBar.FillCenterX(1.6f, 3f), 1e-6f, "fraction clamped to 1");
        }

        [Test]
        public void ClampBelowTop_OnlyLowersNearTheTop() {
            Assert.AreEqual(2f, EnemyHealthBar.ClampBelowTop(2f, 5f, 0.1f, 0.1f), 1e-6f);
            Assert.AreEqual(4.8f, EnemyHealthBar.ClampBelowTop(6f, 5f, 0.1f, 0.1f), 1e-5f);
        }

        [Test]
        public void CompensateScale_CancelsParentScaleAndMirror() {
            Assert.AreEqual(new Vector3(-1f, 1f, 1f), EnemyHealthBar.CompensateScale(new Vector3(-1f, 1f, 1f)));
            var s = EnemyHealthBar.CompensateScale(new Vector3(2f, 0.5f, 1f));
            Assert.AreEqual(0.5f, s.x, 1e-6f);
            Assert.AreEqual(2f, s.y, 1e-6f);
            Assert.AreEqual(new Vector3(1f, 1f, 1f), EnemyHealthBar.CompensateScale(Vector3.zero));
        }

        [Test]
        public void CancelAngle_UndoesTheTurn_ReversedUnderAMirror() {
            Assert.AreEqual(-20f, EnemyHealthBar.CancelAngle(20f, Vector3.one), 1e-6f);
            Assert.AreEqual(20f, EnemyHealthBar.CancelAngle(20f, new Vector3(-1f, 1f, 1f)), 1e-6f);
            Assert.AreEqual(-20f, EnemyHealthBar.CancelAngle(20f, new Vector3(-1f, -1f, 1f)), 1e-6f);
        }

        // ── Bomber ──────────────────────────────────────────────────────

        [Test]
        public void Bomber_SpawnsWithOneBar_NoColliderNoBody_WhitePalette_AboveTheBomber() {
            var b = MakeBomber(new Vector3(10f, -2f, 0f));
            var bar = BarOf(b);

            Assert.AreEqual(b.transform, bar.transform.parent, "the bar is the bomber's own child");
            Assert.AreEqual(0, bar.GetComponentsInChildren<Collider2D>(true).Length, "bar must not catch hits");
            Assert.AreEqual(0, bar.GetComponentsInChildren<Rigidbody2D>(true).Length);
            Assert.AreEqual(EnemyHealthBar.BomberWidth, bar.Width, 1e-6f);
            Assert.AreEqual(ExpectedInnerHeight(EnemyHealthBar.BomberHeight), bar.Height, 1e-5f);

            Assert.AreEqual(EnemyHealthBar.Pencil, bar.FrameRenderer.color);
            Assert.AreEqual(EnemyHealthBar.Paper, bar.BackRenderer.color);
            Assert.AreEqual(EnemyHealthBar.Ink, bar.FillRenderer.color);
            Assert.Greater(bar.FrameRenderer.sortingOrder, 5, "above the aircraft (5)");
            Assert.Greater(bar.BackRenderer.sortingOrder, bar.FrameRenderer.sortingOrder);
            Assert.Greater(bar.FillRenderer.sortingOrder, bar.BackRenderer.sortingOrder);
            Assert.Less(bar.FillRenderer.sortingOrder, 15, "under projectiles (15)");

            Assert.IsTrue(bar.IsShown);
            Assert.AreEqual(1f, bar.Fraction, 1e-6f, "full at 0% damage");
            Assert.AreEqual(EnemyHealthBar.BomberWidth, bar.FillWorldWidth, 1e-5f);
        }

        [Test]
        public void Bomber_FillShrinksWithDamage() {
            var b = MakeBomber(new Vector3(10f, -2f, 0f));
            var bar = BarOf(b);
            var h = b.GetComponent<Health>();
            float bullet = GameBalance.Get().player.bulletDamage;

            for (int i = 0; i < 10; i++) h.ApplyDamage(bullet, DamageSource.Bullet);   // 250 of 1000
            bar.Refresh();
            Assert.AreEqual(0.75f, bar.Fraction, 1e-4f);
            Assert.AreEqual(EnemyHealthBar.BomberWidth * 0.75f, bar.FillWorldWidth, 1e-4f);
            Assert.AreEqual(EnemyHealthBar.FillCenterX(EnemyHealthBar.BomberWidth, 0.75f),
                bar.FillRenderer.transform.localPosition.x, 1e-4f, "left-anchored");

            h.ApplyDamage(GameBalance.Get().player.bombDamage, DamageSource.Bomb);       // 350
            bar.Refresh();
            Assert.AreEqual(0.65f, bar.Fraction, 1e-4f);
            Assert.IsTrue(bar.IsShown);
        }

        [Test]
        public void Bomber_BarFollowsUnrotatedAndUnmirrored() {
            var b = MakeBomber(new Vector3(10f, -2f, 0f));
            var bar = BarOf(b);

            b.transform.position = new Vector3(14f, -1.5f, 0f);
            b.transform.rotation = Quaternion.Euler(0f, 0f, 25f);
            b.transform.localScale = new Vector3(-1f, 1f, 1f);
            bar.Refresh();

            // The real world matrix (not the rotation property, which ignores a parent mirror):
            // world x axis = +right, y axis = +up, unit length, so no turn, flip or squash.
            Matrix4x4 m = bar.transform.localToWorldMatrix;
            Vector3 xAxis = m.GetColumn(0);
            Vector3 yAxis = m.GetColumn(1);
            Assert.AreEqual(1f, xAxis.x, 1e-3f, "positive unit x axis under a mirrored, pitched parent");
            Assert.AreEqual(0f, xAxis.y, 1e-3f);
            Assert.AreEqual(0f, yAxis.x, 1e-3f);
            Assert.AreEqual(1f, yAxis.y, 1e-3f);

            // Unmirrored pitch only (the real bomber: flipX on the sprite, scale +1).
            b.transform.localScale = Vector3.one;
            b.transform.rotation = Quaternion.Euler(0f, 0f, -18f);
            bar.Refresh();
            m = bar.transform.localToWorldMatrix;
            Assert.AreEqual(1f, ((Vector3)m.GetColumn(0)).x, 1e-3f);
            Assert.AreEqual(0f, ((Vector3)m.GetColumn(0)).y, 1e-3f);
            Assert.AreEqual(1f, ((Vector3)m.GetColumn(1)).y, 1e-3f);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, bar.transform.rotation), 0.01f);
            Assert.AreEqual(14f, bar.transform.position.x, 1e-4f, "centred over the bomber");
            Assert.AreEqual(ExpectedBarY(-1.5f, EnemyHealthBar.BomberOffsetY, bar),
                bar.transform.position.y, 1e-4f);
        }

        [Test]
        public void Bomber_BarHidesAtTheKillingHit_AndTheWreckHasNone() {
            var b = MakeBomber(new Vector3(10f, -2f, 0f));
            var bar = BarOf(b);
            var barGo = bar.gameObject;

            b.GetComponent<Health>().ApplyDamage(1f, DamageSource.Collision);   // fatal for enemies
            bar.Refresh();
            Assert.IsFalse(bar.IsShown);
            Assert.IsFalse(bar.FrameRenderer.enabled);
            Assert.IsFalse(bar.BackRenderer.enabled);
            Assert.IsFalse(bar.FillRenderer.enabled);

            var crash = typeof(BomberAI).GetMethod("BeginCrash", Private);
            Assert.IsNotNull(crash, "BomberAI.BeginCrash not found by reflection");
            var wreck = (BomberWreck)crash.Invoke(b, null);
            Assert.IsNotNull(wreck);
            Assert.IsTrue(barGo == null, "the bar is removed with the bomber");
            Assert.IsNull(wreck.GetComponentInChildren<EnemyHealthBar>(true), "the wreck has no bar");
        }

        [Test]
        public void Attach_Twice_ReusesTheBar() {
            var b = MakeBomber(new Vector3(10f, -2f, 0f));
            EnemyHealthBar.Attach(b.transform, b.GetComponent<Health>(), 2f, 0.2f, 1.2f);
            var bar = BarOf(b);
            Assert.AreEqual(2f, bar.Width, 1e-6f);
            Assert.IsNull(EnemyHealthBar.Attach(null, b.GetComponent<Health>(), 1f, 1f, 1f));
        }

        // ── Zeppelin ────────────────────────────────────────────────────

        [Test]
        public void Zeppelin_SpawnsWithOneWideBar_NoCollider() {
            var z = MakeZeppelin(new Vector3(30f, -1f, 0f));
            var bar = BarOf(z);
            Assert.AreEqual(z.transform, bar.transform.parent);
            Assert.AreEqual(0, bar.GetComponentsInChildren<Collider2D>(true).Length);
            Assert.AreEqual(EnemyHealthBar.ZeppelinWidth, bar.Width, 1e-6f);
            Assert.Greater(bar.Width, EnemyHealthBar.BomberWidth);
            Assert.IsTrue(bar.IsShown);
            Assert.AreEqual(1f, bar.Fraction, 1e-6f);
        }

        [Test]
        public void Zeppelin_BarShowsOverallHealth_TurretsAndHull() {
            // B077: the bar shows turrets + hull, so it moves during the shielded turret phase.
            var z = MakeZeppelin(new Vector3(30f, -1f, 0f));
            foreach (var t in z.GetComponentsInChildren<ZeppelinTurret>(true)) InvokeAwake(t);
            var bar = BarOf(z);
            var eh = GameBalance.Get().enemyHealth;
            float bullet = GameBalance.Get().player.bulletDamage;
            float total = 3f * eh.zeppelinTurret + eh.zeppelinHull;
            bar.Refresh();
            Assert.AreEqual(1f, bar.Fraction, 1e-5f);

            // Shielded: a hull hit is absorbed, the bar stays put.
            z.ApplyDamage(bullet, DamageSource.Bullet);
            bar.Refresh();
            Assert.AreEqual(1f, bar.Fraction, 1e-5f, "an absorbed hull hit doesn't move the bar");

            // A turret hit moves the bar by bullet / total.
            var turret = z.GetComponentsInChildren<ZeppelinTurret>(true)[0];
            turret.ApplyDamage(bullet, DamageSource.Bullet);
            bar.Refresh();
            Assert.AreEqual(1f - bullet / total, bar.Fraction, 1e-4f, "a turret hit moves the bar");
            Assert.IsTrue(bar.IsFlashing, "the frame flashes on the hit");

            // All turrets down: only the hull is left.
            DropShield(z);
            bar.Refresh();
            Assert.AreEqual(eh.zeppelinHull / total, bar.Fraction, 1e-4f);

            for (int i = 0; i < 20; i++) z.ApplyDamage(bullet, DamageSource.Bullet);   // 500 of 1000
            bar.Refresh();
            Assert.AreEqual(eh.zeppelinHull * 0.5f / total, bar.Fraction, 1e-4f);
            Assert.AreEqual(z.OverallHealthFraction, bar.Fraction, 1e-4f);
        }

        [Test]
        public void Zeppelin_BarHidesWhenTheHullIsDead() {
            var z = MakeZeppelin(new Vector3(30f, -1f, 0f));
            var bar = BarOf(z);
            // Die() uses Destroy (Play mode only), so set the dead state directly.
            var dmg = typeof(ZeppelinAI).GetField("_damagePercent", Private);
            Assert.IsNotNull(dmg, "ZeppelinAI._damagePercent not found by reflection");
            dmg.SetValue(z, 100f);
            bar.Refresh();
            Assert.IsFalse(bar.IsShown);
            Assert.IsFalse(bar.FillRenderer.enabled);
        }
    }
}
