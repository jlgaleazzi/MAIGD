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
    /// QA T966: extra coverage for the fighter chase (B059) and attack slots (B060), GDD Enemies
    /// item 1, ARCHITECTURE §3 "Fighter chase": no fire mid-turn, no ramming in 2-D (avoid box),
    /// the separation push between live fighters, a dead player (no turn, no fire), the smoke
    /// tail after a turn, the play-start static reset, and the worst-case pack damage rate.
    ///
    /// EditMode runs no Awake/OnEnable/FixedUpdate: they are invoked by reflection, and each
    /// ChaseStep result is written back to the transform. Fighters put in the live list
    /// (OnEnable) are taken out again (OnDisable) in TearDown, and FighterAttackSlots.Shared is
    /// reset in SetUp and TearDown, so nothing leaks into other fixtures.
    /// </summary>
    public class FighterChaseQaTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const float Dt = 0.02f;
        private const float Eps = 1e-4f;

        /// <summary>Centre-to-centre contact distance of the unrotated hitboxes: fighter 1.5 x 1
        /// (FighterAI.Spawn) and player about 1.6 x 0.8 (collider auto-sized to the 160 x 80 px
        /// sprite at 100 PPU). Pitch rotates both boxes in play, so this is the optimistic test.</summary>
        private const float ContactX = 1.55f;
        private const float ContactY = 0.9f;

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

        private PlayerPlane MakePlayer(Vector2 position, Vector2 velocity) {
            var go = new GameObject("player");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<TeamTag>().team = Team.Player;
            Invoke(go.AddComponent<Health>(), "Awake");
            var p = go.AddComponent<PlayerPlane>();
            SetField(p, "_velocity", velocity);
            return p;
        }

        private FighterAI MakeFighter(Vector2 position, PlayerPlane player, float offsetY = 0f) {
            var f = FighterAI.Spawn(position);
            _created.Add(f.gameObject);
            Invoke(f.GetComponent<Health>(), "Awake");
            Invoke(f, "Awake");
            var prop = typeof(FighterAI).GetProperty("PlayerRef", Private);
            Assert.IsNotNull(prop, "FighterAI.PlayerRef not found by reflection");
            prop.SetValue(f, player);
            SetField(f, "_offsetY", offsetY); // worst case: right at the player's height
            return f;
        }

        private static Vector3 Step(FighterAI f) {
            var next = (Vector3)Invoke(f, "ChaseStep", f.transform.position, Dt);
            f.transform.position = next;
            return next;
        }

        private static bool TickFire(FighterAI f, float dt) {
            return (bool)Invoke(f, "TickFire", dt);
        }

        private static void PrimeBulletPool() {
            EnemyBullet.Spawn(Vector3.zero, Vector2.left, DamageSource.Bullet).Recycle();
            foreach (var b in Object.FindObjectsByType<EnemyBullet>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                Invoke(b, "Awake");
            }
        }

        private static bool InContact(Vector2 a, Vector2 b) {
            return Mathf.Abs(a.x - b.x) < ContactX && Mathf.Abs(a.y - b.y) < ContactY;
        }

        // ── No fire mid-turn (B059) ─────────────────────────────────────

        [Test]
        public void MidTurn_NeverFires_FiresOnceTheTurnIsDone() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var player = MakePlayer(new Vector2(-5f, 0f), Vector2.zero); // dead ahead, level
            PrimeBulletPool();
            var f = MakeFighter(Vector2.zero, player);

            SetField(f, "_heading", -0.5f); // mid-turn, still facing left (the nose edge-on)
            Assert.IsTrue(f.IsTurning);
            Assert.IsFalse(TickFire(f, fire.firstShotDelayMax + 0.01f), "no fire while turning");
            Assert.IsFalse(f.HasAttackSlot, "not eligible mid-turn: no slot");

            SetField(f, "_heading", -1f);
            Assert.IsFalse(f.IsTurning);
            Assert.IsTrue(TickFire(f, Dt), "turn done, nose on the player: fires");
        }

        // ── No ramming (avoid box, standoff) ────────────────────────────

        [Test]
        public void FasterPlayer_OvertakesAFighterAhead_WithoutContact() {
            // Missions 3/4 (cruise 3.9 > fighter 3): the fighter ahead can't hold its standoff,
            // so the player overtakes it; the avoid box must take it out of the way vertically.
            foreach (float cruise in new[] { 3.25f, 3.9f }) {
                var player = MakePlayer(new Vector2(0f, 0f), new Vector2(cruise, 0f));
                var f = MakeFighter(new Vector2(13.5f, 0f), player);
                bool passed = false;
                // Closing speed once the fighter is flat out: cruise - 3 (0.25 u/s in mission 2).
                for (int i = 0; i < 4000; i++) {
                    player.transform.position += new Vector3(cruise * Dt, 0f, 0f);
                    Vector3 p = Step(f);
                    Assert.IsFalse(InContact(p, player.transform.position),
                        $"cruise {cruise}, tick {i}: rammed (dx {p.x - player.transform.position.x:0.00}, dy {p.y - player.transform.position.y:0.00})");
                    if (p.x < player.transform.position.x - 2f) passed = true;
                }
                Assert.IsTrue(passed, $"cruise {cruise}: the player overtakes it");
                Assert.AreEqual(1, f.FlightDirection, $"cruise {cruise}: turned to chase the player");
            }
        }

        [Test]
        public void SlowerPlayer_FighterAheadPassesAndEndsBehindAtItsStandoff_NoContact_EvenWhenThePlayerTurnsBack() {
            // Missions 1/5 (cruise 2.6 < fighter 3). B061 (QA T967): a fighter ahead no longer
            // parks tail-first at its standoff; it flies at the player, passes above/below it,
            // turns and then holds its standoff behind it, nose first.
            const float cruise = 2.6f;
            var player = MakePlayer(new Vector2(0f, 0f), new Vector2(cruise, 0f));
            var ahead = MakeFighter(new Vector2(13.5f, 0f), player);
            for (int i = 0; i < 1500; i++) {
                player.transform.position += new Vector3(cruise * Dt, 0f, 0f);
                Vector3 p = Step(ahead);
                Assert.IsFalse(InContact(p, player.transform.position), $"tick {i}: rammed");
                if (!ahead.IsTurning) {
                    Assert.GreaterOrEqual(ahead.FlightDirection * ahead.WorldVelocityX, FighterAI.MinForwardSpeed - 1e-3f,
                        $"tick {i}: never tail-first");
                }
            }
            Assert.AreEqual(1, ahead.FlightDirection, "passed, then turned to chase the player");
            Assert.AreEqual(ahead.Standoff, player.transform.position.x - ahead.transform.position.x, 0.15f,
                "holds its standoff behind the player");
            Assert.AreEqual(cruise, ahead.WorldVelocityX, 0.05f, "matches the cruise, nose first");

            // The player turns round (worst case: at once) onto the fighter trailing it at its
            // standoff. The fighter now flies at it, passes, turns and trails it the other way.
            var player2 = MakePlayer(new Vector2(100f, 0f), new Vector2(-cruise, 0f));
            var behind = MakeFighter(new Vector2(100f, 0f), player2);
            behind.SetFlightDirection(1);
            behind.transform.position = new Vector3(100f - behind.Standoff, 0f, 0f);
            SetField(behind, "_velX", cruise); // it was chasing the player at the cruise
            for (int i = 0; i < 1500; i++) {
                player2.transform.position += new Vector3(-cruise * Dt, 0f, 0f);
                Vector3 p = Step(behind);
                Assert.IsFalse(InContact(p, player2.transform.position), $"turned back onto it, tick {i}: rammed");
                if (!behind.IsTurning) {
                    Assert.GreaterOrEqual(behind.FlightDirection * behind.WorldVelocityX, FighterAI.MinForwardSpeed - 1e-3f,
                        $"turned back onto it, tick {i}: never tail-first");
                }
            }
            Assert.AreEqual(-1, behind.FlightDirection, "passed, then turned to chase the player again");
            Assert.AreEqual(-cruise, behind.WorldVelocityX, 0.05f, "matches the cruise, nose first");
            Assert.AreEqual(behind.Standoff, behind.transform.position.x - player2.transform.position.x, 0.15f,
                "holds its standoff behind the player");
        }

        // ── Separation between live fighters ────────────────────────────

        [Test]
        public void Separation_TwoLevelFighters_PushApart_AndStayClearOfTheGround() {
            var c = FighterChaseData.Resolve(GameBalance.Get());
            // Player low, at the bottom of the flight band (no camera, no terrain: GroundBaseY).
            float floor = TerrainStrip.GroundBaseY + FighterAI.ChaseGroundClearance;
            var player = MakePlayer(new Vector2(-20f, floor), Vector2.zero);
            var a = MakeFighter(new Vector2(0f, floor), player);
            var b = MakeFighter(new Vector2(0f, floor), player);
            foreach (var f in new[] { a, b }) {
                SetField(f, "_standoff", 4f); // same standoff: they stay level in x
                Invoke(f, "OnEnable");
                _enabled.Add(f);
            }
            float minY = float.MaxValue;
            for (int i = 0; i < 300; i++) {
                Step(a);
                Step(b);
                minY = Mathf.Min(minY, Mathf.Min(a.transform.position.y, b.transform.position.y));
            }
            float dy = Mathf.Abs(a.transform.position.y - b.transform.position.y);
            Assert.Greater(dy, 0.5f * c.separation, "level fighters push apart vertically");
            Assert.Greater(minY, TerrainStrip.GroundBaseY + 0.5f,
                "the push may dip below the band floor a little but never to the ground");
        }

        // ── Dead player ─────────────────────────────────────────────────

        [Test]
        public void DeadPlayer_NoTurn_NoClimb_NoFire() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var player = MakePlayer(new Vector2(6f, 3f), Vector2.zero); // behind and above
            PrimeBulletPool();
            var f = MakeFighter(new Vector2(0f, 0f), player);
            player.GetComponent<Health>().ApplyDamage(100f, DamageSource.Collision);
            Assert.IsTrue(player.GetComponent<Health>().IsDead, "precondition");

            float speed = Mathf.Abs(f.WorldVelocityX);
            for (int i = 0; i < 100; i++) {
                Step(f);
                Assert.IsFalse(TickFire(f, 1f), $"tick {i}: never fires at a dead player");
            }
            Assert.AreEqual(-1, f.FlightDirection, "no turn toward a dead player");
            Assert.IsFalse(f.IsTurning);
            Assert.AreEqual(-speed, f.WorldVelocityX, 1e-3f, "keeps its heading at cruise speed");
            Assert.AreEqual(0f, f.transform.position.y, Eps, "no climb toward a dead player");
            Assert.AreEqual(0, FighterAttackSlots.Shared.HolderCount);
        }

        // ── Reinforcement / smoke tail ──────────────────────────────────

        [Test]
        public void Reinforcement_EnteringAwayFromThePlayer_TurnsToChase_SmokeTailFollows() {
            var player = MakePlayer(new Vector2(-8f, 0f), Vector2.zero);
            var f = MakeFighter(new Vector2(0f, 0f), player);
            f.SetFlightDirection(1); // entered flying right, the player is on its left
            Vector3 tail = FighterAI.SmokeTailPoint(f.transform.position, f.PitchAngle, f.FlightDirection);
            Assert.Less(tail.x, f.transform.position.x, "flying right: tail on the left");

            for (int i = 0; i < 100; i++) Step(f);
            Assert.AreEqual(-1, f.FlightDirection, "turned toward the player");
            Assert.IsFalse(f.IsTurning);
            tail = FighterAI.SmokeTailPoint(f.transform.position, f.PitchAngle, f.FlightDirection);
            Assert.Greater(tail.x, f.transform.position.x, "facing left: the smoke tail is on the right");
            Assert.Less(f.WorldVelocityX, 0f, "chasing toward the player");
        }

        // ── Static reset (play start without a domain reload) ───────────

        [Test]
        public void ResetStatics_ClearsTheLiveListAndTheSlots() {
            var fire = EnemyFireData.Resolve(GameBalance.Get());
            var player = MakePlayer(new Vector2(-5f, 0f), Vector2.zero);
            PrimeBulletPool();
            var f = MakeFighter(Vector2.zero, player);
            Invoke(f, "OnEnable");
            _enabled.Add(f);
            Assert.IsTrue(TickFire(f, fire.firstShotDelayMax + 0.01f));
            Assert.AreEqual(1, FighterAttackSlots.Shared.HolderCount);

            var live = (List<FighterAI>)typeof(FighterAI).GetField("s_live", PrivateStatic).GetValue(null);
            Assert.Contains(f, live);
            var reset = typeof(FighterAI).GetMethod("ResetStatics", PrivateStatic);
            Assert.IsNotNull(reset, "FighterAI.ResetStatics not found by reflection");
            reset.Invoke(null, null);
            Assert.AreEqual(0, live.Count, "live list cleared");
            Assert.AreEqual(0, FighterAttackSlots.Shared.HolderCount, "slots cleared");
            Assert.AreEqual(0, FighterAttackSlots.Shared.TrackedCount);
        }

        // ── Balance on paper (B060) ─────────────────────────────────────

        [Test]
        public void WorstPackDamageRate_IsCappedByTheAttackSlots() {
            var g = GameBalance.Get();
            var fire = EnemyFireData.Resolve(g);
            var chase = FighterChaseData.Resolve(g);
            float perHit = g.damageTaken.fighterBullet / PlayerData.ResistanceOrDefault(g.player);
            float rate = chase.maxAttackers * perHit / fire.fighterIntervalMin;
            Assert.AreEqual(5.56f, rate, 0.05f, "B060: 3 shooters x 3.33% per 1.8 s");

            // Every shot hitting, first volley at 0: the crash is no sooner than this.
            int volleys = Mathf.CeilToInt(100f / (chase.maxAttackers * perHit) - 1e-3f);
            float crashAt = (volleys - 1) * fire.fighterIntervalMin;
            Assert.GreaterOrEqual(crashAt, 15f, "a full-health player survives a whole pack for 15 s or more");
        }
    }
}
