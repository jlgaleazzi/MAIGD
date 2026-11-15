# M05-ESCORT — mission-design handoff

STATUS: DONE (mission_05.json only; no code touched, no design sheet exists for M5)

## Changes (Biplanes/Assets/Biplanes/Resources/Missions/mission_05.json)
- Escort groups (esc_z1 x3, esc_z2 x2): atX 40 -> 60, so they launch when the zeppelin (atX 60) already exists. The zeppelin is a ground-style entry that spawns at atX minus the look-ahead, so it is always created before the fighters trigger. Fighter count and later waves (280/450/600) unchanged.
- Escort groups: pattern sine -> escort. `Configure` overwrites the pattern, so with "sine" the escortOf was ignored and the fighters never followed the zeppelin.
- Validator fixes (mine): cleared ids on the escort groups (ids need count 1) and made the plane `y` values positive (3.0, 1.5, 2.0). Plane `y` is ignored at runtime.
- Zeppelin entry, HQ, objectives, terrain, length and background untouched.

## Validation
`python tools/validate_missions.py`: mission_05 OK (one warning: duration 350 s vs the 270 s target, length not mine to change this task). Missions 1-4 FAIL on the same two rules (negative plane y, ids with count > 1; M3 also has 8 bomb targets) — pre-existing, outside this task's scope, not touched.

## Escort lookup check
Zeppelin object name is "zeppelin [zeppelin_01]" (ZeppelinAI.cs line 150); FighterAI.Start matches by case-insensitive substring "zeppelin_01" against TeamTag objects, so it resolves.

REQUESTS: mechanics: the "escort" pattern lerps x and y toward the zeppelin at 0.3*speed*dt with no offset, so escorts will converge onto the zeppelin's exact position and stack there (bullets/hits may be ambiguous, no formation). Consider a per-fighter offset (for example index-based x/y offset around the target) in FighterAI.
REQUESTS: mechanics: Escort fighters that spawn after the zeppelin is destroyed fall back to straight flight, which is fine. MissionRunner.SpawnEntities has no "zeppelin" case in the version I read (falls to the unknown-type warning); confirm the parallel mechanics task adds it.
REQUESTS: orchestrator: Validator rule "y in [0, 8.5]" and "id requires count 1" fail every mission; decide whether to relax the validator or have me fix M1-4.
