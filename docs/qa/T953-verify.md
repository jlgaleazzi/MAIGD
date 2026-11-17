# T953: verification of milestone M6 audio (T948 clips, T949 core, T950-T952 hooks)

**Verdict: FAIL.** 1 P0, 0 P1, 7 P2. (Re-verified in T955 after B032/B033/B034: all 8
closed; see `docs/qa/T955-verify.md`. The checklist below was updated to the fixed rules.)
The P0: neither scene has an AudioListener, and no code adds one, so nothing in the game is
audible. Apart from that, the code is sound: the clips, the core, the loops, the owner guard
and the once-per-event hooks all hold up. The P2s are about timing, doubled cues and
coverage. Unity is open, so the EditMode tests were compiled but not run.

## What was checked
- Read:
  - Handoffs T948-art, T949, T950, T951, T952 and B018 (the test-compile method).
  - GDD Style Guide "Sound effects", ARCHITECTURE §3 Audio and §4 `audio`.
  - Scripts/Audio/*.cs, GameBalance.AudioData, Balance.json `audio`.
  - Every hooked file: FxManager, Bomb, FlakShell (and Projectile), AAAGun, FighterAI,
    BomberAI, PlayerPlane, BombBay, Health, HealthBalloon, ZeppelinAI, ZeppelinTurret, HudView,
    MainMenuView, BriefingView, PauseView, ResultView, GameManager.
  - Editor/ProjectBuilder.cs and both scene files, for the camera and listener.
- `python tools/synth_sfx.py --check`: 23 rows OK, "0 error(s)", exit 0.
- WAV headers (python `wave`): all 23 files are mono, 16-bit, 44100 Hz. Lengths match T948
  (crash 1.8 s, explosion_big 2.6 s, win 2.0 s, lose 2.3 s, loops 1.5 s and 2.0 s).
- `python tools/compile_check.py`: PASS Biplanes (+1 new file not yet in the csproj), PASS
  Biplanes.Editor, exit 0.
- Test compile, the B018 scratch method:
  - The runtime DLL was built from unpatched source with compile_check's helpers.
  - Then a temp copy of Biplanes.Tests.EditMode.csproj (ProjectReferences and Compile items
    stripped, v4.7.1 -> v4.7.2) built all 21 Tests/EditMode/*.cs: 0 errors.
  - Negative control: a deliberately broken extra test file failed with CS0029, so the build
    really compiles the folder. The control file was removed. No temp csproj is left in the
    project.
- Static checks on every touched file:
  - Namespaces match the folders.
  - No `rb.velocity` and no `UnityEngine.Input`.
  - `using UnityEngine.InputSystem` is present where `Keyboard` is used (BriefingView,
    GameManager).
  - Every API used exists (`GetOrNull`, `PlayUi`, `StartLoop`, `SetLoop*`, `HasLoop`,
    `TriggerExplosion(.., bool)`, `TriggerFlakBurst(.., bool)`, `HitCueMinInterval`,
    `TypewriterClacksDue`).
  - No hand-written .prefab, .unity, .asset or .meta files. The WAVs, AudioManager.cs and
    the two AudioHooks test files have no .meta yet; Unity writes them on its next refresh.
  - One public type per file. GameBalance.cs holds AudioData beside its other Balance data
    classes, as PitchData and EnemyFireData already do. That is the existing pattern, so it
    is not counted as a bug.

## New tests (QA)
`Tests/EditMode/AudioManagerQaTests.cs`, 16 tests:
- Setup:
  - The tests build a real AudioManager through `Instance`. Outside Play mode it gets no
    DontDestroyOnLoad.
  - TearDown destroys it, resets `_instance` and `PlayerPlane._audioOwner`, and restores
    timeScale and `AudioListener.pause`. The other suites still see `GetOrNull() == null`.
  - The cue count is read from `AudioManager._playCounter`, which goes up once per cue
    actually started.
- Clips:
  - One WAV per SfxId, named `AudioMix.ClipName`, with no extra files.
  - RIFF fmt is PCM, mono, 16-bit, 44100 Hz.
  - Every clip loads through Resources.
- Core:
  - In EditMode, Instance is not DontDestroyOnLoad, and GetOrNull is null after destroy.
  - The pool has maxVoices + 4 UI sources, and only the UI sources ignore listener pause.
  - While paused, Play is dropped, `AudioListener.pause` is mirrored and PlayUi still
    plays, on an ignoreListenerPause voice. Resuming unpauses.
- Loops:
  - Starting the same id under the same key is a no-op. Another id replaces it. A stopped
    loop's source is reused.
  - Pitch is clamped to 0.1..3 and volume to 0..1 of master*sfx. Null, empty or unknown
    keys are ignored.
  - A scene unload stops every loop.
  - 6 Retry-like cycles add exactly 3 sources (one per concurrent loop) and leave a single
    AudioManager.
- PlayerPlane:
  - On the runway the engine idles at pitch 0.8 and volume 0.35, with no smoke loop.
  - An old plane's late OnDisable doesn't stop the new plane's engine. A late sceneUnloaded
    is healed on the next UpdateAudio. The owner's own OnDisable stops it.
  - MissionEnded stops the loops for good.
- Health:
  - A player hit plays exactly one HitPlayer. A second hit inside 0.05 s is throttled.
    Heals and 0-damage hits are silent.
  - A non-fatal enemy hit plays one HitEnemy. A fatal one plays nothing.

## Acceptance
| Area | Criterion | Result | Evidence |
|---|---|---|---|
| Clips | 23 WAVs named exactly `AudioMix.ClipName` | PASS | AudioMix.cs:46-70 vs Resources/Audio/sfx (23 files, no extras); test `ClipFiles_OneWavPerSfxId_...` |
| Clips | `synth_sfx.py --check` 0 errors | PASS | exit 0, 23 OK |
| Clips | mono 16-bit 44.1 kHz | PASS | python `wave` and test `ClipFiles_AreMono16Bit44k1Pcm` |
| Core | Voice pool, priority stealing | PASS | AudioManager.cs:147-150 (free voice first, else `VoiceToSteal(.., priority)`, -1 drops). AudioMixTests covers stealing (8 tests). |
| Core | PlayUi while paused | PASS | Own 4-voice pool, `ignoreListenerPause` (AudioManager.cs:282-286, 298-305), no pause check in PlayUi (:175-194); QA test |
| Core | AudioListener.pause mirrors timeScale | PASS | SyncPause (:355-360) from Update and from every Play; OnDestroy clears it (:121-124); QA test |
| Core | Scene unload stops loops and gameplay one-shots, UI kept | PASS | OnSceneUnloaded (:362-370), subscribed only in Play mode (:292-295); QA test |
| Core | No AudioSource/GameObject growth across Retry | PASS (see P2 B7 for quit time) | DontDestroyOnLoad manager; loop sources recycled through `_freeLoopSources` (:308-322); QA test `RetryCycles_DoNotGrowAudioSources` |
| Core | GetOrNull null outside Play mode; no DontDestroyOnLoad in EditMode | PASS | :39-55, :108; existing test plus QA test |
| Core | Missing clip warns once | PASS (static) | `_clipTried` per id (:335-348). Not testable without removing a clip. |
| Core | AudioData.Resolve defaults and clamps | PASS | GameBalance.cs:253-269; AudioMixTests Resolve_* (5 tests) |
| Core | Audio is audible at all | **FAIL** | **B1 (P0): no AudioListener exists** |
| Hooks | Bomb ground impact = BombImpact only | PASS | Bomb.cs:110-112 (`TriggerExplosion(p, false)` + BombImpact) |
| Hooks | Zeppelin death = one ExplosionBig | PASS for bullet kills; P2 for bomb direct hits (B4) | ZeppelinAI.cs:378-386 (hull blasts silent, one ExplosionBig) |
| Hooks | Win/Lose once per mission end | PASS | GameManager.cs:414-423 (`!ResultView.IsShowing`); the SetState guard (:163-171) stops a second ending |
| Hooks | Stamp is not also UiClick | PASS | BriefingView.cs:325-335 (Begin is only TriggerTransition) |
| Hooks | Never from Recycle, pool dispose, scene unload or teardown | PASS for one-shots; P2 for loop-stop paths at quit (B7) | FlakShell bursts only from OnHit and the lifetime coroutine, guarded by `activeInHierarchy` (FlakShell.cs:136-156). AAA, Fighter and Bomber cues only after a non-null Spawn. |
| Hooks | Every call null-checks the manager | PASS | All 22 `GetOrNull()` call sites outside Audio/ check for null before use (grep) |
| Hooks | No doubles | PARTIAL | B4: doubled Explosion on bomb direct-hit kills. B8: gun and bomb cues play with no projectile. |
| Loops | Engine: runway idle, flight, crash wind-down, wreck landing | PASS | PlayerPlane.cs:787-829; QA test for idle |
| Loops | MissionEnded / Retry / Menu stop | PASS | OnMissionEndedAudio (:861-865), OnDisable (:358-360), sceneUnloaded; QA tests |
| Loops | Smoke loop | PASS | :823-828 (IsSmoking, stops on wreck landing) |
| Loops | Zeppelin hum: off-screen fade and stop, death, despawn | PASS | ZeppelinAI.cs:267-295, :341-343, :384; unique key `zeppelin_hum_<n>` (:262) |
| Loops | No orphaned keys | PASS | Every StartLoop key has a stop on each exit path, and sceneUnloaded catches anything left |
| Loops | The static owner can't leave the new plane silent after Retry | PASS | Owner guard (:868-876); StartLoop runs every frame, so even a late sceneUnloaded is healed one frame later; QA test |
| Crash | Can't play twice | PASS | `_crashSoundPlayed`, reset only inside the `_hasCrashed`-guarded TriggerCrash (:706-728, :852-857) |
| Crash | Doesn't survive into the next mission | PARTIAL | B3: up to 0.8 s of tail survives a fast Retry or Menu |
| Crash | Design call (PlayUi) | ACCEPT, with B2 | See "Crash cue design call" below |
| Coverage | GDD cue list | P2 gaps | B5 (AAA death silent on a burst kill), B6 (turret hits) |
| Build | compile_check exit 0, tests compile | PASS | See above |

## Crash cue design call
Using PlayUi so the result-screen freeze can't cut the crunch is a reasonable call. The crash
is always on screen, so losing the pan costs little, and it can't play twice. The timing
logic it serves never works, though (B2). The longest possible fall is about 1.12 s: from the
top clamp y 4.5 down to the lowest rest point y -4.3, starting at vy 0 under gravity 14.
That is always shorter than `CrashCrunchOffset` 1.25 s, so the clip always starts at the
crash. The crunch then arrives 0.13 to 1.25 s after the wreck lands, after the landing
Explosion, and always 0.25 s after the result screen opens. There it stacks with Lose and
UiPage.

Recommendation: the engine loop already does the whine-down. Play just a crunch at the
landing, with Play() before the freeze or PlayUi. Two ways to get that:
- Art re-cuts crash.wav so the crunch sits near 0.1 s.
- Mechanics adds a start offset (`AudioSource.time = CrashCrunchOffset - timeToGround`).

## Bugs

```
BUG: No AudioListener in any scene, so every cue is silent
PRIORITY: P0 (blocks the whole M6 deliverable: no sound is ever heard; nothing crashes)
OWNER: mechanics
WHERE: Editor/ProjectBuilder.cs:63-74 (Game) and :376-386 (MainMenu) create "MainCamera" with Camera only; Scenes/Game.unity:440-455 and Scenes/MainMenu.unity:~133 (MainCamera = Transform + Camera, no `!u!81` AudioListener anywhere); AudioManager.cs:268-296 (EnsureInit never adds one)
STEPS: grep -rn "AudioListener" Assets/Biplanes -> only AudioListener.pause in AudioManager.cs; grep "!u!81" Scenes/*.unity -> none. In Play mode Unity logs "There are no audio listeners in the scene" and plays nothing.
EXPECTED: GDD Style Guide "Sound effects" cue list is heard; ARCHITECTURE §3 Audio.
ACTUAL: All AudioSources play into no listener, so the game is silent.
FIX HINT: in AudioManager.EnsureInit (Play mode only), add an AudioListener to the manager's own DontDestroyOnLoad GameObject when `FindAnyObjectByType<AudioListener>() == null`. Every source is 2D (spatialBlend 0), so the listener's position doesn't matter, and no Unity GUI action is needed. The alternative is to add it to MainCamera in ProjectBuilder, but then the user must rerun Biplanes > BuildScenes, and the two must never both exist ("2 audio listeners" warning). Add an EditMode or PlayMode test for it.
```

```
BUG: The crash crunch always lands late, after the wreck hits and after the result screen opens
PRIORITY: P2
OWNER: mechanics (art optional)
WHERE: PlayerPlane.cs:151-154 (CrashClipSeconds 1.8, CrashCrunchOffset 1.25), :756-759 (start when time-to-ground <= 1.25), :88 (CrashFallSeconds 1.0); crash.wav (crunch at 1.25 s)
STEPS: The longest fall is from y 4.5 (top clamp, PlayerPlane.cs:446-449) to rest -4.3 (GroundBaseY -4.5 + 0.2) at vy 0, g 14: t = sqrt(2*8.8/14) = 1.12 s < 1.25 s. So the first UpdateCrashFall tick always starts the clip, and the crunch plays at crash + 1.25 s. The wreck lands at 0..1.12 s (landing Explosion), and the result screen opens at 1.0 s with Lose + UiPage.
EXPECTED: GDD cue list, Crash: "Engine whine dropping in pitch as the wreck falls, ending in a crunch and silence" (the crunch on the impact).
ACTUAL: Impact explosion, then 0.13-1.25 s later a crunch, 0.25 s into the result screen, stacked on the Lose sting.
FIX HINT: The engine wind-down already gives the whine. Either play a short crunch at landing (re-cut crash.wav, art), or start crash.wav with AudioSource.time = max(0, CrashCrunchOffset - timeToGround), which needs a start-offset overload on AudioManager.
```

```
BUG: The crash (and win/lose) UI-pool tails survive Retry and Menu
PRIORITY: P2
OWNER: mechanics
WHERE: AudioManager.cs:362-370 (OnSceneUnloaded stops loops and gameplay voices only); PlayerPlane.cs:852-857 (Crash via PlayUi)
STEPS: Crash; when the result screen opens (crash + 1.0 s), click Retry at once. crash.wav ends at crash + 1.8 s, so up to 0.8 s of the crunch tail plays over the new mission's runway. lose.wav (2.3 s) also plays over the reloaded mission or the next briefing when clicked fast.
EXPECTED: The brief: the crash cue "doesn't survive into the next mission"; GDD Crash "... ending in a crunch and silence".
ACTUAL: The tail carries over (short, and only on a fast click).
FIX HINT: on sceneUnloaded, also stop UI voices whose clip is Crash (or add StopUi(SfxId)). Keep the click that triggered the load.
```

```
BUG: Doubled Explosion cue when a bomb's direct hit kills an enemy
PRIORITY: P2
OWNER: mechanics
WHERE: Bomb.cs:145-149 (OnHit: ApplyDamage, then TriggerExplosion with its cue) plus the victim's own death TriggerExplosion: FighterAI.cs:182, BomberAI.cs:208, BarrageBalloon.cs:151, EnemyBase.cs:146, ZeppelinTurret.cs:211; zeppelin hull: Explosion + ExplosionBig (ZeppelinAI.cs:386)
STEPS: Drop a bomb straight onto a fighter, bomber, balloon, base or turret that dies from it. Two Explosion one-shots start in the same frame (+6 dB, phasey). A bomb kill on the zeppelin hull adds an Explosion to the single ExplosionBig.
EXPECTED: GDD "Every gameplay event has one short, distinct cue"; T951 "zeppelin death = one ExplosionBig".
ACTUAL: Two cues for one kill.
FIX HINT: in Bomb.OnHit, call TriggerExplosion(transform.position, playSound: !target.IsDead) after ApplyDamage, so the victim owns its death cue.
```

```
BUG: An AAA gun killed by the bomb's ground burst has no explosion sound or sprite
PRIORITY: P2
OWNER: mechanics
WHERE: AAAGun.cs:310-318 (HandleDeath only logs and Destroys); Bomb.cs:108-112 (a ground impact plays BombImpact only)
STEPS: One bomb kills an AAA gun (enemyHealth.aaa 100 = bombDamage 100). When the kill comes from the ground burst (the common case, B008), you hear only the bomb_impact thump and the gun vanishes. A direct trigger hit gets the bomb's Explosion.
EXPECTED: GDD cue list, Explosion: "enemy downed, target destroyed".
ACTUAL: No destruction cue (and no explosion FX) on a burst kill.
FIX HINT: FxManager.Instance.TriggerExplosion(transform.position) in HandleDeath. Fix together with B4 so a direct hit doesn't double.
```

```
BUG: Non-fatal hits on zeppelin turrets play no HitEnemy, and turrets have no fire cue
PRIORITY: P2
OWNER: mechanics
WHERE: ZeppelinTurret.cs:70-88 (ApplyDamage has no cue), :163-171 (Fire spawns a FlakShell with no FlakFire)
STEPS: Shoot a turret (75 HP, bulletDamage 25): the first 2 hits are silent, and the 3rd gives the explosion. Turret shots are silent until the burst. Per T951 REQUESTS, this file was outside that brief.
EXPECTED: GDD cue list "Bullet hits an enemy: small metallic tick"; "Flak: a dull pop when fired".
ACTUAL: Silent hits and silent firing (the burst cue does play).
FIX HINT: a throttled HitEnemy like ZeppelinAI.PlayHitCue (ZeppelinAI.cs:298-305) on a non-fatal hit, and FlakFire when FlakShell.Spawn returns non-null. AAAGun needs no HitEnemy: bullets do no damage to it, and one bomb kills it.
```

```
BUG: Teardown loop-stops can spawn a fresh AudioManager while play mode exits
PRIORITY: P2
OWNER: mechanics
WHERE: PlayerPlane.cs:358-360 + :868-876 (OnDisable -> StopPlayerLoops -> GetOrNull); ZeppelinAI.cs:287-295 (OnDisable -> StopHum -> GetOrNull); AudioManager.cs:52-55, :125
STEPS: Static, needs editor confirmation. At play-mode exit or app quit the destroy order is undefined. If the DontDestroyOnLoad AudioManager goes first, its OnDestroy nulls `_instance`. Then the plane or zeppelin OnDisable calls GetOrNull, which sees isPlaying true and creates a new "AudioManager" GameObject during teardown. Unity logs "Some objects were not cleaned up when closing the scene (Did you spawn new GameObjects from OnDestroy?)". Scene reloads (Retry/Menu) are fine, because the manager outlives the scene.
EXPECTED: The brief: "never from ... OnDisable teardown"; no GameObject growth.
ACTUAL: Possible stray manager plus an editor error at quit.
FIX HINT: a non-creating accessor for stop paths (for example `AudioManager.Existing => _instance`), or a static `_quitting` flag set from Application.quitting that makes GetOrNull return null.
```

```
BUG: The gun and bomb-release cues play even when no projectile spawned
PRIORITY: P2
OWNER: mechanics
WHERE: PlayerPlane.cs:572-574 (PlayerGun after Bullet.Spawn, return value ignored); BombBay.cs:41-43 (BombRelease after Bomb.Spawn, return value ignored; the count is decremented as before)
STEPS: With the pool exhausted (Bullet pool 40, Bomb pool 10; ObjectPool.Pop returns null and warns), the rattle or click still plays with no bullet or bomb.
EXPECTED: The rule T950 applied to AAA, Fighter and Bomber: a cue per projectile actually spawned.
ACTUAL: A phantom cue (rare in practice).
FIX HINT: play only when Spawn returns non-null. In BombBay, also don't spend the bomb.
```

## Notes (not bugs)
- With the listener paused, `StartLoop` on an existing key calls `Play()` when `!isPlaying`
  (AudioManager.cs:205-207). If Unity reports isPlaying false for listener-paused sources,
  the engine and hum restart from sample 0 every frame of a pause. That is inaudible while
  paused, and at most a seam on resume. The checklist item below covers it.
- At the top altitude the wreck is still falling when time freezes at 1.0 s (fall 1.12 s),
  so the landing Explosion only happens if the player waits. This is pre-existing T937
  behaviour; with B2 the crunch then plays over a mid-air wreck.
- The UI pool (4 voices) is enough: at most 3 overlap on the briefing (page plus clacks
  0.08 s long, 0.06 s apart) and at the result (crash, lose, page, click).
- WebGL browsers block audio until the first user gesture, so the boot UiPage on the main
  menu will be silent there. WebGL's compressed import may also add a gap at loop seams.
  This is outside the PC scope of this pass.
- Music (GDD "Music") has no clips or channel use yet. That is outside M6 hooks scope.

## Manual listening and playtest checklist (PC, Windows/macOS editor or build)
Updated by T955 after the B032/B033/B034 fixes: every item below now states the expected
behaviour (the bug ids are kept for reference). B1 is fixed in code (AudioManager adds its
own AudioListener in Play mode), so no BuildScenes rerun is needed: just Play. Keep the
Console open and watch for "no audio listeners", "2 audio listeners", "clip ... not found"
or "not cleaned up" messages.

- [ ] Hierarchy in Play mode: exactly one AudioListener, on DontDestroyOnLoad/AudioManager,
      and still one after MainMenu -> Game -> Retry -> Menu (B1).

**Boot and menus**
- [ ] Main menu opens: one page turn, no Console audio warnings.
- [ ] Click How to Play and a mission button: one pencil tick each.
- [ ] Briefing: a page turn, then about 5 typewriter clacks within about 0.4 s, then quiet.
- [ ] Begin by button, by Space, by Enter, and by waiting 5 s: each gives exactly one stamp,
      no tick on top, and no clacks after it.

**Flight (M1)**
- [ ] On the runway: a low, quiet idle drone.
- [ ] After takeoff it is louder. Climb raises the pitch and dive lowers it, smoothly, with
      no clicks at the loop seam (1.5 s period).
- [ ] Hold the left mouse: one dry rattle per bullet, slightly varied, not droning.
      Bullets visibly match the rattle; no rattle without a bullet (B8).
- [ ] Space: a latch click per bomb, and a deep thump on the ground with no explosion boom
      on top.
- [ ] Hit a bomber with bullets: a small tick. Kill a fighter: one boom. Bomb a fighter,
      bomber, balloon or turret dead with a direct hit: exactly one boom, not doubled (B4).
      A non-fatal direct bomb hit: one boom (plus a tick on Health enemies and turrets).
- [ ] Enemy fire: a lower, distant rattle, panned left or right by the shooter's x.
      Off-screen shooters are quieter, not silent.
- [ ] AAA: a dull pop per shell, 3 per burst, then a sharp crack at each burst. Kill AAA
      with the ground burst: the bomb_impact thump plus an explosion sprite and one boom at
      the gun. Kill it with a direct hit: one boom only (B5).
- [ ] Getting hit: a heavier crack plus scratch, clearly different from the enemy tick.

**Damage and crash**
- [ ] Cross 70%: exactly one alarm bell, the crackle loop starts, and the engine coughs
      irregularly.
- [ ] Health balloon: one warm tap, and the crackle stops when damage drops below 70%.
      Crossing 70% again rings again.
- [ ] Crash at low and mid altitude: the engine winds down while falling and goes quiet at
      landing. The crash clip starts 0.1 s before the predicted landing, so the crunch hits
      together with the wreck's impact explosion (B2). Exactly one crunch per crash.
- [ ] Crash at the very top of the screen (level, no dive): the wreck is still falling when
      the result screen opens at 1.0 s. The clip starts at 0.9 s, so the crunch marks the
      result screen opening (with the lose phrase and page turn), and there is no second
      crunch if the wreck later lands (B2).
- [ ] Result screen after a crash: the lose phrase plus a page turn, once. Click Retry at
      once: the Retry click is heard, the crunch tail stops at the reload, and the lose
      phrase may finish over the new runway (by design, B3).

**Pause**
- [ ] Esc: one hush, and the engine, crackle and hum go silent.
- [ ] While paused, clicks still sound.
- [ ] Esc again resumes silently and the loops continue from where they were, with no
      restart click. The Resume button gives a tick.
- [ ] Quit from pause: a tick, then the menu page turn, and no gameplay sound continues.

**Mission ends and flow**
- [ ] Win: one brass flourish plus a page turn when the screen opens. Retry, Next and Menu
      give a tick only, and the sting is not replayed.
- [ ] Map-end or objective loss: one lose phrase.
- [ ] Do Retry 10 times in one mission. In the Hierarchy (DontDestroyOnLoad), there is a
      single "AudioManager", and its AudioSource count stays at 16 plus the loops ever used
      (at most 19).
- [ ] Stop Play mode during a mission (engine and hum playing): the Console shows no
      "objects were not cleaned up" message, and no stray "AudioManager" is left (B7).

**M5 zeppelin**
- [ ] The hum fades in as the zeppelin approaches from off-screen and fades out if it falls
      behind.
- [ ] Each turret shot gives a dull pop, and its burst cracks. Shooting a turret: a tick on
      hits 1 and 2 (at most one per 0.05 s), one boom on the kill (B6).
- [ ] Hull hits after the turrets are gone give a tick.
- [ ] Death: one long rolling boom, not stacked booms, and the hum stops at once.
- [ ] Retry or Menu during the hum: the hum stops.

**Mix (subjective)**
- [ ] The guns and threats (flak, enemy fire, the warning) sit above the engine and hum.
- [ ] UI is clearly quieter than gameplay.
- [ ] The zeppelin hum doesn't mask the guns.
