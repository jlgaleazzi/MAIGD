using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Biplanes.Audio;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Fx;
using Biplanes.Weapons;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// B037: player bullets damage AAA guns in hit points of Balance enemyHealth.aaa (125 /
    /// bulletDamage 25 = 5 bullets), any bomb hit (direct or ground burst) destroys the gun
    /// outright, flak is ignored, a kill raises EntityDestroyed("aaa") once, and non-fatal
    /// bullet hits play a throttled HitEnemy.
    ///
    /// The tests swap GameBalance._instance for a fresh Load() with aaa 125 / bulletDamage 25
    /// so the bullet count doesn't depend on the live Balance.json, and restore it after.
    /// </summary>
    public class AAAGunBulletTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();
        private GameBalance _savedBalance;
        private AudioManager _am;
        private GameObject _fxGo;
        private int _destroyedAaa;
        private int _destroyedOther;

        [SetUp]
        public void SetUp() {
            _savedBalance = (GameBalance)BalanceField().GetValue(null);
            var b = GameBalance.Load();
            Assume.That(b != null && b.enemyHealth != null && b.player != null, "Balance.json not loadable");
            b.enemyHealth.aaa = 125f;
            b.player.bulletDamage = 25f;
            BalanceField().SetValue(null, b);

            _destroyedAaa = 0;
            _destroyedOther = 0;
            GameEvents.EntityDestroyed += OnEntityDestroyed;
        }

        [TearDown]
        public void TearDown() {
            GameEvents.EntityDestroyed -= OnEntityDestroyed;
            BalanceField().SetValue(null, _savedBalance);

            if (_am != null) Object.DestroyImmediate(_am.gameObject);
            _am = null;
            typeof(AudioManager).GetField("_instance", PrivateStatic).SetValue(null, null);

            typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, null);
            if (_fxGo != null) Object.DestroyImmediate(_fxGo);
            _fxGo = null;

            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();

            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (p != null) Object.DestroyImmediate(p.gameObject);
            }
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go == null) continue;
                bool pool = go.name == "FlakShellPool" || go.name.StartsWith("Pool<");
                bool fx = go.name == "Explosion" || go.name == "FlakBurst" || go.name == "SmokePuff";
                if ((pool || fx) && (go.scene.IsValid() || go.hideFlags == HideFlags.HideAndDontSave)) {
                    Object.DestroyImmediate(go);
                }
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static FieldInfo BalanceField() {
            var f = typeof(GameBalance).GetField("_instance", PrivateStatic);
            Assert.IsNotNull(f, "GameBalance._instance not found by reflection");
            return f;
        }

        private void OnEntityDestroyed(string type, Team team) {
            if (type == "aaa" && team == Team.Enemy) _destroyedAaa++;
            else _destroyedOther++;
        }

        private AAAGun SpawnGun(float x = 30f) {
            var gun = AAAGun.Spawn(new Vector3(x, 0f, 0f));
            _created.Add(gun.gameObject);
            return gun;
        }

        private static float Bullet => GameBalance.Get().player.bulletDamage;

        private AudioManager MakeManager() {
            Assume.That(AudioManager.GetOrNull(), Is.Null, "an AudioManager leaked from another test");
            _am = AudioManager.Instance;
            Assert.IsNotNull(_am);
            Assert.IsNotNull(_am.Mix); // forces EnsureInit (Awake does not run in EditMode)
            return _am;
        }

        private float PlayCounter => (float)typeof(AudioManager).GetField("_playCounter", Private).GetValue(_am);

        private static void AssumeClip(SfxId id) {
            Assume.That(Resources.Load<AudioClip>(AudioMix.ClipPath(id)), Is.Not.Null,
                "clip " + AudioMix.ClipPath(id) + " not imported yet");
        }

        private static void SetPrivate(object target, string field, object value) {
            var f = target.GetType().GetField(field, Private);
            Assert.IsNotNull(f, $"{target.GetType().Name}.{field} not found by reflection");
            f.SetValue(target, value);
        }

        // ── Bullets ─────────────────────────────────────────────────────

        [Test]
        public void Bullets_FiveHitsOf25_Vs125_KillExactlyOnFifth() {
            var gun = SpawnGun();
            for (int i = 1; i <= 4; i++) {
                gun.ApplyDamage(Bullet, DamageSource.Bullet);
                Assert.AreEqual(i * 20f, gun.CurrentDamagePercent, 1e-3f, $"bullet {i} = 20% of 125 hp");
                Assert.IsFalse(gun.IsDead, $"alive after {i} bullet(s)");
            }
            Assert.AreEqual(0, _destroyedAaa);

            gun.ApplyDamage(Bullet, DamageSource.Bullet);
            Assert.IsTrue(gun.IsDead, "the fifth bullet kills the gun");
            Assert.AreEqual(100f, gun.CurrentDamagePercent, 1e-5f);
            Assert.AreEqual(1, _destroyedAaa, "one EntityDestroyed(\"aaa\", Enemy)");
            Assert.AreEqual(0, _destroyedOther);
        }

        [Test]
        public void Bullets_AtDeadGun_DoNothing() {
            var gun = SpawnGun();
            for (int i = 0; i < 5; i++) gun.ApplyDamage(Bullet, DamageSource.Bullet);
            Assert.IsTrue(gun.IsDead);

            gun.ApplyDamage(Bullet, DamageSource.Bullet);
            gun.ApplyDamage(Bullet, DamageSource.Bullet);
            gun.ApplyDamage(1000f, DamageSource.Bomb);
            Assert.AreEqual(100f, gun.CurrentDamagePercent, 1e-5f);
            Assert.AreEqual(1, _destroyedAaa, "no second EntityDestroyed for a dead gun");
        }

        [Test]
        public void Bullets_MissingBalanceValue_FallsBackTo125() {
            GameBalance.Get().enemyHealth.aaa = 0f;
            var gun = SpawnGun();
            gun.ApplyDamage(25f, DamageSource.Bullet);
            Assert.AreEqual(20f, gun.CurrentDamagePercent, 1e-3f, "25 of fallback 125 hp = 20%");
        }

        [Test]
        public void Bullets_UseBalanceHitPoints() {
            GameBalance.Get().enemyHealth.aaa = 100f;
            var gun = SpawnGun();
            gun.ApplyDamage(25f, DamageSource.Bullet);
            Assert.AreEqual(25f, gun.CurrentDamagePercent, 1e-3f, "25 of Balance 100 hp = 25%");
        }

        [Test]
        public void Bullets_FourHits_ReachSmokeRange() {
            var gun = SpawnGun();
            for (int i = 0; i < 4; i++) gun.ApplyDamage(Bullet, DamageSource.Bullet);
            Assert.GreaterOrEqual(gun.CurrentDamagePercent, 70f, "bullet damage feeds the >= 70% smoke tint");
            Assert.IsFalse(gun.IsDead);
        }

        // ── Bombs, flak, collision ──────────────────────────────────────

        [Test]
        public void Bomb_FreshGun_AnyDamageValue_KillsOutright() {
            var gun = SpawnGun();
            gun.ApplyDamage(1f, DamageSource.Bomb);
            Assert.IsTrue(gun.IsDead, "any bomb hit destroys the gun regardless of its damage value");
            Assert.AreEqual(1, _destroyedAaa);
        }

        [Test]
        public void Bomb_AfterBullets_KillsOutright_OnceOnly() {
            var gun = SpawnGun();
            gun.ApplyDamage(Bullet, DamageSource.Bullet);
            gun.ApplyDamage(Bullet, DamageSource.Bullet);
            Assert.IsFalse(gun.IsDead);
            gun.ApplyDamage(1f, DamageSource.Bomb);
            Assert.IsTrue(gun.IsDead);
            Assert.AreEqual(1, _destroyedAaa);
        }

        [Test]
        public void Bomb_GroundBurst_KillsGun() {
            var gun = SpawnGun(20f);
            Physics2D.SyncTransforms();
            Assume.That(Physics2D.OverlapPoint(gun.GetComponent<Collider2D>().bounds.center) != null,
                "Physics2D queries return nothing in this EditMode run");

            var go = new GameObject("B037_Bomb");
            _created.Add(go);
            go.transform.position = new Vector3(20f, -0.6f, 0f);
            var bomb = go.AddComponent<Bomb>();
            var awake = typeof(Bomb).GetMethod("Awake", Private);
            Assert.IsNotNull(awake, "Bomb.Awake not found by reflection");
            awake.Invoke(bomb, null);   // EditMode AddComponent doesn't run Awake
            typeof(Projectile).GetField("damage", Private).SetValue(bomb, 1f);   // tiny: still fatal
            var burst = typeof(Bomb).GetMethod("BurstDamage", Private);
            Assert.IsNotNull(burst, "Bomb.BurstDamage not found by reflection");
            burst.Invoke(bomb, new object[] { new Vector2(20f, -0.6f) });

            Assert.IsTrue(gun.IsDead, "a bomb ground burst destroys the gun outright");
            Assert.AreEqual(1, _destroyedAaa);
        }

        [Test]
        public void Flak_IsIgnored() {
            var gun = SpawnGun();
            gun.ApplyDamage(1000f, DamageSource.Flak);
            Assert.AreEqual(0f, gun.CurrentDamagePercent);
            Assert.IsFalse(gun.IsDead);
            Assert.AreEqual(0, _destroyedAaa);
        }

        [Test]
        public void Collision_StillFatal() {
            var gun = SpawnGun();
            gun.ApplyDamage(1f, DamageSource.Collision);
            Assert.IsTrue(gun.IsDead);
            Assert.AreEqual(1, _destroyedAaa);
        }

        // ── Audio ───────────────────────────────────────────────────────

        [Test]
        public void HitEnemy_NonFatalBullet_Throttled_IgnoredHitsSilent_FatalNoTick() {
            AssumeClip(SfxId.HitEnemy);
            MakeManager();
            var gun = SpawnGun();

            float before = PlayCounter;
            gun.ApplyDamage(1000f, DamageSource.Flak);
            Assert.AreEqual(before, PlayCounter, "an ignored hit is silent");

            gun.ApplyDamage(Bullet, DamageSource.Bullet);
            Assert.AreEqual(before + 1f, PlayCounter, "a non-fatal bullet hit ticks once");

            gun.ApplyDamage(Bullet, DamageSource.Bullet);
            Assert.AreEqual(before + 1f, PlayCounter, "a second hit inside HitCueMinInterval is throttled");

            SetPrivate(gun, "_lastHitCueTime", -1f);   // as if the interval passed
            gun.ApplyDamage(Bullet, DamageSource.Bullet);
            Assert.AreEqual(before + 2f, PlayCounter, "after the interval the next hit ticks again");

            SetPrivate(gun, "_lastHitCueTime", -1f);
            gun.ApplyDamage(Bullet, DamageSource.Bullet);   // 4th: 80%, still alive
            Assert.AreEqual(before + 3f, PlayCounter);

            SetPrivate(gun, "_lastHitCueTime", -1f);
            gun.ApplyDamage(Bullet, DamageSource.Bullet);   // 5th: fatal
            Assert.IsTrue(gun.IsDead);
            Assert.AreEqual(before + 3f, PlayCounter, "the fatal hit plays no HitEnemy (HandleDeath owns the explosion)");

            gun.ApplyDamage(Bullet, DamageSource.Bullet);
            Assert.AreEqual(before + 3f, PlayCounter, "hits on a dead gun are silent");
        }

        [Test]
        public void BulletKill_HandleDeath_PlaysOneExplosion() {
            AssumeClip(SfxId.Explosion);
            MakeManager();
            _fxGo = new GameObject("B037_Fx");
            typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, _fxGo.AddComponent<FxManager>());
            var gun = SpawnGun();
            for (int i = 0; i < 5; i++) gun.ApplyDamage(Bullet, DamageSource.Bullet);
            Assert.IsTrue(gun.IsDead);

            float before = PlayCounter;
            LogAssert.ignoreFailingMessages = true;   // HandleDeath's Destroy() logs in EditMode
            var handleDeath = typeof(AAAGun).GetMethod("HandleDeath", Private);
            Assert.IsNotNull(handleDeath, "AAAGun.HandleDeath not found by reflection");
            handleDeath.Invoke(gun, null);
            Assert.AreEqual(before + 1f, PlayCounter, "one Explosion cue for a bullet kill");
            Assert.AreEqual(1, _destroyedAaa);
        }
    }
}
