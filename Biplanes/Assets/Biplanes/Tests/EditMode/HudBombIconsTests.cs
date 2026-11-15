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
    /// check that a freshly built HUD shows one separate, sprited, friendly icon per bomb
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
                Assert.AreEqual(HudView.FriendlyCol, img.color, "icon " + i + " not friendly at start");
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
                Assert.AreEqual(i < 4 ? HudView.FriendlyCol : HudView.PencilCol, Icon(i).color, "icon " + i);
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

        [Test]
        public void DamageFill_StartsEmpty() {
            var fill = _canvasGo.transform.Find("DamageFrame/DamageFill").GetComponent<Image>();
            Assert.AreEqual(Image.Type.Filled, fill.type);
            Assert.AreEqual(0f, fill.fillAmount, 0.0001f);
        }
    }
}
