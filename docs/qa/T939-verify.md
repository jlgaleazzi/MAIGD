# T939 verify: V004 (mission 5 data) and B020 (escort lookup, objective text, validator)

**Verdict: FAIL. Bugs: 0 P0, 1 P1, 1 P2.**
- Both fixes do what T938 asked:
  - The altitude, ids, escort order, M2 escort lookup, objective text and validator are all correct.
  - All five T938 bugs (P1-1 and P2-1..P2-5) are closed as written.
  - The 3 red regression guards from T938 now pass by code reading.
- **Mission 5 still can't be won in practice (new P1-1).** The underside turret is no longer underground. But player bullets only fly horizontally, and gondolas sit to its left and right at the same height. Only a 0.03-unit band of bullet heights reaches the turret without first hitting a gondola or the envelope, and the shielded hull absorbs those bullets.
  - T938 listed this blocking as evidence, then wrongly blamed only the altitude. The fix is in ZeppelinAI geometry (mechanics), not in the mission data.

Scope:
- Read: T938-verify (P1-1, P2-1..P2-5), the V004 and B020 handoffs, `mission_05.json`, `mission_02.json` and `Balance.json`.
- Code: `FighterAI`, `BomberAI`, `ZeppelinAI`, `ZeppelinTurret`, `MissionRunner`, `ObjectiveTracker`, `ScrollController`, `TerrainStrip`, `Projectile`/`Bullet` and the `PlayerPlane` bullet pool.
- Also read `tools/validate_missions.py` and all of `Tests/EditMode/*.cs`.

## Results

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS, exit 0 (`PASS Biplanes (+6 new file(s) not yet in the csproj)`, `PASS Biplanes.Editor`) |
| Test scratch build. The runtime DLL is built from source, then all 9 `Tests/EditMode/*.cs` files are built against it: ProjectReferences become ScriptAssemblies DLLs, retargeted to v4.7.2 for nunit's net472 build. The temporary csproj and the output are in the session scratch dir. | PASS, 0 errors, including the 2 new QA tests. Nothing is left in the project (`git status`: no `zz_*`, no new untracked files). |
| `bash tools/unity.sh test` | Not run: the editor has the project open. |
| `python tools/validate_missions.py` | Exit 1. `OK mission_02`, `OK mission_03`, `OK mission_05`. M1 spawns[1]/[5] and M4 spawns[2]/[9] fail only on "y must be in [0, 8.5]" (known V001, out of scope). |
| Static checks on the B020 files: namespaces match the folders, one public class per file, no `rb.velocity`, no `UnityEngine.Input`, no hand-written YAML/.meta | PASS. `FighterAI.FindEscortTarget` uses `FindObjectsOfType<TeamTag>()`, which is obsolete in Unity 6 and gives a warning, not an error. It runs once in Start, so it isn't a per-frame Find. |

### New QA tests (compiled, not run)
- `ZeppelinAndBaseTests.Zeppelin_EveryTurret_HasAClearHorizontalLineOfFire` (**expected FAIL**, P1-1).
  - It builds the real hull and turret colliders with `ZeppelinAI.Spawn`.
  - For each turret, it finds the widest band of bullet-centre heights (bullet radius 0.08) that reaches the turret from the left or the right without first overlapping a hull box.
  - It requires at least 0.2 units, about two FixedUpdate steps of W/S climb.
  - Results: the top turrets have 0.37 and pass. The underside turret has **0.03** and fails.
- `MissionDataRulesTests.M5_ZeppelinReachesTheHQ_BeforeTheCameraCanReachTheMapEnd` (pass).
  - At 4:3, 16:10, 16:9 and 21:9, the zeppelin's 60 s to the HQ must beat the fastest camera time to the map end by more than 1 s.
  - Every escort group must trigger after the hull.

## T938 bugs re-checked

| T938 bug | Status | Evidence |
|---|---|---|
| P1-1 zeppelin at ground level | **Fixed as reported** (V004), **but see the new P1-1** | JSON y 4.9. `HeightAt(420)` = -4.5 + 0.5 = -4.0, so the hull centre is at world **0.9**. Clamp: camera y 0 (ProjectBuilder), so maxY = 0 + 5 − 1.3 = 3.7 and 0.9 isn't clamped. The turret centres are top fore (−1.90, 1.90), top aft (1.30, 1.90) and underside (0.65, **0.08**), relative to hull x. The highest ground on the path is at x 360: HeightAt = −4.5 + lerp(1.5, 0.5, 80/120) = −3.667. The underside turret clears it by **3.75** (≥ 1.5). Vertical framing doesn't depend on aspect (orthographicSize 5, view y −5..5), so all 3 turrets are on screen at 4:3, 16:10 (960x600) and 16:9. Validator rule `ZEPPELIN_MIN_Y = 3`: 4.9 passes. |
| P2-1 M2 escorts can't find the bomber | **Fixed** (B020) | `MissionRunner` calls `BomberAI.Spawn(position, entry.id)`. `Spawn` sets `SpawnId` before returning, so before any Start runs. The root has `TeamTag` Enemy. `FighterAI.IsEscortTarget` matches through `GetComponent<BomberAI>().SpawnId == id`. The bomber group (spawns[2]) comes before the escort group (spawns[3]) in the same Update, and the escorts' `FindEscortTarget` runs in their Start on the next frame. |
| M2 kill still scores and completes | **Unchanged / OK** | The GameObject name is still `"bomber"`. `Health` raises `EntityDestroyed("bomber")`, so ScoreKeeper scores `score.bomber` = 500. `MissionRunner.OnEntityDestroyed` matches `TypeKey "bomber"` with `IsDead` and calls `OnTargetDestroyed("bomber_1")`. The objective completes, then Won. |
| P2-2 "zep" matches the turrets; escorts before the hull at 4:3 | **Fixed** (V004 + B020) | The id is now `airship_01`, and the hull is named `zeppelin [airship_01]`. Turrets are named `zeppelinTurret` with no `[...]` token and no BomberAI, so they never match. "airship_01" isn't a substring of fighter/bomber/aaa/zeppelinTurret either. The hull triggers at camera (FarthestX) 420 − (5a + 2): **406.3** (21:9), **409.1** (16:9), **410.0** (16:10), **411.3** (4:3). The escorts trigger at camera 424 and 429, so the hull always comes first, by at least 12.7. |
| P2-3 raw ids in the loss text | **Fixed** (B020) | M5: "Objective failed: Destroy the enemy zeppelin (the zeppelin reached its target)". M2: "... Destroy the enemy bomber ...". M1 map end: "Destroy all fighters". AAA: "Destroy all AAA guns". |
| P2-4 the M5 map end comes before the HQ loss | **Fixed** (V004) | See the arithmetic below. |
| P2-5 validator bomber offset | **Fixed** | `BOMBER_SPAWN_OFFSET = 8`. The 4:3 runtime minimum is 6.67 + 1.5 = 8.17, so the validator is about 0.17 stricter than the runtime, which is the safe direction. |

### Mission 5 timing, checked independently
- `Balance.enemySpeed.zeppelin` = 1.0. `ZeppelinAI.FixedUpdate` moves x only, by `_speed * fixedDeltaTime`. `ReachedTarget` is `x <= 360`. Time to the HQ = (420 − 360)/1.0 = **60.0 s** after the spawn.
- The camera's top speed is `scrollSpeed` = 2.0 (`ScrollController` eases toward ±scrollSpeed, then clamps to [0, 540]).
- The fastest time from the zeppelin's spawn to the map end, (540 − trigger)/2:
  - 21:9: 66.8 s
  - 16:9: 65.4 s
  - 16:10: 65.0 s
  - **4:3: 64.3 s**, the narrowest window and the binding case.
- V004 cited 16:9 ("widest") as its case. That is the most generous one, not the binding one. Its conclusion still holds at every aspect: the HQ loss comes first by at least **4.3 s**.
- `ResolveTargetX`: the spawn x is 420 > 360, so the zeppelin is watched and the objective is armed. While the zeppelin is still right of 360 it never despawns (B018).
- **Losable:** yes, three ways:
  - Zeppelin reaches the HQ at about 60 s.
  - Map end, only if the player somehow reaches camera 540 first, which the timing above rules out.
  - Crash.
- **Winnable:** no in practice. See P1-1.

### The 3 T938 regression guards, re-read
1. `M2_EscortOf_NameLookupFindsTheBomber`: `BomberAI.Spawn(pos, "bomber_1")` → the name is "bomber" (the assert holds), and `SpawnId == "bomber_1"` makes `IsEscortTarget` true for the root TeamTag. **Passes.**
2. `M5_EscortOf_NameLookupFindsOnlyTheZeppelinHull`: id "airship_01". The only match is `zeppelin [airship_01]`, so hits = 1, and no entity name contains the id. **Passes.**
3. `M5_ZeppelinUndersideTurret_StaysShootableAboveTheTerrain`: lowest turret at 0.08, ground at most −3.667 from x 420 down to 360, so a gap of at least 3.75 against the 1.5 minimum. **Passes.** It only checks altitude, which is why it misses P1-1. The new line-of-fire test covers that.

B020's own new tests read correctly against the code: `EscortOf_MatchesWholeIdsOnly` ("zep" doesn't match `[zep2]`, `zeppelinTurret`, or a bomber with SpawnId "bomber_1"; "bomber_10" ≠ "bomber_1") and `GetDescriptionFor_ReadsTypesNotRawIds`.

## Bugs

```
BUG: P1-1 Mission 5 still can't be won: the underside turret is screened by the gondolas from every horizontal bullet path
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mechanics
WHERE: Scripts/Enemies/ZeppelinAI.cs:66-78 (Gondola1/2/3 offsets y -0.81, size y 0.26; TurretOffsets[2] = (-0.65, -0.82)); ZeppelinTurret.cs:38 HitboxSize (0.50, 0.30)
STEPS: Static, in hull-local units (flipped to fly left):
  - Underside turret: x 0.40..0.90, y -0.97..-0.67.
  - Gondola 2: x -0.80..-0.10, y -0.94..-0.68, directly left of the turret.
  - Gondola 1: x 1.40..2.10, same y, directly right of it.
  - Envelope: x -4.15..3.00, y -0.58..0.78.
  - Player bullets fly along ±x only (Bullet.Spawn: direction ±1, no pitch). They are circles of radius 0.08 (PlayerPlane MakeProjectile).
  - A bullet from either side overlaps a gondola (or the envelope) before the turret, unless its centre is in y -1.05..-1.02, a 0.03-unit band. The plane moves 0.1 per FixedUpdate when climbing, so in practice it can't hold that band.
  - A shielded hull hit is absorbed: ZeppelinAI.ApplyDamage returns, and Bullet.OnHit still recycles the bullet.
  - Bombs fall onto the envelope first. Ramming a turret crashes the player.
  - Result: the underside turret can't be killed, so IsShielded stays true (hullVulnerableEarly false) and the hull never takes damage. The same geometry was in T938's evidence, but T938 put it down to altitude alone.
  - Regression guard: ZeppelinAndBaseTests.Zeppelin_EveryTurret_HasAClearHorizontalLineOfFire (expected red: band 0.03 < 0.2; the top turrets have 0.37).
EXPECTED: GDD Missions 5: destroy the zeppelin before it reaches the HQ. ARCHITECTURE §3 ZeppelinAI: the shield drops once every turret is destroyed, so every turret must be killable with the player's weapons.
ACTUAL: At most the 2 top turrets die. Mission 5 always ends lost: HQ reached about 60 s after the zeppelin appears, or a crash.
FIX (suggestion): hang the underside turret below the gondola line, for example TurretOffsets[2].y = -1.10. Its hitbox is then y -1.25..-0.95, with a clear band of about 0.31, and it sits 3.47 above the highest ground on the M5 path. Art may want the gondola/turret drawing checked. Alternatives: give the gondolas no colliders, or let bullets pass through a shielded hull. Either one changes the absorb rule, so ARCHITECTURE would need a note.
```

```
BUG: P2-1 The M5 zeppelin pops in on screen instead of flying in from off screen
PRIORITY: P2 polish
OWNER: mechanics (MissionRunner zeppelin trigger margin) or mission-design
WHERE: MissionRunner.Update groundLookAhead = 5·aspect + GroundEntryMarginX (2); ZeppelinAI hull art is 9.0 wide (half 4.5)
STEPS: Static.
  - The zeppelin is placed at atX 420 when the camera reaches 420 − (5a + 2), so the right screen edge is 418.
  - The hull spans 415.5..424.5, so about 2.5 units of it appear at once, already inside the screen.
  - This predates V004 (it was the same at atX 430) and doesn't affect play.
EXPECTED: ARCHITECTURE/MissionRunner comment: ground objects "scroll into view instead of popping in mid-screen".
ACTUAL: The nose of the zeppelin pops in inside the right edge.
FIX: for zeppelins, use a lookahead of at least 5·aspect + 4.5 + 0.5 (the half hull length), or place the zeppelin at the right edge + 5 like planes. Then re-check the timing: a later spawn only adds HQ-vs-map-end margin.
```

## Manual playtest checklist (M2 and M5 deltas since T938)

Setup: Unity → Biplanes → BuildScenes, then Play Game.unity with Gizmos on. Check at 16:9, 16:10 (960x600) and 4:3. Until T937 there is no result screen, so read the Console.

Mission 2:
- M2-1. At camera 80 the bomber enters. The 3 escorts (atX 80/85) now **close on the bomber** instead of flying straight.
- M2-2. Kill the bomber (8 bullets or 2 bombs) → +500, Won once. The Console shows `objective 'destroy_bomber' completed`.
- M2-3. Leave it alone → about 54–58 s later, Lost with "Objective failed: Destroy the enemy bomber (the bomber reached its target)". No raw `bomber_1` appears.

Mission 5:
- M5-1. At camera about 409–411 the zeppelin appears at mid height: hull centre about world 0.9, gondolas well above the ground, every turret on screen at every aspect.
- M5-2. Escorts at camera 424/429 follow the **hull**, not a turret. They keep escorting after a turret dies.
- M5-3. **P1-1 check:** fly level with the underside turret from both sides and fire. Confirm the gondolas absorb every bullet and the turret takes no damage. The top turrets die to 3 bullets each.
- M5-4. Once P1-1 is fixed: kill all 3 turrets, then the hull (40 bullets) → +2500, Won once.
- M5-5. Keep flying right without killing it: Lost with "Objective failed: Destroy the enemy zeppelin (the zeppelin reached its target)" about 60 s after it appears, **before** camera 540, at every aspect.
- M5-6. Turn back and shadow it: the same HQ loss over x 360. Input and scroll stop.
- M5-7. WebGL in Chrome, Firefox and Safari at 960x600: same timings and escort behaviour.
