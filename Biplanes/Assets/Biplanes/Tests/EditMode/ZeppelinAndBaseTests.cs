using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Fx;
using Biplanes.Weapons;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// T936: EditMode tests for T932 (EnemyBase, PlayerHQ), T933 (ZeppelinAI, ZeppelinTurret)
    /// and B014 (FlakShell exact-direction overload, DamageSource.ZeppelinTurret,
    /// enemySpeed.zeppelin).
    ///
    /// EditMode doesn't run Awake on AddComponent, so tests invoke the private Awake by
    /// reflection where the code relies on Balance values. Deaths call FxManager.Instance
    /// (seeded by reflection: its lazy getter uses DontDestroyOnLoad) and Destroy(gameObject),
    /// which logs an edit-mode error; tests that reach it set LogAssert.ignoreFailingMessages.
    /// </summary>
    public class ZeppelinAndBaseTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<string> _destroyedTypes = new List<string>();
        private Action<string, Team> _onDestroyed;
        private GameObject _fxGo;

        [SetUp]
        public void SetUp() {
            _destroyedTypes.Clear();
            _onDestroyed = (type, team) => _destroyedTypes.Add(type);
            GameEvents.EntityDestroyed += _onDestroyed;
            _fxGo = new GameObject("T936_Fx");
            typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, _fxGo.AddComponent<FxManager>());
        }

        [TearDown]
        public void TearDown() {
            GameEvents.EntityDestroyed -= _onDestroyed;
            typeof(FxManager).GetField("_instance", PrivateStatic).SetValue(null, null);
            if (_fxGo != null) Object.DestroyImmediate(_fxGo);

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
                // B030: a flak hit on the player now also plays a "FlakBurst" (T945).
                bool fx = go.name == "Explosion" || go.name == "FlakBurst" || go.name == "SmokePuff";
                if ((pool || fx) && (go.scene.IsValid() || go.hideFlags == HideFlags.HideAndDontSave)) {
                    Object.DestroyImmediate(go);
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

        private Health MakePlayerHealth(string name) {
            var go = new GameObject(name);
            _created.Add(go);
            go.AddComponent<TeamTag>().team = Team.Player;
            var h = go.AddComponent<Health>();
            InvokeAwake(h);
            Assert.AreEqual(Team.Player, h.Team);
            return h;
        }

        private static GameBalance Balance() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.damageTaken != null && g.enemyHealth != null && g.player != null,
                "Balance.json not loadable");
            return g;
        }

        private static void AwakeAllShells() {
            foreach (var s in Object.FindObjectsByType<FlakShell>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                InvokeAwake(s);
            }
        }

        private ZeppelinAI SpawnZeppelin(Vector3 pos, string id) {
            var z = ZeppelinAI.Spawn(pos, id);
            _created.Add(z.gameObject);
            InvokeAwake(z);
            foreach (var t in z.GetComponentsInChildren<ZeppelinTurret>()) InvokeAwake(t);
            return z;
        }

        // ── B014: Balance, DamageSource, Health mapping ─────────────────

        [Test]
        public void Balance_HasZeppelinSpeed_AndTurretDamageDistinctFromFlak() {
            var g = Balance();
            Assert.IsNotNull(g.enemySpeed);
            Assert.AreEqual(1.0f, g.enemySpeed.zeppelin, 1e-6f, "ARCHITECTURE §4 enemySpeed.zeppelin");
            Assert.AreEqual(12f, g.damageTaken.zeppelinTurret, 1e-6f);
            Assert.AreEqual(15f, g.damageTaken.flak, 1e-6f);
        }

        [Test]
        public void DamageSource_ZeppelinTurret_AppendedLast_OrdinalsUnchanged() {
            Assert.AreEqual(0, (int)DamageSource.Bullet);
            Assert.AreEqual(1, (int)DamageSource.Bomb);
            Assert.AreEqual(2, (int)DamageSource.Flak);
            Assert.AreEqual(3, (int)DamageSource.Collision);
            Assert.AreEqual(4, (int)DamageSource.Heal);
            Assert.AreEqual(5, (int)DamageSource.ZeppelinTurret);
        }

        [Test]
        public void PlayerHealth_MapsZeppelinTurretTo12_FlakTo15() {
            var g = Balance();
            float res = PlayerData.ResistanceOrDefault(g.player);   // B052: 12 -> 8, 15 -> 10
            var a = MakePlayerHealth("T936_PlayerA");
            a.ApplyDamage(1f, DamageSource.ZeppelinTurret);
            Assert.AreEqual(g.damageTaken.zeppelinTurret / res, a.CurrentDamagePercent, 1e-4f);

            var b = MakePlayerHealth("T936_PlayerB");
            b.ApplyDamage(1f, DamageSource.Flak);
            Assert.AreEqual(g.damageTaken.flak / res, b.CurrentDamagePercent, 1e-4f);
        }

        // ── B014: FlakShell overloads ───────────────────────────────────

        [Test]
        public void FlakShell_ExactOverload_FliesDownAlongAim_AtShellSpeed() {
            FlakShell.Spawn(Vector3.zero, Vector2.up).Recycle();
            AwakeAllShells();   // speed, team and damage are set in Awake

            var down = FlakShell.Spawn(Vector3.zero, new Vector2(1f, -1f), DamageSource.ZeppelinTurret);
            Assert.IsNotNull(down);
            Vector2 v = down.GetComponent<Rigidbody2D>().linearVelocity;
            Assert.AreEqual(FlakShell.ShellSpeed, v.magnitude, 1e-3f);
            Assert.AreEqual(new Vector2(1f, -1f).normalized.x, v.normalized.x, 1e-3f, "no forced climb");
            Assert.Less(v.y, 0f, "fires downward");
            Assert.AreEqual(DamageSource.ZeppelinTurret, down.Source);

            var zero = FlakShell.Spawn(Vector3.zero, Vector2.zero, DamageSource.ZeppelinTurret);
            Vector2 zv = zero.GetComponent<Rigidbody2D>().linearVelocity;
            Assert.AreEqual(0f, zv.x, 1e-4f);
            Assert.AreEqual(-FlakShell.ShellSpeed, zv.y, 1e-3f, "a zero direction flies straight down");

            // The 2-argument (AAA) overload still climbs and reports Flak.
            var aaa = FlakShell.Spawn(Vector3.zero, new Vector2(1f, -1f));
            Assert.Greater(aaa.GetComponent<Rigidbody2D>().linearVelocity.y, 0f);
            Assert.AreEqual(DamageSource.Flak, aaa.Source);
        }

        [Test]
        public void FlakShell_TurretHitAdds12_ReusedShellFromAAAOverloadAdds15() {
            var g = Balance();
            FlakShell.Spawn(Vector3.zero, Vector2.up).Recycle();
            AwakeAllShells();

            var turretShell = FlakShell.Spawn(Vector3.zero, Vector2.down, DamageSource.ZeppelinTurret);
            float res = PlayerData.ResistanceOrDefault(g.player);   // B052
            var p1 = MakePlayerHealth("T936_P1");
            InvokeOnHit(turretShell, p1);
            Assert.AreEqual(g.damageTaken.zeppelinTurret / res, p1.CurrentDamagePercent, 1e-4f);
            Assert.IsFalse(turretShell.gameObject.activeSelf, "recycled after the hit");

            // Pop until the recycled turret shell comes back from the AAA overload.
            FlakShell reused = null;
            for (int i = 0; i < 30 && reused == null; i++) {
                var s = FlakShell.Spawn(Vector3.zero, Vector2.up);
                if (s == null) break;
                Assert.AreEqual(DamageSource.Flak, s.Source, "every AAA spawn reports Flak");
                if (ReferenceEquals(s, turretShell)) reused = s;
            }
            Assume.That(reused != null, "pool never handed the recycled shell back");
            var p2 = MakePlayerHealth("T936_P2");
            InvokeOnHit(reused, p2);
            Assert.AreEqual(g.damageTaken.flak / res, p2.CurrentDamagePercent, 1e-4f, "source reset on reuse");
        }

        // ── T932: EnemyBase ─────────────────────────────────────────────

        [Test]
        public void EnemyBase_Spawn_GroundBound_Trigger_NamedTypeKey() {
            var b = EnemyBase.Spawn(new Vector3(460f, -4.1f, 0f), "enemy_base_01");
            _created.Add(b.gameObject);
            Assert.AreEqual(EnemyBase.TypeKey, b.gameObject.name);
            Assert.AreEqual("enemy_base_01", b.Id);
            Assert.AreEqual(Team.Enemy, b.Team);
            Assert.AreEqual(Team.Enemy, b.GetComponent<TeamTag>().Team);
            var col = b.GetComponent<BoxCollider2D>();
            Assert.IsTrue(col != null && col.isTrigger);
            Assert.AreEqual(RigidbodyType2D.Kinematic, b.GetComponent<Rigidbody2D>().bodyType);
            var sr = b.GetComponent<SpriteRenderer>();
            Assert.AreEqual(5, sr.sortingOrder);
            Assert.AreEqual(-4.1f, sr.bounds.min.y, 1e-3f, "bottom edge on the ground point");
            Assert.AreEqual(460f, b.transform.position.x, 1e-4f);
        }

        [Test]
        public void EnemyBase_BombOnly_DiesOnce_RaisesBothEventsOnce() {
            var g = Balance();
            var b = EnemyBase.Spawn(new Vector3(460f, -4.1f, 0f), "enemy_base_01");
            _created.Add(b.gameObject);
            var fired = new List<EnemyBase>();
            Action<EnemyBase> onBase = x => fired.Add(x);
            EnemyBase.Destroyed += onBase;
            try {
                b.ApplyDamage(g.player.bulletDamage, DamageSource.Bullet);
                b.ApplyDamage(1f, DamageSource.Flak);
                b.ApplyDamage(1f, DamageSource.Collision);
                b.ApplyDamage(1f, DamageSource.ZeppelinTurret);
                b.ApplyDamage(-50f, DamageSource.Bomb);
                Assert.AreEqual(0f, b.CurrentDamagePercent, "only Bomb damages the base");
                Assert.IsFalse(b.IsDead);

                LogAssert.ignoreFailingMessages = true;   // Destroy() in edit mode logs an error
                b.ApplyDamage(g.player.bombDamage, DamageSource.Bomb);
                Assert.AreEqual(g.player.bombDamage >= g.enemyHealth.enemyBase, b.IsDead,
                    "bombDamage vs enemyHealth.enemyBase decides the one-bomb kill");
                if (!b.IsDead) b.ApplyDamage(g.enemyHealth.enemyBase, DamageSource.Bomb);
                Assert.IsTrue(b.IsDead);
                Assert.IsTrue(b.KilledByBomb);

                b.ApplyDamage(g.player.bombDamage, DamageSource.Bomb);
                CollectionAssert.AreEqual(new[] { EnemyBase.TypeKey }, _destroyedTypes, "EntityDestroyed once");
                Assert.AreEqual(1, fired.Count, "Destroyed once");
                Assert.AreSame(b, fired[0]);
            } finally {
                EnemyBase.Destroyed -= onBase;
            }
        }

        [Test]
        public void EnemyBase_Score_IsAwardedOnceFromEntityDestroyed() {
            var g = Balance();
            Assume.That(g.score != null);
            var skField = typeof(ScoreKeeper).GetField("_instance", PrivateStatic);
            var prev = skField.GetValue(null);
            skField.SetValue(null, null);
            var skGo = new GameObject("T936_ScoreKeeper");
            _created.Add(skGo);
            var sk = skGo.AddComponent<ScoreKeeper>();
            InvokeAwake(sk);   // subscribes to EntityDestroyed
            try {
                var b = EnemyBase.Spawn(new Vector3(0f, -4.5f, 0f), "");
                _created.Add(b.gameObject);
                LogAssert.ignoreFailingMessages = true;
                b.ApplyDamage(g.enemyHealth.enemyBase, DamageSource.Bomb);
                b.ApplyDamage(g.enemyHealth.enemyBase, DamageSource.Bomb);
                Assert.AreEqual(g.score.enemyBase, sk.CurrentScore);
            } finally {
                typeof(ScoreKeeper).GetMethod("OnDestroy", Private).Invoke(sk, null);
                skField.SetValue(null, prev);
            }
        }

        // ── T932: PlayerHQ ──────────────────────────────────────────────

        [Test]
        public void PlayerHQ_NeutralNotDamageable_GroundBound() {
            var hq = PlayerHQ.Spawn(new Vector3(650f, -4.5f, 0f), "hq_01");
            _created.Add(hq.gameObject);
            Assert.AreEqual("hq_01", hq.Id);
            Assert.AreEqual(650f, hq.WorldX, 1e-4f);
            Assert.AreEqual(Team.Neutral, hq.GetComponent<TeamTag>().Team);
            // Projectile.OnTriggerEnter2D and Bomb.BurstDamage both resolve
            // GetComponentInParent<IDamageable>(); null = ignored (no hit, no burst, no score).
            Assert.IsNull(hq.GetComponentInParent<IDamageable>(), "nothing can damage or score the HQ");
            var sr = hq.GetComponent<SpriteRenderer>();
            Assert.AreEqual(-4.5f, sr.bounds.min.y, 1e-3f, "bottom edge on the ground point");
            Assert.IsTrue(hq.GetComponent<BoxCollider2D>().isTrigger);
        }

        // ── T933: ZeppelinAI / ZeppelinTurret ───────────────────────────

        [Test]
        public void Zeppelin_Spawn_NameTurretsAndMirroredGeometry() {
            var z = SpawnZeppelin(new Vector3(60f, 0.9f, 0f), "zeppelin_01");
            Assert.AreEqual("zeppelin [zeppelin_01]", z.gameObject.name);
            Assert.AreEqual("zeppelin_01", z.Id);
            Assert.AreEqual(3, z.TurretsRemaining);
            Assert.IsTrue(z.IsShielded);
            Assert.IsTrue(z.GetComponent<SpriteRenderer>().flipX, "flies left: art mirrored");
            Assert.AreEqual(5, z.GetComponent<SpriteRenderer>().sortingOrder);

            var boxes = z.GetComponents<BoxCollider2D>();
            Assert.AreEqual(5, boxes.Length, "envelope, tail, 3 gondolas");
            foreach (var box in boxes) Assert.IsTrue(box.isTrigger);
            // Envelope (art offset +0.575) and tail (art -3.46) are mirrored with the sprite.
            Assert.AreEqual(-0.575f, boxes[0].offset.x, 1e-4f);
            Assert.AreEqual(3.46f, boxes[1].offset.x, 1e-4f);

            var turrets = z.GetComponentsInChildren<ZeppelinTurret>();
            Assert.AreEqual(3, turrets.Length);
            Assert.AreEqual(-1.90f, turrets[0].transform.localPosition.x, 1e-4f);
            Assert.AreEqual(1.30f, turrets[1].transform.localPosition.x, 1e-4f);
            Assert.AreEqual(0.65f, turrets[2].transform.localPosition.x, 1e-4f);
            Assert.AreEqual(-1.10f, turrets[2].transform.localPosition.y, 1e-4f, "B021: hangs below the gondola line");
            Assert.IsTrue(turrets[2].GetComponent<SpriteRenderer>().flipY, "underside turret hangs down");
            foreach (var t in turrets) {
                Assert.IsNull(t.GetComponent<Rigidbody2D>(), "turret colliders join the hull's body");
                Assert.AreEqual(Team.Enemy, t.Team);
            }
        }

        /// <summary>
        /// T939: player bullets fly along x only (Bullet.Spawn: direction ±1, no pitch) and a
        /// shielded hull collider absorbs them (ZeppelinAI.ApplyDamage returns, Bullet.OnHit
        /// recycles the bullet anyway). So every turret needs a band of bullet heights that
        /// reaches it from the left or the right before touching an envelope, tail or gondola
        /// box; otherwise the shield can never drop and the zeppelin can't be destroyed.
        /// </summary>
        [Test]
        public void Zeppelin_EveryTurret_HasAClearHorizontalLineOfFire() {
            const float bulletRadius = 0.08f;  // PlayerPlane: MakeProjectile<Bullet>("Bullet", …, 0.08f)
            const float minClearBand = 0.25f;  // B021 acceptance; > two FixedUpdate steps of W/S climb (5 u/s × 0.02 s)
            const float step = 0.002f;

            var z = SpawnZeppelin(new Vector3(60f, 0.9f, 0f), "zeppelin_01");
            var hull = z.GetComponents<BoxCollider2D>();
            Assert.AreEqual(5, hull.Length, "envelope, tail, 3 gondolas");

            var turrets = z.GetComponentsInChildren<ZeppelinTurret>();
            Assert.AreEqual(3, turrets.Length);
            foreach (var t in turrets) {
                var box = t.GetComponent<BoxCollider2D>();
                Assert.IsNotNull(box);
                Vector2 c = (Vector2)t.transform.localPosition + box.offset;
                float tx0 = c.x - box.size.x * 0.5f, tx1 = c.x + box.size.x * 0.5f;
                // Bullet-centre heights whose circle overlaps the turret box.
                float y0 = c.y - box.size.y * 0.5f - bulletRadius;
                float y1 = c.y + box.size.y * 0.5f + bulletRadius;

                float best = 0f, run = 0f;
                for (float y = y0 + step * 0.5f; y < y1; y += step) {
                    bool fromLeft = true, fromRight = true;
                    foreach (var h in hull) {
                        float hy0 = h.offset.y - h.size.y * 0.5f - bulletRadius;
                        float hy1 = h.offset.y + h.size.y * 0.5f + bulletRadius;
                        if (y <= hy0 || y >= hy1) continue;
                        float hx0 = h.offset.x - h.size.x * 0.5f, hx1 = h.offset.x + h.size.x * 0.5f;
                        if (hx0 < tx0) fromLeft = false;    // a bullet from the left meets this box first
                        if (hx1 > tx1) fromRight = false;   // a bullet from the right meets this box first
                    }
                    run = (fromLeft || fromRight) ? run + step : 0f;
                    best = Mathf.Max(best, run);
                }

                Assert.GreaterOrEqual(best, minClearBand,
                    $"turret at local ({c.x:0.##}, {c.y:0.##}): only a {best:0.###}-unit band of bullet heights reaches it " +
                    "without first hitting a hull box (gondola/envelope/tail), which absorbs the bullet while shielded");
            }
        }

        [Test]
        public void Zeppelin_Speed_FromBalance() {
            var g = Balance();
            var z = SpawnZeppelin(new Vector3(60f, 0.9f, 0f), "");
            Assert.AreEqual("zeppelin", z.gameObject.name, "no id = plain score key name");
            float speed = (float)typeof(ZeppelinAI).GetField("_speed", Private).GetValue(z);
            Assert.AreEqual(g.enemySpeed.zeppelin, speed, 1e-6f);
        }

        [Test]
        public void Zeppelin_ShieldUntilLastTurret_ScoresEachOnce() {
            var g = Balance();
            Assume.That(!g.hullVulnerableEarly, "Balance hullVulnerableEarly is on");
            var z = SpawnZeppelin(new Vector3(60f, 0.9f, 0f), "zeppelin_01");
            float bullet = g.player.bulletDamage;
            float bomb = g.player.bombDamage;

            z.ApplyDamage(bullet, DamageSource.Bullet);
            z.ApplyDamage(bomb, DamageSource.Bomb);
            z.ApplyDamage(1f, DamageSource.Collision);
            Assert.AreEqual(0f, z.CurrentDamagePercent, "shielded hull absorbs every hit");

            LogAssert.ignoreFailingMessages = true;   // turret/hull Destroy() in edit mode logs an error
            var turrets = z.GetComponentsInChildren<ZeppelinTurret>();
            int bulletsPerTurret = Mathf.CeilToInt(g.enemyHealth.zeppelinTurret / bullet);
            for (int k = 0; k < turrets.Length; k++) {
                var t = turrets[k];
                t.ApplyDamage(1f, DamageSource.Flak);   // enemy fire: ignored
                for (int i = 0; i < bulletsPerTurret - 1; i++) t.ApplyDamage(bullet, DamageSource.Bullet);
                Assert.IsFalse(t.IsDead);
                t.ApplyDamage(bullet, DamageSource.Bullet);
                Assert.IsTrue(t.IsDead);
                t.ApplyDamage(bullet, DamageSource.Bullet);   // dead: no second event
                Assert.AreEqual(turrets.Length - k - 1, z.TurretsRemaining);
                Assert.AreEqual(k < turrets.Length - 1, z.IsShielded);
            }
            Assert.AreEqual(3, _destroyedTypes.FindAll(s => s == ZeppelinTurret.ScoreKey).Count);

            int hullBullets = Mathf.CeilToInt(g.enemyHealth.zeppelinHull / bullet);
            z.ApplyDamage(1f, DamageSource.Flak);
            for (int i = 0; i < hullBullets - 1; i++) z.ApplyDamage(bullet, DamageSource.Bullet);
            Assert.IsFalse(z.IsDead, $"{hullBullets - 1} bullets leave the hull alive");
            z.ApplyDamage(bullet, DamageSource.Bullet);
            Assert.IsTrue(z.IsDead);
            z.ApplyDamage(bullet, DamageSource.Bullet);
            Assert.AreEqual(1, _destroyedTypes.FindAll(s => s == ZeppelinAI.ScoreKey).Count, "hull EntityDestroyed once");
        }

        [Test]
        public void Zeppelin_HullVulnerableEarly_SkipsShield() {
            var z = SpawnZeppelin(new Vector3(60f, 0.9f, 0f), "");
            typeof(ZeppelinAI).GetField("_hullVulnerableEarly", Private).SetValue(z, true);
            Assert.IsFalse(z.IsShielded);
            z.ApplyDamage(25f, DamageSource.Bullet);
            Assert.Greater(z.CurrentDamagePercent, 0f);
        }

        [Test]
        public void Zeppelin_SilentDespawnBehindCamera_RaisesNothing() {
            var camGo = new GameObject("T936_DespawnCam");
            _created.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.aspect = 16f / 9f;
            camGo.transform.position = new Vector3(100f, 0f, -10f);

            var z = SpawnZeppelin(new Vector3(70f, 0.9f, 0f), "zeppelin_01");
            typeof(ZeppelinAI).GetField("_cam", Private).SetValue(z, cam);
            var fixedUpdate = typeof(ZeppelinAI).GetMethod("FixedUpdate", Private);

            // Left edge 100 - 8.89 = 91.1; 70 is 21.1 behind it (> 20): removed silently.
            LogAssert.ignoreFailingMessages = true;   // Destroy() in edit mode logs an error
            fixedUpdate.Invoke(z, null);
            Assert.IsTrue((bool)typeof(ZeppelinAI).GetField("_destroyed", Private).GetValue(z));
            Assert.IsFalse(z.IsDead, "a despawn is not a death");
            Assert.AreEqual(0, _destroyedTypes.Count, "no EntityDestroyed, so no score");

            // Once despawned, hits do nothing either.
            typeof(ZeppelinAI).GetField("_hullVulnerableEarly", Private).SetValue(z, true);
            z.ApplyDamage(10000f, DamageSource.Bomb);
            Assert.AreEqual(0, _destroyedTypes.Count);
        }

        [Test]
        public void Zeppelin_WithinDespawnMargin_Stays() {
            var camGo = new GameObject("T936_StayCam");
            _created.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.aspect = 16f / 9f;
            camGo.transform.position = new Vector3(100f, 0f, -10f);

            var z = SpawnZeppelin(new Vector3(75f, 0.9f, 0f), "");
            typeof(ZeppelinAI).GetField("_cam", Private).SetValue(z, cam);
            typeof(ZeppelinAI).GetMethod("FixedUpdate", Private).Invoke(z, null);
            Assert.IsFalse((bool)typeof(ZeppelinAI).GetField("_destroyed", Private).GetValue(z),
                "16 units behind the left edge is inside the 20-unit margin");
        }
    }
}
