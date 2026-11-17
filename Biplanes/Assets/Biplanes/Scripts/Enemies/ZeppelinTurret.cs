using UnityEngine;
using Biplanes.Audio;
using Biplanes.Core;
using Biplanes.Player;
using Biplanes.Weapons;
using Biplanes.Fx;

namespace Biplanes.Enemies {

    /// <summary>
    /// One gun position on a zeppelin hull. Lives as a child GameObject of the ZeppelinAI
    /// hull (built by ZeppelinAI.Spawn, never on its own) and rides along with the hull's
    /// kinematic Rigidbody2D, so its trigger collider is part of the hull's compound body.
    ///
    /// • Team.Enemy, self-contained IDamageable (no Health component, like AAAGun), so a
    ///   projectile hitting the turret collider always resolves to this turret first.
    /// • Hit points from Balance enemyHealth.zeppelinTurret; damage values are points, as in
    ///   Health enemy mode. Player bullets and bombs both damage it (it is airborne).
    /// • Fires one FlakShell at the player's predicted intercept point on a cooldown, only
    ///   while near the screen, along the exact aim (downward included), with
    ///   DamageSource.ZeppelinTurret: the player's Health scales it by damageTaken.zeppelinTurret.
    /// • On death raises EntityDestroyed("zeppelinTurret", Enemy) exactly once (ScoreKeeper
    ///   awards score.zeppelinTurret), plays an explosion, tells its hull, and is removed.
    /// </summary>
    public class ZeppelinTurret : MonoBehaviour, IDamageable {

        /// <summary>Balance score key; EntityDestroyed is raised with this type.</summary>
        public const string ScoreKey = "zeppelinTurret";

        // ── Named constants (no Balance field exists for these; see T933 handoff) ──

        /// <summary>Seconds between two shots of one turret.</summary>
        private const float FireCooldown = 2.5f;
        /// <summary>Extra world units beyond the camera edge in which the turret still fires
        /// (same value AAAGun uses).</summary>
        private const float FireRangeMargin = 2f;
        /// <summary>Trigger hitbox, fitted inside the 60×40 px zeppelin_turret sprite
        /// (ink x 3..58, y 4..38 at 100 ppu).</summary>
        private static readonly Vector2 HitboxSize = new Vector2(0.50f, 0.30f);
        /// <summary>Fallback solid sprite size in world units (matches the art).</summary>
        private static readonly Vector2 FallbackSize = new Vector2(0.6f, 0.4f);
        private const int FallbackPixelsPerUnit = 10;

        private static bool _warnedMissingSprite;
        private static Sprite _fallbackSprite;

        // ── State ─────────────────────────────────────────────────────────

        private float _damagePercent;          // 0..100
        private float _maxHitPoints;           // Balance enemyHealth.zeppelinTurret
        private bool _destroyed;               // EntityDestroyed raised (once-only guard)
        private float _nextFireTime;
        private float _lastHitCueTime = -1f;   // Time.time of the last HitEnemy; negative = none
        private ZeppelinAI _owner;
        private SpriteRenderer _sr;
        private Camera _cam;

        /// <summary>Player to shoot at. Set by ZeppelinAI (Init or its Start fallback).</summary>
        internal PlayerPlane PlayerRef { get; set; }

        // ── IDamageable ───────────────────────────────────────────────────

        public Team Team => Team.Enemy;

        public bool IsDead => _damagePercent >= 100f;

        /// <summary>Current damage, 0..100.</summary>
        public float CurrentDamagePercent => _damagePercent;

        /// <summary>Bullet and Bomb deal their points against enemyHealth.zeppelinTurret;
        /// Collision is fatal (Health enemy mode rules). Flak (enemy fire) is ignored.</summary>
        public void ApplyDamage(float percent, DamageSource src) {
            if (IsDead || _destroyed) return;

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
                    return;   // Flak / Heal: no effect on enemy turrets.
            }

            float before = _damagePercent;
            _damagePercent = Mathf.Clamp(_damagePercent + actual, 0f, 100f);
            if (IsDead) Die();
            else if (_damagePercent > before) PlayHitCue();
        }

        /// <summary>HitEnemy for a non-fatal hit (a fatal one gets Die's explosion), at most one
        /// per Health.HitCueMinInterval, like Health and ZeppelinAI (B033 / T953 B6).</summary>
        private void PlayHitCue() {
            float now = Time.time;
            if (_lastHitCueTime >= 0f && now - _lastHitCueTime < Health.HitCueMinInterval) return;
            var audio = AudioManager.GetOrNull();
            if (audio == null) return;
            _lastHitCueTime = now;
            audio.Play(SfxId.HitEnemy, transform.position);
        }

        public void Heal(float amount) { /* turrets are not healable */ }

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void Awake() {
            var balance = GameBalance.Get();
            _maxHitPoints = balance != null && balance.enemyHealth != null
                ? balance.enemyHealth.zeppelinTurret : 75f;
            _sr = GetComponent<SpriteRenderer>();
            _cam = Camera.main;
        }

        private void Update() {
            if (_destroyed || PlayerRef == null || PlayerRef.HasCrashed) return;
            if (Time.time < _nextFireTime || !IsInFireRange()) return;

            _nextFireTime = Time.time + FireCooldown;
            Fire();
        }

        /// <summary>Ramming a turret crashes the player like any enemy plane (destroys both).
        /// The hull also receives this callback (compound body) and guards on HasCrashed.</summary>
        private void OnTriggerEnter2D(Collider2D other) {
            if (_destroyed) return;
            var player = other.GetComponentInParent<PlayerPlane>();
            if (player == null || player.HasCrashed) return;

            other.GetComponentInParent<IDamageable>()?.ApplyDamage(1f, DamageSource.Collision);
            player.ForceCrash();
            ApplyDamage(1f, DamageSource.Collision);
        }

        // ── Setup (called by ZeppelinAI.Spawn) ────────────────────────────

        /// <summary>Builds a turret child of `owner` at `localOffset`. `mirrorX` mirrors the
        /// art to face the hull's flight direction; `underside` flips it to hang below.
        /// `firstShotDelay` staggers the turrets so they don't fire in one volley.</summary>
        internal static ZeppelinTurret Create(ZeppelinAI owner, Vector2 localOffset,
                                              bool mirrorX, bool underside, float firstShotDelay) {
            var go = new GameObject(ScoreKey);
            go.transform.SetParent(owner.transform, false);
            go.transform.localPosition = new Vector3(localOffset.x, localOffset.y, 0f);

            var tag = go.AddComponent<TeamTag>();
            tag.team = Team.Enemy;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = LoadSprite();
            sr.flipX = mirrorX;
            sr.flipY = underside;
            sr.sortingOrder = 5;   // ARCHITECTURE §3 draw order: enemies

            // Child collider without its own Rigidbody2D: it joins the hull's kinematic body.
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = HitboxSize;

            var turret = go.AddComponent<ZeppelinTurret>();
            turret._sr = sr;
            turret._owner = owner;
            turret._nextFireTime = Time.time + firstShotDelay;
            return turret;
        }

        // ── Firing ────────────────────────────────────────────────────────

        private bool IsInFireRange() {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return true;
            float halfWidth = _cam.orthographicSize * _cam.aspect + FireRangeMargin;
            return Mathf.Abs(transform.position.x - _cam.transform.position.x) <= halfWidth;
        }

        private void Fire() {
            Vector2 dir = PredictPosition() - transform.position;
            float dist = dir.magnitude;
            dir = dist > 0.01f ? dir / dist : Vector2.left;

            // Exact-direction overload: no forced climb (the zeppelin usually sits above the
            // player), and the hit is scaled by damageTaken.zeppelinTurret. A null return
            // (pool exhausted) just skips this shot, and its FlakFire cue (B033 / T953 B6).
            if (FlakShell.Spawn(transform.position, dir, DamageSource.ZeppelinTurret) != null) {
                var audio = AudioManager.GetOrNull();
                if (audio != null) audio.Play(SfxId.FlakFire, transform.position);
            }
        }

        /// <summary>Same straight-line intercept as AAAGun.PredictPosition: solves
        /// |D + v t| = S t for the earliest positive t, falling back to |D| / S.</summary>
        private Vector3 PredictPosition() {
            Vector3 playerPos = PlayerRef.transform.position;
            Vector2 vel = PlayerRef.Velocity;
            Vector2 d = (Vector2)(playerPos - transform.position);
            float s = FlakShell.ShellSpeed;

            float a = Vector2.Dot(vel, vel) - s * s;
            float b = 2f * Vector2.Dot(d, vel);
            float c = Vector2.Dot(d, d);
            float t = -1f;
            if (Mathf.Abs(a) < 1e-4f) {
                if (b < 0f) t = -c / b;
            } else {
                float disc = b * b - 4f * a * c;
                if (disc >= 0f) {
                    float sq = Mathf.Sqrt(disc);
                    float t1 = (-b - sq) / (2f * a);
                    float t2 = (-b + sq) / (2f * a);
                    float lo = Mathf.Min(t1, t2), hi = Mathf.Max(t1, t2);
                    t = lo > 0f ? lo : hi;
                }
            }
            if (t <= 0f || !float.IsFinite(t)) t = d.magnitude / s;

            return new Vector3(playerPos.x + vel.x * t, playerPos.y + vel.y * t, transform.position.z);
        }

        // ── Death ─────────────────────────────────────────────────────────

        private void Die() {
            if (_destroyed) return;
            _destroyed = true;

            // ScoreKeeper awards score.zeppelinTurret from this single event.
            GameEvents.RaiseEntityDestroyed(ScoreKey, Team.Enemy);
            FxManager.Instance.TriggerExplosion(transform.position);

            if (_owner != null) _owner.NotifyTurretDestroyed(this);
            Destroy(gameObject);
        }

        // ── Sprite ────────────────────────────────────────────────────────

        private static Sprite LoadSprite() {
            var sprite = Resources.Load<Sprite>("Sprites/enemies/zeppelin_turret");
            if (sprite != null) return sprite;

            if (!_warnedMissingSprite) {
                _warnedMissingSprite = true;
                Debug.LogWarning("ZeppelinTurret: 'Sprites/enemies/zeppelin_turret' not found — using a solid placeholder.");
            }
            if (_fallbackSprite == null) {
                int w = Mathf.Max(1, Mathf.RoundToInt(FallbackSize.x * FallbackPixelsPerUnit));
                int h = Mathf.Max(1, Mathf.RoundToInt(FallbackSize.y * FallbackPixelsPerUnit));
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
