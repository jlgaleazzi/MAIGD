TASK: T951
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs (engine and smoke loops, gun cue, crash cue, engine wind-down)
- Biplanes/Assets/Biplanes/Scripts/Player/BombBay.cs (BombRelease per bomb dropped)
- Biplanes/Assets/Biplanes/Scripts/Core/Health.cs (HitPlayer / HitEnemy, throttled)
- Biplanes/Assets/Biplanes/Scripts/Enemies/HealthBalloon.cs (Pickup)
- Biplanes/Assets/Biplanes/Scripts/Enemies/ZeppelinAI.cs (hum loop, hull HitEnemy, ExplosionBig on death, silent hull explosions)
- Biplanes/Assets/Biplanes/Scripts/UI/HudView.cs (DamageWarning on the upward smokeThreshold crossing)
- Biplanes/Assets/Biplanes/Tests/EditMode/AudioHooksPlayerTests.cs (new, 6 tests)
SUMMARY:
- Every hook goes through `AudioManager.GetOrNull()` with a null check, so nothing throws in EditMode or when a clip is missing.
- PlayerPlane:
  - The engine loop `player_engine` starts on the first frame. On the runway it idles at pitch 0.8 and volume 0.35. In flight it runs at volume 0.65, with pitch from `AudioMix.EnginePitch(vy, damage, pitch.fullPitchSpeed)`.
  - At damage >= smokeThreshold the engine sputters: Perlin noise in time, so the dips are brief, irregular and deterministic.
  - While the wreck falls, the pitch slides down to 0.45 over CrashFallSeconds and the volume halves. The loop stops when the wreck lands.
  - The smoke loop `player_smoke` plays at 0.5 while IsSmoking (flying or falling).
  - On MissionEnded the plane's loops stop, unless the plane is crashing. On OnDisable/teardown they stop too, guarded by a static owner so an old plane can't stop a new plane's engine.
- Crash timing:
  - The crash clip is 1.8 s with its crunch at 1.25 s. It starts when the predicted time-to-ground is <= 1.25 s, so the crunch lands with the wreck. If the wreck lands sooner, the clip starts at once.
  - It plays through `PlayUi`, not `Play`. The result screen sets timeScale 0 at 1.0 s and pauses the listener, so a gameplay voice would be cut before the crunch.
  - The landing explosion keeps its normal Explosion cue.
- Health:
  - HitPlayer for player damage > 0. HitEnemy for a non-fatal enemy hit.
  - At most one cue per 0.05 s per Health (`Health.HitCueMinInterval`). No cue for 0-damage hits, heals or ignored hits.
- ZeppelinAI:
  - Hum loop under a unique key `zeppelin_hum_<n>` (a static counter, not GetInstanceID), at volume 0.45. It fades to 0 over 8 units past the screen edge, and stops beyond that, on death, on despawn and in OnDisable.
  - Non-fatal hull hits play HitEnemy (throttled). Shielded hits are silent.
  - Death plays ExplosionBig once. The 3 hull explosions are called with `playSound: false`.
- HudView: DamageWarning is played with `PlayUi` (Ui priority) once per upward crossing of smokeThreshold. It re-arms below the threshold. A fatal jump to 100% plays no warning.
- Mix constants (per the art WAV report):
  - `PlayerPlane.EngineFlightVolume` 0.65, `EngineIdleVolume` 0.35, `EngineIdlePitch` 0.8, `SmokeCrackleVolume` 0.5.
  - `PlayerPlane.CrashClipSeconds` 1.8, `CrashCrunchOffset` 1.25.
  - `ZeppelinAI.HumVolume` 0.45, `HumFadeDistance` 8.
  - explosion_big is 2.6 s. Its timing needs no code: it plays as a one-shot at death.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes (+1 new file(s) not yet in the csproj), PASS Biplanes.Editor, exit 0. `tools/unity.sh compile` was not run because the Unity editor has the project open.
- Tests were compiled with the B018 scratch method, retargeted v4.7.1 -> v4.7.2: the runtime DLL was built with the compile_check helpers, then a temporary test csproj with all 20 Tests/EditMode/*.cs. 0 errors, and nothing was left in the project. The tests were not run.
- To run them with Unity closed: `bash tools/unity.sh test`, then AudioHooksPlayerTests (6) plus the existing suites (Health and PlaneCollision behaviour is unchanged).
- Playtest (needs the WAVs):
  - M1 runway: a low idle engine. After takeoff the pitch rises when climbing and falls when diving.
  - Holding fire gives one rattle per bullet. Each bomb gives one release click.
  - Getting hit gives the heavy crack. Hitting a fighter without killing it, or a bomber, gives the tick.
  - Crossing 70% damage gives one warning plus the crackle loop and engine coughs. A health balloon gives the pickup cue and drops the warning and crackle. Crossing 70% again warns again.
  - Crash: the engine winds down while falling, the crunch is roughly on impact and is heard over the result screen.
  - M5: the zeppelin hum fades in as it approaches. Killing it gives one big explosion, not stacked ones.
  - No loops survive Retry or Menu.
REQUESTS:
- orchestrator: ARCHITECTURE §3 Audio, Hooks, add these notes:
  - "The crash cue uses PlayUi so the result-screen freeze can't cut its crunch. It starts CrashCrunchOffset (1.25 s) before the predicted landing."
  - "Health throttles hit cues to one per 0.05 s per Health."
  - "The zeppelin hum key is `zeppelin_hum_<n>`, at volume 0.45, fading over 8 units off screen."
  - "Player loop volumes: engine 0.65 in flight, 0.35/pitch 0.8 idling, smoke 0.5."
- mechanics (follow-up, optional): ZeppelinTurret.cs was not in this brief. A non-fatal turret hit plays no HitEnemy, and turret death has no cue beyond FxManager's explosion. AAAGun is self-contained like the zeppelin, so it needs its own HitEnemy if T950 didn't add one.
