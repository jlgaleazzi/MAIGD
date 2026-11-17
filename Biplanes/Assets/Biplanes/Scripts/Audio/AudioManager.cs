using System.Collections.Generic;
using Biplanes.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Biplanes.Audio {

    /// <summary>
    /// Sound-effect player (T949, ARCHITECTURE §3 Audio).
    ///
    /// Singleton like FxManager: Instance creates it on first use (DontDestroyOnLoad in Play
    /// mode only); GetOrNull() returns null outside Play mode when none exists, so cosmetic
    /// callers write <c>var a = AudioManager.GetOrNull(); if (a != null) a.Play(...)</c>.
    ///
    ///  • Play(id, worldPos): a 2D one-shot from a pool of audio.maxVoices AudioSources on this
    ///    GameObject. Pitch 1 +/- pitchJitter, volume master * sfx * (1 - 0..volumeJitter). With
    ///    a world position it pans by the camera-viewport x (AudioMix.Pan) and is scaled by
    ///    offscreenVolume outside the view. When every voice is busy, AudioMix.VoiceToSteal
    ///    picks the oldest voice of the lowest priority; a cue that ranks below every busy
    ///    voice is dropped. Dropped too while paused (timeScale 0).
    ///  • PlayUi(id): UI cues on their own small pool with ignoreListenerPause, so they play
    ///    while the game is paused. Use it for click/page/pause/typewriter/stamp/win/lose.
    ///  • Loops by key: StartLoop / StopLoop / SetLoopPitch / SetLoopVolume / StopAllLoops.
    ///    Loop sources are recycled through a free list, so nothing leaks.
    ///  • Pause: Update mirrors timeScale 0 into AudioListener.pause (one-shots and loops
    ///    pause, UI cues don't). A scene unload (Retry, menu) stops every loop, every gameplay
    ///    one-shot and every UI voice playing a non-screen cue such as Crash (B032). UI-screen
    ///    cues (click, page, pause, typewriter, stamp, win, lose) keep playing, so the click on
    ///    the Retry button is heard (KeepsPlayingOnSceneUnload).
    ///  • Listener (B032): the scenes have no AudioListener, so in Play mode the manager keeps
    ///    exactly one enabled. On init and after every scene load it counts the enabled
    ///    listeners other than its own. With none it enables its own (added once to this
    ///    DontDestroyOnLoad object); otherwise it disables its own (NeedsListener). EditMode
    ///    never adds one. Every source is 2D, so the listener's position doesn't matter.
    ///  • Quitting (B032): once the application quits (or Play mode exits), Instance and
    ///    GetOrNull return null in Play mode, so OnDisable/OnDestroy stop paths during teardown
    ///    can never create a new manager. The flag resets at the next Play start.
    ///
    /// Clips load lazily once from Resources/Audio/sfx/&lt;AudioMix.ClipName(id)&gt;; a missing
    /// clip logs one warning per id and the cue is skipped, never thrown.
    /// </summary>
    public class AudioManager : MonoBehaviour {

        // ── Singleton ────────────────────────────────────────────────────

        private static AudioManager _instance;

        /// <summary>Set by Application.quitting / OnApplicationQuit (also on Play-mode exit in
        /// the editor); reset at the start of every Play session.</summary>
        private static bool _quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() {
            _quitting = false;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting() {
            _quitting = true;
        }

        /// <summary>True when an accessor may hand out (or create) a manager: always in EditMode,
        /// and in Play mode until the application starts quitting. Pure, for tests.</summary>
        public static bool MayAccess(bool isPlaying, bool quitting) {
            return !(isPlaying && quitting);
        }

        /// <summary>Singleton accessor (creates the manager on first use). Null in Play mode once
        /// the application is quitting, so teardown code never spawns a new GameObject.</summary>
        public static AudioManager Instance {
            get {
                if (!MayAccess(Application.isPlaying, _quitting)) return null;
                if (_instance == null) {
                    var go = new GameObject("AudioManager");
                    _instance = go.AddComponent<AudioManager>();
                    if (Application.isPlaying) DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        /// <summary>The manager if it exists, or a new one in Play mode. Null outside Play mode
        /// when none exists (EditMode tests), so cosmetic callers can skip the cue safely.
        /// Null in Play mode once the application is quitting (B032).</summary>
        public static AudioManager GetOrNull() {
            if (!MayAccess(Application.isPlaying, _quitting)) return null;
            if (_instance != null) return _instance;
            return Application.isPlaying ? Instance : null;
        }

        // ── Tunables (mix plumbing, not gameplay) ────────────────────────

        private const int UiVoiceCount = 4;
        /// <summary>Marks a UI voice with no tracked cue.</summary>
        private const SfxId NoCue = (SfxId)(-1);
        private const float MinLoopPitch = 0.1f;
        private const float MaxLoopPitch = 3f;

        // Unity's own AudioSource.priority (0 = most important, 256 = least), used when the
        // platform runs out of real voices.
        private const int UnityPriorityUi = 0;
        private const int UnityPriorityLoop = 64;
        private const int UnityPriorityOneShot = 128;

        // ── State ────────────────────────────────────────────────────────

        private sealed class Loop {
            public AudioSource Src;
            public SfxId Id;
            public float Volume = 1f;
        }

        private bool _initialised;
        private bool _subscribed;
        private bool _listenerPaused;

        private AudioData _mix;

        private AudioSource[] _voices;
        private int[] _voicePriority;
        private float[] _voiceOrder;
        private float _playCounter;

        private AudioSource[] _uiVoices;
        private float[] _uiOrder;
        private SfxId[] _uiIds;

        private AudioListener _ownListener;

        private readonly Dictionary<string, Loop> _loops = new Dictionary<string, Loop>();
        private readonly List<string> _loopKeyScratch = new List<string>();
        private readonly Stack<AudioSource> _freeLoopSources = new Stack<AudioSource>();

        private AudioClip[] _clips;
        private bool[] _clipTried;

        private Camera _camera;

        // ── Unity lifecycle ──────────────────────────────────────────────

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
            EnsureInit();
        }

        private void Update() {
            SyncPause();
        }

        private void OnApplicationQuit() {
            _quitting = true;
        }

        private void OnDestroy() {
            if (_subscribed) {
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _subscribed = false;
            }
            if (_listenerPaused) {
                AudioListener.pause = false;
                _listenerPaused = false;
            }
            if (_instance == this) _instance = null;
        }

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>The resolved audio block (volumes, jitter, voices). Never null.</summary>
        public AudioData Mix {
            get {
                EnsureInit();
                return _mix;
            }
        }

        /// <summary>Plays a gameplay one-shot. worldPos pans it by screen x and quiets it when
        /// off-screen; without it the cue is centred at full volume.</summary>
        public void Play(SfxId id, Vector3? worldPos = null) {
            EnsureInit();
            SyncPause();
            if (_listenerPaused) return;
            AudioClip clip = GetClip(id);
            if (clip == null) return;

            int priority = AudioMix.Priority(id);
            int voice = FreeVoice();
            if (voice < 0) voice = AudioMix.VoiceToSteal(_voicePriority, _voiceOrder, priority);
            if (voice < 0) return;

            float volume = ChannelVolume() * AudioMix.VolumeScale(Random.value, _mix.volumeJitter);
            float pan = 0f;
            if (worldPos.HasValue) {
                Camera cam = GetCamera();
                if (cam != null) {
                    Vector3 vp = cam.WorldToViewportPoint(worldPos.Value);
                    pan = AudioMix.Pan(vp.x);
                    volume *= AudioMix.OffscreenFactor(vp.x, vp.y, _mix.offscreenVolume);
                }
            }

            AudioSource src = _voices[voice];
            src.Stop();
            src.clip = clip;
            src.volume = volume;
            src.pitch = AudioMix.Jitter(Random.value, _mix.pitchJitter);
            src.panStereo = pan;
            src.Play();
            _voicePriority[voice] = priority;
            _voiceOrder[voice] = ++_playCounter;
        }

        /// <summary>Plays a UI cue, centred, that is heard even while the game is paused.</summary>
        public void PlayUi(SfxId id) {
            EnsureInit();
            AudioClip clip = GetClip(id);
            if (clip == null) return;

            int voice = 0;
            for (int i = 0; i < _uiVoices.Length; i++) {
                if (!_uiVoices[i].isPlaying) { voice = i; break; }
                if (_uiOrder[i] < _uiOrder[voice]) voice = i;
            }

            AudioSource src = _uiVoices[voice];
            src.Stop();
            src.clip = clip;
            src.volume = ChannelVolume() * AudioMix.VolumeScale(Random.value, _mix.volumeJitter);
            src.pitch = AudioMix.Jitter(Random.value, _mix.pitchJitter);
            src.panStereo = 0f;
            src.Play();
            _uiOrder[voice] = ++_playCounter;
            _uiIds[voice] = id;
        }

        /// <summary>Whether a UI-pool cue keeps playing through a scene unload. UI-screen cues
        /// (the Retry/Menu click, page turns, pause, typewriter, stamp, win, lose) do. Any other
        /// cue routed through PlayUi (Crash) is a gameplay tail and stops (B032).</summary>
        public static bool KeepsPlayingOnSceneUnload(SfxId id) {
            switch (id) {
                case SfxId.UiClick:
                case SfxId.UiPage:
                case SfxId.UiPause:
                case SfxId.Typewriter:
                case SfxId.Stamp:
                case SfxId.Win:
                case SfxId.Lose:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Whether the manager's own AudioListener must be enabled, given how many
        /// other enabled AudioListeners exist: only when there are none, so exactly one is
        /// ever enabled (B032). Pure, for tests.</summary>
        public static bool NeedsListener(int existingEnabledCount) {
            return existingEnabledCount <= 0;
        }

        /// <summary>Starts (or keeps) a looping cue under key. Starting a key that already plays
        /// the same id does nothing; a different id replaces it. Volume and pitch reset to 1.</summary>
        public void StartLoop(SfxId id, string key) {
            EnsureInit();
            if (string.IsNullOrEmpty(key)) return;
            AudioClip clip = GetClip(id);
            if (clip == null) return;

            if (_loops.TryGetValue(key, out Loop loop)) {
                if (loop.Id == id && loop.Src.clip == clip) {
                    if (!loop.Src.isPlaying) loop.Src.Play();
                    return;
                }
                loop.Src.Stop();
            } else {
                loop = new Loop { Src = TakeLoopSource() };
                _loops[key] = loop;
            }

            loop.Id = id;
            loop.Volume = 1f;
            AudioSource src = loop.Src;
            src.clip = clip;
            src.loop = true;
            src.pitch = 1f;
            src.panStereo = 0f;
            src.volume = ChannelVolume();
            src.Play();
        }

        /// <summary>Stops the loop under key and recycles its source. Unknown keys are ignored.</summary>
        public void StopLoop(string key) {
            if (string.IsNullOrEmpty(key)) return;
            if (!_loops.TryGetValue(key, out Loop loop)) return;
            _loops.Remove(key);
            ReleaseLoopSource(loop.Src);
        }

        /// <summary>Sets a loop's pitch (clamped 0.1..3). Unknown keys are ignored.</summary>
        public void SetLoopPitch(string key, float pitch) {
            if (string.IsNullOrEmpty(key)) return;
            if (!_loops.TryGetValue(key, out Loop loop)) return;
            loop.Src.pitch = Mathf.Clamp(pitch, MinLoopPitch, MaxLoopPitch);
        }

        /// <summary>Sets a loop's volume as a 0..1 factor of master * sfx. Unknown keys are ignored.</summary>
        public void SetLoopVolume(string key, float volume) {
            if (string.IsNullOrEmpty(key)) return;
            if (!_loops.TryGetValue(key, out Loop loop)) return;
            loop.Volume = Mathf.Clamp01(volume);
            loop.Src.volume = ChannelVolume() * loop.Volume;
        }

        /// <summary>True while a loop is registered under key.</summary>
        public bool HasLoop(string key) {
            return !string.IsNullOrEmpty(key) && _loops.ContainsKey(key);
        }

        /// <summary>Number of registered loops.</summary>
        public int LoopCount => _loops.Count;

        /// <summary>Stops every loop and recycles their sources.</summary>
        public void StopAllLoops() {
            if (_loops.Count == 0) return;
            _loopKeyScratch.Clear();
            _loopKeyScratch.AddRange(_loops.Keys);
            for (int i = 0; i < _loopKeyScratch.Count; i++) StopLoop(_loopKeyScratch[i]);
            _loopKeyScratch.Clear();
        }

        // ── Internals ────────────────────────────────────────────────────

        private void EnsureInit() {
            if (_initialised) return;
            _initialised = true;

            _mix = AudioData.Resolve(GameBalance.Get());

            int count = _mix.maxVoices;
            _voices = new AudioSource[count];
            _voicePriority = new int[count];
            _voiceOrder = new float[count];
            for (int i = 0; i < count; i++) {
                _voices[i] = CreateSource(UnityPriorityOneShot, false);
            }

            _uiVoices = new AudioSource[UiVoiceCount];
            _uiOrder = new float[UiVoiceCount];
            _uiIds = new SfxId[UiVoiceCount];
            for (int i = 0; i < UiVoiceCount; i++) {
                _uiVoices[i] = CreateSource(UnityPriorityUi, true);
                _uiIds[i] = NoCue;
            }

            int clipCount = System.Enum.GetValues(typeof(SfxId)).Length;
            _clips = new AudioClip[clipCount];
            _clipTried = new bool[clipCount];

            if (Application.isPlaying && !_subscribed) {
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                SceneManager.sceneLoaded += OnSceneLoaded;
                _subscribed = true;
            }
            EnsureListener();
        }

        /// <summary>Play mode only: keeps exactly one enabled AudioListener. Counts the enabled
        /// listeners other than the manager's own (one Find on init and per scene load, never
        /// per frame) and adds the manager's own listener the first time one is needed.</summary>
        private void EnsureListener() {
            if (!Application.isPlaying) return;
            int others = 0;
            AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            for (int i = 0; i < listeners.Length; i++) {
                AudioListener l = listeners[i];
                if (l != null && l != _ownListener && l.enabled) others++;
            }
            bool need = NeedsListener(others);
            if (need && _ownListener == null) {
                _ownListener = gameObject.AddComponent<AudioListener>();
            }
            if (_ownListener != null) _ownListener.enabled = need;
        }

        private AudioSource CreateSource(int unityPriority, bool ignorePause) {
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
            src.priority = unityPriority;
            src.ignoreListenerPause = ignorePause;
            return src;
        }

        private AudioSource TakeLoopSource() {
            while (_freeLoopSources.Count > 0) {
                AudioSource src = _freeLoopSources.Pop();
                if (src != null) return src;
            }
            return CreateSource(UnityPriorityLoop, false);
        }

        private void ReleaseLoopSource(AudioSource src) {
            if (src == null) return;
            src.Stop();
            src.clip = null;
            src.loop = false;
            _freeLoopSources.Push(src);
        }

        private float ChannelVolume() {
            return _mix.master * _mix.sfx;
        }

        private int FreeVoice() {
            for (int i = 0; i < _voices.Length; i++) {
                if (!_voices[i].isPlaying) return i;
            }
            return -1;
        }

        private AudioClip GetClip(SfxId id) {
            int i = (int)id;
            if (i < 0 || i >= _clips.Length) return null;
            if (!_clipTried[i]) {
                _clipTried[i] = true;
                string path = AudioMix.ClipPath(id);
                _clips[i] = path != null ? Resources.Load<AudioClip>(path) : null;
                if (_clips[i] == null) {
                    Debug.LogWarning("AudioManager: clip Resources/" + path + " not found; " + id
                        + " will be silent.");
                }
            }
            return _clips[i];
        }

        private Camera GetCamera() {
            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            return _camera;
        }

        private void SyncPause() {
            bool paused = Time.timeScale <= 0f;
            if (paused == _listenerPaused) return;
            _listenerPaused = paused;
            AudioListener.pause = paused;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            EnsureListener();
        }

        private void OnSceneUnloaded(Scene scene) {
            StopAllLoops();
            if (_voices != null) {
                for (int i = 0; i < _voices.Length; i++) {
                    if (_voices[i] != null) _voices[i].Stop();
                }
            }
            // The crash tail (a gameplay cue on the UI pool) must not carry into the next
            // mission. UI-screen cues such as the Retry click keep playing (B032).
            if (_uiVoices != null && _uiIds != null) {
                for (int i = 0; i < _uiVoices.Length; i++) {
                    if (_uiVoices[i] == null || _uiIds[i] == NoCue) continue;
                    if (KeepsPlayingOnSceneUnload(_uiIds[i])) continue;
                    _uiVoices[i].Stop();
                    _uiIds[i] = NoCue;
                }
            }
            _camera = null;
        }
    }
}
