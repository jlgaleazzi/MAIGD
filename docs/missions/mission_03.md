# Mission 3: Destroy Enemy Defenses

GDD goal: destroy the enemy AAA with bombs. Win: all 5 AAA destroyed (`destroyAllOfType aaa`). Lose: plane destroyed, or the camera reaches the map end with AAA left.
Duration: 500 / 3.9 = about 128 s (V014: scrollSpeed 3.0 -> 3.9, +30%; was 167 s; target 210 s, a length change would be a separate call). Fighters: 46.

Bomb budget: 5 bomb-only AAA against 7 bombs, no refill, so 2 spare bombs for misses.

## Screen time and spacing (V005, GDD "Enemy pressure")
- Camera 3 u/s: **6 units in 2 s**. Fighter closing speed 6 u/s, screen 17.8 + 1.5 sprite, so a fighter is on screen about **3.2 s**.
- Waves trigger every 21 to 29 units (7 to 9.7 s); wave size and spacing ramp from 2 fighters at spacing 16 to 4 at spacing 5.
- Simulated 4000 uncontested runs from the first wave (16.7 s) to the last wave (131 s): **median longest gap about 2.1 s, 99th percentile 2.8 s, worst draw 3.0 s.** Design max gap: 2 s typical, about 3 s in the worst random draw. This mission has the fastest scroll, so each fighter lives shortest; the Enemy pressure net covers the remaining gaps.
- Max alive at once (uncontested): 4.
- Pacing exception: the first AAA (x 80) is meant to be alone, but a continuous stream cannot leave 20 units empty. Around it the stream is only 2-fighter straight waves at spacing 16, the calmest part of the mission.

| x range | What happens | Skill |
|---|---|---|
| 0-45 | Intro, rising terrain, no threats | Warm up |
| 50, 74, 97 | 2 straight fighters each, spacing 16 | Dogfight on rising terrain |
| 80 | First AAA, alone on the ground | Learn to bomb a gun |
| 121 | 3 straight fighters | Dogfight under flak |
| 149-202 | 3 sine, 3 sine, 3 dive; 2 AAA at 160 (spacing 4) | Bomb run while tracking |
| 60 | Health balloon (V012, after the first straight wave, 20 x before the first AAA at 80) | Recover |
| 120 | Health balloon (V012, between the AAA at 80 and 160, ahead of the 3-fighter waves) | Recover |
| 180 | Health balloon | Recover |
| 230 | Health balloon (V009, mid-height 2.5 above the rising terrain) | Recover |
| 228-275 | 3 sine, 3 dive, 3 sine; health balloon at 280 | Dodge dives, recover |
| 296-369 | 4 dive, 4 sine, 4 dive, 4 sine at spacing 5 to 6 (peak); AAA at 320 on high terrain | Crowd control, bomb on a slope |
| 330 | Health balloon (V009, 10 units past the AAA at 320, clear of it in height) | Recover |
| 380 | Health balloon (before the finale) | Recover |
| 450 | Health balloon (V009, after the AAA at 420) | Recover |
| 392 | 3 dive fighters | Pressure before the finale |
| 420 | Last AAA, objective climax | Precise final bomb |

Difficulty knobs:
- Wave counts, spacing and cadence at 50-392 (each denser step raises the alive count; stay at 5 or less).
- AAA spacing at x 160; terrain height under the guns (higher is closer to the player).
- Health balloons at 60, 120, 180, 230, 280, 330, 380 and 450 (remove for hard mode).
- Do not add more AAA: required count must stay at 5 or less.

Heal budget (V012): balloons 6 -> 8 (added 60 and 120, both in the first half, x < 250). Budget 100% + 30% x 6 = 280% before, 100% + 30% x 8 = 340% after.

Heal budget (V009): balloons 3 -> 6 (180, 230, 280, 330, 380, 450; gaps 50, 50, 50, 50, 70). Budget 100% + 30% x 3 = 190% before, 100% + 30% x 6 = 280% after.

## Speed +30% re-check (V014)
Player forward speed = scrollSpeed, raised 30%. Fighters still fly left at 3 u/s, so the closing speed on the screen is 3 + scroll. Numbers are from 3000 uncontested Monte Carlo runs of the spawn model (cumulative stagger 1.5 + 0..6 x per plane, min gap 2 x, 17.8 + 1.5 screen), nobody shooting.
- scrollSpeed 3.0 -> 3.9. Closing speed 6.90 u/s, so an undamaged fighter stays on screen 19.3 / 6.90 = **2.80 s** (was 3.22 s).
- Camera travel in 2 s: **7.8 units** (was 6.0).
- Max empty-sky gap (uncontested): was median 2.1 s, 99th 2.8 s, worst 3.0 s; now **median 1.02 s, 99th 1.66 s, worst 1.88 s**. Waves trigger sooner in time (atX / scroll) while a plane's entry delay shrinks less (closing speed only +12 to +18%), so waves overlap more and the gaps close up instead of opening. Max alive at once: median 3, at most 4, under the cap of 6.
- No spacing change needed: the old mission was over 2 s and is now under it. Side effects: the AAA scroll past in 17.8 / 3.9 = 4.6 s (was 5.9 s) and a level bomb now throws about 3.3 u ahead (was 2.5 u), so the first bomb at x 80 still has a calm sky but a shorter aiming window; no AAA position was changed. 5 AAA / 7 bombs unchanged.
