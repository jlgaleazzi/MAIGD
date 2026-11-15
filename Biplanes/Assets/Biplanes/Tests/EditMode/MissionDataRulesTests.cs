using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Missions;
using Object = UnityEngine.Object;

namespace Biplanes.Tests {

    /// <summary>
    /// T938: data rules the 5 mission JSONs must satisfy for the B017/B018 end rules to give a
    /// fair, finishable mission (GDD change log 2026-11-13, ARCHITECTURE §5):
    ///  - Bomb-only required targets fit the 7-bomb bay (M3 has at most 5 AAA).
    ///  - A failAtX target spawns right of its failAtX at every supported aspect, and M5's HQ
    ///    sits at failAtX.
    ///  - Every required target spawns before the map end.
    ///  - Escort targets exist before their escorts start, and FighterAI's escortOf name lookup
    ///    (first TeamTag whose GameObject name contains the id) can only find the target.
    ///  - The M5 zeppelin flies high enough over the terrain for its underside turret, which
    ///    must die before the hull can be hurt, to be shot at.
    /// Geometry mirrors MissionRunner: planes trigger at camera x ≥ atX and appear
    /// orthographicSize*aspect + PlaneEntryMarginX right of the camera; ground objects sit at
    /// atX and trigger at atX - (orthographicSize*aspect + GroundEntryMarginX); zeppelins sit at
    /// atX and trigger at atX - (orthographicSize*aspect + ZeppelinAI.HalfLength + ZeppelinEntryMarginX).
    /// </summary>
    public class MissionDataRulesTests {

        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        /// <summary>Camera.orthographicSize set by ProjectBuilder for the Game scene.</summary>
        private const float CameraHalfHeight = 5f;

        /// <summary>Supported aspects: WebGL default canvas 960x600 and desktop 1920x1080.</summary>
        private static readonly float[] Aspects = { 960f / 600f, 1920f / 1080f };

        /// <summary>Height above the ground the player can fly level at (MissionRunner's
        /// MinPlaneHeight for enemy planes and the airborne-start minimum are both 1.5).</summary>
        private const float MinShootableHeight = 1.5f;

        private readonly List<GameObject> _created = new List<GameObject>();
        private TerrainStrip _previousTerrain;

        [SetUp]
        public void SetUp() {
            _previousTerrain = TerrainStrip.Instance;
        }

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
            var setter = typeof(TerrainStrip).GetProperty("Instance").GetSetMethod(true);
            setter.Invoke(null, new object[] { _previousTerrain });
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static MissionData LoadMission(int index) {
            var text = Resources.Load<TextAsset>($"Missions/mission_{index:D2}");
            Assert.IsNotNull(text, $"mission_{index:D2}.json not found in Resources/Missions");
            var data = JsonUtility.FromJson<MissionData>(text.text);
            Assert.IsNotNull(data);
            Assert.IsNotNull(data.spawns);
            Assert.IsNotNull(data.objectives);
            return data;
        }

        private static float RunnerConst(string name) {
            var f = typeof(MissionRunner).GetField(name, PrivateStatic);
            Assert.IsNotNull(f, $"MissionRunner.{name} not found by reflection");
            return (float)f.GetValue(null);
        }

        private static bool IsPlane(SpawnEntry s) {
            string t = s.type.ToLowerInvariant();
            return t == "fighter" || t == "bomber";
        }

        /// <summary>Camera x at which MissionRunner.Update spawns this entry.</summary>
        private static float TriggerX(SpawnEntry s, float aspect) {
            if (IsPlane(s)) return s.atX;
            if (s.type.ToLowerInvariant() == "zeppelin") {
                // B021: the whole hull starts off screen (half screen + half hull + margin).
                return s.atX - (CameraHalfHeight * aspect + ZeppelinAI.HalfLength + RunnerConst("ZeppelinEntryMarginX"));
            }
            return s.atX - (CameraHalfHeight * aspect + RunnerConst("GroundEntryMarginX"));
        }

        /// <summary>Lowest world x this entry's first entity can appear at.</summary>
        private static float MinSpawnX(SpawnEntry s, float aspect) {
            if (IsPlane(s)) return s.atX + CameraHalfHeight * aspect + RunnerConst("PlaneEntryMarginX");
            return s.atX;
        }

        private static SpawnEntry FindById(MissionData data, string id) {
            foreach (var s in data.spawns) {
                if (s != null && !string.IsNullOrEmpty(s.id) && s.id == id) return s;
            }
            return null;
        }

        private TerrainStrip MakeTerrain(MissionData data) {
            var strip = TerrainStrip.Create(data.terrain, data.length);
            _created.Add(strip.gameObject);
            return strip;
        }

        // ── Bomb budget (V002) ──────────────────────────────────────────

        [Test]
        public void BombOnlyRequiredTargets_FitTheBombBay_AllMissions() {
            var balance = GameBalance.Get();
            Assert.IsNotNull(balance);
            int bombs = balance.player.bombCapacity;
            Assert.AreEqual(7, bombs, "GDD: 7 bombs, full each mission, no refill");

            for (int m = 1; m <= 5; m++) {
                var data = LoadMission(m);
                int bombOnly = 0;
                foreach (var o in data.objectives) {
                    if (o.type == "bombTarget") bombOnly++;
                    if (o.type == "destroyAllOfType" && string.Equals(o.targetType, "aaa", System.StringComparison.OrdinalIgnoreCase)) {
                        foreach (var s in data.spawns) {
                            if (string.Equals(s.type, "aaa", System.StringComparison.OrdinalIgnoreCase)) bombOnly += s.count;
                        }
                    }
                }
                Assert.LessOrEqual(bombOnly, bombs, $"mission {m}: {bombOnly} bomb-only required targets for {bombs} bombs");
            }
        }

        [Test]
        public void M3_HasAtMostFiveAaa_LeavingSpareBombs() {
            var data = LoadMission(3);
            int aaa = 0;
            foreach (var s in data.spawns) {
                if (string.Equals(s.type, "aaa", System.StringComparison.OrdinalIgnoreCase)) aaa += s.count;
            }
            Assert.LessOrEqual(aaa, 5, "GDD change log 2026-11-13: mission 3 asks for at most 5 AAA");
            Assert.Greater(aaa, 0);
        }

        // ── failAtX geometry (B018 / V003) ──────────────────────────────

        [Test]
        public void FailAtX_TargetSpawnsRightOfFailAtX_AtEverySupportedAspect() {
            int checkedTargets = 0;
            for (int m = 1; m <= 5; m++) {
                var data = LoadMission(m);
                foreach (var o in data.objectives) {
                    if (o.type != "destroyById" || o.failAtX < 0f) continue;
                    var target = FindById(data, o.targetId);
                    Assert.IsNotNull(target, $"mission {m}: failAtX target '{o.targetId}' is never spawned");
                    foreach (float aspect in Aspects) {
                        Assert.Greater(MinSpawnX(target, aspect), o.failAtX,
                            $"mission {m}: '{o.targetId}' would spawn at or past its failAtX {o.failAtX} (aspect {aspect:0.00}); " +
                            "MissionRunner would ignore the target and only the map end could end the mission");
                    }
                    Assert.GreaterOrEqual(o.failAtX, 0f);
                    checkedTargets++;
                }
            }
            Assert.AreEqual(2, checkedTargets, "missions 2 and 5 each have one failAtX target");
        }

        [Test]
        public void M5_PlayerHQ_SitsAtTheZeppelinFailAtX() {
            var data = LoadMission(5);
            float failAtX = data.objectives[0].failAtX;
            SpawnEntry hq = null;
            foreach (var s in data.spawns) {
                if (string.Equals(s.type, "playerHQ", System.StringComparison.OrdinalIgnoreCase)) hq = s;
            }
            Assert.IsNotNull(hq, "mission 5 spawns the player's HQ");
            Assert.AreEqual(failAtX, (float)hq.atX, "GDD: the zeppelin is lost when it reaches the HQ");
            var zep = FindById(data, data.objectives[0].targetId);
            Assert.IsNotNull(zep);
            Assert.Greater(zep.atX, hq.atX, "the zeppelin flies left toward the HQ");
            Assert.Less(zep.atX, data.length, "the zeppelin appears before the map end");
        }

        /// <summary>T939 (V004): a player who flies right the whole time must still see the
        /// thematic loss (zeppelin reaches the HQ) before the camera reaches the map end, and
        /// the escorts must start after the hull exists, at every aspect a desktop window or
        /// WebGL canvas can have (4:3 up to 21:9).</summary>
        [Test]
        public void M5_ZeppelinReachesTheHQ_BeforeTheCameraCanReachTheMapEnd() {
            var data = LoadMission(5);
            var o = data.objectives[0];
            var zep = FindById(data, o.targetId);
            Assert.IsNotNull(zep);
            var balance = GameBalance.Get();
            Assert.IsNotNull(balance);
            Assert.IsNotNull(balance.enemySpeed);
            float speed = balance.enemySpeed.zeppelin > 0f ? balance.enemySpeed.zeppelin : 1f;
            // ZeppelinAI.FixedUpdate moves x only, at enemySpeed.zeppelin; ReachedTarget is x <= targetX.
            float hqSeconds = (zep.atX - o.failAtX) / speed;

            foreach (float aspect in new[] { 4f / 3f, 960f / 600f, 16f / 9f, 21f / 9f }) {
                float spawnCam = TriggerX(zep, aspect);
                // The camera moves at most scrollSpeed (ScrollController eases toward ±scrollSpeed).
                float mapEndSeconds = (data.length - spawnCam) / data.scrollSpeed;
                Assert.Less(hqSeconds + 1f, mapEndSeconds,
                    $"aspect {aspect:0.00}: the zeppelin needs {hqSeconds:0.#} s to reach the HQ, but the camera can reach " +
                    $"the map end {mapEndSeconds:0.#} s after it spawns (less than 1 s margin)");

                foreach (var s in data.spawns) {
                    if (s.escortOf != zep.id) continue;
                    Assert.Less(spawnCam, TriggerX(s, aspect),
                        $"aspect {aspect:0.00}: escort group at atX {s.atX} starts before the zeppelin exists");
                }
            }
        }

        /// <summary>B021 (T939 P2-1): when the M5 zeppelin spawns, its whole hull (centred on
        /// atX, ZeppelinAI.HalfLength each side) is beyond the right screen edge, so it flies in
        /// instead of popping in, at every aspect.</summary>
        [Test]
        public void M5_Zeppelin_SpawnsWhollyBeyondTheRightScreenEdge() {
            var data = LoadMission(5);
            var zep = FindById(data, data.objectives[0].targetId);
            Assert.IsNotNull(zep);
            foreach (float aspect in new[] { 4f / 3f, 960f / 600f, 16f / 9f, 21f / 9f }) {
                float rightEdge = TriggerX(zep, aspect) + CameraHalfHeight * aspect;
                float hullLeft = zep.atX - ZeppelinAI.HalfLength;
                Assert.Greater(hullLeft, rightEdge,
                    $"aspect {aspect:0.00}: the hull's nose ({hullLeft:0.##}) is inside the right screen edge ({rightEdge:0.##})");
            }
        }

        // ── Map end (B017) ──────────────────────────────────────────────

        [Test]
        public void AllMissions_EveryRequiredTargetSpawnsBeforeTheMapEnd() {
            foreach (float aspect in Aspects) {
                for (int m = 1; m <= 5; m++) {
                    var data = LoadMission(m);
                    foreach (var o in data.objectives) {
                        foreach (var s in data.spawns) {
                            bool required =
                                (o.type == "destroyAllOfType" && string.Equals(s.type, o.targetType, System.StringComparison.OrdinalIgnoreCase)) ||
                                ((o.type == "destroyById" || o.type == "bombTarget") && !string.IsNullOrEmpty(o.targetId) && s.id == o.targetId);
                            if (!required) continue;
                            Assert.Less(TriggerX(s, aspect), data.length,
                                $"mission {m}: required {s.type} at atX {s.atX} triggers at or after the map end {data.length}");
                        }
                    }
                }
            }
        }

        // ── Escorts ─────────────────────────────────────────────────────

        [Test]
        public void EscortTargets_ExistBeforeTheirEscortsStart_AtSupportedAspects() {
            for (int m = 1; m <= 5; m++) {
                var data = LoadMission(m);
                for (int i = 0; i < data.spawns.Length; i++) {
                    var escort = data.spawns[i];
                    if (string.IsNullOrEmpty(escort.escortOf)) continue;
                    var target = FindById(data, escort.escortOf);
                    Assert.IsNotNull(target, $"mission {m}: escortOf '{escort.escortOf}' names no spawn id");
                    int targetIndex = System.Array.IndexOf(data.spawns, target);
                    foreach (float aspect in Aspects) {
                        float tt = TriggerX(target, aspect), et = TriggerX(escort, aspect);
                        // Same trigger is fine: the whole spawn list is processed in one Update and
                        // FighterAI.Start (the lookup) runs after it.
                        Assert.LessOrEqual(tt, et,
                            $"mission {m}: '{escort.escortOf}' triggers at camera {tt:0.##} after its escort at {et:0.##} (aspect {aspect:0.00}); " +
                            "FighterAI.Start can't find it and the escort flies straight");
                    }
                    Assert.GreaterOrEqual(targetIndex, 0);
                }
            }
        }

        /// <summary>FighterAI.Start: the first TeamTag whose GameObject name contains escortOf.
        /// Returns every TeamTag name in `roots` (and children) that the lookup could pick.</summary>
        private static List<string> NamesMatching(string escortOf, IEnumerable<GameObject> roots) {
            var hits = new List<string>();
            foreach (var root in roots) {
                foreach (var tag in root.GetComponentsInChildren<TeamTag>(true)) {
                    // The runtime rule (B020): whole-id match via FighterAI.IsEscortTarget.
                    if (FighterAI.IsEscortTarget(tag.gameObject, escortOf)) {
                        hits.Add(tag.gameObject.name);
                    }
                }
            }
            return hits;
        }

        [Test]
        public void M2_EscortOf_NameLookupFindsTheBomber() {
            var data = LoadMission(2);
            string id = data.objectives[0].targetId;
            bool anyEscort = false;
            foreach (var s in data.spawns) if (s.escortOf == id) anyEscort = true;
            Assert.IsTrue(anyEscort, "mission 2 escorts the bomber");

            // MissionRunner spawns the bomber with BomberAI.Spawn(position, entry.id) (B020).
            var bomber = BomberAI.Spawn(new Vector3(90f, 0f, 0f), id);
            Assert.AreEqual("bomber", bomber.gameObject.name, "the name stays the EntityDestroyed type key");
            _created.Add(bomber.gameObject);
            var hits = NamesMatching(id, new[] { bomber.gameObject });
            CollectionAssert.Contains(hits, bomber.gameObject.name,
                $"FighterAI escortOf '{id}' can't find the bomber named '{bomber.gameObject.name}': the escorts fly straight");
        }

        [Test]
        public void M5_EscortOf_NameLookupFindsOnlyTheZeppelinHull() {
            var data = LoadMission(5);
            string id = data.objectives[0].targetId;
            var zep = ZeppelinAI.Spawn(new Vector3(430f, 0f, 0f), id);
            _created.Add(zep.gameObject);

            var hits = NamesMatching(id, new[] { zep.gameObject });
            CollectionAssert.Contains(hits, zep.gameObject.name, "the hull is found by its id");
            Assert.AreEqual(1, hits.Count,
                $"escortOf '{id}' also matches {string.Join(", ", hits)}: FindObjectsOfType order decides whether " +
                "the escorts follow the hull or a turret (and stop escorting when that turret dies)");
            foreach (string other in new[] { "fighter", "bomber", "aaa", ZeppelinTurret.ScoreKey }) {
                Assert.AreEqual(-1, other.IndexOf(id, System.StringComparison.OrdinalIgnoreCase),
                    $"escortOf '{id}' is a substring of the '{other}' entity name");
            }
        }

        [Test]
        public void EscortOf_MatchesWholeIdsOnly() {
            var zep = ZeppelinAI.Spawn(new Vector3(430f, 0f, 0f), "zep");
            _created.Add(zep.gameObject);
            var other = ZeppelinAI.Spawn(new Vector3(460f, 0f, 0f), "zep2");
            _created.Add(other.gameObject);
            var bomber = BomberAI.Spawn(new Vector3(90f, 0f, 0f), "bomber_1");
            _created.Add(bomber.gameObject);

            var hits = NamesMatching("zep", new[] { zep.gameObject, other.gameObject, bomber.gameObject });
            CollectionAssert.AreEqual(new[] { zep.gameObject.name }, hits,
                "'zep' must match only the hull named [zep], not its turrets or [zep2]");
            Assert.IsTrue(FighterAI.IsEscortTarget(bomber.gameObject, "bomber_1"));
            Assert.IsFalse(FighterAI.IsEscortTarget(bomber.gameObject, "bomber"), "the type key is not an id");
            Assert.IsFalse(FighterAI.IsEscortTarget(bomber.gameObject, "bomber_10"));
        }

        // ── M5 zeppelin height (V003) ───────────────────────────────────

        [Test]
        public void M5_ZeppelinUndersideTurret_StaysShootableAboveTheTerrain() {
            var data = LoadMission(5);
            var o = data.objectives[0];
            var entry = FindById(data, o.targetId);
            Assert.IsNotNull(entry);

            var terrain = MakeTerrain(data);
            // MissionRunner: (atX, HeightAt(atX) + y); y is height above terrain (§5). The hull
            // keeps that y while it flies left (ZeppelinAI.FixedUpdate moves x only).
            float hullY = terrain.HeightAt(entry.atX) + entry.y;
            var zep = ZeppelinAI.Spawn(new Vector3(entry.atX, hullY, 0f), entry.id);
            _created.Add(zep.gameObject);

            float lowestTurretY = float.MaxValue;
            foreach (var t in zep.GetComponentsInChildren<ZeppelinTurret>(true)) {
                lowestTurretY = Mathf.Min(lowestTurretY, t.transform.position.y);
            }
            Assert.AreNotEqual(float.MaxValue, lowestTurretY, "the zeppelin has turrets");
            float turretLocalY = lowestTurretY - hullY;

            for (float x = entry.atX; x >= o.failAtX; x -= 1f) {
                float ground = terrain.HeightAt(x);
                Assert.GreaterOrEqual(lowestTurretY - ground, MinShootableHeight,
                    $"at x {x}: the underside turret (hull y {hullY:0.##}, turret {turretLocalY:0.##} below/above it) is " +
                    $"{lowestTurretY - ground:0.##} above the ground {ground:0.##}; the player can't fly level with it, " +
                    "so the hull stays shielded (JSON y is height above terrain, not world y)");
            }
        }
    }
}
