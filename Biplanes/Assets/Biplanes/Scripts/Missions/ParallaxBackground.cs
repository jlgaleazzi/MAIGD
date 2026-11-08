using System.Collections.Generic;
using UnityEngine;
using Biplanes.Core;

namespace Biplanes.Missions {

    /// <summary>
    /// Three-layer parallax scenery for one mission (ARCHITECTURE §3 Missions → ParallaxBackground).
    ///
    /// Loads Sprites/backgrounds/bg_{theme}_{far|mid|near}. Each layer is two side-by-side
    /// tiles. In LateUpdate (after ScrollController moves the camera) each layer is placed with
    ///   offset = Mathf.Repeat(camX * f, w);  tile A x = camX - offset;  tile B x = camX - offset + w
    /// where f is the layer's parallax factor from Balance.json → background
    /// (0 = pinned to the screen, 1 = moves with the world) and w is the tile width.
    /// </summary>
    public class ParallaxBackground : MonoBehaviour {

        /// <summary>One parallax layer: two tiles that leapfrog as the camera scrolls.</summary>
        private class Layer {
            public SpriteRenderer a;
            public SpriteRenderer b;
            public float width;
            public float factor;
        }

        // §3 Draw order.
        private static readonly string[] LayerNames  = { "far", "mid", "near" };
        private static readonly int[]    LayerOrders = { -30, -20, -10 };

        // Used only if Balance.json has no "background" block (mirrors its defaults, §4).
        private static readonly float[] FallbackFactors = { 0.1f, 0.3f, 0.6f };

        private readonly List<Layer> _layers = new List<Layer>();
        private Camera _cam;

        // ── Factory ─────────────────────────────────────────────────────

        /// <summary>Builds the far/mid/near layers for <paramref name="theme"/> (e.g. "farmland").</summary>
        public static ParallaxBackground Create(string theme, Camera cam) {
            var go = new GameObject("ParallaxBackground");
            var bg = go.AddComponent<ParallaxBackground>();
            bg._cam = cam != null ? cam : Camera.main;

            if (string.IsNullOrEmpty(theme)) {
                Debug.LogWarning("ParallaxBackground: mission has no 'background' theme; no layers created.");
                return bg;
            }

            float[] factors = ReadFactors();
            for (int i = 0; i < LayerNames.Length; i++) {
                string path = $"Sprites/backgrounds/bg_{theme}_{LayerNames[i]}";
                var sprite = Resources.Load<Sprite>(path);
                if (sprite == null) {
                    Debug.LogWarning($"ParallaxBackground: missing sprite '{path}'; skipping the {LayerNames[i]} layer.");
                    continue;
                }

                var layer = new Layer {
                    a = bg.MakeTile($"{LayerNames[i]}_A", sprite, LayerOrders[i]),
                    b = bg.MakeTile($"{LayerNames[i]}_B", sprite, LayerOrders[i]),
                    width = sprite.bounds.size.x,
                    factor = factors[i]
                };
                if (layer.width <= 0f) layer.width = 1f; // guard against a degenerate sprite
                bg._layers.Add(layer);
            }

            bg.Reposition(); // correct placement on the very first frame
            return bg;
        }

        /// <summary>Reads far/mid/near factors from GameBalance, falling back to the §4 defaults.</summary>
        private static float[] ReadFactors() {
            var g = GameBalance.Get();
            // JsonUtility may fill a missing block with zeros instead of leaving it null.
            if (g == null || g.background == null
                || (g.background.farFactor == 0f && g.background.midFactor == 0f && g.background.nearFactor == 0f)) {
                Debug.LogWarning("ParallaxBackground: Balance.json has no 'background' block; using defaults.");
                return FallbackFactors;
            }
            return new[] { g.background.farFactor, g.background.midFactor, g.background.nearFactor };
        }

        private SpriteRenderer MakeTile(string name, Sprite sprite, int order) {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        // ── Scrolling ───────────────────────────────────────────────────

        private void LateUpdate() {
            Reposition();
        }

        private void Reposition() {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            Vector3 camPos = _cam.transform.position;
            foreach (var layer in _layers) {
                float offset = Mathf.Repeat(camPos.x * layer.factor, layer.width);
                float ax = camPos.x - offset;
                layer.a.transform.position = new Vector3(ax, camPos.y, 0f);
                layer.b.transform.position = new Vector3(ax + layer.width, camPos.y, 0f);
            }
        }
    }
}
