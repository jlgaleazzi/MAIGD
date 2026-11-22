# V012 mission-design handoff

STATUS: DONE

Added 2 health balloons each to missions 1-3, all in the first half, in lulls between early fighter waves. Spawns stay sorted by atX. No AAA/base/HQ within 10 x of a new balloon; heights are 2.0-3.0 above terrain. Missions 4-5 and Balance.json untouched.

| Mission | Before | After | New balloons (atX, y) |
|---|---|---|---|
| M1 (length 300) | 4 (110, 170, 225, 280) | 6 | 42 (2.5), 88 (2.0) |
| M2 (length 450) | 5 (65, 135, 195, 250, 320) | 7 | 30 (2.5), 100 (3.0) |
| M3 (length 500) | 6 (180, 230, 280, 330, 380, 450) | 8 | 60 (2.5), 120 (2.0) |

Heal budget: M1 220% -> 280%, M2 250% -> 310%, M3 280% -> 340%.
Files: mission_01..03.json, docs/missions/mission_01..03.md.
Validation: `python tools/validate_missions.py` OK for all 5 missions.
REQUESTS: none
