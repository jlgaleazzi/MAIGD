# T955: re-verification of the T953 audio bugs (B032, B033, B034) and the M6 audio verdict

**Verdict: PASS.** All 8 T953 bugs are closed: B1 (P0) and B2-B8 (P2).
New bugs: 0 P0, 0 P1, 0 P2. There is one doc request for the orchestrator and one test hardening, described below.
Unity is open, so the EditMode tests were compiled but not run. The listening checklist in
`docs/qa/T953-verify.md` (updated) still needs a human pass.

## What was checked
- Read:
  - `docs/qa/T953-verify.md`.
  - Handoffs B032-mechanics, B033-mechanics and B034-art.
  - ARCHITECTURE §3 "Audio (Audio/)" (lines 235-277).
  - AudioManager.cs, PlayerPlane.cs, BombBay.cs, Bomb.cs, AAAGun.cs, ZeppelinTurret.cs, ZeppelinAI.cs, FxManager.cs and GameManager.cs (the result flow).
  - Projectile.cs, ObjectPool.cs, FlakShell.cs.
  - AudioListenerTests.cs, tools/synth_sfx.py.
- `python tools/compile_check.py`: "PASS Biplanes (+1 new file(s) not yet in the csproj)", "PASS Biplanes.Editor", exit 0.
- `python tools/synth_sfx.py --check`: 23 rows OK, "0 error(s)", exit 0. The crash row reads 1.000 s, -3.0 dBFS, low 91.5%.
- crash.wav measured with python `wave`:
  - Format: mono, 16-bit, 44100 Hz, 44100 frames = **1.000 s**.
  - The sample peak is at **0.1030 s** (-3.0 dBFS).
  - The 10 ms RMS is -28.4 dB at 0.09 s and -9.6 dB at 0.10 s, so the crunch onset is at 0.10 s.
  - The last sample above -60 dBFS is at 0.906 s.
- Test compile, the B018 scratch method:
  - Biplanes.dll was built from source with compile_check's helpers.
  - A temp copy of Biplanes.Tests.EditMode.csproj had its ProjectReferences and Compile items stripped and was retargeted from v4.7.1 to v4.7.2.
  - Then all **23** Tests/EditMode/*.cs files were built: runtime PASS, tests PASS.
  - Negative control: the first build of my new file failed with CS1061, because `ZeppelinTurret.PlayerRef` is internal. That shows the folder really compiles. I fixed the test with reflection.
  - No temp csproj is left (`ls Biplanes/zz_*` is empty).
- Static checks on the touched files:
  - Namespaces match the folders.
  - No `rb.velocity` and no `UnityEngine.Input`.
  - Every API used exists: `FxManager.GetOrNull`, `TriggerExplosion(.., bool)`, `Health.HitCueMinInterval`, `GameEvents.RaiseBombsChanged` and `FlakShell.Spawn(.., DamageSource)` (each one grepped).
  - No hand-written .prefab, .unity, .asset or .meta files. AudioListenerTests.cs, AudioFixesQaTests.cs and Resources/Audio have no .meta yet; Unity writes them on refresh.
  - `AudioListener` appears nowhere outside AudioManager.cs. There is no `!u!81` in either scene.

## Acceptance
| # | Criterion | Result | Evidence |
|---|---|---|---|
| B1 | Exactly one enabled AudioListener in Play mode, across MainMenu -> Game -> Retry -> Menu | PASS | See "B1 lifecycle" below. AudioManager.cs:376-389 (EnsureListener), :263-265 (NeedsListener), :365-370 (sceneLoaded subscribed in Play mode only), :455-457 (recount per scene load). Tests: `AudioListenerTests.NeedsListener_*`, `EditMode_ManagerInit_AddsNoAudioListener` |
| B1 | No listener added in EditMode; none duplicated if a scene has one | PASS | :377 returns when `!Application.isPlaying`. :382 counts only enabled listeners other than its own, and :388 disables its own when one exists |
| B2 | Crash starts 0.1 s before the predicted landing, or at the fallback; exactly once; through PlayUi | PASS | PlayerPlane.cs:759-764 (rule checked on the moved position each tick), :860-862 (`ttg <= 0.1 || elapsed + 0.1 >= 1.0`), :769 (landing fallback guarded), :870-875 (`_crashSoundPlayed` guard; `PlayUi(SfxId.Crash)`), :726 (reset only inside the `_hasCrashed`-guarded TriggerCrash). Fall simulation and the real UpdateCrashFall are covered by new tests (see below) |
| B2 | crash.wav about 1.0 s, crunch at about 0.1 s | PASS | Measured 1.000 s, peak at 0.1030 s, onset 0.10 s. Constants: PlayerPlane.cs:152 (1.0) and :155 (0.1) |
| B3 | The Crash voice stops on scene unload; Win, Lose, click and page don't | PASS | AudioManager.cs:239 (UI voice id tracked), :245-258 (`KeepsPlayingOnSceneUnload`), :468-475 (stop and clear only non-screen ids). Tests: `KeepsPlayingOnSceneUnload_FalseForEveryGameplayCue` (all 23 ids), `SceneUnload_ClearsCrashUiVoice_KeepsScreenCues_Deterministic` |
| B4 | One Explosion cue per bomb kill | PASS | Bomb.cs:151 `TriggerExplosion(pos, !target.IsDead)` after ApplyDamage. Every enemy IDamageable reports IsDead synchronously: Health:43, AAAGun:54, EnemyBase:53 (`_dead` is set in Die :139, which ApplyDamage :62 calls), ZeppelinAI:152, ZeppelinTurret:65. Each victim plays its own death explosion: FighterAI:182, BomberAI:208, BarrageBalloon:151, EnemyBase:146, ZeppelinTurret:229, AAAGun:322, and ZeppelinAI:379-388 (ExplosionBig only). Test `BombDirectHit_Fatal_IsSilent_NonFatal_PlaysOneExplosion` |
| B5 | AAA death explosion plays once | PASS | AAAGun.cs:170-175 (the `_wasAlive` transition runs HandleDeath once). :321-322 is `FxManager.GetOrNull()?.TriggerExplosion`. A direct hit gives the bomb a silent explosion plus the gun's boom. A ground burst gives BombImpact (Bomb.cs:110-112) plus the gun's boom. Tests `AAAGun_BombDirectKill_PlaysExactlyOneExplosion`, `AAAGun_HandleDeath_WithoutFxManager_CreatesNone_InEditMode` |
| B6 | Turret FlakFire per spawned shell; throttled HitEnemy | PASS | ZeppelinTurret.cs:186-189 (FlakFire only on a non-null Spawn), :88-91 (tick only when damage rose and the hit wasn't fatal), :96-103 (throttled by `Health.HitCueMinInterval`). Flak and Heal return at :85, so they are silent. Tests `ZeppelinTurret_NonFatalHits_TickThrottled_FatalHitExplodesOnly`, `ZeppelinTurret_Fire_PlaysFlakFire_PerSpawnedShell` |
| B7 | Instance and GetOrNull are null in Play mode while quitting; EditMode is never blocked; no manager is created from teardown | PASS | AudioManager.cs:65-67 (`MayAccess`), :73 and :87 (both accessors gated), :52-61 (the flag is reset at SubsystemRegistration; `quitting` is unsubscribed before resubscribing), :156-158 (OnApplicationQuit). Unity raises `Application.quitting` and OnApplicationQuit before the exit-Play teardown. The teardown stop paths use GetOrNull only: PlayerPlane.cs:359-361 and :887-895; ZeppelinAI.cs:287-297. Tests `MayAccess_FalseOnlyInPlayModeWhileQuitting`, `QuittingFlag_DoesNotBlockEditMode` |
| B8 | PlayerGun only for a spawned bullet; BombRelease only for a dropped bomb; a null Spawn spends nothing and raises no BombsChanged | PASS | PlayerPlane.cs:573-576. BombBay.cs:40-46: Spawn first, `return false` on null before `_remaining--`, the cue and RaiseChanged. The only caller (PlayerPlane.cs:640) ignores the return, so nothing else changes. Tests `BombBay_NullSpawn_SpendsNoBomb_NoCue_NoBombsChanged`, `PlayerGun_OnlyForASpawnedBullet` |
| Reg. | Other audio hooks, FX, and the bomb and AAA scoring tests | PASS (static) | `EnemyWeaponsAndPoolTests.AAAGun_BombKill_ScoresAaaExactlyOnce`: score is raised only in OnDamageTaken. In EditMode, HandleDeath now hits `FxManager.GetOrNull()`, which is null unless a manager was seeded, so it creates nothing; its Destroy log is already ignored by that test. FxManager tests are unchanged. The other 22 GetOrNull call sites are untouched and null-checked. Win and Lose are still once per result (GameManager.cs:420-423) |
| Build | compile_check exit 0; synth --check 0 errors; all tests compile | PASS | See above |

### B1 lifecycle (reasoned)
- **Boot (MainMenu):** the first cue creates the manager through `GetOrNull` -> `Instance` (MainMenuView's UiPage).
  - In Play mode, Awake makes it DontDestroyOnLoad and calls EnsureInit, which subscribes to sceneLoaded and sceneUnloaded, then runs EnsureListener.
  - Neither scene has a listener, so `others` is 0, and AddComponent gives exactly one enabled listener on the manager.
  - The MainMenu sceneLoaded that follows recounts and finds no change.
- **MainMenu -> Game, Retry (Game -> Game), Menu (Game -> MainMenu):**
  - Single-mode loads keep the DontDestroyOnLoad manager, so the listener persists.
  - Each sceneLoaded recounts the other enabled listeners (`FindObjectsByType` skips inactive objects, and :382 skips disabled ones). The count is still 0, so the manager keeps its own enabled.
  - It is never added twice, because of the `_ownListener == null` guard at :385.
- **A future scene with a camera listener:** after that load, `others` is 1 and the manager's listener is disabled. The listener is re-enabled after a later load that has none. So there are never 2.
- **A duplicate manager:** Awake :143-146 destroys the newcomer before EnsureInit, so it adds no listener.
- **EditMode:** :377 returns early, and the test confirms it.

## New tests (QA)
`Tests/EditMode/AudioFixesQaTests.cs` adds 12 tests (compiled, not run). They seed FxManager by reflection, as FxManagerTests does, and count cues with `AudioManager._playCounter`.
- **B3:**
  - `_uiIds` is set directly to Crash, UiClick, Lose and UiPage, then `OnSceneUnloaded` runs. Only Crash is cleared.
  - `KeepsPlayingOnSceneUnload` is checked for every SfxId.
- **B2:**
  - A pure fall simulation copies UpdateCrashFall's order: velocity, then position, then the start rule, then the landing test. It covers heights 0.5-8.8 and dives 0, -3 and -6. Each case gets exactly one start. When the wreck lands, the crunch lands with it (within one 0.02 s tick). Otherwise the start is at 0.9 s and the crunch at 1.0 s (±0.02).
  - A level fall from the top clamp (8.8 units, 1.12 s) needs the fallback.
  - A real `PlayerPlane.UpdateCrashFall` run, from 3 units above rest with ticks driven by reflection, plays Crash on the UI pool exactly once. It starts 0.1 s (±0.02) before the landing tick.
  - Python cross-check of the same simulation in float32: every landing case comes out at 0.00-0.02 s. The fallback starts at 0.92 s (float accumulation), so its crunch comes about 20 ms after the result opens. That is inaudible.
- **B4:** a fatal stub direct hit plays 0 cues. A non-fatal one plays 1 Explosion.
- **B5:** a bomb direct kill on the AAA gun plays 0 cues from the bomb and 1 Explosion from HandleDeath. HandleDeath without a seeded FxManager creates none in EditMode.
- **B6:**
  - Flak on a turret is silent.
  - The first bullet ticks once, and a second hit in the same instant is throttled.
  - The kill ends on Explosion.
  - `Fire` spawns one shell and plays one FlakFire.
- **B8:**
  - A live pool: the drop spends one bomb, plays one BombRelease and raises one BombsChanged.
  - A disposed pool (Spawn returns null): no bomb spent, no cue, no BombsChanged.
  - The gun: a rattle with a bullet, and none after the Bullet pool is disposed.

**Hardened (not a production bug):** `AudioListenerTests.SceneUnload_StopsCrashUiVoice_KeepsTheClick` asserted that the Crash and UiClick cues land on different UI voices.
- PlayUi picks the first voice whose `isPlaying` is false. If EditMode AudioSources don't report `isPlaying` (for example, audio off in batch mode), both cues take voice 0 and the test would fail spuriously.
- Those two preconditions are now `Assume` (Inconclusive instead of a false failure).
- The rule is covered deterministically by the new `_uiIds` test.

## Notes (not bugs)
- **Fallback timing:** the fallback assumes game time keeps pace with the real-time 1.0 s result delay (GameManager.cs:407 `WaitForSecondsRealtime`).
  - If a frame takes longer than `Time.maximumDeltaTime` (0.33 s) during the fall, game time can trail by more than 0.1 s. The result screen then freezes time before elapsed reaches 0.9, and a mid-air wreck would get no crunch.
  - This needs a long hitch exactly during a crash, so it is negligible.
- **Non-fatal direct bomb hits:** on a Health enemy or a turret, one gives Explosion (the bomb) plus HitEnemy (the victim), as B033 notes. This matches the Health rule from before B033.
- **Landing stack:** at landing the crunch now coincides with the wreck's Explosion boom (PlayerPlane.cs:770-771). That is intended (B032 playtest note). Judge the mix by ear (checklist).
- **Fallback stack:** in the top-clamp fallback, the crunch stacks with Lose and UiPage at the result opening. That is the accepted design in the brief.
- **Lose over Retry:** lose.wav (2.3 s) may still finish over a fast Retry. That is the accepted design ("Win/Lose ... are not" stopped).

## Requests
- orchestrator: ARCHITECTURE §3 Audio "Hooks" has the B032 rules but not the B033 ones. Add:
  - "a bomb direct hit plays its explosion silently when it kills (the victim's death explosion is the one cue)";
  - "AAAGun plays an explosion (FX + Explosion) on death";
  - "ZeppelinTurret plays FlakFire per shell actually spawned and a HitEnemy on a non-fatal hit, throttled to one per 0.05 s".

## Bugs
None open. B1-B8 from T953 are closed.

## M6 audio verdict
**PASS:** 0 P0, 0 P1, 0 P2 open.
Before M6 is called fully done, with Unity closed, run `bash tools/unity.sh test` and expect these to pass:
- AudioFixesQaTests (12)
- AudioListenerTests (8)
- AudioManagerQaTests (16)
- AudioHooks*
- AudioMixTests
- EnemyWeaponsAndPoolTests

Then do the updated listening checklist in `docs/qa/T953-verify.md`.
