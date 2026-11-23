using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Missions;
using Biplanes.Player;

namespace Biplanes.Tests {

    /// <summary>
    /// B040 (QA T957 BUG 1, 2, 3, 5, 6, 7): the aircraft liveness check never casts a
    /// non-IDamageable (BomberAI) to IDamageable; reinforcements and re-entering fighters come
    /// in just past the RIGHT edge and fly left (B074; was the edge the player faces); escaped fighters
    /// re-enter while an open destroyAllOfType objective counts them and are removed otherwise;
    /// the fighter fallback health is 75 (three bullets).
    ///
    /// EditMode doesn't run Awake on AddComponent, so the FighterAI tests only use what the
    /// factory sets up.
    /// </summary>
    public class EnemyPressureFixTests {

        private const float Eps = 1e-4f;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        // 16:9 view, 10 units tall, centred on x = 100.
        private static readonly Rect View = new Rect(100f - 8.8889f, -5f, 17.7778f, 10f);

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown() {
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        private static void InvokeAwake(Component c) {
            var awake = c.GetType().GetMethod("Awake", Private);
            Assert.IsNotNull(awake, $"{c.GetType().Name}.Awake not found by reflection");
            awake.Invoke(c, null);
        }

        // ── BUG 3, superseded by B074: entry always from the right edge ──

        [Test]
        public void Entry_RightEdge_FlyingLeft() {
            // B074: there is no left entry any more, whatever the player's facing.
            Assert.AreEqual(-1, EnemyPressure.EntryFlightDirection);
            float x = EnemyPressure.EntryX(100f, 8.8889f, 1f);
            Assert.AreEqual(View.xMax + 1f, x, Eps);
        }

        [Test]
        public void Entry_StartsOffScreen_AndIsNeverAnEscape() {
            for (int i = 0; i < 4; i++) {
                float offset = EnemyPressure.EntryOffset(i, 1f);
                float x = EnemyPressure.EntryX(100f, 8.8889f, offset);
                Assert.IsFalse(EnemyPressure.InView(new Vector2(x, 0f), EnemyPressure.FighterHalfWidth,
                    EnemyPressure.AircraftHalfHeight, View), $"plane {i} spawns off screen");
                // Flying left toward the view with the camera standing still: closing, not escaped.
                float rel = EnemyPressure.EntryFlightDirection * 3f;
                Assert.IsFalse(EnemyPressure.IsEscapedOrLeft(x, rel, View, EnemyPressure.EscapeMargin));
                // With the entry boost (B074/B075) it closes at cruise speed even while the camera
                // runs left at 3.9: never an escape at its entry.
                int dir = EnemyPressure.EntryFlightDirection;
                float camV = -3.9f;
                float boosted = dir * (3f + FighterAI.EntryBoost(dir, camV)) - camV;
                Assert.Less(boosted, -EnemyPressure.ClosingSpeedEpsilon, "closing in the camera's frame");
                Assert.IsFalse(EnemyPressure.IsEscapedOrLeft(x, boosted, View, EnemyPressure.DespawnBehindMargin));
                // The planes of a default 2-plane wave (and every re-entry, plane 0) start inside
                // the 3-unit margin, so even with no relative motion they aren't removed (B075).
                if (i < 2) {
                    Assert.IsFalse(EnemyPressure.IsEscapedOrLeft(x, 0f, View, EnemyPressure.DespawnBehindMargin),
                        "a reinforcement sits inside its removal margin");
                    Assert.IsFalse(EnemyPressure.IsEscapedOrLeft(x, 0f, View, EnemyPressure.EscapeMargin),
                        "a re-entering fighter sits inside the escape margin");
                }
            }
        }

        // ── BUG 7: the first plane is on screen almost at once ───────────

        [Test]
        public void Entry_FirstPlane_OnScreenWithinAFifthOfASecond_AtFighterSpeed() {
            float worst = EnemyPressure.EntryOffset(0, 1f) - EnemyPressure.FighterHalfWidth;
            Assert.AreEqual(EnemyPressure.EntryGap + EnemyPressure.EntryJitter, worst, Eps);
            // Camera pinned (closing = fighter speed only, the slowest case): 0.5 / 3 s.
            var g = GameBalance.Get();
            float speed = g != null && g.enemySpeed != null && g.enemySpeed.fighter > 0f ? g.enemySpeed.fighter : 3f;
            Assert.LessOrEqual(worst / speed, 0.2f, "worst entry delay past the quiet limit");

            float x = EnemyPressure.EntryX(100f, 8.8889f, EnemyPressure.EntryOffset(0, 1f));
            Assert.IsTrue(EnemyPressure.InView(new Vector2(x - worst - 0.01f, 0f), EnemyPressure.FighterHalfWidth,
                EnemyPressure.AircraftHalfHeight, View), "after flying `worst` units it overlaps the view");
        }

        // ── BUG 2 + 6: escape test and decision ──────────────────────────

        [Test]
        public void Escape_PassedTheLeftEdge_MovingAway_IsEscaped() {
            // Player faces right (camera +2), fighter flies left (-3): -5 u/s relative.
            Assert.IsTrue(EnemyPressure.IsEscaped(View.xMin - 4f, -5f, View, EnemyPressure.EscapeMargin));
            Assert.IsFalse(EnemyPressure.IsEscaped(View.xMin - 2f, -5f, View, EnemyPressure.EscapeMargin),
                "inside the margin it may still turn around on screen");
        }

        [Test]
        public void Escape_PlayerChasesLeft_SlowerThanTheFighter_IsEscaped() {
            // Mission 1: camera -2, fighter -3: the gap grows by 1 u/s, it can never be caught.
            Assert.IsTrue(EnemyPressure.IsEscaped(View.xMin - 4f, -1f, View, EnemyPressure.EscapeMargin));
        }

        [Test]
        public void Escape_Backtracking_AtEqualSpeed_FighterBehind_IsEscaped() {
            // Missions 3-4: player faces left (camera -3), fighter right of the view flying left
            // at 3: it holds its distance forever (T957 BUG 3 pile-up).
            Assert.IsTrue(EnemyPressure.IsEscaped(View.xMax + 10f, 0f, View, EnemyPressure.EscapeMargin));
        }

        [Test]
        public void Escape_StillClosing_IsNotEscaped() {
            // About to enter from the right while the player faces right.
            Assert.IsFalse(EnemyPressure.IsEscaped(View.xMax + 25f, -5f, View, EnemyPressure.EscapeMargin));
            // Behind on the left but catching up.
            Assert.IsFalse(EnemyPressure.IsEscaped(View.xMin - 10f, 1f, View, EnemyPressure.EscapeMargin));
            // On screen.
            Assert.IsFalse(EnemyPressure.IsEscaped(100f, -5f, View, EnemyPressure.EscapeMargin));
        }

        [Test]
        public void Escape_Reinforcement_RemovedThreeUnitsPastTheRightEdge_WhenNotClosing() {
            // B075 (QA T975 BUG-1): the old 20-unit margin let reinforcements that fell behind a
            // left-flying player linger off the right edge and return all at once after a turn.
            Assert.AreEqual(EnemyPressure.EscapeMargin, EnemyPressure.DespawnBehindMargin, Eps);
            Assert.IsFalse(EnemyPressure.IsEscapedOrLeft(View.xMax + 2.9f, 0.25f, View, EnemyPressure.DespawnBehindMargin));
            Assert.IsTrue(EnemyPressure.IsEscapedOrLeft(View.xMax + 3.1f, 0.25f, View, EnemyPressure.DespawnBehindMargin));
            Assert.IsFalse(EnemyPressure.IsEscapedOrLeft(View.xMax + 3.1f, -1f, View, EnemyPressure.DespawnBehindMargin),
                "still closing: it is coming in, not leaving");
        }

        [Test]
        public void DecideEscape_WrapWhileNeeded_OtherwiseRemove() {
            Assert.AreEqual(EnemyPressure.EscapeAction.None, EnemyPressure.DecideEscape(false, false, true));
            Assert.AreEqual(EnemyPressure.EscapeAction.None, EnemyPressure.DecideEscape(false, true, false));
            Assert.AreEqual(EnemyPressure.EscapeAction.ReEnter, EnemyPressure.DecideEscape(true, false, true),
                "a JSON fighter an open destroy-all needs comes back");
            Assert.AreEqual(EnemyPressure.EscapeAction.Remove, EnemyPressure.DecideEscape(true, false, false),
                "not needed: removed silently");
            Assert.AreEqual(EnemyPressure.EscapeAction.Remove, EnemyPressure.DecideEscape(true, true, true),
                "reinforcements never re-enter");
        }

        // ── BUG 1: liveness without casting ──────────────────────────────

        [Test]
        public void IsLiveAircraft_NonDamageableWithHealth_UsesHealth() {
            var go = new GameObject("T_B040_aircraft");
            _created.Add(go);
            var tagComp = go.AddComponent<TeamTag>();   // a MonoBehaviour that is not IDamageable
            tagComp.team = Team.Enemy;
            var health = go.AddComponent<Health>();
            health.SetMaxHitPoints(10f);

            Assert.IsFalse(tagComp is IDamageable, "precondition");
            Assert.IsTrue(EnemyPressure.IsLiveAircraft(tagComp));
            health.ApplyDamage(10f, DamageSource.Collision);
            Assert.IsTrue(health.IsDead, "precondition");
            Assert.IsFalse(EnemyPressure.IsLiveAircraft(tagComp));
        }

        [Test]
        public void IsLiveAircraft_NullAndNoHealth() {
            Assert.IsFalse(EnemyPressure.IsLiveAircraft(null));
            var go = new GameObject("T_B040_bare");
            _created.Add(go);
            var bare = go.AddComponent<TeamTag>();
            Assert.IsTrue(EnemyPressure.IsLiveAircraft(bare), "no Health: counts as alive");
        }

        [Test]
        public void IsLiveAircraft_BomberAndFighter_NoException() {
            var bomber = BomberAI.Spawn(new Vector3(0f, 1f, 0f), "bomber_1");
            _created.Add(bomber.gameObject);
            var fighter = FighterAI.Spawn(new Vector3(2f, 1f, 0f));
            _created.Add(fighter.gameObject);
            bool b = false, f = false;
            Assert.DoesNotThrow(() => b = EnemyPressure.IsLiveAircraft(bomber));
            Assert.DoesNotThrow(() => f = EnemyPressure.IsLiveAircraft(fighter));
            Assert.IsTrue(b);
            Assert.IsTrue(f);
        }

        // ── FighterAI direction, re-entry and fallback (BUG 3, 2, 5) ─────

        [Test]
        public void Fighter_FlyingRight_NoseAndVelocityPointRight() {
            var f = FighterAI.Spawn(new Vector3(0f, 1f, 0f));
            _created.Add(f.gameObject);
            // T958: without Awake the SpriteRenderer isn't cached and SetFlightDirection can't
            // mirror it (the flipX asserts below would pass or fail vacuously).
            InvokeAwake(f.GetComponent<Health>());
            InvokeAwake(f);
            f.GetComponent<SpriteRenderer>().flipX = true; // FighterAI.Start for a left flier
            Assert.AreEqual(-1, f.FlightDirection, "default: flies left");
            Assert.Less(f.NoseDirection.x, 0f);
            Assert.Less(f.WorldVelocityX, 0f);

            f.SetFlightDirection(1);
            Assert.AreEqual(1, f.FlightDirection);
            Assert.Greater(f.NoseDirection.x, 0f);
            Assert.Greater(f.WorldVelocityX, 0f);
            Assert.IsFalse(f.GetComponent<SpriteRenderer>().flipX, "right-facing art is not mirrored");

            f.SetFlightDirection(-5);
            Assert.AreEqual(-1, f.FlightDirection);
            Assert.IsTrue(f.GetComponent<SpriteRenderer>().flipX);
        }

        [Test]
        public void Fighter_ReEnter_KeepsNameAndDamage() {
            var f = FighterAI.Spawn(new Vector3(0f, 1f, 0f));
            _created.Add(f.gameObject);
            var h = f.GetComponent<Health>();
            h.SetMaxHitPoints(75f);
            h.ApplyDamage(25f, DamageSource.Bullet);
            float before = h.CurrentDamagePercent;

            f.ReEnter(new Vector3(-50f, 2f, 0f), 1);

            Assert.AreEqual("fighter", f.gameObject.name, "still a JSON fighter for destroy-all");
            Assert.AreEqual(before, h.CurrentDamagePercent, Eps, "same damage");
            Assert.AreEqual(-50f, f.transform.position.x, Eps);
            Assert.AreEqual(2f, f.transform.position.y, Eps);
            Assert.AreEqual(1, f.FlightDirection);
        }

        [Test]
        public void Fighter_FallbackHitPoints_IsThreeBullets() {
            var field = typeof(FighterAI).GetField("FallbackHitPoints", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "FighterAI.FallbackHitPoints not found");
            Assert.AreEqual(75f, (float)field.GetValue(null), Eps);
        }

        // ── T958: re-entry keeps the count exact; escorts are exempt ─────

        [Test]
        public void ReEnteredFighter_KilledOnce_RaisesOneEntityDestroyed_SilentRemovalRaisesNone() {
            var f = FighterAI.Spawn(new Vector3(0f, 1f, 0f));
            _created.Add(f.gameObject);
            InvokeAwake(f.GetComponent<Health>());
            InvokeAwake(f);
            var balance = GameBalance.Get();
            float bullet = balance.player.bulletDamage;

            int events = 0;
            string lastType = null;
            System.Action<string, Team> onDestroyed = (type, team) => { events++; lastType = type; };
            GameEvents.EntityDestroyed += onDestroyed;
            try {
                f.ApplyDamage(bullet, DamageSource.Bullet);            // 1st hit before it escapes
                f.ReEnter(new Vector3(-40f, 2f, 0f), 1);
                f.ReEnter(new Vector3(40f, 2f, 0f), -1);               // escapes and comes back twice
                Assert.AreEqual(0, events, "re-entry is not a kill");
                f.ApplyDamage(bullet, DamageSource.Bullet);
                Assert.IsFalse(f.IsDead, "damage carried over: 2 of 3 hits");
                f.ApplyDamage(bullet, DamageSource.Bullet);
                Assert.IsTrue(f.IsDead, "the 3rd hit across re-entries kills it");
                f.ApplyDamage(bullet, DamageSource.Bullet);
                Assert.AreEqual(1, events, "exactly one EntityDestroyed: no double count");
                Assert.AreEqual("fighter", lastType, "counted as a JSON fighter for destroy-all");

                var g = FighterAI.Spawn(new Vector3(0f, 1f, 0f));
                InvokeAwake(g.GetComponent<Health>());
                InvokeAwake(g);
                Object.DestroyImmediate(g.gameObject);                   // EnemyPressure's silent removal
                Assert.AreEqual(1, events, "a removed fighter is never a kill");
            } finally {
                GameEvents.EntityDestroyed -= onDestroyed;
            }
        }

        [Test]
        public void Escort_WithLiveTarget_IsExempt_ChasesThePlayerWhenTheTargetIsGone() {
            // B059 (QA T966): WorldVelocityX is the escort's measured formation velocity while it
            // holds formation (0 before it has moved), and the real chase velocity once its
            // target is gone (it is then an ordinary chasing fighter, no longer exempt).
            var target = new GameObject("bomber_target");
            _created.Add(target);
            target.transform.position = new Vector3(0f, 1f, 0f);
            var player = MakePlayer(new Vector2(-30f, 1f)); // far beyond the leash, to the left
            var f = FighterAI.Spawn(new Vector3(0f, 1f, 0f));
            _created.Add(f.gameObject);
            InvokeAwake(f.GetComponent<Health>());
            InvokeAwake(f);
            SetPlayerRef(f, player);
            f.Configure("escort", "bomber_1");
            var field = typeof(FighterAI).GetField("_escortTarget", Private);
            Assert.IsNotNull(field, "FighterAI._escortTarget not found by reflection");
            field.SetValue(f, target.transform);

            Assert.AreEqual("escort", f.Pattern);
            Assert.IsTrue(f.IsEscorting, "live target: escorting (EnemyPressure skips it)");
            Assert.IsFalse(f.IsEscortChasing, "player beyond the leash: formation");
            Assert.AreEqual(0f, f.WorldVelocityX, Eps, "formation velocity before it has moved");

            // B061: it starts on the target and first settles in its own formation slot (slot 0:
            // above, behind a plane that flies left by default).
            for (int i = 0; i < 50; i++) ChaseTick(f);
            Assert.AreEqual(0, f.EscortSlot);
            Vector2 slot = (Vector2)target.transform.position
                + FighterAI.FormationOffset(0, 1, FighterAI.FormationDefaultHalfHeight);
            Assert.AreEqual(slot.x, f.transform.position.x, 0.01f, "settled in its slot (x)");
            Assert.AreEqual(slot.y, f.transform.position.y, 0.01f, "settled in its slot (y)");
            Assert.AreEqual(0f, f.WorldVelocityX, 1e-3f, "a still target: formation velocity 0");

            // The escorted plane moves 0.02 u per tick (1 u/s) to the right: the slot moves to its
            // new tail side, the escort reaches it and then follows at the plane's speed.
            for (int i = 0; i < 100; i++) {
                target.transform.position += new Vector3(0.02f, 0f, 0f);
                ChaseTick(f);
            }
            Assert.IsTrue(f.IsEscorting);
            Assert.IsFalse(f.IsEscortChasing);
            slot = (Vector2)target.transform.position
                + FighterAI.FormationOffset(0, -1, FighterAI.FormationDefaultHalfHeight);
            Assert.AreEqual(slot.x, f.transform.position.x, 0.01f, "slot behind a plane flying right");
            Assert.AreEqual(1f, f.WorldVelocityX, 1e-3f, "measured formation velocity (moves with its target)");
            Assert.AreEqual(1, f.FlightDirection, "faces the way it travels (never tail-first)");

            Object.DestroyImmediate(target);
            Assert.IsFalse(f.IsEscorting, "target gone: no longer exempt");
            // It leaves at its formation velocity and accelerates toward the player (accel-limited).
            for (int i = 0; i < 100; i++) {
                float x0 = f.transform.position.x;
                ChaseTick(f);
                Assert.AreEqual((f.transform.position.x - x0) / ChaseDt, f.WorldVelocityX, 1e-3f,
                    $"tick {i}: WorldVelocityX is the real chase velocity");
            }
            Assert.Less(f.WorldVelocityX, 0f, "chases the player on its left");
            Assert.AreEqual(-1, f.FlightDirection, "faces the player");
        }

        [Test]
        public void ReEntry_ClosesOnTheView_ThenChasesOnScreen_InMission1() {
            // Mission 1: the cruise (scrollSpeed) is slower than a fighter, so a re-entered
            // fighter closes on the view at entry and never escapes again (no re-entry loop).
            // B074: it always re-enters past the RIGHT edge flying left, with the entry boost
            // (FighterAI.BeginEntry) while the camera scrolls left.
            // Facing right (B061, QA T967): it can't back off tail-first, so it flies at the
            // player (down to MinForwardSpeed), is passed, turns and then holds its standoff
            // BEHIND the player, with its centre on screen (standoffMax 5 < the 5.33 u from the
            // player to the trailing edge), so it can fire.
            // Facing left: it comes in behind the player and holds its standoff there without
            // turning (the player sits at 0.7 of the view, 5.33 u from the right edge).
            var g = GameBalance.Get();
            float fighter = g.enemySpeed.fighter;
            float scroll = LoadMission(1).scrollSpeed;
            Assert.Less(scroll, fighter, "precondition: mission 1 cruises slower than a fighter");
            var chase = FighterChaseData.Resolve(g);
            float anchor = g.player.screenAnchor > 0f ? g.player.screenAnchor : 0.3f;

            for (int facing = -1; facing <= 1; facing += 2) {
                float x = EnemyPressure.EntryX(100f, HalfW, EnemyPressure.EntryOffset(0, 1f));
                int dir = EnemyPressure.EntryFlightDirection;
                float vx = dir * (fighter + FighterAI.EntryBoost(dir, facing * scroll));
                float rel = vx - facing * scroll;
                Assert.GreaterOrEqual(Mathf.Abs(rel), fighter - scroll - Eps);
                Assert.IsFalse(EnemyPressure.IsEscapedOrLeft(x, rel, View, EnemyPressure.EscapeMargin),
                    $"facing {facing}: a fresh re-entry is closing, not escaped");
                Assert.AreEqual(-1, System.Math.Sign(rel), "it moves left, toward the view");

                foreach (float standoff in new[] { chase.standoff, chase.standoffMax }) {
                    var r = SimulateChase(x, vx, facing, scroll, anchor, standoff, 60f);
                    Assert.AreEqual(-1f, r.EscapedAt, $"facing {facing}, standoff {standoff}: never escapes");
                    Assert.IsTrue(r.InViewAtEnd, $"facing {facing}, standoff {standoff}: on screen after 60 s");
                    Assert.IsTrue(r.CentreInViewAtEnd, $"facing {facing}, standoff {standoff}: centre on screen (may fire)");
                    Assert.AreEqual(standoff, r.GapAtEnd, 0.1f, $"facing {facing}: holds its standoff behind the player");
                    Assert.AreEqual(facing * scroll, r.VxAtEnd, 0.05f, "matches the cruise, nose first");
                    Assert.AreEqual(facing > 0, r.Passed,
                        $"facing {facing}, standoff {standoff}: facing right it is passed and turns; facing left it chases from behind");
                }
            }
        }

        [Test]
        public void ReEntry_FasterCruise_FighterFallsBehindAndEscapes_AfterAFullPass_NoRapidLoop() {
            // Missions 2-4 (QA T966): the player cruises faster than a fighter. A re-entered
            // fighter comes on screen ahead, is overtaken, turns, chases flat out, falls behind
            // and escapes again (re-enter or removal per B040). One cycle takes several seconds and
            // most of it is on screen, so re-entry can't loop off screen. B074: it now escapes as
            // soon as it is wholly past the left edge (LeftExitMargin) instead of drifting 3 u
            // further off screen, so a cycle is shorter (about 6.8 s at 3.9 u/s) and almost all
            // on screen.
            var g = GameBalance.Get();
            float fighter = g.enemySpeed.fighter;
            var chase = FighterChaseData.Resolve(g);
            float anchor = g.player.screenAnchor > 0f ? g.player.screenAnchor : 0.3f;
            foreach (int mission in new[] { 2, 3, 4 }) {
                float scroll = LoadMission(mission).scrollSpeed;
                Assert.Greater(scroll, fighter, $"precondition: mission {mission} cruises faster than a fighter");
                foreach (float standoff in new[] { chase.standoff, chase.standoffMax }) {
                    float x = EnemyPressure.EntryX(100f, HalfW, EnemyPressure.EntryOffset(0, 1f));
                    var r = SimulateChase(x, -fighter, 1, scroll, anchor, standoff, 300f);
                    Assert.Greater(r.EscapedAt, 0f, $"M{mission} standoff {standoff}: the overtaken fighter escapes");
                    Assert.Greater(r.EscapedAt, 6f, $"M{mission}: one re-entry cycle lasts several seconds");
                    Assert.Greater(r.SecondsInView / r.EscapedAt, 0.5f, $"M{mission}: on screen most of the cycle");
                    Assert.IsTrue(r.Passed, $"M{mission}: it was overtaken and turned to chase");
                }
            }
        }

        // ── B059 chase model helpers (QA T966) ───────────────────────────

        private const float ChaseDt = 0.02f;
        private const float HalfW = 8.8889f;

        private struct ChaseResult {
            public float EscapedAt;      // seconds until IsEscaped, -1 = never
            public float SecondsInView;
            public bool InViewAtEnd;
            public float GapAtEnd;       // (player x - fighter x) along the facing: > 0 = behind the player
            public bool CentreInViewAtEnd; // centre inside the view (EnemyBullet.IsOnScreen)
            public float VxAtEnd;
            public bool Passed;          // the fighter turned around to chase
        }

        /// <summary>1-D chase model with the B059 pure helpers: camera and player cruise at
        /// `scroll` along `facing`, the player at its trailing screen anchor; the fighter starts
        /// at `x0` with `vx0`, facing its flight direction, and steers with DesiredFacing +
        /// HorizontalVelocityStep with the B061 forward floor (FighterAI.MinForwardSpeed), as
        /// ChaseStep does, with the B074 entry boost (FighterAI.EntryBoost until EntryComplete).
        /// Stops at the first IsEscapedOrLeft (EscapeMargin).</summary>
        private static ChaseResult SimulateChase(float x0, float vx0, int facing, float scroll, float anchor,
                float standoff, float seconds) {
            var chase = FighterChaseData.Resolve(GameBalance.Get());
            float fighterSpeed = GameBalance.Get().enemySpeed.fighter;
            float camX = 100f;
            float camV = facing * scroll;
            float anchorViewport = facing > 0 ? anchor : 1f - anchor;
            float x = x0, v = vx0;
            int dir = vx0 < 0f ? -1 : 1;
            int startDir = dir;
            bool entering = true;
            var r = new ChaseResult { EscapedAt = -1f };
            int ticks = Mathf.RoundToInt(seconds / ChaseDt);
            for (int i = 0; i < ticks; i++) {
                camX += camV * ChaseDt;
                float px = camX + (anchorViewport - 0.5f) * 2f * HalfW;
                if (entering && FighterAI.EntryComplete(x, camX - HalfW, camX + HalfW)) entering = false;
                float maxSpeed = fighterSpeed + (entering ? FighterAI.EntryBoost(dir, camV) : 0f);
                dir = FighterAI.DesiredFacing(dir, px - x, chase.turnDeadzone);
                if (dir != startDir) r.Passed = true;
                v = FighterAI.HorizontalVelocityStep(x, v, px, camV, dir, standoff, maxSpeed, chase.accel, ChaseDt,
                    FighterAI.MinForwardSpeed);
                x += v * ChaseDt;
                var view = new Rect(camX - HalfW, -5f, 2f * HalfW, 10f);
                bool inView = EnemyPressure.InView(new Vector2(x, 0f), EnemyPressure.FighterHalfWidth, 0.5f, view);
                if (inView) r.SecondsInView += ChaseDt;
                if (EnemyPressure.IsEscapedOrLeft(x, v - camV, view, EnemyPressure.EscapeMargin)) {
                    r.EscapedAt = (i + 1) * ChaseDt;
                    break;
                }
                r.InViewAtEnd = inView;
                r.GapAtEnd = facing * (px - x);
                r.CentreInViewAtEnd = x >= view.xMin && x <= view.xMax;
                r.VxAtEnd = v;
            }
            return r;
        }

        private static MissionData LoadMission(int index) {
            var text = Resources.Load<TextAsset>($"Missions/mission_{index:D2}");
            Assert.IsNotNull(text, $"mission_{index:D2}.json not found in Resources/Missions");
            var data = JsonUtility.FromJson<MissionData>(text.text);
            Assert.IsNotNull(data);
            return data;
        }

        private PlayerPlane MakePlayer(Vector2 position) {
            var go = new GameObject("player");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<TeamTag>().team = Team.Player;
            InvokeAwake(go.AddComponent<Health>());
            return go.AddComponent<PlayerPlane>();
        }

        private static void SetPlayerRef(FighterAI f, PlayerPlane plane) {
            var prop = typeof(FighterAI).GetProperty("PlayerRef", Private);
            Assert.IsNotNull(prop, "FighterAI.PlayerRef not found by reflection");
            prop.SetValue(f, plane);
        }

        /// <summary>One FighterAI.ChaseStep tick from the transform, written back (EditMode:
        /// MovePosition doesn't move the transform).</summary>
        private static void ChaseTick(FighterAI f) {
            var m = typeof(FighterAI).GetMethod("ChaseStep", Private);
            Assert.IsNotNull(m, "FighterAI.ChaseStep not found by reflection");
            f.transform.position = (Vector3)m.Invoke(f, new object[] { f.transform.position, ChaseDt });
        }

        // ── MissionRunner: which fighters an open destroy-all still needs ─

        [Test]
        public void Runner_OpenDestroyAllCounts_JsonFightersOnly_UntilComplete() {
            var go = new GameObject("T_B040_Runner");
            _created.Add(go);
            var runner = go.AddComponent<MissionRunner>();
            var tracker = go.AddComponent<ObjectiveTracker>();
            var objective = new ObjectiveEntry { id = "kill_all", type = "destroyAllOfType", targetType = "fighter" };
            var data = new MissionData {
                objectives = new[] { objective },
                spawns = new[] { new SpawnEntry { type = "fighter", count = 2 } }
            };
            tracker.Init(data.objectives, 0f, data.spawns);
            typeof(MissionRunner).GetField("_missionData", Private).SetValue(runner, data);
            typeof(MissionRunner).GetField("_objectiveTracker", Private).SetValue(runner, tracker);

            Assert.IsTrue(runner.ObjectivesOpen, "precondition");
            Assert.IsTrue(runner.OpenDestroyAllCounts("fighter"));
            Assert.IsTrue(runner.OpenDestroyAllCounts("Fighter"), "case-insensitive like ObjectiveTracker");
            Assert.IsFalse(runner.OpenDestroyAllCounts(ScoreKeeper.ReinforcementFighterKey));
            Assert.IsFalse(runner.OpenDestroyAllCounts("bomber"));

            tracker.OnEntityDestroyed("fighter", Team.Enemy);
            tracker.OnEntityDestroyed("fighter", Team.Enemy);
            Assert.IsTrue(objective.evaluated, "precondition: objective complete");
            Assert.IsFalse(runner.OpenDestroyAllCounts("fighter"), "complete: escaped fighters are removed");
        }
    }
}
