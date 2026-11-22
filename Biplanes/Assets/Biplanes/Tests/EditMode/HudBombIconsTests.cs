using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Biplanes.Core;
using Biplanes.Player;
using Biplanes.UI;

namespace Biplanes.Tests {

    /// <summary>
    /// B022: the HUD bomb row showed one white bar until the first bomb drop. These tests
    /// check that a freshly built HUD shows one separate, sprited, ink icon per bomb
    /// (36 px, 8 px gap, 16 px from the bottom-left), that BombsChanged greys out spent
    /// bombs, that BombBay raises its starting count, and that no HUD Image is a default
    /// white rect and no HUD Text lacks a font.
    ///
    /// EditMode doesn't run Awake/Start/OnDestroy on AddComponent, so they are invoked by
    /// reflection, the way Play mode would run them.
    /// </summary>
    public class HudBombIconsTests {

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _canvasGo;
        private HudView _hud;
        private GameObject _bayGo;

        [SetUp]
        public void SetUp() {
            _canvasGo = new GameObject("B022_Canvas", typeof(RectTransform));
            _canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _hud = _canvasGo.AddComponent<HudView>();
            Call(_hud, "Awake");
        }

        [TearDown]
        public void TearDown() {
            if (_hud != null) Call(_hud, "OnDestroy");   // unsubscribes the static events
            if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
            if (_bayGo != null) Object.DestroyImmediate(_bayGo);
        }

        private static void Call(object target, string method) {
            var m = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(m, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, null);
        }

        private static int Capacity() {
            var g = GameBalance.Get();
            return g != null && g.player != null && g.player.bombCapacity > 0 ? g.player.bombCapacity : 7;
        }

        private Image Icon(int i) {
            var t = _canvasGo.transform.Find("BombRow/BombIcon_" + i);
            Assert.IsNotNull(t, "BombIcon_" + i + " missing");
            return t.GetComponent<Image>();
        }

        [Test]
        public void FreshHud_ShowsFullBombBay_BeforeAnyEvent() {
            int cap = Capacity();
            Assert.AreEqual(7, cap, "GDD: 7 bombs per mission");
            Assert.AreEqual(cap, _hud.BombSlots);
            Assert.AreEqual(cap, _hud.DisplayedBombs);
            for (int i = 0; i < cap; i++) {
                var img = Icon(i);
                Assert.IsNotNull(img.sprite, "icon " + i + " has no sprite (draws a white rect)");
                Assert.AreEqual(HudView.BombAvailableCol, img.color, "icon " + i + " not filled ink at start");
            }
        }

        [Test]
        public void BombIcons_AreSeparate36pxSlots_With8pxGap_BottomLeft() {
            var row = (RectTransform)_canvasGo.transform.Find("BombRow");
            Assert.AreEqual(Vector2.zero, row.anchorMin);
            Assert.AreEqual(new Vector2(16f, 16f), row.anchoredPosition);
            for (int i = 0; i < _hud.BombSlots; i++) {
                var r = (RectTransform)Icon(i).transform;
                Assert.AreEqual(36f, r.rect.width, 0.01f, "icon " + i + " width");
                Assert.AreEqual(36f, r.rect.height, 0.01f, "icon " + i + " height");
                Assert.AreEqual(i * 44f, r.anchoredPosition.x, 0.01f, "icon " + i + " x (36 px + 8 px gap)");
            }
        }

        [Test]
        public void BombsChanged_GreysSpentIcons_AndRefillRestores() {
            GameEvents.RaiseBombsChanged(4);
            Assert.AreEqual(4, _hud.DisplayedBombs);
            for (int i = 0; i < _hud.BombSlots; i++) {
                Assert.AreEqual(i < 4 ? HudView.BombAvailableCol : HudView.BombSpentCol, Icon(i).color, "icon " + i);
            }
            GameEvents.RaiseBombsChanged(_hud.BombSlots);
            Assert.AreEqual(_hud.BombSlots, _hud.DisplayedBombs);
        }

        [Test]
        public void BombBayStart_RaisesFullCount_SoHudResetsWithoutADrop() {
            GameEvents.RaiseBombsChanged(0);   // a stale count from a previous mission
            Assert.AreEqual(0, _hud.DisplayedBombs);

            _bayGo = new GameObject("B022_BombBay");
            var bay = _bayGo.AddComponent<BombBay>();
            Call(bay, "Awake");
            Call(bay, "Start");

            Assert.AreEqual(Capacity(), bay.BombsLeft);
            Assert.AreEqual(Capacity(), _hud.DisplayedBombs);
        }

        [Test]
        public void NoHudImageLacksASprite_AndNoTextLacksAFont() {
            foreach (var img in _canvasGo.GetComponentsInChildren<Image>(true)) {
                Assert.IsNotNull(img.sprite, img.name + " has no sprite (default white rect)");
            }
            foreach (var txt in _canvasGo.GetComponentsInChildren<Text>(true)) {
                Assert.IsNotNull(txt.font, txt.name + " has no font (draws nothing)");
            }
        }

        // ── T957: white-only HUD (B038b) ──────────────────────────────────

        private Image Fill() {
            return _canvasGo.transform.Find("DamageFrame/DamageFill").GetComponent<Image>();
        }

        private static bool IsPalette(Color c) {
            Color[] palette = { HudView.InkCol, HudView.PencilCol, HudView.PaperCol, Color.white };
            foreach (var p in palette) {
                if (Mathf.Abs(c.r - p.r) < 0.002f && Mathf.Abs(c.g - p.g) < 0.002f
                    && Mathf.Abs(c.b - p.b) < 0.002f) return true;
            }
            return false;
        }

        [Test]
        public void BombColours_AreInkAndHalfPencil() {
            Assert.AreEqual(HudView.InkCol, HudView.BombAvailableCol);
            Assert.AreEqual(HudView.PencilCol.r, HudView.BombSpentCol.r, 0.0001f);
            Assert.AreEqual(HudView.PencilCol.g, HudView.BombSpentCol.g, 0.0001f);
            Assert.AreEqual(HudView.PencilCol.b, HudView.BombSpentCol.b, 0.0001f);
            Assert.AreEqual(0.5f, HudView.BombSpentCol.a, 0.0001f);
        }

        [Test]
        public void DamageBelowSmoke_SolidInk_NoFlash_AtSmoke_HatchedAndFlashing() {
            var solid = Fill().sprite;
            GameEvents.RaisePlayerDamageChanged(69f);
            Assert.IsFalse(_hud.DamageFlashing, "69% must not flash");
            Assert.AreSame(solid, Fill().sprite, "69% keeps the solid fill sprite");
            Assert.AreEqual(1f, Fill().color.a, 0.0001f);
            Assert.IsFalse(_canvasGo.transform.Find("SmokeWarning").gameObject.activeSelf);

            GameEvents.RaisePlayerDamageChanged(70f);
            Assert.IsTrue(_hud.DamageFlashing, "GDD: 70% starts the smoke warning");
            Assert.AreNotSame(solid, Fill().sprite, "70% swaps to the hatched sprite");
            Assert.IsTrue(_canvasGo.transform.Find("SmokeWarning").gameObject.activeSelf);

            GameEvents.RaisePlayerDamageChanged(50f);
            Assert.IsFalse(_hud.DamageFlashing);
            Assert.AreSame(solid, Fill().sprite);
            Assert.AreEqual(1f, Fill().color.a, 0.0001f, "back below smoke: full alpha");
        }

        [Test]
        public void FlashAlpha_StartsAtOne_DipsToPoint4_At3Hz() {
            Assert.AreEqual(1f, HudView.FlashAlpha(0f), 0.0001f);
            Assert.AreEqual(0.4f, HudView.FlashAlpha(1f / 6f), 0.001f);
            Assert.AreEqual(1f, HudView.FlashAlpha(1f / 3f), 0.001f);
        }

        [Test]
        public void EveryHudGraphic_IsInkPencilPaperOrNoTint() {
            GameEvents.RaisePlayerDamageChanged(80f);
            GameEvents.RaiseBombsChanged(2);
            foreach (var g in _canvasGo.GetComponentsInChildren<Graphic>(true)) {
                Assert.IsTrue(IsPalette(g.color), g.name + " colour " + g.color + " is outside ink/pencil/paper");
            }
        }

        [Test]
        public void DamageFill_StartsEmpty() {
            var fill = _canvasGo.transform.Find("DamageFrame/DamageFill").GetComponent<Image>();
            Assert.AreEqual(Image.Type.Filled, fill.type);
            Assert.AreEqual(0f, fill.fillAmount, 0.0001f);
        }
    }
}
