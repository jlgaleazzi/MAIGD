using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Biplanes.Core;
using Biplanes.Missions;
using Biplanes.Enemies;
using Biplanes.Player;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// B064: recharge bomb balloon. BombBay.AddBombs clamps to capacity and raises
    /// BombsChanged; a collected balloon grants BombBalloonData.grant bombs and is used up even
    /// at a full bay; the empty-bay RechargeTimer fires after the delay (not before), cancels on
    /// a refill and spawns once per empty-bay event. EditMode doesn't run Awake on AddComponent,
    /// so private state is set and the private Collect is invoked by reflection.
    /// </summary>
    public class BombBalloonTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        /// <summary>A player object with TeamTag(Player) and a BombBay holding `bombs` of 7.</summary>
        private BombBay MakeBay(int bombs) {
            var go = new GameObject("B064_Player");
            _created.Add(go);
            go.AddComponent<TeamTag>().team = Team.Player;
            var bay = go.AddComponent<BombBay>();
            var cap = typeof(BombBay).GetField("_capacity", Private);
            var rem = typeof(BombBay).GetField("_remaining", Private);
            Assert.IsNotNull(cap, "BombBay._capacity not found by reflection");
            Assert.IsNotNull(rem, "BombBay._remaining not found by reflection");
            cap.SetValue(bay, 7);
            rem.SetValue(bay, bombs);
            Assert.AreEqual(bombs, bay.BombsLeft);
            return bay;
        }

        private BombBalloon MakeBalloon() {
            var balloon = BombBalloon.Spawn(new Vector3(0f, 2f, 0f));
            _created.Add(balloon.gameObject);
            return balloon;
        }

        private static void Collect(BombBalloon balloon, BombBay bay) {
            var collect = typeof(BombBalloon).GetMethod("Collect", Private);
            Assert.IsNotNull(collect, "BombBalloon.Collect not found by reflection");
            collect.Invoke(balloon, new object[] { bay.GetComponent<TeamTag>() });
        }

        // ── Balance ───────────────────────────────────────────────────────

        [Test]
        public void Balance_Defaults_AndJsonValues() {
            var defaults = BombBalloonData.Resolve(null);
            Assert.AreEqual(4, defaults.grant);
            Assert.AreEqual(4f, defaults.delay, 1e-4f);
            Assert.AreEqual(12f, defaults.lead, 1e-4f);

            var empty = BombBalloonData.Resolve(new GameBalance());
            Assert.AreEqual(BombBalloonData.DefaultGrant, empty.grant, "missing key falls back");
            Assert.AreEqual(BombBalloonData.DefaultDelay, empty.delay, 1e-4f);
            Assert.AreEqual(BombBalloonData.DefaultLead, empty.lead, 1e-4f);

            var balance = GameBalance.Get();
            Assume.That(balance != null, "Balance.json not loadable");
            var data = BombBalloonData.Resolve(balance);
            Assert.AreEqual(4, data.grant, "Balance.json bombBalloonGrant");
            Assert.AreEqual(4f, data.delay, 1e-4f, "Balance.json bombBalloonDelay");
            Assert.Greater(data.lead, 0f);
        }

        // ── BombBay.AddBombs ──────────────────────────────────────────────

        [Test]
        public void ClampedAdd_Grants_AndClampsToCapacity() {
            Assert.AreEqual(4, BombBay.ClampedAdd(0, 4, 7), "0 + 4 = 4");
            Assert.AreEqual(7, BombBay.ClampedAdd(5, 4, 7), "5 + 4 clamps to 7");
            Assert.AreEqual(7, BombBay.ClampedAdd(7, 4, 7), "full stays full");
            Assert.AreEqual(3, BombBay.ClampedAdd(3, -2, 7), "a negative grant adds nothing");
        }

        [Test]
        public void AddBombs_FromZero_GivesFour_AndRaisesBombsChanged() {
            var bay = MakeBay(0);
            int reported = -1;
            System.Action<int> onChanged = b => reported = b;
            GameEvents.BombsChanged += onChanged;
            try {
                bay.AddBombs(4);
                Assert.AreEqual(4, bay.BombsLeft);
                Assert.AreEqual(4, reported, "the HUD hears the new count");
            } finally {
                GameEvents.BombsChanged -= onChanged;
            }
        }

        [Test]
        public void AddBombs_FromFive_ClampsToSeven() {
            var bay = MakeBay(5);
            bay.AddBombs(4);
            Assert.AreEqual(7, bay.BombsLeft);
        }

        // ── Collection ────────────────────────────────────────────────────

        [Test]
        public void Collect_FromEmptyBay_GrantsFour_AndIsUsedUp() {
            var bay = MakeBay(0);
            var balloon = MakeBalloon();
            string destroyedType = null;
            Team destroyedTeam = Team.Player;
            System.Action<string, Team> onDestroyed = (t, team) => { destroyedType = t; destroyedTeam = team; };
            GameEvents.EntityDestroyed += onDestroyed;
            try {
                Collect(balloon, bay);
            } finally {
                GameEvents.EntityDestroyed -= onDestroyed;
            }
            Assert.AreEqual(BombBalloonData.Resolve(GameBalance.Get()).grant, bay.BombsLeft);
            Assert.IsTrue(balloon.IsDead);
            Assert.IsFalse(balloon.gameObject.activeSelf, "the balloon is recycled");
            Assert.AreEqual(nameof(BombBalloon), destroyedType);
            Assert.AreEqual(Team.Neutral, destroyedTeam);
        }

        [Test]
        public void Collect_AtFullBay_StaysFull_AndIsStillUsedUp() {
            var bay = MakeBay(7);
            var balloon = MakeBalloon();
            Collect(balloon, bay);
            Assert.AreEqual(7, bay.BombsLeft);
            Assert.IsTrue(balloon.IsDead, "a balloon taken at a full bay is still consumed");
            Assert.IsFalse(balloon.gameObject.activeSelf);
        }

        [Test]
        public void Balloon_IsNeutral_SoWeaponsIgnoreIt() {
            var balloon = MakeBalloon();
            Assert.AreEqual(Team.Neutral, balloon.Team);
            balloon.ApplyDamage(100f, DamageSource.Bomb);
            Assert.IsFalse(balloon.IsDead, "damage never destroys a pickup");
        }

        // ── Recharge timer ────────────────────────────────────────────────

        [Test]
        public void Timer_FiresAtFourSeconds_NotAt3_9() {
            var timer = new BombBalloon.RechargeTimer(4f);
            timer.OnBombsChanged(1);
            timer.OnBombsChanged(0);
            Assert.IsTrue(timer.Armed);
            Assert.IsFalse(timer.Tick(3.9f), "not due at 3.9 s");
            Assert.IsTrue(timer.Tick(0.1f), "due at 4.0 s");
        }

        [Test]
        public void Timer_DoesNotRunWhileBayHasBombs() {
            var timer = new BombBalloon.RechargeTimer(4f);
            timer.OnBombsChanged(7);
            Assert.IsFalse(timer.Armed);
            Assert.IsFalse(timer.Tick(10f));
        }

        [Test]
        public void Timer_CancelsOnRefill() {
            var timer = new BombBalloon.RechargeTimer(4f);
            timer.OnBombsChanged(0);
            Assert.IsFalse(timer.Tick(3f));
            timer.OnBombsChanged(4);
            Assert.IsFalse(timer.Armed, "a refill cancels the timer");
            Assert.IsFalse(timer.Tick(5f));
            timer.OnBombsChanged(0);
            Assert.IsFalse(timer.Tick(3.9f), "a new empty bay restarts from zero");
            Assert.IsTrue(timer.Tick(0.1f));
        }

        [Test]
        public void Timer_NoDoubleSpawn_OnePerEmptyBayEvent() {
            var timer = new BombBalloon.RechargeTimer(4f);
            timer.OnBombsChanged(0);
            Assert.IsTrue(timer.Tick(4f));
            timer.MarkSpawned();
            Assert.IsFalse(timer.Tick(10f), "no second spawn from the same empty bay");
            timer.OnBombsChanged(0);
            Assert.IsFalse(timer.Armed, "a repeated 0 report doesn't re-arm");
            Assert.IsFalse(timer.Tick(10f));

            // Collect (4), drop them all again: one more spawn.
            timer.OnBombsChanged(4);
            timer.OnBombsChanged(0);
            Assert.IsTrue(timer.Armed);
            Assert.IsTrue(timer.Tick(4f), "each new empty-bay event can spawn one more");
        }

        [Test]
        public void Timer_StaysDueUntilSpawned_SoABlockedSpawnRetries() {
            var timer = new BombBalloon.RechargeTimer(4f);
            timer.OnBombsChanged(0);
            Assert.IsTrue(timer.Tick(4f));
            Assert.IsTrue(timer.Tick(0f), "still due next frame while blocked");
            timer.OnBombsChanged(0);
            Assert.IsTrue(timer.Due, "a repeated 0 report doesn't restart the count");
        }

        [Test]
        public void Timer_ZeroDtDoesNotAdvance_PauseAndHold() {
            var timer = new BombBalloon.RechargeTimer(4f);
            timer.OnBombsChanged(0);
            for (int i = 0; i < 100; i++) Assert.IsFalse(timer.Tick(0f), "timeScale 0 never advances");
            Assert.AreEqual(0f, timer.Elapsed, 1e-6f);
        }

        // ── Placement helpers ─────────────────────────────────────────────

        [Test]
        public void AutoSpawn_PlacesAheadOfPlayer_InsideMissionAndBand() {
            Assert.AreEqual(22f, BombBalloon.AutoSpawnX(10f, 1, 12f, 0f, 100f), 1e-4f);
            Assert.AreEqual(38f, BombBalloon.AutoSpawnX(50f, -1, 12f, 0f, 100f), 1e-4f, "facing left: ahead is left");
            Assert.AreEqual(100f, BombBalloon.AutoSpawnX(95f, 1, 12f, 0f, 100f), 1e-4f, "clamped to the mission end");
            Assert.AreEqual(0f, BombBalloon.AutoSpawnX(5f, -1, 12f, 0f, 100f), 1e-4f, "clamped to the mission start");

            Assert.AreEqual(3f, BombBalloon.AutoSpawnY(3f, 1f, 5f), 1e-4f, "player height when inside the band");
            Assert.AreEqual(1f, BombBalloon.AutoSpawnY(-2f, 1f, 5f), 1e-4f, "never under the terrain clearance");
            Assert.AreEqual(5f, BombBalloon.AutoSpawnY(9f, 1f, 5f), 1e-4f, "never above the screen top margin");
        }

        // ── B065: re-arm after a missed balloon ───────────────────────────

        [Test]
        public void Rearm_MissedBalloon_WithEmptyBay_Rearms() {
            // The auto balloon scrolled off behind the player: it no longer blocks.
            bool serves = BombBalloon.BlocksAutoSpawn(-30f, -4f, 1, 12f, 0f, 9f);
            Assert.IsFalse(serves);
            Assert.IsTrue(MissionRunner.ShouldRearmRecharge(0, false, serves), "missed balloon -> re-arm");
        }

        [Test]
        public void Rearm_NotWhenCollected_OnScreen_OrAlreadyCounting() {
            Assert.IsFalse(MissionRunner.ShouldRearmRecharge(4, false, false), "collected (bay refilled) -> no re-arm");
            Assert.IsFalse(MissionRunner.ShouldRearmRecharge(0, false, true), "balloon still on screen/ahead -> wait");
            Assert.IsFalse(MissionRunner.ShouldRearmRecharge(0, true, false), "already counting -> no double timer");
            Assert.IsFalse(MissionRunner.ShouldRearmRecharge(-1, false, false), "no bay report yet -> nothing");
        }

        [Test]
        public void Rearm_RestartsTheFullDelay_ThenSpawnsOnce() {
            var timer = new BombBalloon.RechargeTimer(4f);
            timer.OnBombsChanged(0);
            Assert.IsTrue(timer.Tick(4f));
            timer.MarkSpawned();
            Assert.IsFalse(timer.Armed);

            // MissionRunner's re-arm (missed balloon): Reset + empty report.
            timer.Reset();
            timer.OnBombsChanged(0);
            Assert.IsTrue(timer.Armed);
            Assert.IsFalse(timer.Tick(3.9f), "the replacement waits the full delay");
            Assert.IsTrue(timer.Tick(0.1f));
            timer.MarkSpawned();
            Assert.IsFalse(timer.Tick(10f), "still one at a time");

            timer.OnBombsChanged(4);
            Assert.IsFalse(timer.Armed, "a refill cancels");
        }

        // ── B065: pickups score nothing, silently ─────────────────────────

        [Test]
        public void ScoreKeeper_NeutralPickups_ScoreZero_WithoutWarning() {
            Assert.IsTrue(ScoreKeeper.IsScorelessPickup("BombBalloon"));
            Assert.IsTrue(ScoreKeeper.IsScorelessPickup("HealthBalloon"));
            Assert.IsTrue(ScoreKeeper.IsScorelessPickup("bombballoon"));
            Assert.IsFalse(ScoreKeeper.IsScorelessPickup("fighter"));

            var go = new GameObject("B065_ScoreKeeper");
            _created.Add(go);
            var sk = go.AddComponent<ScoreKeeper>();
            sk.OnEntityDestroyed(nameof(BombBalloon), Team.Neutral);
            sk.OnEntityDestroyed("HealthBalloon", Team.Neutral);
            Assert.AreEqual(0, sk.CurrentScore);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ReadableType_HasBombBalloonName() {
            Assert.AreEqual("bomb balloon", ObjectiveTracker.ReadableType("bombBalloon", false));
            Assert.AreEqual("bomb balloons", ObjectiveTracker.ReadableType("bombBalloon", true));
        }

        [Test]
        public void BlocksAutoSpawn_OnScreenOrAheadWithinLead_Only() {
            // Camera at 0, half width 9; player at -4 facing right; lead 12.
            Assert.IsTrue(BombBalloon.BlocksAutoSpawn(5f, -4f, 1, 12f, 0f, 9f), "on screen");
            Assert.IsTrue(BombBalloon.BlocksAutoSpawn(-8f, -4f, 1, 12f, 0f, 9f), "on screen behind still blocks");
            Assert.IsTrue(BombBalloon.BlocksAutoSpawn(7.5f, -4f, 1, 12f, 0f, 3f), "ahead within lead");
            Assert.IsFalse(BombBalloon.BlocksAutoSpawn(20f, -4f, 1, 12f, 0f, 9f), "far ahead doesn't block");
            Assert.IsFalse(BombBalloon.BlocksAutoSpawn(-30f, -4f, 1, 12f, 0f, 9f), "missed behind doesn't block");
        }
    }
}
