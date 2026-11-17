using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Biplanes.Audio;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Fx;
using Biplanes.Missions;
using Biplanes.Player;
using Biplanes.Weapons;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// T955 (QA re-verify of B032/B033/B034, the T953 audio bugs):
    ///  • B2: the crash cue starts once, 0.1 s before the wreck lands (crunch with the impact),
    ///    or at elapsed CrashFallSeconds - 0.1 when the result freeze cuts a long fall. Checked
    ///    as a pure fall simulation and through the real PlayerPlane.UpdateCrashFall.
    ///  • B3: on scene unload only non-screen UI cues (Crash) are stopped, set up through
    ///    _uiIds directly so it doesn't depend on AudioSource.isPlaying in EditMode.
    ///  • B4/B5/B6: one Explosion per bomb kill, the AAA death explosion, turret HitEnemy
    ///    (throttled) and FlakFire per spawned shell.
    ///  • B8: PlayerGun / BombRelease only for a spawned projectile; a null Bomb.Spawn spends
    ///    no bomb and raises no BombsChanged.
    /// Cues are counted with AudioManager._playCounter (one per cue actually started).
    /// </summary>
    public class AudioFixesQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const float FixedDt = 0.02f;

        private readonly List<GameObject> _created = new List<GameObject>();
        private AudioManager _am;
        private GameObject _fxGo;
        private float _timeScale;

        [SetUp]
        public void SetUp() {
            _timeScale = Time.timeScale;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        [TearDown]
        public void TearDown() {
            Time.timeScale = _timeScale;
            AudioListener.pause = false;

            if (_am != null) Object.DestroyImmediate(_am.gameObject);
            _am = null;
            typeof(AudioManager).GetField("_instance", PrivateStatic).SetValue(null, null);
            typeof(PlayerPlane).GetField("_audioOwner", PrivateStatic).SetValue(null, null);

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

        private AudioManager MakeManager() {
            Assume.That(AudioManager.GetOrNull(), Is.Null, "an AudioManager leaked from another test");
            _am = AudioManager.Instance;
            Assert.IsNotNull(_am);
            Assert.IsNotNull(_am.Mix); // forces EnsureInit (Awake does not run in EditMode)
            return _am;
        }

        private void SeedFx() {
            _fxGo = new GameObject("T955_Fx");
            typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, _fxGo.AddComponent<FxManager>());
        }

        private static void AssumeClip(SfxId id) {
            Assume.That(Resources.Load<AudioClip>(AudioMix.ClipPath(id)), Is.Not.Null,
                "clip " + AudioMix.ClipPath(id) + " not imported yet");
        }

        private float PlayCounter => (float)typeof(AudioManager).GetField("_playCounter", Private).GetValue(_am);

        private static object Get(object target, string field) {
            var f = target.GetType().GetField(field, Private);
            Assert.IsNotNull(f, $"{target.GetType().Name}.{field} not found by reflection");
            return f.GetValue(target);
        }

        private static void Set(object target, string field, object value) {
            var f = target.GetType().GetField(field, Private);
            Assert.IsNotNull(f, $"{target.GetType().Name}.{field} not found by reflection");
            f.SetValue(target, value);
        }

        private static void Invoke(object target, string method, params object[] args) {
            var m = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found by reflection");
            m.Invoke(target, args);
        }

        /// <summary>The clip of the most recently started gameplay one-shot voice.</summary>
        private AudioClip LastGameplayClip() {
            var voices = (AudioSource[])Get(_am, "_voices");
            var order = (float[])Get(_am, "_voiceOrder");
            int best = -1;
            for (int i = 0; i < voices.Length; i++) {
                if (order[i] > 0f && (best < 0 || order[i] > order[best])) best = i;
            }
            return best >= 0 ? voices[best].clip : null;
        }

        private static AudioClip Clip(SfxId id) => Resources.Load<AudioClip>(AudioMix.ClipPath(id));

        private static float PrivateConst(Type t, string name) {
            var f = t.GetField(name, PrivateStatic | BindingFlags.Public);
            Assert.IsNotNull(f, $"{t.Name}.{name} not found by reflection");
            return Convert.ToSingle(f.GetValue(null));
        }

        private static void DisposePool(Type projectileType) {
            var f = projectileType.GetField("_pool", PrivateStatic);
            Assert.IsNotNull(f, $"{projectileType.Name}._pool not found by reflection");
            var pool = f.GetValue(null);
            Assume.That(pool, Is.Not.Null, projectileType.Name + " pool not initialised");
            pool.GetType().GetMethod("Dispose").Invoke(pool, null);
        }

        /// <summary>Team.Enemy target that dies on the first hit when `fatal`.</summary>
        private sealed class StubTarget : IDamageable {
            private readonly bool _fatal;
            public bool Dead;
            public int Hits;
            public StubTarget(bool fatal) { _fatal = fatal; }
            public Team Team => Team.Enemy;
            public bool IsDead => Dead;
            public void ApplyDamage(float percent, DamageSource src) { Hits++; if (_fatal) Dead = true; }
            public void Heal(float amount) { }
        }

        private Bomb MakeLooseBomb() {
            var go = new GameObject("T955_Bomb");
            _created.Add(go);
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<BoxCollider2D>().isTrigger = true;
            return go.AddComponent<Bomb>();
        }

        // ── B3: scene unload stops only non-screen UI cues ──────────────

        [Test]
        public void SceneUnload_ClearsCrashUiVoice_KeepsScreenCues_Deterministic() {
            var am = MakeManager();
            var ids = (SfxId[])Get(am, "_uiIds");
            Assert.AreEqual(4, ids.Length);
            ids[0] = SfxId.Crash;
            ids[1] = SfxId.UiClick;
            ids[2] = SfxId.Lose;
            ids[3] = SfxId.UiPage;

            Invoke(am, "OnSceneUnloaded", default(Scene));

            Assert.AreNotEqual(SfxId.Crash, ids[0], "the crash voice is stopped and its id cleared");
            Assert.AreEqual(SfxId.UiClick, ids[1], "the Retry/Menu click keeps playing");
            Assert.AreEqual(SfxId.Lose, ids[2], "the lose sting is a screen cue and is kept");
            Assert.AreEqual(SfxId.UiPage, ids[3]);
        }

        [Test]
        public void KeepsPlayingOnSceneUnload_FalseForEveryGameplayCue() {
            var screen = new HashSet<SfxId> {
                SfxId.UiClick, SfxId.UiPage, SfxId.UiPause, SfxId.Typewriter, SfxId.Stamp, SfxId.Win, SfxId.Lose
            };
            foreach (SfxId id in Enum.GetValues(typeof(SfxId))) {
                Assert.AreEqual(screen.Contains(id), AudioManager.KeepsPlayingOnSceneUnload(id), id.ToString());
            }
        }

        // ── B2: crash cue timing (pure fall simulation) ─────────────────

        /// <summary>Same integration as PlayerPlane.UpdateCrashFall: velocity, then position,
        /// then the start rule on the moved position, then the landing test. Returns the
        /// elapsed time of the start tick and of the landing tick (-1 if not reached before
        /// the result freeze at CrashFallSeconds).</summary>
        private static void SimulateFall(float height, float vy0, float gravity,
                                         out float start, out float land, out int starts) {
            float y = height, vy = vy0, elapsed = 0f;
            start = -1f; land = -1f; starts = 0;
            bool played = false;
            int maxTicks = Mathf.RoundToInt(PlayerPlane.CrashFallSeconds / FixedDt);
            for (int tick = 0; tick < maxTicks; tick++) {
                elapsed += FixedDt;
                vy -= gravity * FixedDt;
                y += vy * FixedDt;
                if (!played && PlayerPlane.ShouldStartCrashSound(PlayerPlane.CrashTimeToGround(y, vy, gravity), elapsed)) {
                    played = true; starts++; start = elapsed;
                }
                if (y <= 0f) {
                    if (!played) { played = true; starts++; start = elapsed; }
                    land = elapsed;
                    return;
                }
            }
        }

        [Test]
        public void CrashFall_CrunchLandsWithTheWreck_OrMarksTheResultScreen() {
            float g = PrivateConst(typeof(PlayerPlane), "CrashGravity");
            float offset = PlayerPlane.CrashCrunchOffset;
            float freeze = PlayerPlane.CrashFallSeconds;
            Assert.AreEqual(0.1f, offset, 1e-4f);

            // Heights above the rest point, from a ground crash to the top clamp (y 4.5 -> -4.3).
            float[] heights = { 0.5f, 1f, 2f, 3f, 5f, 6.3f, 7.5f, 8.8f };
            float[] dives = { 0f, -3f, -6f };
            foreach (float h in heights) {
                foreach (float vy in dives) {
                    SimulateFall(h, vy, g, out float start, out float land, out int starts);
                    string label = $"h {h}, vy {vy}";
                    Assert.AreEqual(1, starts, label + ": exactly one crash cue");
                    float crunch = start + offset;
                    if (land > 0f && land - FixedDt > offset) {
                        Assert.AreEqual(land, crunch, FixedDt + 1e-3f, label + ": the crunch lands with the wreck");
                    } else if (land < 0f) {
                        Assert.AreEqual(freeze - offset, start, FixedDt + 1e-3f, label + ": fallback start");
                        Assert.AreEqual(freeze, crunch, FixedDt + 1e-3f, label + ": the crunch marks the result screen");
                    }
                    Assert.LessOrEqual(crunch, freeze + FixedDt + 1e-3f,
                        label + ": the crunch never comes after the result screen opens");
                }
            }
        }

        [Test]
        public void CrashFall_TopClampFall_IsLongerThanTheFreeze_SoTheFallbackIsNeeded() {
            float g = PrivateConst(typeof(PlayerPlane), "CrashGravity");
            float rest = PrivateConst(typeof(PlayerPlane), "CrashRestHeight");
            float top = 4.5f - (TerrainStrip.GroundBaseY + rest);
            Assert.Greater(PlayerPlane.CrashTimeToGround(top, 0f, g), PlayerPlane.CrashFallSeconds,
                "a level crash at the top clamp is still falling when the result screen freezes time");
            SimulateFall(top, 0f, g, out float start, out float land, out int starts);
            Assert.AreEqual(1, starts);
            Assert.Less(land, 0f, "the wreck hasn't landed by the freeze");
            Assert.Greater(start, 0f, "the fallback still plays the crunch");
        }

        // ── B2: the real UpdateCrashFall plays Crash on the UI pool exactly once ──

        [Test]
        public void UpdateCrashFall_PlaysCrashOnce_OnTheUiPool_BeforeLanding() {
            Assume.That(TerrainStrip.Instance == null, "a TerrainStrip leaked from another test");
            AssumeClip(SfxId.Crash);
            var am = MakeManager();
            SeedFx();

            var plane = PlayerPlane.Create(new Vector3(0f, 0f, 0f), false);
            _created.Add(plane.gameObject);
            var rb = (Rigidbody2D)Get(plane, "_rb");
            Assume.That(rb, Is.Not.Null);

            float g = PrivateConst(typeof(PlayerPlane), "CrashGravity");
            float rest = TerrainStrip.GroundBaseY + PrivateConst(typeof(PlayerPlane), "CrashRestHeight");
            float height = 3f;   // lands at about 0.65 s
            Vector2 pos = new Vector2(0f, rest + height);
            rb.position = pos;
            Assume.That(Mathf.Abs(rb.position.y - pos.y) < 1e-4f, "Rigidbody2D.position can't be set in this editor");

            Set(plane, "_hasCrashed", true);
            Set(plane, "_crashLanded", false);
            Set(plane, "_crashElapsed", 0f);
            Set(plane, "_crashSoundPlayed", false);
            Set(plane, "_crashVelocity", Vector2.zero);

            var uiIds = (SfxId[])Get(am, "_uiIds");
            var uiOrder = (float[])Get(am, "_uiOrder");
            int crashCues = 0, otherUiCues = 0;
            float startElapsed = -1f, landElapsed = -1f;
            int ticks = Mathf.RoundToInt(PlayerPlane.CrashFallSeconds / FixedDt) + 10;
            for (int i = 0; i < ticks; i++) {
                float maxBefore = Max(uiOrder);
                Invoke(plane, "UpdateCrashFall", FixedDt);
                float maxAfter = Max(uiOrder);
                if (maxAfter > maxBefore) {
                    int v = Array.IndexOf(uiOrder, maxAfter);
                    if (uiIds[v] == SfxId.Crash) {
                        crashCues++;
                        startElapsed = (float)Get(plane, "_crashElapsed");
                    } else {
                        otherUiCues++;
                    }
                }
                bool landed = (bool)Get(plane, "_crashLanded");
                Vector2 vel = (Vector2)Get(plane, "_crashVelocity");
                if (landed) {
                    if (landElapsed < 0f) landElapsed = (float)Get(plane, "_crashElapsed");
                    pos.y = rest;
                } else {
                    pos += vel * FixedDt;
                }
                rb.position = pos;   // EditMode doesn't run MovePosition
            }

            Assert.AreEqual(1, crashCues, "Crash plays exactly once, through PlayUi");
            Assert.AreEqual(0, otherUiCues);
            Assert.Greater(landElapsed, 0f, "the wreck landed");
            Assert.AreEqual(landElapsed, startElapsed + PlayerPlane.CrashCrunchOffset, FixedDt + 1e-3f,
                "the clip starts CrashCrunchOffset before the landing");
            Assert.AreEqual(PlayerPlane.CrashTimeToGround(height, 0f, g), landElapsed, FixedDt + 1e-3f);
        }

        private static float Max(float[] a) {
            float m = float.MinValue;
            for (int i = 0; i < a.Length; i++) if (a[i] > m) m = a[i];
            return m;
        }

        // ── B4: bomb direct hit ─────────────────────────────────────────

        [Test]
        public void BombDirectHit_Fatal_IsSilent_NonFatal_PlaysOneExplosion() {
            AssumeClip(SfxId.Explosion);
            MakeManager();
            SeedFx();
            var bomb = MakeLooseBomb();

            var fatal = new StubTarget(true);
            float before = PlayCounter;
            Invoke(bomb, "OnHit", fatal);
            Assert.AreEqual(1, fatal.Hits);
            Assert.AreEqual(before, PlayCounter, "a kill: the victim owns the single Explosion cue");

            var tough = new StubTarget(false);
            before = PlayCounter;
            Invoke(bomb, "OnHit", tough);
            Assert.AreEqual(before + 1f, PlayCounter, "a non-fatal direct hit plays one Explosion");
            Assert.AreSame(Clip(SfxId.Explosion), LastGameplayClip());
        }

        // ── B5: AAA death explosion ─────────────────────────────────────

        [Test]
        public void AAAGun_BombDirectKill_PlaysExactlyOneExplosion() {
            AssumeClip(SfxId.Explosion);
            MakeManager();
            SeedFx();
            var gun = AAAGun.Spawn(new Vector3(40f, 0f, 0f));
            _created.Add(gun.gameObject);
            var bomb = MakeLooseBomb();
            Set(bomb, "damage", 1000f);   // Start doesn't run in EditMode: make sure it kills

            float before = PlayCounter;
            Invoke(bomb, "OnHit", gun);
            Assert.IsTrue(gun.IsDead);
            Assert.AreEqual(before, PlayCounter, "the bomb is silent on a kill");

            LogAssert.ignoreFailingMessages = true;   // HandleDeath's Destroy() logs in EditMode
            Invoke(gun, "HandleDeath");
            Assert.AreEqual(before + 1f, PlayCounter, "the gun's death explosion is the one cue");
            Assert.AreSame(Clip(SfxId.Explosion), LastGameplayClip());
        }

        [Test]
        public void AAAGun_HandleDeath_WithoutFxManager_CreatesNone_InEditMode() {
            Assume.That(FxManager.GetOrNull(), Is.Null, "an FxManager leaked from another test");
            var gun = AAAGun.Spawn(new Vector3(40f, 0f, 0f));
            _created.Add(gun.gameObject);
            gun.ApplyDamage(1000f, DamageSource.Bomb);
            LogAssert.ignoreFailingMessages = true;
            Invoke(gun, "HandleDeath");
            Assert.IsNull(FxManager.GetOrNull(), "HandleDeath uses GetOrNull: no manager in EditMode");
        }

        // ── B6: zeppelin turret cues ────────────────────────────────────

        private ZeppelinAI SpawnZeppelin() {
            var z = ZeppelinAI.Spawn(new Vector3(60f, 0.9f, 0f), "zeppelin_t955");
            _created.Add(z.gameObject);
            Invoke(z, "Awake");
            foreach (var t in z.GetComponentsInChildren<ZeppelinTurret>()) Invoke(t, "Awake");
            return z;
        }

        [Test]
        public void ZeppelinTurret_NonFatalHits_TickThrottled_FatalHitExplodesOnly() {
            AssumeClip(SfxId.HitEnemy);
            AssumeClip(SfxId.Explosion);
            var g = GameBalance.Get();
            Assume.That(g != null && g.enemyHealth != null && g.player != null, "Balance.json not loadable");
            float bullet = g.player.bulletDamage;
            int toKill = Mathf.CeilToInt(g.enemyHealth.zeppelinTurret / bullet);
            Assume.That(toKill >= 2, "a single bullet kills a turret");

            MakeManager();
            SeedFx();
            var z = SpawnZeppelin();
            var t = z.GetComponentsInChildren<ZeppelinTurret>()[0];

            float before = PlayCounter;
            t.ApplyDamage(1f, DamageSource.Flak);
            Assert.AreEqual(before, PlayCounter, "ignored enemy fire is silent");

            t.ApplyDamage(bullet, DamageSource.Bullet);
            Assert.AreEqual(before + 1f, PlayCounter, "a non-fatal hit ticks once");
            Assert.AreSame(Clip(SfxId.HitEnemy), LastGameplayClip());

            if (toKill > 2) {
                t.ApplyDamage(bullet, DamageSource.Bullet);
                Assert.AreEqual(before + 1f, PlayCounter, "a second hit inside HitCueMinInterval is throttled");
            }

            Set(t, "_lastHitCueTime", -1f);   // as if the interval passed
            float beforeKill = PlayCounter;
            LogAssert.ignoreFailingMessages = true;   // Die's Destroy() logs in EditMode
            for (int i = 0; i < toKill && !t.IsDead; i++) t.ApplyDamage(bullet, DamageSource.Bullet);
            Assert.IsTrue(t.IsDead);
            float played = PlayCounter - beforeKill;
            Assert.GreaterOrEqual(played, 1f);
            Assert.AreSame(Clip(SfxId.Explosion), LastGameplayClip(), "the fatal hit ends on the explosion, not a tick");
        }

        [Test]
        public void ZeppelinTurret_Fire_PlaysFlakFire_PerSpawnedShell() {
            AssumeClip(SfxId.FlakFire);
            MakeManager();
            SeedFx();
            var z = SpawnZeppelin();
            var t = z.GetComponentsInChildren<ZeppelinTurret>()[0];
            var plane = PlayerPlane.Create(new Vector3(50f, -2f, 0f), false);
            _created.Add(plane.gameObject);
            var playerRef = typeof(ZeppelinTurret).GetProperty("PlayerRef", Private);   // internal
            Assert.IsNotNull(playerRef, "ZeppelinTurret.PlayerRef not found by reflection");
            playerRef.SetValue(t, plane);

            int shellsBefore = Object.FindObjectsByType<FlakShell>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            float before = PlayCounter;
            Invoke(t, "Fire");
            int shellsAfter = Object.FindObjectsByType<FlakShell>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;

            Assert.AreEqual(shellsBefore + 1, shellsAfter, "one shell spawned");
            Assert.AreEqual(before + 1f, PlayCounter, "one FlakFire for it");
            Assert.AreSame(Clip(SfxId.FlakFire), LastGameplayClip());
        }

        // ── B8: cues only for spawned projectiles ───────────────────────

        [Test]
        public void BombBay_NullSpawn_SpendsNoBomb_NoCue_NoBombsChanged() {
            AssumeClip(SfxId.BombRelease);
            MakeManager();
            var plane = PlayerPlane.Create(new Vector3(0f, 0f, 0f), false);   // builds the Bomb pool
            _created.Add(plane.gameObject);
            var bay = plane.GetComponent<BombBay>();
            Assert.IsNotNull(bay);
            int start = bay.BombsLeft;
            Assume.That(start > 0);

            int raised = 0;
            Action<int> onChanged = n => raised++;
            GameEvents.BombsChanged += onChanged;
            try {
                // A live pool: one bomb is spent, one release cue, one BombsChanged.
                float before = PlayCounter;
                Assert.IsTrue(bay.TryDropBomb(Vector3.zero, Vector2.zero));
                Assert.AreEqual(start - 1, bay.BombsLeft);
                Assert.AreEqual(before + 1f, PlayCounter);
                Assert.AreSame(Clip(SfxId.BombRelease), LastGameplayClip());
                Assert.AreEqual(1, raised);

                // Spawn returns null (disposed pool behaves like an exhausted one).
                DisposePool(typeof(Bomb));
                before = PlayCounter;
                Assert.IsFalse(bay.TryDropBomb(Vector3.zero, Vector2.zero), "no bomb dropped");
                Assert.AreEqual(start - 1, bay.BombsLeft, "no bomb spent");
                Assert.AreEqual(before, PlayCounter, "no BombRelease");
                Assert.AreEqual(1, raised, "no BombsChanged");
            } finally {
                GameEvents.BombsChanged -= onChanged;
            }
        }

        [Test]
        public void PlayerGun_OnlyForASpawnedBullet() {
            AssumeClip(SfxId.PlayerGun);
            MakeManager();
            var plane = PlayerPlane.Create(new Vector3(0f, 0f, 0f), false);   // builds the Bullet pool
            _created.Add(plane.gameObject);

            float before = PlayCounter;
            Invoke(plane, "OnFire");
            Assert.AreEqual(before + 1f, PlayCounter, "a bullet: one rattle");
            Assert.AreSame(Clip(SfxId.PlayerGun), LastGameplayClip());

            DisposePool(typeof(Bullet));
            before = PlayCounter;
            Invoke(plane, "OnFire");
            Assert.AreEqual(before, PlayCounter, "no bullet: no rattle");
        }
    }
}
