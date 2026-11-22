# V009 mission-design handoff

DONE. Added 3 health balloons each to M1, M2, M3 (drift, count 1). No other change; M4/M5 untouched.

| Mission | Balloons before | Balloons after (atX, y) | Heal budget before -> after |
|---|---|---|---|
| M1 | 170 | 110 (2.5), 170, 225 (2.0), 280 (2.5) | 130% -> 220% |
| M2 | 65, 135 | 65, 135, 195 (2.5), 250 (3.0), 320 (2.5) | 160% -> 250% |
| M3 | 180, 280, 380 | 180, 230 (2.5), 280, 330 (2.5), 380, 450 (2.5) | 190% -> 280% |

Budget = 100% + 30% (healthBalloonRestore) per balloon; Balance.json read only. Gaps are 55-70 units in M1/M2 and 50-70 in M3 (M2 195-250-320 uses 55/70; 135-195 is 60). Balloons sit in lulls after fighter groups; none share x with an AAA (M1 130/250, M3 80/160/320/420) and none share x and y with another balloon, so AllMissions_HealthBalloons_NeverStackAtTheSameSpot holds. y values are heights above terrain (2.0-3.0), above the 2.0 clearance rule.
Sheets updated: docs/missions/mission_01.md, mission_02.md, mission_03.md.
Validation: `python tools/validate_missions.py` OK for all 5.
REQUESTS: none
