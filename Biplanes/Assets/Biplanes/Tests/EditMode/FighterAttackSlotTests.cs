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
    /// B060: at most fighterChase.maxAttackers fighters fire at once (FighterAttackSlots), and
    /// each fighter holds its own standoff in [standoff, standoffMax] so a pack spreads out.
    ///
    /// The coordinator is pure C# with explicit time: most tests drive their own instance.
    /// The FighterAI integration tests use FighterAttackSlots.Shared (reset in SetUp and
    /// TearDown, so nothing leaks into other fixtures); in EditMode FighterAI's slot clock is
    /// frozen at 0 and Awake/TickFire are invoked by reflection as in EnemyFireTests.
    /// </summary>
    public class FighterAttackSlotTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const float Eps = 1e-4f;
        private const float Gap = 1.8f;

        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp() {
            FighterAttackSlots.Shared.Reset();
        }

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
            FighterAttackSlots.Shared.Reset();

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

        // ── Pure coordinator helpers ────────────────────────────────────

        /// <summary>Reports every key eligible at `now` with the given distances.</summary>
        private static void ReportAll(FighterAttackSlots s, object[] keys, float[] dist, float now) {
            for (int i = 0; i < keys.Length; i++) s.Report(keys[i], true, dist[i], now);
        }

        private static bool Acquire(FighterAttackSlots s, object key, float now) {
            return s.TryAcquire(key, now, Gap, out _);
        }

        private static object[] Keys(int n) {
            var k = new object[n];
            for (int i = 0; i < n; i++) k[i] = new object();
            return k;
        }

        // ── Pure coordinator ────────────────────────────────────────────

        [Test]
        public void SlotCap_OnlyMaxAttackersHoldSlots() {
            var s = new FighterAttackSlots(3, 0.5f);
            var keys = Keys(6);
            var dist = new float[] { 4f, 4.5f, 5f, 5.5f, 6f, 6.5f };
            ReportAll(s, keys, dist, 0f);
            int granted = 0;
            foreach (var k in keys) if (Acquire(s, k, 0f)) granted++;
            Assert.AreEqual(3, granted, "only maxAttackers get a slot");
            Assert.AreEqual(3, s.HolderCount);
            Assert.AreEqual(6, s.TrackedCount, "the others keep reporting and wait");
        }

        [Test]
        public void NearestFirst_EvenWhenFartherFightersAskFirst() {
            var s = new FighterAttackSlots(3, 0.5f);
            var keys = Keys(5);
            var dist = new float[] { 9f, 4f, 7f, 5f, 6f };
            ReportAll(s, keys, dist, 0f);
            // Farthest asks first.
            Assert.IsFalse(Acquire(s, keys[0], 0f), "9 u: four closer fighters wait");
            Assert.IsFalse(Acquire(s, keys[2], 0f), "7 u: three closer fighters wait");
            Assert.IsTrue(Acquire(s, keys[4], 0f), "6 u: third nearest");
            Assert.IsTrue(Acquire(s, keys[3], 0f), "5 u");
            Assert.IsTrue(Acquire(s, keys[1], 0f), "4 u");
            Assert.IsFalse(Acquire(s, keys[2], 0f), "full");
            Assert.IsTrue(s.Holds(keys[1]) && s.Holds(keys[3]) && s.Holds(keys[4]));
        }

        [Test]
        public void NotEligible_NeverGetsASlot_AndDoesNotBlockFartherOnes() {
            var s = new FighterAttackSlots(1, 0.5f);
            var near = new object();
            var far = new object();
            s.Report(near, false, 2f, 0f);
            s.Report(far, true, 8f, 0f);
            Assert.IsFalse(Acquire(s, near, 0f), "nose off the player: no slot");
            Assert.IsTrue(Acquire(s, far, 0f), "the nearer one can't shoot, so it doesn't block");
        }

        [Test]
        public void ReleaseOnDeathOrEscape_FreesTheSlot_ForTheNextNearest() {
            var s = new FighterAttackSlots(2, 0.5f);
            var keys = Keys(4);
            var dist = new float[] { 3f, 4f, 5f, 6f };
            ReportAll(s, keys, dist, 0f);
            foreach (var k in keys) Acquire(s, k, 0f);
            Assert.IsTrue(s.Holds(keys[0]) && s.Holds(keys[1]));

            s.Release(keys[0]); // died
            Assert.IsFalse(s.Holds(keys[0]));
            Assert.AreEqual(3, s.TrackedCount, "a released fighter is forgotten");
            ReportAll(s, new[] { keys[1], keys[2], keys[3] }, new[] { 4f, 5f, 6f }, 0.02f);
            Assert.IsFalse(Acquire(s, keys[3], 0.02f), "6 u: 5 u goes first");
            Assert.IsTrue(Acquire(s, keys[2], 0.02f), "next nearest takes the freed slot");

            s.Release(keys[1]); // escaped / re-entered
            s.Report(keys[3], true, 6f, 0.04f);
            Assert.IsTrue(Acquire(s, keys[3], 0.04f));
        }

        [Test]
        public void DestroyedUnityObject_IsPruned_AndItsSlotFreed() {
            var s = new FighterAttackSlots(1, 0.5f);
            var go = new GameObject("fighter");
            var other = new object();
            s.Report(go, true, 3f, 0f);
            Assert.IsTrue(Acquire(s, go, 0f));
            Object.DestroyImmediate(go);
            s.Report(other, true, 5f, 0f);
            Assert.IsTrue(Acquire(s, other, 0f), "a destroyed holder gives its slot up");
            Assert.AreEqual(1, s.TrackedCount);
        }

        [Test]
        public void AimLoss_KeepsTheSlotWithinGrace_ReleasesAfter() {
            var s = new FighterAttackSlots(1, 0.5f);
            var a = new object();
            s.Report(a, true, 3f, 0f);
            Assert.IsTrue(Acquire(s, a, 0f));
            s.Report(a, false, 3f, 0.3f);
            Assert.IsTrue(s.Holds(a), "a brief aim loss keeps the slot");
            s.Report(a, true, 3f, 0.45f);
            s.Report(a, false, 3f, 0.9f);
            Assert.IsTrue(s.Holds(a), "the grace restarts once it aims again");
            s.Report(a, false, 3f, 0.96f);
            Assert.IsFalse(s.Holds(a), "more than 0.5 s without aim: released");
        }

        [Test]
        public void Rotation_HolderThatCantShoot_HandsOver_WithTheSlotShotGap() {
            var s = new FighterAttackSlots(1, 0.5f);
            var holder = new object();
            var waiting = new object();
            s.Report(holder, true, 3f, 0f);
            s.Report(waiting, true, 5f, 0f);
            Assert.IsTrue(Acquire(s, holder, 0f));
            s.NotifyShot(holder, 0f);
            Assert.IsFalse(Acquire(s, waiting, 0f), "slot taken");

            // The holder loses its aim; the other keeps it.
            float t = 0f;
            bool handedOver = false;
            float wait = -1f;
            for (int i = 1; i <= 40 && !handedOver; i++) {
                t = i * 0.02f;
                s.Report(holder, false, 3f, t);
                s.Report(waiting, true, 5f, t);
                handedOver = s.TryAcquire(waiting, t, Gap, out wait);
            }
            Assert.IsTrue(handedOver, "the slot rotates to the waiting fighter (no starvation)");
            Assert.Greater(t, 0.5f - Eps, "only after the grace");
            Assert.Less(t, 0.6f, "right after the grace");
            Assert.IsFalse(s.Holds(holder));
            Assert.AreEqual(Gap - t, wait, 1e-3f, "waits out the slot's shot gap since the last shot");

            // A slot that never fired hands over with no wait.
            var fresh = new FighterAttackSlots(1, 0.5f);
            var k = new object();
            fresh.Report(k, true, 1f, 10f);
            Assert.IsTrue(fresh.TryAcquire(k, 10f, Gap, out float w0));
            Assert.AreEqual(0f, w0, Eps);
        }

        [Test]
        public void StaleReporter_IsDropped() {
            var s = new FighterAttackSlots(1, 0.5f);
            var gone = new object();
            var other = new object();
            s.Report(gone, true, 3f, 0f);
            Assert.IsTrue(Acquire(s, gone, 0f));
            s.Report(other, true, 5f, 1f);
            Assert.IsTrue(Acquire(s, other, 1f), "a holder that stopped reporting loses its slot");
            Assert.AreEqual(1, s.TrackedCount);
        }

        [Test]
        public void ReleaseAll_AndBindTarget_ResetTheSlots() {
            var s = new FighterAttackSlots(3, 0.5f);
            var playerA = new object();
            s.BindTarget(playerA);
            var keys = Keys(3);
            ReportAll(s, keys, new[] { 1f, 2f, 3f }, 0f);
            foreach (var k in keys) Acquire(s, k, 0f);
            Assert.AreEqual(3, s.HolderCount);

            s.ReleaseAll(); // the player died
            Assert.AreEqual(0, s.HolderCount);
            Assert.AreEqual(0, s.TrackedCount);

            ReportAll(s, keys, new[] { 1f, 2f, 3f }, 0f);
            foreach (var k in keys) Acquire(s, k, 0f);
            s.BindTarget(playerA);
            Assert.AreEqual(3, s.HolderCount, "the same player changes nothing");
            s.BindTarget(new object()); // next mission / Retry: a new player
            Assert.AreEqual(0, s.HolderCount);
            Assert.AreEqual(0, s.TrackedCount);
        }

        [Test]
        public void Configure_ClampsAndResizes() {
            var s = new FighterAttackSlots(0, -1f);
            Assert.AreEqual(1, s.MaxAttackers, "at least one slot");
            Assert.AreEqual(0f, s.GraceSeconds, Eps);
            s.Configure(4, 0.5f);
            Assert.AreEqual(4, s.MaxAttackers);
            Assert.AreEqual(0.5f, s.GraceSeconds, Eps);
        }

        // ── Balance ─────────────────────────────────────────────────────

        [Test]
        public void Balance_HasTheB060Values_AndResolveDefaults() {
            var d = FighterChaseData.Resolve(null);
            Assert.AreEqual(3, d.maxAttackers);
            // QA T967: the code default is still 6 (GameBalance.FighterChaseData.DefaultStandoffMax);
            // Balance is 5 since B061. Reported to mechanics as a P2: only a missing Balance key
            // uses the default. Pinned at its current value so a fix shows up here.
            Assert.AreEqual(FighterChaseData.DefaultStandoffMax, d.standoffMax, Eps);
            Assert.AreEqual(0.5f, d.slotGraceSeconds, Eps);

            var c = FighterChaseData.Resolve(GameBalance.Get());
            Assert.AreEqual(3, c.maxAttackers, "Balance fighterChase.maxAttackers");
            Assert.AreEqual(5f, c.standoffMax, Eps, "Balance fighterChase.standoffMax (B061: 6 -> 5)");
            Assert.AreEqual(0.5f, c.slotGraceSeconds, Eps, "Balance fighterChase.slotGraceSeconds");

            var low = new GameBalance { fighterChase = new FighterChaseData { standoff = 5f, standoffMax = 4f } };
            var r = FighterChaseData.Resolve(low);
            Assert.AreEqual(5f, r.standoffMax, Eps, "standoffMax below standoff is raised to it");
        }

        // ── Standoff spread ─────────────────────────────────────────────

        [Test]
        public void PickStandoff_UniformRange_Clamped() {
            Assert.AreEqual(3.5f, FighterAI.PickStandoff(3.5f, 6f, 0f), Eps);
            Assert.AreEqual(6f, FighterAI.PickStandoff(3.5f, 6f, 1f), Eps);
            Assert.AreEqual(4.75f, FighterAI.PickStandoff(3.5f, 6f, 0.5f), Eps);
            Assert.AreEqual(6f, FighterAI.PickStandoff(3.5f, 6f, 2f), Eps, "t clamped");
            Assert.AreEqual(5f, FighterAI.PickStandoff(5f, 4f, 0.7f), Eps, "max below min gives min");
        }

        [Test]
        public void EachFighter_PicksItsStandoffOnce_InRange_PackSpreads() {
            var c = FighterChaseData.Resolve(GameBalance.Get());
            var player = MakePlayer(new Vector2(0f, 0f));
            float min = float.MaxValue, max = float.MinValue;
            var step = typeof(FighterAI).GetMethod("ChaseStep", Private);
            Assert.IsNotNull(step, "FighterAI.ChaseStep not found by reflection");
            for (int i = 0; i < 20; i++) {
                var f = MakeFighter(new Vector2(8f + i, 0f), player);
                float s0 = f.Standoff;
                Assert.GreaterOrEqual(s0, c.standoff - Eps);
                Assert.LessOrEqual(s0, c.standoffMax + Eps);
                for (int t = 0; t < 25; t++) {
                    f.transform.position = (Vector3)step.Invoke(f, new object[] { f.transform.position, 0.02f });
                }
                Assert.AreEqual(s0, f.Standoff, 0f, "stable for the fighter's life");
                min = Mathf.Min(min, s0);
                max = Mathf.Max(max, s0);
            }
            Assert.Greater(max - min, 0.5f, "20 fighters don't share one standoff");
        }

        // ── FighterAI integration (Shared coordinator) ──────────────────

        private static void InvokeAwake(Component c) {
            var awake = c.GetType().GetMethod("Awake", Private);
            Assert.IsNotNull(awake, $"{c.GetType().Name}.Awake not found by reflection");
            awake.Invoke(c, null);
        }

        private static bool TickFire(FighterAI f, float dt) {
            var m = typeof(FighterAI).GetMethod("TickFire", Private);
            Assert.IsNotNull(m, "FighterAI.TickFire not found by reflection");
            return (bool)m.Invoke(f, new object[] { dt });
        }

        private static void SetPlayerRef(FighterAI f, PlayerPlane plane) {
            var prop = typeof(FighterAI).GetProperty("PlayerRef", Private);
            Assert.IsNotNull(prop, "FighterAI.PlayerRef not found by reflection");
            prop.SetValue(f, plane);
        }

        private PlayerPlane MakePlayer(Vector2 position) {
            var go = new GameObject("player");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<TeamTag>().team = Team.Player;
            InvokeAwake(go.AddComponent<Health>());
            return go.AddComponent<PlayerPlane>();
        }

        private FighterAI MakeFighter(Vector2 position, PlayerPlane player) {
            var f = FighterAI.Spawn(position);
            _created.Add(f.gameObject);
            InvokeAwake(f.GetComponent<Health>());
            InvokeAwake(f);
            SetPlayerRef(f, player);
            return f;
        }

        private static void PrimeBulletPool() {
            EnemyBullet.Spawn(Vector3.zero, Vector2.left, DamageSource.Bullet).Recycle();
            foreach (var b in Object.FindObjectsByType<EnemyBullet>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                InvokeAwake(b);
            }
        }

        private static int ActiveBullets() {
            return Object.FindObjectsByType<EnemyBullet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        }

        /// <summary>Five left-flying fighters dead ahead of the player (level nose on target) at
        /// 4..8 u; one reporting pass (dt 0), then fire ticks farthest first.</summary>
        private List<FighterAI> MakePack(PlayerPlane player) {
            var pack = new List<FighterAI>();
            for (int i = 0; i < 5; i++) pack.Add(MakeFighter(new Vector2(4f + i, 0f), player));
            foreach (var f in pack) TickFire(f, 0f);
            return pack;
        }

        [Test]
        public void Pack_OnlyTheThreeNearestFire() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var player = MakePlayer(Vector2.zero);
            PrimeBulletPool();
            var pack = MakePack(player);

            int shots = 0;
            for (int i = pack.Count - 1; i >= 0; i--) {
                if (TickFire(pack[i], fire.firstShotDelayMax + 0.01f)) shots++;
            }
            Assert.AreEqual(3, shots, "maxAttackers fighters fire");
            Assert.AreEqual(3, ActiveBullets());
            Assert.IsTrue(pack[0].HasAttackSlot && pack[1].HasAttackSlot && pack[2].HasAttackSlot, "the nearest three");
            Assert.IsFalse(pack[3].HasAttackSlot || pack[4].HasAttackSlot, "the others wait");

            // Waiting fighters keep holding fire, however long they wait.
            Assert.IsFalse(TickFire(pack[4], 10f));
            Assert.IsFalse(TickFire(pack[3], 10f));
        }

        [Test]
        public void Pack_DeathAndReEntry_FreeSlots_ForTheNextNearest() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var player = MakePlayer(Vector2.zero);
            PrimeBulletPool();
            var pack = MakePack(player);
            for (int i = pack.Count - 1; i >= 0; i--) TickFire(pack[i], fire.firstShotDelayMax + 0.01f);

            pack[0].ApplyDamage(GameBalance.Get().enemyHealth.fighter, DamageSource.Bullet);
            Assert.IsTrue(pack[0].IsDead);
            Assert.IsFalse(TickFire(pack[0], 0.02f), "dead: no fire");
            Assert.IsFalse(pack[0].HasAttackSlot, "dead: slot released");

            Assert.IsFalse(TickFire(pack[3], 0.02f), "took over a slot that just fired: waits out its gap");
            Assert.IsTrue(pack[3].HasAttackSlot, "the next nearest got the freed slot");
            Assert.IsFalse(pack[4].HasAttackSlot);
            Assert.IsTrue(TickFire(pack[3], fire.fighterIntervalMin + 0.01f), "fires once the slot's gap ran out");

            pack[1].ReEnter(new Vector3(20f, 0f, 0f), -1);
            Assert.IsFalse(pack[1].HasAttackSlot, "re-entering frees the slot");
            pack[1].transform.position = new Vector3(5f, 0f, 0f);
            TickFire(pack[1], 0f); // reports (its own cooldown still runs)
            Assert.IsFalse(TickFire(pack[4], 0f), "8 u: the nearer re-entered fighter goes first");
            Assert.IsFalse(pack[4].HasAttackSlot);
            Assert.IsFalse(TickFire(pack[1], fire.fighterIntervalMax + 0.01f), "back in: waits out the slot's gap");
            Assert.IsTrue(pack[1].HasAttackSlot, "the nearest waiting fighter takes the freed slot");
            Assert.IsFalse(pack[4].HasAttackSlot);
            Assert.AreEqual(3, FighterAttackSlots.Shared.HolderCount);
        }

        [Test]
        public void PlayerDeath_ReleasesAllSlots_NewPlayer_ResetsThem() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var player = MakePlayer(Vector2.zero);
            PrimeBulletPool();
            var pack = MakePack(player);
            for (int i = pack.Count - 1; i >= 0; i--) TickFire(pack[i], fire.firstShotDelayMax + 0.01f);
            Assert.AreEqual(3, FighterAttackSlots.Shared.HolderCount);

            player.GetComponent<Health>().ApplyDamage(1f, DamageSource.Collision);
            Assert.IsTrue(player.GetComponent<Health>().IsDead);
            Assert.IsFalse(TickFire(pack[4], 0.02f));
            Assert.AreEqual(0, FighterAttackSlots.Shared.HolderCount, "the player died: every slot free");

            // Next mission / Retry: a new player plane. Leftover holders from the old one are dropped.
            var player2 = MakePlayer(Vector2.zero);
            for (int i = pack.Count - 1; i >= 0; i--) SetPlayerRef(pack[i], player2);
            foreach (var f in pack) TickFire(f, 0f);
            Assert.AreEqual(0, FighterAttackSlots.Shared.HolderCount);
            int shots = 0;
            for (int i = pack.Count - 1; i >= 0; i--) {
                if (TickFire(pack[i], fire.fighterIntervalMax + 0.01f)) shots++;
            }
            Assert.AreEqual(3, shots, "fresh slots for the new player, no carried-over gap");
        }
    }
}
