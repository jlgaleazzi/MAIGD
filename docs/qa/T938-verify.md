# T938 verify: B017, B018, V002, V003, V001a (mission end rules, entity failAtX, M2/M3/M5 data, validator)

**Verdict: FAIL. Bugs: 0 P0, 1 P1, 5 P2.**
- The code tasks pass: B017, B018 and V001a. V002 (mission 3) passes too.
- V003 fails. Mission 5's zeppelin now flies at ground level. Its underside turret can't be shot, so the hull never loses its shield and **mission 5 can't be won** (P1-1).
- The 3 P1s and 3 P2s from T936 are fixed. The new bugs are all in the M5/M2 data or are small follow-ups.

Scope:
- Read: the handoffs B017, B018, V002, V003 and V001a.
- Code: `MissionRunner`, `ObjectiveTracker`, `GameManager`, `Health`, `ResultView`, `ZeppelinAI`, `BomberAI`, plus `ZeppelinTurret`, `FighterAI` (escort lookup), `ScrollController` and `TerrainStrip`.
- Data: `mission_01..05.json`, `Balance.json` and `tools/validate_missions.py`.
- Reference: ARCHITECTURE §3/§5, the GDD Missions section and the 2026-11-13 change log.

Unity is open, so I couldn't run the tests. I checked compile and logic statically, then scratch-built the tests.

## Results

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS, exit 0: `PASS Biplanes (+6 new file(s) not yet in the csproj)`, `PASS Biplanes.Editor` |
| Test scratch build. The runtime DLL is built with compile_check's helpers, then all 9 `Tests/EditMode/*.cs` files are built against it (ProjectReferences become ScriptAssemblies DLLs, v4.7.2). The build runs in the session scratch dir. | PASS, 0 errors. Nothing is left in the project (no `zz_*` files; `Biplanes/obj` already existed and is gitignored). |
| `bash tools/unity.sh test` | Not run: the editor has the project open. |
| `python tools/validate_missions.py` | Exit 1. `OK mission_02`, `OK mission_03`, `OK mission_05`. M1 and M4 FAIL only on "y must be in [0, 8.5]": M1 spawns[1] and [5], M4 spawns[2] and [9]. These are the known V001 items and out of scope. |
| Static: namespaces match folders, one public class per file, no `rb.velocity`, no `UnityEngine.Input`, `Keyboard` only in files with `using UnityEngine.InputSystem`, no hand-written YAML | PASS for every file in the 5 handoffs. |

### Stale tests repaired (QA-owned, no production change)
V002 and V003 changed the mission data after B017/B018 wrote their tests. Seven tests compiled, but they would **fail at run time**:
- `ObjectiveTrackerTests.DestroyAllOfType_RequiredCount_IsTheSpawnTotal_PerMission` expected M3 = 8. It is now 5.
- `ObjectiveTrackerTests.DestroyById_M2_CompletesOnlyForBomber01_Once` used `bomber_01`, now `bomber_1`.
- `ObjectiveTrackerTests.GetFailAtXFor_ReturnsTheObjectiveFailAtX_ForItsTargetIdOnly`: same id problem.
- `ObjectiveTrackerTests.Init_ResetsEvaluatedAndCounts`: same id problem.
- `ObjectiveTrackerTests.Runner_M2_DestroyById_OnlyTheTrackedBomberWins` spawned `bomber_01`, which the real M2 objective doesn't target.
- `ObjectiveTrackerTests.Runner_M5_ZeppelinKill_WinsOnce_TurretKillsDontCount` spawned `zeppelin_01`, which the real M5 objective doesn't target (it targets `zep`).
- `MissionEndRulesTests.MapEnd_CompletedObjectives_DontFail` used `bomber_01`.

All seven now read the target id from the JSON (`M2BomberId`, `M5ZeppelinId`, `data.objectives[0].targetId`), and the M3 count is 5.

### New tests: `Tests/EditMode/MissionDataRulesTests.cs` (9, compiled, not run)
Each test states the expected outcome on the current data:
- `BombOnlyRequiredTargets_FitTheBombBay_AllMissions` (pass): the bomb-only required targets (AAA plus bombTargets) are within `player.bombCapacity` = 7. Counts: M3 5, M4 1, the rest 0.
- `M3_HasAtMostFiveAaa_LeavingSpareBombs` (pass).
- `FailAtX_TargetSpawnsRightOfFailAtX_AtEverySupportedAspect` (pass): checked at 16:10 (WebGL 960x600) and 16:9.
- `M5_PlayerHQ_SitsAtTheZeppelinFailAtX` (pass).
- `AllMissions_EveryRequiredTargetSpawnsBeforeTheMapEnd` (pass).
- `EscortTargets_ExistBeforeTheirEscortsStart_AtSupportedAspects` (pass at 16:10 and 16:9; see P2-2 for 4:3).
- `M2_EscortOf_NameLookupFindsTheBomber` (**expected FAIL**, P2-1).
- `M5_EscortOf_NameLookupFindsOnlyTheZeppelinHull` (**expected FAIL**, P2-2).
- `M5_ZeppelinUndersideTurret_StaysShootableAboveTheTerrain` (**expected FAIL**, P1-1).

The three red tests are regression guards. They go green once the bugs are fixed.

## T936 bugs re-checked

| T936 bug | Status | Evidence |
|---|---|---|
| P1-1 soft-lock (a mission can't end once its objective is impossible) | **Fixed** | `MissionRunner.Update` → `ObjectiveTracker.CheckMapEnd(cameraX, length)` → `HandleObjectiveFailed` → `GameManager.LoseMission(reason)`. The camera is clamped to `[0, length]` (`ScrollController.FixedUpdate` pins `pos.x = _maxX`), so `cameraX >= length` is reached exactly. In M2/M5 the target itself loses the mission through `CheckTargetsReached`, whatever the camera does. Bombers never despawn. A zeppelin with a targetX ahead of it never despawns. The only way to keep a mission running is to choose not to fly forward. |
| P1-2 M3 has 8 AAA for 7 bombs | **Fixed** (V002) | 5 AAA: x 80, 160, 164, 320, 420. 2 bombs spare. |
| P1-3 M5 zeppelin flies away from the HQ | **Fixed geometrically** (V003 + B018), **but see the new P1-1** | The HQ is at 360 = failAtX. The zeppelin spawns at 430 and flies left toward it. |
| P2-1 crash after a win flips the state to Lost | **Fixed** | `SetState(state, reason)` ignores Won/Lost while `IsMissionOver`. |
| P2-2 rammed turret survives | Open as B019 (out of scope) | — |
| P2-3 Heal warning | **Fixed** | `Health.GetDamageMultiplier` has `case DamageSource.Heal: return 1f;` (Health.cs:128). |

## Brief checks

**Retry path (B013).**
1. `RetryMission(n)`: an invalid n falls back to `ActiveMissionIndex`.
2. `HideResultView`, then `timeScale = 1`, then `_pendingMission = n`, then `LoadScene("Game")`. The state stays Won/Lost.
3. On the reload, the scene's duplicate GameManager destroys itself in Awake. The persistent one's `OnSceneLoaded("Game")` sees `_pendingMission > 0` and calls `StartMission(n)`.
4. `StartMission(n)` finds the new MissionRunner, so `runner.StartMission(n)` runs, then `SetState(Playing)`. Leaving Won/Lost for Playing passes the guard, because Playing isn't an ending state.
5. The ResultView fallback also loads "Game". **PASS.** The buttons themselves still can't be clicked (T937, out of scope).

**LoseMission reason flow (B016).**
- Every objective loss goes through `HandleObjectiveFailed(id, reason)`, which sets `_missionEnded` and calls `GameManager.LoseMission(reason)`.
- That calls `SetState(Lost, reason)`, which calls `ShowResultView(false, reason)` → `ResultView.Show(mission, won, score, reason)`, then `RaiseMissionEnded(false, reason)`.
- Reasons:
  - Map end: "Reached the end of the map with an objective open: <desc>".
  - Target reached: "Objective failed: <desc> (the bomber|zeppelin reached its target)".
  - A crash keeps "Player crashed" (`SetState(Lost)` uses `DefaultLossReason`).
- **PASS** (see P2-3 for the wording).

**Won/Lost guard.** `ending && IsMissionOver` → log and return. `HandleObjectiveComplete`, `HandleObjectiveFailed` and `CheckTargetsReached` all check `_missionEnded` first. **PASS.**

**No Heal warning.** **PASS** (above).

**Input off on end.**
- Every `MissionEnded` reaches `OnMissionEnded` → `EndPlayerControl()`. That covers a crash, a win and an objective loss.
- `EndPlayerControl()` does two things:
  - It disables `Biplanes.Player.PlayerInput`, the only class that polls `Keyboard.current` for the plane. PlayerPlane only mentions Keyboard in a comment.
  - It calls `ScrollController.SetScrollSpeed(0)`.
- Next (`ScrollController.Create`) and Retry (reload) restore both.
- **PASS.**

**Static review notes (no bug):**
- `ResolveTargetX` logs and ignores a target that spawns at or left of its failAtX, so a data error can't cause an instant loss.
- Completed objectives never fail: `IsOpenFailAtX` checks `!evaluated`.
- Dead and despawned targets are skipped: IsDead, Unity-null.
- Update order: spawns → target reached → surviveToEnd → map end. A win by event in the same frame has already set `_missionEnded`.

## Mission walk-through

Constants used throughout:
- Camera: orthographicSize 5. Half width 8.89 at 16:9 and 8.0 at WebGL 16:10.
- Planes trigger at camera x ≥ atX and appear half width + 1.5 + rand(0..6) right of the camera.
- Ground objects and zeppelins sit at atX and trigger at atX − (half width + 2): 10.9 at 16:9, 10.0 at 16:10.
- The camera scrolls at `scrollSpeed` and is clamped to [0, length].
- Speeds: fighter 3, bomber 1.5, zeppelin 1.0 u/s.
- Damage: bullet 25. Bomb 100, i.e. 1 bomb per AAA. Bomber 200 hp: 8 bullets or 2 bombs.
- Losable by crash in every mission (terrain, ramming, 100% damage).

| M | Length / scroll / time | Objective and spawns | Winnable? | Losable (besides crash) | Soft-lock gone? |
|---|---|---|---|---|---|
| 1 | 300 / 2.0 / 150 s | Kill 12 fighters (3+2+4+3 at camera 30/60/90/210). AAA at 130 and 250, health balloon at 170. | Yes. All 12 are spawned by camera 210. | Map end at camera 300 with a fighter alive or escaped: reason "...: Destroy all fighter". | Yes. An escaped fighter now means a loss at x 300. |
| 2 | 450 / 2.5 / 180 s | `bomber_1` spawns at camera 80, at world x 90.4–96.4 (16:9) or 89.5–95.5 (16:10). Escorts 2+1 (atX 80/85), plus waves at 50 and 150 and a balloon at 65. | Yes: kill the bomber, 8 bullets or 2 bombs. | Bomber x ≤ failAtX 10, (80.4..86.4)/1.5 ≈ **54–58 s after it spawns**, wherever the camera is. Its bomb run starts at x 15. The map end at 450 can't come first: even flying right the whole time, the camera is at most 80 + 58×2.5 ≈ 225 when the bomber arrives. x 10 is on screen whenever the camera is ≤ 18.9, so the player sees the loss. | Yes. Turning back no longer dodges it, and the bomber never despawns. |
| 3 | 500 / 3.0 / 167 s | Destroy 5 AAA (x 80, 160, 164, 320, 420; the last triggers at camera 409). Fighters 3+2+2+4+3. | Yes. 7 bombs, 2 spare. | Map end at camera 500 with AAA standing (for example, bombs spent). | Yes. |
| 4 | 600 / 3.0 / 200 s | 6 barrage balloons (40, 45, 180, 186, 400, 405) **and** bomb `enemy_base_01` (x 460, trigger 449). | Yes. 1 mandatory bomb out of 7. | Map end at camera 600 with balloons or the base left. | Yes. |
| 5 | 540 / 2.0 / 270 s | `zep` spawns at world x 430 when the camera reaches 419.1 (16:9) or 420.0 (16:10). HQ at 360 = failAtX (spawns at camera 349). Escorts 3 at camera 420 and 2 at 425. | **No. See P1-1.** | Zeppelin x ≤ 360: **70 s after it spawns**. Map end at camera 540: (540 − 419.1)/2 ≈ **60.5 s** after the zeppelin spawns if the player flies right the whole time. | Yes. |

**M5 geometry (brief question):**
- The HQ (world 360) and the zeppelin (430) are correctly ordered: the zeppelin flies left into the HQ, and the HQ spawns about 35 s before it.
- The map end is at **camera** x 540, which is 110 units right of anything in the mission. A player who keeps flying right reaches it about 10 s **before** the zeppelin would reach the HQ. That player loses with the map-end reason, not "reached its target".
- A player who turns back to fight (the intended play: closing speed 3 u/s, the zeppelin passes about 4 s after appearing) keeps the camera below 540. They can then lose only to the zeppelin reaching the HQ, or to a crash.
- So both loss paths work. The map-end one is the less thematic of the two (P2-4).

**Escort lookup:** `FighterAI.Start` takes the first `TeamTag` whose GameObject name contains `escortOf`.
- **M5:** the hull is `zeppelin [zep]`, but its 3 turrets are `zeppelinTurret`, which also contains "zep" (P2-2). The zeppelin exists before the escorts start at 16:10 and 16:9. At 4:3 (a resizable desktop window) the zeppelin triggers at 421.3, after the 420 group.
- **M2:** the bomber is named `bomber` and has no id, so `bomber_1` is never found and the escorts fly straight (P2-1).

## Bugs

```
BUG: P1-1 Mission 5 can't be won: the zeppelin flies at ground level, so its underside turret can't be hit and the hull stays shielded
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mission-design
WHERE: Resources/Missions/mission_05.json spawns[11] zeppelin "y": 0.9 (x 430 → 360)
STEPS: Static. y is height above terrain (ARCHITECTURE §5; MissionRunner: HeightAt(atX) + y).
  - HeightAt(430) = -4.5 + 0.5 = -4.0, so the hull centre is at -3.1.
  - The underside turret is at local y -0.82: its centre is at -3.92 and its bottom at about -4.12, inside the ground.
  - The gondola boxes reach -4.04, and the envelope box bottom is at -3.68.
  - Along the path the terrain rises to -3.67 at x 360. From about x 400 on, the underside turret is entirely underground.
  - The turret sits horizontally between gondola 1 (x +1.4..+2.1) and gondola 2 (-0.8..-0.1). Any bullet aimed at it first hits a gondola or the envelope, and the shielded hull absorbs it.
  - A plane can't fly level with it: PlayerPlane crashes when its collider bottom touches HeightAt. Bombs land on the envelope.
  - The hull ignores all damage while a turret lives, and hullVulnerableEarly isn't set, so the objective can't complete.
  - Cause: T936 wrote that the OLD data (y 5.0) put the hull centre at world y ≈ 0.9. V003, and the GDD log line "y 0.9", took that world y as the JSON y.
  - Regression guard: MissionDataRulesTests.M5_ZeppelinUndersideTurret_StaysShootableAboveTheTerrain (expected red).
EXPECTED: GDD Missions 5: destroy the zeppelin before it reaches the HQ. Every turret must be killable (ARCHITECTURE §3 ZeppelinAI shield).
ACTUAL: At most 2 of the 3 turrets can die. The zeppelin can never be destroyed, so M5 always ends in a loss (HQ reached at about 70 s, or the map end).
FIX: set y to about 4.9 (centre at world ≈ 0.9 over ground -4.0, the T936 geometry). ZeppelinAI.Start clamps the centre to ≤ 3.7 anyway. The underside turret then sits about 4.1 above the ground at x 430 and about 3.75 at x 360. Also correct the GDD change log "y 0.9" to "y 4.9 (centre ≈ world 0.9)". Suggested validator rule (mechanics): zeppelin y ≥ 3.
```

```
BUG: P2-1 Mission 2 escorts never find the bomber, so they fly straight
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Scripts/Missions/MissionRunner.cs:260 (BomberAI.Spawn(position), no id); Scripts/Enemies/BomberAI.cs:242 (new GameObject("bomber")); Scripts/Enemies/FighterAI.cs:100-106
STEPS: Static. M2's escorts use pattern "escort" with escortOf "bomber_1". FighterAI.Start looks for a TeamTag whose name contains "bomber_1", but the bomber is named "bomber". _escortTarget stays null and the escorts fly straight at 3 u/s. Regression guard: MissionDataRulesTests.M2_EscortOf_NameLookupFindsTheBomber (expected red).
EXPECTED: ARCHITECTURE §5 escortOf: "follow the entity with that id". GDD M2: "bomber with fighter escort".
ACTUAL: No escort behaviour. The mission is still winnable and losable.
FIX: name id'd bombers "bomber [<id>]", as ZeppelinAI.Spawn does (for example an id parameter or a name set in MissionRunner). The Health/EntityDestroyed type key stays "bomber".
```

```
BUG: P2-2 Mission 5 escortOf "zep" also matches the turrets ("zeppelinTurret"), and at 4:3 the first escort group starts before the zeppelin exists
PRIORITY: P2 polish
OWNER: mission-design (id/atX); mechanics optional (exact-id lookup)
WHERE: mission_05.json spawns[9] (atX 420), spawns[10], spawns[11] (id "zep"); FighterAI.cs:103 (substring match)
STEPS: Static.
  (a) "zeppelinTurret".IndexOf("zep") = 0. FindObjectsOfType order is undefined, so each escort follows either the hull or a turret. A turret-bound escort falls back to straight flight once that turret dies.
  (b) The zeppelin triggers at camera 430 - (5·aspect + 2): 419.1 at 16:9, 420.0 at 16:10, 421.3 at 4:3. The 420 escort group's FighterAI.Start runs before the zeppelin exists at 4:3, which the resizable Windows player allows.
  Regression guard: MissionDataRulesTests.M5_EscortOf_NameLookupFindsOnlyTheZeppelinHull (expected red).
EXPECTED: ARCHITECTURE §5 escortOf follows the entity with that id.
ACTUAL: The escorts follow a random part of the zeppelin, and none at all at 4:3 for the first group.
FIX: use an id that isn't a substring of any entity name (for example "zeppelin_01", or "raider"), and move both escort groups to atX ≥ 422. Or have mechanics match "[<id>]" exactly.
```

```
BUG: P2-3 The objective loss reason shows the raw spawn id ("Destroy zep", "Destroy bomber_1")
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Scripts/Missions/ObjectiveTracker.cs:247-248 GetDescription (destroyById → $"Destroy {obj.targetId}"); used by MissionRunner.TargetReachedReason/MapEndReason
STEPS: Static. Lose M5 → "Objective failed: Destroy zep (the zeppelin reached its target)". Lose M1 at the map end → "...: Destroy all fighter".
EXPECTED: UI_SPEC result screen: a readable reason.
ACTUAL: Internal ids and an unpluralised type appear on the result screen.
FIX: use targetType for destroyById ("Destroy the zeppelin") and pluralise destroyAllOfType, or add an optional "description" field to ObjectiveEntry.
```

```
BUG: P2-4 Mission 5: a player who keeps flying right always loses at the map end, 10 s before the zeppelin can reach the HQ
PRIORITY: P2 polish
OWNER: mission-design
WHERE: mission_05.json length 540; zeppelin atX 430, failAtX 360
STEPS: Static at scroll 2.0. The zeppelin spawns at camera ≈ 419.1. The camera reaches 540 after (540-419.1)/2 ≈ 60.5 s. The zeppelin needs (430-360)/1.0 = 70 s to reach the HQ. Nothing spawns past 430, so the last 110 units are empty sky.
EXPECTED: GDD M5: the loss is the zeppelin reaching the HQ. The map end is only the B017 backstop.
ACTUAL: That player gets "Reached the end of the map ..." instead. It is still a correct loss, and the turn-back player gets the HQ loss.
FIX (optional): shorten the length to about 470, so the map end comes just after the zeppelin passes the right edge and the player has to turn, or accept it as is.
```

```
BUG: P2-5 validate_missions.py assumes a bomber spawns at least 10 right of atX; at 16:10 the minimum is 9.5, and at 4:3 it is 8.2
PRIORITY: P2 polish
OWNER: mechanics
WHERE: tools/validate_missions.py:26 BOMBER_SPAWN_OFFSET = 10
STEPS: Static. MissionRunner puts planes at camera + 5·aspect + 1.5 + rand(0..6): 9.5 at WebGL 960x600 and 8.17 at 4:3. A failAtX between atX + 8.2 and atX + 10 passes the validator, but MissionRunner ignores it at runtime (only the map end ends the mission).
EXPECTED: The validator rejects every failAtX that the runtime would ignore.
ACTUAL: It is 0.5–1.8 units too lenient. M2 isn't affected (failAtX 10 vs atX 80).
FIX: BOMBER_SPAWN_OFFSET = 8 (the 4:3 minimum), or document 16:10 as the minimum aspect and use 9.5.
```

## Manual playtest checklist

Setup:
- Unity → Biplanes → BuildScenes, then Play Game.unity with Gizmos on.
- Run at 16:9 and also with the Game view at 16:10 (960x600).
- Until T937 there is no result screen, so read the Console: `MissionRunner: objective '…' failed — mission lost (…)`, `[GameManager] State changed: Won/Lost`, `[GameManager] Mission result: won=… reason='…'`.

Mission 2 (destroy `bomber_1`, failAtX 10):
- M2-1. Start airborne at about 1.5–3 above the ground. There is no "below the ground" warning (playerStart y 3.0).
- M2-2. The warm-up pair at camera 50 and the health balloon at 65 appear. Flying through the balloon heals, with no `Health: unknown DamageSource 'Heal'` warning.
- M2-3. At camera 80 the bomber enters from the right edge, plus 3 escorts.
  - There is no `already at or past its targetX` warning.
  - The escorts fly straight instead of with the bomber (P2-1, until fixed).
- M2-4. Kill the bomber (8 bullets or 2 bombs) → Won once. Then crash into the ground: the state stays Won (the Console shows "Ignoring Lost").
- M2-5. Ignore the bomber and keep flying right. About 54–58 s after it appeared:
  - Lost with "Objective failed: Destroy bomber_1 (the bomber reached its target)".
  - The plane stops answering WASD/Space/Enter, and the camera stops.
- M2-6. Turn back and shadow the bomber without killing it. It starts bombing around x 15 and the loss comes at x 10, on screen. Turning back can't dodge it.
- M2-7. Retry (once T937 makes it clickable): Game reloads, M2 starts fresh at timeScale 1, with score 0.

Mission 5 (destroy `zep`, HQ at 360, failAtX 360):
- M5-1. Start airborne (y 3.0). Fighters at 70, 170 and 275, AAA at 120, 235 and 385, health balloons at 200 and 320.
- M5-2. The player HQ appears on the ground at x 360 (camera ≈ 349). Bullets and bombs pass through it, and touching it crashes you.
- M5-3. At camera ≈ 419 the zeppelin enters from the right, flying left at 1 u/s. **Check its height (P1-1):** on current data it scrapes the ground, with the gondolas and underside turret in the terrain. Confirm that the underside turret can't be shot. After the fix (y ≈ 4.9) it flies mid-screen.
- M5-4. The escorts (camera 420 and 425) close on the zeppelin. Note whether they sit on the hull or on a turret (P2-2). At 4:3, the 420 group flies straight.
- M5-5. After the fix: the turrets die to 3 bullets each, then the hull to 40 bullets → Won once, +2500.
- M5-6. Turn back and let the zeppelin fly. When its centre reaches x 360 over the HQ (about 70 s after it appeared): Lost with "... (the zeppelin reached its target)". The input and the scroll stop.
- M5-7. Keep flying right instead: at camera 540 (about 60 s after the zeppelin appeared) you get Lost with "Reached the end of the map with an objective open: …" (P2-4).
- M5-8. The zeppelin never vanishes behind the camera while it is still right of 360 (B018 despawn rule).
- M5-9. WebGL in Chrome, Firefox and Safari: the same timings at 960x600 and the same escort start.

Flow, all missions:
- F-1. Each loss path shows the right reason: crash → "Player crashed"; map end (M1/M3/M4: fly to the end with a target left); target reached (M2/M5).
- F-2. After any end the controls stop and the camera stops. A second crash or a late kill changes nothing.
- F-3. Retry and Next, once T937 lands.
