using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Missions;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// T957 (QA of B035-B039, V005/V006): the player bullet is a 3x1 ink line burst, the
    /// player_bomb sprite loads at the agreed size, Balance carries the 3-hit fighter and
    /// 5-bullet AAA numbers, and EnemyPressure's in-view scan survives every aircraft type
    /// MissionRunner registers (BomberAI is not an IDamageable).
    ///
    /// EditMode doesn't run Awake on AddComponent, so Bullet.Awake is invoked by reflection,
    /// the way Play mode would run it.
    /// </summary>
    public class WhiteOnlyAndPressureQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Color Ink = new Color(0xE8 / 255f, 0xF1 / 255f, 0xFA / 255f, 1f);

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        private static void AssertInk(Color c, string label) {
            Assert.AreEqual(Ink.r, c.r, 2e-3f, label + " r");
            Assert.AreEqual(Ink.g, c.g, 2e-3f, label + " g");
            Assert.AreEqual(Ink.b, c.b, 2e-3f, label + " b");
            Assert.AreEqual(1f, c.a, 2e-3f, label + " a");
        }

        // ── B039: player bullet sprite ────────────────────────────────────

        [Test]
        public void PlayerBullet_Awake_Uses3x1SolidInkLine_At100Ppu_Untinted() {
            var go = new GameObject("T957_Bullet");
            _created.Add(go);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.color = Color.red;   // whatever the placeholder had, Awake must reset it
            var bullet = go.AddComponent<Bullet>();
            typeof(Bullet).GetMethod("Awake", Private).Invoke(bullet, null);

            Assert.IsNotNull(sr.sprite);
            Assert.AreEqual(Color.white, sr.color, "the pixels carry the ink: no tint");
            Assert.AreEqual(3f, sr.sprite.rect.width, 1e-4f);
            Assert.AreEqual(1f, sr.sprite.rect.height, 1e-4f);
            Assert.AreEqual(100f, sr.sprite.pixelsPerUnit, 1e-4f);
            for (int x = 0; x < 3; x++) AssertInk(sr.sprite.texture.GetPixel(x, 0), "pixel " + x);
            Assert.AreEqual(FilterMode.Point, sr.sprite.texture.filterMode);
            Assert.AreEqual(Team.Player, bullet.Team);
        }

        // ── A-BOMB / B036: player bomb sprite ─────────────────────────────

        [Test]
        public void PlayerBombSprite_Loads_32x14_At100Ppu() {
            var sprite = Resources.Load<Sprite>("Sprites/player/player_bomb");
            Assert.IsNotNull(sprite, "Resources/Sprites/player/player_bomb must import as a Sprite");
            Assert.AreEqual(32f, sprite.rect.width, 1e-4f);
            Assert.AreEqual(14f, sprite.rect.height, 1e-4f);
            Assert.AreEqual(100f, sprite.pixelsPerUnit, 1e-3f, "SpriteImportSettings: 100 px/unit");
        }

        // ── Balance numbers behind the GDD hit counts ─────────────────────

        [Test]
        public void Balance_FighterThreeBullets_AaaFiveBullets_BombOneShotsAaa() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.enemyHealth != null && g.player != null, "Balance.json not loadable");
            float b = g.player.bulletDamage;
            Assert.AreEqual(3, Mathf.CeilToInt(g.enemyHealth.fighter / b - 1e-4f), "GDD: three hits bring a fighter down");
            Assert.Greater(g.enemyHealth.fighter, 2f * b, "two hits must not kill a fighter");
            Assert.AreEqual(5, Mathf.CeilToInt(g.enemyHealth.aaa / b - 1e-4f), "GDD: five hits destroy an AAA gun");
            Assert.Greater(g.enemyHealth.aaa, 4f * b, "four hits must not kill an AAA gun");
        }

        [Test]
        public void AaaGun_FiveBalanceBullets_Kill_FourDoNot() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.player != null, "Balance.json not loadable");
            var gun = AAAGun.Spawn(new Vector3(10f, -3f, 0f));
            _created.Add(gun.gameObject);
            for (int i = 0; i < 4; i++) gun.ApplyDamage(g.player.bulletDamage, DamageSource.Bullet);
            Assert.IsFalse(gun.IsDead, "four bullets");
            gun.ApplyDamage(g.player.bulletDamage, DamageSource.Bullet);
            Assert.IsTrue(gun.IsDead, "five bullets");
        }

        // ── B035: EnemyPressure in-view scan with every registered aircraft type ─

        private EnemyPressure MakePressure(out List<MonoBehaviour> aircraft) {
            var go = new GameObject("T957_Runner");
            _created.Add(go);
            var runner = go.AddComponent<MissionRunner>();
            aircraft = (List<MonoBehaviour>)typeof(MissionRunner)
                .GetField("_enemyAircraft", Private).GetValue(runner);
            var pressure = go.AddComponent<EnemyPressure>();
            pressure.Init(runner, PressureData.Resolve(null));
            return pressure;
        }

        private static bool AnyInView(EnemyPressure pressure, Rect view) {
            var m = typeof(EnemyPressure).GetMethod("AnyEnemyAircraftInView", Private);
            Assert.IsNotNull(m, "EnemyPressure.AnyEnemyAircraftInView not found");
            return (bool)m.Invoke(pressure, new object[] { view });
        }

        /// <summary>MissionRunner registers BomberAI in EnemyAircraft (mission 2). BomberAI is
        /// not an IDamageable, so the scan must not cast it to one. Before the fix this threw
        /// InvalidCastException every frame the bomber was reached in the list.</summary>
        [Test]
        public void Pressure_InViewScan_WithABomberOffScreen_DoesNotThrow_AndIsFalse() {
            var pressure = MakePressure(out var aircraft);
            var bomber = BomberAI.Spawn(new Vector3(500f, 2f, 0f), "bomber_1");
            _created.Add(bomber.gameObject);
            aircraft.Add(bomber);

            var view = new Rect(-8.9f, -5f, 17.8f, 10f);
            bool inView = true;
            Assert.DoesNotThrow(() => inView = AnyInView(pressure, view),
                "a live bomber in EnemyAircraft must not break the quiet-time scan");
            Assert.IsFalse(inView, "the bomber is far off screen");
        }

        [Test]
        public void Pressure_InViewScan_SeesABomberOnScreen() {
            var pressure = MakePressure(out var aircraft);
            var bomber = BomberAI.Spawn(new Vector3(0f, 1f, 0f), "bomber_1");
            _created.Add(bomber.gameObject);
            aircraft.Add(bomber);

            var view = new Rect(-8.9f, -5f, 17.8f, 10f);
            bool inView = false;
            Assert.DoesNotThrow(() => inView = AnyInView(pressure, view));
            Assert.IsTrue(inView, "GDD Enemy pressure: a bomber on screen is an enemy aircraft in view");
        }
    }
}
