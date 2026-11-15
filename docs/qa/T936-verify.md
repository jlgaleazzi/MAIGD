# T936 verify: the goal agent's 8 gaps (T932, T933, T934, T935, B014, M05-ESCORT)

**Verdict: PASS per task (6 of 6). Bugs: 0 P0, 3 P1, 3 P2. All are new and none is a regression.**
The P1s come from walking missions 1-5 end to end. They block missions from finishing, but they
are gaps around the new objective code, not errors in it.

Scope: the six handoffs `docs/crew/handoffs/{T932,T933,T934,T935,B014}-mechanics.md` and
`M05-ESCORT-mission-design.md`. I read every file they list, plus `FighterAI`, `BomberAI`,
`HealthBalloon`, `Bomb`, `Projectile`, `PlayerPlane` (crash paths), `ScoreKeeper`, `SaveSystem`,
`ResultView`, `PauseView`, `ProjectBuilder` (scenes) and the 5 mission JSONs. ARCHITECTURE §3/§4/§5
and GDD §2/Missions were the reference. The Unity editor is open, so this is a compile and static
pass, and the tests were written but not run.

## Results

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS, exit 0 (`PASS Biplanes (+6 new file(s) not yet in the csproj)`, `PASS Biplanes.Editor`) |
| Tests scratch build: runtime DLL built from source, then all 7 `Tests/EditMode/*.cs` built against it (generated test csproj, ProjectReferences swapped for ScriptAssemblies DLLs, v4.7.2). Everything was in the session scratch dir. | PASS, 0 errors. No stale tests: the T935 `Init(..., spawns = null)` signature is source-compatible, and no test called the old `CheckFailAtX(string, float)`. Nothing was written to the project except the 2 new test files (`git status`: no `zz_*`, no obj). |
| `bash tools/unity.sh test` | Not run: the editor is open. |
| `python tools/validate_missions.py` | mission_05 OK (warning: duration 350 s vs 270 s). Missions 1-4 FAIL on the known V001 rules. The M3 "8 bomb targets but only 7 bombs" warning is now a real blocker, see P1-2. |
| Static: namespaces match folders, one public class per file, no `rb.velocity`, no `UnityEngine.Input`, no `Keyboard`, no hand-written YAML, no `Instantiate` in update loops | PASS for all 5 new files and the edited ones. Sprites `zeppelin_hull` 900x220, `zeppelin_turret` 60x40, `enemy_base` 260x120 and `player_hq` 400x100 all exist at 100 ppu with a centre pivot, which matches the hitbox maths. |

### New EditMode tests (2 files, 35 tests, compile only)
- `Tests/EditMode/ObjectiveTrackerTests.cs` (20):
  - Pure tracker:
    - The required counts are M1 12, M3 8 and M4 6.
    - M1 completes on the 12th fighter only, once. Other types, other teams and kills after completion don't count.
    - Types match case-insensitively. Without a spawn list, destroyAllOfType never completes.
    - destroyById completes only for `bomber_01`, and only once.
    - bombTarget matches by id, or by type when the id is empty.
    - M4 needs the balloons and the base, in either order.
    - CheckFailAtX fires at 370 (M2) and 620 (M5), returns null after completion, and never fires for M1/M3/M4.
    - surviveToEnd completes once. Init resets everything.
  - MissionRunner wiring:
    - It seeds GameManager/FxManager/ScoreKeeper and backs up `unlocked_mission`.
    - Start 1 → 2 → 1 leaves one subscription each to EntityDestroyed, MissionEnded and `EnemyBase.Destroyed`, and 11 kills count 11.
    - The 12th kill wins once, and later kills or Updates change nothing.
    - Teardown unsubscribes.
    - M2: an untracked bomber's death doesn't win, and the tracked one wins once.
    - M2 failAtX at 370 loses once.
    - M5: a despawned zeppelin never counts, and the camera at 620 loses. Turret kills don't win; the hull kill wins once.
    - M4: base bombed first, then the 6th balloon wins.
- `Tests/EditMode/ZeppelinAndBaseTests.cs` (15):
  - Balance: `enemySpeed.zeppelin` 1.0, turret damage 12 vs flak 15.
  - DamageSource ordinals are unchanged.
  - Player Health maps ZeppelinTurret → 12 and Flak → 15.
  - `FlakShell.Spawn(dir, ZeppelinTurret)` flies down at ShellSpeed, and a zero direction flies straight down. The 2-argument overload still climbs and reports Flak. A recycled turret shell reused by the AAA overload hits for 15.
  - EnemyBase:
    - Spawn: name, Id, trigger, kinematic body, order 5, bottom edge on the ground.
    - Only Bomb damages it.
    - It dies once, raising EntityDestroyed and `Destroyed` once each, and scores `score.enemyBase` once.
  - PlayerHQ: Neutral, no IDamageable, bottom edge on the ground.
  - Zeppelin:
    - Name `zeppelin [zeppelin_01]`, 3 turrets, hitboxes and turret offsets mirrored.
    - Speed comes from Balance.
    - The shield holds until the last turret is gone. Each turret takes 3 bullets and scores once, the hull takes 40 bullets and scores once, and flak is ignored.
    - `hullVulnerableEarly` bypasses the shield.
    - A despawn 20 units behind the camera raises nothing; one inside the margin keeps the zeppelin.

## Mission walk-through: what wins, what loses (T935 against the JSON)

The loss paths common to every mission:
- A crash: `PlayerPlane.TriggerCrash` → `GameManager.SetState(Lost)` → `MissionEnded(false)` → `_missionEnded`.
- After any end, `HandleObjectiveComplete` / `HandleObjectiveFailed` return early. A second crash is the same state, so it is ignored.
- Losses never save (as before).

| Mission | Win (each objective completes once, `_missionEnded` set, Won, `ScoreKeeper.OnMissionEnded(id, true)` saves the best score and unlocks N+1) | Lose besides crash |
|---|---|---|
| M1 | 12 `EntityDestroyed("fighter", Enemy)`: the spawn groups are 3+2+4+3. Shooting a fighter or ramming it both count (ramming also crashes you). | None. If one fighter escapes left past the camera's start limit, the mission can never end (P1-1). |
| M2 | `bomber_01` dies. Its Health is tracked through `_idTargets`, and only that instance's IsDead counts. The escorts (`esc_01`, `esc_02`, ids on fighters) aren't tracked because the fighter case doesn't call TrackIdTarget, which is correct. | Camera x ≥ 370 while it is alive → Lost once. Turning back keeps the camera under 370 forever, and the bomber flies left without end and is unreachable once it is past x ≈ -9 (P1-1; T047 owns targetX). |
| M3 | 8 `EntityDestroyed("aaa")` (1+2+3+2). | None. **Unwinnable:** 8 bomb-only targets, 7 bombs, no refill (P1-2). |
| M4 | 6 `barrageBalloon` kills, plus `EnemyBase.Destroyed` with `KilledByBomb` and Id `enemy_base_01`, in either order. The base's own `EntityDestroyed("enemyBase")` only scores; it reaches `OnTargetDestroyed`, which matches no destroyById. | None. With all 7 bombs spent and the base standing, it can never end (P1-1). |
| M5 | The `zeppelin_01` hull dies. That needs the 3 turrets first (9 bullets), then 40 bullets or 10 bombs on the hull. Turret kills are `zeppelinTurret` events and don't touch the objective. | Camera x ≥ 620 while the hull is alive or despawned. The zeppelin despawns about 13 s after spawning unless you chase it, and the HQ is never seen on a loss (P1-3). |

Specific checks from the brief:
- **Kills are counted once after Next/Retry.** StartMission unsubscribes before it subscribes (EntityDestroyed, EnemyBase.Destroyed, MissionEnded), and TearDownPreviousMission and OnDestroy remove all three. There is one tracker, re-Init'ed. Covered by tests.
- **No double-counting between EntityDestroyed and id tracking.** destroyAllOfType only reads EntityDestroyed. destroyById is only reached through `OnTargetDestroyed`, guarded by `IdTarget.Reported` and by `evaluated`. A mission with both kinds of objective on one type would correctly complete both.
- **A destroyed-then-recycled bomber id can't happen.** Bombers, fighters and zeppelins are not pooled: each spawn is a `new GameObject` and death is `Destroy`. Once the tracked instance is Destroyed it is Unity-null and skipped, and `Reported` already stops a second report.
- **A zeppelin that despawns** sets `_destroyed` and Destroys silently, with no event. The tracked reference becomes Unity-null, so `t.Target == null` skips it and it never counts. Only failAtX can end M5 then. Covered by tests.
- **IsDead at event time.** All three tracked kinds set their dead state before raising `EntityDestroyed`: Health `currentDamage`, ZeppelinAI `_damagePercent`, EnemyBase `_dead`. Destroy is deferred.
- **MissionRestartTests still describe the real behaviour.** `_missionEnded`, `OnDestroy`, `HandleObjectiveComplete` and `StartMission` are unchanged. Their teardown calls OnDestroy, which now also drops the EnemyBase subscription.
- **The `else` branch of `HandleObjectiveComplete`**, which re-raises ObjectiveCompleted, is dead: every caller checks AllComplete first. Harmless.

## Per-task verdicts

### T932 EnemyBase / PlayerHQ / HealthBalloon.Heal: PASS
- EnemyBase:
  - `ApplyDamage` returns unless `src == Bomb` and `percent > 0`.
  - `_dead` guards `Die`, which raises `EntityDestroyed("enemyBase", Enemy)` once (ScoreKeeper → 1000 once) and then `Destroyed(this)` with `KilledByBomb = true`.
  - Bomb direct hits and the ground burst both use `DamageSource.Bomb`, and the burst skips the direct-hit target.
- PlayerHQ:
  - TeamTag Neutral and no IDamageable. `Projectile.OnTriggerEnter2D` returns on a null IDamageable, so bullets, bombs and flak pass through.
  - `Bomb.BurstDamage` skips null or non-Enemy targets, so the ground burst never touches it.
  - It has no score key and is never scored.
- Both crash the player on contact (`ApplyDamage(1, Collision)` on Team.Player). That is consistent with GDD §1 "crashes if it hits terrain or buildings".
- Both sit their bottom edge on `HeightAt(atX) + y` (tested). ARCHITECTURE §3 says the same.
- `HealthBalloon.Heal` is a no-op now and no longer throws. The pickup path is unchanged (see P2-3, which predates this task).

### T933 ZeppelinAI / ZeppelinTurret: PASS
- **Shield:** `IsShielded = !hullVulnerableEarly && turrets > 0`. Turrets remove themselves through `NotifyTurretDestroyed` in their once-only `Die`.
- **Scoring:** each turret scores 200 once and the hull 2500 once. Flak and Heal have no effect on either.
- **No per-frame Instantiate:** the turrets are built only in `Spawn`, and shots come from the FlakShell pool.
- **Flipped geometry is consistent:** one `sign` drives `flipX`, all 5 box offsets and the 3 turret x offsets (tested).
- **Projectile resolution:** turret boxes are child colliders on the hull's kinematic body, so `GetComponentInParent<IDamageable>` resolves a turret hit to the turret.
- **Escort lookup:** the GameObject name `zeppelin [zeppelin_01]` contains the escortOf id. FighterAI's substring lookup skips the turret objects, which are named `zeppelinTurret`.
- **Handoff claim corrected, not a bug:** T933 says M5's y 5.0 becomes 3.7 after the screen-top clamp. y is height above terrain (§5), so the hull centre is `HeightAt(60) + 5 = -4.1 + 5 = 0.9`, mid-screen, and the clamp never applies. The hull then spans about y -0.05..1.9 with the ground at -4.1. That leaves room to pass above or below, but mission-design may want it higher.

### B014 enemySpeed.zeppelin / FlakShell overload / DamageSource.ZeppelinTurret: PASS
- `ZeppelinTurret` is appended last, so the other ordinals are unchanged (tested).
- `Health` maps it to `damageTaken.zeppelinTurret` (12), and Flak stays at 15 (tested).
- The 2-argument `FlakShell.Spawn` keeps the forced climb and Flak (the existing tests still hold).
- The 3-argument overload fires along the exact direction, and a zero direction flies down.
- `_source` is reset on every Spawn, so a pooled shell reused by AAA reports Flak (tested).
- `ZeppelinTurret.Fire` uses the new overload and no longer overrides the velocity.
- `enemySpeed.zeppelin` 1.0 is in Balance.json and EnemySpeedData. ZeppelinAI reads it in Awake and falls back to 1.0.

### T934 MainMenuView + GameManager boot: PASS
- **Boot:** `Awake`'s `SetState(Menu)` is a no-op because the default state is Menu. `Start` → `EnterMenu` → timeScale 0 → `MainMenuView.Show`. Game.unity (build scene 0) has a GameManager component, so this runs on Play.
- **Menu → Playing:** `StartMission` calls `runner.StartMission(n)` while timeScale is still 0, which is harmless. `SetState(Playing)` then leaves Menu: `MainMenuView.Hide()` and timeScale 1.
- **Unlock gating:** `RefreshUnlocked` clamps `SaveSystem.GetUnlockedMission()` to 1..5. Locked buttons are `interactable = false` and dimmed, and `OnMissionClicked` checks again. A win saves N+1 through ScoreKeeper (tested for M1).
- **Scene path:** when no MissionRunner exists and the active scene isn't Game, it sets `_pendingMission`, loads "Game", and `OnSceneLoaded` starts the mission. The scene's duplicate GameManager destroys itself in Awake. PauseView's Quit loads "MainMenu", which `OnSceneLoaded` turns into the menu, and the EventSystem is created on demand with `InputSystemUIInputModule`. Statically sound, but unreachable until T937 creates PauseView.
- **B013 confirmed, still P1 (not re-filed).** `RetryMission` calls `LoadScene("MainScene")` (GameManager.cs:219, and ResultView.cs:308 as a fallback). That scene isn't in Build Settings, so Unity logs an error and loads nothing. `SetState(Playing)` then runs anyway, so Retry hides the result screen, sets timeScale 1, and leaves the ended mission running (a crashed plane, or a won world), with `_missionEnded` still true. **Retry does nothing useful today.** It can't be reached yet, because no ResultView is ever created (T937).
  - Fix note for B013: renaming the scene to "Game" is not enough. `SetState(Playing)` would run before the reload, and the reloaded scene would start with no mission, because `_pendingMission` is 0 and the state isn't Menu, so the screen would be blank. Either set `_pendingMission = n` before the load, or call `StartMission(n)` without a reload, as Next does.
- **Next:**
  - `StartMission(n+1)` → the runner tears down and restarts → Playing (verified by tests through the runner). On mission 5, `ShowMainMenu` → the menu over a frozen world → picking a mission works.
  - ResultView shows Next on a loss as well, and skips the unlock gate. That belongs to the T937 ResultView flow.

### T935 MissionRunner spawn cases + ObjectiveTracker rewrite: PASS
The objective logic is correct for all 5 missions (table above, 20 tests). `failAtX` reads the
camera x (`MissionDistanceX`) and is checked before the win check in Update. A win that arrives by
event in the same frame has already set `_missionEnded`, so the two can't race. Spawn cases:
- `enemyBase`, `playerHQ` and `zeppelin` use the ground look-ahead (atX - half screen width - 2) and `(atX, HeightAt(atX) + y)`.
- The zeppelin is spawned before its escorts. Its trigger is ≈ 49.1; the fighters' is 60.

### M05-ESCORT: PASS
- `"escort"` is a supported pattern. It is listed in ARCHITECTURE §5 and has its own `case "escort"` in `FighterAI.FixedUpdate`, and `Configure` sets it before Start.
- Both escort groups now trigger at camera x 60, after the zeppelin exists (trigger ≈ 49.1), so `FighterAI.Start` resolves the target.
- If the zeppelin dies or despawns, `_escortTarget` becomes Unity-null and the fighters fall back to straight flight.
- The escorts stacking on the hull centre is B015 (tracked). Because the escorts sit inside the shielded hull's boxes, many bullets aimed at them will be absorbed by the hull. Worth knowing for B015's priority.

## Bugs

```
BUG: P1-1 A mission can't end once an objective becomes impossible (soft-lock, only a crash ends it)
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mechanics
WHERE: Missions/MissionRunner.cs Update/HandleObjectiveFailed; Enemies/FighterAI.cs (no despawn); Missions/ObjectiveTracker.cs
STEPS: Static. Now that objectives can complete, a mission whose objective can no longer be met just keeps going:
  M1: let one of the 12 fighters fly past. FighterAI flies left forever and never despawns. The camera can't go left of x 0 (SetLimits), so once the fighter is past x ≈ -9 it is unreachable and kills top out at 11/12. M1 has no failAtX, and reaching length 300 does nothing (no surviveToEnd), so the player flies on until they crash on purpose.
  M3/M4: spend all 7 bombs with AAA (M3) or the base (M4) still standing. Same result.
  M2/M5: failAtX is camera-driven, so turning back (Defender-style) holds the camera below 370/620 forever, even though the bomber is long gone or the zeppelin has despawned.
EXPECTED: ARCHITECTURE §5: "The mission is won when all objectives are complete and lost when player damage reaches 100% or any failAtX triggers". GDD §1: each mission ends in a win or a loss. Something must end a mission whose objective is impossible. Options (pick one design): lose when a tracked id target despawns or leaves the reachable area; lose when a destroyAllOfType target escapes; lose when the camera reaches `length` with objectives open; or add the BombBay refill when the bomb count can't cover the remaining bomb targets.
ACTUAL: The mission continues with no win and no loss. The only way out is a deliberate crash, and the result screen doesn't exist yet (T937).
```

```
BUG: P1-2 Mission 3 is unwinnable: 8 bomb-only AAA required, 7 bombs, no refill
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mission-design
WHERE: Resources/Missions/mission_03.json spawns aaa at x 80 (1), 160 (2), 320 (3), 420 (2); objective destroy_all_aaa
STEPS: Static. destroyAllOfType "aaa" needs 8 (tested: _required = 8). AAAGun takes Bomb damage only (bombDamage 100 = enemyHealth.aaa 100, so 1 bomb each). BombBay starts at 7, and Refill() has no caller. One bomb's 0.5 burst can't reach two guns 3+ units apart. validate_missions.py already warns "8 bomb targets but only 7 bombs and no refills". That warning was folded into V001 (P2), but now that destroyAllOfType really evaluates, it blocks the win even with perfect play. This escalates V001's M3 item; it is not a duplicate.
EXPECTED: GDD Missions 3: "destroy AAA with bombs". The mission must be winnable with the bombs available (ARCHITECTURE §3 BombBay: 7 bombs, full each mission).
ACTUAL: At most 7 of 8 AAA can be destroyed, so the mission can never be won (and with P1-1, never ends).
FIX OPTIONS: drop one AAA (for example the x 80 single, giving 7, which still leaves no margin for a miss), or have mechanics wire BombBay.Refill to health-balloon pickups (needs a GDD call).
```

```
BUG: P1-3 Mission 5: the zeppelin flies away from the HQ and despawns about 13 s after it spawns; the HQ only appears after failAtX
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mission-design
WHERE: Resources/Missions/mission_05.json (zeppelin atX 60, HQ atX 650, failAtX 620); Enemies/ZeppelinAI.cs FixedUpdate (flies left, despawns 20 behind the left edge)
STEPS: Static, at scrollSpeed 2 with the zeppelin at 1 u/s, flying right:
  - The zeppelin spawns at x 60 when the camera reaches about 49.1.
  - Its centre is on screen from about 0.7 s to 6.6 s after spawning.
  - It despawns silently at about 13.3 s (camera about 75.6).
  - Killing it takes 9 + 40 bullets: about 6.1 s of continuous fire at fireRate 8. So unless the player turns back to chase it, the objective is gone by camera x about 76.
  - The loss then comes only at camera x 620: (620 - 76) / 2 ≈ 272 s later, about 4.5 minutes of a mission that can no longer be won.
  - The HQ (650) spawns at camera about 639, after the 620 loss, so the player never sees what the zeppelin was meant to reach.
  - The zeppelin flies left, away from the HQ, which is to the right.
EXPECTED: GDD Missions 5: "Destroy it before it reaches its destination, the player's HQ." The zeppelin should be travelling toward a visible HQ, and the loss should come when it gets there.
ACTUAL: The HQ is cosmetic and appears only after the mission has already ended. Most of the mission is unwinnable dead time, and turning back avoids the loss forever (P1-1).
FIX OPTIONS: place the HQ behind the player (left of the start, like M2's airfield) and fail when the zeppelin's x reaches it (mechanics: a ZeppelinAI targetX, the same need as T047 for BomberAI); or keep the camera proxy but end the mission (lose) when the tracked zeppelin despawns, and put the HQ before failAtX.
```

```
BUG: P2-1 Crashing after a win flips GameManager from Won to Lost and raises MissionEnded(false)
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Core/GameManager.cs:106 SetState (no guard for Won→Lost); Player/PlayerPlane.cs:489 TriggerCrash
STEPS: Win any mission (now possible), then keep flying and hit the ground. There is no ResultView to pause time (T937), so the plane is still live. TriggerCrash → SetState(Lost) → state Lost and MissionEnded(false, "Player crashed").
EXPECTED: ARCHITECTURE §3 Missions: the mission end is handled once per mission. A win stays a win.
ACTUAL: MissionRunner ignores it (_missionEnded), and the save and unlock already happened. But GameManager.CurrentState becomes Lost and any MissionEnded listener sees a loss. Once T937 adds ResultView (timeScale 0) this becomes mostly unreachable. Still, guard SetState: ignore Won/Lost when already Won/Lost.
```

```
BUG: P2-2 Ramming a zeppelin turret may leave the turret alive (depends on callback order)
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Enemies/ZeppelinTurret.cs:112 OnTriggerEnter2D; Enemies/ZeppelinAI.cs:254 OnTriggerEnter2D
STEPS: Static. The T933 handoff says the hull also gets the trigger callback for a turret collider (compound body). If the hull's handler runs first, it crashes the player (HasCrashed = true). The turret's handler then returns at `player.HasCrashed` before `ApplyDamage(1, Collision)` on itself.
EXPECTED: T933 handoff and ARCHITECTURE §3: "A rammed turret is destroyed; the hull is not."
ACTUAL: Whether the turret is destroyed (+200, one less shield) depends on which handler Unity calls first. Fix: in the turret, kill itself before the HasCrashed early-return, or have the hull call it. Confirm in playtest (checklist M5-7).
```

```
BUG: P2-3 Every health-balloon pickup logs "Health: unknown DamageSource 'Heal', using 1x"
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Enemies/HealthBalloon.cs:123 (health.ApplyDamage(-restore, DamageSource.Heal)); Core/Health.cs:107 GetDamageMultiplier (no Heal case)
STEPS: Static. Fly through any health balloon. The heal works (−30 × 1, clamped), but the default branch logs a warning each time. This predates the B014 Health edit, which added the ZeppelinTurret case but not Heal.
EXPECTED: No warning for a supported source. Add `case DamageSource.Heal: return 1f;`, or call Health.Heal(restore).
ACTUAL: One console warning per pickup.
```

Notes (not bugs):
- `GameManager.Instance` never returns null, because its getter creates the object. So the "without a GameManager, raise MissionEnded" fallbacks in `MissionRunner.HandleObjectiveFailed` and `PlayerPlane.TriggerCrash` are dead code.
- The HUD shows only the destroyAllOfType count. M2 shows "bomber 1", and M4 doesn't show the base objective. That is T937/HUD scope.

## Consolidated manual playtest checklist (menu → result, missions 1-5)

Setup:
- Unity → Biplanes → BuildScenes, then Play Game.unity (Gizmos on for hitboxes).
- Reset the save first with `PlayerPrefs.DeleteAll` or by deleting `unlocked_mission`.

Until T937 lands there is no result screen, so read wins and losses from the Console: `MissionRunner: all objectives complete — mission won!`, `objective '…' failed (failAtX reached)`, and `[GameManager] State changed: Won/Lost`.

Run first, by reference (still needing a human):
- `docs/qa/T924-verify.md` § Manual playtest checklist steps 1-19: reversible flight, limits, terrain and parallax.
- `docs/qa/B001-verify.md` § Manual playtest checklist: Next/restart teardown.
- `docs/qa/B003-verify.md` § Manual playtest checklist: AAA flak, enemy bombs, pools.
- `docs/qa/B010-verify.md` § Consolidated manual playtest checklist: bomb burst, AAA lead, balloons pass-through. It includes `docs/qa/B008-verify.md` by reference.
- `docs/qa/B012-verify.md` § Manual playtest: bomber hitboxes and flip.

Menu (T934):
1. Play opens on the blueprint menu (logo or "BIPLANES"). The world is not visible or moving. Only Mission 1 is bright; 2-5 are dim, with no hover and no click.
2. "How to Play" toggles the controls panel. Hover gives a pencil tint, press a red tint.
3. Set `unlocked_mission = 3` and replay: missions 1-3 can be clicked.
4. Clicking Mission 1 removes the menu. The mission starts at timeScale 1 with the HUD, and the Console shows no errors.

Mission 1 (destroyAllOfType fighter ×12):
- M1-1. Take off from the runway with D or W. Smoke appears at 70% damage. The health balloon at x 170 heals (expect the P2-3 warning).
- M1-2. Kill all 12 fighters. Each kill logs `destroyed fighter k/12`, and the 12th logs a win once. `unlocked_mission` becomes 2.
- M1-3. Let one fighter pass and keep going to x 300: no win and no loss (documents P1-1).
- M1-4. Crash: Lost once. Crashing again after a win flips the state to Lost (P2-1).

Mission 2 (destroyById bomber_01, failAtX 370):
- M2-1. Killing the escort fighters doesn't win. Killing the bomber (8 bullets or 2 bombs) wins once.
- M2-2. Fly past the bomber to camera x 370: Lost once (the text still says "Player crashed", B016).
- M2-3. Turn back before 370 after the bomber has left: the mission never ends (P1-1).

Mission 3 (destroyAllOfType aaa ×8):
- M3-1. Bombs kill AAA one each, and bullets do nothing. With 7 bombs, confirm the 8th AAA can't be destroyed (P1-2).

Mission 4 (6 barrage balloons + bomb enemy_base_01):
- M4-1. The base appears at x 460, sitting on the ground. Bullets and flak don't hurt it, and touching it crashes you.
- M4-2. One bomb destroys it with an explosion and +1000 once.
- M4-3. Win only when both the 6 balloons and the base are done, in either order.

Mission 5 (destroyById zeppelin_01, failAtX 620):
- M5-1. The zeppelin enters from the right around camera x 50, mid-screen (centre y ≈ 0.9), flying left at 1 u/s. The 5 escorts converge on it (they stack, B015).
- M5-2. The hull ignores bullets and bombs while any turret lives.
- M5-3. Each turret dies to 3 bullets (+200).
- M5-4. Turret shells fly down at you, and each hit adds 12%. AAA hits still add 15%.
- M5-5. After the last turret, the hull dies to 40 bullets, with 3 explosions, +2500 once, and the win.
- M5-6. Don't chase it: it disappears about 13 s after spawning with no score. The loss arrives only at camera x 620, and the HQ (x 650) is never seen (P1-3).
- M5-7. Ram a turret: you crash. Check whether that turret is destroyed (P2-2). Ram the hull: you crash and the hull survives.
- M5-8. The HQ (if you reach it after a win): bullets and bombs pass through, no score, and touching it crashes you.

Flow (after T937/B013):
- F-1. Retry restarts the same mission cleanly.
- F-2. Next goes to N+1. On M5, it goes to the menu.
- F-3. Esc opens Pause, and Quit loads MainMenu, which shows the menu.
- F-4. WebGL in Chrome, Firefox and Safari: the menu buttons respond to the mouse (InputSystemUIInputModule), and all text renders, including the views' fonts (T937).
