# T941 verify: playtest-fix batch (B022, T937, B023, B025, B026)

**Verdict: PASS.** There are no P0 or P1 bugs. I found 3 P2 bugs in production code, plus 1 test defect that QA already fixed. Unity is open, so I could not run the tests or play the game. Everything below comes from reading the code, compiling it and scratch-building the tests.

## 1. Commands

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes (+2 new files not yet in the csproj), PASS Biplanes.Editor, exit 0 |
| `bash tools/unity.sh compile/test` | Not run: the editor has the project open |
| Tests scratch build (B018 method, test csproj bumped from v4.7.1 to v4.7.2, all `Tests/EditMode/*.cs`) | 14 files, 0 errors (13 existing files plus the new `GameFlowTests.cs`). No `zz_*` csproj or other file was left in the project |
| `python tools/validate_missions.py` | M2, M3 and M5 OK. M1 spawns[1,5] and M4 spawns[2,9] fail with "y must be in [0, 8.5]". That is the known V001, out of scope |

Static check:
- Namespaces match the folders: `EnemyBullet` is in Biplanes.Weapons and `PlayerInputGate` in Biplanes.Player.
- `using UnityEngine.InputSystem` is present in every file that uses `Keyboard`/`Mouse`: GameManager, PlayerInput and BriefingView.
- No `rb.velocity` and no `UnityEngine.Input`.
- No hand-written YAML. Unity generated the .meta files for the new scripts.
- The new public types each have their own file. `EnemyFireData` sits in GameBalance.cs, which follows the existing pattern for Balance data classes.
- Every type, method and event used exists.

## 2. Flows (by reading the code)

| Flow | timeScale | Screen | OK |
|---|---|---|---|
| Boot → Menu (`EnterMenu`) | 0 | MainMenuView | yes |
| Menu click → `ShowBriefing(n)` (leaving Menu sets 1, entering Briefing sets 0) | 0 | BriefingView (sort order 105, full backdrop) | yes |
| Briefing → `StartMission`, MainMenu scene: sets `_pendingMission` and loads Game. The briefing dies with the scene, then `OnSceneLoaded` → `StartMission` → Playing | 1 | HUD | yes |
| Briefing → `StartMission`, Game scene: runner found → Playing (`BriefingView.Hide`) | 1 | HUD | yes |
| Esc while Playing → Paused: PlayerInput disabled | 0 | PauseView | yes |
| Esc or Resume → Playing: inputs re-enabled (`OnEnable` resets the gate) | 1 | HUD | yes |
| Quit from Pause → `QuitToMenu`: restores inputs and timeScale 1, loads MainMenu, then `OnSceneLoaded` → Menu | 0 | Menu | yes. The world runs one unpaused frame before the load, which is harmless |
| Crash (ground, second ram, flak to 100%) → `TriggerCrash`: input and collider off, `LoseMission(reason, 1 s)`. State is Lost and `MissionEnded` fires at once, then MissionRunner stops the scroll and the input | 1 for 1 s (real time) | none during the fall, by design | yes |
| `OpenResultAfter`: 1 s real time, opens only if the state is still Lost | 0 | ResultView | yes |
| Result Retry → `RetryMission`: hides the result, sets timeScale 1 and reloads Game. `OnSceneLoaded` starts the mission (no briefing, per UI_SPEC §3.2) | 1 | HUD | yes |
| Result Next → `NextMission` → Briefing n+1 (no scene reload; MissionRunner tears down the previous mission). Next after mission 5 → menu | 0 | BriefingView | yes |
| Result Menu → `QuitToMenu` | 0 | Menu | yes |
| Win → `SetState(Won)` → result opens at once, then Next | 0 | ResultView | yes |

**Frozen world with no screen:**
- Every state that sets timeScale 0 builds its own view in the same call: Menu, Briefing, Paused, and the result when it opens.
- The only window with no screen is the 1 s crash fall, and the world runs then (timeScale 1).
- Any state change cancels a pending result.
- Esc is ignored in Briefing, Lost and Won.
- A loss while Paused leaves Paused first (timeScale 1, inputs back) before entering Lost. GameFlowTests covers this.

## 3. Input (B026)

- Fire is `Mouse.leftButton.isPressed`, bomb is `Keyboard.spaceKey.wasPressedThisFrame`, and PlayerInput never reads Enter. The only other Enter reader is the BriefingView skip.
- While MainMenu, Briefing, Pause or Result `IsShowing`, or when timeScale is 0, the gate returns nothing.
- A button held across a screen, or already held when the input is enabled, counts as spent until it is released.
- The Space that skips the briefing never drops a bomb. Both orders are covered: the gate starts blocked, and the new plane's PlayerInput is created after the press.
- uGUI `onClick` fires on pointer-up, so Begin, Resume, Retry and Next clicks are released before the input comes back.
- Pointer-over-UI check: `EventSystem.current.IsPointerOverGameObject()`, only on the frame of the press.
  - Every HUD graphic has `raycastTarget = false`, and the scene "UI" canvas is empty, so in play it never steals a fire click.
  - Game.unity has an EventSystem with InputSystemUIInputModule (`activeInputHandler: 1`). `EnsureEventSystem` adds one with default actions when it's missing.
- WebGL without focus: no key or button events reach Unity, so nothing fires.
  - A null `Mouse.current` or `Keyboard.current` reads as released.
  - The click that refocuses the canvas mid-play can fire. That's acceptable, because left click is the fire button.

## 4. Enemy fire (B023)

**Damage and targeting:**
- `EnemyBullet` is `Team.Enemy` and deals 1 hit unit.
  - A fighter shot uses `Bullet`, which maps to fighterBullet: **8%**.
  - A bomber gunner shot uses `Bomb`, which maps to bomberGunner: **10%**.
  - The tests assert 8 and then 18.
- Enemies (same team) and neutral objects are skipped without recycling the bullet.
- A gunner bullet spawns inside the bomber's own hitboxes and is skipped by the team check.

**Firing rules:**
- Fighters fire only inside a 40° cone, clamped so they never shoot backwards, and within range 11.
- Both fighters and gunners fire only while on screen, and their cooldowns only run while on screen.

**Pool:**
- The pool is built lazily under the scene root `EnemyBulletPool`.
  - After a reload the dead root triggers `Dispose` and a rebuild.
  - The ObjectPool's hidden root gets `HideFlags.None`, so it dies with the scene.
- After Next (no reload), bullets left in flight are recycled by the off-screen check on the next frame, because the camera resets to x 0.

**Dead or crashing player:**
- Fighters, bomber gunners and zeppelin turrets stop on `HasCrashed` or when the player's Health is dead.
- AAA keeps firing for the 1 s fall (P2-2). It can't hit, because the collider is off.

**Fire arithmetic:**
- Closing speed is fighter speed 3 plus the scroll speed. A fighter enters about 12.5 u in front of the player and gets in range at 11 u.
  - First shot after 0.4–1.2 s on screen, so about **1 shot per fighter per pass, 2 at most**.
  - Bullets aim at the player's current position with no lead, at about 10 u/s closing. A player who keeps moving vertically (5 u/s) dodges most shots; a player who holds still gets hit.

| Mission | Fighters | Other hostile fire | Balloons | Worst case if every shot hits | Read |
|---|---|---|---|---|---|
| M1 | 12 | 3 AAA | 1 (+30) | about 12–18 fighter shots → 96–144% spread over 300 u | OK for a tutorial if the player moves. Not lethal per wave: 4 fighters → 32–64% |
| M2 | 8 (3 escort) | bomber gunner + bomb run | 1 | Bomber pass: 3 escorts + gunner ≈ 1.8 shots/s ≈ 15%/s for about 4 s | Hot but fair. The escorts die to one bullet each |
| M3 | 14 (5 dive) | 5 AAA | 1 | 14–20 shots plus flak | **Playtest watch**: the hottest mission. Scroll 3 shortens exposure (about 2.1 s per pass) |
| M4 | 9 | 5 AAA, 6 barrage balloons | 1 | about 9–14 shots plus flak | OK |
| M5 | 15 (5 escort on the zeppelin) | 7 AAA, zeppelin turrets | 2 | The zeppelin fight is long and the escorts sit on the zeppelin's centre | **Playtest watch**: the 5 escorts fire every 2.15 s while the player engages the zeppelin |

None of this is a defect. Tune `enemyFire` (mission-design) only if the playtest shows M3 or M5 being unfair.

## 5. Plane collision (B025) and the crash path (T937) in PlayerPlane

- A contact with a FighterAI or BomberAI hitbox applies:
  - to the enemy: Collision, which is fatal, so the score and `EntityDestroyed` are unchanged;
  - to the player: PlaneCollision, which maps to damageTaken.planeCollision, **50%**.
- `TriggerCrash` runs only when the total reaches 100%.
- No double hit:
  - Dead enemies are ignored, which covers re-entry and the bomber's other hitboxes.
  - A 0.5 s grace window follows each hit, counted down in FixedUpdate.
- These paths stay fatal: terrain (HeightAt → Collision 100), buildings and trees (`StaticObstacle` 100× Collision), EnemyBase and PlayerHQ, and zeppelin ForceCrash.
- **Conflict check:**
  - `OnTriggerEnter2D` returns first thing when `_hasCrashed`.
  - `TriggerCrash` disables the collider, so no later Enter callbacks arrive.
  - A callback already queued in the same step is ignored by the guard.
  - `FixedUpdate` on a wreck only runs `UpdateCrashFall`.
  - A ram that crashes the plane goes through the same `LoseMission(reason, 1 s)` path.
  - New tests cover this: `ContactAfterCrash_ChangesNothing` and `SecondRam_Crashes_ThroughTheSameLossPath`.
- The user's "plane stops after being hit" is explained by two old behaviours: an instant crash on a ram, and the frozen world with no result screen. Both are gone. A flak or bullet hit only raises Health, and nothing but `TriggerCrash`, pause or mission end touches the input or the scroll.

## 6. HUD (B022)

- The bomb row has 7 slots from `Balance player.bombCapacity` (7), each 36 px with an 8 px gap, at 16 px from the bottom-left.
  - Each slot uses the sprite `ui_bomb_icon` (it exists, 96x128, imported as a Sprite), tinted friendly, and pencil when spent.
  - The HUD builds full. `BombBay.Start` raises the count after the HUD subscribes.
- The damage fill is a white sprite with `Filled Horizontal` and fillAmount 0 at the start. It goes from ink to hostile above `smokeThreshold`.
- Every Text uses LegacyRuntime.ttf. Every Image has a sprite and `raycastTarget = false`. HudBombIconsTests covers this.
- This answers the user's "white bar at bottom-left": the slots used to have no sprite and an offset-overwritten size.

## 7. Bugs

```
BUG: Briefing "Begin Mission" button is live during the 0.5 s fade; a double-click on result "Next" skips the briefing
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/UI/BriefingView.cs:136-142,212-213 (CanvasGroup alpha 0, blocksRaycasts left true); ResultView.cs:47 (Next at (0,-120), 150x52) vs BriefingView.cs:42-44 (Begin at (0,-130), 240x56)
STEPS: Static. Win a mission (3 buttons: Next is centred at (0,-120)). Double-click Next: the first click opens the briefing, and the second lands on the invisible Begin button at the same spot, so TriggerTransition starts the mission before the briefing is visible. Space/Enter are gated by `ready` (_elapsed >= fade); the button isn't.
EXPECTED: UI_SPEC §3.2: the briefing fades in over 0.5 s, then starts on 5 s / Enter / Space / button.
ACTUAL: A click in the fade window starts the mission at once.
FIX HINT: set _group.blocksRaycasts = false (or begin.interactable = false) until ready.
```

```
BUG: AAA keeps firing at the falling wreck for the 1 s crash fall
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/AAAGun.cs:156 (no PlayerRef.HasCrashed check)
STEPS: Static. Crash anywhere in range of an AAA gun; for the 1 s before the result screen the gun still starts bursts at the wreck. Fighters, bomber gunners and ZeppelinTurret all gate on HasCrashed. The shells can't hit (the collider is off), so this is cosmetic.
EXPECTED: Consistent with B023: enemies stop firing at a crashed player.
ACTUAL: Flak bursts continue during the fall.
```

```
BUG: (FIXED by QA in tests) PlaneCollisionTests crash cases reach GameManager.Instance unseeded
PRIORITY: P2 polish
OWNER: qa (done)
WHERE: Biplanes/Assets/Biplanes/Tests/EditMode/PlaneCollisionTests.cs (SetUp/TearDown)
STEPS: BuildingContact_StillCrashes, ForceCrash_*, SecondContact_AfterGrace_*, TerrainContact_* call TriggerCrash → GameManager.Instance, whose lazy getter calls DontDestroyOnLoad, which throws outside Play mode (as documented in ObjectiveTrackerTests/ZeppelinAndBaseTests). The first such test in a run errored, then leaked a GameManager GameObject.
EXPECTED: EditMode tests seed singletons by reflection.
ACTUAL: Now fixed: SetUp seeds an un-awakened GameManager in Playing, and TearDown clears it and restores timeScale.
```

**Hygiene (not part of this batch):** `Biplanes/Assets/_Recovery/0.unity` and `0 (1).unity` (with .meta files) are Unity crash-recovery scenes from Nov 12 and today 20:37. They are untracked. Don't commit them; the user can delete them in the editor.

## 8. Tests added or changed
- **New:** `Tests/EditMode/GameFlowTests.cs` (10 tests):
  - Pause_FreezesWorld_DisablesInput_ResumeRestoresBoth
  - Pause_OnlyWhilePlaying
  - LeavingPaused_ForAnEnd_RestoresInputList_AndTime
  - LoseMission_WithResultDelay_EndsAtOnce_AndOnlyOnce
  - Next_OpensBriefingOfNextMission_WorldFrozen
  - ShowBriefing_ClampsMissionIndex
  - Crash_TurnsColliderAndInputOff_LosesAtOnce
  - ContactAfterCrash_ChangesNothing
  - SecondRam_Crashes_ThroughTheSameLossPath
  - FlakTo100_CrashesOnNextTick_NotBefore
- **Changed:** `PlaneCollisionTests.cs` now seeds and clears the GameManager singleton (see the third bug).
- **Run with Unity closed:** `bash tools/unity.sh test`, then GameFlowTests, PlaneCollisionTests, EnemyFireTests, PlayerInputGateTests, HudBombIconsTests, MissionEndRulesTests and MissionRestartTests.

## 9. Combined manual playtest checklist (Unity → Biplanes → BuildScenes, then Play; repeat 1–6 in a WebGL build)

1. **Menu:**
   - How to Play shows 5 lines inside the panel: W/S, A/D, LEFT MOUSE (hold), SPACE (7 bombs), ESC.
   - Click Mission 1.
2. **Briefing:**
   - It fades in over 0.5 s and the world behind it is hidden.
   - The hint reads "SPACE  begin".
   - Space starts the mission and no bomb drops (the HUD still shows 7 bombs). Repeat with Enter, with the button and with the 5 s timer: no shot or bomb on the first frames.
3. **HUD at start:**
   - Seven separate yellow bomb icons at the bottom-left, not a white bar.
   - The damage bar frame is visible and empty.
   - "Score: 0000" and the objective text are visible at the top-right.
4. **Controls:**
   - Hold left mouse to autofire; release to stop.
   - Space drops exactly one bomb per press, and one icon turns grey. Holding Space doesn't repeat.
   - Enter does nothing in play. W/S climb and dive; A/D turn.
5. **Enemy fire (M1):**
   - Orange-red rounds come from the fighters' noses only while you are ahead of them (to their left), never backwards.
   - The shots are staggered, not a volley.
   - A hit adds 8% on the bar and the plane **keeps flying and scrolling**.
   - Keep moving vertically: most shots miss.
6. **Damage:**
   - At 70% the plane is tinted amber and SMOKE shows under the bar.
   - The health balloon restores 30%.
   - Taking flak (15%) never stops the plane.
7. **Ram a fighter:**
   - It explodes and scores, the bar jumps by 50%, and you keep flying.
   - Ram a second one more than 0.5 s later: the wreck drops and tumbles about 1 s while enemies keep moving and stop firing at you, then MISSION FAILED.
8. **Ram the M2 bomber:** it dies and you take 50%. Its gunner adds 10% per hit before that.
9. **Ground, building or tree:** an instant crash. Input stops at once, there's an explosion on the ground, and the result opens about 1 s later. The zeppelin hull and turret are still an instant crash (M5).
10. **Flak to 100%** (let the AAA hit you about 7 times): the same crash fall, then the result.
11. **Pause:**
    - Esc shows PAUSED and the world freezes. A left click or Space does nothing.
    - Esc resumes. Esc → Resume button resumes with no shot fired.
    - Esc while holding the mouse, then Esc again: firing resumes only after you release and press again.
    - Esc → Quit goes to the main menu.
    - Esc during the briefing, the crash fall or the result does nothing.
12. **Result Lost:** Retry and Menu are shown and all texts are visible. Retry restarts the same mission with no briefing, 7 bombs and 0%. Menu returns to the menu.
13. **Result Won:** MISSION COMPLETE!, then Retry, Next and Menu. Next opens the briefing of mission n+1, which then plays. On mission 5 there is no Next.
14. **Objectives and lose paths, per mission:**
    - M1: kill all fighters; map end with fighters left = loss.
    - M2: kill the bomber before it reaches its target ("reached its target" = loss).
    - M3: kill all AAA.
    - M4: bomb the base.
    - M5: kill the zeppelin before the HQ.
    - **Watch:** is M3 (14 fighters + 5 AAA) or M5 (5 escorts on the zeppelin + turrets + 7 AAA) unfair? Note the damage at the end of each.
15. **Console:** no errors and no "No ResultView/PauseView found" warnings.
16. **WebGL (Chrome, Firefox, Safari):**
    - Before the canvas has focus nothing fires.
    - Click to focus and play mission 1 through steps 2–13.
    - Alt-tab away while holding the mouse or Space, then come back: nothing stays stuck.
