# V065 mission-design: Mission 5 shortened by 30%

STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Resources/Missions/mission_05.json, docs/missions/mission_05.md

## What changed
- length 580 -> 406 (156 s at scroll 2.6, was 223 s). All x values scaled by 0.7, rounded.
- Terrain 0/84/196/280/378. HQ and failAtX 252. Zeppelin atX 294 (inside the length, 112 before the end). Escorts 298 and 302 (kept 4 and 8 above the hull).
- AAA 84 (x2) and 165 (x3). Health balloons 42, 56, 69, 105, 140, 179, 203, 224, 270, 284 (6 in the first half, unchanged). Bomb balloon 190 (inside [135, 271]).
- Enemy counts are identical (62 pair fighters, 5 escorts, 5 AAA, 10 health balloons, 1 bomb balloon). Nothing was deleted. Pair fighters of different waves now share x in two places (69, 270) which is fine for air units; ground units are 80+ apart.
- Briefing text and objective kept. validate_missions.py passes (only the duration warning, 156 s vs 270).

## Zeppelin check
- Travel time to the HQ: 42 s (was 60 s). Map end after the trigger: 46.4 s at 4:3, 47.3 s at 16:9, so the HQ loss still comes first, margin 4.4 to 5.3 s.
- Tighter: the fight window is 30% shorter. About 5 s of fire per pass; 40 hull hits plus turrets need 2-3 passes. Winnable on paper, not playtested. Fighters arrive 30% faster per second.
- Not changed: zeppelin start x relative to the HQ (kept 42 = 60 * 0.7). Fallback if too hard: lower the zeppelin speed or move the HQ down.

REQUESTS: orchestrator: GDD Mission 5 / Enemy pressure: update M5 length 580 -> 406 and duration 223 s -> 156 s; zeppelin atX 294, HQ/failAtX 252.
REQUESTS: mechanics: none.
