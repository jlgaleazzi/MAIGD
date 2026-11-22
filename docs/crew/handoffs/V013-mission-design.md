# V013 mission-design handoff

DONE. Added 2 health balloons each to missions 4 and 5 in the first half.

| Mission | Before (all / first half) | After (all / first half) | New balloons |
|---|---|---|---|
| M4 (length 600, half 300) | 7 / 3 (80, 140, 230) | 9 / 5 | atX 58 y 2.5; atX 205 y 2.5 |
| M5 (length 540, half 270) | 8 / 4 (80, 150, 200, 255) | 10 / 6 | atX 60 y 2.0; atX 98 y 2.0 |

Placement: M4 58 is 13 past the barrage pair at 40-45 and in the intro before the first fighter (66); 205 is 19 past the barrage pair at 180-186 and 25 before the balloon at 230. M5 60 is at the end of the intro (before the first pair at 66); 98 is 22 from the AAA at 120. No two balloons share x or y, heights are 2.0-2.5 (inside 1.5-3.0), spawns sorted. M5 HQ 360, failAtX 360, airship_01 atX 420 y 4.9 and escorts unchanged.

Files: Resources/Missions/mission_04.json, mission_05.json, docs/missions/mission_04.md, mission_05.md (balloon lists, budget lines).

Validation: `python tools/validate_missions.py` gives OK for mission_04 and mission_05 (and mission_01). mission_02 and mission_03 currently FAIL with "spawns must be sorted by atX": that is V012's in-progress work, not touched here.

REQUESTS: none
