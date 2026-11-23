# T971: verify B068 (bomber 1000 HP, damage smoke, explosion and falling wreck)

**Verdict: FAIL** (1 P1, 1 P2). The code works and all tests pass. But in the game, the
new death sequence (mid-air explosion, falling wreck, ground burst) never shows in
mission 2, the only mission with a bomber. The win freezes time at the killing hit.

## What ran
| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes (+1 new file), PASS Biplanes.Editor, exit 0 |
| `python tools/validate_missions.py` | 5/5 OK (existing duration warnings on M3/M4 only) |
| Unity batch `tools/unity.sh test` (editor closed, no `Temp/UnityLockfile`) | 1st run: total 632, passed 631, failed 1 (`BomberDamageTests.BeginCrash_ReplacesTheBomberWithAVisualOnlyWreck`, a test bug, see below). After the QA test fix: **total 632, passed 632, failed 0** (`Biplanes/Logs/editmode-results.xml`, 2026-11-22 17:59:30Z) |
| Scratch build of tests | Covered by the Unity batch run, which compiled the tests assembly |

Static check:
- `BomberWreck` is in `Biplanes.Enemies` under `Scripts/Enemies`.
- Every type and member it uses exists: `TerrainStrip.GroundBaseY`, `PlanePitch.BodyAngle/NoseDirection`, `FxManager.GetOrNull/SpawnEnemySmokePuff/TriggerExplosion(pos, bool)`.
- None of `rb.velocity`, `UnityEngine.Input` or a hand-written `.meta` appear. Unity generated the `.meta` files in the batch run.
- `BomberCrashData` is a second public class in `GameBalance.cs`. That follows the file's existing pattern (`PitchData`, `EnemyFireData`, `BombBalloonData`), so it is not filed.

## Acceptance results
- **1000 HP everywhere: PASS.**
  - Balance.json `enemyHealth.bomber` 1000.
  - `BomberAI.FallbackHitPoints` 1000, also used when the value is not positive.
  - ARCHITECTURE has the Balance example (line 564), `bomberCrash` (565), §"Bomber smoke and crash (B068)" (394) and "40 bullets or 10 bombs" (583).
  - GDD change log 2026-11-22.
  - The validator has no HP rule, so it needed no change.
  - Stale: `docs/missions/mission_02.md:30` still says "200 hp" (BUG 2).
- **Smoke: PASS.**
  - The levels are exact at the boundaries: 12 bullets / 3 bombs give Light (70%), 24 bullets give Thick (40%), 34 bullets give Heavy (15%). 11, 23 and 33 bullets stay at the level below.
  - Puffs go only through `Kind.EnemySmokePuff` (never the player's kind), nothing comes out at dt 0 or after death, and the first puff comes at once.
  - Same `smoke_puff` sprite as the player's, and the wreck renderer is untinted white (white-only).
- **The kill counts at the killing hit: PASS** (tested through MissionRunner with mission 2's own JSON):
  - The 40th bullet completes `destroy_bomber` and wins the mission. 39 bullets don't.
  - Score +500 once.
  - Hits after death, `BeginCrash` and the wreck landing raise no further `EntityDestroyed` and no second MissionEnded.
- **The wreck is visual only: PASS.**
  - It has no Collider2D, Rigidbody2D, Health, TeamTag or BomberAI, and it never raises EntityDestroyed.
  - `maxFallSeconds` safety net works (gone at 10 s).
  - `ClearAll` on MissionRunner teardown works: StartMission again (Next) leaves 0 wrecks. A scene reload (Retry or the menu) destroys scene-root objects.
- **Tracking: PASS.**
  - After BeginCrash, the escort's `IsEscorting` turns false (it chases), `FindEscortTarget("bomber_1")` returns null, and `EnemyPressure.IsLiveAircraft(bomber)` is false from the killing hit.
  - EnemyAircraft holds no wreck. The minimap uses the same Unity-null/IsDead check (`MinimapView.IsLive`).
  - failAtX: I killed the bomber 0.2 u right of failAtX 10 and moved the dead object and the wreck past 10, then ran Update. The mission stays Won, with no "reached its target". The target is `Reported` at the hit, and `CheckTargetsReached` skips IsDead and Unity-null targets.
  - Other missions with a bomber: none. Only mission_02.json spawns `bomber`.
- **Mission 2 winnable: YES, but much harder. See the estimate below.**

## Bugs

```
BUG: Mission win freezes time at the killing hit, so the bomber's crash and wreck are never seen
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mechanics
WHERE: Scripts/Missions/MissionRunner.cs:550 (HandleObjectiveComplete -> GameManager.SetState(Won)),
       Scripts/Core/GameManager.cs:141-143 (SetState(Won) passes resultDelay 0f), :403-411, :423 (OpenResultView: Time.timeScale = 0),
       Scripts/Enemies/BomberAI.cs:248-253 (BeginCrash runs from FixedUpdate)
STEPS: Static evidence. Mission 2 has one objective, destroyById bomber_1. The killing bullet's
       OnTriggerEnter2D -> Health.ApplyDamage -> EntityDestroyed -> MissionRunner.OnEntityDestroyed ->
       HandleObjectiveComplete -> SetState(Won, reason, 0f) -> ShowResultView(delay 0) -> OpenResultView
       sets Time.timeScale = 0 synchronously, still inside the physics callback. FixedUpdate does not run
       at timeScale 0, so BeginCrash never runs before the result screen. Even if a leftover fixed step ran,
       BomberWreck.Update uses Time.deltaTime = 0 and FxManager animates on scaled time, so nothing would move.
       SmokeLevel is None for a dead bomber, so even its smoke trail stops.
EXPECTED: GDD Change Log 2026-11-22: "when shot down it explodes and falls to the ground as a harmless
       wreck with a ground explosion burst; the kill counts the moment it is shot down". ARCHITECTURE
       "Bomber smoke and crash (B068)". The B068 handoff play checks ("the wreck then explodes once, dives
       and smokes ... lands with a 3-explosion burst").
ACTUAL: In mission 2, the only mission with a bomber, the intact, smokeless bomber sprite freezes
       behind the result screen. No explosion, no fall, no ground burst. Next tears it down. The whole
       B068 death sequence only plays when a bomber dies without winning the mission, which no
       current mission can do.
       Suggested fix (needs an orchestrator design call): delay the win result screen like
       LoseMission(reason, delay), e.g. about 2.5 s of real time, enough for the wreck to fall from
       flight height (about 1.4 s at gravity 5 from y 4 to 5) and burst. SetState already ignores a
       later loss, so the win can't flip. Alternatively, give the win a delay only when the final
       objective is a destroyById bomber/zeppelin.
```

```
BUG: Mission 2 design doc still says the bomber has 200 hp
PRIORITY: P2 polish
OWNER: mission-design
WHERE: docs/missions/mission_02.md:30
STEPS: grep "200 hp" docs/missions/mission_02.md
EXPECTED: Balance enemyHealth.bomber 1000 (40 bullets or 10 bombs), GDD Change Log 2026-11-22, ARCHITECTURE §4 line 583.
ACTUAL: "The objective climax is the bomber kill itself (200 hp, gunner fire)". The doc also has no
       kill-time or feasibility note for 1000 HP. Add the estimate below to the "Bomber catchability" section.
```

## Mission 2 feasibility (1000 HP), an honest estimate
The inputs come from Balance and mission_02.json:
- bulletDamage 25 and fireRate 8/s, so 200 DPS when every bullet hits. That means **5.0 s of continuous on-target fire, at least** (was 1.0 s at 200 HP).
- The bomber flies left at 1.5 u/s, level at a fixed height (pattern straight).
- scrollSpeed 3.25.
- The screen is 17.8 u wide (ortho 5, 16:9). The player sits at screenAnchor 0.3, so **about 12.4 u of sky is visible ahead**.
- Contact comes about 25 s into the mission. Time from contact to failAtX 10: **about 50 s** (mission_02.md).

Pass windows (with matched altitude, the straight-line bullets hit a level bomber reliably):
- **Head-on (player facing right):** closing speed 4.75 u/s, so a 12.4 / 4.75 = **2.6 s** window. That is 21 bullets, at most 525 HP if every one hits. The player must then break off above or below.
- **Stern chase (player facing left, after a turn):** closing speed 3.25 - 1.5 = 1.75 u/s, so a 12.4 / 1.75 = **7.1 s** window. That is 56 bullets (1400 HP potential). One stern pass kills the bomber if at least 72% of the bullets hit.
- **Cycle:** stern pass 7 s, then overtake above or below it, turn (0.6 s), a head-on reset of 2.5 s and turn back (0.6 s). That is about 11-12 s and about 75 bullets per cycle, so **about 4 cycles fit in 50 s**. Overall, about 13-15% of about 300 possible shots must hit.
- **Bombs:** 7 bombs = 700 HP (28 bullets' worth). A stern pass from above at 1.75 u/s relative speed makes bomb hits possible but skill-heavy. Recharge balloons come 4 s after the bay is empty, and the static bomb balloon at x 225 is behind the action.
- **Threats during the fight:** the bomber gunner (10% / 1.5 = 6.7% per hit, every 2.5-3 s within 11 u), 3 escorts that leave formation within 8 u, and the waves at x 93-170.

**Assessment:** winnable for a competent player (2 good stern passes in 50 s). It is noticeably harder than before, and a novice may run out the 50 s while the escorts harass. It is not too hard by the numbers, but it is the toughest kill in the game at the point the game introduces it (mission 2). Two design notes for the orchestrator (not filed as bugs):
1. **The ram bypass:** a mid-air collision is fatal for any enemy (B025, `Health.ApplyDamage` Collision = 100%). So ramming the 1000 HP bomber kills it instantly and only costs the player 33.3%. With 40 bullets needed, ramming becomes the obvious best tactic, which undercuts the toughness change. Covered by test `Ram_KillsTheToughBomberAtOnce_OneEvent`. Decide whether bombers should take only fixed ram damage.
2. If playtests find it too hard, the cheapest knobs are failAtX (lower) or bomber `atX` (earlier), not HP.

## Tests
- **New:** `Biplanes/Assets/Biplanes/Tests/EditMode/BomberCrashQaTests.cs`, 11 tests, all pass:
  - MissionRunner mission 2: the kill wins at the hit, scores once and is not recounted on crash or landing.
  - A kill near failAtX whose wreck drifts past it is still won.
  - Escort, EnemyPressure and escort lookup after the crash.
  - Teardown on Next clears wrecks.
  - Exact smoke thresholds for bullets and for bombs.
  - Puffs only when damaged, as enemy smoke.
  - The wreck's sprite and white tint, with no Rigidbody2D.
  - The maxFallSeconds safety net.
  - Landing raises no event.
  - A ram kills at once.
- **Fixed (test bug, not production):** `BomberDamageTests.BeginCrash_ReplacesTheBomberWithAVisualOnlyWreck` asserted `flipX` without running `BomberAI.Start`, which is what sets flipX. EditMode never runs Start, so the test now invokes Start by reflection first. Production behaviour is correct: Start always runs before any FixedUpdate.

## Manual playtest checklist (mission 2, after the P1 fix)
- [ ] No smoke until about 12 bullets have hit. Light, then thick at 24, heavy at 34. The smoke comes from the nose engine and is white or pencil, never coloured.
- [ ] The 40th bullet (or the 10th bomb, or a mix) gives +500 once and completes destroy_bomber. Then: the mid-air explosion, the wreck dives nose-down smoking, and a 3-explosion ground burst.
- [ ] The result screen opens only after the burst. There is no second +500.
- [ ] Flying through the falling wreck does no damage.
- [ ] Escorts leave formation and chase after the kill.
- [ ] The minimap bomber marker disappears at the kill.
- [ ] Killing it just before x 10 still wins. Letting it reach x 10 loses with "reached its target".
- [ ] Retry, Next and Menu during the fall leave no wreck behind.
- [ ] Pause (Esc) mid-fall freezes the wreck, and it resumes cleanly.
