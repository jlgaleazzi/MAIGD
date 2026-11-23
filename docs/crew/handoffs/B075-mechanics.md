TASK: B075 (QA T975 BUG-1..4)
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Missions/EnemyPressure.cs
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs
- Biplanes/Assets/Biplanes/Scripts/Enemies/FighterAI.cs
- Biplanes/Assets/Biplanes/Tests/EditMode/EnemyEntrySideTests.cs (5 B075 tests added)
- Biplanes/Assets/Biplanes/Tests/EditMode/EnemyPressureFixTests.cs (two tests updated for the 3-unit reinforcement margin)
- docs/crew/handoffs/B075-mechanics.md
EnemyEntrySideQaTests.cs was not touched.
SUMMARY:
- BUG-1 (P1):
  - Change: `EnemyPressure.DespawnBehindMargin` = `EscapeMargin` (3, was 20). A reinforcement that is not closing is now removed 3 u past the right edge instead of lingering outside the cap.
  - I picked this over counting non-closing fighters toward the cap: counting them would block waves while none are on screen, which breaks the 2 s rule.
  - In a Python replica of QA's `SimulatePressure`, the worst count on screen after the turn drops from 18/12/11 (M2/M3/M4) to 4/3/2, and the gap stays at 2.18 s or less.
- BUG-2 (P2): two changes to the boost, plus a new helper.
  - The entry boost now uses `FighterAI.BoostCameraVelocity(dir, velocity, Direction*scrollSpeed)`, i.e. whichever of the current and the target camera velocity runs farther left. A wave sent mid-turn now starts at full speed.
  - A fighter flying left with the player ahead of it keeps the boost (`BehindBoostApplies`: behind a left-flying player). It holds its standoff instead of falling off the right edge.
  - The boost is never more than the camera's leftward speed. It ends by itself when the player turns right or the fighter turns, so no fighter keeps the extra speed. Facing right, the B059 chase is unchanged.
  - Model with turns every 3-10 s over 1200 s, scrolls 2.6/3.25/3.9: worst gap 2.18 s, worst count on screen 2.
- BUG-3 (P2): `LeftExitMargin` = `FighterHalfWidth` (0.75, was 1.0).
- BUG-4 (P2): `MissionRunner.PlaneEntryMargin(type)` places bombers at least `BomberHalfWidth + EntryGap` (1.75) past the right edge. Fighters stay at 1.5.
VERIFY:
- `python tools/compile_check.py`: exit 0 (PASS Biplanes, PASS Biplanes.Editor).
- The EditMode test assembly also builds with dotnet against the fresh runtime DLL: 0 errors. Unity batch was not run, per the brief.
- QA: run all EditMode tests. These matter most:
  - `EnemyEntrySideQaTests`: all 4 should pass, including `FlyingLeftThenTurningRight_NeverMoreThanMaxLiveFightersOnScreen`.
  - `EnemyEntrySideTests`, including the new `B075_*` tests.
  - `EnemyPressureFixTests`. With the 0.75 left margin, `ReEntry_FasterCruise...` escapes at 6.5-16.5 s; the test requires more than 6 s.
  - `EnemyPressure*`, `FighterChase*`, `FighterPassQaTests`, `FighterOvertakeTests`.
- Playtest M3 or M4: fly left for 60 s, then turn right. You should see at most 6 fighters, and in practice about 2. Fighters behind a left-flying player should keep up and stay on screen. Turning back and forth must never leave the sky empty for more than about 2.2 s, and no fighter may ever reappear from the left.
REQUESTS:
- orchestrator: ARCHITECTURE.md "Enemy pressure", Escapes bullet: change "reinforcements are removed 20 u out" to "reinforcements are removed 3 u out (`DespawnBehindMargin` = `EscapeMargin`, B075), so ones that fell behind a left-flying player can't pile up outside the maxLiveFighters cap". Change the left-exit margin text to "`LeftExitMargin` = `FighterHalfWidth` (0.75 u; was 0.75 + 0.25)".
- orchestrator: ARCHITECTURE.md "Fighter chase", entry boost note. Replace it with: "The boost is max(0, the camera speed along the flight direction), where that speed comes from `FighterAI.BoostCameraVelocity(dir, velocity, Direction*scrollSpeed)` (current or target camera velocity, whichever runs farther that way; `BoostCameraVelocityNow`). It applies while the fighter enters (until its hitbox overlaps the view) and, since B075, also while it flies left with the player ahead (`BehindBoostApplies`), so it holds its standoff behind a left-flying player in M2-M4. Facing right the chase is unchanged." Also change the M2-M4 sentence to: "facing right, a fighter behind falls away (left exit); facing left, fighters keep up behind the player."
- orchestrator: ARCHITECTURE.md §5 / spawn notes: "JSON planes are centred `MissionRunner.PlaneEntryMargin(type)` past the right edge (1.5; bombers at least `BomberHalfWidth` + `EntryGap` = 1.75), plus spacing and stagger." Add `PlaneEntryMargin`, `BoostCameraVelocity`, `BoostCameraVelocityNow` and `BehindBoostApplies` to the API list.
- none for other agents.
