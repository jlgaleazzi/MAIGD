using System.Collections.Generic;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Player;
using Biplanes.Fx;
using Biplanes.Audio;

namespace Biplanes.Enemies {

    /// <summary>
    /// Enemy zeppelin hull (GDD: Zeppelins, mission 5). A big, slow airship that flies left
    /// and carries ZeppelinTurret children (gun positions) on its envelope.
    ///
    /// • Self-contained IDamageable (no Health component, like AAAGun), so a hit on a hull
    ///   collider can never bypass the shield rule below.
    /// • Hit points from Balance enemyHealth.zeppelinHull (damage values are points).
    /// • Shielded while any turret is alive: hull hits are absorbed (the projectile is used
    ///   up, no damage). Always vulnerable when Balance hullVulnerableEarly is true.
    /// • On death raises EntityDestroyed("zeppelin", Enemy) exactly once (ScoreKeeper awards
    ///   score.zeppelin), plays explosions along the hull and removes itself and its turrets.
    /// • Art faces right; the zeppelin flies left, so the sprite is mirrored with flipX and
    ///   every hitbox / turret x offset is negated (same convention as BomberAI).
    /// • Removed silently (no score) once it is far behind the left screen edge, unless it
    ///   still has a targetX ahead of it (SetTargetX; reaching it loses the mission, B018).
    ///
    /// Built from code by the static factory Spawn(position, id).
    /// </summary>
    public class ZeppelinAI : MonoBehaviour, IDamageable {

        /// <summary>Balance score key; EntityDestroyed is raised with this type.</summary>
        public const string ScoreKey = "zeppelin";

        // ── Named constants (no Balance field exists for these; see T933 handoff) ──

        /// <summary>Fallback flight speed (world units/s) used only when Balance
        /// enemySpeed.zeppelin is missing or not positive (same pattern as BomberAI).</summary>
        private const float FallbackSpeed = 1.0f;
        /// <summary>World units past the left screen edge after which the zeppelin is removed.</summary>
        private const float DespawnBehindMargin = 20f;
        /// <summary>The hull centre is kept at least this far below the screen top, so the
        /// top turrets (local y up to ~1.15) stay on screen.</summary>
        private const float ScreenTopClearance = 1.3f;
        /// <summary>Seconds added to each successive turret's first shot (staggers volleys).</summary>
        private const float TurretFirstShotStagger = 0.8f;
        /// <summary>Explosions played along the hull on death (local x, art facing right).</summary>
        private static readonly float[] DeathExplosionOffsetsX = { -2.5f, 0f, 2.5f };
        private const int FallbackPixelsPerUnit = 10;

        // ── Audio (T951) ──────────────────────────────────────────────────

        /// <summary>Hum loop volume (0..1 of master*sfx) while the hull is on screen. The
        /// zeppelin_hum clip is mastered loud (RMS about -12.6 dBFS), so it sits under the guns.</summary>
        public const float HumVolume = 0.45f;
        /// <summary>World units beyond the screen edge (nearest point of the hull) over which
        /// the hum fades from HumVolume to silent; farther away the loop is stopped.</summary>
        public const float HumFadeDistance = 8f;
        /// <summary>Minimum seconds between two HitEnemy cues on the hull.</summary>
        private const float HitCueMinInterval = 0.05f;

        /// <summary>The zeppelin always flies left: mirror the right-facing art and offsets.</summary>
        private const bool FlipsToFlyLeft = true;

        // ── Geometry fitted to Sprites/enemies/zeppelin_hull.png ──────────
        // 900×220 px, 100 ppu, pivot centre, art facing RIGHT (nose right, tail fins left).
        // Local units = (px - 450) / 100 in x and (110 - py) / 100 in y. Envelope ink spans
        // y 25..174; gondolas hang at y 176..206. Boxes are inset slightly inside the ink.
        // Collider geometry is sprite data, not a gameplay tunable.

        private static readonly Vector2 HullArtSize = new Vector2(9.0f, 2.2f);

        /// <summary>Half the hull's drawn length (world units). MissionRunner spawns the
        /// zeppelin this much (plus a margin) beyond the right screen edge so it flies in
        /// instead of popping in (B021).</summary>
        public static float HalfLength => HullArtSize.x * 0.5f;

        /// <summary>Main envelope, px x 150..865, y 32..168.</summary>
        private static readonly Vector2 EnvelopeSize   = new Vector2(7.15f, 1.36f);
        private static readonly Vector2 EnvelopeOffset = new Vector2(0.575f, 0.10f);
        /// <summary>Tapering tail with fins, px x 58..150, y 30..170.</summary>
        private static readonly Vector2 TailSize   = new Vector2(0.92f, 1.40f);
        private static readonly Vector2 TailOffset = new Vector2(-3.46f, 0.10f);
        /// <summary>Gondolas, px x 240..310 / 460..530 / 650..745, y 178..204.</summary>
        private static readonly Vector2 GondolaSmallSize  = new Vector2(0.70f, 0.26f);
        private static readonly Vector2 GondolaLargeSize  = new Vector2(0.95f, 0.26f);
        private static readonly Vector2 Gondola1Offset = new Vector2(-1.75f, -0.81f);
        private static readonly Vector2 Gondola2Offset = new Vector2(0.45f, -0.81f);
        private static readonly Vector2 Gondola3Offset = new Vector2(2.475f, -0.81f);

        /// <summary>Turret positions (art facing right). Two sit on top of the envelope
        /// (ink top y 25 px → 0.85, turret base 0.17 below its centre), fore and aft; one
        /// hangs under the envelope between gondolas 1 and 2.
        /// B021: the underside turret hangs BELOW the gondola line (gondola boxes y -0.94..-0.68),
        /// at px y 220 (hitbox y -1.25..-0.95). Player bullets fly horizontally only and a
        /// shielded gondola box absorbs them, so at the old -0.82 (level with the gondolas) only
        /// a 0.03-unit band of bullet heights could reach it and the shield never dropped. At
        /// -1.10 bullet centres in y -1.33..-1.02 (≈0.31) pass under the gondolas to the turret.
        /// The hull art carries a short pylon from the envelope (px y 174) down to the turret.</summary>
        private static readonly Vector2[] TurretOffsets = {
            new Vector2( 1.90f,  1.00f),   // top, fore (px x 640)
            new Vector2(-1.30f,  1.00f),   // top, aft  (px x 320)
            new Vector2(-0.65f, -1.10f),   // underside (px x 385), below the gondolas
        };
        private static readonly bool[] TurretUnderside = { false, false, true };

        private static bool _warnedMissingSprite;
        private static Sprite _fallbackSprite;

        // ── State ─────────────────────────────────────────────────────────

        private Rigidbody2D _rb;
        private SpriteRenderer _sr;
        private Camera _cam;
        private float _maxHitPoints;
        private float _speed = FallbackSpeed;   // Balance enemySpeed.zeppelin, set in Awake
        private float _damagePercent;           // 0..100
        private bool _destroyed;                // EntityDestroyed raised (once-only guard)
        private bool _hullVulnerableEarly;
        private PlayerPlane _player;
        private float _targetX = -1f;           // world x it must not reach; -1 = none
        private readonly List<ZeppelinTurret> _turrets = new List<ZeppelinTurret>();
        private static int _humKeyCounter;      // makes each hum loop key unique
        private string _humKey;                 // unique AudioManager loop key per zeppelin
        private float _lastHitCueTime = -1f;

        /// <summary>Mission spawn id (e.g. "zeppelin_01"); empty when none was given.</summary>
        public string Id { get; private set; } = "";

        /// <summary>World x the zeppelin is flying (left) toward, e.g. the player's HQ; -1 when
        /// it has none. Set by MissionRunner from the objective's failAtX (B018). Reaching it
        /// loses the mission; MissionRunner checks that, the zeppelin only keeps flying.</summary>
        public float TargetX => _targetX;

        /// <summary>True when a targetX (≥ 0) is set.</summary>
        public bool HasTargetX => _targetX >= 0f;

        /// <summary>True once the hull's world x is at or past its targetX (flying left).</summary>
        public bool ReachedTarget => HasTargetX && transform.position.x <= _targetX;

        /// <summary>Turrets still alive.</summary>
        public int TurretsRemaining => _turrets.Count;

        /// <summary>True while hull hits are ignored (turrets alive and not hullVulnerableEarly).</summary>
        public bool IsShielded => !_hullVulnerableEarly && _turrets.Count > 0;

        /// <summary>Hull damage, 0..100.</summary>
        public float CurrentDamagePercent => _damagePercent;

        // ── IDamageable ───────────────────────────────────────────────────

        public Team Team => Team.Enemy;

        public bool IsDead => _damagePercent >= 100f;

        /// <summary>Bullet / Bomb points against enemyHealth.zeppelinHull, Collision fatal,
        /// Flak ignored. While shielded every hit is absorbed with no damage; the projectile
        /// still recycles itself, as on any other target it touches.</summary>
        public void ApplyDamage(float percent, DamageSource src) {
            if (IsDead || _destroyed || IsShielded) return;

            float actual;
            switch (src) {
                case DamageSource.Bullet:
                case DamageSource.Bomb:
                    actual = _maxHitPoints > 0f ? percent / _maxHitPoints * 100f : 100f;
                    break;
                case DamageSource.Collision:
                    actual = 100f;
                    break;
                default:
                    return;   // Flak / Heal: no effect.
            }

            float before = _damagePercent;
            _damagePercent = Mathf.Clamp(_damagePercent + actual, 0f, 100f);
            if (IsDead) {
                Die();
            } else if (_damagePercent > before) {
                PlayHitCue();
            }
        }

        public void Heal(float amount) { /* the zeppelin is not healable */ }

        // ── Static factory (for MissionRunner) ────────────────────────────

        /// <summary>
        /// Spawn a zeppelin hull at `position` with its turrets, flying left.
        /// `id` is the mission spawn id (may be null/empty); the GameObject name contains it
        /// so FighterAI escortOf lookups (name contains id) can find the hull.
        /// Call Init(player) afterwards if the player is known; otherwise the hull finds the
        /// player once in Start.
        /// </summary>
        public static ZeppelinAI Spawn(Vector3 position, string id) {
            string safeId = id ?? "";
            var go = new GameObject(string.IsNullOrEmpty(safeId) ? ScoreKey : $"{ScoreKey} [{safeId}]");
            go.transform.position = position;

            var tag = go.AddComponent<TeamTag>();
            tag.team = Team.Enemy;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            // Report overlaps with other kinematic bodies (the player) for ramming.
            rb.useFullKinematicContacts = true;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = LoadSprite();
            sr.flipX = FlipsToFlyLeft;
            sr.sortingOrder = 5;   // ARCHITECTURE §3 draw order: enemies

            float sign = FlipsToFlyLeft ? -1f : 1f;
            AddBox(go, EnvelopeSize, EnvelopeOffset, sign);
            AddBox(go, TailSize, TailOffset, sign);
            AddBox(go, GondolaSmallSize, Gondola1Offset, sign);
            AddBox(go, GondolaSmallSize, Gondola2Offset, sign);
            AddBox(go, GondolaLargeSize, Gondola3Offset, sign);

            var zep = go.AddComponent<ZeppelinAI>();
            zep.Id = safeId;
            zep._rb = rb;
            zep._sr = sr;

            // Turrets are built once here, never in an update loop.
            for (int i = 0; i < TurretOffsets.Length; i++) {
                Vector2 o = TurretOffsets[i];
                var turret = ZeppelinTurret.Create(zep, new Vector2(o.x * sign, o.y),
                    FlipsToFlyLeft, TurretUnderside[i], TurretFirstShotStagger * (i + 1));
                zep._turrets.Add(turret);
            }
            return zep;
        }

        /// <summary>Give the hull and its turrets the player to shoot at.</summary>
        public void Init(PlayerPlane player) {
            _player = player;
            for (int i = 0; i < _turrets.Count; i++) {
                if (_turrets[i] != null) _turrets[i].PlayerRef = player;
            }
        }

        /// <summary>Set the world x the zeppelin flies toward (negative = none). While it has
        /// one and hasn't reached it, it is never despawned for being behind the camera, so a
        /// player who outruns it can't make the objective vanish (B018).</summary>
        public void SetTargetX(float targetX) {
            _targetX = targetX >= 0f ? targetX : -1f;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void Awake() {
            var balance = GameBalance.Get();
            _maxHitPoints = balance != null && balance.enemyHealth != null
                ? balance.enemyHealth.zeppelinHull : 1000f;
            _hullVulnerableEarly = balance != null && balance.hullVulnerableEarly;
            // Speed: Balance enemySpeed.zeppelin (slower than the bomber). Fallback 1.0.
            _speed = balance != null && balance.enemySpeed != null && balance.enemySpeed.zeppelin > 0f
                ? balance.enemySpeed.zeppelin : FallbackSpeed;

            if (_rb == null) _rb = GetComponent<Rigidbody2D>();
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            _cam = Camera.main;
            _humKey = "zeppelin_hum_" + (++_humKeyCounter);
        }

        /// <summary>Hum loop: on while the hull is on screen or within HumFadeDistance of it,
        /// fading with the distance; off beyond that and once destroyed (T951).</summary>
        private void Update() {
            if (_destroyed) return;
            var audio = AudioManager.GetOrNull();
            if (audio == null) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            float halfW = _cam.orthographicSize * _cam.aspect;
            float camX = _cam.transform.position.x;
            float x = transform.position.x;
            // Gap between the screen and the nearest end of the hull (0 when any of it shows).
            float gap = Mathf.Max(0f, Mathf.Abs(x - camX) - halfW - HalfLength);
            if (gap >= HumFadeDistance) {
                audio.StopLoop(_humKey);
                return;
            }
            audio.StartLoop(SfxId.ZeppelinHum, _humKey);
            audio.SetLoopVolume(_humKey, HumVolume * (1f - gap / HumFadeDistance));
        }

        private void OnDisable() {
            StopHum();
        }

        /// <summary>Stops this zeppelin's hum. Safe from OnDisable at quit: GetOrNull never
        /// creates a manager while the application is quitting (B032).</summary>
        private void StopHum() {
            if (string.IsNullOrEmpty(_humKey)) return;
            var audio = AudioManager.GetOrNull();
            if (audio != null) audio.StopLoop(_humKey);
        }

        /// <summary>HitEnemy for a non-fatal hull hit (not while shielded), throttled.</summary>
        private void PlayHitCue() {
            float now = Time.time;
            if (_lastHitCueTime >= 0f && now - _lastHitCueTime < HitCueMinInterval) return;
            var audio = AudioManager.GetOrNull();
            if (audio == null) return;
            _lastHitCueTime = now;
            audio.Play(SfxId.HitEnemy, transform.position);
        }

        private void Start() {
            // One-time lookup (never per frame) when MissionRunner did not call Init.
            if (_player == null) {
                var player = FindFirstObjectByType<PlayerPlane>();
                if (player != null) Init(player);
            }

            // Keep the top turrets on screen: clamp the hull below the screen top.
            if (_cam != null) {
                float maxY = _cam.transform.position.y + _cam.orthographicSize - ScreenTopClearance;
                if (transform.position.y > maxY) {
                    var p = transform.position;
                    p.y = maxY;
                    transform.position = p;
                    if (_rb != null) _rb.position = p;   // FixedUpdate reads _rb.position
                }
            }
        }

        private void FixedUpdate() {
            if (_destroyed || _rb == null) return;

            Vector2 pos = _rb.position;
            pos.x -= _speed * Time.fixedDeltaTime;
            _rb.MovePosition(pos);

            // Far behind the camera (the camera never scrolls back past its start limit):
            // remove without score, like a flyer that left the battle. Not while it is still
            // on its way to a targetX: it must stay alive until it gets there (B018).
            bool enRouteToTarget = HasTargetX && pos.x > _targetX;
            if (_cam == null) _cam = Camera.main;
            if (_cam != null && !enRouteToTarget) {
                float leftEdge = _cam.transform.position.x - _cam.orthographicSize * _cam.aspect;
                if (pos.x < leftEdge - DespawnBehindMargin) {
                    _destroyed = true;
                    StopHum();
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>Ramming the hull crashes the player. The airship itself is not brought
        /// down by a biplane. Also fires for turret colliders (compound body); the
        /// HasCrashed guard keeps it to one crash.</summary>
        private void OnTriggerEnter2D(Collider2D other) {
            if (_destroyed) return;
            var player = other.GetComponentInParent<PlayerPlane>();
            if (player == null || player.HasCrashed) return;

            other.GetComponentInParent<IDamageable>()?.ApplyDamage(1f, DamageSource.Collision);
            player.ForceCrash();
        }

        // ── Turret bookkeeping ────────────────────────────────────────────

        /// <summary>Called once by a turret when it dies.</summary>
        internal void NotifyTurretDestroyed(ZeppelinTurret turret) {
            _turrets.Remove(turret);
        }

        // ── Death ─────────────────────────────────────────────────────────

        private void Die() {
            if (_destroyed) return;
            _destroyed = true;

            // ScoreKeeper awards score.zeppelin from this single event.
            GameEvents.RaiseEntityDestroyed(ScoreKey, Team.Enemy);

            float sign = FlipsToFlyLeft ? -1f : 1f;
            var fx = FxManager.Instance;
            for (int i = 0; i < DeathExplosionOffsetsX.Length; i++) {
                fx.TriggerExplosion(transform.position + new Vector3(DeathExplosionOffsetsX[i] * sign, 0f, 0f),
                    playSound: false);   // ExplosionBig below is the one death cue
            }

            // Big explosion cue once, on top of the FxManager explosions (T951).
            StopHum();
            var audio = AudioManager.GetOrNull();
            if (audio != null) audio.Play(SfxId.ExplosionBig, transform.position);

            // Turrets are children: destroying the hull removes them without extra score events.
            _turrets.Clear();
            Destroy(gameObject);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static void AddBox(GameObject go, Vector2 size, Vector2 offset, float xSign) {
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = size;
            col.offset = new Vector2(offset.x * xSign, offset.y);
        }

        private static Sprite LoadSprite() {
            var sprite = Resources.Load<Sprite>("Sprites/enemies/zeppelin_hull");
            if (sprite != null) return sprite;

            if (!_warnedMissingSprite) {
                _warnedMissingSprite = true;
                Debug.LogWarning("ZeppelinAI: 'Sprites/enemies/zeppelin_hull' not found — using a solid placeholder.");
            }
            if (_fallbackSprite == null) {
                int w = Mathf.Max(1, Mathf.RoundToInt(HullArtSize.x * FallbackPixelsPerUnit));
                int h = Mathf.Max(1, Mathf.RoundToInt(HullArtSize.y * FallbackPixelsPerUnit));
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var pixels = new Color[w * h];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
                tex.SetPixels(pixels);
                tex.Apply(false);
                _fallbackSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), FallbackPixelsPerUnit);
            }
            return _fallbackSprite;
        }
    }
}
