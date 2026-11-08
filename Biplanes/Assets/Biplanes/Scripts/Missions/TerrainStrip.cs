using System.Collections.Generic;
using UnityEngine;

namespace Biplanes.Missions {

    /// <summary>
    /// The ground for one mission (ARCHITECTURE §3 Missions → TerrainStrip).
    ///
    /// - HeightAt(x) returns the world y of the ground surface, interpolated linearly
    ///   between the mission's terrain keyframes on top of the -4.5 baseline.
    /// - Drawn with SpriteRenderers only (no meshes, materials or Shader.Find): the strip
    ///   is cut into 20-unit chunks, each a runtime Texture2D at 32 px per unit.
    ///   Only chunks near the camera exist.
    /// - Has no collider. PlayerPlane and Bomb compare their y against HeightAt.
    /// </summary>
    public class TerrainStrip : MonoBehaviour {

        // ── Contract constants (ARCHITECTURE §3) ─────────────────────────

        /// <summary>World y of the ground baseline (terrain height 0).</summary>
        public const float GroundBaseY = -4.5f;

        private const float ChunkWidth      = 20f;    // world units per chunk
        private const int   PixelsPerUnit   = 32;     // texture resolution
        private const float ChunkBottomY    = -5f;    // camera bottom edge
        private const float ChunkTopPadding = 0.5f;   // space above the highest point
        private const int   RidgePixels     = 2;      // pencil line under the surface
        private const float KeepBehind      = 10f;    // keep chunks this far left of camera
        private const float KeepAhead       = 30f;    // and this far right of camera
        private const int   SortingOrder    = 0;      // §3 Draw order: terrain

        // ART_STYLE palette.
        private static readonly Color32 Paper  = new Color32(0x0F, 0x2A, 0x4A, 0xFF); // #0F2A4A
        private static readonly Color32 Pencil = new Color32(0x9D, 0xB4, 0xCC, 0xFF); // #9DB4CC
        private static readonly Color32 Clear  = new Color32(0, 0, 0, 0);

        // ── Singleton ───────────────────────────────────────────────────

        /// <summary>The current mission's ground; null outside a mission.</summary>
        public static TerrainStrip Instance { get; private set; }

        // ── State ───────────────────────────────────────────────────────

        private TerrainPoint[] _points = new TerrainPoint[0];   // sorted by atX
        private float _length;                                   // mission length (informational)
        private Camera _cam;

        /// <summary>Live chunks keyed by chunk index (chunk i covers [i*20, (i+1)*20)).</summary>
        private readonly Dictionary<int, GameObject> _chunks = new Dictionary<int, GameObject>();
        private readonly List<int> _toRemove = new List<int>();

        // ── Factory ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates the ground for a mission from its terrain keyframes. The chunks around
        /// the camera are built immediately, so the first frame already shows ground;
        /// after that at most one chunk is built per frame.
        /// </summary>
        public static TerrainStrip Create(TerrainPoint[] points, float length) {
            var go = new GameObject("TerrainStrip");
            var strip = go.AddComponent<TerrainStrip>();

            var sorted = new List<TerrainPoint>();
            if (points != null) {
                foreach (var p in points) {
                    if (p != null) sorted.Add(p);
                }
            }
            sorted.Sort((a, b) => a.atX.CompareTo(b.atX));
            strip._points = sorted.ToArray();
            strip._length = length;
            strip._cam = Camera.main;

            Instance = strip;

            // Initial window, built synchronously.
            float camX = strip.CameraX();
            int first = ChunkIndex(camX - KeepBehind);
            int last  = ChunkIndex(camX + KeepAhead);
            for (int i = first; i <= last; i++) strip.BuildChunk(i);

            return strip;
        }

        // ── Public API ──────────────────────────────────────────────────

        /// <summary>World y of the ground surface at world x.</summary>
        public float HeightAt(float x) {
            if (_points == null || _points.Length == 0) return GroundBaseY;

            if (x <= _points[0].atX) return GroundBaseY + _points[0].height;

            for (int i = 1; i < _points.Length; i++) {
                if (x <= _points[i].atX) {
                    var a = _points[i - 1];
                    var b = _points[i];
                    float span = b.atX - a.atX;
                    if (span <= 0f) return GroundBaseY + b.height;
                    float t = (x - a.atX) / span;
                    return GroundBaseY + Mathf.Lerp(a.height, b.height, t);
                }
            }

            return GroundBaseY + _points[_points.Length - 1].height;
        }

        /// <summary>Mission length in world units, as given to Create.</summary>
        public float Length => _length;

        // ── Lifecycle ───────────────────────────────────────────────────

        private void Update() {
            if (_cam == null) _cam = Camera.main;
            float camX = CameraX();
            int first = ChunkIndex(camX - KeepBehind);
            int last  = ChunkIndex(camX + KeepAhead);

            // Drop chunks that left the window.
            _toRemove.Clear();
            foreach (var kv in _chunks) {
                if (kv.Key < first || kv.Key > last) _toRemove.Add(kv.Key);
            }
            foreach (int i in _toRemove) DestroyChunk(i);

            // Build at most one missing chunk this frame.
            for (int i = first; i <= last; i++) {
                if (!_chunks.ContainsKey(i)) {
                    BuildChunk(i);
                    break;
                }
            }
        }

        private void OnDestroy() {
            _toRemove.Clear();
            _toRemove.AddRange(_chunks.Keys);
            foreach (int i in _toRemove) DestroyChunk(i);
            if (Instance == this) Instance = null;
        }

        // ── Chunks ──────────────────────────────────────────────────────

        private float CameraX() => _cam != null ? _cam.transform.position.x : 0f;

        private static int ChunkIndex(float x) => Mathf.FloorToInt(x / ChunkWidth);

        /// <summary>Paints chunk i into a runtime texture and shows it with a SpriteRenderer.</summary>
        private void BuildChunk(int index) {
            if (_chunks.ContainsKey(index)) return;

            float startX = index * ChunkWidth;
            int widthPx = Mathf.RoundToInt(ChunkWidth * PixelsPerUnit);

            // Sample the surface once per pixel column and find the highest point.
            var surfacePx = new int[widthPx];
            float topY = GroundBaseY;
            var surfaceY = new float[widthPx];
            for (int c = 0; c < widthPx; c++) {
                float worldX = startX + (c + 0.5f) / PixelsPerUnit;
                surfaceY[c] = HeightAt(worldX);
                if (surfaceY[c] > topY) topY = surfaceY[c];
            }
            int heightPx = Mathf.Max(1, Mathf.CeilToInt((topY + ChunkTopPadding - ChunkBottomY) * PixelsPerUnit));
            for (int c = 0; c < widthPx; c++) {
                surfacePx[c] = Mathf.RoundToInt((surfaceY[c] - ChunkBottomY) * PixelsPerUnit);
            }

            var pixels = new Color32[widthPx * heightPx];
            for (int r = 0; r < heightPx; r++) {
                int row = r * widthPx;
                for (int c = 0; c < widthPx; c++) {
                    int s = surfacePx[c];
                    Color32 col;
                    if (r >= s)                    col = Clear;
                    else if (r >= s - RidgePixels) col = Pencil;
                    else                           col = Paper;
                    pixels[row + c] = col;
                }
            }

            var tex = new Texture2D(widthPx, heightPx, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(pixels);
            tex.Apply(false, true); // upload, then free the CPU copy

            var sprite = Sprite.Create(tex, new Rect(0, 0, widthPx, heightPx), Vector2.zero, PixelsPerUnit);

            var go = new GameObject($"TerrainChunk_{index}");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(startX, ChunkBottomY, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = SortingOrder;

            _chunks[index] = go;
        }

        /// <summary>Destroys chunk i together with its sprite and texture.</summary>
        private void DestroyChunk(int index) {
            if (!_chunks.TryGetValue(index, out var go)) return;
            _chunks.Remove(index);
            if (go == null) return;

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) {
                var tex = sr.sprite.texture;
                Destroy(sr.sprite);
                if (tex != null) Destroy(tex);
            }
            Destroy(go);
        }
    }
}
