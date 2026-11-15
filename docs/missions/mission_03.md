# Mission 3: Destroy Enemy Defenses

GDD goal: destroy the enemy AAA with bombs. Win: all 5 AAA destroyed (`destroyAllOfType aaa`). Lose: plane destroyed, or the camera reaches the map end with AAA left.
Duration: 500 / 3.0 = about 167 s (target 210 s; length change would be a separate call).

Bomb budget: 5 bomb-only AAA against 7 bombs, no refill, so 2 spare bombs for misses.

| x range | What happens | Skill |
|---|---|---|
| 0-70 | Intro, rising terrain, no threats | Warm up |
| 80 | First AAA, alone (no threat within 10 x) | Learn to bomb a gun |
| 95 | 3 straight fighters | Dogfight under flak |
| 160 | 2 AAA, spacing 4 | Bomb run, pick a line |
| 200 | 2 sine fighters | Tracking |
| 245 | 2 dive fighters | Dodge dives |
| 280 | Health balloon (breather) | Recover |
| 320 | 1 AAA on high terrain | Bomb on a slope |
| 360 | 4 sine fighters (peak) | Crowd control |
| 400 | 3 dive fighters | Pressure before the finale |
| 420 | Last AAA, objective climax | Precise final bomb |

Difficulty knobs:
- Fighter counts and patterns at 95, 200, 245, 360, 400.
- AAA spacing at x 160; terrain height under the guns (higher is closer to the player).
- Health balloon at 280 (remove for hard mode).
- Do not add more AAA: required count must stay at 5 or less.
