# Mission 4: Destroy the Enemy Base

GDD goal: pop the barrage balloons guarding the enemy territory and bomb the enemy base.
Win: `destroyAllOfType barrageBalloon` and `bombTarget enemy_base_01`. Lose: plane destroyed (no failAtX).
Duration: 600 / 3.9 = 154 s (V014: scrollSpeed 3.0 -> 3.9, +30%; was 200 s). Theme `enemyland`. Bombs: 1 target for the base (+ 5 AAA available, 7 bombs, no refills).
The airborne start is now y 3.0 (it was -4.0, which the runner raised to 1.5 with a warning).

| x range | What happens | Skill |
|---|---|---|
| 0-60 | Intro, no threats (first balloons enter at about 40) | Warm up |
| 40 | 2 barrage balloons | Spot and shoot balloons |
| 66-583 | A single fighter every 11 x, patterns cycle straight, sine, dive, heights random (48 fighters) | Constant dogfight |
| 100 | 3 AAA, spacing 4 | Flak and fighters together |
| 58 | Health balloon (V013, y 2.5) | Early top up in the intro, before the first fighter at 66 |
| 80 | Health balloon (V010, y 2.5) | Top up early, before the first fighters stack up |
| 140 | Health balloon (V008) | Recover after the first flak |
| 180 | 2 balloons, spacing 6 | Pop under fire |
| 205 | Health balloon (V013, y 2.5) | Recover after the balloon pair at 180 (clear of it by 19) |
| 230 | Health balloon | Recover |
| 280 | 2 AAA, spacing 3 | Flak lanes |
| 320 | Health balloon (V008) | Recover after the flak lanes |
| 380 | Health balloon | Recover before the base |
| 400 | 2 balloons, spacing 5 | Last balloons |
| 445 | Health balloon (V010, y 2.5) | Recover before the base at 460 |
| 532 | Health balloon (V010, y 2.0) | Final top up on the closing run |
| 460 | Enemy base `enemy_base_01` (objective climax) | Bomb run through fighters |

Enemy pressure (V006): fighters fly left at 3 u/s and the camera scrolls right at 3 u/s, so a fighter closes on the screen at 6 u/s. The screen is 17.8 wide (16:9), so an undisturbed fighter is visible for 17.8 / 6 = 3.0 s. It appears (1.5 + random 0..6) / 6 = 0.25 to 1.25 s after its trigger. Fighters trigger every 11 x = 3.7 s of camera travel, so the worst case gap is 3.7 + 1.25 - (0.25 + 3.0) = 1.7 s (the common case is zero: the next fighter is already in view). Camera travel in 2 s = 6 units. Designed max gap: 1.7 s (about 5 units). Live fighters: 1 to 3, well under the cap of 6, because fighters take 3 hits and shoot back. The first AAA at 100 no longer has a clear +-10 x; the constant single fighters are the trade for the 2 s rule, and singles are easy to ignore while bombing. When the player shoots fighters down faster than they arrive, the EnemyPressure net adds reinforcements. After the base and balloons are done, play ends.

Fighter count: 48 singles (was 9). That is far above 2x because the 2 s rule needs a plane every 4 s; singles keep the count as low as the rule allows and keep M4 below M5 (0.27 vs 0.36 fighters per second).

Balance (V008, QA T958 method: 100% start + 30% per balloon vs expected damage, dodging p=0.25):
- Before: 2 balloons, budget 160%, expected 168%. Over budget.
- After: 4 balloons (140, 230, 320, 380), budget 220%, expected 168%. Headroom 52 points (31%). Dueling (215%) is about break-even. All 48 fighters and 5 AAA are unchanged. Estimate only, not a playtest.
- V010: added 3 balloons (80, 445, 532) -> 7 balloons, budget 310% vs expected 168%. Headroom 142 points; dueling (215%) is now comfortably survivable.

- V013: added 2 balloons (58, 205), both in the first half (x < 300) -> 9 balloons (was 7; first half 3 -> 5), budget 370% vs expected 168%. Nothing else changed.

Difficulty knobs:
- Fighter step (11 x; 13 would give 2.4 s worst gap) and count per wave (2 doubles the density).
- AAA counts at 100 and 280; balloon counts and heights.
- Health balloons at 58, 80, 140, 205, 230, 320, 380, 445 and 532.
- Terrain under the base (rises to 2.5 at x 300, falls to 1.0 at 500).

## Speed +30% re-check (V014)
Player forward speed = scrollSpeed, raised 30%. Fighters still fly left at 3 u/s, so the closing speed on the screen is 3 + scroll. Numbers are from 3000 uncontested Monte Carlo runs of the spawn model (cumulative stagger 1.5 + 0..6 x per plane, min gap 2 x, 17.8 + 1.5 screen), nobody shooting.
- scrollSpeed 3.0 -> 3.9. Closing speed 6.90 u/s, so an undamaged fighter stays on screen 19.3 / 6.90 = **2.80 s** (was 3.22 s).
- Camera travel in 2 s: **7.8 units** (was 6.0).
- Max empty-sky gap (uncontested): was worst 1.7 s; now **median 0.74 s, 99th 0.87 s, worst 0.89 s**. Waves trigger sooner in time (atX / scroll) while a plane's entry delay shrinks less (closing speed only +12 to +18%), so waves overlap more and the gaps close up instead of opening. Max alive at once: median 2, at most 2, under the cap of 6.
- No spacing change needed (all 48 singles kept, step 11 x). Side effects: one fighter every 11 / 3.9 = 2.8 s (was 3.7 s), and the ground units (balloons, AAA, base) scroll past 30% faster (the base is on screen 4.6 s, was 5.9 s). Objectives and balloons unchanged.
