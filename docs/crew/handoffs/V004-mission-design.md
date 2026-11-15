# V004 mission-design handoff

DONE: mission 5 fixes for QA T938 P1-1, P2-2, P2-4. `python tools/validate_missions.py`: mission_05 OK (M1 and M4 fail on y > 8.5 from the validator's new rule, not touched by me; M2 and M3 OK).

Changes in `Assets/Biplanes/Resources/Missions/mission_05.json`:
- Zeppelin y 0.9 -> 4.9 (height above terrain, so the underside turret is shootable).
- Zeppelin id `zep` -> `airship_01` (not a substring of `zeppelinTurret`). Updated escortOf (both groups) and objective targetId.
- Zeppelin atX 430 -> 420; escorts 420/425 -> 424/429 (zeppelin spawns first; spawns still sorted). failAtX 360 < 420.
- Length stays 540 (270 s).

Arithmetic: zeppelin needs (420-360)/1.0 = 60 s to reach the HQ. Camera at zeppelin spawn is 420-10.9 = 409.1 (16:9 widest). Flying right to 540 takes (540-409.1)/2 = 65.5 s > 60 s, so the HQ loss comes first. Shortening length to 470 (QA idea) would make the map end come sooner and worsen P2-4; instead I moved the zeppelin 10 closer. Aspect spread of the trigger is 2.2, escorts are 4+ later, so the hull exists first.

Sheet updated: `docs/missions/mission_05.md`.

REQUESTS: orchestrator: GDD change log line for M5 should read zeppelin y 4.9 (height above terrain), id airship_01, atX 420. Update any tests that reference id "zep" (M5 name lookup test, objective tests) to "airship_01".
