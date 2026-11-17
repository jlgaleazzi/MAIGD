# T945: verification of B030 (pencil FX animation)

**Verdict: PASS.** No P0 or P1 bugs. 3 P2 items (1 test-hygiene item fixed by QA, 2 open).
Unity is open, so the EditMode tests were compiled but not run.

## What was checked
- Read: B030-mechanics.md, T944-art.md, FxManager.cs, FxFrames.cs, FlakShell.cs, Projectile.cs,
  PlayerPlane.cs, FxFramesTests.cs, and every FxManager caller (EnemyBase, FighterAI, BomberAI,
  ZeppelinAI, ZeppelinTurret, BarrageBalloon, Bomb, EnemyBomb, PlayerPlane crash landing).
- `git diff HEAD` of FxManager, FlakShell and PlayerPlane.
- `python tools/compile_check.py`: PASS Biplanes (+1 new file not in the csproj), PASS
  Biplanes.Editor, exit 0.
- Test compile (scratch, the B018 method plus a v4.7.2 retarget): the runtime DLL built from
  source, then all 17 Tests/EditMode/*.cs files. 0 errors, and no temp csproj was left in the
  project. The scratch script is outside the repo.
- `python tools/rasterize.py --check`: 40/40 OK and 4/4 OK, exit 0. Sprite sizes and import
  settings: explosion_0..5 128x128, flak_burst_0..3 96x96, smoke_puff 48x48. All are
  textureType Sprite, spriteMode 1, 100 px/unit. This matches ART_STYLE.md.
- Static checks:
  - The namespaces match the folders: Biplanes.Fx in Fx/ and Biplanes.Tests in Tests/EditMode.
  - One public class per file, and each filename matches its class.
  - No `rb.velocity` and no `UnityEngine.Input`.
  - No new .prefab, .unity, .asset or .meta files.
  - Every API the code uses exists, including `GetOrNull`, `CountActive`, `Kind`, `OnLifetimeEnd` (virtual) and `SmokePuffInterval`.

## Acceptance
| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | Explosions play explosion_0..5 at 0.08 s/frame | PASS | FxManager.cs:58-63 and :196-201. FxFrames.FrameIndex has an epsilon for exact boundaries. New test `TriggerExplosion_FramesStepAt008_...` steps frames 1..5. |
| 1 | No 0.1x->2x zoom (grow stays within 0.8x-1.2x) | PASS | The grow is 0.85 -> 1.15 (FxManager.cs:62-63). The test asserts the scale stays in [0.8, 1.2] on every frame. |
| 1 | Fade only at the end | PASS | FadeOutAlpha holds 1 until the last 0.12 s of 0.48 s (FxFrames.cs:42-50). The test checks alpha 1 through t = 0.32 and below 1 at 0.40. |
| 1 | Random flips kept | PASS | FxManager.cs:181-183. Smoke keeps flipY off by design. |
| 1 | Cap 20 | PASS | MaxExplosions = 20. New test `Caps_PerKind_...`: 25 triggers give 20 live objects, and the oldest 5 are moved to the new x. |
| 1 | Existing callers unchanged | PASS | `TriggerExplosion(Vector3)` keeps its signature. All 9 callers compile untouched, and the object is still named "Explosion". |
| 2 | TriggerFlakBurst plays flak_burst_0..3 | PASS | FxManager.cs:65-70 and :203-208. |
| 2 | Bursts on a player hit and on lifetime expiry | PASS | FlakShell.cs:134-146 (OnHit, then the OnLifetimeEnd override). New test `FlakShell_LifetimeEnd_Bursts_PlainRecycle_DoesNot`. |
| 2 | No burst on a plain Recycle, pool dispose or scene reload | PASS | Recycle() isn't virtual and doesn't call PlayBurst. Dispose destroys the items, which stops the lifetime coroutine. A late OnLifetimeEnd on a pooled shell is skipped by the `activeInHierarchy` guard (tested). |
| 2 | No NullReference without an FxManager | PASS | `GetOrNull()` returns null outside Play mode (FxManager.cs:45-48). New tests `GetOrNull_EditModeWithoutInstance_...` and `FlakShell_LifetimeEnd_WithoutFxManager_...`. |
| 3 | Smoke only at damage >= smokeThreshold while flying or falling | PASS | IsSmoking (PlayerPlane.cs:582-584). New test `PlayerSmoke_...`: no puff below the threshold, the first puff comes at once, the next one after 0.12 s. |
| 3 | World space; rises, grows, fades | PASS | Puffs are scene-root objects with no parent. The test checks rise 0.18 u, scale 1.3 and alpha 0.45 at t = 0.6 s. |
| 3 | Smoke capped at 24 | PASS | Caps test. |
| 3 | Smoke stops on landing, respawn and mission end | PASS | `_crashLanded` stops it (tested). The game has no respawn. A teardown deactivates the old plane, so its Update stops. The result, pause and briefing screens set timeScale 0, so dt is 0 and nothing is emitted (tested). |
| 3 | No leaks across scene reloads | PASS | Effects are scene-root objects and die with the scene. Prune() drops the dead entries (tested with DestroyImmediate). Retry and Menu use LoadScene. No DontDestroyOnLoad object is created outside Play mode by the new paths. |
| 4 | Live counts can't drift | PASS | The `_active` list is the only count, and it is pruned before counting or playing. |
| 4 | At a cap the oldest effect of that kind is reused | PASS | FxManager.cs:161-170 (tested). |
| 4 | Sprites cached | PASS | The caches are lazy, one per kind. An empty array is cached too, so a missing sprite warns only once. |
| 4 | Missing frames warn once and never throw | PASS | New test `LoadFrames_MissingFrames_...` gets 6 of 8 frames with a single warning, and 0 of 3 frames with a warning and no throw. `NoFramesCached_EffectSkipped_...` gets no object and no log. |
| 5 | Pause freezes FX; Resume and Retry work | PASS | Ticks use scaled dt (`TickAtZeroDt_FreezesEffects`). Retry calls LoadScene with timeScale 1. |
| 6 | Regression: "Explosion" name and flak-hit tests | PASS | The name is kept. FlakShell_HitOnPlayer_* seed no FxManager, so GetOrNull returns null. Bomb_Update_BelowGround_* still finds "Explosion"; it needs explosion_0..5 importable, and they are. |
| 6 | `_smokeTrailRoot` not referenced | PASS | Grep over Assets/ (Editor/ProjectBuilder, *.unity, *.prefab): 0 hits. |
| 7 | compile_check exits 0, and the tests compile | PASS | See above. |

## Bugs

```
BUG: ZeppelinAndBaseTests leaked "FlakBurst" objects into the EditMode scene
PRIORITY: P2 polish
OWNER: mechanics (fixed by QA, tests are QA-owned)
WHERE: Biplanes/Assets/Biplanes/Tests/EditMode/ZeppelinAndBaseTests.cs:61
STEPS: SetUp seeds an FxManager. FlakShell_TurretHitAdds12_* calls OnHit on a player twice, so B030's PlayBurst now creates 2 "FlakBurst" scene objects per run. TearDown only cleaned "Explosion".
EXPECTED: tests leave no scene objects behind.
ACTUAL: fixed. TearDown now also removes "FlakBurst" and "SmokePuff".
```

```
BUG: AAA airbursts mostly happen off-screen
PRIORITY: P2 polish
OWNER: mechanics (design call for the orchestrator)
WHERE: Biplanes/Assets/Biplanes/Scripts/Weapons/FlakShell.cs:21,24,27 (ShellSpeed 6, MinUpward 0.5, ShellLifetime 3)
STEPS: Static math. A shell from a gun on the ground (y about -4.5) climbs at least 0.447 * 6 * 3 = 8 u in 3 s, so it ends at y >= 3.5 and about 16 u away horizontally. That is above the camera top (y 5) or outside its half-width (8.9) in nearly every case. Zeppelin turret shells fired down expire under the ground, off-screen.
EXPECTED: B030 playtest note: "shells airburst after 3 s with a flak_burst animation" should be visible to the player.
ACTUAL: the airburst plays, but the player will rarely see one. The off-screen bursts also take slots from the cap of 16. Options: a shorter fuse, a burst when the shell passes near the player, or accept it. Not a GDD rule.
```

```
BUG: ART_STYLE.md still says FxManager shows only frame 0
PRIORITY: P2 polish
OWNER: art
WHERE: docs/ART_STYLE.md:116 (fx explosion_0..5 row)
STEPS: The row says "(works alone as a still: FxManager shows only frame 0)".
EXPECTED: matches B030: all 6 frames play at 0.08 s each.
ACTUAL: the text is stale. Docs only.
```

Observation (no bug): on "Next" (no scene reload), FX from the previous mission stay frozen behind the briefing at timeScale 0. They finish within 1.2 s once the next mission starts. They sit at the old mission's x, usually off-screen after the camera reset. Acceptable.
Observation (already there before B030): callers other than FlakShell and PlayerPlane smoke still use `FxManager.Instance`. In EditMode that calls DontDestroyOnLoad unless a test seeds `_instance`. B030 didn't change this.

## Tests added or changed by QA
- New: `Biplanes/Assets/Biplanes/Tests/EditMode/FxManagerTests.cs`, 11 tests:
  - GetOrNull in EditMode;
  - explosion frames, scale and fade;
  - per-kind caps and reuse of the oldest;
  - pruning;
  - smoke rise, scale, fade and flips;
  - dt 0 freezes;
  - missing-frame warning;
  - an empty cache skips the effect;
  - FlakShell lifetime burst vs plain Recycle;
  - FlakShell with no FxManager;
  - the PlayerPlane EmitSmoke threshold, interval, tail offset, pause and landing.

  The tests never tick an effect to completion, because Destroy is rejected in EditMode.
- Changed: ZeppelinAndBaseTests TearDown also cleans "FlakBurst" and "SmokePuff".
- Run with Unity closed: `bash tools/unity.sh test`. Expected: FxFramesTests (8), FxManagerTests (11) and every existing suite green.

## Manual playtest checklist (Biplanes -> BuildScenes, then Play)
- [ ] Kill a fighter: a pencil explosion of about 0.5 s, 6 frames, with no big zoom. It fades only at the end and its flips vary.
- [ ] Bomb an AAA gun or a ground target: the explosion sits on the ground surface.
- [ ] Get hit by AAA: a flak_burst plays on the plane, and the damage is 15%.
- [ ] Watch for AAA airbursts after 3 s. Note whether any are visible (see the P2 above).
- [ ] Zeppelin turret hit: a flak burst on the plane, 12%.
- [ ] Reach 70% damage or more: puffs come from behind the tail and stay in the world as the plane moves. They rise, grow and fade, and appear behind the plane sprite (sortingOrder 10 < 20). Turn left: they still come from the tail.
- [ ] Pick up a health balloon below 70%: the smoke stops.
- [ ] Reach 100%: the wreck smokes while it falls and stops after the landing explosion.
- [ ] Esc to pause: FX freeze; Resume continues them. Retry from the result screen: no leftover puffs.
- [ ] Next mission: no frozen FX visible at the start of the new mission.
- [ ] Spam bombs and kills (20+ explosions): no hitch, and no warnings in the Console.
- [ ] WebGL build in Chrome, Firefox and Safari: the FX render with alpha (no black boxes).
