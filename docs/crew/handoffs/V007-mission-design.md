# V007 mission-design handoff
STATUS: DONE
Fixed T957 BUG 9 (duplicate health balloons).
- mission_02.json: removed the second healthBalloon at atX 135 (y 2.5). Accidental copy: the sheet lists 2 balloons (65, 135) and 135 was doubled. Budget stays 2.
- mission_03.json: removed the second copies at atX 180 and atX 380 (y 1.5). Accidental copies: the sheet lists 3 balloons (180, 280, 380). Budget stays 3.
- Missions 1, 4 and 5 checked: no duplicates, unchanged.
- Fighter waves, objectives, titles and briefings are untouched. Sheets (mission_02.md, mission_03.md) already described the single balloons, so no edit.
- validate_missions.py: all 5 OK.
REQUESTS: none
