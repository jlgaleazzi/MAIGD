TASK: T949
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Audio/AudioManager.cs (new)
- Biplanes/Assets/Biplanes/Scripts/Audio/SfxId.cs (new)
- Biplanes/Assets/Biplanes/Scripts/Audio/AudioMix.cs (new)
- Biplanes/Assets/Biplanes/Scripts/Core/GameBalance.cs (AudioData class + `audio` field)
- Biplanes/Assets/Biplanes/Resources/Balance.json (`audio` block)
- Biplanes/Assets/Biplanes/Tests/EditMode/AudioMixTests.cs (new, 25 tests)
SUMMARY:
- Audio core per ARCHITECTURE §3 Audio. There are no gameplay hooks yet. SfxId has 23 values, one per clip. AudioMix holds the pure math. AudioManager is a FxManager-style singleton: a one-shot voice pool with priority stealing, a separate UI pool that ignores pause, keyed loops recycled through a free list, a timeScale 0 -> AudioListener.pause mirror, and a stop-all on scene unload.
- Clips load lazily once from Resources/Audio/sfx/<snake_name>. A missing clip logs one warning per id and the cue is skipped (Resources/Audio was not touched).
- Balance `audio` block, read through `AudioData.Resolve(GameBalance.Get())`. Defaults: master 0.8, sfx 1.0, music 0.6, pitchJitter 0.06, volumeJitter 0.15, offscreenVolume 0.4, maxVoices 12.
PUBLIC API (for T950-T952):
- `AudioManager.Instance`, `AudioManager.GetOrNull()` (null outside Play mode when none exists; DontDestroyOnLoad only in Play mode).
- `Play(SfxId id, Vector3? worldPos = null)`: gameplay one-shot. Dropped while timeScale is 0, or when every busy voice outranks it.
- `PlayUi(SfxId id)`: centred, plays while paused. Use it for UiClick/UiPage/UiPause/Typewriter/Stamp and for Win/Lose (GameManager may have frozen time).
- `StartLoop(SfxId, string key)`: restarting the same id under the same key is a no-op; a different id replaces it. Volume and pitch reset to 1.
- `StopLoop(key)`, `SetLoopPitch(key, p)` (clamped 0.1..3), `SetLoopVolume(key, v)` (0..1 factor of master*sfx), `StopAllLoops()`, `HasLoop(key)`, `LoopCount`, `Mix` (the resolved AudioData).
- `AudioMix`:
  - `ClipName(id)`, `ClipPath(id)`, `ClipCount`, `Priority(id)`.
  - Priority constants: Rattle 0 = PlayerGun/EnemyGun. Normal 1. Important 2 = HitPlayer/Crash/ExplosionBig/Pickup. Ui 3 = DamageWarning/Ui*/Typewriter/Stamp/Win/Lose.
  - `Pan(x01)`, `Jitter(rand01, amount)` (pitch, symmetric), `VolumeScale(rand01, amount)` (1 - r*amount), `IsOnScreen(vx, vy)`, `OffscreenFactor(vx, vy, offscreenVolume)`.
  - `EnginePitch(vy, damagePercent[, fullPitchSpeed])`: 1.0 level, 1.15 at full climb, 0.9 at full dive, linear in between. A steady drop of up to 4% at 100% damage. Deterministic: the irregular sputter at >= smokeThreshold is the caller's job.
  - `VoiceToSteal(int[] priorities, float[] startTimes[, int incomingPriority])`: the oldest voice of the lowest priority, or -1 when all voices outrank the incoming cue.
- `AudioData.Resolve(balance)`: never null. A missing or non-positive key falls back to its default (JsonUtility can't tell a missing key from 0, so to mute set a tiny value like 0.001). Volumes and jitters are clamped to 0..1, maxVoices to 1..32.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes (+1 new file(s) not yet in the csproj), PASS Biplanes.Editor, exit 0. `tools/unity.sh compile` was not run because the Unity editor has the project open.
- Test compile, scratch build in the session temp dir, nothing left in the project. Method: build the runtime DLL with the compile_check helpers, then build a temp copy of Biplanes.Tests.EditMode.csproj. In the copy, ProjectReferences and Compile items were stripped, it was retargeted v4.7.1 -> v4.7.2, the ScriptAssemblies DLLs and the fresh Biplanes.dll were referenced, and all 18 Tests/EditMode/*.cs were included. Result: 0 errors. The tests were not run.
- Run with Unity closed: `bash tools/unity.sh test`, then AudioMixTests (all 25 must pass). Or use Test Runner > EditMode > AudioMixTests in the editor.
- Play check once T948 clips and the T950 hooks land:
  - No AudioSource count growth across Retry: the AudioManager GameObject keeps maxVoices + 4 UI sources + one source per concurrent loop ever used.
  - Esc pause silences the engine and smoke loops, and UI clicks still sound.
  - Resume continues the loops.
- A missing clip logs exactly one "AudioManager: clip Resources/Audio/sfx/<name> not found" warning per id.
REQUESTS:
- orchestrator: update ARCHITECTURE.md §3 Audio to match the shipped behaviour:
  - Replace "Paused (timeScale 0): one-shots stop, loops pause" with: "AudioManager.Update mirrors timeScale 0 into AudioListener.pause: playing one-shots and loops pause and resume. New `Play` calls are dropped while paused. UI cues use `PlayUi(SfxId)` (own 4-voice pool, ignoreListenerPause) and still play. A scene unload (SceneManager.sceneUnloaded) stops every loop and gameplay one-shot; UI voices keep playing."
  - Add to the loop API: `SetLoopVolume(key, v)` (0..1 of master*sfx), `StopAllLoops()`, `HasLoop(key)`, `LoopCount`. StartLoop with the same id and key is a no-op.
  - AudioMix: add `ClipName/ClipPath/ClipCount`, `Priority(id)` with the 4 ranks above, `VolumeScale(rand, amount)`, `IsOnScreen`, `OffscreenFactor`, and the `EnginePitch(vy, damage, fullPitchSpeed)` overload (callers should pass `PitchData.Resolve(...).fullPitchSpeed`). `VoiceToSteal(priorities, startTimes, incomingPriority)` returns -1 when every voice outranks the new cue, so a gun rattle never steals a UI cue or warning.
  - Hooks note: GameManager win/lose and every UI-view cue go through `PlayUi`, not `Play`.
  - §4 Balance.json: add the `audio` block (mirrored as `AudioData`, `AudioData.Resolve`). Non-positive values fall back to defaults; volumes are clamped 0..1 and maxVoices 1..32.
- art (T948): the clip names must match `AudioMix.ClipName` exactly (the 23 snake_case names in ARCHITECTURE §3 Audio). Loops (engine_loop, zeppelin_hum, smoke_crackle) should loop seamlessly because AudioSource.loop is used.
