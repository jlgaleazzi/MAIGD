using System.Collections.Generic;
using Biplanes.Audio;
using UnityEngine;

namespace Biplanes.Fx {

    /// <summary>
    /// Visual-effects manager: frame-animated pencil FX (B030).
    ///
    /// Singleton, created on first access (DontDestroyOnLoad). Gameplay code asks for an
    /// effect at a world position and never deals with sprites:
    ///  • TriggerExplosion(pos): Sprites/fx/explosion_0..5, 0.08 s per frame (~0.5 s).
    ///  • TriggerFlakBurst(pos): Sprites/fx/flak_burst_0..3, 0.07 s per frame.
    ///  • SpawnSmokePuff(pos):   Sprites/fx/smoke_puff, rises, grows and fades over 1.2 s.
    ///  • SpawnEnemySmokePuff(pos): the same puff for damaged enemy fighters (B051), under its
    ///    own cap (Kind.EnemySmokePuff, 32) so enemy trails never starve the player's (24).
    /// Sound (T950): TriggerExplosion plays SfxId.Explosion and TriggerFlakBurst plays
    /// SfxId.FlakBurst at the position through AudioManager (optional playSound = false
    /// suppresses it). Smoke puffs are silent (the smoke loop belongs to PlayerPlane).
    ///
    /// Effects are scene-root GameObjects (sortingOrder 10, ARCHITECTURE draw order) animated
    /// from this manager's Update; frame math lives in FxFrames. Each kind has a live cap: at
    /// the cap the oldest effect of that kind is restarted at the new position. The live list
    /// is the only bookkeeping, and entries destroyed by a scene unload are pruned, so the
    /// counts can't drift. Sprites are loaded once; missing frames log one warning and are
    /// skipped (an effect with no frames at all is not shown).
    /// </summary>
    public class FxManager : MonoBehaviour {

        /// <summary>The effect kinds this manager plays.</summary>
        public enum Kind { Explosion, FlakBurst, SmokePuff, EnemySmokePuff }

        // ── Singleton ────────────────────────────────────────────────────

        private static FxManager _instance;

        /// <summary>Singleton accessor (creates the manager on first use).</summary>
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

        /// <summary>The manager if it exists, or a new one in Play mode. Null outside Play mode
        /// when none exists (EditMode tests), so cosmetic callers can skip the effect safely.</summary>
        public static FxManager GetOrNull() {
            if (_instance != null) return _instance;
            return Application.isPlaying ? Instance : null;
        }

        // ── Tunables (visual only, not gameplay) ─────────────────────────

        private const int SortingOrder = 10;

        private const int MaxExplosions = 20;
        private const int MaxFlakBursts = 16;
        private const int MaxSmokePuffs = 24;
        /// <summary>Enemy fighter smoke (B051) has its own cap, separate from the player's 24,
        /// so a sky of damaged fighters can never recycle the player's trail. At 32 a few
        /// heavily hit fighters still show trails; past it the oldest enemy puff restarts.</summary>
        private const int MaxEnemySmokePuffs = 32;

        private const string ExplosionPrefix = "Sprites/fx/explosion_";
        private const int ExplosionFrameCount = 6;
        private const float ExplosionFrameSeconds = 0.08f;
        private const float ExplosionFadeSeconds = 0.12f;   // the last ~1.5 frames
        private const float ExplosionScaleFrom = 0.85f;      // gentle grow: big upscales blur the hatching
        private const float ExplosionScaleTo = 1.15f;

        private const string FlakPrefix = "Sprites/fx/flak_burst_";
        private const int FlakFrameCount = 4;
        private const float FlakFrameSeconds = 0.07f;
        private const float FlakFadeSeconds = 0.1f;
        private const float FlakScaleFrom = 0.9f;
        private const float FlakScaleTo = 1.1f;

        private const string SmokePath = "Sprites/fx/smoke_puff";
        private const float SmokeLifetime = 1.2f;
        private const float SmokeRiseSpeed = 0.3f;           // world units per second, upward
        private const float SmokeScaleFrom = 1.0f;
        private const float SmokeScaleTo = 1.6f;
        private const float SmokeStartAlpha = 0.9f;

        // ── State ────────────────────────────────────────────────────────

        /// <summary>One live effect. Oldest first in _active.</summary>
        private sealed class Effect {
            public GameObject Go;
            public SpriteRenderer Sr;
            public Kind Kind;
            public Sprite[] Frames;
            public float FrameSeconds;
            public float Duration;
            public float FadeSeconds;
            public float ScaleFrom;
            public float ScaleTo;
            public float RiseSpeed;
            public float StartAlpha;
            public float Elapsed;
            public int ShownFrame;
        }

        private readonly List<Effect> _active = new List<Effect>();

        // Sprite caches, loaded lazily once (Awake doesn't run in EditMode tests).
        private Sprite[] _explosionFrames;
        private Sprite[] _flakFrames;
        private Sprite[] _smokeFrames;

        // ── Unity lifecycle ──────────────────────────────────────────────

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update() {
            Tick(Time.deltaTime);
        }

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>Explosion at `position`: explosion_0..5 at 0.08 s per frame (~0.5 s),
        /// random flipX/flipY, a gentle 0.85x→1.15x grow, alpha fade over the last frames,
        /// then removed. At most 20 live; past that the oldest restarts here.
        /// Plays SfxId.Explosion at `position` unless `playSound` is false (T950; e.g. a bomb
        /// ground impact plays its own BombImpact cue instead).</summary>
        public void TriggerExplosion(Vector3 position, bool playSound = true) {
            Play(Kind.Explosion, position);
            if (playSound) PlaySfx(SfxId.Explosion, position);
        }

        /// <summary>Flak airburst at `position`: flak_burst_0..3 at 0.07 s per frame, then
        /// removed. At most 16 live; past that the oldest restarts here.
        /// Plays SfxId.FlakBurst at `position` unless `playSound` is false (T950).</summary>
        public void TriggerFlakBurst(Vector3 position, bool playSound = true) {
            Play(Kind.FlakBurst, position);
            if (playSound) PlaySfx(SfxId.FlakBurst, position);
        }

        /// <summary>One smoke puff at `position` in world space: rises 0.3 u/s, grows
        /// 1.0x→1.6x and fades out over 1.2 s. At most 24 live; past that the oldest
        /// restarts here.</summary>
        public void SpawnSmokePuff(Vector3 position) {
            Play(Kind.SmokePuff, position);
        }

        /// <summary>One enemy smoke puff at `position` (B051, damaged FighterAI): looks and
        /// moves exactly like SpawnSmokePuff, but counts as Kind.EnemySmokePuff with its own
        /// cap of 32, so it never recycles a player puff. Past the cap the oldest enemy puff
        /// restarts here.</summary>
        public void SpawnEnemySmokePuff(Vector3 position) {
            Play(Kind.EnemySmokePuff, position);
        }

        /// <summary>Number of live effects of `kind` (destroyed ones are pruned first).</summary>
        public int CountActive(Kind kind) {
            Prune();
            int n = 0;
            for (int i = 0; i < _active.Count; i++) {
                if (_active[i].Kind == kind) n++;
            }
            return n;
        }

        // ── Playback ─────────────────────────────────────────────────────

        /// <summary>One-shot sound at `position` through AudioManager. Skipped when there is
        /// no manager (EditMode); a missing clip only warns once inside AudioManager.</summary>
        private static void PlaySfx(SfxId id, Vector3 position) {
            var audio = AudioManager.GetOrNull();
            if (audio != null) audio.Play(id, position);
        }

        private void Play(Kind kind, Vector3 position) {
            Sprite[] frames = FramesFor(kind);
            if (frames.Length == 0) return;   // warned once at load

            Prune();

            Effect e = null;
            if (CountOf(kind) >= CapFor(kind)) {
                // Recycle the oldest effect of this kind: it moves to the end of the list.
                for (int i = 0; i < _active.Count; i++) {
                    if (_active[i].Kind == kind) {
                        e = _active[i];
                        _active.RemoveAt(i);
                        break;
                    }
                }
            }

            if (e == null) {
                var go = new GameObject(NameFor(kind));
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = SortingOrder;
                e = new Effect { Go = go, Sr = sr, Kind = kind };
            }

            Configure(e, kind, frames);
            e.Go.transform.SetPositionAndRotation(position, Quaternion.identity);
            e.Sr.flipX = Random.value > 0.5f;
            // Smoke keeps its hatched underside at the bottom.
            e.Sr.flipY = !IsSmoke(kind) && Random.value > 0.5f;
            Apply(e);

            _active.Add(e);
        }

        private static void Configure(Effect e, Kind kind, Sprite[] frames) {
            e.Frames = frames;
            e.Elapsed = 0f;
            e.ShownFrame = -1;
            e.RiseSpeed = 0f;
            e.StartAlpha = 1f;
            switch (kind) {
                case Kind.Explosion:
                    e.FrameSeconds = ExplosionFrameSeconds;
                    e.Duration = FxFrames.Duration(frames.Length, ExplosionFrameSeconds);
                    e.FadeSeconds = ExplosionFadeSeconds;
                    e.ScaleFrom = ExplosionScaleFrom;
                    e.ScaleTo = ExplosionScaleTo;
                    break;
                case Kind.FlakBurst:
                    e.FrameSeconds = FlakFrameSeconds;
                    e.Duration = FxFrames.Duration(frames.Length, FlakFrameSeconds);
                    e.FadeSeconds = FlakFadeSeconds;
                    e.ScaleFrom = FlakScaleFrom;
                    e.ScaleTo = FlakScaleTo;
                    break;
                default: // SmokePuff / EnemySmokePuff: one frame held for the whole life
                    e.FrameSeconds = SmokeLifetime / frames.Length;
                    e.Duration = SmokeLifetime;
                    e.FadeSeconds = SmokeLifetime;
                    e.ScaleFrom = SmokeScaleFrom;
                    e.ScaleTo = SmokeScaleTo;
                    e.RiseSpeed = SmokeRiseSpeed;
                    e.StartAlpha = SmokeStartAlpha;
                    break;
            }
        }

        /// <summary>Advances every live effect by `dt`; finished ones are destroyed.</summary>
        private void Tick(float dt) {
            int write = 0;
            for (int read = 0; read < _active.Count; read++) {
                var e = _active[read];
                if (e.Go == null) continue;   // destroyed with its scene

                e.Elapsed += dt;
                if (FxFrames.IsDone(e.Elapsed, e.Duration)) {
                    Destroy(e.Go);
                    continue;
                }

                if (e.RiseSpeed != 0f) {
                    e.Go.transform.position += Vector3.up * (e.RiseSpeed * dt);
                }
                Apply(e);
                _active[write++] = e;
            }
            if (write < _active.Count) _active.RemoveRange(write, _active.Count - write);
        }

        /// <summary>Shows the frame, scale and alpha for the effect's elapsed time.</summary>
        private static void Apply(Effect e) {
            int frame = FxFrames.FrameIndex(e.Elapsed, e.Frames.Length, e.FrameSeconds);
            if (frame >= 0 && frame != e.ShownFrame) {
                e.Sr.sprite = e.Frames[frame];
                e.ShownFrame = frame;
            }

            float s = FxFrames.Scale(e.Elapsed, e.Duration, e.ScaleFrom, e.ScaleTo);
            e.Go.transform.localScale = new Vector3(s, s, 1f);

            var col = e.Sr.color;
            col.a = e.StartAlpha * FxFrames.FadeOutAlpha(e.Elapsed, e.Duration, e.FadeSeconds);
            e.Sr.color = col;
        }

        /// <summary>Drops entries whose GameObject was destroyed (scene unload).</summary>
        private void Prune() {
            for (int i = _active.Count - 1; i >= 0; i--) {
                if (_active[i].Go == null) _active.RemoveAt(i);
            }
        }

        private int CountOf(Kind kind) {
            int n = 0;
            for (int i = 0; i < _active.Count; i++) {
                if (_active[i].Kind == kind) n++;
            }
            return n;
        }

        private static int CapFor(Kind kind) {
            switch (kind) {
                case Kind.Explosion: return MaxExplosions;
                case Kind.FlakBurst: return MaxFlakBursts;
                case Kind.EnemySmokePuff: return MaxEnemySmokePuffs;
                default: return MaxSmokePuffs;
            }
        }

        /// <summary>The two smoke kinds share the sprite, motion and fade.</summary>
        private static bool IsSmoke(Kind kind) {
            return kind == Kind.SmokePuff || kind == Kind.EnemySmokePuff;
        }

        private static string NameFor(Kind kind) {
            switch (kind) {
                case Kind.Explosion: return "Explosion";
                case Kind.FlakBurst: return "FlakBurst";
                default: return "SmokePuff";   // player and enemy smoke look alike
            }
        }

        // ── Sprite loading (once per kind) ───────────────────────────────

        private Sprite[] FramesFor(Kind kind) {
            switch (kind) {
                case Kind.Explosion:
                    if (_explosionFrames == null) _explosionFrames = LoadFrames(ExplosionPrefix, ExplosionFrameCount);
                    return _explosionFrames;
                case Kind.FlakBurst:
                    if (_flakFrames == null) _flakFrames = LoadFrames(FlakPrefix, FlakFrameCount);
                    return _flakFrames;
                default:
                    if (_smokeFrames == null) _smokeFrames = LoadSingle(SmokePath);
                    return _smokeFrames;
            }
        }

        /// <summary>Loads prefix0..prefix(count-1) in order, keeping the frames that exist.
        /// Logs one warning listing any missing frame. Never null.</summary>
        private static Sprite[] LoadFrames(string prefix, int count) {
            var frames = new List<Sprite>(count);
            string missing = null;
            for (int i = 0; i < count; i++) {
                string path = prefix + i;
                var sprite = Resources.Load<Sprite>(path);
                if (sprite != null) {
                    frames.Add(sprite);
                } else {
                    missing = missing == null ? path : missing + ", " + path;
                }
            }
            if (missing != null) {
                Debug.LogWarning(frames.Count > 0
                    ? $"FxManager: missing FX frame(s) {missing}; playing the {frames.Count} that exist."
                    : $"FxManager: missing FX frame(s) {missing}; this effect is skipped.");
            }
            return frames.ToArray();
        }

        private static Sprite[] LoadSingle(string path) {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null) {
                Debug.LogWarning($"FxManager: missing FX sprite {path}; this effect is skipped.");
                return new Sprite[0];
            }
            return new[] { sprite };
        }
    }
}
