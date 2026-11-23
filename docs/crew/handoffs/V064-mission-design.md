# V064 mission-design handoff
STATUS: DONE
- Added one `bombBalloon` spawn (count 1, drift, id "", y 3.5) to each mission: M1 x150/300, M2 x225/450, M3 x250/500, M4 x300/600, M5 x272/580. All sit in the middle third, with no ground unit within 20 x. M5 was nudged from 290 to 272 to avoid the healthBalloon at 290.
- `tools/validate_missions.py`: `bombBalloon` is in TYPES (non-ground). It is not in BOMB_TARGETS, so it is not an enemy or bombable target. It is not a healthBalloon, so it is outside the health rules. New error if a mission has no bombBalloon within [length/3, 2*length/3].
- Sheets `docs/missions/mission_01..05.md` each got one bullet about the balloon.
- Validator: all 5 OK. Existing duration warnings for M3 (128s vs 210s) and M4 (154s vs 240s) predate this task.
REQUESTS: none
