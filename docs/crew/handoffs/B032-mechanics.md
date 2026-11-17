TASK: B032
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Audio/AudioManager.cs
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs
- Biplanes/Assets/Biplanes/Scripts/Player/BombBay.cs
- Biplanes/Assets/Biplanes/Scripts/Enemies/ZeppelinAI.cs (comment only; the B7 fix lives in AudioManager.GetOrNull)
- Biplanes/Assets/Biplanes/Tests/EditMode/AudioListenerTests.cs (new)
SUMMARY:
- B1: in Play mode only, AudioManager keeps exactly one enabled AudioListener. On init and on every `sceneLoaded`, it counts the enabled listeners other than its own (one `FindObjectsByType`, never per frame). `NeedsListener(count)` decides: with none, it enables its own listener, which is added once to the DontDestroyOnLoad manager. Otherwise it disables its own, so a scene that later brings a camera listener never gives two. EditMode adds nothing.
- B2: crash.wav is now treated as a crunch at 0.1 s (`CrashClipSeconds` 1.0, `CrashCrunchOffset` 0.1). The new `PlayerPlane.ShouldStartCrashSound(timeToGround, elapsed)` starts it when the time to ground is <= 0.1 s, so the crunch lands with the wreck. As a fallback it also starts at elapsed >= `CrashFallSeconds` - 0.1. A top-altitude fall (about 1.12 s) is frozen by the result screen at 1.0 s before it lands, so without this the crash would be silent; with it, the crunch marks the result opening. The crash still plays once, via PlayUi, and the landing fallback is unchanged.
- B3: AudioManager tracks the SfxId on each UI voice. On `sceneUnloaded` it stops UI voices whose cue fails `KeepsPlayingOnSceneUnload(id)`. Click, page, pause, typewriter, stamp, win and lose keep playing. Crash (and any other non-screen PlayUi cue) stops.
- B7: there is a static `_quitting` flag, set by `Application.quitting` and `OnApplicationQuit` and reset at SubsystemRegistration on each Play start. `Instance` and `GetOrNull()` return null when `!MayAccess(Application.isPlaying, _quitting)`, which happens only in Play mode while quitting. EditMode is never blocked, even if the flag is stale. PlayerPlane.StopPlayerLoops and ZeppelinAI.StopHum already used GetOrNull, so they can no longer create a manager at quit.
- B8: PlayerGun plays only when `Bullet.Spawn` returns non-null. `BombBay.TryDropBomb` now calls `Bomb.Spawn` first. When it returns null (pool exhausted), it returns false: no bomb is spent, no BombRelease plays and BombsChanged isn't raised.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes (+1 new file(s) not yet in the csproj), PASS Biplanes.Editor, exit 0. The `tools/unity.sh compile` step wasn't run because Unity has the project open.
- Tests were compiled, not run. I used the B018 scratch method via QA's T953 script: Biplanes.dll built from source, then a temp copy of Biplanes.Tests.EditMode.csproj with ProjectReferences and Compile items stripped, retargeted v4.7.1 -> v4.7.2, with all 22 Tests/EditMode/*.cs. Result: runtime PASS, tests (22 files) PASS. No temp csproj is left in the project.
- With Unity closed, run `bash tools/unity.sh test`. Expect AudioListenerTests (8 tests) plus the existing AudioManagerQaTests and AudioHooksPlayerTests to pass.
- Playtest (Play mode):
  - The Console shows no "no audio listeners" message, and cues are audible.
  - The Hierarchy has one AudioListener, on DontDestroyOnLoad/AudioManager, and it stays one across Menu -> Mission -> Retry.
  - Crash low: the crunch coincides with the wreck landing and its explosion.
  - Crash from the top clamp: the crunch plays as the result screen opens.
  - Crash, then Retry at once: no crunch tail on the new runway, and the Retry click is still heard.
  - Stop Play mode: no "objects were not cleaned up" message, and no stray AudioManager.
REQUESTS:
- orchestrator: in ARCHITECTURE.md §3 "Audio (Audio/)":
  - Add a bullet: "Listener: the scenes have no AudioListener. In Play mode only, AudioManager keeps exactly one enabled. On init and after every `sceneLoaded`, it counts the enabled AudioListeners other than its own. `NeedsListener(count)` (true only when count is 0) enables its own listener (added once to the DontDestroyOnLoad manager), otherwise disables it, so a scene that brings a camera listener never gives two. EditMode never adds one. All sources are 2D, so the listener position doesn't matter. Do not add an AudioListener in ProjectBuilder."
  - Add a bullet: "Quitting: a static flag set by `Application.quitting` / `OnApplicationQuit` (reset at each Play start) makes `Instance` and `GetOrNull()` return null in Play mode while the application quits (`MayAccess(isPlaying, quitting)`). OnDisable/OnDestroy stop paths must use `GetOrNull()` and so never create a manager during teardown."
  - Pause bullet, replace the last sentence with: "A scene unload (`SceneManager.sceneUnloaded`) stops every loop and gameplay one-shot, and every UI voice playing a non-screen cue (Crash). UI-screen cues (`KeepsPlayingOnSceneUnload`: UiClick, UiPage, UiPause, Typewriter, Stamp, Win, Lose) keep playing."
  - Player and damage (T951), replace "starts 1.25 s before the predicted landing so the crunch lands with the wreck" with: "crash.wav is a crunch at 0.1 s (clip about 1.0 s; the engine loop's wind-down is the whine). It starts 0.1 s before the predicted landing (`ShouldStartCrashSound`), or at elapsed `CrashFallSeconds` - 0.1 when the result-screen freeze would cut a longer fall, so the crunch then marks the result screen."
  - Hooks: add "PlayerGun / BombRelease play only for a bullet or bomb actually spawned. A null Bomb.Spawn doesn't spend a bomb."
- art: the constants assume the re-cut crash.wav has its crunch at about 0.1 s and lasts about 1.0 s. If the cut differs, tell mechanics the exact crunch offset.
- qa: no changes to existing tests are needed. AudioHooksPlayerTests.CrashClipTiming still holds (1.0 > 0.1 > 0). The T953 manual checklist item about the crunch timing needs updating to the new rule above.
