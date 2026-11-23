using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Player;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// B023: enemy fighters and bomber gunners shoot EnemyBullets at the player.
    ///
    /// EditMode doesn't run Awake/Start on AddComponent or FixedUpdate, so tests invoke the
    /// private Awake by reflection, set the internal PlayerRef, and drive the internal
    /// TickFire / TickGunner with explicit time steps. With no camera cached (Start never runs)
    /// the shooters count as on screen; the off-screen test injects a camera.
    ///
    /// B029: fighters fire along their nose (± spread) and only while the player is within
    /// enemyFire.aimToleranceDegrees of it; the nose eases toward the player through the
    /// internal UpdatePitch, which the tests drive with TrackPlayer.
    /// </summary>
    public class EnemyFireTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();

            foreach (var p in Object.FindObjectsByType<EnemyBullet>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (p != null) Object.DestroyImmediate(p.gameObject);
            }
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go == null) continue;
                if (go.name == "EnemyBulletPool" || go.name == "Pool<EnemyBullet>") {
                    if (go.scene.IsValid() || go.hideFlags == HideFlags.HideAndDontSave) Object.DestroyImmediate(go);
                }
            }
            typeof(EnemyBullet).GetField("_cam", PrivateStatic)?.SetValue(null, null);
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

        private static bool Tick(Component shooter, string method, float dt) {
            var m = shooter.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{shooter.GetType().Name}.{method} not found by reflection");
            return (bool)m.Invoke(shooter, new object[] { dt });
        }

        private static void SetPlayerRef(Component shooter, PlayerPlane plane) {
            var prop = shooter.GetType().GetProperty("PlayerRef", Private);
            Assert.IsNotNull(prop, $"{shooter.GetType().Name}.PlayerRef not found by reflection");
            prop.SetValue(shooter, plane);
        }

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

        /// <summary>A player stand-in: TeamTag(Player) + Health (percent mode) + PlayerPlane
        /// (Awake not run, so HasCrashed is false) at `position`.</summary>
        private PlayerPlane MakePlayer(Vector2 position) {
            var h = MakeHealth("player", Team.Player);
            h.transform.position = position;
            return h.gameObject.AddComponent<PlayerPlane>();
        }

        private FighterAI MakeFighter(Vector2 position, PlayerPlane player) {
            var f = FighterAI.Spawn(position);
            _created.Add(f.gameObject);
            InvokeAwake(f.GetComponent<Health>());
            InvokeAwake(f);
            SetPlayerRef(f, player);
            return f;
        }

        private BomberAI MakeBomber(Vector2 position, PlayerPlane player) {
            var b = BomberAI.Spawn(position);
            _created.Add(b.gameObject);
            InvokeAwake(b.GetComponent<Health>());
            InvokeAwake(b);
            SetPlayerRef(b, player);
            return b;
        }

        /// <summary>Lets a fighter in level flight turn its nose toward the player for 2 s.</summary>
        private static void TrackPlayer(FighterAI f) {
            var m = typeof(FighterAI).GetMethod("UpdatePitch", Private);
            Assert.IsNotNull(m, "FighterAI.UpdatePitch not found by reflection");
            for (int i = 0; i < 100; i++) m.Invoke(f, new object[] { 0f, 0.02f });
        }

        private static List<EnemyBullet> ActiveBullets() {
            return new List<EnemyBullet>(
                Object.FindObjectsByType<EnemyBullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        }

        /// <summary>Spawns once so the pool exists, runs Awake on every pooled bullet (as Play
        /// mode's AddComponent would), and returns them to the pool.</summary>
        private static void PrimeBulletPool() {
            EnemyBullet.Spawn(Vector3.zero, Vector2.left, DamageSource.Bullet).Recycle();
            foreach (var b in Object.FindObjectsByType<EnemyBullet>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                InvokeAwake(b);
            }
        }

        private static int AliveRootsNamed(string name) {
            int n = 0;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go != null && go.name == name && go.transform.parent == null && go.scene.IsValid()) n++;
            }
            return n;
        }

        // ── Balance ─────────────────────────────────────────────────────

        [Test]
        public void EnemyFire_Resolve_ReadsBalance_AndDefaultsWhenMissing() {
            var defaults = EnemyFireData.Resolve(null);
            Assert.AreEqual(EnemyFireData.DefaultBulletSpeed, defaults.bulletSpeed);
            Assert.AreEqual(EnemyFireData.DefaultFighterIntervalMin, defaults.fighterIntervalMin);
            Assert.AreEqual(EnemyFireData.DefaultBomberIntervalMax, defaults.bomberIntervalMax);
            Assert.AreEqual(EnemyFireData.DefaultRange, defaults.range);

            var g = GameBalance.Get();
            Assert.IsNotNull(g, "Balance.json must load");
            Assert.IsNotNull(g.enemyFire, "Balance.json has an enemyFire block");
            var f = EnemyFireData.Resolve(g);
            Assert.AreEqual(g.enemyFire.bulletSpeed, f.bulletSpeed);
            Assert.Less(f.bulletSpeed, g.player.bulletSpeed, "enemy rounds are slower than the player's, so they can be dodged");
            Assert.That(f.fighterIntervalMin, Is.InRange(1.5f, 2.5f));
            Assert.That(f.fighterIntervalMax, Is.InRange(f.fighterIntervalMin, 3f));
            Assert.That(f.bomberIntervalMin, Is.InRange(2f, 3f));
            Assert.That(f.bomberIntervalMax, Is.InRange(f.bomberIntervalMin, 3.5f));
            Assert.Less(f.fighterConeDegrees, 90f, "a fighter never fires backwards");

            Assert.AreEqual(EnemyFireData.DefaultAimToleranceDegrees, defaults.aimToleranceDegrees);
            Assert.AreEqual(g.enemyFire.aimToleranceDegrees, f.aimToleranceDegrees, "Balance enemyFire.aimToleranceDegrees");
            Assert.Greater(f.aimToleranceDegrees, 0f);
        }

        // ── EnemyBullet ─────────────────────────────────────────────────

        [Test]
        public void EnemyBullet_FighterShotDealsBalanceFighterBullet_BomberShotDealsBalanceBomberGunner() {
            PrimeBulletPool();
            var taken = GameBalance.Get().damageTaken;
            // B040 / GDD change log 2026-11-18: a fighter shot costs 5%, a bomber gunner shot 10%.
            Assert.AreEqual(5f, taken.fighterBullet, 1e-4f, "Balance damageTaken.fighterBullet is 5 (B040)");
            Assert.AreEqual(10f, taken.bomberGunner, 1e-4f, "Balance damageTaken.bomberGunner is 10");

            // B052: player hits are divided by Balance player.resistance (1.5).
            float res = PlayerData.ResistanceOrDefault(GameBalance.Get().player);
            var player = MakeHealth("player", Team.Player);
            var fighterShot = EnemyBullet.Spawn(Vector3.zero, Vector2.left, FighterAI.ShotSource);
            InvokeOnHit(fighterShot, player);
            Assert.AreEqual(taken.fighterBullet / res, player.CurrentDamagePercent, 1e-4f, "fighter shot = damageTaken.fighterBullet / resistance");
            Assert.IsFalse(fighterShot.gameObject.activeSelf, "the bullet recycles on a hit");

            var bomberShot = EnemyBullet.Spawn(Vector3.zero, Vector2.left, BomberAI.GunnerShotSource);
            InvokeOnHit(bomberShot, player);
            Assert.AreEqual((taken.fighterBullet + taken.bomberGunner) / res, player.CurrentDamagePercent, 1e-4f,
                "bomber shot = damageTaken.bomberGunner / resistance");
            Assert.IsFalse(bomberShot.gameObject.activeSelf);
        }

        [Test]
        public void EnemyBullet_IgnoresEnemiesAndNeutrals_AndStaysInFlight() {
            PrimeBulletPool();
            var enemy = MakeHealth("fighter", Team.Enemy, 25f);
            var neutral = MakeHealth("healthBalloon", Team.Neutral);

            var bullet = EnemyBullet.Spawn(Vector3.zero, Vector2.left, DamageSource.Bullet);
            InvokeOnHit(bullet, enemy);
            InvokeOnHit(bullet, neutral);

            Assert.AreEqual(0f, enemy.CurrentDamagePercent);
            Assert.AreEqual(0f, neutral.CurrentDamagePercent);
            Assert.IsTrue(bullet.gameObject.activeSelf, "a non-player target does not stop the bullet");
        }

        [Test]
        public void EnemyBullet_TeamEnemy_OneHitUnit_SpeedFromBalance_InkDotGapDot() {
            PrimeBulletPool();
            var fire = EnemyFireData.Resolve(GameBalance.Get());

            var bullet = EnemyBullet.Spawn(Vector3.zero, new Vector2(-3f, 4f), DamageSource.Bullet);
            Assert.AreEqual(Team.Enemy, bullet.Team);
            Assert.AreEqual(1f, bullet.Damage);
            Assert.AreEqual(fire.bulletSpeed, bullet.Speed);
            Assert.AreEqual(fire.bulletLifetime, bullet.Lifetime);

            var v = bullet.GetComponent<Rigidbody2D>().linearVelocity;
            Assert.AreEqual(fire.bulletSpeed, v.magnitude, 1e-3f);
            Assert.AreEqual(-0.6f, v.normalized.x, 1e-3f);
            Assert.AreEqual(0.8f, v.normalized.y, 1e-3f);

            // White only (B038a/B039): the renderer is untinted and the ink lives in a 3x1
            // dot-gap-dot sprite at 100 ppu (ink, transparent, ink).
            var sr = bullet.GetComponent<SpriteRenderer>();
            Assert.AreEqual(Color.white, sr.color, "no tint: the pixels carry the ink");
            Assert.IsNotNull(sr.sprite);
            Assert.AreEqual(3f, sr.sprite.rect.width, 1e-4f);
            Assert.AreEqual(1f, sr.sprite.rect.height, 1e-4f);
            Assert.AreEqual(100f, sr.sprite.pixelsPerUnit, 1e-4f);
            var tex = sr.sprite.texture;
            var ink = new Color(0xE8 / 255f, 0xF1 / 255f, 0xFA / 255f, 1f);
            foreach (int x in new[] { 0, 2 }) {
                var p = tex.GetPixel(x, 0);
                Assert.AreEqual(ink.r, p.r, 2e-3f, "pixel " + x + " r");
                Assert.AreEqual(ink.g, p.g, 2e-3f, "pixel " + x + " g");
                Assert.AreEqual(ink.b, p.b, 2e-3f, "pixel " + x + " b");
                Assert.AreEqual(1f, p.a, 2e-3f, "pixel " + x + " opaque");
            }
            Assert.AreEqual(0f, tex.GetPixel(1, 0).a, 2e-3f, "middle pixel is the gap");
        }

        [Test]
        public void EnemyBullet_PoolUnderSceneRoot_RebuiltAfterRootDestroyed() {
            Assert.IsNotNull(EnemyBullet.Spawn(Vector3.zero, Vector2.left, DamageSource.Bullet));
            Assert.IsNotNull(EnemyBullet.Spawn(Vector3.zero, Vector2.left, DamageSource.Bullet));
            Assert.AreEqual(1, AliveRootsNamed("EnemyBulletPool"), "one pool root, reused");

            var root = (GameObject)typeof(EnemyBullet).GetField("_root", PrivateStatic).GetValue(null);
            var firstPool = typeof(EnemyBullet).GetField("_pool", PrivateStatic).GetValue(null);
            Object.DestroyImmediate(root);   // what a scene unload does

            Assert.IsNotNull(EnemyBullet.Spawn(Vector3.zero, Vector2.left, DamageSource.Bullet));
            Assert.AreNotSame(firstPool, typeof(EnemyBullet).GetField("_pool", PrivateStatic).GetValue(null));
            Assert.AreEqual(1, AliveRootsNamed("EnemyBulletPool"));
        }

        [Test]
        public void EnemyBullet_LeavingTheScreen_Recycles() {
            PrimeBulletPool();
            var camGo = new GameObject("B023_Camera");
            _created.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            typeof(EnemyBullet).GetField("_cam", PrivateStatic).SetValue(null, cam);

            var bullet = EnemyBullet.Spawn(Vector3.zero, Vector2.left, DamageSource.Bullet);
            var update = typeof(EnemyBullet).GetMethod("Update", Private);
            update.Invoke(bullet, null);
            Assert.IsTrue(bullet.gameObject.activeSelf, "on screen: still flying");

            bullet.transform.position = new Vector3(-100f, 0f, 0f);
            update.Invoke(bullet, null);
            Assert.IsFalse(bullet.gameObject.activeSelf, "off screen: recycled");
        }

        // ── Aiming helpers ──────────────────────────────────────────────

        [Test]
        public void InFiringCone_AheadInRange_Only() {
            Vector2 at = new Vector2(10f, 0f);
            Assert.IsTrue(EnemyBullet.InFiringCone(at, new Vector2(4f, 1f), Vector2.left, 40f, 11f));
            Assert.IsFalse(EnemyBullet.InFiringCone(at, new Vector2(14f, 0f), Vector2.left, 40f, 11f), "behind");
            Assert.IsFalse(EnemyBullet.InFiringCone(at, new Vector2(9f, 5f), Vector2.left, 40f, 11f), "too steep");
            Assert.IsFalse(EnemyBullet.InFiringCone(at, new Vector2(-5f, 0f), Vector2.left, 40f, 11f), "out of range");
            Assert.IsTrue(EnemyBullet.InFiringCone(at, new Vector2(14f, 0f), Vector2.left, 180f, 11f), "180 = any direction");
        }

        [Test]
        public void AimWithinCone_NeverFiresBackwards() {
            for (int i = 0; i < 50; i++) {
                Vector2 dir = EnemyBullet.AimWithinCone(Vector2.zero, new Vector2(5f, 0.5f), Vector2.left, 40f, 4f);
                Assert.AreEqual(1f, dir.magnitude, 1e-3f);
                Assert.LessOrEqual(Vector2.Angle(Vector2.left, dir), 40.01f, "clamped into the forward cone");
                Assert.Less(dir.x, 0f);

                Vector2 ahead = EnemyBullet.AimWithinCone(Vector2.zero, new Vector2(-5f, 1f), Vector2.left, 40f, 4f);
                Assert.LessOrEqual(Vector2.Angle(new Vector2(-5f, 1f), ahead), 4.01f, "aim error stays within the spread");
            }
        }

        // ── FighterAI gun ───────────────────────────────────────────────

        [Test]
        public void Fighter_FiresAtPlayerAhead_WithCadenceFromBalance() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var player = MakePlayer(new Vector2(0f, 0f));
            var fighter = MakeFighter(new Vector2(6f, 1f), player);
            TrackPlayer(fighter);

            Assert.IsTrue(Tick(fighter, "TickFire", fire.firstShotDelayMax + 0.01f), "fires once the first-shot delay ran out");
            var shots = ActiveBullets();
            Assert.AreEqual(1, shots.Count);
            Assert.AreEqual(FighterAI.ShotSource, shots[0].Source);
            Assert.AreEqual(Team.Enemy, shots[0].Team);
            Assert.Less(shots[0].transform.position.x, 6f, "fired from the nose, ahead of the centre");

            Assert.IsFalse(Tick(fighter, "TickFire", fire.fighterIntervalMin - 0.05f), "no shot before fighterIntervalMin");
            Assert.IsTrue(Tick(fighter, "TickFire", fire.fighterIntervalMax - fire.fighterIntervalMin + 0.1f),
                "a shot by fighterIntervalMax");
        }

        [Test]
        public void Fighter_HoldsFire_PlayerBehindOrOutOfRange() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var player = MakePlayer(new Vector2(0f, 0f));
            var behind = MakeFighter(new Vector2(-4f, 0f), player);
            var far = MakeFighter(new Vector2(fire.range + 5f, 0f), player);

            Assert.IsFalse(Tick(behind, "TickFire", 10f), "player behind the fighter");
            Assert.IsFalse(Tick(far, "TickFire", 10f), "player out of range");
            Assert.AreEqual(0, ActiveBullets().Count);
        }

        [Test]
        public void Fighter_DeadOrPlayerDead_NeverFires() {
            var player = MakePlayer(new Vector2(0f, 0f));
            var dead = MakeFighter(new Vector2(5f, 0f), player);
            dead.ApplyDamage(GameBalance.Get().enemyHealth.fighter, DamageSource.Bullet);
            Assert.IsTrue(dead.IsDead);
            Assert.IsFalse(Tick(dead, "TickFire", 10f), "a dead fighter never fires");

            var alive = MakeFighter(new Vector2(5f, 0f), player);
            player.GetComponent<Health>().ApplyDamage(1f, DamageSource.Collision);
            Assert.IsTrue(player.GetComponent<Health>().IsDead);
            Assert.IsFalse(Tick(alive, "TickFire", 10f), "no fire at a dead player");
            Assert.AreEqual(0, ActiveBullets().Count);
        }

        [Test]
        public void Fighter_OffScreen_HoldsFire_AndCooldownDoesNotRun() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var camGo = new GameObject("B023_Camera");
            _created.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;

            var player = MakePlayer(new Vector2(0f, 0f));
            var fighter = MakeFighter(new Vector2(40f, 0f), player);
            typeof(FighterAI).GetField("_cam", Private).SetValue(fighter, cam);

            Assert.IsFalse(Tick(fighter, "TickFire", 100f), "off screen: no shot");
            fighter.transform.position = new Vector3(5f, 0f, 0f);
            Assert.IsFalse(Tick(fighter, "TickFire", fire.firstShotDelayMin * 0.5f),
                "the cooldown did not run off screen, so no shot right on entry");
        }

        [Test]
        public void Fighter_HoldsFire_UntilItsNoseIsOnThePlayer() {
            // B029: player 30 degrees above the flight line (inside the 40 degree cone), nose level.
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var player = MakePlayer(new Vector2(0f, 3.5f));
            var fighter = MakeFighter(new Vector2(6f, 0f), player);
            Assume.That(Vector2.Angle(Vector2.left, new Vector2(-6f, 3.5f)) > fire.aimToleranceDegrees);

            Assert.IsFalse(Tick(fighter, "TickFire", fire.firstShotDelayMax + 0.01f),
                "nose more than aimToleranceDegrees off the player: no shot");
            Assert.AreEqual(0, ActiveBullets().Count);

            TrackPlayer(fighter);
            Assert.LessOrEqual(Vector2.Angle(fighter.NoseDirection, new Vector2(-6f, 3.5f)), fire.aimToleranceDegrees);
            Assert.IsTrue(Tick(fighter, "TickFire", 0.01f), "nose on the player: the held shot goes at once");
            Assert.AreEqual(1, ActiveBullets().Count);
        }

        [Test]
        public void Fighter_Shot_FliesAlongTheNose_WithinSpread_NotStraightAtThePlayer() {
            // Player inside the tolerance but off the level nose: the round follows the nose.
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            float off = fire.aimToleranceDegrees * 0.8f;
            var player = MakePlayer(new Vector2(6f - 6f, 6f * Mathf.Tan(off * Mathf.Deg2Rad)));
            var fighter = MakeFighter(new Vector2(6f, 0f), player);
            Assert.AreEqual(0f, fighter.PitchAngle, 1e-5f, "nose level");

            PrimeBulletPool();
            for (int i = 0; i < 20; i++) {
                Assert.IsTrue(Tick(fighter, "TickFire", fire.fighterIntervalMax + 0.01f));
            }
            foreach (var b in ActiveBullets()) {
                Vector2 v = b.GetComponent<Rigidbody2D>().linearVelocity;
                Assert.LessOrEqual(Vector2.Angle(fighter.NoseDirection, v), fire.spreadDegrees + 0.05f,
                    "the shot flies along the nose, ± spread");
                Assert.Less(b.transform.position.x, 6f, "fired from the nose, ahead of the centre");
            }
        }

        // ── Right-flying fighters (B040, T958) ──────────────────────────

        /// <summary>A fighter flying right (Awake run, SetFlightDirection(+1) before Start), as the
        /// chase leaves it after turning toward the player. Since B074 SpawnReinforcementFighter
        /// and ReEnterFighter always leave a fighter flying left (EntryFlightDirection).</summary>
        private FighterAI MakeRightFighter(Vector2 position, PlayerPlane player) {
            var f = MakeFighter(position, player);
            f.SetFlightDirection(1);
            return f;
        }

        [Test]
        public void RightFighter_Start_KeepsSpriteUnmirrored_NoseAndBodyPointRight() {
            var player = MakePlayer(new Vector2(20f, 0f));
            var f = MakeRightFighter(new Vector2(0f, 0f), player);
            var sr = f.GetComponent<SpriteRenderer>();
            Assert.IsFalse(sr.flipX, "SetFlightDirection(+1): the right-facing art is not mirrored");

            var start = typeof(FighterAI).GetMethod("Start", Private);
            Assert.IsNotNull(start, "FighterAI.Start not found by reflection");
            start.Invoke(f, null);
            Assert.IsFalse(sr.flipX, "Start must not mirror a right-flying fighter (no tail-first sprite)");
            Assert.Greater(f.NoseDirection.x, 0f, "the nose points right");

            f.SetFlightDirection(-1);
            Assert.IsTrue(sr.flipX, "back to left: mirrored again");
        }

        [Test]
        public void RightFighter_PlayerAhead_FiresRight_FromTheNose() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var player = MakePlayer(new Vector2(6f, 0f));
            var f = MakeRightFighter(new Vector2(0f, 0f), player);

            PrimeBulletPool();
            for (int i = 0; i < 10; i++) {
                Assert.IsTrue(Tick(f, "TickFire", fire.fighterIntervalMax + 0.01f), $"shot {i}: player dead ahead on the right");
            }
            foreach (var b in ActiveBullets()) {
                Vector2 v = b.GetComponent<Rigidbody2D>().linearVelocity;
                Assert.Greater(v.x, 0f, "a right-flying fighter never shoots backwards");
                Assert.LessOrEqual(Vector2.Angle(Vector2.right, v), fire.spreadDegrees + 0.05f, "along the level nose ± spread");
                Assert.Greater(b.transform.position.x, 0f, "fired from the nose (ahead on the right)");
                float z = b.transform.eulerAngles.z;
                Assert.That(Mathf.DeltaAngle(0f, z), Is.InRange(-fire.spreadDegrees - 0.05f, fire.spreadDegrees + 0.05f),
                    "the 3x1 tracer is turned along +x");
            }
        }

        [Test]
        public void RightFighter_PlayerBehindOnTheLeft_HoldsFire() {
            var player = MakePlayer(new Vector2(-6f, 0f));
            var f = MakeRightFighter(new Vector2(0f, 0f), player);
            Assert.IsFalse(Tick(f, "TickFire", 10f), "player behind a right-flying fighter: no shot");
            Assert.AreEqual(0, ActiveBullets().Count);
        }

        [Test]
        public void RightFighter_PitchesNoseUpTowardAPlayerAbove() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var player = MakePlayer(new Vector2(6f, 3.5f));
            var f = MakeRightFighter(new Vector2(0f, 0f), player);
            Assert.IsFalse(Tick(f, "TickFire", fire.firstShotDelayMax + 0.01f), "level nose, player 30 degrees up: held");

            TrackPlayer(f);
            Assert.Greater(f.PitchAngle, 0f, "nose up");
            Assert.Greater(f.BodyAngle, 0f, "unmirrored body turns counter-clockwise to climb (BodyAngle = +pitch)");
            Assert.Greater(f.NoseDirection.x, 0f);
            Assert.Greater(f.NoseDirection.y, 0f);
            Assert.LessOrEqual(Vector2.Angle(f.NoseDirection, new Vector2(6f, 3.5f)), fire.aimToleranceDegrees);
            Assert.IsTrue(Tick(f, "TickFire", 0.01f), "nose on the player: fires");
            var shots = ActiveBullets();
            Assert.AreEqual(1, shots.Count);
            Vector2 v = shots[0].GetComponent<Rigidbody2D>().linearVelocity;
            Assert.Greater(v.x, 0f);
            Assert.Greater(v.y, 0f, "climbing shot");
        }

        // ── BomberAI gunner ─────────────────────────────────────────────

        [Test]
        public void Bomber_Gunner_FiresAnyDirection_WithBomberCadence() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var player = MakePlayer(new Vector2(0f, 0f));
            var bomber = MakeBomber(new Vector2(-4f, 2f), player);   // player behind the bomber
            float x0 = bomber.transform.position.x;

            Assert.IsTrue(Tick(bomber, "TickGunner", fire.firstShotDelayMax + 0.01f));
            var shots = ActiveBullets();
            Assert.AreEqual(1, shots.Count);
            Assert.AreEqual(BomberAI.GunnerShotSource, shots[0].Source);

            Assert.IsFalse(Tick(bomber, "TickGunner", fire.bomberIntervalMin - 0.05f));
            Assert.IsTrue(Tick(bomber, "TickGunner", fire.bomberIntervalMax - fire.bomberIntervalMin + 0.1f));
            Assert.AreEqual(x0, bomber.transform.position.x, "the gunner never moves the bomber");
        }

        [Test]
        public void Bomber_Gunner_HoldsFire_OutOfRange_OrDead() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var player = MakePlayer(new Vector2(0f, 0f));
            var far = MakeBomber(new Vector2(fire.range + 3f, 0f), player);
            Assert.IsFalse(Tick(far, "TickGunner", 10f));

            var dead = MakeBomber(new Vector2(3f, 0f), player);
            dead.GetComponent<Health>().ApplyDamage(10000f, DamageSource.Bomb);
            Assert.IsTrue(dead.GetComponent<Health>().IsDead);
            Assert.IsFalse(Tick(dead, "TickGunner", 10f));
            Assert.AreEqual(0, ActiveBullets().Count);
        }
    }
}
