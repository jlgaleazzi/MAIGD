using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Player;
using Biplanes.UI;
using Biplanes.Weapons;

namespace Biplanes.Tests {

    /// <summary>
    /// T941 (QA): the T937 state flow and time scale, and the T937 crash path against the B025
    /// plane-collision path (both live in PlayerPlane).
    ///  - Pause freezes the world and disables PlayerInput; Resume (or any exit from Paused)
    ///    restores timeScale 1 and the input. Pause only works while Playing.
    ///  - LoseMission(reason, delay) ends the mission at once (state Lost, MissionEnded once);
    ///    only the screen is delayed. Nothing can flip it to Won afterwards.
    ///  - Result Next opens the next mission's briefing with the world frozen.
    ///  - A crash turns the collider and the input off at once and loses the mission at once;
    ///    a contact callback that still arrives afterwards changes nothing.
    ///
    /// GameManager.Instance and FxManager.Instance lazily call DontDestroyOnLoad (Play mode
    /// only), so both are seeded by reflection with un-awakened components, as in
    /// MissionEndRulesTests. Views are not built outside Play mode, so only state is checked.
    /// The Menu state is never entered (EnterMenu builds MainMenuView even in EditMode).
    /// </summary>
    public class GameFlowTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<Camera> _disabledCameras = new List<Camera>();
        private readonly List<bool> _ended = new List<bool>();
        private readonly List<string> _reasons = new List<string>();
        private GameManager _gm;
        private float _timeScale;

        private void OnMissionEnded(bool won, string reason) {
            _ended.Add(won);
            _reasons.Add(reason);
        }

        [SetUp]
        public void SetUp() {
            _timeScale = Time.timeScale;

            foreach (var c in Camera.allCameras) {
                if (c != null && c.CompareTag("MainCamera")) {
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
            }
            var camGo = new GameObject("T941_Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.aspect = 16f / 9f;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            _created.Add(camGo);

            var gmGo = new GameObject("T941_GameManager");
            _created.Add(gmGo);
            _gm = gmGo.AddComponent<GameManager>();   // no Awake in EditMode: state Menu
            typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, _gm);

            var fxGo = new GameObject("T941_Fx");
            _created.Add(fxGo);
            typeof(Biplanes.Fx.FxManager).GetField("_instance", PrivateStatic)
                .SetValue(null, fxGo.AddComponent<Biplanes.Fx.FxManager>());

            _ended.Clear();
            _reasons.Clear();
            GameEvents.MissionEnded += OnMissionEnded;
        }

        [TearDown]
        public void TearDown() {
            GameEvents.MissionEnded -= OnMissionEnded;

            foreach (var p in Object.FindObjectsByType<PlayerPlane>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (p != null) Object.DestroyImmediate(p.gameObject);
            }
            foreach (var go in _created) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();

            // PlayerPlane.Create builds the bullet and bomb pools.
            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (p != null) Object.DestroyImmediate(p.gameObject);
            }
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (go != null && go.hideFlags == HideFlags.HideAndDontSave && go.name.StartsWith("Pool<")) {
                    Object.DestroyImmediate(go);
                }
            }

            typeof(GameManager).GetField("_instance", PrivateStatic).SetValue(null, null);
            typeof(Biplanes.Fx.FxManager).GetField("_instance", PrivateStatic).SetValue(null, null);

            foreach (var c in _disabledCameras) {
                if (c != null) c.enabled = true;
            }
            _disabledCameras.Clear();
            Time.timeScale = _timeScale;
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static void Invoke(object target, string method, params object[] args) {
            var m = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found by reflection");
            m.Invoke(target, args);
        }

        private PlayerPlane MakePlane(Vector2 position) {
            var plane = PlayerPlane.Create(new Vector3(position.x, position.y, 0f), false);
            _created.Add(plane.gameObject);
            typeof(Health).GetMethod("Awake", Private).Invoke(plane.GetComponent<Health>(), null);
            plane.transform.position = new Vector3(position.x, position.y, 0f);
            plane.GetComponent<Rigidbody2D>().position = position;
            Physics2D.SyncTransforms();
            return plane;
        }

        private FighterAI MakeFighter(Vector2 position) {
            var f = FighterAI.Spawn(position);
            _created.Add(f.gameObject);
            typeof(Health).GetMethod("Awake", Private).Invoke(f.GetComponent<Health>(), null);
            Invoke(f, "Awake");
            return f;
        }

        // ── Pause / resume ──────────────────────────────────────────────

        [Test]
        public void Pause_FreezesWorld_DisablesInput_ResumeRestoresBoth() {
            var plane = MakePlane(new Vector2(0f, 1f));
            var input = plane.GetComponent<PlayerInput>();
            _gm.SetState(GameManager.GameState.Playing);
            Assert.AreEqual(1f, Time.timeScale);

            _gm.PauseGame();
            Assert.AreEqual(GameManager.GameState.Paused, _gm.CurrentState);
            Assert.AreEqual(0f, Time.timeScale, "pause freezes the world");
            Assert.IsFalse(input.enabled, "no move, fire or bomb while paused");

            _gm.ResumeGame();
            Assert.AreEqual(GameManager.GameState.Playing, _gm.CurrentState);
            Assert.AreEqual(1f, Time.timeScale, "resume unfreezes the world");
            Assert.IsTrue(input.enabled, "resume gives the controls back");
        }

        [Test]
        public void Pause_OnlyWhilePlaying() {
            _gm.SetState(GameManager.GameState.Playing);
            _gm.LoseMission("crash", 1f);
            _gm.PauseGame();
            Assert.AreEqual(GameManager.GameState.Lost, _gm.CurrentState, "no pause during the crash fall or on the result");

            _gm.ShowBriefing(2);
            _gm.PauseGame();
            Assert.AreEqual(GameManager.GameState.Briefing, _gm.CurrentState, "no pause on the briefing");

            _gm.ResumeGame();
            Assert.AreEqual(GameManager.GameState.Briefing, _gm.CurrentState, "resume only leaves Paused");
        }

        [Test]
        public void LeavingPaused_ForAnEnd_RestoresInputList_AndTime() {
            var plane = MakePlane(new Vector2(0f, 1f));
            var input = plane.GetComponent<PlayerInput>();
            _gm.SetState(GameManager.GameState.Playing);
            _gm.PauseGame();
            Assert.IsFalse(input.enabled);

            // A loss while paused (e.g. a late objective check) must not leave time frozen
            // with no screen: leaving Paused restores timeScale 1 before the Lost entry.
            _gm.LoseMission("late");
            Assert.AreEqual(GameManager.GameState.Lost, _gm.CurrentState);
            var list = (List<Behaviour>)typeof(GameManager).GetField("_pausedInputs", Private).GetValue(_gm);
            Assert.AreEqual(0, list.Count, "paused input list cleared when leaving Paused");
            Assert.IsTrue(input.enabled, "leaving Paused re-enables what pause disabled");
            Assert.AreEqual(1f, Time.timeScale, "leaving Paused restores time (the result screen then freezes it)");
        }

        // ── Mission end ────────────────────────────────────────────────

        [Test]
        public void LoseMission_WithResultDelay_EndsAtOnce_AndOnlyOnce() {
            _gm.SetState(GameManager.GameState.Playing);
            _gm.LoseMission(GameManager.DefaultLossReason, PlayerPlane.CrashFallSeconds);

            Assert.AreEqual(GameManager.GameState.Lost, _gm.CurrentState, "state is Lost immediately");
            CollectionAssert.AreEqual(new[] { false }, _ended, "MissionEnded fires immediately, once");
            Assert.AreEqual(GameManager.DefaultLossReason, _reasons[0]);
            Assert.AreEqual(1f, Time.timeScale, "the world keeps running while the wreck falls");

            _gm.SetState(GameManager.GameState.Won);
            _gm.LoseMission("again", 1f);
            Assert.AreEqual(GameManager.GameState.Lost, _gm.CurrentState);
            Assert.AreEqual(1, _ended.Count, "no second end");
        }

        [Test]
        public void Next_OpensBriefingOfNextMission_WorldFrozen() {
            _gm.SetState(GameManager.GameState.Playing);
            _gm.SetState(GameManager.GameState.Won);
            Time.timeScale = 0f;   // the result screen froze time

            _gm.NextMission(2);
            Assert.AreEqual(GameManager.GameState.Briefing, _gm.CurrentState);
            Assert.AreEqual(3, _gm.ActiveMissionIndex);
            Assert.AreEqual(0f, Time.timeScale, "the briefing freezes the finished world behind it");
            Assert.IsFalse(ResultView.IsShowing);
        }

        [Test]
        public void ShowBriefing_ClampsMissionIndex() {
            _gm.SetState(GameManager.GameState.Playing);
            _gm.SetState(GameManager.GameState.Won);
            _gm.ShowBriefing(9);
            Assert.AreEqual(GameManager.MissionCount, _gm.ActiveMissionIndex);
            _gm.ShowBriefing(0);
            Assert.AreEqual(1, _gm.ActiveMissionIndex, "already briefing: rebuilt for the new index");
            Assert.AreEqual(GameManager.GameState.Briefing, _gm.CurrentState);
        }

        // ── Crash (T937) vs plane collision (B025) ──────────────────────

        [Test]
        public void Crash_TurnsColliderAndInputOff_LosesAtOnce() {
            var plane = MakePlane(new Vector2(0f, 1f));
            _gm.SetState(GameManager.GameState.Playing);

            plane.ForceCrash();

            Assert.IsTrue(plane.HasCrashed);
            Assert.IsFalse(plane.GetComponent<Collider2D>().enabled, "the wreck can't be hit again");
            Assert.IsFalse(plane.GetComponent<PlayerInput>().enabled, "input off at once");
            Assert.AreEqual(GameManager.GameState.Lost, _gm.CurrentState);
            CollectionAssert.AreEqual(new[] { false }, _ended);
            Assert.AreEqual(GameManager.DefaultLossReason, _reasons[0]);
        }

        [Test]
        public void ContactAfterCrash_ChangesNothing() {
            var plane = MakePlane(new Vector2(0f, 1f));
            _gm.SetState(GameManager.GameState.Playing);
            plane.GetComponent<Health>().ApplyDamage(1f, DamageSource.Flak);
            float before = plane.GetComponent<Health>().CurrentDamagePercent;
            plane.ForceCrash();

            // A trigger callback still queued for this physics step after the collider went off.
            var fighter = MakeFighter(new Vector2(0.5f, 1f));
            Invoke(plane, "OnTriggerEnter2D", fighter.GetComponent<Collider2D>());

            Assert.AreEqual(before, plane.GetComponent<Health>().CurrentDamagePercent, 1e-4f, "no plane-collision damage on a wreck");
            Assert.IsFalse(fighter.IsDead, "the wreck doesn't ram");
            Assert.AreEqual(1, _ended.Count, "one end only");
        }

        [Test]
        public void SecondRam_Crashes_ThroughTheSameLossPath() {
            var plane = MakePlane(new Vector2(0f, 1f));
            _gm.SetState(GameManager.GameState.Playing);

            Invoke(plane, "OnTriggerEnter2D", MakeFighter(new Vector2(0.5f, 1f)).GetComponent<Collider2D>());
            Assert.IsFalse(plane.HasCrashed);
            Assert.AreEqual(GameManager.GameState.Playing, _gm.CurrentState, "a 50% ram is not a loss");
            Assert.IsTrue(plane.GetComponent<PlayerInput>().enabled, "the plane keeps flying after a hit");

            Invoke(plane, "TickPlaneCollisionGrace", PlayerPlane.PlaneCollisionGraceSeconds + 0.1f);
            Invoke(plane, "OnTriggerEnter2D", MakeFighter(new Vector2(0.5f, 1f)).GetComponent<Collider2D>());

            Assert.IsTrue(plane.HasCrashed);
            Assert.IsFalse(plane.GetComponent<Collider2D>().enabled);
            Assert.AreEqual(GameManager.GameState.Lost, _gm.CurrentState);
            CollectionAssert.AreEqual(new[] { false }, _ended);
        }

        [Test]
        public void FlakTo100_CrashesOnNextTick_NotBefore() {
            var plane = MakePlane(new Vector2(0f, 1f));
            _gm.SetState(GameManager.GameState.Playing);
            var health = plane.GetComponent<Health>();

            for (int i = 0; i < 6; i++) health.ApplyDamage(1f, DamageSource.Flak);   // 6 x 15 = 90
            Invoke(plane, "FixedUpdate");
            Assert.IsFalse(plane.HasCrashed, "90% still flies");
            Assert.AreEqual(GameManager.GameState.Playing, _gm.CurrentState);

            health.ApplyDamage(1f, DamageSource.Flak);                                   // 100 (clamped)
            Invoke(plane, "FixedUpdate");
            Assert.IsTrue(plane.HasCrashed);
            Assert.AreEqual(GameManager.GameState.Lost, _gm.CurrentState);
        }
    }
}
