using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Missions;
using Biplanes.Player;
using Biplanes.UI;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// T968 (QA): gaps left by BombBalloonTests for the recharge bomb balloon (B064, B065,
    /// A064, V064). Every mission places a bomb balloon in the middle third of the map; no
    /// objective, minimap enemy rule or tracker counter sees a bomb balloon; the sprite loads
    /// from Resources at the ART_STYLE size; the HUD bomb icons follow a refill from 0 to 4
    /// and then to 7; the auto spawn sits `lead` (12) ahead along the facing.
    /// </summary>
    public class BombBalloonQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        private static MissionData LoadMission(int index) {
            var text = Resources.Load<TextAsset>($"Missions/mission_{index:D2}");
            Assert.IsNotNull(text, $"mission_{index:D2}.json not found");
            var data = JsonUtility.FromJson<MissionData>(text.text);
            Assert.IsNotNull(data);
            Assert.IsNotNull(data.spawns);
            return data;
        }

        private static bool IsBombBalloon(SpawnEntry s) {
            return s != null && s.type != null && s.type.ToLowerInvariant() == "bombballoon";
        }

        // ── Mission data (V064) ───────────────────────────────────────────

        [Test]
        public void EveryMission_PlacesABombBalloon_InTheMiddleThird([Values(1, 2, 3, 4, 5)] int index) {
            var m = LoadMission(index);
            float third = m.length / 3f;
            int mid = 0;
            foreach (var s in m.spawns) {
                if (!IsBombBalloon(s)) continue;
                Assert.GreaterOrEqual(s.count, 1, "a bombBalloon entry spawns at least one balloon");
                Assert.GreaterOrEqual(s.y, 1.5f, "JSON y is above the plane floor (terrain + 1.5)");
                Assert.LessOrEqual(s.y, 6f, "JSON y stays inside the flight band");
                if (s.atX >= third && s.atX <= 2f * third) mid++;
            }
            Assert.GreaterOrEqual(mid, 1, $"mission {index}: no bombBalloon in [{third:0}, {2 * third:0}]");
        }

        [Test]
        public void NoObjective_TargetsABombBalloon([Values(1, 2, 3, 4, 5)] int index) {
            var m = LoadMission(index);
            if (m.objectives == null) return;
            foreach (var o in m.objectives) {
                if (o == null) continue;
                Assert.AreNotEqual("bombballoon", (o.targetType ?? "").ToLowerInvariant(),
                    $"mission {index}: objective {o.id} targets a pickup");
            }
        }

        // ── Objectives, counters, minimap ignore the pickup ───────────────

        [Test]
        public void CollectingABombBalloon_NeverAdvancesAnObjective([Values(1, 2, 3, 4, 5)] int index) {
            var m = LoadMission(index);
            var go = new GameObject("T968_Tracker");
            _created.Add(go);
            var tracker = go.AddComponent<ObjectiveTracker>();
            tracker.Init(m.objectives, 0f, m.spawns);

            int n = m.objectives != null ? m.objectives.Length : 0;
            var before = new int[n];
            for (int i = 0; i < n; i++) before[i] = tracker.RemainingKills(i);
            bool allBefore = tracker.AllComplete;

            tracker.OnEntityDestroyed(nameof(BombBalloon), Team.Neutral);
            tracker.OnEntityDestroyed(nameof(BombBalloon), Team.Neutral);

            for (int i = 0; i < n; i++)
                Assert.AreEqual(before[i], tracker.RemainingKills(i), $"objective {i} counter moved");
            Assert.AreEqual(allBefore, tracker.AllComplete);
        }

        [Test]
        public void BombBalloon_IsNotAKillOfAnyEnemyType_NorAMinimapEnemy() {
            foreach (var t in new[] { "fighter", "bomber", "zeppelin", "aaa", "barrageBalloon", "enemyBase" })
                Assert.IsFalse(ObjectiveTracker.CountsAsKillOf(t, nameof(BombBalloon)), t);
            Assert.IsFalse(MinimapView.IsEnemySpawnType("bombBalloon"), "pickups are not drawn as enemies");
        }

        // ── Sprite (A064) ─────────────────────────────────────────────────

        [Test]
        public void Sprite_LoadsFromResources_AtArtStyleSize() {
            var sprite = Resources.Load<Sprite>(BombBalloon.SpritePath);
            Assert.IsNotNull(sprite, $"Resources.Load<Sprite>(\"{BombBalloon.SpritePath}\") is null: "
                + "PNG missing or not imported as a single Sprite");
            Assert.AreEqual(70f, sprite.rect.width, 0.5f, "ART_STYLE: 70x100");
            Assert.AreEqual(100f, sprite.rect.height, 0.5f, "ART_STYLE: 70x100");
        }

        // ── Spawn factory ─────────────────────────────────────────────────

        [Test]
        public void Spawn_IsANeutralTrigger_AndKeepsTheAutoFlag() {
            var auto = BombBalloon.Spawn(new Vector3(3f, 4f, 0f), true);
            _created.Add(auto.gameObject);
            var placed = BombBalloon.Spawn(new Vector3(0f, 4f, 0f));
            _created.Add(placed.gameObject);

            Assert.IsTrue(auto.IsAutoSpawned);
            Assert.IsFalse(placed.IsAutoSpawned);
            Assert.AreEqual(Team.Neutral, auto.Team);
            var col = auto.GetComponent<BoxCollider2D>();
            Assert.IsNotNull(col);
            Assert.IsTrue(col.isTrigger, "the player flies through it");
            Assert.AreEqual(new Vector3(3f, 4f, 0f), auto.transform.position);
        }

        [Test]
        public void AutoSpawn_UsesBalanceLead_AheadAlongFacing() {
            var data = BombBalloonData.Resolve(GameBalance.Get());
            Assert.AreEqual(12f, data.lead, 1e-4f, "Balance.json bombBalloonLead");
            Assert.AreEqual(4, data.grant);
            Assert.AreEqual(4f, data.delay, 1e-4f);
            Assert.AreEqual(112f, BombBalloon.AutoSpawnX(100f, 1, data.lead, 0f, 600f), 1e-4f);
            Assert.AreEqual(88f, BombBalloon.AutoSpawnX(100f, -1, data.lead, 0f, 600f), 1e-4f);
        }

        // ── HUD bomb icons follow a recharge ──────────────────────────────

        private static void Call(object target, string method) {
            var m = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, null);
        }

        [Test]
        public void Hud_ShowsFour_ThenSeven_AfterTwoBalloonsFromAnEmptyBay() {
            var canvasGo = new GameObject("T968_Canvas", typeof(RectTransform));
            _created.Add(canvasGo);
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var hud = canvasGo.AddComponent<HudView>();
            Call(hud, "Awake");
            try {
                var playerGo = new GameObject("T968_Player");
                _created.Add(playerGo);
                playerGo.AddComponent<TeamTag>().team = Team.Player;
                var bay = playerGo.AddComponent<BombBay>();
                typeof(BombBay).GetField("_capacity", Private).SetValue(bay, 7);
                typeof(BombBay).GetField("_remaining", Private).SetValue(bay, 0);

                GameEvents.RaiseBombsChanged(0);
                Assert.AreEqual(0, hud.DisplayedBombs);

                bay.AddBombs(BombBalloonData.Resolve(GameBalance.Get()).grant);
                Assert.AreEqual(4, hud.DisplayedBombs, "first balloon: 4 icons filled");

                bay.AddBombs(BombBalloonData.Resolve(GameBalance.Get()).grant);
                Assert.AreEqual(7, hud.DisplayedBombs, "second balloon: capped at 7");
                Assert.AreEqual(7, bay.BombsLeft);
            } finally {
                Call(hud, "OnDestroy");
            }
        }
    }
}
