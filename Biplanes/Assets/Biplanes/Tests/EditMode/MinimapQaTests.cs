using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Missions;
using Biplanes.UI;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// T961 (QA of B044, UI_SPEC §3.3.1): extra minimap checks beyond MinimapTests.
    /// - Layout measured from the real HUD widgets (not from the reserves), at 1920, 1280, 1024.
    /// - Live aircraft markers: fighters, right-flying fighters, reinforcements, bombers;
    ///   destroyAllOfType diamonds skip reinforcements; null-safe when an entity dies.
    /// - A mission that ended drops every diamond and failAtX line.
    /// - HQ flag from the JSON; health balloons never drawn.
    /// - Pooling: repeated refreshes create no new GameObjects.
    /// - MissionRunner teardown clears GroundEnemies.
    /// </summary>
    public class MinimapQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go == null) continue;
                var hud = go.GetComponent<HudView>();
                if (hud != null) Call(hud, "OnDestroy");
                Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        private static void Call(object target, string method) {
            var m = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, null);
        }

        private T Track<T>(T c) where T : Component {
            _created.Add(c.gameObject);
            return c;
        }

        private HudView MakeHud() {
            var go = new GameObject("T961_Canvas", typeof(RectTransform));
            _created.Add(go);
            go.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var hud = go.AddComponent<HudView>();
            Call(hud, "Awake");
            return hud;
        }

        private static ObjectiveEntry Obj(string type, string targetType, string targetId = "", float failAtX = -1f) {
            return new ObjectiveEntry { id = type, type = type, targetType = targetType, targetId = targetId, failAtX = failAtX };
        }

        private MissionRunner MakeRunner(MissionData data) {
            var go = new GameObject("T961_Runner");
            _created.Add(go);
            var runner = go.AddComponent<MissionRunner>();
            var tracker = go.AddComponent<ObjectiveTracker>();
            tracker.Init(data.objectives, 0f, data.spawns);
            typeof(MissionRunner).GetField("_missionData", Private).SetValue(runner, data);
            typeof(MissionRunner).GetField("_objectiveTracker", Private).SetValue(runner, tracker);
            typeof(MissionRunner).GetField("_spawnedFlags", Private).SetValue(runner, new bool[data.spawns.Length]);
            return runner;
        }

        private static List<MonoBehaviour> Aircraft(MissionRunner runner) {
            return (List<MonoBehaviour>)typeof(MissionRunner).GetField("_enemyAircraft", Private).GetValue(runner);
        }

        // ── Layout from the real widgets ──────────────────────────────────

        private static float LeftEdgeReach(Transform canvas, string name) {
            var r = (RectTransform)canvas.Find(name);
            Assert.IsNotNull(r, name + " missing");
            Assert.AreEqual(new Vector2(0f, 1f), r.anchorMin, name + " is not top-left anchored");
            return r.anchoredPosition.x + r.sizeDelta.x;
        }

        private static float RightEdgeReach(Transform canvas, string name) {
            var r = (RectTransform)canvas.Find(name);
            Assert.IsNotNull(r, name + " missing");
            Assert.AreEqual(new Vector2(1f, 1f), r.anchorMin, name + " is not top-right anchored");
            return -r.anchoredPosition.x + r.sizeDelta.x;
        }

        [Test]
        public void Layout_FrameClearsDamageBarLabelScoreAndObjective_At1920_1280_1024() {
            var hud = MakeHud();
            var canvas = hud.transform;
            float left = Mathf.Max(LeftEdgeReach(canvas, "DamageFrame"), LeftEdgeReach(canvas, "SmokeWarning"));
            float right = Mathf.Max(RightEdgeReach(canvas, "ScoreDisplay"), RightEdgeReach(canvas, "ObjectiveText"));
            Assert.LessOrEqual(left, hud.LeftReserve + 0.01f, "LeftReserve under-reports the damage widgets");
            Assert.LessOrEqual(right, hud.RightReserve + 0.01f, "RightReserve under-reports the score/objective column");

            foreach (float w in new[] { 1920f, 1280f, 1024f }) {
                float frame = MinimapView.FrameWidth(w, hud.LeftReserve, hud.RightReserve);
                float frameLeft = w * 0.5f - frame * 0.5f;
                float frameRight = w * 0.5f + frame * 0.5f;
                Assert.GreaterOrEqual(frameLeft - left, MinimapView.SideGap - 0.01f, $"{w}: too close to DAMAGE bar/label");
                Assert.GreaterOrEqual((w - right) - frameRight, MinimapView.SideGap - 0.01f, $"{w}: too close to score/objective");
            }
            Assert.AreEqual(480f, MinimapView.FrameWidth(1920f, hud.LeftReserve, hud.RightReserve), 0.01f);
            Assert.AreEqual(480f, MinimapView.FrameWidth(1280f, hud.LeftReserve, hud.RightReserve), 0.01f);
            Assert.AreEqual(368f, MinimapView.FrameWidth(1024f, hud.LeftReserve, hud.RightReserve), 0.01f);
        }

        // ── Live aircraft, reinforcements, death mid-refresh, mission end ──

        [Test]
        public void LiveAircraft_AllTypesDrawn_ReinforcementsGetDiamonds_NullSafe_EndClearsDiamonds() {
            var data = new MissionData {
                length = 300f,
                spawns = new[] { new SpawnEntry { atX = 5000, type = "bomber", count = 1, pattern = "straight", id = "bomber_1" } },
                objectives = new[] { Obj("destroyAllOfType", "fighter"), Obj("destroyById", "bomber", "bomber_9", 20f) }
            };
            var runner = MakeRunner(data);
            var list = Aircraft(runner);

            var leftFighter = Track(FighterAI.Spawn(new Vector3(50f, 1f, 0f)));
            var rightFighter = Track(FighterAI.Spawn(new Vector3(60f, 1f, 0f)));
            rightFighter.SetFlightDirection(1);
            var reinforcement = Track(FighterAI.Spawn(new Vector3(70f, 1f, 0f)));
            reinforcement.gameObject.name = ScoreKeeper.ReinforcementFighterKey;
            var otherBomber = Track(BomberAI.Spawn(new Vector3(80f, 1f, 0f), "bomber_x"));
            list.Add(leftFighter);
            list.Add(rightFighter);
            list.Add(reinforcement);
            list.Add(otherBomber);

            var map = MakeHud().Minimap;
            map.Bind(runner);
            Assert.AreEqual(4, map.LiveAircraftCount, "two fighters, one reinforcement, one bomber");
            // B050/B053: reinforcements count toward destroyAll fighter, so they get a diamond too.
            Assert.AreEqual(3, map.DiamondCount, "destroyAll fighter: two JSON fighters and the reinforcement, not bomber_x");
            Assert.Greater(map.LineMarkCount, 0, "bomber_9 failAtX dashed line");

            Object.DestroyImmediate(leftFighter.gameObject);   // dies between refreshes
            Assert.DoesNotThrow(() => map.Refresh());
            Assert.AreEqual(3, map.LiveAircraftCount);
            Assert.AreEqual(2, map.DiamondCount, "the right fighter and the reinforcement");

            rightFighter.gameObject.SetActive(false);           // torn down (DestroyNow deactivates first)
            map.Refresh();
            Assert.AreEqual(2, map.LiveAircraftCount);
            Assert.AreEqual(1, map.DiamondCount, "only the reinforcement is left");

            typeof(MissionRunner).GetField("_missionEnded", Private).SetValue(runner, true);
            map.Refresh();
            Assert.AreEqual(0, map.DiamondCount, "an ended mission shows no diamonds");
            Assert.AreEqual(0, map.LineMarkCount, "an ended mission shows no failAtX line");
        }

        // ── HQ flag, health balloons ──────────────────────────────────────

        [Test]
        public void HqFlag_FromJson_HealthBalloonsNeverShown() {
            var data = new MissionData {
                length = 540f,
                spawns = new[] {
                    new SpawnEntry { atX = 360, type = "playerHQ", count = 1, pattern = "static", id = "hq_01" },
                    new SpawnEntry { atX = 100, type = "healthBalloon", count = 2, pattern = "drift", id = "" },
                },
                objectives = new ObjectiveEntry[0]
            };
            var runner = MakeRunner(data);
            var map = MakeHud().Minimap;
            map.Bind(runner);
            Assert.AreEqual(1, map.LineMarkCount, "one HQ flag, before the HQ spawns");
            Assert.AreEqual(0, map.UpcomingCount, "health balloons and the HQ are not upcoming enemies");
            Assert.AreEqual(0, map.DiamondCount);
        }

        // ── Pooling ───────────────────────────────────────────────────────

        [Test]
        public void RepeatedRefresh_CreatesNoNewGameObjects() {
            var data = new MissionData {
                length = 450f,
                spawns = new[] {
                    new SpawnEntry { atX = 80, type = "bomber", count = 1, pattern = "straight", id = "bomber_1" },
                    new SpawnEntry { atX = 200, type = "fighter", count = 3, pattern = "straight", id = "" },
                },
                objectives = new[] { Obj("destroyById", "bomber", "bomber_1", 10f) }
            };
            var runner = MakeRunner(data);
            Aircraft(runner).Add(Track(FighterAI.Spawn(new Vector3(30f, 1f, 0f))));
            var map = MakeHud().Minimap;
            map.Bind(runner);
            map.Refresh();
            int before = map.Root.GetComponentsInChildren<Transform>(true).Length;
            for (int i = 0; i < 20; i++) map.Refresh();
            Assert.AreEqual(before, map.Root.GetComponentsInChildren<Transform>(true).Length,
                "markers are pooled: a steady refresh adds no GameObjects");
            foreach (var img in map.Root.GetComponentsInChildren<Image>(true)) {
                Assert.IsFalse(img.raycastTarget, img.name + " blocks clicks");
            }
        }

        // ── Runner teardown ───────────────────────────────────────────────

        [Test]
        public void Teardown_ClearsGroundEnemies() {
            var data = new MissionData {
                length = 300f,
                spawns = new[] { new SpawnEntry { atX = -1000, type = "aaa", count = 1, pattern = "static", id = "" } },
                objectives = new[] { Obj("destroyAllOfType", "aaa") }
            };
            var runner = MakeRunner(data);
            Call(runner, "Update");
            try {
                Assert.AreEqual(1, runner.GroundEnemies.Count, "precondition: AAA registered at spawn");
            } finally {
                Call(runner, "TearDownPreviousMission");
            }
            Assert.AreEqual(0, runner.GroundEnemies.Count, "teardown (Retry/Next) clears the minimap list");
            Assert.AreEqual(0, runner.EnemyAircraft.Count);
        }
    }
}
