TASK: V005
AGENT: mission-design
STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Resources/Missions/mission_01.json, mission_02.json, mission_03.json; docs/missions/mission_01.md (new), mission_02.md, mission_03.md
SUMMARY: Fighters M1 12->37, M2 8->29 (3 escorts), M3 14->46. Waves are cadenced so uncontested screen-empty gaps are about 2 s typical (p99 2.6 / 1.9 / 2.8 s, worst draw 2.9 / 2.4 / 3.0 s); max alive 4-5. Fixed the negative fighter y values (V001). Added health balloons: M2 at 135, M3 at 180 and 380.
VERIFY: python tools/validate_missions.py 1 2 3 (all OK). Playtest: fighters take 3 hits, fewer than 6 alive; the EnemyPressure net should rarely fire before the last wave.
REQUESTS: none
NOTES:
- Numbers (also in each sheet): screen 17.8 u; a fighter is on screen 3.9 s (M1), 3.5 s (M2), 3.2 s (M3); the camera travels 4 / 5 / 6 u in 2 s.
- Fighter counts exceed the roughly 2x target (3.1x, 3.6x, 3.3x): an uncontested screen needs about 1 plane per 4 s, which is the minimum for a 2 s gap.
- M2 dense stream runs only until x 170 (the bomber hunt); the tail (210, 280) is thin. Backtracking freezes FarthestX, so only the pressure net covers that.
- M3 first AAA is no longer alone (2-fighter straight waves nearby); M1 AAA at 130 and 250 kept as optional decoration.
- Objectives, titles, briefings, terrain, bomber and escort entries unchanged; M2 failAtX stays 10.
- Gap figures come from a random simulation of the placement rules (uncontested planes); real play opens gaps with kills.
