using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Biplanes.Core;
using Biplanes.Enemies;
using Biplanes.Missions;
using Biplanes.Player;

namespace Biplanes.UI {

    /// <summary>
    /// HUD minimap (B044, UI_SPEC §3.3.1, ARCHITECTURE §3 "Minimap"): a thin blueprint strip at
    /// the top centre showing the whole mission span, world x 0 (left) to the mission length
    /// (right), and the flight band from the lowest ground (bottom) to the flight ceiling (top).
    ///
    /// Draws, back to front: paper backing with an ink border, the terrain as a pencil
    /// polyline, upcoming JSON spawns as faint pencil ticks on the bottom edge, failAtX dashed
    /// ink lines and player HQ flags, the pencil camera-window bracket, live ground enemies
    /// (pencil), live enemy aircraft (ink), hollow ink diamonds on open objective targets, and
    /// the ink player triangle on top. Health balloons are never shown.
    ///
    /// Reads only, never writes: MissionRunner (length, spawn list and pending flags, live
    /// aircraft and ground enemies, objectives), PlayerPlane, the camera and TerrainStrip.
    /// Markers are pooled Images with small procedural white sprites built once and cached;
    /// positions refresh at 10 Hz on unscaled time while the game is Playing or Paused.
    /// Nothing here blocks clicks (raycastTarget off).
    /// </summary>
    public class MinimapView : MonoBehaviour {

        // ── Layout (UI pixels at the 1920x1080 reference, UI_SPEC §3.3.1) ─

        /// <summary>Frame width when the canvas has room for it.</summary>
        public const float RefWidth = 480f;
        /// <summary>Frame height.</summary>
        public const float FrameHeight = 44f;
        /// <summary>Distance from the top of the canvas.</summary>
        public const float TopMargin = 12f;
        /// <summary>Minimum clear gap between the frame and the corner widgets.</summary>
        public const float SideGap = 12f;
        /// <summary>The frame never shrinks below this (very narrow canvases only).</summary>
        public const float MinWidth = 120f;
        /// <summary>Inset of the drawing area inside the frame border, each side.</summary>
        public const float InnerPad = 3f;

        private const float BorderPx       = 1f;
        private const float BackingAlpha   = 0.7f;
        private const float UpcomingAlpha  = 0.4f;
        private const float RefreshSeconds = 0.1f;   // 10 Hz

        // Marker sizes in UI px (UI_SPEC §3.3.1).
        private static readonly Vector2 PlayerSize    = new Vector2(7f, 7f);
        private static readonly Vector2 FighterSize   = new Vector2(3f, 3f);
        private static readonly Vector2 BomberSize    = new Vector2(5f, 3f);
        private static readonly Vector2 ZeppelinSize  = new Vector2(12f, 4f);
        private static readonly Vector2 AaaSize       = new Vector2(1.5f, 3f);
        /// <summary>5 px, not the spec's 3 px: a 3 px ring reads as a dot (see handoff).</summary>
        private static readonly Vector2 BalloonSize   = new Vector2(5f, 5f);
        private static readonly Vector2 BaseSize      = new Vector2(6f, 6f);
        private static readonly Vector2 UpcomingSize  = new Vector2(1.5f, 6f);
        private static readonly Vector2 DiamondSize   = new Vector2(9f, 9f);
        private static readonly Vector2 FlagSize      = new Vector2(6f, 6f);
        private const float TerrainLinePx = 1.5f;
        private const float DashPx        = 4f;
        private const float DashGapPx     = 3f;
        private const float DashWidthPx   = 1f;

        // ── Terrain sampling ──────────────────────────────────────────────

        /// <summary>World units between terrain polyline samples.</summary>
        public const float TerrainSampleStep = 5f;
        private const int MinTerrainSegments = 8;
        private const int MaxTerrainSegments = 120;
        /// <summary>Band height used when no camera gives the flight ceiling.</summary>
        private const float FallbackBandHeight = 10f;

        // ── Palette (UI_SPEC §2: ink, pencil, paper only; alpha allowed) ──

        private static readonly Color Ink      = HudView.InkCol;
        private static readonly Color Pencil   = HudView.PencilCol;
        private static readonly Color Backing  = WithAlpha(HudView.PaperCol, BackingAlpha);
        private static readonly Color Upcoming = WithAlpha(HudView.PencilCol, UpcomingAlpha);

        // ── Procedural sprites (built once, shared by every minimap) ──────

        private const int ShapeTexels = 16;
        private static Sprite _solid, _dot, _ring, _triangle, _diamond, _flag, _outline;

        // ── Hierarchy ─────────────────────────────────────────────────────

        private RectTransform _root;
        private RectTransform _canvasRect;
        private GameObject    _content;
        private RectTransform _contentRect;
        private RectTransform _backingRect;
        private RectTransform _borderRect;
        private RectTransform _map;            // drawing area inside the border
        private Image         _cameraBracket;
        private Image         _player;

        private MarkerPool _terrainPool, _upcomingPool, _linePool, _groundPool, _aircraftPool, _diamondPool;

        private float _leftReserve, _rightReserve;
        private float _frameWidth = -1f;
        private Vector2 _mapSize;

        // ── Bound data ────────────────────────────────────────────────────

        private MissionRunner _runner;
        private GameManager   _gameManager;
        private Camera        _camera;
        private MissionData   _boundData;
        private TerrainStrip  _boundTerrain;
        private bool  _staticDirty = true;
        private float _bandBottom = TerrainStrip.GroundBaseY;
        private float _bandTop    = TerrainStrip.GroundBaseY + FallbackBandHeight;
        private float _nextRefresh;

        // ── Read-only state (tests, QA) ───────────────────────────────────

        /// <summary>Upcoming-spawn ticks drawn by the last Refresh.</summary>
        public int UpcomingCount => _upcomingPool != null ? _upcomingPool.Count : 0;
        /// <summary>Live enemy aircraft markers drawn by the last Refresh.</summary>
        public int LiveAircraftCount => _aircraftPool != null ? _aircraftPool.Count : 0;
        /// <summary>Live ground enemy markers drawn by the last Refresh.</summary>
        public int LiveGroundCount => _groundPool != null ? _groundPool.Count : 0;
        /// <summary>Objective diamonds drawn by the last Refresh.</summary>
        public int DiamondCount => _diamondPool != null ? _diamondPool.Count : 0;
        /// <summary>Terrain polyline segments currently drawn.</summary>
        public int TerrainSegmentCount => _terrainPool != null ? _terrainPool.Count : 0;
        /// <summary>failAtX dashes plus HQ flags drawn by the last Refresh.</summary>
        public int LineMarkCount => _linePool != null ? _linePool.Count : 0;
        /// <summary>The player triangle is shown.</summary>
        public bool PlayerShown => _player != null && _player.gameObject.activeSelf;
        /// <summary>Current frame width in UI px.</summary>
        public float FrameWidthPx => _frameWidth;
        /// <summary>The strip's root RectTransform (top-centre anchored).</summary>
        public RectTransform Root => _root;

        // ── Factory ───────────────────────────────────────────────────────

        /// <summary>
        /// Builds the minimap under `parent` (the HUD canvas root), top-centre, TopMargin from
        /// the top. `leftReserve` / `rightReserve` are how far the top-left (damage bar) and
        /// top-right (score, objective) widgets reach in from their edges; the frame shrinks to
        /// stay clear of them. Binds to the MissionRunner above `parent`, if any.
        /// </summary>
        public static MinimapView Create(Transform parent, float leftReserve, float rightReserve) {
            var go = new GameObject("Minimap", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MinimapView>();
            view._canvasRect = parent as RectTransform;
            view._leftReserve = leftReserve;
            view._rightReserve = rightReserve;
            view.Build();
            view._gameManager = FindAnyObjectByType<GameManager>();   // once, never per update
            view.Bind(parent != null ? parent.GetComponentInParent<MissionRunner>() : null);
            view._content.SetActive(view.IsVisibleState());
            return view;
        }

        /// <summary>Points the minimap at a mission runner (null clears it) and redraws.</summary>
        public void Bind(MissionRunner runner) {
            _runner = runner;
            _boundData = null;
            _staticDirty = true;
            Refresh();
        }

        // ── Unity lifecycle ───────────────────────────────────────────────

        /// <summary>Shows the strip only while Playing or Paused; repositions markers at 10 Hz
        /// on unscaled time (it keeps updating behind the pause screen).</summary>
        private void Update() {
            bool show = IsVisibleState();
            if (_content != null && _content.activeSelf != show) _content.SetActive(show);
            if (!show) {
                _nextRefresh = 0f;   // redraw at once when it shows again
                return;
            }
            float now = Time.unscaledTime;
            if (now < _nextRefresh) return;
            _nextRefresh = now + RefreshSeconds;
            Refresh();
        }

        // ── Pure helpers (EditMode-tested) ────────────────────────────────

        /// <summary>Strip x (0..width) of world x: 0 at the mission start, width at `length`,
        /// clamped to the strip. 0 when length or width is not positive.</summary>
        public static float MapX(float worldX, float length, float width) {
            if (length <= 0f || width <= 0f) return 0f;
            return Mathf.Clamp01(worldX / length) * width;
        }

        /// <summary>Strip y (0..height) of world y inside the flight band [bandBottom, bandTop],
        /// clamped to the strip. 0 when the band or height is empty.</summary>
        public static float MapY(float worldY, float bandBottom, float bandTop, float height) {
            float span = bandTop - bandBottom;
            if (height <= 0f || span <= 1e-4f) return 0f;
            return Mathf.Clamp01((worldY - bandBottom) / span) * height;
        }

        /// <summary>Z rotation of the player triangle (drawn pointing right): 0 facing right,
        /// 180 facing left.</summary>
        public static float FacingAngle(int facing) {
            return facing < 0 ? 180f : 0f;
        }

        /// <summary>
        /// Frame width for a canvas `canvasWidth` px wide: RefWidth, shrunk so the centred frame
        /// keeps SideGap clear of the widest corner widget (the frame is centred, so the wider
        /// reserve limits both halves), never below MinWidth. An unknown (≤ 0) canvas width
        /// gives RefWidth.
        /// </summary>
        public static float FrameWidth(float canvasWidth, float leftReserve, float rightReserve) {
            if (canvasWidth <= 0f) return RefWidth;
            float half = canvasWidth * 0.5f - Mathf.Max(leftReserve, rightReserve) - SideGap;
            return Mathf.Clamp(half * 2f, MinWidth, RefWidth);
        }

        /// <summary>The strip shows while Playing or Paused only.</summary>
        public static bool VisibleIn(GameManager.GameState state) {
            return state == GameManager.GameState.Playing || state == GameManager.GameState.Paused;
        }

        /// <summary>Spawn types drawn as enemies (live markers and upcoming ticks). Health
        /// balloons, buildings, trees, airfields and the HQ are not enemies.</summary>
        public static bool IsEnemySpawnType(string type) {
            return Same(type, "fighter") || Same(type, "bomber") || Same(type, ZeppelinAI.ScoreKey)
                || Same(type, "aaa") || Same(type, "barrageBalloon") || Same(type, EnemyBase.TypeKey);
        }

        /// <summary>
        /// Does an entity of `typeKey` with spawn id `id` carry an open objective diamond?
        /// destroyById: the id matches targetId. bombTarget: the id matches targetId, or the
        /// type matches targetType when targetId is empty. destroyAllOfType: the type matches,
        /// only for live entities (`live`), never for upcoming ticks. Evaluated (completed)
        /// objectives never match. Null objectives (mission over) never match.
        /// </summary>
        public static bool IsObjectiveTarget(ObjectiveEntry[] objectives, string typeKey, string id, bool live) {
            if (objectives == null) return false;
            for (int i = 0; i < objectives.Length; i++) {
                var obj = objectives[i];
                if (obj == null || obj.evaluated) continue;
                switch (obj.type) {
                    case "destroyAllOfType":
                        // The tracker's own matcher: reinforcements count for "fighter" (B050).
                        if (live && ObjectiveTracker.CountsAsKillOf(obj.targetType, typeKey)) return true;
                        break;
                    case "destroyById":
                        if (!string.IsNullOrEmpty(obj.targetId) && obj.targetId == id) return true;
                        break;
                    case "bombTarget":
                        if (string.IsNullOrEmpty(obj.targetId)
                                ? Same(obj.targetType, typeKey)
                                : obj.targetId == id) return true;
                        break;
                }
            }
            return false;
        }

        private static bool Same(string a, string b) {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
                && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // ── Refresh ───────────────────────────────────────────────────────

        /// <summary>Repositions every marker from the current mission state. Called at 10 Hz
        /// by Update; public so tests and Bind can force a redraw.</summary>
        public void Refresh() {
            if (_root == null) return;
            LayoutIfNeeded();

            if (_camera == null) {
                _camera = ScrollController.Instance != null && ScrollController.Instance.Camera != null
                    ? ScrollController.Instance.Camera : Camera.main;
            }

            MissionData data = _runner != null ? _runner.MissionData : null;
            if (data != _boundData || TerrainStrip.Instance != _boundTerrain) _staticDirty = true;
            if (_staticDirty) RebuildStatic(data);

            float length = data != null ? data.length : 0f;
            ObjectiveEntry[] objectives = _runner != null && _runner.ObjectivesOpen ? data.objectives : null;

            DrawUpcomingAndLines(data, length, objectives);
            DrawCameraWindow(length);
            _diamondPool.Begin();
            DrawGround(length, objectives);
            DrawAircraft(length, objectives);
            _diamondPool.End();
            DrawPlayer(length);
        }

        /// <summary>Flight band and terrain polyline: once per mission, terrain or width.</summary>
        private void RebuildStatic(MissionData data) {
            _staticDirty = false;
            _boundData = data;
            _boundTerrain = TerrainStrip.Instance;
            float length = data != null ? data.length : 0f;

            _terrainPool.Begin();
            if (length <= 0f) {
                _terrainPool.End();
                return;
            }

            int segments = Mathf.Clamp(Mathf.CeilToInt(length / TerrainSampleStep), MinTerrainSegments, MaxTerrainSegments);
            float lowest = float.PositiveInfinity;
            for (int i = 0; i <= segments; i++) {
                lowest = Mathf.Min(lowest, GroundAt(length * i / segments));
            }
            _bandBottom = lowest;
            _bandTop = _camera != null && _camera.orthographic
                ? _camera.transform.position.y + _camera.orthographicSize
                : lowest + FallbackBandHeight;
            if (_bandTop <= _bandBottom + 1f) _bandTop = _bandBottom + FallbackBandHeight;

            Vector2 prev = MapPoint(0f, GroundAt(0f), length);
            for (int i = 1; i <= segments; i++) {
                float x = length * i / segments;
                Vector2 next = MapPoint(x, GroundAt(x), length);
                Vector2 d = next - prev;
                float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                _terrainPool.Next(Solid(), Pencil, new Vector2(d.magnitude + TerrainLinePx * 0.5f, TerrainLinePx), prev, angle);
                prev = next;
            }
            _terrainPool.End();
        }

        /// <summary>Upcoming spawn ticks (with diamonds on pending id targets), failAtX dashed
        /// lines of open objectives, and player HQ flags.</summary>
        private void DrawUpcomingAndLines(MissionData data, float length, ObjectiveEntry[] objectives) {
            _upcomingPool.Begin();
            _linePool.Begin();
            var upcomingDiamonds = 0;
            if (data != null && data.spawns != null && length > 0f) {
                for (int i = 0; i < data.spawns.Length; i++) {
                    var s = data.spawns[i];
                    if (s == null) continue;
                    if (Same(s.type, "playerHQ")) {
                        int n = Mathf.Max(1, s.count);
                        for (int k = 0; k < n; k++) {
                            float hx = s.atX + s.spacing * k;
                            float gy = MapY(GroundAt(hx), _bandBottom, _bandTop, _mapSize.y);
                            _linePool.Next(Flag(), Ink, FlagSize,
                                new Vector2(MapX(hx, length, _mapSize.x), gy + FlagSize.y * 0.5f), 0f);
                        }
                        continue;
                    }
                    if (!IsEnemySpawnType(s.type) || !_runner.IsSpawnPending(i)) continue;
                    var tick = new Vector2(MapX(s.atX, length, _mapSize.x), UpcomingSize.y * 0.5f);
                    _upcomingPool.Next(Solid(), Upcoming, UpcomingSize, tick, 0f);
                    if (IsObjectiveTarget(objectives, s.type, s.id, false)) upcomingDiamonds++;
                }
            }
            _upcomingPool.End();

            if (objectives != null && length > 0f) {
                for (int i = 0; i < objectives.Length; i++) {
                    var obj = objectives[i];
                    if (obj == null || obj.evaluated || obj.type != "destroyById" || obj.failAtX < 0f) continue;
                    DrawDashedLine(MapX(obj.failAtX, length, _mapSize.x));
                }
            }
            _linePool.End();
            _pendingUpcomingDiamonds = upcomingDiamonds;
        }

        /// <summary>Upcoming id-target ticks needing a diamond; drawn with the other diamonds
        /// (the diamond pool is filled once per Refresh).</summary>
        private int _pendingUpcomingDiamonds;

        private void DrawDashedLine(float x) {
            float step = DashPx + DashGapPx;
            for (float y = 0f; y < _mapSize.y; y += step) {
                float h = Mathf.Min(DashPx, _mapSize.y - y);
                _linePool.Next(Solid(), Ink, new Vector2(DashWidthPx, h), new Vector2(x, y + h * 0.5f), 0f);
            }
        }

        /// <summary>The pencil bracket over the part of the map on screen.</summary>
        private void DrawCameraWindow(float length) {
            bool show = _camera != null && _camera.orthographic && length > 0f;
            if (_cameraBracket.gameObject.activeSelf != show) _cameraBracket.gameObject.SetActive(show);
            if (!show) return;
            float half = _camera.orthographicSize * _camera.aspect;
            float cx = _camera.transform.position.x;
            float left = MapX(cx - half, length, _mapSize.x);
            float right = MapX(cx + half, length, _mapSize.x);
            var r = _cameraBracket.rectTransform;
            r.anchoredPosition = new Vector2(left, 0f);
            r.sizeDelta = new Vector2(Mathf.Max(2f, right - left), _mapSize.y);
        }

        /// <summary>Live AAA (pencil tick on the terrain line), barrage balloons (pencil
        /// circle) and enemy bases (pencil square).</summary>
        private void DrawGround(float length, ObjectiveEntry[] objectives) {
            _groundPool.Begin();
            if (_runner != null && length > 0f) {
                var list = _runner.GroundEnemies;
                for (int i = 0; i < list.Count; i++) {
                    var e = list[i];
                    if (!IsLive(e)) continue;
                    Vector3 p = e.transform.position;
                    float x = MapX(p.x, length, _mapSize.x);
                    float ground = MapY(GroundAt(p.x), _bandBottom, _bandTop, _mapSize.y);
                    Vector2 at;
                    string typeKey;
                    string id = "";
                    if (e is AAAGun) {
                        typeKey = "aaa";
                        at = new Vector2(x, ground + AaaSize.y * 0.5f);
                        _groundPool.Next(Solid(), Pencil, AaaSize, at, 0f);
                    } else if (e is BarrageBalloon) {
                        typeKey = "barrageBalloon";
                        at = new Vector2(x, MapY(p.y, _bandBottom, _bandTop, _mapSize.y));
                        _groundPool.Next(Ring(), Pencil, BalloonSize, at, 0f);
                    } else if (e is EnemyBase enemyBase) {
                        typeKey = EnemyBase.TypeKey;
                        id = enemyBase.Id;
                        at = new Vector2(x, ground + BaseSize.y * 0.5f);
                        _groundPool.Next(Solid(), Pencil, BaseSize, at, 0f);
                    } else {
                        continue;
                    }
                    if (IsObjectiveTarget(objectives, typeKey, id, true)) Diamond(at);
                }
            }
            _groundPool.End();
        }

        /// <summary>Live enemy aircraft: fighters (and reinforcements) 3 px ink dots, bombers
        /// 5x3 ink, zeppelins a 12x4 ink bar. Upcoming id-target diamonds are drawn here too.</summary>
        private void DrawAircraft(float length, ObjectiveEntry[] objectives) {
            _aircraftPool.Begin();
            if (_runner != null && length > 0f) {
                var list = _runner.EnemyAircraft;
                for (int i = 0; i < list.Count; i++) {
                    var a = list[i];
                    if (!IsLive(a)) continue;
                    Vector3 p = a.transform.position;
                    var at = new Vector2(MapX(p.x, length, _mapSize.x), MapY(p.y, _bandBottom, _bandTop, _mapSize.y));
                    bool target;
                    if (a is FighterAI) {
                        _aircraftPool.Next(Dot(), Ink, FighterSize, at, 0f);
                        // Reinforcements count toward destroyAllOfType "fighter" like JSON
                        // fighters (B050, ObjectiveTracker.CountsAsKillOf), so every live
                        // fighter is a target while a fighter objective is open (B053).
                        target = IsObjectiveTarget(objectives, "fighter", "", true);
                    } else if (a is BomberAI bomber) {
                        _aircraftPool.Next(Solid(), Ink, BomberSize, at, 0f);
                        target = IsObjectiveTarget(objectives, "bomber", bomber.SpawnId, true);
                    } else if (a is ZeppelinAI zeppelin) {
                        _aircraftPool.Next(Solid(), Ink, ZeppelinSize, at, 0f);
                        target = IsObjectiveTarget(objectives, ZeppelinAI.ScoreKey, zeppelin.Id, true);
                    } else {
                        continue;
                    }
                    if (target) Diamond(at);
                }
            }
            _aircraftPool.End();

            // Diamonds on pending id targets (the destroyById bomber/zeppelin, the bombTarget
            // base) sit on their upcoming tick until they spawn.
            if (_pendingUpcomingDiamonds > 0 && _boundData != null && _boundData.spawns != null) {
                for (int i = 0; i < _boundData.spawns.Length; i++) {
                    var s = _boundData.spawns[i];
                    if (s == null || !IsEnemySpawnType(s.type) || !_runner.IsSpawnPending(i)) continue;
                    if (!IsObjectiveTarget(objectives, s.type, s.id, false)) continue;
                    Diamond(new Vector2(MapX(s.atX, length, _mapSize.x), UpcomingSize.y * 0.5f));
                }
            }
        }

        private void Diamond(Vector2 at) {
            _diamondPool.Next(DiamondSprite(), Ink, DiamondSize, at, 0f);
        }

        /// <summary>The ink player triangle, pointing the way the plane faces; on top.</summary>
        private void DrawPlayer(float length) {
            PlayerPlane plane = _runner != null ? _runner.Player : null;
            bool show = plane != null && length > 0f;
            if (_player.gameObject.activeSelf != show) _player.gameObject.SetActive(show);
            if (!show) return;
            Vector3 p = plane.transform.position;
            var r = _player.rectTransform;
            r.anchoredPosition = new Vector2(MapX(p.x, length, _mapSize.x), MapY(p.y, _bandBottom, _bandTop, _mapSize.y));
            r.localEulerAngles = new Vector3(0f, 0f, FacingAngle(plane.Facing));
        }

        /// <summary>Alive and active: Unity-null (destroyed), deactivated (torn down) and dead
        /// (IDamageable.IsDead) entities are skipped.</summary>
        private static bool IsLive(MonoBehaviour e) {
            if (e == null || !e.gameObject.activeInHierarchy) return false;
            return !(e is IDamageable d) || !d.IsDead;
        }

        private float GroundAt(float x) {
            var terrain = TerrainStrip.Instance;
            return terrain != null ? terrain.HeightAt(x) : TerrainStrip.GroundBaseY;
        }

        private Vector2 MapPoint(float worldX, float worldY, float length) {
            return new Vector2(MapX(worldX, length, _mapSize.x), MapY(worldY, _bandBottom, _bandTop, _mapSize.y));
        }

        private bool IsVisibleState() {
            // Without a GameManager (EditMode tests, a bare scene) the strip just shows.
            return _gameManager == null || VisibleIn(_gameManager.CurrentState);
        }

        // ── Build and layout ──────────────────────────────────────────────

        /// <summary>Builds the hierarchy once; LayoutIfNeeded sizes it.</summary>
        private void Build() {
            _root = (RectTransform)transform;
            _root.anchorMin = new Vector2(0.5f, 1f);
            _root.anchorMax = new Vector2(0.5f, 1f);
            _root.pivot     = new Vector2(0.5f, 1f);
            _root.anchoredPosition = new Vector2(0f, -TopMargin);

            _contentRect = NewRect("Content", _root);
            _content = _contentRect.gameObject;

            _backingRect = NewRect("Backing", _contentRect);
            NewImage(_backingRect, Solid(), Backing);

            _map = NewRect("Map", _contentRect);
            _terrainPool  = new MarkerPool(NewLayer("Terrain"), new Vector2(0f, 0.5f));
            _upcomingPool = new MarkerPool(NewLayer("Upcoming"), new Vector2(0.5f, 0.5f));
            _linePool     = new MarkerPool(NewLayer("Lines"), new Vector2(0.5f, 0.5f));

            var bracketRect = NewRect("CameraWindow", _map);
            _cameraBracket = NewImage(bracketRect, Outline(), Pencil);
            _cameraBracket.type = Image.Type.Sliced;

            _groundPool   = new MarkerPool(NewLayer("Ground"), new Vector2(0.5f, 0.5f));
            _aircraftPool = new MarkerPool(NewLayer("Aircraft"), new Vector2(0.5f, 0.5f));
            _diamondPool  = new MarkerPool(NewLayer("Diamonds"), new Vector2(0.5f, 0.5f));

            var playerRect = NewRect("Player", _map);
            playerRect.pivot = new Vector2(0.5f, 0.5f);
            playerRect.sizeDelta = PlayerSize;
            _player = NewImage(playerRect, Triangle(), Ink);
            _player.gameObject.SetActive(false);

            // The border goes last so markers clamped to the edge never cover it.
            _borderRect = NewRect("Border", _contentRect);
            var border = NewImage(_borderRect, Outline(), Ink);
            border.type = Image.Type.Sliced;

            LayoutIfNeeded();
        }

        /// <summary>Resizes the frame when the canvas width changes (window resize).</summary>
        private void LayoutIfNeeded() {
            float canvasWidth = _canvasRect != null ? _canvasRect.rect.width : 0f;
            float w = FrameWidth(canvasWidth, _leftReserve, _rightReserve);
            if (Mathf.Approximately(w, _frameWidth)) return;
            _frameWidth = w;
            _staticDirty = true;

            var frame = new Vector2(w, FrameHeight);
            _root.sizeDelta = frame;
            PlaceBottomLeft(_contentRect, Vector2.zero, frame);
            PlaceBottomLeft(_backingRect, Vector2.zero, frame);
            PlaceBottomLeft(_borderRect, Vector2.zero, frame);
            _mapSize = new Vector2(w - InnerPad * 2f, FrameHeight - InnerPad * 2f);
            PlaceBottomLeft(_map, new Vector2(InnerPad, InnerPad), _mapSize);
            for (int i = 0; i < _map.childCount; i++) {
                var layer = (RectTransform)_map.GetChild(i);
                if (layer == _player.rectTransform || layer == _cameraBracket.rectTransform) continue;
                PlaceBottomLeft(layer, Vector2.zero, _mapSize);
            }
        }

        private RectTransform NewLayer(string name) {
            return NewRect(name, _map);
        }

        /// <summary>A RectTransform with bottom-left anchor and pivot (positions and sizes are
        /// set with anchoredPosition and sizeDelta only, ARCHITECTURE §3 UI).</summary>
        private static RectTransform NewRect(string name, Transform parent) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.zero;
            r.pivot     = Vector2.zero;
            return r;
        }

        private static void PlaceBottomLeft(RectTransform r, Vector2 position, Vector2 size) {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.zero;
            r.pivot     = Vector2.zero;
            r.sizeDelta = size;
            r.anchoredPosition = position;
        }

        private static Image NewImage(RectTransform rect, Sprite sprite, Color color) {
            var img = rect.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Color WithAlpha(Color c, float a) {
            return new Color(c.r, c.g, c.b, a);
        }

        // ── Marker pool ───────────────────────────────────────────────────

        /// <summary>
        /// Reusable Images under one layer. Begin, then Next per marker, then End: unused
        /// Images are deactivated, never destroyed, so a steady refresh allocates nothing.
        /// </summary>
        private sealed class MarkerPool {
            private readonly RectTransform _layer;
            private readonly Vector2 _pivot;
            private readonly List<Image> _items = new List<Image>();
            private int _used;

            public MarkerPool(RectTransform layer, Vector2 pivot) {
                _layer = layer;
                _pivot = pivot;
            }

            /// <summary>Markers placed since the last Begin (after End: markers shown).</summary>
            public int Count => _used;

            public void Begin() {
                _used = 0;
            }

            public Image Next(Sprite sprite, Color color, Vector2 size, Vector2 position, float angle) {
                Image img;
                if (_used < _items.Count) {
                    img = _items[_used];
                } else {
                    var rect = NewRect("Marker_" + _items.Count, _layer);
                    rect.pivot = _pivot;
                    img = NewImage(rect, sprite, color);
                    _items.Add(img);
                }
                _used++;
                if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
                if (img.sprite != sprite) img.sprite = sprite;
                if (img.color != color) img.color = color;
                var r = img.rectTransform;
                r.sizeDelta = size;
                r.anchoredPosition = position;
                r.localEulerAngles = new Vector3(0f, 0f, angle);
                return img;
            }

            public void End() {
                for (int i = _used; i < _items.Count; i++) {
                    if (_items[i].gameObject.activeSelf) _items[i].gameObject.SetActive(false);
                }
            }
        }

        // ── Procedural sprites (white, tinted by Image.color) ─────────────

        private static Sprite Solid() {
            if (_solid == null) _solid = Shape(4, (x, y) => 1f, Vector4.zero);
            return _solid;
        }

        private static Sprite Dot() {
            if (_dot == null) {
                _dot = Shape(ShapeTexels, (x, y) => Circle(x, y) <= 7.5f ? 1f : 0f, Vector4.zero);
            }
            return _dot;
        }

        private static Sprite Ring() {
            if (_ring == null) {
                _ring = Shape(ShapeTexels, (x, y) => {
                    float d = Circle(x, y);
                    return d <= 7.5f && d >= 4.5f ? 1f : 0f;
                }, Vector4.zero);
            }
            return _ring;
        }

        /// <summary>Points right: tip at the middle of the right edge.</summary>
        private static Sprite Triangle() {
            if (_triangle == null) {
                _triangle = Shape(ShapeTexels, (x, y) => {
                    float half = 8f * (1f - x / 16f);   // half height shrinks toward the tip
                    return Mathf.Abs(y - 8f) <= half ? 1f : 0f;
                }, Vector4.zero);
            }
            return _triangle;
        }

        /// <summary>Hollow diamond, 2 texels thick.</summary>
        private static Sprite DiamondSprite() {
            if (_diamond == null) {
                _diamond = Shape(ShapeTexels, (x, y) => {
                    float d = Mathf.Abs(x - 8f) + Mathf.Abs(y - 8f);
                    return d <= 8f && d >= 5.5f ? 1f : 0f;
                }, Vector4.zero);
            }
            return _diamond;
        }

        /// <summary>A pole on the left with a pennant at the top flying right.</summary>
        private static Sprite Flag() {
            if (_flag == null) {
                _flag = Shape(ShapeTexels, (x, y) => {
                    if (x < 3f) return 1f;
                    float half = 3.5f * (1f - (x - 3f) / 13f);
                    return Mathf.Abs(y - 12f) <= half ? 1f : 0f;
                }, Vector4.zero);
            }
            return _flag;
        }

        /// <summary>A 1-texel outline, 9-sliced so it stays 1 px at any size.</summary>
        private static Sprite Outline() {
            if (_outline == null) {
                _outline = Shape(8, (x, y) => x < 1f || y < 1f || x >= 7f || y >= 7f ? 1f : 0f,
                    new Vector4(1f, 1f, 1f, 1f));
            }
            return _outline;
        }

        private static float Circle(float x, float y) {
            float dx = x - 8f, dy = y - 8f;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// A size x size white texture whose alpha is `coverage` averaged over 4x4 sub-samples
        /// per texel (smooth edges), as a Sprite. Built once per shape. Sliced sprites get no
        /// mip chain (their border must stay exactly one texel).
        /// </summary>
        private static Sprite Shape(int size, Func<float, float, float> coverage, Vector4 border) {
            bool sliced = border != Vector4.zero;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, !sliced && size > 4);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.DontSave;
            var pixels = new Color[size * size];
            const int Sub = 4;
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    float sum = 0f;
                    for (int sy = 0; sy < Sub; sy++) {
                        for (int sx = 0; sx < Sub; sx++) {
                            sum += coverage(x + (sx + 0.5f) / Sub, y + (sy + 0.5f) / Sub);
                        }
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, sum / (Sub * Sub));
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, border);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
