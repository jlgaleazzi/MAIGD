using UnityEngine;

namespace Biplanes.Fx {

    /// <summary>
    /// Simple visual-effects manager for explosions and impact VFX.
    ///
    /// Singleton — created on first access. Provides a unified hook so
    /// gameplay code (Bomb, Bullet, etc.) can request VFX without
    /// knowing the details of how they are rendered.
    /// </summary>
    public class FxManager : MonoBehaviour {

        // ── Singleton ────────────────────────────────────────────────────

        private static FxManager _instance;

        /// <summary>Singleton accessor.</summary>
        public static FxManager Instance {
            get {
                if (_instance == null) {
                    var go = new GameObject("FxManager");
                    _instance = go.AddComponent<FxManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        // ── Explosion pool (reuse temporary VFX objects) ────────────────

        private const int MaxExplosions = 20;
        private readonly GameObject[] _explosionPool = new GameObject[MaxExplosions];
        private int _activeExplosions = 0;

        // ── Unity lifecycle ──────────────────────────────────────────────

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Pre-create explosion slots (game objects are built on first use).
            for (int i = 0; i < MaxExplosions; ++i) {
                _explosionPool[i] = null;
            }
        }

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// Trigger an explosion effect at the given world position.
        /// Creates a temporary GameObject with a SpriteRenderer (loaded from
        /// Resources) that scales up and fades out over 0.5 seconds,
        /// then destroys itself.
        /// </summary>
        public void TriggerExplosion(Vector3 position) {
            if (_activeExplosions >= MaxExplosions) {
                // Pool exhausted — recycle the oldest one.
                RecycleOldest();
            }

            var go = new GameObject("Explosion");
            go.transform.position = position;
            go.layer = LayerMask.NameToLayer("UI") != -1 ? LayerMask.NameToLayer("UI") : 5;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 10; // Above terrain but below HUD.

            // Load the explosion sprite from Resources.
            Sprite explosionSprite = Resources.Load<Sprite>("Sprites/fx/explosion_0");

            if (explosionSprite != null) {
                sr.sprite = explosionSprite;
                sr.flipX = Random.value > 0.5f;
                sr.flipY = Random.value > 0.5f;

                // Start small and scale up.
                go.transform.localScale = Vector3.one * 0.1f;

                StartCoroutine(ExplodeFadeOut(go, explosionSprite));
            } else {
                Debug.LogWarning("FxManager: Missing sprite 'Sprites/fx/explosion_0', skipping VFX.");
                // Fallback: log instead of crashing — per ARCHITECTURE rules.
                Destroy(go);
            }

            _activeExplosions++;
        }

        /// <summary>Coroutine that scales up and fades out an explosion GameObject.</summary>
        private System.Collections.IEnumerator ExplodeFadeOut(GameObject go, Sprite sprite) {
            float elapsed = 0f;
            float duration = 0.5f;
            var sr = go.GetComponent<SpriteRenderer>();

            while (elapsed < duration) {
                float t = elapsed / duration;
                // Scale up from tiny → full size over the fade-out period.
                go.transform.localScale = Vector3.one * (0.1f + 1.9f * t);

                // Fade alpha: start fully visible, drop to zero.
                var col = sr.color;
                col.a = Mathf.Lerp(1f, 0f, t);
                sr.color = col;

                elapsed += Time.deltaTime;
                yield return null;
            }

            Destroy(go);
            _activeExplosions--;
        }

        /// <summary>Recycle the oldest active explosion (oldest first by creation order).</summary>
        private void RecycleOldest() {
            for (int i = 0; i < MaxExplosions; ++i) {
                if (_explosionPool[i] != null) {
                    Destroy(_explosionPool[i]);
                    _explosionPool[i] = null;
                    break;
                }
            }
        }
    }
}
