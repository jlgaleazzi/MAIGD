using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Biplanes.Fx;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// T945 (QA of B030): FxManager playback, caps and bookkeeping, and the FlakShell bursts.
    ///
    /// EditMode doesn't run Awake on AddComponent, so FxManager is seeded into _instance by
    /// reflection (Instance would call DontDestroyOnLoad, Play mode only). Update doesn't run
    /// either, so the private Tick(dt) is invoked by reflection. Tests never tick an effect to
    /// its end: Tick then calls Destroy, which EditMode rejects.
    /// </summary>
    public class FxManagerTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private static readonly string[] FxNames = { "Explosion", "FlakBurst", "SmokePuff" };

        private GameObject _fxGo;
        private FxManager _fx;
        private GameObject _plane;

        [SetUp]
        public void SetUp() {
            DestroyFxObjects();
            _fxGo = new GameObject("T945_Fx");
            _fx = _fxGo.AddComponent<FxManager>();
            InstanceField().SetValue(null, _fx);
        }

        [TearDown]
        public void TearDown() {
            InstanceField().SetValue(null, null);
            if (_fxGo != null) Object.DestroyImmediate(_fxGo);
            if (_plane != null) Object.DestroyImmediate(_plane);
            _plane = null;
            DestroyFxObjects();

            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (p != null) Object.DestroyImmediate(p.gameObject);
            }
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go == null) continue;
                if (go.name == "FlakShellPool" || go.name.StartsWith("Pool<")) {
                    if (go.scene.IsValid() || go.hideFlags == HideFlags.HideAndDontSave) Object.DestroyImmediate(go);
                }
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static FieldInfo InstanceField() {
            var f = typeof(FxManager).GetField("_instance", PrivateStatic);
            Assert.IsNotNull(f, "FxManager._instance not found by reflection");
            return f;
        }

        private static void DestroyFxObjects() {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go == null || !go.scene.IsValid()) continue;
                if (System.Array.IndexOf(FxNames, go.name) >= 0) Object.DestroyImmediate(go);
            }
        }

        private static List<GameObject> SceneObjectsNamed(string name) {
            var list = new List<GameObject>();
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go != null && go.name == name && go.scene.IsValid()) list.Add(go);
            }
            return list;
        }

        private void Tick(float dt) {
            var tick = typeof(FxManager).GetMethod("Tick", Private);
            Assert.IsNotNull(tick, "FxManager.Tick not found by reflection");
            tick.Invoke(_fx, new object[] { dt });
        }

        private static void AssumeSprites() {
            Assume.That(Resources.Load<Sprite>("Sprites/fx/explosion_0") != null
                        && Resources.Load<Sprite>("Sprites/fx/flak_burst_0") != null
                        && Resources.Load<Sprite>("Sprites/fx/smoke_puff") != null,
                "FX sprites not importable in this editor");
        }

        // ── Singleton ───────────────────────────────────────────────────

        [Test]
        public void GetOrNull_EditModeWithoutInstance_ReturnsNull_CreatesNoManager() {
            InstanceField().SetValue(null, null);
            int before = SceneObjectsNamed("FxManager").Count;
            Assert.IsNull(FxManager.GetOrNull(), "outside Play mode no manager is created");
            Assert.AreEqual(before, SceneObjectsNamed("FxManager").Count);

            InstanceField().SetValue(null, _fx);
            Assert.AreSame(_fx, FxManager.GetOrNull(), "an existing manager is returned");
        }

        // ── Explosion ───────────────────────────────────────────────────

        [Test]
        public void TriggerExplosion_FramesStepAt008_GrowStaysGentle_FadesOnlyAtTheEnd() {
            AssumeSprites();
            _fx.TriggerExplosion(new Vector3(2f, 3f, 0f));

            var all = SceneObjectsNamed("Explosion");
            Assert.AreEqual(1, all.Count, "one 'Explosion' object (name kept for older tests)");
            var go = all[0];
            var sr = go.GetComponent<SpriteRenderer>();
            Assert.AreEqual(new Vector3(2f, 3f, 0f), go.transform.position);
            Assert.AreEqual(10, sr.sortingOrder, "ARCHITECTURE draw order");
            Assert.AreEqual("explosion_0", sr.sprite.name);
            Assert.AreEqual(1f, sr.color.a, 1e-5f);

            // 0.08 s per frame: frames 1..5 at 0.08, 0.16, ... 0.40 s.
            for (int i = 1; i <= 5; i++) {
                Tick(0.08f);
                Assert.AreEqual("explosion_" + i, sr.sprite.name, $"frame at t = {0.08f * i:0.00}");
                float s = go.transform.localScale.x;
                Assert.That(s, Is.InRange(0.8f, 1.2f), "no 0.1x->2x zoom");
                if (i <= 4) Assert.AreEqual(1f, sr.color.a, 1e-4f, "opaque until the last frames");
            }
            Assert.Less(sr.color.a, 1f, "fading during the last frame");
            Assert.Greater(sr.color.a, 0f);
            Assert.AreEqual(1, _fx.CountActive(FxManager.Kind.Explosion));
        }

        [Test]
        public void Caps_PerKind_OldestReused_CountsMatchSceneObjects() {
            AssumeSprites();
            for (int i = 0; i < 25; i++) _fx.TriggerExplosion(new Vector3(i, 0f, 0f));
            for (int i = 0; i < 20; i++) _fx.TriggerFlakBurst(new Vector3(i, 1f, 0f));
            for (int i = 0; i < 30; i++) _fx.SpawnSmokePuff(new Vector3(i, 2f, 0f));

            Assert.AreEqual(20, _fx.CountActive(FxManager.Kind.Explosion));
            Assert.AreEqual(16, _fx.CountActive(FxManager.Kind.FlakBurst));
            Assert.AreEqual(24, _fx.CountActive(FxManager.Kind.SmokePuff));
            Assert.AreEqual(20, SceneObjectsNamed("Explosion").Count, "at the cap objects are reused, not added");
            Assert.AreEqual(16, SceneObjectsNamed("FlakBurst").Count);
            Assert.AreEqual(24, SceneObjectsNamed("SmokePuff").Count);

            // The 5 oldest explosions (x = 0..4) were restarted at x = 20..24.
            var xs = new HashSet<float>();
            foreach (var go in SceneObjectsNamed("Explosion")) xs.Add(go.transform.position.x);
            for (int i = 0; i < 5; i++) Assert.IsFalse(xs.Contains(i), $"oldest at x = {i} reused");
            for (int i = 5; i < 25; i++) Assert.IsTrue(xs.Contains(i), $"x = {i} live");
        }

        [Test]
        public void DestroyedEffects_ArePruned_CountsDoNotDrift() {
            AssumeSprites();
            for (int i = 0; i < 3; i++) _fx.TriggerExplosion(new Vector3(i, 0f, 0f));
            Object.DestroyImmediate(SceneObjectsNamed("Explosion")[0]);   // like a scene unload
            Assert.AreEqual(2, _fx.CountActive(FxManager.Kind.Explosion));

            Assert.DoesNotThrow(() => Tick(0.05f), "Tick skips destroyed entries");
            for (int i = 0; i < 20; i++) _fx.TriggerExplosion(Vector3.zero);
            Assert.AreEqual(20, _fx.CountActive(FxManager.Kind.Explosion));
            Assert.AreEqual(20, SceneObjectsNamed("Explosion").Count);
        }

        // ── Smoke ───────────────────────────────────────────────────────

        [Test]
        public void SmokePuff_WorldSpace_RisesGrowsFades_NoVerticalFlip() {
            AssumeSprites();
            _fx.SpawnSmokePuff(new Vector3(1f, 0f, 0f));
            var go = SceneObjectsNamed("SmokePuff")[0];
            var sr = go.GetComponent<SpriteRenderer>();
            Assert.IsNull(go.transform.parent, "world space: not parented to the plane");
            Assert.AreEqual("smoke_puff", sr.sprite.name);
            Assert.IsFalse(sr.flipY);
            Assert.AreEqual(0.9f, sr.color.a, 1e-4f);

            Tick(0.6f);
            Assert.AreEqual(0.18f, go.transform.position.y, 1e-3f, "rises 0.3 u/s");
            Assert.AreEqual(1f, go.transform.position.x, 1e-5f);
            Assert.AreEqual(1.3f, go.transform.localScale.x, 1e-3f, "1.0x -> 1.6x over 1.2 s");
            Assert.AreEqual(0.45f, sr.color.a, 1e-3f, "fades over its life");
        }

        [Test]
        public void TickAtZeroDt_FreezesEffects() {
            AssumeSprites();
            _fx.SpawnSmokePuff(Vector3.zero);
            var go = SceneObjectsNamed("SmokePuff")[0];
            Tick(0f);
            Tick(0f);
            Assert.AreEqual(0f, go.transform.position.y, 1e-6f, "paused (timeScale 0): no motion");
            Assert.AreEqual(1, _fx.CountActive(FxManager.Kind.SmokePuff));
        }

        // ── Missing sprites ─────────────────────────────────────────────

        [Test]
        public void LoadFrames_MissingFrames_WarnOnce_KeepExisting_NeverThrow() {
            AssumeSprites();
            var load = typeof(FxManager).GetMethod("LoadFrames", PrivateStatic);
            Assert.IsNotNull(load, "FxManager.LoadFrames not found by reflection");

            LogAssert.Expect(LogType.Warning, new Regex("explosion_6, Sprites/fx/explosion_7"));
            var some = (Sprite[])load.Invoke(null, new object[] { "Sprites/fx/explosion_", 8 });
            Assert.AreEqual(6, some.Length, "the frames that exist still play");

            LogAssert.Expect(LogType.Warning, new Regex("skipped"));
            var none = (Sprite[])load.Invoke(null, new object[] { "Sprites/fx/t945_missing_", 3 });
            Assert.IsNotNull(none);
            Assert.AreEqual(0, none.Length);
        }

        [Test]
        public void NoFramesCached_EffectSkipped_NoObject_NoThrow() {
            var cache = typeof(FxManager).GetField("_smokeFrames", Private);
            Assert.IsNotNull(cache, "FxManager._smokeFrames not found by reflection");
            cache.SetValue(_fx, new Sprite[0]);
            Assert.DoesNotThrow(() => _fx.SpawnSmokePuff(Vector3.zero));
            Assert.AreEqual(0, SceneObjectsNamed("SmokePuff").Count);
            Assert.AreEqual(0, _fx.CountActive(FxManager.Kind.SmokePuff));
            LogAssert.NoUnexpectedReceived();
        }

        // ── FlakShell bursts ────────────────────────────────────────────

        [Test]
        public void FlakShell_LifetimeEnd_Bursts_PlainRecycle_DoesNot() {
            AssumeSprites();
            var shell = FlakShell.Spawn(new Vector3(3f, 4f, 0f), Vector2.up);
            Assert.IsNotNull(shell);
            shell.OnLifetimeEnd();
            Assert.AreEqual(1, _fx.CountActive(FxManager.Kind.FlakBurst), "airburst at lifetime end");
            Assert.AreEqual(new Vector3(3f, 4f, 0f), SceneObjectsNamed("FlakBurst")[0].transform.position);
            Assert.IsFalse(shell.gameObject.activeSelf, "back in the pool");

            // Already pooled: a late lifetime callback shows nothing.
            shell.OnLifetimeEnd();
            Assert.AreEqual(1, _fx.CountActive(FxManager.Kind.FlakBurst));

            var other = FlakShell.Spawn(Vector3.zero, Vector2.up);
            other.Recycle();
            Assert.AreEqual(1, _fx.CountActive(FxManager.Kind.FlakBurst), "a plain Recycle shows no burst");
        }

        // ── PlayerPlane smoke trail ─────────────────────────────────────

        [Test]
        public void PlayerSmoke_OnlyAtThreshold_BehindTail_EveryInterval_StopsWhenLandedOrPaused() {
            AssumeSprites();
            var g = Core.GameBalance.Get();
            Assume.That(g != null && g.damageTaken != null && g.player != null, "Balance.json not loadable");

            var plane = PlayerPlane_Create(new Vector3(0f, 1f, 0f));
            var health = plane.GetComponent<Core.Health>();
            var emit = typeof(Player.PlayerPlane).GetMethod("EmitSmoke", Private);
            Assert.IsNotNull(emit, "PlayerPlane.EmitSmoke not found by reflection");

            emit.Invoke(plane, new object[] { 0.5f });
            Assert.AreEqual(0, _fx.CountActive(FxManager.Kind.SmokePuff), "no smoke below the threshold");

            while (health.CurrentDamagePercent < g.player.smokeThreshold) health.ApplyDamage(1f, Core.DamageSource.Flak);
            Assume.That(health.CurrentDamagePercent < 100f, "threshold reached without dying");

            emit.Invoke(plane, new object[] { 0.016f });
            Assert.AreEqual(1, _fx.CountActive(FxManager.Kind.SmokePuff), "first puff at once");
            var puff = SceneObjectsNamed("SmokePuff")[0];
            Vector3 expected = plane.transform.position - plane.transform.rotation * Vector3.right * 0.75f;
            Assert.AreEqual(expected.x, puff.transform.position.x, 1e-4f, "behind the tail (facing right)");
            Assert.AreEqual(expected.y, puff.transform.position.y, 1e-4f);

            emit.Invoke(plane, new object[] { 0.05f });
            Assert.AreEqual(1, _fx.CountActive(FxManager.Kind.SmokePuff), "not before SmokePuffInterval");
            emit.Invoke(plane, new object[] { 0.08f });
            Assert.AreEqual(2, _fx.CountActive(FxManager.Kind.SmokePuff), "next puff after 0.12 s");

            emit.Invoke(plane, new object[] { 0f });
            emit.Invoke(plane, new object[] { 0f });
            Assert.AreEqual(2, _fx.CountActive(FxManager.Kind.SmokePuff), "paused: no puffs");

            typeof(Player.PlayerPlane).GetField("_crashLanded", Private).SetValue(plane, true);
            emit.Invoke(plane, new object[] { 1f });
            Assert.AreEqual(2, _fx.CountActive(FxManager.Kind.SmokePuff), "a landed wreck stops smoking");
        }

        private Player.PlayerPlane PlayerPlane_Create(Vector3 position) {
            var plane = Player.PlayerPlane.Create(position, false);
            _plane = plane.gameObject;
            var awake = typeof(Core.Health).GetMethod("Awake", Private);
            Assert.IsNotNull(awake, "Health.Awake not found by reflection");
            awake.Invoke(plane.GetComponent<Core.Health>(), null);
            return plane;
        }


        [Test]
        public void FlakShell_LifetimeEnd_WithoutFxManager_NoThrow_NoManagerCreated() {
            InstanceField().SetValue(null, null);
            var shell = FlakShell.Spawn(Vector3.zero, Vector2.up);
            Assert.DoesNotThrow(() => shell.OnLifetimeEnd());
            Assert.AreEqual(0, SceneObjectsNamed("FxManager").Count);
            Assert.AreEqual(0, SceneObjectsNamed("FlakBurst").Count);
        }
    }
}
