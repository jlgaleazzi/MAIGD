using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Player;

namespace Biplanes.Tests {

    /// <summary>
    /// B059: every enemy fighter faces and chases the player (GDD Enemies item 1, ARCHITECTURE
    /// §3 "Fighter chase"). The pure steering helpers on FighterAI, the fighterChase Balance
    /// block, and the internal FighterAI.ChaseStep driven tick by tick.
    ///
    /// EditMode doesn't run Awake/Start/FixedUpdate and MovePosition doesn't move the
    /// transform, so the integration tests invoke Awake by reflection, set PlayerRef by
    /// reflection, and feed each ChaseStep result back into transform.position.
    /// </summary>
    public class FighterChaseTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const float Dt = 0.02f;
        private const float Eps = 1e-4f;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static FighterChaseData Chase => FighterChaseData.Resolve(GameBalance.Get());

        private static float FighterSpeed {
            get {
                var g = GameBalance.Get();
                return g != null && g.enemySpeed != null && g.enemySpeed.fighter > 0f ? g.enemySpeed.fighter : 3f;
            }
        }

        private static void InvokeAwake(Component c) {
            var awake = c.GetType().GetMethod("Awake", Private);
            Assert.IsNotNull(awake, $"{c.GetType().Name}.Awake not found by reflection");
            awake.Invoke(c, null);
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
            var prop = typeof(FighterAI).GetProperty("PlayerRef", Private);
            Assert.IsNotNull(prop, "FighterAI.PlayerRef not found by reflection");
            prop.SetValue(f, player);
            return f;
        }

        private static readonly MethodInfo ChaseStepMethod = typeof(FighterAI).GetMethod("ChaseStep", Private);

        /// <summary>One chase tick: ChaseStep from the transform, result written back.</summary>
        private static Vector3 Step(FighterAI f) {
            Assert.IsNotNull(ChaseStepMethod, "FighterAI.ChaseStep not found by reflection");
            var next = (Vector3)ChaseStepMethod.Invoke(f, new object[] { f.transform.position, Dt });
            f.transform.position = next;
            return next;
        }

        // ── Balance ─────────────────────────────────────────────────────

        [Test]
        public void Resolve_NullBalance_GivesDefaults() {
            var c = FighterChaseData.Resolve(null);
            Assert.AreEqual(0.6f, c.turnSeconds, Eps);
            Assert.AreEqual(1f, c.turnDeadzone, Eps);
            Assert.AreEqual(4f, c.accel, Eps);
            Assert.AreEqual(3.5f, c.standoff, Eps);
            Assert.AreEqual(2.5f, c.climbRate, Eps);
            Assert.AreEqual(1.2f, c.separation, Eps);
            Assert.AreEqual(8f, c.escortLeash, Eps);
        }

        [Test]
        public void BalanceJson_HasFighterChaseBlock_WithSpecValues() {
            var g = GameBalance.Get();
            Assert.IsNotNull(g, "Balance.json loads");
            Assert.IsNotNull(g.fighterChase, "Balance.json has a fighterChase block");
            var c = FighterChaseData.Resolve(g);
            Assert.AreEqual(0.6f, c.turnSeconds, Eps);
            Assert.AreEqual(1f, c.turnDeadzone, Eps);
            Assert.AreEqual(4f, c.accel, Eps);
            Assert.AreEqual(3.5f, c.standoff, Eps);
            Assert.AreEqual(2.5f, c.climbRate, Eps);
            Assert.AreEqual(1.2f, c.separation, Eps);
            Assert.AreEqual(8f, c.escortLeash, Eps);
        }

        // ── Facing / turn ───────────────────────────────────────────────

        [Test]
        public void DesiredFacing_TurnsOnlyWhenPlayerIsBeyondTheDeadzoneBehind() {
            Assert.AreEqual(-1, FighterAI.DesiredFacing(-1, -5f, 1f), "player ahead (left): keep");
            Assert.AreEqual(-1, FighterAI.DesiredFacing(-1, 0.9f, 1f), "just behind, inside deadzone: keep");
            Assert.AreEqual(1, FighterAI.DesiredFacing(-1, 1.1f, 1f), "behind beyond deadzone: turn right");
            Assert.AreEqual(1, FighterAI.DesiredFacing(1, -0.5f, 1f), "right-facing, inside deadzone: keep");
            Assert.AreEqual(-1, FighterAI.DesiredFacing(1, -1.5f, 1f), "right-facing, player behind: turn left");
        }

        [Test]
        public void StepHeading_FullTurnTakesTurnSeconds_AndIsEased() {
            float h = -1f;
            int steps = 0;
            while (h < 1f && steps < 1000) {
                h = FighterAI.StepHeading(h, 1, 0.6f, Dt);
                steps++;
            }
            Assert.AreEqual(0.6f, steps * Dt, Dt + Eps, "a full turn takes turnSeconds");
            Assert.AreEqual(1f, FighterAI.StepHeading(-1f, 1, 0f, Dt), "turnSeconds 0: instant");
            Assert.AreEqual(1, FighterAI.FacingFromHeading(0.01f, -1));
            Assert.AreEqual(-1, FighterAI.FacingFromHeading(-0.01f, 1));
            Assert.AreEqual(1, FighterAI.FacingFromHeading(0f, 1), "exactly 0 keeps the facing");
            Assert.AreEqual(1f, FighterAI.TurnScaleX(-1f), Eps);
            Assert.AreEqual(1f, FighterAI.TurnScaleX(1f), Eps);
            Assert.AreEqual(FighterAI.MinTurnScale, FighterAI.TurnScaleX(0f), Eps, "edge-on mid-turn");
            Assert.Greater(FighterAI.TurnScaleX(0.9f), 0.9f, "eased: slow change near the ends");
        }

        // ── Horizontal pursuit ──────────────────────────────────────────

        [Test]
        public void HorizontalStep_AccelLimited_AndCappedAtMaxSpeed() {
            float v = 0f;
            float next = FighterAI.HorizontalVelocityStep(0f, v, 50f, 0f, 1, 3.5f, 3f, 4f, Dt);
            Assert.AreEqual(4f * Dt, next, Eps, "speeds up at accel");
            for (int i = 0; i < 200; i++) v = FighterAI.HorizontalVelocityStep(0f, v, 50f, 0f, 1, 3.5f, 3f, 4f, Dt);
            Assert.AreEqual(3f, v, Eps, "never above enemySpeed.fighter");
            Assert.AreEqual(0f, FighterAI.HorizontalVelocityStep(6.5f, 0f, 10f, 0f, 1, 3.5f, 3f, 4f, Dt), Eps,
                "at the standoff point with a still player: holds");
        }

        [Test]
        public void HorizontalStep_NeverOvershootsTheStandoffPoint() {
            // Any start behind the aim point, any initial speed toward it: it stops short.
            float[] starts = { -30f, -10f, 0f, 3f, 5f };
            float[] speeds = { 0f, 1.5f, 3f };
            foreach (float x0 in starts) {
                foreach (float v0 in speeds) {
                    float x = x0, v = v0;
                    const float playerX = 10f, standoff = 3.5f;
                    float aim = playerX - standoff;
                    for (int i = 0; i < 1500; i++) {
                        v = FighterAI.HorizontalVelocityStep(x, v, playerX, 0f, 1, standoff, 3f, 4f, Dt);
                        x += v * Dt;
                        Assert.LessOrEqual(x, aim + 0.05f, $"start {x0} at {v0}: overshot at tick {i}");
                    }
                    Assert.AreEqual(aim, x, 0.05f, $"start {x0} at {v0}: settles at the standoff point");
                }
            }
        }

        [Test]
        public void HorizontalStep_PlayerFasterThanFighter_FallsAway_PlayerSlower_HoldsStandoff() {
            float x = 0f, v = 3f, px = 3.5f;
            for (int i = 0; i < 500; i++) {
                v = FighterAI.HorizontalVelocityStep(x, v, px, 3.9f, 1, 3.5f, 3f, 4f, Dt);
                x += v * Dt;
                px += 3.9f * Dt;
            }
            Assert.AreEqual(3f, v, Eps, "flat out behind a faster player");
            Assert.Greater(px - x, 3.5f + 5f, "the faster player pulls away");

            x = 0f; v = 3f; px = 10f;
            for (int i = 0; i < 1500; i++) {
                v = FighterAI.HorizontalVelocityStep(x, v, px, 2.6f, 1, 3.5f, 3f, 4f, Dt);
                x += v * Dt;
                px += 2.6f * Dt;
                Assert.Less(x, px - 3.5f + 0.1f, "no ramming in steady pursuit");
            }
            Assert.AreEqual(3.5f, px - x, 0.1f, "holds the standoff behind a slower player");
            Assert.AreEqual(2.6f, v, 0.05f, "matches the player's speed");
        }

        // ── Vertical pursuit ────────────────────────────────────────────

        [Test]
        public void VerticalStep_ClimbRateCapped_BandClamped_EasesIntoTheBand() {
            float y = FighterAI.VerticalStep(0f, 10f, 2.5f, -5f, 20f, Dt);
            Assert.AreEqual(2.5f * Dt, y, Eps, "climbs at most climbRate");
            y = 0f;
            for (int i = 0; i < 500; i++) y = FighterAI.VerticalStep(y, -10f, 2.5f, -2f, 5f, Dt);
            Assert.AreEqual(-2f, y, 0.01f, "never chases below the band floor");
            y = 0f;
            for (int i = 0; i < 500; i++) y = FighterAI.VerticalStep(y, 10f, 2.5f, -2f, 3f, Dt);
            Assert.AreEqual(3f, y, 0.01f, "never above the band ceiling");
            float outside = FighterAI.VerticalStep(-6f, 0f, 2.5f, -2f, 3f, Dt);
            Assert.AreEqual(-6f + 2.5f * Dt, outside, Eps, "below the band: eases up, no snap");
        }

        // ── Separation / avoidance ──────────────────────────────────────

        [Test]
        public void SeparationPush_PushesApartVertically_InsideTheRadiusOnly() {
            Assert.AreEqual(0f, FighterAI.SeparationPush(new Vector2(0f, 0f), new Vector2(0f, 2f), 1.2f, 1), Eps);
            Assert.Greater(FighterAI.SeparationPush(new Vector2(0f, 0.5f), new Vector2(0f, 0f), 1.2f, -1), 0f, "above: up");
            Assert.Less(FighterAI.SeparationPush(new Vector2(0f, 0f), new Vector2(0f, 0.5f), 1.2f, 1), 0f, "below: down");
            Assert.AreEqual(1f, FighterAI.SeparationPush(Vector2.zero, Vector2.zero, 1.2f, 1), Eps, "level tie: tieBreak +");
            Assert.AreEqual(-1f, FighterAI.SeparationPush(Vector2.zero, Vector2.zero, 1.2f, -1), Eps, "level tie: tieBreak -");
            Assert.Greater(FighterAI.SeparationPush(new Vector2(0f, 0.2f), Vector2.zero, 1.2f, 1),
                FighterAI.SeparationPush(new Vector2(0f, 1f), Vector2.zero, 1.2f, 1), "stronger when closer");
        }

        [Test]
        public void Avoidance_InsideTheStandoffBox_SteersAboveOrBelow() {
            Assert.IsTrue(FighterAI.InAvoidBox(2f, 0.3f, 3.5f));
            Assert.IsFalse(FighterAI.InAvoidBox(3.5f, 0f, 3.5f), "holding the standoff is not inside the box");
            Assert.IsFalse(FighterAI.InAvoidBox(1f, 2f, 3.5f), "well above: clear");
            Assert.AreEqual(FighterAI.AvoidClearance, FighterAI.AvoidTargetY(0f, 1, -10f, 10f), Eps);
            Assert.AreEqual(-FighterAI.AvoidClearance, FighterAI.AvoidTargetY(0f, -1, -10f, 10f), Eps);
            Assert.AreEqual(-FighterAI.AvoidClearance, FighterAI.AvoidTargetY(0f, 1, -10f, 0.5f), Eps,
                "no room above under the ceiling: goes below");
        }

        // ── Escort leash / flavour ──────────────────────────────────────

        [Test]
        public void EscortShouldChase_LeashWithHysteresis() {
            Assert.IsFalse(FighterAI.EscortShouldChase(false, 9f, 8f), "formation: player beyond the leash");
            Assert.IsTrue(FighterAI.EscortShouldChase(false, 7f, 8f), "player within the leash: chase");
            Assert.IsTrue(FighterAI.EscortShouldChase(true, 15f, 8f), "chasing: keeps going up to twice the leash");
            Assert.IsFalse(FighterAI.EscortShouldChase(true, 17f, 8f), "beyond twice the leash: return");
        }

        [Test]
        public void FlavourOffset_SmallOnTop_NeverAFixedPath() {
            Assert.AreEqual(0f, FighterAI.FlavourOffset("straight", 1f, 0.5f, 2f, 0f), Eps);
            Assert.AreEqual(0f, FighterAI.FlavourOffset("static", 1f, 0.5f, 2f, 0f), Eps);
            for (float t = 0f; t < 10f; t += 0.1f) {
                Assert.LessOrEqual(Mathf.Abs(FighterAI.FlavourOffset("sine", t, 0.5f, 2f, 0.3f)), 0.5f + Eps);
            }
            Assert.Less(FighterAI.FlavourOffset("dive", FighterAI.DiveFlavourSeconds * 0.5f, 0.5f, 2f, 0f), -1f,
                "dive: an initial dip");
            Assert.AreEqual(0f, FighterAI.FlavourOffset("dive", FighterAI.DiveFlavourSeconds + 0.1f, 0.5f, 2f, 0f), Eps,
                "then it chases at the plain height");
        }

        // ── Integration: ChaseStep ──────────────────────────────────────

        [Test]
        public void FighterAhead_TurnsToThePlayerBehind_WithinTurnSeconds_AndClosesToStandoff() {
            // B061 (QA T967): a fighter never hovers or flies tail-first. With a still player it
            // turns (the turn is held until its velocity along the new facing reaches
            // MinForwardSpeed), closes, slows to MinForwardSpeed at its standoff point, flies
            // over/under the player with AvoidClearance and turns again once past it.
            var c = Chase;
            var player = MakePlayer(new Vector2(6f, 0f));
            var f = MakeFighter(new Vector2(0f, 0f), player);
            typeof(FighterAI).GetField("_offsetY", Private).SetValue(f, 0f); // worst case: level
            Assert.AreEqual(-1, f.FlightDirection, "JSON fighter starts flying left, away from the player");

            Step(f);
            Assert.AreEqual(-1, f.FlightDirection, "no instant flip");
            Assert.IsTrue(f.IsTurning, "the turn has started");
            Assert.Less(f.transform.localScale.x, 1f, "eased: the body narrows into the turn");

            // The turn ends when both the eased turn (turnSeconds) and the speed hold (from
            // -speed to +MinForwardSpeed at accel) are done.
            float turnBound = Mathf.Max(c.turnSeconds, (FighterSpeed + FighterAI.MinForwardSpeed) / c.accel) + 3f * Dt;
            float turnedAt = -1f, passedAt = -1f, turnedBackAt = -1f, speedAtStandoff = -1f;
            bool flipped = false;
            float minDyNear = float.MaxValue;
            for (int i = 1; i < 1500; i++) {
                Vector3 p = Step(f);
                float t = (i + 1) * Dt;
                if (f.FlightDirection == 1 && !flipped) {
                    flipped = true;
                    Assert.IsTrue(f.GetComponent<SpriteRenderer>().flipX == false, "sprite unmirrored facing right");
                }
                if (turnedAt < 0f && f.FlightDirection == 1 && !f.IsTurning) {
                    turnedAt = t;
                    Assert.AreEqual(1f, f.transform.localScale.x, Eps, "full width after the turn");
                    Assert.Greater(f.NoseDirection.x, 0f, "nose toward the player");
                }
                if (turnedAt > 0f && speedAtStandoff < 0f && p.x >= 6f - f.Standoff) speedAtStandoff = f.WorldVelocityX;
                if (turnedAt > 0f && passedAt < 0f && p.x > 6f) passedAt = t;
                if (passedAt > 0f && turnedBackAt < 0f && f.FlightDirection == -1 && !f.IsTurning) turnedBackAt = t;
                if (!f.IsTurning) {
                    Assert.GreaterOrEqual(f.FlightDirection * f.WorldVelocityX, FighterAI.MinForwardSpeed - 1e-3f,
                        $"tick {i}: never tail-first or hovering (vx {f.WorldVelocityX:0.000}, facing {f.FlightDirection})");
                }
                if (Mathf.Abs(p.x - 6f) < 1.55f) minDyNear = Mathf.Min(minDyNear, Mathf.Abs(p.y));
            }
            Assert.Greater(turnedAt, 0f, "the turn finished");
            Assert.LessOrEqual(turnedAt, turnBound, "turned within about turnSeconds (plus the speed hold)");
            Assert.AreEqual(FighterAI.MinForwardSpeed, speedAtStandoff, 0.05f,
                "closes to its own standoff point (B060) and is down to MinForwardSpeed there");
            Assert.Greater(passedAt, 0f, "a still player is passed, not hovered at");
            Assert.Greater(turnedBackAt, passedAt, "then it turns back toward the player");
            Assert.GreaterOrEqual(minDyNear, FighterAI.AvoidClearance - 0.05f, "passes with the avoid clearance, no contact");
        }

        [Test]
        public void WorldVelocityX_IsTheRealChaseVelocity() {
            var player = MakePlayer(new Vector2(-20f, 0f));
            var f = MakeFighter(new Vector2(0f, 0f), player);
            for (int i = 0; i < 20; i++) {
                float x0 = f.transform.position.x;
                Step(f);
                Assert.AreEqual((f.transform.position.x - x0) / Dt, f.WorldVelocityX, 1e-3f, $"tick {i}");
            }
            Assert.Less(f.WorldVelocityX, 0f, "flies toward the player on the left");
        }

        [Test]
        public void NoPlayer_KeepsItsHeadingAtCruiseSpeed() {
            var f = MakeFighter(new Vector2(0f, 1f), null);
            f.SetFlightDirection(1);
            for (int i = 0; i < 100; i++) Step(f);
            Assert.AreEqual(1, f.FlightDirection, "no turn without a player");
            Assert.AreEqual(FighterSpeed, f.WorldVelocityX, 1e-3f, "cruise speed along its heading");
            Assert.AreEqual(1f, f.transform.position.y, Eps, "no climb or dive");
        }

        [Test]
        public void Escort_HoldsFormation_UntilThePlayerIsWithinTheLeash() {
            var c = Chase;
            var target = new GameObject("bomber_target");
            _created.Add(target);
            target.transform.position = new Vector3(2f, 0f, 0f);
            var player = MakePlayer(new Vector2(-30f, 0f));
            var f = MakeFighter(new Vector2(0f, 0f), player);
            f.Configure("escort", "bomber_1");
            typeof(FighterAI).GetField("_escortTarget", Private).SetValue(f, target.transform);

            for (int i = 0; i < 100; i++) Step(f);
            Assert.IsTrue(f.IsEscorting);
            Assert.IsFalse(f.IsEscortChasing, "player far away: formation");
            // B061: its own slot (0: above, FormationBehindX behind a plane that flies left by
            // default; no box collider on the target, so FormationDefaultHalfHeight).
            Assert.AreEqual(0, f.EscortSlot);
            Vector2 slot = (Vector2)target.transform.position
                + FighterAI.FormationOffset(0, 1, FighterAI.FormationDefaultHalfHeight);
            Assert.AreEqual(slot.x, f.transform.position.x, 0.01f, "sits at its formation slot x (behind)");
            Assert.AreEqual(slot.y, f.transform.position.y, 0.01f, "sits at its formation slot y (above)");
            Assert.AreEqual(0f, f.WorldVelocityX, 1e-3f, "holds the slot of a still plane");

            player.transform.position = new Vector3(2f - c.escortLeash + 1f, 0f, 0f);
            Step(f);
            Assert.IsTrue(f.IsEscortChasing, "player within the leash: chase");
            Assert.IsTrue(f.IsEscorting, "still tied to its live target (EnemyPressure exempt)");

            player.transform.position = new Vector3(2f - 2f * c.escortLeash - 1f, 0f, 0f);
            Step(f);
            Assert.IsFalse(f.IsEscortChasing, "beyond twice the leash: back to formation");
        }
    }
}
