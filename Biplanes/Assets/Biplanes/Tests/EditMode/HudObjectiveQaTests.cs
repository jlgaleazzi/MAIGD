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
    /// T964 (QA for B054): the HUD objective text shows one objective at a time, reacts
    /// only to that objective's ObjectiveCompleted, moves on to the next open objective
    /// (counter for destroyAllOfType, else ObjectiveTracker.GetDescription) and shows
    /// "Objective Complete!" only when none remain (UI_SPEC §3.3, Objective text row).
    ///
    /// The data is mission-4-shaped: destroyAllOfType barrageBalloon "pop_all_balloons" x3
    /// plus bombTarget enemyBase "bomb_base" (targetId enemy_base_01).
    ///
    /// EditMode doesn't run Awake/OnDestroy on AddComponent, so HudView's are invoked by
    /// reflection (as in FighterCounterQaTests). The 2 s hide coroutine is never ticked in
    /// EditMode, so the text checked right after a completion is stable.
    /// </summary>
    public class HudObjectiveQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Complete = "Objective Complete!";
        private const string BalloonsLeft3 = "Destroy 3 more barrage balloons";
        private const string BombBase = "Bomb the enemy base";

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

        private static MissionData M4Data() {
            return new MissionData {
                spawns = new[] {
                    new SpawnEntry { type = "barrageBalloon", count = 2 },
                    new SpawnEntry { type = "barrageBalloon", count = 1 },
                    new SpawnEntry { type = "enemyBase", count = 1, id = "enemy_base_01" },
                    new SpawnEntry { type = "healthBalloon", count = 1 }
                },
                objectives = new[] {
                    new ObjectiveEntry { id = "pop_all_balloons", type = "destroyAllOfType",
                        targetType = "barrageBalloon", targetId = "", failAtX = -1 },
                    new ObjectiveEntry { id = "bomb_base", type = "bombTarget",
                        targetType = "enemyBase", targetId = "enemy_base_01", failAtX = -1 }
                }
            };
        }

        private HudView MakeHud(Transform parent) {
            var canvasGo = new GameObject("T964_Canvas", typeof(RectTransform));
            if (parent != null) canvasGo.transform.SetParent(parent, false);
            else _created.Add(canvasGo);
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var hud = canvasGo.AddComponent<HudView>();
            Call(hud, "Awake");
            _huds.Add(hud);
            return hud;
        }

        /// <summary>A MissionRunner (not started) holding `data`, with an initialised tracker
        /// and a HUD canvas under it, bound the way StartMission binds it.</summary>
        private HudView MakeRunnerHud(MissionData data, out ObjectiveTracker tracker) {
            var go = new GameObject("T964_Runner");
            _created.Add(go);
            var runner = go.AddComponent<MissionRunner>();
            var field = typeof(MissionRunner).GetField("_missionData", Private);
            Assert.IsNotNull(field, "MissionRunner._missionData not found");
            field.SetValue(runner, data);
            tracker = go.AddComponent<ObjectiveTracker>();
            tracker.Init(data.objectives, 0f, data.spawns);
            var hud = MakeHud(go.transform);
            hud.SetObjective("barrageBalloon", 3);
            return hud;
        }

        private static Text ObjectiveTextComponent(HudView hud) {
            var f = typeof(HudView).GetField("_objectiveText", Private);
            Assert.IsNotNull(f, "HudView._objectiveText not found");
            var text = f.GetValue(hud) as Text;
            Assert.IsNotNull(text, "the HUD built no objective text");
            return text;
        }

        private static string ObjectiveText(HudView hud) => ObjectiveTextComponent(hud).text;

        /// <summary>A balloon kill in the runtime order: tracker (MissionRunner) first, then
        /// the HUD hears EntityDestroyed, then a frame passes.</summary>
        private static void PopBalloon(ObjectiveTracker tracker, HudView hud) {
            tracker.OnEntityDestroyed("barrageBalloon", Team.Enemy);
            GameEvents.RaiseEntityDestroyed("barrageBalloon", Team.Enemy);
            Call(hud, "Update");
        }

        private static void BombBaseNow(ObjectiveTracker tracker, HudView hud) {
            Assert.IsTrue(tracker.OnBombTargetDestroyed("enemy_base_01", "enemyBase"), "bomb_base completes");
            GameEvents.RaiseEntityDestroyed("enemyBase", Team.Enemy);
            Call(hud, "Update");
        }

        // ── Base first ──────────────────────────────────────────────────

        [Test]
        public void M4_BaseFirst_KeepsBalloonCounter_ThenCompletesOnLastBalloon() {
            var hud = MakeRunnerHud(M4Data(), out var tracker);
            Assert.IsTrue(hud.ObjectiveFromTracker, "bound to the balloon objective");
            Assert.AreEqual(BalloonsLeft3, ObjectiveText(hud));

            BombBaseNow(tracker, hud);
            Assert.AreEqual(BalloonsLeft3, ObjectiveText(hud),
                "bomb_base completing must not replace the balloon counter (B054)");
            Assert.AreEqual(3, hud.DisplayedObjectiveRemaining);

            PopBalloon(tracker, hud);
            Assert.AreEqual("Destroy 2 more barrage balloons", ObjectiveText(hud));
            PopBalloon(tracker, hud);
            Assert.AreEqual("Destroy 1 more barrage balloons", ObjectiveText(hud));
            Assert.IsFalse(tracker.AllComplete);

            PopBalloon(tracker, hud);
            Assert.IsTrue(tracker.AllComplete, "both objectives done");
            Assert.AreEqual(Complete, ObjectiveText(hud));

            // Late kill events after completion don't overwrite the message.
            GameEvents.RaiseEntityDestroyed("barrageBalloon", Team.Enemy);
            Call(hud, "Update");
            Assert.AreEqual(Complete, ObjectiveText(hud));
        }

        // ── Balloons first ──────────────────────────────────────────────

        [Test]
        public void M4_BalloonsFirst_ShowsBombTheBase_IgnoresLaterBalloons_ThenCompletes() {
            var hud = MakeRunnerHud(M4Data(), out var tracker);
            Assert.AreEqual(BalloonsLeft3, ObjectiveText(hud));

            PopBalloon(tracker, hud);
            PopBalloon(tracker, hud);
            Assert.AreEqual("Destroy 1 more barrage balloons", ObjectiveText(hud));

            // The last balloon: the tracker completes pop_all_balloons (HUD moves on) and the
            // HUD then hears the EntityDestroyed of that same kill.
            PopBalloon(tracker, hud);
            Assert.AreEqual(BombBase, ObjectiveText(hud), "moves on to the open bombTarget");
            Assert.IsFalse(hud.ObjectiveFromTracker, "a bombTarget has no tracker counter");
            Assert.AreNotEqual(Complete, ObjectiveText(hud));

            // Later balloon events (stray kills, a duplicate event) don't change it.
            for (int i = 0; i < 3; i++) {
                tracker.OnEntityDestroyed("barrageBalloon", Team.Enemy);
                GameEvents.RaiseEntityDestroyed("barrageBalloon", Team.Enemy);
                Call(hud, "Update");
                Assert.AreEqual(BombBase, ObjectiveText(hud), $"stray balloon event {i}");
            }
            // A repeat of the already-handled completion payload doesn't end it either.
            GameEvents.RaiseObjectiveCompleted("pop_all_balloons");
            Assert.AreEqual(BombBase, ObjectiveText(hud), "a stale pop_all_balloons payload");

            BombBaseNow(tracker, hud);
            Assert.IsTrue(tracker.AllComplete);
            Assert.AreEqual(Complete, ObjectiveText(hud));
        }

        // ── Reverse event order on the last balloon ─────────────────────

        [Test]
        public void M4_BalloonsFirst_HudHearsKillBeforeTracker_StillMovesOn() {
            var hud = MakeRunnerHud(M4Data(), out var tracker);
            PopBalloon(tracker, hud);
            PopBalloon(tracker, hud);

            GameEvents.RaiseEntityDestroyed("barrageBalloon", Team.Enemy);   // HUD first
            tracker.OnEntityDestroyed("barrageBalloon", Team.Enemy);         // then completion
            Call(hud, "Update");
            Assert.AreEqual(BombBase, ObjectiveText(hud), "the pending refresh must not bring the counter back");
        }

        // ── Standalone HUD ──────────────────────────────────────────────

        [Test]
        public void Standalone_AnyCompletion_ShowsObjectiveComplete() {
            var hud = MakeHud(null);
            hud.SetObjective("fighter", 3);
            Assert.IsFalse(hud.ObjectiveFromTracker, "no runner: unbound");
            Assert.AreEqual("Destroy 3 more fighters", ObjectiveText(hud));

            GameEvents.RaiseObjectiveCompleted("x");
            Assert.AreEqual(Complete, ObjectiveText(hud));
        }

        [Test]
        public void SetObjective_AfterComplete_ShowsTheNewObjectiveAgain() {
            var hud = MakeHud(null);
            hud.SetObjective("fighter", 1);
            GameEvents.RaiseObjectiveCompleted("x");
            Assert.AreEqual(Complete, ObjectiveText(hud));

            hud.SetObjective("aaa", 2);   // the next mission
            Assert.AreEqual("Destroy 2 more AAA guns", ObjectiveText(hud));
            Assert.IsTrue(ObjectiveTextComponent(hud).gameObject.activeSelf);
        }
    }
}
