using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Biplanes.Core;
using Biplanes.Missions;
using Biplanes.UI;

namespace Biplanes.Tests {

    /// <summary>
    /// T963 (QA for B050, B052 and B053):
    ///  - ObjectiveTracker.CountsAsKillOf / RemainingKills: every Team.Enemy fighter kill,
    ///    reinforcements included, moves the destroy-all counter once; the required number
    ///    stays the JSON total (GDD "Enemy pressure", change log 2026-11-20).
    ///  - HudView reads the objective number from the runner's tracker (B053), follows a
    ///    reinforcement kill, ignores non-enemy and other-type kills, never prints the raw
    ///    "fighterReinforcement" key, and still counts down on its own with no tracker.
    ///  - Health.PlayerDeathSnap (B052) never kills a player whose real total is below 100.
    ///
    /// EditMode doesn't run Awake/OnDestroy on AddComponent, so HudView's are invoked by
    /// reflection, the way Play mode would run them (as in HudBombIconsTests).
    /// </summary>
    public class FighterCounterQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Reinforcement = ScoreKeeper.ReinforcementFighterKey;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<HudView> _huds = new List<HudView>();

        [TearDown]
        public void TearDown() {
            foreach (var hud in _huds) {
                if (hud != null) Call(hud, "OnDestroy");   // unsubscribes the static events
            }
            _huds.Clear();
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static void Call(object target, string method) {
            var m = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, null);
        }

        private static ObjectiveEntry DestroyAll(string type) {
            return new ObjectiveEntry { id = "kill_all", type = "destroyAllOfType", targetType = type };
        }

        private static SpawnEntry Spawn(string type, int count) {
            return new SpawnEntry { type = type, count = count };
        }

        private ObjectiveTracker MakeTracker(ObjectiveEntry[] objectives, SpawnEntry[] spawns) {
            var go = new GameObject("T963_Tracker");
            _created.Add(go);
            var t = go.AddComponent<ObjectiveTracker>();
            t.Init(objectives, 0f, spawns);
            return t;
        }

        private HudView MakeHud(Transform parent) {
            var canvasGo = new GameObject("T963_Canvas", typeof(RectTransform));
            if (parent != null) canvasGo.transform.SetParent(parent, false);
            else _created.Add(canvasGo);
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var hud = canvasGo.AddComponent<HudView>();
            Call(hud, "Awake");
            _huds.Add(hud);
            return hud;
        }

        /// <summary>A MissionRunner (not started) holding `data`, with an initialised tracker
        /// and a HUD canvas under it, like StartMission builds them.</summary>
        private HudView MakeRunnerHud(MissionData data, out ObjectiveTracker tracker) {
            var go = new GameObject("T963_Runner");
            _created.Add(go);
            var runner = go.AddComponent<MissionRunner>();
            var field = typeof(MissionRunner).GetField("_missionData", Private);
            Assert.IsNotNull(field, "MissionRunner._missionData not found");
            field.SetValue(runner, data);
            tracker = go.AddComponent<ObjectiveTracker>();
            tracker.Init(data.objectives, 0f, data.spawns);
            return MakeHud(go.transform);
        }

        private static string ObjectiveText(HudView hud) {
            var f = typeof(HudView).GetField("_objectiveText", Private);
            Assert.IsNotNull(f, "HudView._objectiveText not found");
            var text = f.GetValue(hud) as Text;
            Assert.IsNotNull(text, "the HUD built no objective text");
            return text.text;
        }

        private static void AssertNoRawKey(HudView hud) {
            StringAssert.DoesNotContain("fighterreinforcement", ObjectiveText(hud).ToLowerInvariant(),
                "the HUD must never print the reinforcement type key");
        }

        // ── CountsAsKillOf ──────────────────────────────────────────────

        [Test]
        public void CountsAsKillOf_ReinforcementCountsForFighterOnly() {
            Assert.IsTrue(ObjectiveTracker.CountsAsKillOf("fighter", Reinforcement));
            Assert.IsTrue(ObjectiveTracker.CountsAsKillOf("Fighter", Reinforcement.ToUpperInvariant()), "case-insensitive");
            Assert.IsTrue(ObjectiveTracker.CountsAsKillOf("fighter", "fighter"));
            Assert.IsTrue(ObjectiveTracker.CountsAsKillOf("aaa", "AAA"));
            Assert.IsFalse(ObjectiveTracker.CountsAsKillOf("bomber", Reinforcement));
            Assert.IsFalse(ObjectiveTracker.CountsAsKillOf(Reinforcement, "fighter"), "not symmetric");
            Assert.IsFalse(ObjectiveTracker.CountsAsKillOf("fighter", "bomber"));
            Assert.IsFalse(ObjectiveTracker.CountsAsKillOf("fighter", null));
            Assert.IsFalse(ObjectiveTracker.CountsAsKillOf(null, "fighter"));
            Assert.IsFalse(ObjectiveTracker.CountsAsKillOf("", ""));
        }

        // ── Required count and RemainingKills ───────────────────────────

        [Test]
        public void RequiredCount_IsTheJsonFighterTotal_ReinforcementEntriesAddNothing() {
            var t = MakeTracker(new[] { DestroyAll("fighter") },
                new[] { Spawn("fighter", 2), Spawn("fighter", 3), Spawn(Reinforcement, 4), Spawn("bomber", 1) });
            Assert.AreEqual(5, t.RemainingKills(0), "required = 2 + 3 JSON fighters only");
        }

        [Test]
        public void RemainingKills_DropsByOnePerCountedKill_FloorsAtZero_CompletesOnce() {
            int completions = 0;
            System.Action<string> onDone = _ => completions++;
            GameEvents.ObjectiveCompleted += onDone;
            try {
                var t = MakeTracker(new[] { DestroyAll("fighter"), new ObjectiveEntry { id = "s", type = "surviveToEnd" } },
                    new[] { Spawn("fighter", 3) });
                Assert.AreEqual(3, t.RemainingKills(0));

                t.OnEntityDestroyed("fighter", Team.Enemy);
                Assert.AreEqual(2, t.RemainingKills(0), "a JSON fighter");
                t.OnEntityDestroyed(Reinforcement, Team.Enemy);
                Assert.AreEqual(1, t.RemainingKills(0), "a reinforcement counts the same");

                // Not counted: other teams, other types.
                t.OnEntityDestroyed("fighter", Team.Player);
                t.OnEntityDestroyed(Reinforcement, Team.Neutral);
                t.OnEntityDestroyed("bomber", Team.Enemy);
                t.OnEntityDestroyed("aaa", Team.Enemy);
                Assert.AreEqual(1, t.RemainingKills(0));
                Assert.AreEqual(0, completions);

                t.OnEntityDestroyed(Reinforcement, Team.Enemy);
                Assert.AreEqual(0, t.RemainingKills(0));
                Assert.AreEqual(1, completions, "the objective completes at the JSON total");

                for (int i = 0; i < 4; i++) t.OnEntityDestroyed(i % 2 == 0 ? "fighter" : Reinforcement, Team.Enemy);
                Assert.AreEqual(0, t.RemainingKills(0), "never below 0");
                Assert.AreEqual(1, completions, "later kills raise nothing");

                Assert.AreEqual(0, t.RemainingKills(1), "0 for a non-destroyAll objective");
                Assert.AreEqual(0, t.RemainingKills(-1), "0 for a bad index");
                Assert.AreEqual(0, t.RemainingKills(2), "0 for a bad index");
            } finally {
                GameEvents.ObjectiveCompleted -= onDone;
            }
        }

        // ── HUD objective number (B053) ─────────────────────────────────

        [Test]
        public void Hud_UnderRunner_ShowsTrackerCount_AndMovesOnAReinforcementKill() {
            var data = new MissionData {
                spawns = new[] { Spawn("fighter", 4), Spawn("fighter", 2) },
                objectives = new[] { DestroyAll("fighter") }
            };
            var hud = MakeRunnerHud(data, out var tracker);

            // The `total` argument is only a fallback: the tracker's number wins.
            hud.SetObjective("fighter", 99);
            Assert.IsTrue(hud.ObjectiveFromTracker, "the HUD binds to the runner's tracker");
            Assert.AreEqual(6, hud.DisplayedObjectiveRemaining);
            Assert.AreEqual("Destroy 6 more fighters", ObjectiveText(hud));

            // Usual order: MissionRunner (tracker) first, then the HUD.
            tracker.OnEntityDestroyed(Reinforcement, Team.Enemy);
            GameEvents.RaiseEntityDestroyed(Reinforcement, Team.Enemy);
            Assert.AreEqual(5, hud.DisplayedObjectiveRemaining, "a reinforcement kill moves the counter");
            Assert.AreEqual("Destroy 5 more fighters", ObjectiveText(hud));
            AssertNoRawKey(hud);

            tracker.OnEntityDestroyed("fighter", Team.Enemy);
            GameEvents.RaiseEntityDestroyed("fighter", Team.Enemy);
            Assert.AreEqual(4, hud.DisplayedObjectiveRemaining, "a JSON fighter kill moves it too");

            // Reverse order: the HUD hears the kill before the tracker counts it. It shows the
            // old number for one frame and re-reads on the next Update.
            GameEvents.RaiseEntityDestroyed(Reinforcement, Team.Enemy);
            tracker.OnEntityDestroyed(Reinforcement, Team.Enemy);
            Call(hud, "Update");
            Assert.AreEqual(3, hud.DisplayedObjectiveRemaining, "re-read from the tracker on the next frame");
            Assert.AreEqual("Destroy 3 more fighters", ObjectiveText(hud));
            AssertNoRawKey(hud);
        }

        [Test]
        public void Hud_UnderRunner_IgnoresNonEnemyAndOtherTypeKills() {
            var data = new MissionData {
                spawns = new[] { Spawn("fighter", 3), Spawn("bomber", 1) },
                objectives = new[] { DestroyAll("fighter") }
            };
            var hud = MakeRunnerHud(data, out var tracker);
            hud.SetObjective("fighter", 3);
            Assert.AreEqual(3, hud.DisplayedObjectiveRemaining);

            void Kill(string type, Team team) {
                tracker.OnEntityDestroyed(type, team);
                GameEvents.RaiseEntityDestroyed(type, team);
                Call(hud, "Update");
            }
            Kill("fighter", Team.Player);
            Kill(Reinforcement, Team.Player);
            Kill("PlayerPlane", Team.Player);
            Kill("healthBalloon", Team.Neutral);
            Kill("bomber", Team.Enemy);
            Kill("aaa", Team.Enemy);
            Assert.AreEqual(3, hud.DisplayedObjectiveRemaining, "only Team.Enemy fighter kills move the counter");
            Assert.AreEqual("Destroy 3 more fighters", ObjectiveText(hud));
        }

        [Test]
        public void Hud_Standalone_FallbackCountsDown_EnemyFighterKillsOnly() {
            var hud = MakeHud(null);
            hud.SetObjective("fighter", 3);
            Assert.IsFalse(hud.ObjectiveFromTracker, "no runner: no tracker");
            Assert.AreEqual("Destroy 3 more fighters", ObjectiveText(hud));

            GameEvents.RaiseEntityDestroyed(Reinforcement, Team.Enemy);
            Assert.AreEqual(2, hud.DisplayedObjectiveRemaining, "a reinforcement counts in the fallback too");
            AssertNoRawKey(hud);

            GameEvents.RaiseEntityDestroyed("fighter", Team.Player);
            GameEvents.RaiseEntityDestroyed("bomber", Team.Enemy);
            Assert.AreEqual(2, hud.DisplayedObjectiveRemaining);

            GameEvents.RaiseEntityDestroyed("fighter", Team.Enemy);
            GameEvents.RaiseEntityDestroyed("fighter", Team.Enemy);
            GameEvents.RaiseEntityDestroyed("fighter", Team.Enemy);
            Assert.AreEqual(0, hud.DisplayedObjectiveRemaining, "never below 0");
            Assert.AreEqual("Destroy 0 more fighters", ObjectiveText(hud));
        }

        [Test]
        public void Hud_ObjectiveText_UsesReadableTypeNames() {
            var hud = MakeHud(null);
            hud.SetObjective("aaa", 2);
            Assert.AreEqual("Destroy 2 more AAA guns", ObjectiveText(hud));
            hud.SetObjective("barrageBalloon", 2);
            Assert.AreEqual("Destroy 2 more barrage balloons", ObjectiveText(hud));
        }

        // ── B052 death snap ─────────────────────────────────────────────

        private Health MakePlayerHealth() {
            var go = new GameObject("T963_Player");
            _created.Add(go);
            go.AddComponent<TeamTag>().team = Team.Player;
            var h = go.AddComponent<Health>();
            Call(h, "Awake");
            return h;
        }

        [Test]
        public void PlayerDeathSnap_NeverKillsOnANonLethalTotal() {
            var g = GameBalance.Get();
            Assume.That(g != null && g.damageTaken != null && g.player != null, "Balance.json not loadable");
            float res = PlayerData.ResistanceOrDefault(g.player);

            // One bullet short of death: 29 x 3.33 = 96.7%.
            float bullet = g.damageTaken.fighterBullet / res;
            int toKill = Mathf.CeilToInt(100f / bullet - 1e-3f);
            var a = MakePlayerHealth();
            for (int i = 0; i < toKill - 1; i++) a.ApplyDamage(1f, DamageSource.Bullet);
            Assert.Less(a.CurrentDamagePercent, 100f - 1f);
            Assert.IsFalse(a.IsDead, "one bullet short of 100% is alive");

            // Two contacts + bullets up to just under 100.
            var b = MakePlayerHealth();
            b.ApplyDamage(1f, DamageSource.PlaneCollision);
            b.ApplyDamage(1f, DamageSource.PlaneCollision);
            while (b.CurrentDamagePercent + bullet < 100f - 0.01f) b.ApplyDamage(1f, DamageSource.Bullet);
            Assert.IsFalse(b.IsDead, $"{b.CurrentDamagePercent}% is not dead");

            // A raw hit just outside the snap band leaves the player alive.
            var c = MakePlayerHealth();
            float multiplier = g.damageTaken.flak / res;   // percent per 1.0 of Flak input
            c.ApplyDamage(99.99f / multiplier, DamageSource.Flak);
            Assert.AreEqual(99.99f, c.CurrentDamagePercent, 1e-3f);
            Assert.IsFalse(c.IsDead, "99.99% is outside the 0.001% snap band");

            // A heal never snaps anything to 100.
            c.ApplyDamage(-0.5f, DamageSource.Heal);
            Assert.Less(c.CurrentDamagePercent, 100f);
            Assert.IsFalse(c.IsDead);
        }
    }
}
