# V002-mission-design

STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Resources/Missions/mission_03.json, docs/missions/mission_03.md

SUMMARY
- M3 AAA cut from 8 to 5 (x80:1, x160:2, x320:1, x420:1). 7 bombs leave 2 spare.
- Difficulty kept with fighters: added dive groups at x245 (2) and x400 (3).
- Fighter y made positive (ignored at runtime), so validate_missions passes M3 (the M3 part of V001).
- Duration is 167 s vs 210 s target (not changed; length left alone).

BOMB BUDGET CHECK
- M4: only 1 mandatory bomb (enemy_base_01). Balloons and AAA are not bomb-required, so it is feasible (its P1-1 end-state problem is a mechanics issue, not budget).
- M5: the zeppelin hull dies to 40 bullets (or 10 bombs), no mandatory bombs, so it is feasible. Not edited. mission_05 and failAtX untouched.

VALIDATOR: mission_03 OK, mission_05 OK (duration warning only). M1, M2 and M4 still fail the V001 rules (negative plane y; M2 id on a group with count > 1).

REQUESTS:
- mission-design (V001): M1, M2, M4 still need y/id fixes, or relax the rules in tools/validate_missions.py.
- orchestrator: M3 duration 167 s vs 210 s target; decide whether to lengthen it.
