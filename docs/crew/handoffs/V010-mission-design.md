# V010 mission-design handoff

DONE. Added health balloons (drift, count 1) in lulls at flight-band heights, none on an AAA/obstacle x, no duplicate x/y.
- M4: +80 (y 2.5), +445 (y 2.5), +532 (y 2.0) -> 7 balloons (80, 140, 230, 320, 380, 445, 532). Budget 310% vs expected 168% (was 220%).
- M5: +80 (y 2.5), +470 (y 2.5, zeppelin stretch) -> 8 balloons (80, 150, 200, 255, 290, 320, 385, 470). Budget 340% vs expected 235% (was 280%).
- M5 otherwise unchanged (HQ 360, failAtX 360, airship_01 atX 420 y 4.9, escorts). Fighters, objectives, titles, briefings unchanged.
- Files: Resources/Missions/mission_04.json, mission_05.json, docs/missions/mission_04.md, mission_05.md.
- `python tools/validate_missions.py`: all 5 OK. Heal-budget test should still pass (budget only rises). Not run by me (Unity tests).
REQUESTS: none
