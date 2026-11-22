# V008 mission-design handoff

STATUS: DONE. `python tools/validate_missions.py` passes for all 5 missions.

Files changed: Biplanes/Assets/Biplanes/Resources/Missions/mission_04.json, mission_05.json; docs/missions/mission_04.md, mission_05.md.

Changes (QA T958 method: budget = 100% + 30% per balloon; expected damage at dodging p=0.25)

| Mission | Before | After |
|---|---|---|
| M4 | 2 balloons (230, 380), budget 160% vs 168% damage: over | 4 balloons (+140, +320), budget 220% vs 168%: headroom 52 points (31%) |
| M5 | 3 balloons (200, 255, 320), budget 190% vs 271%: over | 2 AAA removed (385 group, -36%) and 3 balloons added (+150, +290, +385): 6 balloons, budget 280% vs 235%, headroom 45 points (19%) |

- No two balloons share x and y. Each new balloon sits in a lull after a hard beat (M4: after the AAA at 100 and 280; M5: after the AAA at 120 and the 235 peak, and one right before the zeppelin appears at camera about 409).
- All fighters (M4 48, M5 62 plus 5 escorts), objectives, titles, briefings, terrain, HQ x 360, failAtX 360 and zeppelin (atX 420, y 4.9) are unchanged. M5 keeps 5 AAA (120 and 235).
- Dueling (p=0.4) stays tight: M4 about 215% vs 220%, M5 about 300% vs 280%. Intended for a harder game. These are paper estimates, not playtests.

REQUESTS: none
