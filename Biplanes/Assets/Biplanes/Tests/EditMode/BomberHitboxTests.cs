using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Fx;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// T931: EditMode tests for B012 (bomber hitboxes fitted to its sprite, flipX mirroring,
    /// and Projectile ignoring trigger callbacks once recycled, so one projectile damages a
    /// multi-collider target once).
    ///
    /// EditMode doesn't run Awake on AddComponent, so tests invoke the private Awake by
    /// reflection where the code under test relies on it. Projectile.OnTriggerEnter2D is
    /// private on the base class, so it is looked up on typeof(Projectile).
    /// </summary>
    public class BomberHitboxTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const float Eps = 1e-4f;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();

            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (p != null) Object.DestroyImmediate(p.gameObject);
            }
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go == null) continue;
                if (go.name.StartsWith("Pool<") && go.hideFlags == HideFlags.HideAndDontSave) Object.DestroyImmediate(go);
                else if (go.name == "Explosion" && go.scene.IsValid()) Object.DestroyImmediate(go);
            }
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

        /// <summary>The box with the given size (sizes are unique per hitbox).</summary>
        private static BoxCollider2D BoxOfSize(BoxCollider2D[] boxes, Vector2 size) {
            foreach (var b in boxes) {
                if (Mathf.Abs(b.size.x - size.x) < Eps && Mathf.Abs(b.size.y - size.y) < Eps) return b;
            }
            Assert.Fail($"no hitbox of size {size}");
            return null;
        }

        /// <summary>A bomber built by Spawn, with its Health awake (Team.Enemy) and enough
        /// hit points that a single hit can't kill it.</summary>
        private Health MakeBomber(Vector3 pos, out BoxCollider2D[] boxes) {
            var bomber = BomberAI.Spawn(pos);
            _created.Add(bomber.gameObject);
            var h = bomber.GetComponent<Health>();
            InvokeAwake(h);
            h.SetMaxHitPoints(10000f);
            Assert.AreEqual(Team.Enemy, h.Team);
            boxes = bomber.GetComponents<BoxCollider2D>();
            return h;
        }

        private static void AssertBomberHitboxes(GameObject go, string label) {
            var cols = go.GetComponents<Collider2D>();
            Assert.AreEqual(3, cols.Length, $"{label}: three colliders on the bomber root");
            Assert.AreEqual(0, go.GetComponentsInChildren<Collider2D>(true).Length - cols.Length,
                $"{label}: no colliders on children");
            foreach (var c in cols) {
                Assert.IsInstanceOf<BoxCollider2D>(c, $"{label}: every hitbox is a BoxCollider2D");
                Assert.IsTrue(c.isTrigger, $"{label}: every hitbox is a trigger (ARCHITECTURE §3 Physics)");
            }
            var boxes = go.GetComponents<BoxCollider2D>();

            // As applied with flipX on: nose and wings on the left (-x), tail on the right (+x).
            var fuselage = BoxOfSize(boxes, new Vector2(2.64f, 0.40f));
            var wing = BoxOfSize(boxes, new Vector2(0.70f, 1.02f));
            var tail = BoxOfSize(boxes, new Vector2(0.46f, 0.50f));
            Assert.AreEqual(-0.12f, fuselage.offset.x, Eps, $"{label}: fuselage x mirrored");
            Assert.AreEqual(-0.08f, fuselage.offset.y, Eps, $"{label}: fuselage y");
            Assert.AreEqual(-0.69f, wing.offset.x, Eps, $"{label}: wing block x mirrored (left)");
            Assert.AreEqual(0.13f, wing.offset.y, Eps, $"{label}: wing block y");
            Assert.AreEqual(1.27f, tail.offset.x, Eps, $"{label}: tail fin x mirrored (right)");
            Assert.AreEqual(0.09f, tail.offset.y, Eps, $"{label}: tail fin y");

            // Every box lies inside the 320x140 px sprite at 100 ppu (3.2 x 1.4 units, centre pivot).
            foreach (var b in boxes) {
                Assert.LessOrEqual(Mathf.Abs(b.offset.x) + b.size.x / 2f, 1.6f + Eps, $"{label}: box inside sprite width");
                Assert.LessOrEqual(Mathf.Abs(b.offset.y) + b.size.y / 2f, 0.7f + Eps, $"{label}: box inside sprite height");
            }
        }

        // ── Hitboxes and flip (B012) ────────────────────────────────────

        [Test]
        public void BomberAI_FlipsToFlyLeft_IsTrue() {
            var f = typeof(BomberAI).GetField("FlipsToFlyLeft", PrivateStatic);
            Assert.IsNotNull(f, "BomberAI.FlipsToFlyLeft not found by reflection");
            Assert.AreEqual(true, f.GetRawConstantValue(),
                "art faces right (ART_STYLE) and the bomber flies left, so it must be mirrored");
        }

        [Test]
        public void BomberAI_Spawn_HasThreeMirroredTriggerHitboxes() {
            var bomber = BomberAI.Spawn(new Vector3(100f, 2f, 0f));
            _created.Add(bomber.gameObject);
            AssertBomberHitboxes(bomber.gameObject, "Spawn");
        }

        [Test]
        public void BomberAI_AwakeFallback_AddsTheSameHitboxes() {
            var go = new GameObject("T931_BareBomber");
            _created.Add(go);
            go.AddComponent<TeamTag>().team = Team.Enemy;
            var bomber = go.AddComponent<BomberAI>();
            InvokeAwake(bomber);
            AssertBomberHitboxes(go, "Awake fallback");
        }

        // ── Recycled-projectile guard (B012) ────────────────────────────

        [Test]
        public void Bullet_Inactive_IgnoresOnTriggerEnter2D() {
            var health = MakeBomber(new Vector3(100f, 2f, 0f), out var boxes);

            var go = new GameObject("T931_Bullet");
            _created.Add(go);
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<CircleCollider2D>().isTrigger = true;
            var bullet = go.AddComponent<Bullet>();
            InvokeAwake(bullet);
            Assert.Greater(bullet.Damage, 0f);

            go.SetActive(false);
            InvokeTrigger(bullet, boxes[0]);
            Assert.AreEqual(0f, health.CurrentDamagePercent, "a recycled (inactive) bullet must not deal damage");

            // Control: the same bullet, active, does hit.
            go.SetActive(true);
            InvokeTrigger(bullet, boxes[0]);
            Assert.AreEqual(bullet.Damage / health.MaxHitPoints * 100f, health.CurrentDamagePercent, Eps,
                "an active bullet hits through OnTriggerEnter2D");
        }

        [Test]
        public void PooledBullet_OverlappingTwoBomberBoxes_DamagesOnce() {
            var health = MakeBomber(new Vector3(100f, 2f, 0f), out var boxes);
            Bullet.InitPool(() => {
                var go = new GameObject("T931_PooledBullet");
                go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                go.AddComponent<CircleCollider2D>().isTrigger = true;
                return go.AddComponent<Bullet>();
            }, 2);

            // Spawned and hit in the same step: Pop activates it before any callback.
            var bullet = Bullet.Spawn(new Vector3(100f, 2f, 0f), Quaternion.identity, 1);
            Assert.IsNotNull(bullet);
            InvokeAwake(bullet);
            Assert.IsTrue(bullet.gameObject.activeInHierarchy, "a freshly spawned bullet is active");

            var fuselage = BoxOfSize(boxes, new Vector2(2.64f, 0.40f));
            var wing = BoxOfSize(boxes, new Vector2(0.70f, 1.02f));
            InvokeTrigger(bullet, fuselage);
            float oneHit = bullet.Damage / health.MaxHitPoints * 100f;
            Assert.AreEqual(oneHit, health.CurrentDamagePercent, Eps, "first box: one bulletDamage");
            Assert.IsFalse(bullet.gameObject.activeInHierarchy, "the bullet is recycled after the hit");

            InvokeTrigger(bullet, wing);   // second pending callback from the same step
            Assert.AreEqual(oneHit, health.CurrentDamagePercent, Eps, "B012: overlapping box must not hit again");
        }

        [Test]
        public void PooledBomb_OverlappingTwoBomberBoxes_DamagesOnce_AndRecordsDirectHit() {
            var health = MakeBomber(new Vector3(100f, 2f, 0f), out var boxes);
            Bomb.InitPool(() => {
                var go = new GameObject("T931_PooledBomb");
                go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                go.AddComponent<CircleCollider2D>().isTrigger = true;
                return go.AddComponent<Bomb>();
            }, 1);

            var fxField = typeof(FxManager).GetField("_instance", PrivateStatic);
            Assert.IsNotNull(fxField, "FxManager._instance not found by reflection");
            var fxGo = new GameObject("T931_Fx");
            _created.Add(fxGo);
            fxField.SetValue(null, fxGo.AddComponent<FxManager>());
            try {
                var bomb = Bomb.Spawn(new Vector3(100f, 2f, 0f), Vector2.zero);
                Assert.IsNotNull(bomb);
                InvokeAwake(bomb);
                Assert.AreEqual(Team.Player, bomb.Team);

                var fuselage = BoxOfSize(boxes, new Vector2(2.64f, 0.40f));
                var tail = BoxOfSize(boxes, new Vector2(0.46f, 0.50f));
                InvokeTrigger(bomb, fuselage);
                float oneHit = bomb.Damage / health.MaxHitPoints * 100f;
                Assert.AreEqual(oneHit, health.CurrentDamagePercent, Eps, "first box: one bomb hit");
                Assert.AreSame(health, typeof(Bomb).GetField("_directHit", Private).GetValue(bomb),
                    "the trigger hit is recorded so the ground burst skips it");
                Assert.IsFalse(bomb.gameObject.activeInHierarchy, "the bomb is recycled after the hit");

                InvokeTrigger(bomb, tail);
                Assert.AreEqual(oneHit, health.CurrentDamagePercent, Eps, "B012: a second box must not hit again");
            } finally {
                fxField.SetValue(null, null);
            }
        }
    }
}
