using UnityEngine;
using Biplanes.Core;
using Biplanes.Audio;
using Biplanes.Player;

namespace Biplanes.Enemies {

    /// <summary>
    /// Recharge bomb balloon pickup (B064). Placed by mission JSON (type "bombBalloon") or
    /// spawned by MissionRunner a few seconds after the player's bay runs empty. It drifts like
    /// HealthBalloon; when the player flies through it (TeamTag == Player) the player's BombBay
    /// gains BombBalloonData.grant bombs (clamped to capacity, BombBay.AddBombs) and the balloon
    /// recycles itself, raising EntityDestroyed(nameof(BombBalloon), Neutral). Team.Neutral, so
    /// bullets and bombs fly through it (B011); it never counts toward objectives, enemy
    /// counters or EnemyPressure.
    /// </summary>
    public class BombBalloon : MonoBehaviour, IDamageable {

        // ── Constants ─────────────────────────────────────────────────────

        /// <summary>Ink white (#E8F1FA): white-only palette (GDD Style Guide, 2026-11-18).</summary>
        private static readonly Color Ink = new Color(0xE8 / 255f, 0xF1 / 255f, 0xFA / 255f, 1f);

        /// <summary>Resources path of the pickup sprite (art, B064).</summary>
        public const string SpritePath = "Sprites/pickups/bomb_balloon";

        private const float DRIFT_AMPLITUDE = 0.4f;
        private const float DRIFT_SPEED = 1.2f;

        // ── IDamageable contract ──────────────────────────────────────────

        public Team Team => Team.Neutral;

        /// <summary>True once the balloon has been collected and recycled.</summary>
        public bool IsDead { get; private set; }

        /// <summary>True when MissionRunner spawned it for an empty bay (not from mission JSON).</summary>
        public bool IsAutoSpawned { get; private set; }

        private Rigidbody2D _rb;
        private float _spawnTime;
        private int _id;

        void Awake() {
            var sr = GetComponent<SpriteRenderer>();
            if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();

            var sprite = Resources.Load<Sprite>(SpritePath);
            if (sprite != null) {
                sr.sprite = sprite;
            } else {
                Debug.LogWarning($"BombBalloon '{name}': '{SpritePath}' not found in Resources — "
                    + "using 1×1 white placeholder.");
                sr.sprite = CreateWhiteSprite();
            }
            sr.color = Ink;

            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) {
                _rb = gameObject.AddComponent<Rigidbody2D>();
                _rb.bodyType = RigidbodyType2D.Kinematic;
            }
        }

        void Start() {
            IsDead = false;
            _spawnTime = Time.time;
            _id = Random.Range(0, 999);
        }

        void FixedUpdate() {
            if (IsDead || _rb == null) return;
            // Gentle sinusoidal bob, same as HealthBalloon.
            float t = (Time.time - _spawnTime) * DRIFT_SPEED + _id;
            float offsetY = Mathf.Sin(t) * DRIFT_AMPLITUDE;
            Vector3 pos = transform.position;
            pos.y += offsetY * Time.fixedDeltaTime * 0.5f;
            _rb.MovePosition(pos);
        }

        // ── Trigger handling: only the player team collects ──────────────

        void OnTriggerEnter2D(Collider2D other) {
            if (IsDead) return;
            var tag = other.GetComponentInParent<TeamTag>();
            if (tag != null && tag.Team == Team.Player) Collect(tag);
        }

        /// <summary>IDamageable: pickups are immune to all damage (no-op).</summary>
        public void ApplyDamage(float percent, DamageSource src) { }

        /// <summary>IDamageable: a pickup isn't healable (no-op).</summary>
        public void Heal(float amount) { }

        // ── Collection ────────────────────────────────────────────────────

        /// <summary>Grants the bombs and recycles. A balloon taken with a full bay is still used up.</summary>
        private void Collect(TeamTag playerTag) {
            IsDead = true;

            var data = BombBalloonData.Resolve(GameBalance.Get());
            var bay = playerTag.GetComponentInParent<BombBay>();
            if (bay != null) bay.AddBombs(data.grant);

            var audio = AudioManager.GetOrNull();
            if (audio != null) audio.Play(SfxId.Pickup, transform.position);

            Recycle();
        }

        private void Recycle() {
            GameEvents.RaiseEntityDestroyed(nameof(BombBalloon), Team.Neutral);
            gameObject.SetActive(false);
        }

        // ── Static factory ────────────────────────────────────────────────

        /// <summary>Spawns a bomb balloon at `position` with a 1×1 trigger. `autoSpawned` marks
        /// MissionRunner's empty-bay recharge balloons.</summary>
        public static BombBalloon Spawn(Vector3 position, bool autoSpawned = false) {
            var go = new GameObject(nameof(BombBalloon));
            go.transform.position = position;
            var balloon = go.AddComponent<BombBalloon>();
            balloon.IsAutoSpawned = autoSpawned;

            var col = go.GetComponent<BoxCollider2D>();
            if (col == null) col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.0f, 1.0f);
            col.offset = Vector2.zero;
            return balloon;
        }

        // ── Pure helpers (EditMode tested) ────────────────────────────────

        /// <summary>World x of an auto balloon: `lead` units ahead of the player along `facing`,
        /// kept inside [minX, maxX] (the mission's playable span).</summary>
        public static float AutoSpawnX(float playerX, int facing, float lead, float minX, float maxX) {
            float x = playerX + (facing < 0 ? -1f : 1f) * lead;
            if (maxX < minX) maxX = minX;
            return Mathf.Clamp(x, minX, maxX);
        }

        /// <summary>World y of an auto balloon: the player's height kept inside the flight band
        /// [low, high], so flying straight on collects it.</summary>
        public static float AutoSpawnY(float playerY, float low, float high) {
            if (high < low) high = low;
            return Mathf.Clamp(playerY, low, high);
        }

        /// <summary>True when an uncollected balloon at `balloonX` already serves the player: on
        /// screen (within halfWidth of the camera x) or ahead of the player within `lead`.</summary>
        public static bool BlocksAutoSpawn(float balloonX, float playerX, int facing, float lead,
                                           float cameraX, float halfWidth) {
            if (Mathf.Abs(balloonX - cameraX) <= halfWidth) return true;
            float ahead = (balloonX - playerX) * (facing < 0 ? -1f : 1f);
            return ahead >= 0f && ahead <= lead;
        }

        /// <summary>Creates a minimal 1×1 ink-white sprite — never crash on missing assets.</summary>
        private static Sprite CreateWhiteSprite() {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Ink);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f, 1f);
        }

        // ── Empty-bay recharge timer (pure, driven by MissionRunner) ─────

        /// <summary>
        /// Empty-bay recharge timer (B064). OnBombsChanged(0) after a non-empty bay arms it;
        /// any refill (&gt; 0) cancels it. Tick adds scaled seconds and reports Due once `delay`
        /// has passed; it stays Due until MarkSpawned (so a blocked spawn retries next frame).
        /// After MarkSpawned, further empty reports are ignored until the bay holds bombs again:
        /// one spawn per empty-bay event.
        /// </summary>
        public sealed class RechargeTimer {
            private const float Epsilon = 1e-4f;

            private readonly float _delay;
            private float _elapsed;
            private bool _spawnedThisEmpty;

            public RechargeTimer(float delay) {
                _delay = delay > 0f ? delay : BombBalloonData.DefaultDelay;
            }

            /// <summary>Counting down to a spawn.</summary>
            public bool Armed { get; private set; }

            /// <summary>Seconds counted since the bay ran empty.</summary>
            public float Elapsed => _elapsed;

            /// <summary>The delay has passed and no spawn has been made yet.</summary>
            public bool Due => Armed && _elapsed >= _delay - Epsilon;

            /// <summary>Feed every BombsChanged value here.</summary>
            public void OnBombsChanged(int bombs) {
                if (bombs > 0) {
                    Cancel();
                    _spawnedThisEmpty = false;
                    return;
                }
                if (Armed || _spawnedThisEmpty) return;
                Armed = true;
                _elapsed = 0f;
            }

            /// <summary>Advances by `dt` scaled seconds; returns Due.</summary>
            public bool Tick(float dt) {
                if (!Armed) return false;
                if (dt > 0f) _elapsed += dt;
                return Due;
            }

            /// <summary>The balloon was spawned: disarm until the next empty-bay event.</summary>
            public void MarkSpawned() {
                Armed = false;
                _elapsed = 0f;
                _spawnedThisEmpty = true;
            }

            /// <summary>Stops counting (refill, crash, mission end).</summary>
            public void Cancel() {
                Armed = false;
                _elapsed = 0f;
            }

            /// <summary>Forget everything (new mission).</summary>
            public void Reset() {
                Cancel();
                _spawnedThisEmpty = false;
            }
        }
    }
}
