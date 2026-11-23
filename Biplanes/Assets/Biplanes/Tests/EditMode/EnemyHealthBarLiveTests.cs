using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// B077: the enemy damage bar driven through the real projectile path, as close to the game
    /// as EditMode allows. A real Bullet (Awake run, Balance damage) hits the bomber's own
    /// hitbox through Projectile.OnTriggerEnter2D (the method Unity calls for a bullet
    /// collider), and the bar is checked after every hit: 2.5% of the width per 25 damage,
    /// left-anchored, flashing, bound to the very Health the bullet damaged.
    /// Also: the pixel -> world helpers behind the thin bar, the minimum visible first step,
    /// and the zeppelin's OverallFraction formula.
    /// </summary>
    public class EnemyHealthBarLiveTests {

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

        private static void InvokeTrigger(Projectile p, Collider2D other) {
            var m = typeof(Projectile).GetMethod("OnTriggerEnter2D", Private);
            Assert.IsNotNull(m, "Projectile.OnTriggerEnter2D not found by reflection");
            m.Invoke(p, new object[] { other });
        }

        /// <summary>The bomber as MissionRunner builds it, with the Awake calls Play mode makes
        /// inside AddComponent (Health first, then BomberAI).</summary>
        private BomberAI MakeBomber(Vector3 position) {
            var b = BomberAI.Spawn(position, "bomber_1");
            _created.Add(b.gameObject);
            InvokeAwake(b.GetComponent<Health>());
            InvokeAwake(b);
            return b;
        }

        private Bullet MakeBullet() {
            var go = new GameObject("B077_Bullet");
            _created.Add(go);
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<CircleCollider2D>().isTrigger = true;
            go.AddComponent<SpriteRenderer>();
            var bullet = go.AddComponent<Bullet>();
            InvokeAwake(bullet);
            return bullet;
        }

        private static EnemyHealthBar BarOf(Component c) {
            var bars = c.GetComponentsInChildren<EnemyHealthBar>(true);
            Assert.AreEqual(1, bars.Length, "exactly one damage bar per aircraft");
            return bars[0];
        }

        // ── Live bomber path ────────────────────────────────────────────

        [Test]
        public void Bomber_RealBulletHits_MoveTheBarEveryHit() {
            var b = MakeBomber(new Vector3(10f, -2f, 0f));
            var bar = BarOf(b);
            var health = b.GetComponent<Health>();
            var boxes = b.GetComponents<BoxCollider2D>();
            Assert.AreEqual(3, boxes.Length);
            Assert.AreEqual(Team.Enemy, health.Team);

            // The bullet resolves the same Health the bar is bound to.
            Assert.AreSame(health, boxes[0].GetComponentInParent<IDamageable>() as Health,
                "the hitbox resolves to the bomber's Health");

            var bullet = MakeBullet();
            float hp = health.MaxHitPoints;
            Assert.Greater(hp, 0f, "enemy-mode Health");
            float step = bullet.Damage / hp;   // 25 / 1000 = 0.025

            bar.Refresh();
            Assert.AreEqual(1f, bar.Fraction, 1e-6f);
            Assert.AreEqual(bar.Width, bar.FillWorldWidth, 1e-5f);
            Assert.IsFalse(bar.IsFlashing);

            float prevWidth = bar.FillWorldWidth;
            for (int i = 1; i <= 12; i++) {
                InvokeTrigger(bullet, boxes[i % boxes.Length]);
                bar.Refresh();

                float expected = 1f - step * i;
                Assert.AreEqual(expected, bar.Fraction, 1e-4f, $"fraction after hit {i}");
                Assert.AreEqual(EnemyHealthBar.VisibleFillWidth(bar.Width, expected, bar.PixelsPerUnitUsed),
                    bar.FillWorldWidth, 1e-4f, $"fill width after hit {i}");
                Assert.Less(bar.FillWorldWidth, prevWidth - 1e-5f, $"hit {i} visibly shrinks the fill");
                Assert.AreEqual(-bar.Width * 0.5f + bar.FillWorldWidth * 0.5f,
                    bar.FillRenderer.transform.localPosition.x, 1e-4f, "left-anchored");
                Assert.IsTrue(bar.FillRenderer.enabled && bar.BackRenderer.enabled && bar.FrameRenderer.enabled);
                Assert.IsTrue(bar.IsFlashing, $"frame flashes on hit {i}");
                Assert.AreEqual(EnemyHealthBar.Ink, bar.FrameRenderer.color);
                prevWidth = bar.FillWorldWidth;
            }

            // Fill stays above the backing and the frame, and above the bomber (5).
            Assert.Greater(bar.FillRenderer.sortingOrder, bar.BackRenderer.sortingOrder);
            Assert.Greater(bar.BackRenderer.sortingOrder, bar.FrameRenderer.sortingOrder);
            Assert.Greater(bar.FrameRenderer.sortingOrder, b.GetComponent<SpriteRenderer>().sortingOrder);
            Assert.AreEqual(bar.FillRenderer.sortingLayerID, b.GetComponent<SpriteRenderer>().sortingLayerID);
        }

        [Test]
        public void Bomber_FlashEndsAfterFlashSeconds_AndNoFlashWithoutAHit() {
            var b = MakeBomber(new Vector3(10f, -2f, 0f));
            var bar = BarOf(b);
            InvokeTrigger(MakeBullet(), b.GetComponents<BoxCollider2D>()[0]);
            bar.Tick(0.016f);
            Assert.IsTrue(bar.IsFlashing);

            bar.Tick(EnemyHealthBar.FlashSeconds);
            Assert.IsFalse(bar.IsFlashing);
            Assert.AreEqual(EnemyHealthBar.Pencil, bar.FrameRenderer.color);

            bar.Tick(0.016f);
            Assert.IsFalse(bar.IsFlashing, "no new hit, no flash");
        }

        [Test]
        public void Bomber_ReRunningAwake_DoesNotRebindOrResetTheBar() {
            // Play mode runs BomberAI.Awake inside AddComponent, before Spawn attaches the bar;
            // a second Awake (SetMaxHitPoints again) must not detach or reset it.
            var b = MakeBomber(new Vector3(10f, -2f, 0f));
            var bar = BarOf(b);
            var bullet = MakeBullet();
            var box = b.GetComponents<BoxCollider2D>()[0];
            InvokeTrigger(bullet, box);
            InvokeAwake(b);
            InvokeTrigger(bullet, box);
            bar.Refresh();
            Assert.AreEqual(1f - 2f * bullet.Damage / b.GetComponent<Health>().MaxHitPoints, bar.Fraction, 1e-4f);
        }

        [Test]
        public void Bomber_BarHeight_IsPixelSized() {
            var b = MakeBomber(new Vector3(10f, -2f, 0f));
            var bar = BarOf(b);
            bar.Refresh();
            if (bar.PixelsPerUnitUsed > 0f) {
                Assert.AreEqual(EnemyHealthBar.InnerHeightPx, bar.Height * bar.PixelsPerUnitUsed, 1e-3f);
                Assert.AreEqual(EnemyHealthBar.FramePx, bar.FrameThickness * bar.PixelsPerUnitUsed, 1e-3f);
                float totalPx = (bar.Height + 2f * bar.FrameThickness) * bar.PixelsPerUnitUsed;
                Assert.That(totalPx, Is.InRange(10f, 20f), "10-20 px tall on screen");
            } else {
                Assert.AreEqual(EnemyHealthBar.BomberHeight, bar.Height, 1e-6f, "world fallback without a camera");
                Assert.AreEqual(EnemyHealthBar.OutlineThickness, bar.FrameThickness, 1e-6f);
            }
            Assert.AreEqual(bar.Width, bar.FrameRenderer.transform.localScale.x - 2f * bar.FrameThickness, 1e-5f);
            Assert.AreEqual(bar.Height, bar.BackRenderer.transform.localScale.y, 1e-6f);
            Assert.AreEqual(bar.Height, bar.FillRenderer.transform.localScale.y, 1e-6f);
        }

        // ── Pure helpers ────────────────────────────────────────────────

        [Test]
        public void PixelsPerUnit_IsPixelHeightOverTwiceOrthoSize() {
            Assert.AreEqual(108f, EnemyHealthBar.PixelsPerUnit(1080f, 5f), 1e-4f);
            Assert.AreEqual(72f, EnemyHealthBar.PixelsPerUnit(720f, 5f), 1e-4f);
            Assert.AreEqual(216f, EnemyHealthBar.PixelsPerUnit(2160f, 5f), 1e-4f);
            Assert.AreEqual(0f, EnemyHealthBar.PixelsPerUnit(0f, 5f));
            Assert.AreEqual(0f, EnemyHealthBar.PixelsPerUnit(1080f, 0f));
            Assert.AreEqual(0f, EnemyHealthBar.PixelsPerUnit(float.NaN, 5f));
        }

        [Test]
        public void PixelsToWorld_ConvertsOrFallsBack() {
            Assert.AreEqual(10f / 108f, EnemyHealthBar.PixelsToWorld(10f, 108f, 0.09f), 1e-6f);
            Assert.AreEqual(2f / 216f, EnemyHealthBar.PixelsToWorld(2f, 216f, 0.02f), 1e-6f);
            Assert.AreEqual(0.09f, EnemyHealthBar.PixelsToWorld(10f, 0f, 0.09f), 1e-6f);

            // Same on-screen thickness at 720p, 1080p and 4K.
            foreach (float h in new[] { 720f, 1080f, 1440f, 2160f }) {
                float ppu = EnemyHealthBar.PixelsPerUnit(h, 5f);
                float inner = EnemyHealthBar.PixelsToWorld(EnemyHealthBar.InnerHeightPx, ppu, 0.09f);
                float frame = EnemyHealthBar.PixelsToWorld(EnemyHealthBar.FramePx, ppu, 0.02f);
                float totalPx = (inner + 2f * frame) * ppu;
                Assert.AreEqual(EnemyHealthBar.InnerHeightPx + 2f * EnemyHealthBar.FramePx, totalPx, 1e-3f);
                Assert.That(totalPx, Is.InRange(10f, 20f));
            }
        }

        [Test]
        public void VisibleFillWidth_FirstHitAlwaysShows_LaterHitsExact() {
            const float w = 1.6f;
            Assert.AreEqual(w, EnemyHealthBar.VisibleFillWidth(w, 1f, 108f), 1e-6f);
            Assert.AreEqual(0f, EnemyHealthBar.VisibleFillWidth(w, 0f, 108f), 1e-6f);
            // 0.1% damage at 108 px/u: drawn MinStepPx short of full.
            Assert.AreEqual(w - EnemyHealthBar.MinStepPx / 108f, EnemyHealthBar.VisibleFillWidth(w, 0.999f, 108f), 1e-6f);
            // A 2.5% bullet (4.3 px) already shows: exact.
            Assert.AreEqual(w * 0.975f, EnemyHealthBar.VisibleFillWidth(w, 0.975f, 108f), 1e-6f);
            Assert.AreEqual(w * 0.5f, EnemyHealthBar.VisibleFillWidth(w, 0.5f, 108f), 1e-6f);
            // No camera: exact.
            Assert.AreEqual(w * 0.999f, EnemyHealthBar.VisibleFillWidth(w, 0.999f, 0f), 1e-6f);
        }

        [Test]
        public void OverallFraction_CombinesTurretsAndHull() {
            // Balance: 3 turrets x 75 + hull 1000 = 1225.
            Assert.AreEqual(1f, ZeppelinAI.OverallFraction(225f, 225f, 1000f, 1000f), 1e-6f);
            Assert.AreEqual(1200f / 1225f, ZeppelinAI.OverallFraction(200f, 225f, 1000f, 1000f), 1e-6f, "one bullet on a turret");
            Assert.AreEqual(1000f / 1225f, ZeppelinAI.OverallFraction(0f, 225f, 1000f, 1000f), 1e-6f, "all turrets down");
            Assert.AreEqual(500f / 1225f, ZeppelinAI.OverallFraction(0f, 225f, 500f, 1000f), 1e-6f);
            Assert.AreEqual(0f, ZeppelinAI.OverallFraction(0f, 225f, 0f, 1000f), 1e-6f);
            Assert.AreEqual(1f, ZeppelinAI.OverallFraction(0f, 0f, 0f, 0f), 1e-6f, "no max: nothing to show");
            Assert.AreEqual(1000f / 1225f, ZeppelinAI.OverallFraction(-5f, 225f, 1000f, 1000f), 1e-6f, "negatives count 0");
        }
    }
}
