# B001 + B002 verification (QA task T926)

**Verdict: PASS.** All acceptance items hold. I found 0 P0 and 0 P1 bugs in the fixes, plus 3 P2
bugs (one is the B002a fix being effectively one tick long, two are pre-existing leaks the
mechanics agent flagged). I also report 3 pre-existing bugs outside this task's scope
(2 P1, 1 P2) that I found while tracing the Next path.

Unity is open, so batch mode was unavailable. The tests were compiled but not run.

## Compile

- `python tools/compile_check.py` → PASS Biplanes, PASS Biplanes.Editor, exit 0.
- **Test assembly (scratch build):**
  - First I built Biplanes from source.
  - Then I built all 3 files in `Tests/EditMode/*.cs` against that Biplanes.dll, the
    ScriptAssemblies DLLs and `nunit.framework.dll` (net472). The scratch csproj was
    retargeted to v4.7.2, because the net472 NUnit is otherwise skipped.
  - Result: **PASS**. Nothing was left in the project.
- **Static checks:**
  - The changed files use no `rb.velocity` and no `UnityEngine.Input`.
  - The namespaces match their folders.
  - Every API used exists: `ScrollController.SetLimits/FarthestX/MissionDistanceX`,
    `ObjectiveTracker.Init`, `GameEvents.EntityDestroyed`, `TerrainStrip.Instance` and
    `PlayerPlane.Facing`.
  - No Unity YAML was hand-written.

## Static check of each fix

| Fix | Result | Evidence |
|---|---|---|
| B001 one player | OK | `TearDownPreviousMission` deactivates and destroys `_playerPlane` before `PlayerPlane.Create` (MissionRunner.cs:489-493). The old plane stays subscribed to the static PlayerInput events until its deferred OnDestroy. Its `OnMove`, `OnFire` and `OnDropBomb` now return early when `!activeInHierarchy` (PlayerPlane.cs:317, 388, 421), so it can't call `SetDirection` and can't fire. |
| B001 one ObjectiveTracker | OK | Teardown keeps `trackers[0]` and destroys the rest. `GetComponent` then returns `trackers[0]` (the first component, even while the extras wait for their deferred destroy), and it is re-Inited. `Init` resets `_objectives` and `_completedCount`. |
| B001 one subscription | OK | The handler is removed in teardown, then `-=` and `+=` are both called in StartMission (lines 183-184). OnDestroy always unsubscribes. |
| B001 camera at start | OK | `ResetCameraToMissionStart` sets x = 0 and keeps y and z. It runs before `ScrollController.Create`, so `FarthestX = MissionDistanceX = 0`. |
| B001 limits | OK | `SetLimits(sc.MissionDistanceX /*0*/, data.length)`. `Create` resets `_direction`, `_velocity`, `_minX`, `_maxX` and `_farthestX`. |
| B001 terrain/parallax at start | OK | Both are created after the camera reset, so TerrainStrip builds its first chunk window around x = 0. |
| B001 HUD | OK | The old canvas is deactivated and destroyed, and CreateHud makes a new one. GameManager doesn't cache the HUD. |
| B002a W+A takeoff | OK, with a caveat (P2-1) | `tookOffThisTick` skips `UpdateFacing` on the takeoff tick, so the plane takes off facing right. A alone on the runway doesn't trigger takeoff, and `UpdateFacing` only runs when `!_onRunway`. |
| B002b SetLimits | OK | The call is unconditional. `SetLimits` collapses max < min, which pins the camera at the start. |
| B002c Bullet Awake | OK | `MakeProjectile<T>` does `new GameObject` (active) and then `AddComponent<T>()`. In Play mode, Awake runs inside AddComponent, before the pool's `SetActive(false)` and long before the first `Pop`/`Init`. Bullet has no `Start` any more, so nothing overrides the value later. |
| B002d manifest | OK | Valid JSON. `com.unity.test-framework: 1.8.0` matches `packages-lock.json` (1.8.0). The lock entry will change from depth 1 to depth 0 when Unity re-resolves, and that's expected. |

## The two concerns mechanics raised

1. **Deferred Destroy of terrain, parallax and scroll: not a real bug.**
   - **TerrainStrip:** `TerrainStrip.Create` assigns `Instance = strip` synchronously, and the old strip's OnDestroy only clears `Instance` when `Instance == this`. So `Instance` points at the new strip as soon as Create returns, and nothing reads it between the `Destroy` and the `Create` (MissionRunner.cs:146-148).
     - The old strip is not deactivated, so its `Update` may run once more that frame. It rebuilds at most one chunk around x = 0, which is a child that its OnDestroy frees.
     - Unity destroys deferred objects before rendering, so the old strip is never drawn.
   - **ParallaxBackground:** it has no static. The old one's `LateUpdate` may reposition once, and then it dies before rendering.
   - **ScrollController:** it isn't destroyed. It is reused and fully reset by `Create`.
   - **Other torn-down objects:** the old player, enemies and HUD are `SetActive(false)` first, so they get no Update, FixedUpdate, triggers or coroutines.
   - **Old HudView:** it stays subscribed to GameEvents until the end of the frame and would only update hidden text. Harmless.
   - StartMission runs from a uGUI click (in Update) after `HideResultView` sets `timeScale = 1`, so no FixedUpdate runs in between.
2. **Leftover pooled projectiles on Next: a real bug, but only P2 (a leak; nothing interacts with the new mission).** See P2-2.
   - In-flight bullets (3 s lifetime) and bombs from the old mission keep flying near the old camera x (about 300), far from the new mission's entities, which spawn around x 0..10.
   - Old bombs burst on the new terrain off-screen and are pushed into the orphaned old pool.

## Regressions

- **First mission start (nothing to tear down):** OK. Teardown is null-safe: `_playerPlane`, `_hud`, `_background` and `_terrain` are null, the list is empty and `GetComponents` returns 0 trackers. The camera reset is a no-op at x = 0. EditMode test `FirstStart_NothingToTearDown_StartsClean` covers it.
- **Retry (scene reload):** OK. The fresh scene gets a fresh MissionRunner, so the first-start path applies. `ScrollController.Instance` and `TerrainStrip.Instance` are cleared by their OnDestroy. The old MissionRunner's OnDestroy unsubscribes unconditionally, which is now stricter than before. The pool-root leak (P2-2) also applies to Retry.
- **PlayerPlane in EditMode tests:** the new `activeInHierarchy` guard is true for the test planes, so the existing 15 PlayerFacingTests are unaffected.

## Bugs

```
BUG: The W+A takeoff fix only lasts one physics tick; holding A turns the plane left on the next tick
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs:354 (tookOffThisTick) and UpdateFacing:399-403
STEPS: Static. On the runway, hold A, then press W. Tick N: takeoff, UpdateFacing is skipped, Facing = +1,
  and _prevLeft becomes true. Tick N+1, with A still held and D not held: `state.left && !state.right` → Facing = -1
  and SetDirection(-1). That's 0.02 s later, so in play it looks the same as the T924 P2 behaviour.
EXPECTED: ARCHITECTURE §3 PlayerPlane (proposed wording): "W+A also takes off facing right". The intent
  is that a held A doesn't turn the plane at takeoff.
ACTUAL: It literally takes off facing right, then turns left 1 tick later.
  - Option A: accept it, and reword the spec as "A held through takeoff turns the plane right after takeoff".
  - Option B: ignore A after takeoff until it is released (and pressed again).
  This is a design call for the orchestrator.
```

```
BUG: Pooled Bullet/Bomb objects leak on every Next and Retry
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs:169-175 (InitWeaponPools on every Create); Weapons/ObjectPool.cs:32-34, 65
STEPS: Static. Each PlayerPlane.Create builds new Bullet(40) and Bomb(10) pools and 2 new Texture2D/Sprites,
  and replaces the static _pool without destroying the old items.
  - On Next, the old 50 objects stay in the scene. In-flight ones finish and Push into the orphaned pool.
  - The pool root is HideAndDontSave, and DontSave objects are not destroyed on scene load. Every recycled
    projectile is parented under it, so the root and its children also survive Retry's scene reload.
  - In the editor they also survive leaving Play mode ("objects were not cleaned up" warnings).
EXPECTED: ARCHITECTURE §0/§3 pools are per mission and are freed with it. Memory stays flat across missions.
ACTUAL: About 50 GameObjects, 2 textures and 1 root are leaked per mission start. They're harmless in play, because
  old projectiles stay near the old x and expire within their lifetime.
  Fix idea: build the pools once (if _pool == null) and recycle every active item on mission start, or destroy the old pool root and items.
```

```
BUG: Score carries over into the next mission on Next
PRIORITY: P2 polish (pre-existing, on the Next path)
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Core/ScoreKeeper.cs:49-90 (CurrentScore is never reset); MissionRunner.StartMission
STEPS: Static. Win mission 1 with 500 points and press Next. ScoreKeeper isn't recreated and CurrentScore isn't
  reset. Mission 2's HUD starts at 0 (the new HudView) but jumps to 500+ on the first kill. OnMissionEnded(2) then saves
  the cumulative score as mission 2's best score (SaveSystem.SaveBestScore).
EXPECTED: per-mission best scores (ARCHITECTURE SaveSystem, UI_SPEC ResultView "score"). Retry (scene reload) starts at 0.
ACTUAL: mission N's best score includes the earlier missions' points when it is reached via Next. B001's "clean start"
  doesn't cover the score. Add a score reset (for example ScoreKeeper.ResetScore) to the start of StartMission.
```

### Out of scope (pre-existing, found while tracing; not part of this verdict)

```
BUG: The FlakShell pool is never initialised, so AAA guns never fire
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Weapons/FlakShell.cs:24 (InitPool has no caller anywhere in Assets); AAAGun.cs:187
STEPS: `grep -rn "InitPool" Biplanes/Assets` only finds Bullet.InitPool and Bomb.InitPool (PlayerPlane.cs:173-174).
  When AAAGun fires, FlakShell.Spawn logs "FlakShell pool not initialised" and returns null.
EXPECTED: GDD §2 (AAA "fires up from the ground at the player"), ARCHITECTURE §3 AAAGun.
ACTUAL: AAA is harmless, and the log fills with an error per shell.
```

```
BUG: Bomber bombs come from the player's Bomb pool (Team.Player), so they can't hurt the player
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/BomberAI.cs:188,199 → Weapons/Bomb.cs Spawn/Awake (team = Team.Player)
STEPS: Static. BomberAI calls Bomb.Spawn. Projectile.OnTriggerEnter2D skips targets with target.Team == team, so a
  bomber's bomb skips the player and can damage enemies and AAA instead. It also takes slots from the player's 10-bomb pool.
EXPECTED: enemy ordnance is Team.Enemy and damages the player (GDD §1 "each hit from enemy fire adds damage").
ACTUAL: bomber bombs only hurt enemies, and they can exhaust the pool the player's own bombs draw from.
```

```
BUG: After a win, MissionRunner.Update calls HandleObjectiveComplete every frame
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs:373-378
STEPS: Static. Once AllComplete is true (for any objective type), every Update calls HandleObjectiveComplete. SetState(Won)
  is guarded, but ScoreKeeper.OnMissionEnded → SaveSystem.SaveBestScore and the unlock run every frame while the
  result screen is up, because Update still runs at timeScale 0.
EXPECTED: the mission end is handled once.
ACTUAL: repeated saves, which means PlayerPrefs writes every frame (and on WebGL, IndexedDB syncs). Guard it with a _missionEnded flag.
```

## EditMode tests added or changed (compiled, not run)

`Biplanes/Assets/Biplanes/Tests/EditMode/`:

- **New `MissionRestartTests.cs` (3 tests):**
  - `FirstStart_NothingToTearDown_StartsClean`.
  - `Next_SecondStart_TearsDownFirstMission`: StartMission(1), spawn 2 trees, camera at 300, then StartMission(2). It checks:
    - The old plane and trees are destroyed.
    - There is 1 PlayerPlane, 1 ObjectiveTracker (the same instance), 1 HUD canvas, 1 EntityDestroyed subscription and 1 ScrollController.
    - The camera x is 0 and y/z are kept.
    - FarthestX is 0 and the limits are [0, 400].
    - Direction and Facing are +1.
  - `ThirdStart_StillOneOfEverything`.
  - The subscription count comes from reflection on the `GameEvents.EntityDestroyed` backing field.
  - Teardown in EditMode uses DestroyImmediate, so the tests check the end state, not Play mode's deferred-destroy timing.
- **`PlayerFacingTests.cs` (+4 tests):**
  - `Runway_WPlusA_TakesOffFacingRight`.
  - `AfterTakeoff_FreshA_TurnsLeft`.
  - `InactivePlane_IgnoresMoveAndFire`.
  - `Bullet_FirstSpawn_UsesBalanceSpeedAndDamage`. The factory invokes Awake the way Play mode's AddComponent does, and the test asserts that Bullet has no Start.

That makes 37 EditMode tests in total. The new `MissionRestartTests.cs` needs Unity to generate its `.meta` on the next import.
Risk: the restart tests run the real StartMission in EditMode, which loads Resources, builds terrain textures and runs
`StaticObstacle.Spawn`. If anything there logs an error in EditMode, Unity's LogAssert fails the test. In that case the
harness is the cause, not B001.

## Manual playtest checklist (B001/B002)

1. Win mission 1 and press **Next**. Mission 2 starts at camera x 0, with no spawn burst on the first frame.
2. The Hierarchy shows exactly 1 PlayerPlane, 1 TerrainStrip, 1 ParallaxBackground, 1 HUD Canvas under MissionRunner, and 1 ObjectiveTracker on it.
3. In mission 2, fly left: the camera stops at x 0. Fly right: it stops at 400, and surviveToEnd completes there, not after 100 units.
4. Kill one enemy: ObjectiveTracker logs it once, and the HUD counter drops by 1.
5. Chain Next through missions 2→3→4→5. Repeat checks 1-4 each time. Watch the Profiler's GameObject count for growth (P2-2).
6. Note the score at the start of mission 2 after Next, then compare it with Retry (P2-3).
7. Retry after a loss. The mission is clean and starts at x 0, the same as before B001.
8. On the mission 1 runway, hold A and then press W. The plane takes off; note that it turns left almost at once if A is still held (P2-1). Pressing A alone on the runway does nothing.
9. The first burst after a mission start (and after Next) flies at the same speed as later bursts (14).
10. Unity: the Package Manager resolves with no change, then run Test Runner → EditMode → all 37 tests pass.
