using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Biplanes.Core;
using Biplanes.Missions;
using Biplanes.UI;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// B044: the HUD minimap (UI_SPEC §3.3.1). Pure mapping helpers (world x/y to strip px,
    /// clamping, facing to triangle rotation, frame width clear of the corner widgets),
    /// objective-target rules, the white-only / no-raycast build, and a runner-driven check
    /// that a spawn moves its marker from "upcoming" to "live" and that a completed objective
    /// loses its diamond.
    ///
    /// EditMode doesn't run Awake/Update on AddComponent, so HudView.Awake and
    /// MissionRunner.Update are invoked by reflection, the way Play mode would run them.
    /// </summary>
    public class MinimapTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go == null) continue;
                var hud = go.GetComponent<HudView>();
                if (hud != null) Call(hud, "OnDestroy");   // unsubscribes the static events
                Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        private static void Call(object target, string method) {
            var m = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, null);
        }

        private HudView MakeHud() {
            var go = new GameObject("B044_Canvas", typeof(RectTransform));
            _created.Add(go);
            go.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var hud = go.AddComponent<HudView>();
            Call(hud, "Awake");
            return hud;
        }

        // ── Mapping helpers ───────────────────────────────────────────────

        [Test]
        public void MapX_SpansZeroToLength_AndClamps() {
            Assert.AreEqual(0f, MinimapView.MapX(0f, 300f, 474f), 1e-4f);
            Assert.AreEqual(237f, MinimapView.MapX(150f, 300f, 474f), 1e-4f);
            Assert.AreEqual(474f, MinimapView.MapX(300f, 300f, 474f), 1e-4f);
            Assert.AreEqual(0f, MinimapView.MapX(-12f, 300f, 474f), 1e-4f, "left of the start clamps to 0");
            Assert.AreEqual(474f, MinimapView.MapX(420f, 300f, 474f), 1e-4f, "past the end clamps to the width");
            Assert.AreEqual(0f, MinimapView.MapX(50f, 0f, 474f), "no length: no mapping");
            Assert.AreEqual(0f, MinimapView.MapX(50f, 300f, 0f), "no width: no mapping");
        }

        [Test]
        public void MapY_SpansTheFlightBand_AndClamps() {
            // Band: ground -4.5 to ceiling 5.
            Assert.AreEqual(0f, MinimapView.MapY(-4.5f, -4.5f, 5f, 38f), 1e-4f);
            Assert.AreEqual(38f, MinimapView.MapY(5f, -4.5f, 5f, 38f), 1e-4f);
            Assert.AreEqual(19f, MinimapView.MapY(0.25f, -4.5f, 5f, 38f), 1e-4f);
            Assert.AreEqual(0f, MinimapView.MapY(-9f, -4.5f, 5f, 38f), 1e-4f, "below the band clamps to 0");
            Assert.AreEqual(38f, MinimapView.MapY(12f, -4.5f, 5f, 38f), 1e-4f, "above the ceiling clamps to the top");
            Assert.AreEqual(0f, MinimapView.MapY(1f, 2f, 2f, 38f), "empty band: no mapping");
        }

        [Test]
        public void FacingAngle_RightIsZero_LeftIs180() {
            Assert.AreEqual(0f, MinimapView.FacingAngle(1));
            Assert.AreEqual(180f, MinimapView.FacingAngle(-1));
            Assert.AreEqual(0f, MinimapView.FacingAngle(0), "no facing yet: right, like the plane's default");
        }

        [Test]
        public void VisibleOnlyWhilePlayingOrPaused() {
            Assert.IsTrue(MinimapView.VisibleIn(GameManager.GameState.Playing));
            Assert.IsTrue(MinimapView.VisibleIn(GameManager.GameState.Paused));
            Assert.IsFalse(MinimapView.VisibleIn(GameManager.GameState.Menu));
            Assert.IsFalse(MinimapView.VisibleIn(GameManager.GameState.Briefing));
            Assert.IsFalse(MinimapView.VisibleIn(GameManager.GameState.Won));
            Assert.IsFalse(MinimapView.VisibleIn(GameManager.GameState.Lost));
        }

        // ── Layout: top-centre, clear of the corner widgets ───────────────

        [Test]
        public void FrameWidth_Is480_At1920_AndNeverOverlapsTheCornerWidgets() {
            var hud = MakeHud();
            float left = hud.LeftReserve, right = hud.RightReserve;
            // T961: was 232 (wrong sum); HudView.LeftReserve = 16 + 200 + 2*4 = 224.
            Assert.AreEqual(224f, left, 0.01f, "damage frame: 16 + 200 + 2*4");
            Assert.AreEqual(316f, right, 0.01f, "score/objective: 16 + 300");
            Assert.AreEqual(480f, MinimapView.FrameWidth(1920f, left, right), 0.01f, "16:9 reference");

            // 16:9, 16:10 and 4:3 canvases (constant-pixel CanvasScaler: canvas = screen px).
            float[] widths = { 1920f, 1728f, 1680f, 1440f, 1280f, 1024f, 800f };
            foreach (float w in widths) {
                float frame = MinimapView.FrameWidth(w, left, right);
                float frameLeft = w * 0.5f - frame * 0.5f;
                float frameRight = w * 0.5f + frame * 0.5f;
                Assert.LessOrEqual(frame, MinimapView.RefWidth);
                Assert.GreaterOrEqual(frameLeft, left, $"canvas {w}: overlaps the damage bar");
                Assert.LessOrEqual(frameRight, w - right, $"canvas {w}: overlaps the score/objective text");
            }
            Assert.AreEqual(MinimapView.RefWidth, MinimapView.FrameWidth(0f, left, right), "unknown canvas: reference width");
        }

        [Test]
        public void Hud_BuildsMinimap_TopCentre_44pxHigh_NoRaycasts_WhiteOnly() {
            var hud = MakeHud();
            Assert.IsNotNull(hud.Minimap, "HudView builds the minimap");
            var root = hud.Minimap.Root;
            Assert.AreEqual(new Vector2(0.5f, 1f), root.anchorMin);
            Assert.AreEqual(new Vector2(0.5f, 1f), root.anchorMax);
            Assert.AreEqual(new Vector2(0.5f, 1f), root.pivot);
            Assert.AreEqual(new Vector2(0f, -12f), root.anchoredPosition, "12 px from the top");
            Assert.AreEqual(MinimapView.FrameHeight, root.sizeDelta.y, 0.01f);
            Assert.AreEqual(44f, MinimapView.FrameHeight);

            Color[] palette = { HudView.InkCol, HudView.PencilCol, HudView.PaperCol };
            foreach (var img in root.GetComponentsInChildren<Image>(true)) {
                Assert.IsFalse(img.raycastTarget, img.name + " blocks clicks");
                Assert.IsNotNull(img.sprite, img.name + " has no sprite");
                bool ok = false;
                foreach (var p in palette) {
                    ok |= Mathf.Abs(img.color.r - p.r) < 0.002f && Mathf.Abs(img.color.g - p.g) < 0.002f
                        && Mathf.Abs(img.color.b - p.b) < 0.002f;
                }
                Assert.IsTrue(ok, img.name + " colour " + img.color + " is outside ink/pencil/paper");
            }
        }

        // ── Objective targets ─────────────────────────────────────────────

        private static ObjectiveEntry Obj(string type, string targetType, string targetId = "", float failAtX = -1f) {
            return new ObjectiveEntry { id = type, type = type, targetType = targetType, targetId = targetId, failAtX = failAtX };
        }

        [Test]
        public void IsObjectiveTarget_ById_BombTarget_AndDestroyAllLiveOnly() {
            var byId = new[] { Obj("destroyById", "bomber", "bomber_1") };
            Assert.IsTrue(MinimapView.IsObjectiveTarget(byId, "bomber", "bomber_1", true));
            Assert.IsTrue(MinimapView.IsObjectiveTarget(byId, "bomber", "bomber_1", false), "pending id target keeps its diamond");
            Assert.IsFalse(MinimapView.IsObjectiveTarget(byId, "bomber", "bomber_2", true));
            Assert.IsFalse(MinimapView.IsObjectiveTarget(byId, "bomber", "", true), "escort-less bomber without the id");

            var bomb = new[] { Obj("bombTarget", "enemyBase", "base_1") };
            Assert.IsTrue(MinimapView.IsObjectiveTarget(bomb, "enemyBase", "base_1", true));
            Assert.IsFalse(MinimapView.IsObjectiveTarget(bomb, "enemyBase", "base_2", true));
            var bombByType = new[] { Obj("bombTarget", "enemyBase") };
            Assert.IsTrue(MinimapView.IsObjectiveTarget(bombByType, "enemyBase", "", true), "empty targetId matches the type");

            var all = new[] { Obj("destroyAllOfType", "fighter") };
            Assert.IsTrue(MinimapView.IsObjectiveTarget(all, "fighter", "", true));
            Assert.IsTrue(MinimapView.IsObjectiveTarget(all, "Fighter", "", true), "case-insensitive like ObjectiveTracker");
            Assert.IsFalse(MinimapView.IsObjectiveTarget(all, "fighter", "", false), "destroyAll diamonds go on live enemies only");
            Assert.IsTrue(MinimapView.IsObjectiveTarget(all, ScoreKeeper.ReinforcementFighterKey, "", true),
                "B050/B053: reinforcements count for destroyAll fighter, so they get diamonds");
            Assert.IsFalse(MinimapView.IsObjectiveTarget(all, "bomber", "", true));

            all[0].evaluated = true;
            Assert.IsFalse(MinimapView.IsObjectiveTarget(all, "fighter", "", true), "completed objectives lose their diamonds");
            Assert.IsFalse(MinimapView.IsObjectiveTarget(null, "fighter", "", true), "mission over: no diamonds");
            Assert.IsFalse(MinimapView.IsObjectiveTarget(new[] { Obj("surviveToEnd", "") }, "fighter", "", true));
        }

        [Test]
        public void EnemySpawnTypes_ExcludeHealthBalloonsAndScenery() {
            foreach (var t in new[] { "fighter", "bomber", "zeppelin", "aaa", "barrageBalloon", "enemyBase" }) {
                Assert.IsTrue(MinimapView.IsEnemySpawnType(t), t);
            }
            foreach (var t in new[] { "healthBalloon", "building", "tree", "airfield", "playerHQ", "", null }) {
                Assert.IsFalse(MinimapView.IsEnemySpawnType(t), t ?? "null");
            }
        }

        // ── Runner-driven: upcoming -> live, diamonds, failAtX ────────────

        [Test]
        public void Spawning_MovesTheMarker_FromUpcomingToLive_AndCompletionDropsTheDiamond() {
            var runnerGo = new GameObject("B044_Runner");
            _created.Add(runnerGo);
            var runner = runnerGo.AddComponent<MissionRunner>();
            var tracker = runnerGo.AddComponent<ObjectiveTracker>();

            // An AAA gun far left of any camera trigger (MissionRunner.Update spawns ground
            // entries once FarthestX >= atX - look-ahead; without a ScrollController FarthestX
            // reads -999), a health balloon (never drawn), and a destroyAll "aaa" objective plus
            // a destroyById with a failAtX line.
            var killAaa = Obj("destroyAllOfType", "aaa");
            var killBomber = Obj("destroyById", "bomber", "bomber_1", 50f);
            var data = new MissionData {
                length = 300f,
                spawns = new[] {
                    new SpawnEntry { atX = -1000, type = "aaa", count = 1, pattern = "static", id = "" },
                    new SpawnEntry { atX = 5000, type = "healthBalloon", count = 1, pattern = "drift", id = "" },
                    new SpawnEntry { atX = 5000, type = "bomber", count = 1, pattern = "straight", id = "bomber_1" },
                },
                objectives = new[] { killAaa, killBomber }
            };
            tracker.Init(data.objectives, 0f, data.spawns);
            typeof(MissionRunner).GetField("_missionData", Private).SetValue(runner, data);
            typeof(MissionRunner).GetField("_objectiveTracker", Private).SetValue(runner, tracker);
            typeof(MissionRunner).GetField("_spawnedFlags", Private).SetValue(runner, new bool[data.spawns.Length]);
            Assert.AreEqual(300f, runner.MissionLength);
            Assert.AreEqual(3, runner.SpawnEntryCount);
            Assert.IsTrue(runner.IsSpawnPending(0));
            Assert.IsFalse(runner.IsSpawnPending(3), "out of range");

            var hud = MakeHud();
            var map = hud.Minimap;
            map.Bind(runner);

            Assert.AreEqual(2, map.UpcomingCount, "AAA and bomber ticks; the health balloon is never shown");
            Assert.AreEqual(0, map.LiveGroundCount);
            Assert.AreEqual(1, map.DiamondCount, "the pending destroyById bomber; destroyAll AAA has no live gun yet");
            Assert.Greater(map.LineMarkCount, 0, "dashed failAtX line");
            Assert.Greater(map.TerrainSegmentCount, 0, "terrain polyline");

            Call(runner, "Update");   // FarthestX -999 >= -1000 - look-ahead: the AAA spawns
            try {
                Assert.IsFalse(runner.IsSpawnPending(0), "precondition: AAA spawned");
                Assert.IsTrue(runner.IsSpawnPending(2), "precondition: bomber still pending");
                Assert.AreEqual(1, runner.GroundEnemies.Count);

                map.Refresh();
                Assert.AreEqual(1, map.UpcomingCount, "the spawned AAA's tick is gone");
                Assert.AreEqual(1, map.LiveGroundCount, "the AAA is now a live marker");
                Assert.AreEqual(2, map.DiamondCount, "live AAA (destroyAll) + pending bomber (destroyById)");

                killAaa.evaluated = true;
                killBomber.evaluated = true;
                int lines = map.LineMarkCount;
                map.Refresh();
                Assert.AreEqual(0, map.DiamondCount, "completed objectives lose their diamonds");
                Assert.AreEqual(0, map.LineMarkCount, "a completed destroyById drops its failAtX line");
                Assert.Greater(lines, 0);
                Assert.AreEqual(1, map.LiveGroundCount, "the gun itself stays");
                Assert.IsFalse(map.PlayerShown, "no player plane in this runner");
            } finally {
                for (int i = 0; i < runner.GroundEnemies.Count; i++) {
                    if (runner.GroundEnemies[i] != null) Object.DestroyImmediate(runner.GroundEnemies[i].gameObject);
                }
            }

            map.Refresh();
            Assert.AreEqual(0, map.LiveGroundCount, "a destroyed gun leaves the map");
        }
    }
}
