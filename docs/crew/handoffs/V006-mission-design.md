# V006 mission-design handoff

STATUS: DONE. `python tools/validate_missions.py 4 5` passes (OK, no warnings).

Files changed:
- D:\Developer\MAIGD\Biplanes\Assets\Biplanes\Resources\Missions\mission_04.json
- D:\Developer\MAIGD\Biplanes\Assets\Biplanes\Resources\Missions\mission_05.json
- D:\Developer\MAIGD\docs\missions\mission_04.md (new), mission_05.md (updated)

What changed
- M4: 9 fighters replaced by 48 singles, one every 11 x from x 66 to 583 (patterns cycle straight, sine, dive). Added a health balloon at 380 (2 total). Start y -4.0 to 3.0. The negative-y fighter spawns (-2, -3) are gone (V001).
- M5: 15 fighters replaced by 62 in pairs (spacing 6), one pair every 11 x from 66 to 396, plus the unchanged 5 escorts. Added a health balloon at 255 (3 total).
- Unchanged: objectives, titles, briefings, terrain, ground units, HQ x 360, failAtX 360, zeppelin airship_01 at x 420 y 4.9, escorts at 424 and 429.

Gap math (full derivation in the sheets): a fighter closes on the screen at 3 + scroll u/s (M4 6, M5 5); the screen is 17.8 wide, so it is visible 3.0 s (M4) and 3.6 s (M5); entry is delayed 1.5 + random 0..6 x. Camera travel in 2 s: M4 6 units, M5 4 units.
- M4 max designed gap: 1.7 s (worst case on every roll; usually zero).
- M5 max designed gap: 1.9 s. Live fighters at most 5 (M4 at most 3), under the cap of 6. No extra fighters during the zeppelin fight.

Notes for the orchestrator
- The fighter count is about 5x (M4) and 4x (M5), not 2x. The 2 s rule needs a plane about every 4 to 5 s, and singles in M4 are the lowest count that meets it. To cut the count, raise the step (12 or 13) and accept a 2.0 to 2.4 s worst case, and let EnemyPressure fill it.
- The "no other threat within +-10 x of the first AAA" pacing rule can't hold with continuous fighters; this follows from the 2 s rule.
- Killing fighters early still leaves clear sky; that is what EnemyPressure covers.
- This estimate is from the code (spawn stagger, speeds, 16:9). It has not been playtested.

REQUESTS: none
