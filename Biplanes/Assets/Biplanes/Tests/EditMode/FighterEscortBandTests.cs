using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Missions;
using Biplanes.Player;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// B063 (QA T967 P2-2), ARCHITECTURE §3 "Fighter chase" formation slots: an escort's slot is
    /// kept inside the flight band (ChaseGroundClearance above the ground up to ChaseTopMargin
    /// below the view top). The upper slot of a high plane flips below it (next free tier), so
    /// the real cases (M2 bomber_1 at y 4.0 with 3 escorts, M5 airship_01 held at y 3.7 with
    /// 5 escorts, camera at y 0 with orthographicSize 5: band ceiling 4) are all visible,
    /// distinct, clear of the escorted plane's hitbox, and can fire. Also pins the
    /// FighterChaseData.DefaultStandoffMax fix (QA T967 P2-1).
    ///
    /// EditMode runs no Awake/OnEnable/FixedUpdate: they are invoked by reflection, each
    /// ChaseStep result is written back to the transform, like FixedUpdate does.
    /// </summary>
    public class FighterEscortBandTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const float Dt = 0.02f;
        private const float Eps = 1e-3f;
        /// <summary>Fighter hitbox half size (1.5 x 1, FighterAI.Spawn).</summary>
        private static readonly Vector2 FighterHalf = new Vector2(0.75f, 0.5f);
        /// <summary>Camera like Game.unity: y 0, orthographicSize 5, so the band ceiling is 4.</summary>
        private const float CamSize = 5f;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<FighterAI> _enabled = new List<FighterAI>();

        [SetUp]
        public void SetUp() {
            FighterAttackSlots.Shared.Reset();
        }

        [TearDown]
        public void TearDown() {
            foreach (var f in _enabled) {
                if (f != null) Invoke(f, "OnDisable");
            }
            _enabled.Clear();
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
            FighterAttackSlots.Shared.Reset();
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static object Invoke(object target, string method, params object[] args) {
            var m = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found by reflection");
            return m.Invoke(target, args);
        }

        private static void SetField(object target, string field, object value) {
            var f = target.GetType().GetField(field, Private);
            Assert.IsNotNull(f, $"{target.GetType().Name}.{field} not found by reflection");
            f.SetValue(target, value);
        }

        private Camera MakeCamera(float x) {
            var go = new GameObject("b063_camera");
            _created.Add(go);
            go.transform.position = new Vector3(x, 0f, -10f);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = CamSize;
            cam.aspect = 16f / 9f;
            return cam;
        }

        private PlayerPlane MakePlayer(Vector2 position) {
            var go = new GameObject("player");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<TeamTag>().team = Team.Player;
            Invoke(go.AddComponent<Health>(), "Awake");
            var p = go.AddComponent<PlayerPlane>();
            SetField(p, "_velocity", Vector2.zero);
            return p;
        }

        /// <summary>An escorted plane stand-in: one box collider of half-height `halfHeight`.</summary>
        private Transform MakeTarget(string name, Vector2 position, Vector2 boxSize) {
            var go = new GameObject(name);
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = boxSize;
            return go.transform;
        }

        private List<FighterAI> MakeEscorts(int count, Transform target, PlayerPlane player, Camera cam, string id) {
            var list = new List<FighterAI>();
            for (int i = 0; i < count; i++) {
                var f = FighterAI.Spawn((Vector2)target.position + new Vector2(0.5f * i, -1.5f));
                _created.Add(f.gameObject);
                Invoke(f.GetComponent<Health>(), "Awake");
                Invoke(f, "Awake");
                var prop = typeof(FighterAI).GetProperty("PlayerRef", Private);
                Assert.IsNotNull(prop, "FighterAI.PlayerRef not found by reflection");
                prop.SetValue(f, player);
                f.Configure("escort", id);
                SetField(f, "_escortTarget", target);
                SetField(f, "_cam", cam);
                Invoke(f, "OnEnable");
                _enabled.Add(f);
                list.Add(f);
            }
            return list;
        }

        private static void Tick(FighterAI f) {
            float y0 = f.transform.position.y;
            var next = (Vector3)Invoke(f, "ChaseStep", f.transform.position, Dt);
            f.transform.position = next;
            Invoke(f, "UpdatePitch", (next.y - y0) / Dt, Dt);
        }

        private static void Fly(Transform target, List<FighterAI> escorts, float vx, int ticks) {
            for (int i = 0; i < ticks; i++) {
                target.position += new Vector3(vx * Dt, 0f, 0f);
                foreach (var f in escorts) Tick(f);
            }
        }

        private static void Band(float x, out float low, out float high) {
            var terrain = TerrainStrip.Instance;
            low = (terrain != null ? terrain.HeightAt(x) : TerrainStrip.GroundBaseY) + FighterAI.ChaseGroundClearance;
            high = CamSize - FighterAI.ChaseTopMargin;
        }

        /// <summary>Every escort in formation, inside the band, on screen, clear of the escorted
        /// plane's hitbox (`halfHeight`) and of each other.</summary>
        private static void AssertFormationInBand(List<FighterAI> escorts, Transform target, float halfHeight,
                Camera cam, string at) {
            Vector2 tp = target.position;
            for (int k = 0; k < escorts.Count; k++) {
                var f = escorts[k];
                Vector2 p = f.transform.position;
                Assert.IsTrue(f.IsEscorting && !f.IsEscortChasing, $"{at} escort {k}: in formation");
                Band(p.x, out float low, out float high);
                Assert.GreaterOrEqual(p.y, low - 0.01f, $"{at} escort {k}: above the band floor ({p})");
                Assert.LessOrEqual(p.y, high + 0.01f, $"{at} escort {k}: below the band ceiling ({p})");
                Assert.IsTrue(EnemyBullet.IsOnScreen(cam, p, 0f), $"{at} escort {k}: centre on screen ({p})");
                Assert.GreaterOrEqual(Mathf.Abs(p.y - tp.y), halfHeight + FighterHalf.y,
                    $"{at} escort {k}: clear of the escorted plane's hitbox ({p} vs {tp})");
                Vector2 expectedOffset = FighterAI.FormationOffset(f.EscortSlot, 1, halfHeight);
                Assert.AreEqual(tp.x + expectedOffset.x, p.x, 0.05f, $"{at} escort {k}: slot x (behind)");
                Assert.AreEqual(FighterAI.FormationSlotY(f.EscortSlot, tp.y, halfHeight, low, high), p.y, 0.05f,
                    $"{at} escort {k}: on its band-kept slot y");
                for (int j = k + 1; j < escorts.Count; j++) {
                    Vector2 d = p - (Vector2)escorts[j].transform.position;
                    Assert.IsTrue(Mathf.Abs(d.x) >= 2f * FighterHalf.x || Mathf.Abs(d.y) >= 2f * FighterHalf.y,
                        $"{at} escorts {k} and {j} don't overlap ({d})");
                }
            }
            var slots = new HashSet<int>();
            foreach (var f in escorts) Assert.IsTrue(slots.Add(f.EscortSlot), $"{at}: slot {f.EscortSlot} is unique");
        }

        // ── Pure slot maths ─────────────────────────────────────────────

        [Test]
        public void FormationSlotY_InsideTheBand_MatchesFormationOffset() {
            for (int i = 0; i < 6; i++) {
                float y = FighterAI.FormationSlotY(i, 1f, 0.64f, -3.25f, 4f);
                Assert.AreEqual(1f + FighterAI.FormationOffset(i, 1, 0.64f).y, y, Eps, $"slot {i}: unchanged with room");
                Assert.AreEqual(1f + FighterAI.FormationOffset(i, 1, 0.64f).y,
                    FighterAI.FormationSlotY(i, 1f, 0.64f, -3.25f, float.PositiveInfinity), Eps, $"slot {i}: no ceiling");
            }
        }

        [Test]
        public void FormationSlotY_M2Bomber_UpperSlotsFlipBelow() {
            // bomber_1 y 4.0, half-height 0.64: the upper slot 5.34 > ceiling 4.
            float lower1 = 4f - 0.64f - FighterAI.FormationGapY;
            float lower2 = lower1 - FighterAI.FormationTierGapY;
            Assert.AreEqual(lower2, FighterAI.FormationSlotY(0, 4f, 0.64f, -3.25f, 4f), Eps, "slot 0 flips to tier 2 below");
            Assert.AreEqual(lower1, FighterAI.FormationSlotY(1, 4f, 0.64f, -3.25f, 4f), Eps, "slot 1 keeps tier 1 below");
            Assert.AreEqual(lower2, FighterAI.FormationSlotY(2, 4f, 0.64f, -3.25f, 4f), Eps, "slot 2 (next row) flips too");
        }

        [Test]
        public void FormationSlotY_M5Zeppelin_UpperSlotsFlipBelow() {
            // airship_01 held at view top - 1.3 = 3.7, half-height 0.94: the upper slot 5.34 > 4.
            float lower1 = 3.7f - 0.94f - FighterAI.FormationGapY;
            float lower2 = lower1 - FighterAI.FormationTierGapY;
            for (int i = 0; i < 5; i++) {
                float y = FighterAI.FormationSlotY(i, 3.7f, 0.94f, -3.25f, 4f);
                Assert.AreEqual(i % 2 == 0 ? lower2 : lower1, y, Eps, $"slot {i}");
                Assert.LessOrEqual(y, 4f);
                Assert.GreaterOrEqual(y, -3.25f);
            }
        }

        [Test]
        public void FormationSlotY_LowPlane_LowerSlotFlipsAbove_AndNarrowBandClamps() {
            // Plane near the floor: below tier 1 (-3.84) is under the floor -3.25.
            float upper1 = -2.5f + 0.64f + FighterAI.FormationGapY;
            Assert.AreEqual(upper1, FighterAI.FormationSlotY(1, -2.5f, 0.64f, -3.25f, 4f), Eps, "slot 1 flips above");
            Assert.AreEqual(upper1 + FighterAI.FormationTierGapY, FighterAI.FormationSlotY(0, -2.5f, 0.64f, -3.25f, 4f), Eps,
                "slot 0 takes the next free tier above");
            // No tier fits: both clamp into the band, never outside it.
            for (int i = 0; i < 2; i++) {
                float y = FighterAI.FormationSlotY(i, 0f, 0.64f, -0.2f, 0.2f);
                Assert.GreaterOrEqual(y, -0.2f - Eps);
                Assert.LessOrEqual(y, 0.2f + Eps);
            }
        }

        // ── Real cases, ticked ──────────────────────────────────────────

        [Test]
        public void M2Bomber_ThreeEscorts_HoldDistinctSlotsInsideTheBand_Stable() {
            var cam = MakeCamera(18f);
            var bomber = MakeTarget("bomber_1", new Vector2(20f, 4f), new Vector2(2.64f, 1.28f)); // half-height 0.64
            var player = MakePlayer(new Vector2(-60f, 2f)); // beyond the leash
            var escorts = MakeEscorts(3, bomber, player, cam, "bomber_1");

            Fly(bomber, escorts, -1f, 300);
            AssertFormationInBand(escorts, bomber, 0.64f, cam, "M2");
            var before = new List<Vector2>();
            foreach (var f in escorts) before.Add((Vector2)(f.transform.position - bomber.position));

            Fly(bomber, escorts, -1f, 100);
            AssertFormationInBand(escorts, bomber, 0.64f, cam, "M2 later");
            for (int k = 0; k < escorts.Count; k++) {
                Vector2 now = escorts[k].transform.position - bomber.position;
                Assert.AreEqual(before[k].x, now.x, 0.05f, $"escort {k}: stable slot x");
                Assert.AreEqual(before[k].y, now.y, 0.05f, $"escort {k}: stable slot y");
            }
        }

        [Test]
        public void M5Zeppelin_FiveEscorts_HoldDistinctSlotsInsideTheBand_Stable() {
            var cam = MakeCamera(16f);
            var zeppelin = MakeTarget("airship_01", new Vector2(20f, 3.7f), new Vector2(7.15f, 1.88f)); // half-height 0.94
            var player = MakePlayer(new Vector2(-60f, 2f));
            var escorts = MakeEscorts(5, zeppelin, player, cam, "airship_01");

            Fly(zeppelin, escorts, -0.8f, 400);
            AssertFormationInBand(escorts, zeppelin, 0.94f, cam, "M5");
            var slotsBefore = new List<int>();
            foreach (var f in escorts) slotsBefore.Add(f.EscortSlot);

            Fly(zeppelin, escorts, -0.8f, 100);
            AssertFormationInBand(escorts, zeppelin, 0.94f, cam, "M5 later");
            for (int k = 0; k < escorts.Count; k++) {
                Assert.AreEqual(slotsBefore[k], escorts[k].EscortSlot, $"escort {k}: same slot for its life");
            }
        }

        [Test]
        public void M2Bomber_EscortsInFormation_FireAtThePlayerInRange() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var chase = FighterChaseData.Resolve(GameBalance.Get());
            var cam = MakeCamera(14f);
            var bomber = MakeTarget("bomber_1", new Vector2(20f, 4f), new Vector2(2.64f, 1.28f));
            var player = MakePlayer(new Vector2(-60f, 2f));
            var escorts = MakeEscorts(2, bomber, player, cam, "bomber_1");
            Fly(bomber, escorts, -1f, 300);

            // The bomber holds still; the player comes into gun range of the first pair but stays
            // beyond the leash, so the escorts keep formation, face it and aim.
            Vector2 bp = bomber.position;
            player.transform.position = new Vector3(bp.x - (chase.escortLeash + 1f), 2f, 0f);
            for (int i = 0; i < 100; i++) foreach (var f in escorts) Tick(f);
            AssertFormationInBand(escorts, bomber, 0.64f, cam, "M2 firing");

            foreach (var f in escorts) {
                Vector2 to = (Vector2)player.transform.position - (Vector2)f.transform.position;
                Assert.Less(to.magnitude, fire.range, $"slot {f.EscortSlot}: player in gun range");
                Assert.AreEqual(-1, f.FlightDirection, $"slot {f.EscortSlot}: faces the player");
                Assert.IsTrue((bool)Invoke(f, "TickFire", fire.firstShotDelayMax + 0.01f),
                    $"slot {f.EscortSlot}: fires once its first-shot delay ran out");
                Assert.IsTrue(f.HasAttackSlot, $"slot {f.EscortSlot}: holds an attack slot");
            }
        }

        // ── Balance default (QA T967 P2-1) ──────────────────────────────

        [Test]
        public void DefaultStandoffMax_MatchesBalance() {
            Assert.AreEqual(5f, FighterChaseData.DefaultStandoffMax, Eps);
            Assert.AreEqual(5f, FighterChaseData.Resolve(null).standoffMax, Eps);
            Assert.AreEqual(FighterChaseData.Resolve(GameBalance.Get()).standoffMax,
                FighterChaseData.Resolve(null).standoffMax, Eps, "a Balance without the key behaves like Balance");
        }
    }
}
