using System.Collections;
using UnityEngine;
using Biplanes.Core;
using Biplanes.Weapons;
using Biplanes.Player;

namespace Biplanes.Enemies {

    /// <summary>
    /// Anti-aircraft ground gun that fires bursts of flak shells toward the player's
    /// predicted position. Ground-bound (y = -4.5 per ARCHITECTURE §3). Destroyable
    /// only by bombs — bullets and flak bounce off harmlessly (GDD: "destroyed with bombs").
    ///
    /// Self-contained IDamageable implementation (no separate Health component) so that
    /// the interface method resolution on this GameObject always targets AAAGun itself,
    /// guaranteeing that damage-source filtering is never bypassed.
    /// </summary>
    public class AAAGun : MonoBehaviour, Core.IDamageable {

        // ── Serialized tunables ────────────────────────────────────────────

        [SerializeField] private float fireRate = 3f;              // seconds between bursts
        [SerializeField] private int burstCount = 3;               // shells per burst
        [SerializeField] private float shellInterval = 0.2f;       // delay between shells

        // ── Damage tracking (self-contained — no separate Health component) ─

        private float _currentDamagePercent = 0f;                  // 0 → 100

        // ── Cache (populated in Awake) ────────────────────────────────────

        private SpriteRenderer _sr;
        private Rigidbody2D _rb;
        private bool _wasAlive = true;                             // death-transition guard

        private const float GROUND_Y = -4.5f;                       // ground height per ARCHITECTURE §3
        private float _lastFireTime = -999f;                        // allow immediate first fire
        private Coroutine _fireCoroutine;

        /// <summary>Reference to the player plane, set by MissionRunner after spawn.</summary>
        internal PlayerPlane PlayerRef { get; set; }

        // ── IDamageable contract (intercepts all damage hits) ─────────────

        public Core.Team Team => _team;
        private readonly Core.Team _team = Core.Team.Enemy;

        public bool IsDead => _currentDamagePercent >= 100f;

        /// <summary>Current damage percentage — used by smoke-tint logic in LateUpdate.</summary>
        public float CurrentDamagePercent => _currentDamagePercent;

        /// <summary>Intercept every hit routed through IDamageable. Only Bomb-source
        /// damage is forwarded to the internal health tracker; Bullet and Flak are dropped.</summary>
        public void ApplyDamage(float percent, Core.DamageSource src) {
            if (IsDead) return;

            // ── Damage-source filter ────────────────────────────────────────
            // Only Bomb-type damage reaches the gun — bullets / flak bounce off.
            switch (src) {
                case Core.DamageSource.Bomb: {
                    float actualDamage = percent * GetMultiplier(src);
                    _currentDamagePercent = Mathf.Clamp(
                        _currentDamagePercent + actualDamage, 0f, 100f);
                    OnDamageTaken(Core.DamageSource.Bomb, actualDamage);
                    break;
                }

                case Core.DamageSource.Collision: {
                    float actualDamage = percent * GetMultiplier(src);
                    _currentDamagePercent = Mathf.Clamp(
                        _currentDamagePercent + actualDamage, 0f, 100f);
                    OnDamageTaken(Core.DamageSource.Collision, actualDamage);
                    break;
                }

                default:
                    // Bullet (player gun) and Flak (friendly shells) bounce off harmlessly.
                    break;
            }
        }

        public void Heal(float amount) { /* AAA guns are not healable */ }

        // ── Static factory ────────────────────────────────────────────────

        private static int _spawnCounter;                             // monotonically incrementing ID

        /// <summary>Create a AAA gun at `position` and return the AAAGun component.
        /// The GameObject is tagged with TeamTag so other systems can identify it.</summary>
        public static AAAGun Spawn(Vector3 position) {
            _spawnCounter++;
            // Name = Balance score key ("aaa"); EntityDestroyed is raised with gameObject.name.
            var go = new GameObject("aaa");
            go.transform.position = position;

            // Team tag for identification (ARCHITECTURE §2 rule 3).
            var tag = go.AddComponent<TeamTag>();
            tag.team = Core.Team.Enemy;

            // AAAGun itself — self-contained IDamageable.
            var gun = go.AddComponent<AAAGun>();

            // Sprite from Resources (graceful fallback to 1x1 white).
            var sr = go.AddComponent<SpriteRenderer>();
            var sprite = Resources.Load<Sprite>("Sprites/enemies/aaa_gun");
            if (sprite != null) {
                sr.sprite = sprite;
            } else {
                Debug.LogWarning(
                    $"AAAGun: missing sprite at 'Sprites/enemies/aaa_gun' — using 1x1 white.");
                sr.sprite = CreateFallbackSprite();
            }
            gun._sr = sr;

            // Kinematic rigidbody for any future movement (currently ground-bound).
            gun._rb = go.AddComponent<Rigidbody2D>();
            gun._rb.bodyType = RigidbodyType2D.Kinematic;

            return gun;
        }

        /// <summary>Called by MissionRunner after all entities are spawned.</summary>
        public void Init(PlayerPlane player) {
            PlayerRef = player;
        }

        // ── MonoBehaviour hooks ──────────────────────────────────────────

        private void Awake() {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        }

        private void LateUpdate() {
            // ── 1. Ground clamp: never leave the ground plane ──────────────
            var pos = transform.position;
            if (Mathf.Approximately(pos.y, GROUND_Y) == false) {
                pos.y = GROUND_Y;
                transform.position = pos;
            }

            // ── 2. Fire burst on cooldown ───────────────────────────────────
            if (PlayerRef != null && !IsDead) {
                float elapsed = Time.time - _lastFireTime;
                if (elapsed >= fireRate && _fireCoroutine == null) {
                    _fireCoroutine = StartCoroutine(FireBurstRoutine());
                }
            }

            // ── 3. Smoke tint at ≥70% damage ────────────────────────────────
            ApplySmokeTint();

            // ── 4. Death-transition detection ───────────────────────────────
            if (!IsDead && _wasAlive) {
                /* still alive */
            } else if (IsDead && _wasAlive) {
                _wasAlive = false;
                HandleDeath();
            }
        }

        private void OnDestroy() {
            if (_fireCoroutine != null) {
                StopCoroutine(_fireCoroutine);
                _fireCoroutine = null;
            }
        }

        // ── Burst fire routine ────────────────────────────────────────────

        /// <summary>Fire a burst of shells toward the player's predicted position, spaced
        /// by `shellInterval` seconds.</summary>
        private IEnumerator FireBurstRoutine() {
            for (int i = 0; i < burstCount && !IsDead; i++) {
                if (i > 0) yield return new WaitForSeconds(shellInterval);

                Vector3 predictedPos = PredictPosition();
                Vector2 dirToTarget = predictedPos - transform.position;
                float dist = dirToTarget.magnitude;
                if (dist > 0.01f) {
                    dirToTarget /= dist;
                } else {
                    // Player is directly overhead or unreachable — default leftward.
                    dirToTarget = new Vector2(-1f, 0f);
                }

                FlakShell.Spawn(transform.position, dirToTarget);
            }
            _fireCoroutine = null;
        }

        /// <summary>Predict where the player will be when a shell reaches their altitude.
        /// Uses player velocity for leading shots (not just current position).</summary>
        private Vector3 PredictPosition() {
            if (PlayerRef == null) return transform.position + Vector3.up * 5f;

            var playerPos = PlayerRef.transform.position;
            float dy = transform.position.y - playerPos.y; // positive when gun is below player.

            float shellSpeed = 6f;                         // matches FlakShell.verticalSpeed default.
            float timeToImpact = dy / shellSpeed;
            if (timeToImpact < 0f || !float.IsFinite(timeToImpact)) timeToImpact = 2f;

            var rb = PlayerRef.GetComponent<Rigidbody2D>();
            Vector2 vel = rb != null ? rb.linearVelocity : Vector2.zero;

            return new Vector3(playerPos.x + vel.x * timeToImpact,
                               playerPos.y + vel.y * timeToImpact,
                               transform.position.z);
        }

        // ── Damage multiplier (mirrors Health.GetDamageMultiplier) ──────────

        /// <summary>Converts damage points to percent of Balance enemyHealth.aaa.
        /// Collision is always fatal. Hit points work like Health enemy mode.</summary>
        private float GetMultiplier(Core.DamageSource src) {
            if (src == Core.DamageSource.Collision) return 100f;
            var balance = GameBalance.Get();
            float hp = balance != null && balance.enemyHealth != null ? balance.enemyHealth.aaa : 100f;
            return hp > 0f ? 100f / hp : 100f;
        }

        /// <summary>Called by ApplyDamage for sources that deal real damage (Bomb, Collision).</summary>
        private void OnDamageTaken(Core.DamageSource src, float amount) {
            // If this source deals enough to kill us, fire the events.
            if (IsDead) {
                GameEvents.RaiseEntityDestroyed(gameObject.name, Team);
            }

            // Bomb-source damage also raises a warning for gameplay feedback.
            if (src == Core.DamageSource.Bomb && _currentDamagePercent > 0f && _currentDamagePercent < 100f) {
                Debug.LogFormat("AAA gun hit by bomb — damage: {0:P1}", _currentDamagePercent / 100f);
            }
        }

        // ── Smoke tint helper ─────────────────────────────────────────────

        private void ApplySmokeTint() {
            if (_sr == null) return;

            float dmg = _currentDamagePercent;
            if (dmg >= 70f && dmg < 100f) {
                float intensity = (dmg - 70f) / 30f;   // 0..1 in this range.
                Color c = _sr.color;
                // Lerp toward dark smoky grey with alpha based on severity.
                c.r = Mathf.Lerp(c.r, 0.25f, intensity * Time.deltaTime);
                c.g = Mathf.Lerp(c.g, 0.22f, intensity * Time.deltaTime);
                c.b = Mathf.Lerp(c.b, 0.18f, intensity * Time.deltaTime);
                c.a = 0.4f + 0.3f * intensity;          // 0.4 → 0.7 alpha.
                _sr.color = c;
            } else if (dmg < 50f) {
                // Fade back to normal when health recovered below threshold.
                Color c2 = _sr.color;
                c2.r = Mathf.Lerp(c2.r, 1f, Time.deltaTime);
                c2.g = Mathf.Lerp(c2.g, 1f, Time.deltaTime);
                c2.b = Mathf.Lerp(c2.b, 1f, Time.deltaTime);
                c2.a = Mathf.Lerp(c2.a, 1f, Time.deltaTime);
                _sr.color = c2;
            }
        }

        // ── Death handling (called on death-transition) ───────────────────

        private void HandleDeath() {
            Debug.LogFormat("AAA gun destroyed at {0}", transform.position);

            // Award score from Balance.json.
            var balance = GameBalance.Get();
            int pts = balance != null && balance.scoreData != null
                      ? balance.scoreData.aaa : 150;
            ScoreKeeper.Instance?.AddPoints(pts);

            // Destroy game object (death cleanup, not update-loop pooling).
            Destroy(gameObject);
        }

        // ── Fallback sprite builder (in-memory, no asset dependency) ──────

        private static Sprite CreateFallbackSprite() {
            Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply(false);
            tex.name = "AAAGun_fallback";
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
        }

        // ── Debug helpers ─────────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected() {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 1.5f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 8f);
        }
#endif
    }
}
