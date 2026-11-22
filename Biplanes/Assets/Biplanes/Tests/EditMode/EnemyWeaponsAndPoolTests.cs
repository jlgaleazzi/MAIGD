using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Fx;
using Biplanes.Missions;
using Biplanes.Player;
using Biplanes.Weapons;
using UnityEngine.TestTools;

namespace Biplanes.Tests {

    /// <summary>
    /// T927: EditMode tests for B004 (live AAA flak), B005 (EnemyBomb) and B006a/b (pool
    /// disposal, score reset). T928: B007 (player bomb multiplier), B008 (bomb ground burst),
    /// B009a/b/c (single AAA score, AAA lead, stale flak/enemy-bomb pool disposal).
    /// T929: B008 reopened (burst on the surface at the crossing x), B010 (AAA intercept aim),
    /// B011 (player projectiles fly through health balloons).
    ///
    /// EditMode doesn't run Awake on AddComponent, so tests invoke the private Awake by
    /// reflection where the code under test relies on it (as Play mode's AddComponent would).
    /// FxManager.Instance calls DontDestroyOnLoad, which only works in Play mode, so the one test
    /// that reaches TriggerExplosion seeds FxManager._instance by reflection first.
    /// </summary>
    public class EnemyWeaponsAndPoolTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

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
                if (go.name == "FlakShellPool" || go.name == "EnemyBombPool" || go.name.StartsWith("Pool<")) {
                    if (go.scene.IsValid() || go.hideFlags == HideFlags.HideAndDontSave) Object.DestroyImmediate(go);
                }
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static void InvokeAwake(Component c) {
            var awake = c.GetType().GetMethod("Awake", Private);
            Assert.IsNotNull(awake, $"{c.GetType().Name}.Awake not found by reflection");
            awake.Invoke(c, null);
        }

        private static void InvokeOnHit(Projectile p, IDamageable target) {
            var onHit = p.GetType().GetMethod("OnHit", Private);
            Assert.IsNotNull(onHit, $"{p.GetType().Name}.OnHit not found by reflection");
            onHit.Invoke(p, new object[] { target });
        }

        /// <summary>A Health with a TeamTag, Awake run so it reads its team.</summary>
        private Health MakeHealth(string name, Team team, float maxHitPoints = 0f) {
            var go = new GameObject(name);
            _created.Add(go);
            go.AddComponent<TeamTag>().team = team;
            var h = go.AddComponent<Health>();
            InvokeAwake(h);
            if (maxHitPoints > 0f) h.SetMaxHitPoints(maxHitPoints);
            Assert.AreEqual(team, h.Team);
            return h;
        }

        private static int AliveRootsNamed(string name) {
            int n = 0;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go != null && go.name == name && go.transform.parent == null && go.scene.IsValid()) n++;
            }
            return n;
        }

        private static int AliveHiddenPoolRoots(string name) {
            int n = 0;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go != null && go.name == name && go.hideFlags == HideFlags.HideAndDontSave) n++;
            }
            return n;
        }

        /// <summary>Runs Awake on every pooled item of type T, as Play mode's AddComponent would have.</summary>
        private static void AwakeAll<T>() where T : Projectile {
            foreach (var p in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                InvokeAwake(p);
            }
        }

        private static T StaticField<T>(System.Type type, string name) {
            var f = type.GetField(name, PrivateStatic);
            Assert.IsNotNull(f, $"{type.Name}.{name} not found by reflection");
            return (T)f.GetValue(null);
        }

        // ── ObjectPool (B006a) ──────────────────────────────────────────

        [Test]
        public void ObjectPool_ItemsUnderRoot_DisposeDestroysPooledAndInFlight() {
            var pool = new ObjectPool<SpriteRenderer>(() => new GameObject("T927_Item").AddComponent<SpriteRenderer>(), 3);
            var inFlight = pool.Pop();
            Assert.IsNotNull(inFlight);
            Transform root = inFlight.transform.parent;
            Assert.IsNotNull(root, "items must live under the pool root from creation");

            var all = new List<SpriteRenderer>();
            foreach (Transform child in root) all.Add(child.GetComponent<SpriteRenderer>());
            Assert.AreEqual(3, all.Count, "every item is parented under the root");

            pool.Dispose();
            Assert.IsTrue(pool.IsDisposed);
            foreach (var item in all) Assert.IsTrue(item == null, "Dispose must destroy every item, including in flight");
            Assert.IsTrue(root == null, "Dispose must destroy the pool root");

            Assert.IsNull(pool.Pop(), "a disposed pool hands out nothing");
            Assert.DoesNotThrow(() => pool.Push(inFlight), "Push of a destroyed item after Dispose is a no-op");
            Assert.DoesNotThrow(() => pool.Dispose(), "Dispose is idempotent");
        }

        [Test]
        public void ObjectPool_PopSkipsItemsDestroyedElsewhere() {
            var pool = new ObjectPool<SpriteRenderer>(() => new GameObject("T927_Item").AddComponent<SpriteRenderer>(), 2);
            var a = pool.Pop();
            pool.Push(a);           // queue: b, a
            var b = pool.Pop();     // b
            pool.Push(b);           // queue: a, b
            Object.DestroyImmediate(a.gameObject);
            var next = pool.Pop();
            Assert.IsTrue(next != null, "Pop must skip a destroyed item");
            Assert.AreSame(b, next);
            pool.Dispose();
        }

        [Test]
        public void BulletAndBombInitPool_Twice_DisposeThePreviousPool() {
            Bullet.InitPool(() => new GameObject("T927_Bullet").AddComponent<Bullet>(), 2);
            Bomb.InitPool(() => new GameObject("T927_Bomb").AddComponent<Bomb>(), 2);
            var firstBulletPool = StaticField<ObjectPool<Bullet>>(typeof(Bullet), "_pool");
            var firstBombPool = StaticField<ObjectPool<Bomb>>(typeof(Bomb), "_pool");
            var firstBullets = Object.FindObjectsByType<Bullet>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var firstBombs = Object.FindObjectsByType<Bomb>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.AreEqual(2, firstBullets.Length);
            Assert.AreEqual(2, firstBombs.Length);

            Bullet.InitPool(() => new GameObject("T927_Bullet").AddComponent<Bullet>(), 2);
            Bomb.InitPool(() => new GameObject("T927_Bomb").AddComponent<Bomb>(), 2);

            Assert.IsTrue(firstBulletPool.IsDisposed, "Bullet.InitPool must dispose the old pool");
            Assert.IsTrue(firstBombPool.IsDisposed, "Bomb.InitPool must dispose the old pool");
            foreach (var b in firstBullets) Assert.IsTrue(b == null, "old bullets must be destroyed");
            foreach (var b in firstBombs) Assert.IsTrue(b == null, "old bombs must be destroyed");
            Assert.AreEqual(1, AliveHiddenPoolRoots("Pool<Bullet>"), "exactly one bullet pool root");
            Assert.AreEqual(1, AliveHiddenPoolRoots("Pool<Bomb>"), "exactly one bomb pool root");
        }

        // ── FlakShell (B004) ────────────────────────────────────────────

        [Test]
        public void FlakShell_PoolBuiltOnce_UnderSceneRoot_WithoutDontSaveFlags() {
            var s1 = FlakShell.Spawn(Vector3.zero, Vector2.up);
            var s2 = FlakShell.Spawn(Vector3.zero, Vector2.up);
            Assert.IsNotNull(s1);
            Assert.IsNotNull(s2);
            Assert.AreEqual(1, AliveRootsNamed("FlakShellPool"), "one FlakShellPool root, reused");

            Transform top = s1.transform.root;
            Assert.AreEqual("FlakShellPool", top.name, "shells live under the scene root");
            Assert.AreEqual(HideFlags.None, top.gameObject.hideFlags);
            Assert.AreEqual(HideFlags.None, s1.transform.parent.gameObject.hideFlags,
                "the ObjectPool inner root must lose its DontSave flags");
            Assert.AreEqual(0, AliveHiddenPoolRoots("Pool<FlakShell>"), "no HideAndDontSave flak root left behind");
        }

        [Test]
        public void FlakShell_RebuildsPool_AfterRootDestroyed() {
            Assert.IsNotNull(FlakShell.Spawn(Vector3.zero, Vector2.up));
            var firstPool = StaticField<ObjectPool<FlakShell>>(typeof(FlakShell), "_pool");
            var root = StaticField<GameObject>(typeof(FlakShell), "_root");
            Object.DestroyImmediate(root);   // what a scene reload (Retry) does

            var shell = FlakShell.Spawn(Vector3.zero, Vector2.up);
            Assert.IsNotNull(shell, "Spawn after a scene reload must rebuild the pool");
            Assert.AreNotSame(firstPool, StaticField<ObjectPool<FlakShell>>(typeof(FlakShell), "_pool"));
            Assert.AreEqual(1, AliveRootsNamed("FlakShellPool"));
            // B009c: the stale pool is disposed (unsubscribed from Application.quitting).
            Assert.IsTrue(firstPool.IsDisposed, "EnsurePool must Dispose the stale pool before rebuilding");
            Assert.IsTrue(StaticField<GameObject>(typeof(FlakShell), "_root") != null,
                "disposing the stale pool must not destroy the new scene root");
        }

        [Test]
        public void EnemyBomb_RebuildsPool_AfterRootDestroyed_DisposesStalePool() {
            Assert.IsNotNull(EnemyBomb.Spawn(new Vector3(0f, 3f, 0f), Vector2.zero));
            var firstPool = StaticField<ObjectPool<EnemyBomb>>(typeof(EnemyBomb), "_pool");
            Object.DestroyImmediate(StaticField<GameObject>(typeof(EnemyBomb), "_root"));   // scene reload

            EnemyBomb fresh = null;
            Assert.DoesNotThrow(() => fresh = EnemyBomb.Spawn(new Vector3(0f, 3f, 0f), Vector2.zero),
                "disposing a pool whose root and items are already destroyed must not throw");
            Assert.IsNotNull(fresh);
            Assert.IsTrue(firstPool.IsDisposed, "B009c: the stale pool must be disposed");
            Assert.AreNotSame(firstPool, StaticField<ObjectPool<EnemyBomb>>(typeof(EnemyBomb), "_pool"));
            Assert.AreEqual(1, AliveRootsNamed("EnemyBombPool"));
            Assert.IsTrue(fresh.gameObject.activeSelf, "the new pool's item is live");
        }

        [Test]
        public void FlakShell_FliesUpAlongAim_AtShellSpeed() {
            FlakShell.Spawn(Vector3.zero, Vector2.up).Recycle();
            AwakeAll<FlakShell>();   // speed, team and damage are set in Awake

            var aimed = FlakShell.Spawn(Vector3.zero, new Vector2(1f, 2f));
            Vector2 v = aimed.GetComponent<Rigidbody2D>().linearVelocity;
            Assert.AreEqual(FlakShell.ShellSpeed, v.magnitude, 1e-3f);
            Assert.AreEqual(new Vector2(1f, 2f).normalized.x, v.normalized.x, 1e-3f, "follows the aim");
            Assert.Greater(v.y, 0f);

            var low = FlakShell.Spawn(Vector3.zero, new Vector2(1f, -1f));
            Vector2 lv = low.GetComponent<Rigidbody2D>().linearVelocity;
            Assert.Greater(lv.y, 0f, "a shell must never fly down");
            Assert.Greater(lv.x, 0f, "keeps the horizontal aim side");

            var none = FlakShell.Spawn(Vector3.zero, Vector2.zero);
            Vector2 nv = none.GetComponent<Rigidbody2D>().linearVelocity;
            Assert.AreEqual(0f, nv.x, 1e-4f);
            Assert.AreEqual(FlakShell.ShellSpeed, nv.y, 1e-3f, "a zero aim flies straight up");
        }

        [Test]
        public void FlakShell_HitOnPlayer_AddsExactlyDamageTakenFlak_IgnoresEnemies() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.damageTaken != null, "Balance.json not loadable");

            var shell = FlakShell.Spawn(Vector3.zero, Vector2.up);
            InvokeAwake(shell);
            Assert.AreEqual(Team.Enemy, shell.Team);
            Assert.AreEqual(1f, shell.Damage, 1e-6f, "one flak hit unit");

            var enemy = MakeHealth("T927_Enemy", Team.Enemy, 25f);
            InvokeOnHit(shell, enemy);
            Assert.AreEqual(0f, enemy.CurrentDamagePercent, "flak must not hurt enemies");
            Assert.IsTrue(shell.gameObject.activeSelf, "a skipped hit doesn't consume the shell");

            var player = MakeHealth("T927_Player", Team.Player);
            InvokeOnHit(shell, player);
            Assert.AreEqual(g.damageTaken.flak / PlayerData.ResistanceOrDefault(g.player), player.CurrentDamagePercent, 1e-4f,
                "one shell = damageTaken.flak / player.resistance percent (B052)");
            Assert.IsFalse(shell.gameObject.activeSelf, "the shell is recycled after the hit");
        }

        // ── AAAGun (B004) ───────────────────────────────────────────────

        [Test]
        public void AAAGun_TriggerCollider_BulletsChip_FlakIgnored_BombKills() {
            var gun = AAAGun.Spawn(new Vector3(10f, -3.2f, 0f));
            _created.Add(gun.gameObject);

            var col = gun.GetComponent<BoxCollider2D>();
            Assert.IsNotNull(col, "the gun needs a collider so bombs can hit it");
            Assert.IsTrue(col.isTrigger, "ARCHITECTURE §3 Physics: hittables use trigger colliders");
            Assert.AreEqual(RigidbodyType2D.Kinematic, gun.GetComponent<Rigidbody2D>().bodyType);
            Assert.AreEqual(-3.2f, (float)typeof(AAAGun).GetField("_groundY", Private).GetValue(gun), 1e-5f,
                "ground-bound at the spawn (terrain) height");

            // B037: bullets now chip the gun (damage / enemyHealth.aaa); flak is still ignored.
            var g = GameBalance.Get();
            float aaaHp = g != null && g.enemyHealth != null && g.enemyHealth.aaa > 0f ? g.enemyHealth.aaa : 125f;
            gun.ApplyDamage(25f, DamageSource.Bullet);
            Assert.AreEqual(25f / aaaHp * 100f, gun.CurrentDamagePercent, 1e-3f,
                "one bullet = 25 / enemyHealth.aaa (20% at 125)");
            gun.ApplyDamage(1f, DamageSource.Flak);
            Assert.AreEqual(25f / aaaHp * 100f, gun.CurrentDamagePercent, 1e-3f, "flak must not damage AAA");
            Assert.IsFalse(gun.IsDead);

            float bombDamage = g != null && g.player != null ? g.player.bombDamage : 100f;
            gun.ApplyDamage(bombDamage, DamageSource.Bomb);
            Assert.IsTrue(gun.IsDead, "one player bomb (bombDamage vs enemyHealth.aaa) destroys the AAA");
        }

        // ── EnemyBomb (B005) ────────────────────────────────────────────

        [Test]
        public void EnemyBomb_OwnPool_TeamEnemy_NeverTouchesPlayerBombPool() {
            Bomb.InitPool(() => new GameObject("T927_Bomb").AddComponent<Bomb>(), 3);
            var playerPool = StaticField<ObjectPool<Bomb>>(typeof(Bomb), "_pool");
            int before = playerPool.Available;

            var eb1 = EnemyBomb.Spawn(new Vector3(0f, 3f, 0f), new Vector2(-1.5f, 0f));
            var eb2 = EnemyBomb.Spawn(new Vector3(1f, 3f, 0f), new Vector2(-1.5f, 0f));
            Assert.IsNotNull(eb1);
            Assert.IsNotNull(eb2);
            Assert.AreEqual(before, playerPool.Available, "enemy bombs must not draw from the player's Bomb pool");
            Assert.AreEqual("EnemyBombPool", eb1.transform.root.name);
            Assert.AreEqual(1, AliveRootsNamed("EnemyBombPool"));
            Assert.AreEqual(new Vector2(-1.5f, 0f), eb1.GetComponent<Rigidbody2D>().linearVelocity,
                "starts with exactly the bomber's velocity");

            InvokeAwake(eb1);
            Assert.AreEqual(Team.Enemy, eb1.Team);
            Assert.AreEqual(1f, eb1.Damage, 1e-6f, "one enemy bomb hit unit");
        }

        [Test]
        public void EnemyBomb_IgnoresEnemiesAndNeutrals() {
            var bomb = EnemyBomb.Spawn(new Vector3(0f, 3f, 0f), Vector2.zero);
            InvokeAwake(bomb);

            var enemy = MakeHealth("T927_Enemy", Team.Enemy, 200f);
            var neutral = MakeHealth("T927_Neutral", Team.Neutral, 50f);
            InvokeOnHit(bomb, enemy);
            InvokeOnHit(bomb, neutral);
            Assert.AreEqual(0f, enemy.CurrentDamagePercent, "enemy bombs must not hurt enemies");
            Assert.AreEqual(0f, neutral.CurrentDamagePercent, "enemy bombs ignore neutral objects");
            Assert.IsTrue(bomb.gameObject.activeSelf, "a skipped hit doesn't consume the bomb");
        }

        // ── ScoreKeeper (B006b) ─────────────────────────────────────────

        [Test]
        public void ScoreKeeper_ResetScore_ZeroesAndRaisesScoreChanged() {
            var go = new GameObject("T927_ScoreKeeper");
            _created.Add(go);
            var sk = go.AddComponent<ScoreKeeper>();
            sk.AddPoints(350);
            Assert.AreEqual(350, sk.CurrentScore);

            int raised = -1;
            System.Action<int> onScore = s => raised = s;
            GameEvents.ScoreChanged += onScore;
            try {
                sk.ResetScore();
            } finally {
                GameEvents.ScoreChanged -= onScore;
            }
            Assert.AreEqual(0, sk.CurrentScore);
            Assert.AreEqual(0, raised);
        }

        // ── T928: B007, B008, B009 ──────────────────────────────────────

        private static float PlayerBombDamage() {
            var g = GameBalance.Get();
            return g != null && g.player != null ? g.player.bombDamage : 100f;
        }

        /// <summary>A bare Bomb with Balance bombDamage (Start doesn't run in EditMode).</summary>
        private Bomb MakeBomb(Vector3 position) {
            var go = new GameObject("T928_Bomb");
            _created.Add(go);
            go.transform.position = position;
            var bomb = go.AddComponent<Bomb>();
            InvokeAwake(bomb);
            typeof(Projectile).GetField("damage", Private).SetValue(bomb, PlayerBombDamage());
            return bomb;
        }

        private static void InvokeBurst(Bomb bomb, Vector2 point) {
            var burst = typeof(Bomb).GetMethod("BurstDamage", Private);
            Assert.IsNotNull(burst, "Bomb.BurstDamage not found by reflection");
            burst.Invoke(bomb, new object[] { point });
        }

        /// <summary>A Health (player mode, or enemy mode when maxHitPoints &gt; 0) with a trigger box.</summary>
        private Health MakeHittableHealth(string name, Team team, Vector3 position, float maxHitPoints = 0f) {
            var h = MakeHealth(name, team, maxHitPoints);
            h.transform.position = position;
            var col = h.gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.4f, 0.4f);
            return h;
        }

        /// <summary>Skips (Inconclusive) when 2D physics queries don't see EditMode colliders.</summary>
        private static void AssumePhysicsQueriesWork(Collider2D probe) {
            Physics2D.SyncTransforms();
            var hit = Physics2D.OverlapPoint(probe.bounds.center);
            Assume.That(hit != null, "Physics2D queries return nothing in this EditMode run");
        }

        [Test]
        public void Bomb_GroundBurst_KillsAdjacentAAA_SparesPlayerNeutralAndHealthBalloon() {
            // Gun on the ground at y = 0 (trigger 1 x 0.6). The bomb was recycled 0.6 under the
            // ground, past the gun's trigger: the B008 tunnelling case.
            var gun = AAAGun.Spawn(new Vector3(20f, 0f, 0f));
            _created.Add(gun.gameObject);
            var player = MakeHittableHealth("T928_Player", Team.Player, new Vector3(20.4f, -0.6f, 0f));
            var neutral = MakeHittableHealth("T928_Neutral", Team.Neutral, new Vector3(19.6f, -0.6f, 0f), 50f);
            var pickup = HealthBalloon.Spawn(new Vector3(20f, -1.0f, 0f));
            _created.Add(pickup.gameObject);
            var farEnemy = MakeHittableHealth("T928_FarEnemy", Team.Enemy, new Vector3(23f, -0.6f, 0f), 25f);
            AssumePhysicsQueriesWork(gun.GetComponent<Collider2D>());

            var bomb = MakeBomb(new Vector3(20f, -0.6f, 0f));
            InvokeBurst(bomb, new Vector2(20f, -0.6f));

            Assert.IsTrue(gun.IsDead, "a bomb bursting just under an AAA must destroy it (B008)");
            Assert.AreEqual(0f, player.CurrentDamagePercent, "the player's own bomb burst never hurts the player");
            Assert.AreEqual(0f, neutral.CurrentDamagePercent, "Neutral objects are skipped by the burst");
            Assert.IsFalse(pickup.IsDead, "a health balloon is untouched by the burst");
            Assert.IsTrue(pickup.gameObject.activeSelf);
            Assert.AreEqual(0f, farEnemy.CurrentDamagePercent, "targets outside burstRadius are untouched");
        }

        [Test]
        public void Bomb_GroundBurst_SkipsDirectHit_AndDamagesMultiColliderTargetOnce() {
            // Enemy mode with 4x bombDamage hit points: one bomb = 25%.
            float hp = PlayerBombDamage() * 4f;
            var multi = MakeHittableHealth("T928_Multi", Team.Enemy, new Vector3(30f, -0.2f, 0f), hp);
            var child = new GameObject("T928_MultiChild");
            child.transform.SetParent(multi.transform, false);
            child.transform.localPosition = new Vector3(0.2f, -0.2f, 0f);
            var childCol = child.AddComponent<CircleCollider2D>();
            childCol.isTrigger = true;
            childCol.radius = 0.2f;
            var direct = MakeHittableHealth("T928_Direct", Team.Enemy, new Vector3(29.8f, -0.4f, 0f), hp);
            AssumePhysicsQueriesWork(multi.GetComponent<Collider2D>());

            var bomb = MakeBomb(new Vector3(30f, -0.3f, 0f));
            typeof(Bomb).GetField("_directHit", Private).SetValue(bomb, direct);
            InvokeBurst(bomb, new Vector2(30f, -0.3f));

            Assert.AreEqual(25f, multi.CurrentDamagePercent, 1e-3f, "two colliders, one target: damaged once");
            Assert.AreEqual(0f, direct.CurrentDamagePercent, "the target already hit through the trigger is skipped");

            // A second burst from the same bomb (no direct hit) still respects IsDead and dedupe.
            typeof(Bomb).GetField("_directHit", Private).SetValue(bomb, null);
            InvokeBurst(bomb, new Vector2(30f, -0.3f));
            Assert.AreEqual(50f, multi.CurrentDamagePercent, 1e-3f, "one more burst = exactly one more hit");
            Assert.AreEqual(25f, direct.CurrentDamagePercent, 1e-3f);
        }

        [Test]
        public void Bomb_Spawn_ClearsDirectHitFromPreviousFlight() {
            Bomb.InitPool(() => {
                var go = new GameObject("T928_PooledBomb");
                go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                go.AddComponent<CircleCollider2D>().isTrigger = true;
                return go.AddComponent<Bomb>();
            }, 1);
            var enemy = MakeHealth("T928_Enemy", Team.Enemy, 100f);
            var field = typeof(Bomb).GetField("_directHit", Private);

            var first = Bomb.Spawn(Vector3.zero, Vector2.zero);
            Assert.IsNotNull(first);
            field.SetValue(first, enemy);
            first.Recycle();

            var second = Bomb.Spawn(Vector3.zero, Vector2.zero);
            Assert.AreSame(first, second, "pool of one reuses the same bomb");
            Assert.IsNull(field.GetValue(second), "Spawn must clear the previous flight's direct hit");
        }

        [Test]
        public void AAAGun_BombKill_ScoresAaaExactlyOnce() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.score != null, "Balance.json not loadable");

            typeof(ScoreKeeper).GetField("_instance", PrivateStatic).SetValue(null, null);
            var skGo = new GameObject("T928_ScoreKeeper");
            _created.Add(skGo);
            var sk = skGo.AddComponent<ScoreKeeper>();
            InvokeAwake(sk);   // subscribes to GameEvents.EntityDestroyed
            try {
                var gun = AAAGun.Spawn(new Vector3(40f, 0f, 0f));
                _created.Add(gun.gameObject);

                gun.ApplyDamage(PlayerBombDamage(), DamageSource.Bomb);
                Assert.IsTrue(gun.IsDead);
                Assert.AreEqual(g.score.aaa, sk.CurrentScore, "the kill scores score.aaa via EntityDestroyed");

                // A second bomb on the dead gun, then the death transition (HandleDeath, run by
                // LateUpdate in play). Its Destroy logs an edit-mode error, which is not under test;
                // the flag is scoped to this test's LogScope.
                gun.ApplyDamage(PlayerBombDamage(), DamageSource.Bomb);
                LogAssert.ignoreFailingMessages = true;
                typeof(AAAGun).GetMethod("HandleDeath", Private).Invoke(gun, null);
                Assert.AreEqual(g.score.aaa, sk.CurrentScore, "B009a: exactly one score.aaa per AAA kill");
            } finally {
                typeof(ScoreKeeper).GetMethod("OnDestroy", Private).Invoke(sk, null);   // unsubscribe
            }
        }

        /// <summary>B010: A is the true intercept iff A = P + v t with t = |A - gun| / ShellSpeed.</summary>
        private static void AssertIntercept(Vector3 gun, Vector3 player, Vector2 v, Vector3 a, string label) {
            Assert.IsTrue(float.IsFinite(a.x) && float.IsFinite(a.y), $"{label}: finite aim point");
            float t = Vector2.Distance(a, gun) / FlakShell.ShellSpeed;
            Assert.Greater(t, 0f, $"{label}: positive flight time");
            Assert.AreEqual(player.x + v.x * t, a.x, 1e-3f, $"{label}: A.x = P.x + v.x t, t = |A - gun| / ShellSpeed");
            Assert.AreEqual(player.y + v.y * t, a.y, 1e-3f, $"{label}: A.y = P.y + v.y t, t = |A - gun| / ShellSpeed");
        }

        private (AAAGun gun, PlayerPlane plane, FieldInfo vel, MethodInfo predict) MakeGunAndPlane() {
            var gun = AAAGun.Spawn(new Vector3(50f, 0f, 0f));
            _created.Add(gun.gameObject);
            var planeGo = new GameObject("T929_Player");
            _created.Add(planeGo);
            var plane = planeGo.AddComponent<PlayerPlane>();
            gun.Init(plane);
            var velField = typeof(PlayerPlane).GetField("_velocity", Private);
            Assert.IsNotNull(velField, "PlayerPlane._velocity not found by reflection");
            var predict = typeof(AAAGun).GetMethod("PredictPosition", Private);
            Assert.IsNotNull(predict, "AAAGun.PredictPosition not found by reflection");
            return (gun, plane, velField, predict);
        }

        private static Vector3 Predict(AAAGun gun, PlayerPlane plane, FieldInfo vel, MethodInfo predict,
                                       Vector3 playerPos, Vector2 v) {
            plane.transform.position = playerPos;
            vel.SetValue(plane, v);
            return (Vector3)predict.Invoke(gun, null);
        }

        [Test]
        public void AAAGun_PredictPosition_LeadsWithPlayerVelocity_BothFacings() {
            var (gun, plane, vel, predict) = MakeGunAndPlane();
            Vector3 g = gun.transform.position;

            vel.SetValue(plane, new Vector2(5f, 0f));
            Assert.AreEqual(new Vector2(5f, 0f), plane.Velocity, "Velocity exposes the per-tick world velocity");

            // Flying right toward the gun: the exact intercept (t ~ 1.2035 s, x ~ 54.017).
            var pr = new Vector3(48f, 6f, 0f);
            var right = Predict(gun, plane, vel, predict, pr, new Vector2(5f, 0f));
            AssertIntercept(g, pr, new Vector2(5f, 0f), right, "facing right");
            Assert.AreEqual(54.0174f, right.x, 1e-2f, "leads to the right, past the gun");

            // The mirror image, flying left: the lead flips exactly.
            var pl = new Vector3(52f, 6f, 0f);
            var left = Predict(gun, plane, vel, predict, pl, new Vector2(-5f, 0f));
            AssertIntercept(g, pl, new Vector2(-5f, 0f), left, "facing left");
            Assert.AreEqual(g.x - (right.x - g.x), left.x, 1e-3f, "mirror of the right-facing lead");
            Assert.AreEqual(right.y, left.y, 1e-3f);

            // Facing left and climbing, moving away from the gun (still slower than the shell).
            var pc = new Vector3(48f, 6f, 0f);
            var climb = Predict(gun, plane, vel, predict, pc, new Vector2(-5f, 2f));
            AssertIntercept(g, pc, new Vector2(-5f, 2f), climb, "facing left, climbing");
            Assert.Less(climb.x, pc.x, "leads to the left when the plane flies left");
            Assert.Greater(climb.y, pc.y, "leads upward when the plane climbs");
        }

        [Test]
        public void AAAGun_PredictPosition_StationaryPlane_SpeedEqualsShell_NoSolutionFallback() {
            var (gun, plane, vel, predict) = MakeGunAndPlane();
            Vector3 g = gun.transform.position;
            float s = FlakShell.ShellSpeed;

            // Stationary plane: aim straight at it.
            var p0 = new Vector3(47f, 4f, 0f);
            var still = Predict(gun, plane, vel, predict, p0, Vector2.zero);
            Assert.AreEqual(p0.x, still.x, 1e-4f, "a stationary plane is aimed at directly");
            Assert.AreEqual(p0.y, still.y, 1e-4f);

            // |v| == ShellSpeed, closing on the gun: linear case, t = -|D|^2 / (2 D.v), no division by zero.
            var p1 = new Vector3(48f, 6f, 0f);
            var closing = Predict(gun, plane, vel, predict, p1, new Vector2(s, 0f));
            AssertIntercept(g, p1, new Vector2(s, 0f), closing, "|v| == ShellSpeed, closing");
            Assert.AreEqual(58f, closing.x, 1e-3f, "t = 40 / 24 s for D = (-2, 6), v = (6, 0)");

            // |v| == ShellSpeed, moving away: no intercept; fallback t = |D| / S, still finite.
            var p2 = new Vector3(52f, 6f, 0f);
            var away = Predict(gun, plane, vel, predict, p2, new Vector2(s, 0f));
            float tFallback = Vector2.Distance(p2, g) / s;
            Assert.IsTrue(float.IsFinite(away.x) && float.IsFinite(away.y), "no NaN or infinity at |v| == ShellSpeed");
            Assert.AreEqual(p2.x + s * tFallback, away.x, 1e-3f, "fallback t = |D| / ShellSpeed");
            Assert.AreEqual(p2.y, away.y, 1e-3f);

            // Faster than the shell and moving away: both roots negative or none; same fallback.
            var p3 = new Vector3(52f, 6f, 0f);
            var outrun = Predict(gun, plane, vel, predict, p3, new Vector2(8f, 0f));
            Assert.IsTrue(float.IsFinite(outrun.x) && float.IsFinite(outrun.y));
            Assert.AreEqual(p3.x + 8f * Vector2.Distance(p3, g) / s, outrun.x, 1e-3f, "outrun: fallback t = |D| / ShellSpeed");

            // Faster than the shell but closing: the earliest positive root is the intercept.
            var p4 = new Vector3(40f, 6f, 0f);
            var fastClosing = Predict(gun, plane, vel, predict, p4, new Vector2(8f, 0f));
            AssertIntercept(g, p4, new Vector2(8f, 0f), fastClosing, "faster than the shell, closing");
        }

        [Test]
        public void Health_PlayerBombHit_UsesBomberGunner_OtherSourcesAndEnemiesUnchanged() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.damageTaken != null, "Balance.json not loadable");

            // B052: non-fatal player hits are divided by player.resistance; Collision is not.
            float res = PlayerData.ResistanceOrDefault(g.player);
            var bombed = MakeHealth("T928_PlayerBomb", Team.Player);
            bombed.ApplyDamage(1f, DamageSource.Bomb);
            Assert.AreEqual(g.damageTaken.bomberGunner / res, bombed.CurrentDamagePercent, 1e-4f, "B007: bomb = bomberGunner (/ resistance)");

            var shot = MakeHealth("T928_PlayerBullet", Team.Player);
            shot.ApplyDamage(1f, DamageSource.Bullet);
            Assert.AreEqual(g.damageTaken.fighterBullet / res, shot.CurrentDamagePercent, 1e-4f);

            var crashed = MakeHealth("T928_PlayerCrash", Team.Player);
            crashed.ApplyDamage(1f, DamageSource.Collision);
            Assert.AreEqual(Mathf.Min(100f, g.damageTaken.collision), crashed.CurrentDamagePercent, 1e-4f);

            // Enemies are in hit-point mode: the player-side multiplier never applies to them.
            var bomber = MakeHealth("T928_Bomber", Team.Enemy, 200f);
            bomber.ApplyDamage(100f, DamageSource.Bomb);
            Assert.AreEqual(50f, bomber.CurrentDamagePercent, 1e-4f);
        }

        // ── T929: B011 (health balloons don't stop player fire), B008 (impact point) ──

        private static T MakePooledProjectile<T>(string name) where T : Projectile {
            var go = new GameObject(name);
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<CircleCollider2D>().isTrigger = true;
            return go.AddComponent<T>();
        }

        [Test]
        public void PlayerBulletAndBomb_OnHitHealthBalloon_FlyOn_BalloonUntouched() {
            Bullet.InitPool(() => MakePooledProjectile<Bullet>("T929_Bullet"), 2);
            Bomb.InitPool(() => MakePooledProjectile<Bomb>("T929_Bomb"), 2);
            var bullet = Bullet.Spawn(Vector3.zero, Quaternion.identity, 1);
            var bomb = Bomb.Spawn(new Vector3(0f, 3f, 0f), new Vector2(5f, 0f));
            Assert.IsNotNull(bullet);
            Assert.IsNotNull(bomb);
            InvokeAwake(bullet);
            InvokeAwake(bomb);
            Assert.AreEqual(Team.Player, bullet.Team);
            Assert.AreEqual(Team.Player, bomb.Team);

            var balloon = HealthBalloon.Spawn(new Vector3(0f, 2f, 0f));
            _created.Add(balloon.gameObject);
            Assert.AreEqual(Team.Neutral, balloon.Team);

            InvokeOnHit(bullet, balloon);
            InvokeOnHit(bomb, balloon);
            Assert.IsTrue(bullet.gameObject.activeSelf, "B011: a bullet flies on through a health balloon");
            Assert.IsTrue(bomb.gameObject.activeSelf, "B011: a bomb flies on through a health balloon");
            Assert.IsNull(typeof(Bomb).GetField("_directHit", Private).GetValue(bomb),
                "the balloon is not recorded as the bomb's direct hit");
            Assert.IsFalse(balloon.IsDead, "projectiles never collect the balloon");
            Assert.IsTrue(balloon.gameObject.activeSelf);

            // Not vacuous: the pool is wired, so a real hit on an enemy does recycle the bullet.
            var enemy = MakeHealth("T929_Enemy", Team.Enemy, 100f);
            InvokeOnHit(bullet, enemy);
            Assert.Greater(enemy.CurrentDamagePercent, 0f, "bullets still damage enemies");
            Assert.IsFalse(bullet.gameObject.activeSelf, "a bullet that hits an enemy is recycled");
            bomb.Recycle();
            Assert.IsFalse(bomb.gameObject.activeSelf, "the bomb's pool is wired (Recycle deactivates)");
        }

        [Test]
        public void Bomb_Update_BelowGround_BurstsOnSurfaceAtCrossingX_KillsAaaThere() {
            // Slope 0.1 between x = 100 and 120: HeightAt(x) = -4.5 + 0.1 (x - 100).
            var strip = TerrainStrip.Create(new[] {
                new TerrainPoint { atX = 0, height = 0f },
                new TerrainPoint { atX = 100, height = 0f },
                new TerrainPoint { atX = 120, height = 2f },
            }, 200f);
            _created.Add(strip.gameObject);
            Assert.AreEqual(-3.5f, strip.HeightAt(110f), 1e-4f, "test terrain");

            var fxField = typeof(FxManager).GetField("_instance", PrivateStatic);
            Assert.IsNotNull(fxField, "FxManager._instance not found by reflection");
            var fxGo = new GameObject("T929_Fx");
            _created.Add(fxGo);
            fxField.SetValue(null, fxGo.AddComponent<FxManager>());
            try {
                // prev 0.6 above the surface at x = 109.5, cur 0.9 below it at x = 110:
                // the crossing is 0.4 of the way, at x = 109.7, y = HeightAt(109.7) = -3.53.
                // The gun at 108.9 is reached from the crossing point (0.3 past its edge) but not from
                // the below-ground position (110, -4.4) nor from the surface under it (110, -3.5).
                var gun = AAAGun.Spawn(new Vector3(108.9f, strip.HeightAt(108.9f), 0f));
                _created.Add(gun.gameObject);
                AssumePhysicsQueriesWork(gun.GetComponent<Collider2D>());

                var bomb = MakeBomb(new Vector3(110f, strip.HeightAt(110f) - 0.9f, 0f));
                var rb = bomb.gameObject.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Kinematic;
                typeof(Projectile).GetField("_rb", Private).SetValue(bomb, rb);
                var prevField = typeof(Bomb).GetField("_prevPos", Private);
                Assert.IsNotNull(prevField, "Bomb._prevPos not found by reflection");
                prevField.SetValue(bomb, new Vector2(109.5f, strip.HeightAt(109.5f) + 0.6f));
                Physics2D.SyncTransforms();

                typeof(Bomb).GetMethod("Update", Private).Invoke(bomb, null);

                Assert.IsTrue(gun.IsDead, "B008: the burst on the surface at the crossing x kills the AAA there");
                GameObject explosion = null;
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                    if (go != null && go.name == "Explosion" && go.scene.IsValid()) { explosion = go; break; }
                }
                Assert.IsNotNull(explosion, "the ground hit triggers an explosion");
                Assert.AreEqual(109.7f, explosion.transform.position.x, 1e-3f, "explosion at the crossing x");
                Assert.AreEqual(strip.HeightAt(109.7f), explosion.transform.position.y, 1e-3f,
                    "explosion on the terrain surface, not inside it");
            } finally {
                fxField.SetValue(null, null);
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                    if (go != null && go.name == "Explosion" && go.scene.IsValid()) Object.DestroyImmediate(go);
                }
                typeof(TerrainStrip).GetProperty("Instance").SetValue(null, null);
            }
        }
    }
}
