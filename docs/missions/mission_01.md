# Mission 1: Destroy Incoming Airplanes

GDD goal: destroy every enemy fighter before they reach the airfield. Start on the runway, take off, fight.
Win: `destroyAllOfType fighter` (37 fighters; the count is computed from the spawns). Lose: plane destroyed, or the camera reaches the map end (x 300) with fighters left.
Duration: 300 / 2.6 = 115 s (V014: scrollSpeed 2.0 -> 2.6, +30%; was 150 s).

## Screen time and spacing (V005, GDD "Enemy pressure")
- Camera speed = scrollSpeed = 2 u/s, so the camera travels **4 units in 2 s**. A fighter flies left at 3 u/s, so it closes on the screen at 5 u/s.
- The screen is 17.8 units wide (orthographic size 5, 16:9) and a fighter is 1.5 long, so an undamaged fighter crosses the whole screen in 19.3 / 5 = **3.9 s**.
- A wave triggers when the camera reaches `atX`; plane k enters 1.5 + k x max(spacing, 2) + random(0..6) units past the right edge (the first one 0.3 to 1.5 s after the trigger).
- Cadence rule used: the next wave triggers early enough that, even with the worst stagger, its first plane arrives within the 2 s design gap (plus 1 s slack) of the earlier wave's earliest-leaving plane. Wave triggers are 24 to 19 units apart (12 to 9.5 s).
- Simulated 4000 uncontested runs (nobody shoots anything) from takeoff-time 15 s to the last wave: **median longest empty-screen gap 1.65 s, 99th percentile 2.6 s, worst draw 2.9 s.** Design max gap: 2 s typical, under 3 s in the worst random draw. Kills open gaps that atX cannot prevent: the Enemy pressure reinforcement (2 s rule) covers those.
- Max alive at once (uncontested): 4, so the 6-fighter cap never bites.
- The first wave is at x 30 (15 s) to keep the 10% intro; the pressure net does not run while the plane is on the runway.

| x range | What happens | Skill |
|---|---|---|
| 0-25 | Intro: runway, take off, no threats | Takeoff |
| 30-78 | 3 waves of 3 straight fighters, trickling in (spacing 16) | Aim, 3 hits per kill |
| 102-147 | 3 waves of 3 sine fighters (spacing 14); AAA at 130 | Tracking weaving targets |
| 42 | Health balloon (V012, lull after the first straight wave) | Recover |
| 88 | Health balloon (V012, lull after the third straight wave, before the sine waves) | Recover |
| 110 | Health balloon (V009, after the first sine waves) | Recover |
| 170 | Health balloon (breather) | Recover |
| 225 | Health balloon (V009, between the dive waves and the peak) | Recover |
| 280 | Health balloon (V009, after the peak) | Recover |
| 170-212 | Sine, then 2 waves of 3 dive fighters | Dodge dives |
| 231-252 | 2 peak waves of 5 (dive, then sine), spacing 6 | Crowd control; the last wave must be cleared before x 300 |
| 250 | 2 AAA (spacing 3) on the ground | Optional ground harassment |

Objective climax: kill the last fighter of the final wave (x 252 trigger, about 48 units of camera left).
Pacing note: the AAA at 130 and 250 stay as optional decoration. With a continuous fighter stream the "no threat within 10 x of a first AAA" rule cannot hold in M1, and AAA first becomes a bomb target in M3.

Difficulty knobs:
- Wave `count` and `spacing` (lower spacing means clusters and more alive at once; raising it thins the stream and widens gaps).
- Trigger `atX` cadence (the 24 to 19 unit steps set the longest empty gap).
- Patterns per segment (straight, then sine, then dive).
- Health balloons at 42, 88, 110, 170, 225 and 280 (remove for hard mode).
- Last wave atX (252; leave at least 20 s before the map end).

Heal budget (V012): balloons 4 -> 6 (added 42 and 88, both in the first half, x < 150; gaps 42, 46, 22, 60, 55, 55). Budget 100% + 30% x 4 = 220% before, 100% + 30% x 6 = 280% after.

Heal budget (V009): balloons 1 -> 4 (110, 170, 225, 280; gaps 60, 55, 55). Budget 100% + 30% x 1 = 130% before, 100% + 30% x 4 = 220% after.

## Speed +30% re-check (V014)
Player forward speed = scrollSpeed, raised 30%. Fighters still fly left at 3 u/s, so the closing speed on the screen is 3 + scroll. Numbers are from 3000 uncontested Monte Carlo runs of the spawn model (cumulative stagger 1.5 + 0..6 x per plane, min gap 2 x, 17.8 + 1.5 screen), nobody shooting.
- scrollSpeed 2.0 -> 2.6. Closing speed 5.60 u/s, so an undamaged fighter stays on screen 19.3 / 5.60 = **3.45 s** (was 3.86 s).
- Camera travel in 2 s: **5.2 units** (was 4.0).
- Max empty-sky gap (uncontested): was median 1.65 s, 99th 2.6 s, worst 3.0 s; now **median 0.40 s, 99th 0.93 s, worst 1.24 s**. Waves trigger sooner in time (atX / scroll) while a plane's entry delay shrinks less (closing speed only +12 to +18%), so waves overlap more and the gaps close up instead of opening. Max alive at once: median 3, at most 4, under the cap of 6.
- No spacing change needed (all 37 fighters kept). Side effect: more fighters per second (waves arrive 23% sooner), so the mission is a little harder per minute and 35 s shorter. Last wave (x 252) triggers at 97 s, leaving 18 s of map; the final wave is on screen about 3.5 s plus its stagger, so there is still time to clear it. Health balloons and objective unchanged.

- **Bomb recharge (V064):** one static `bombBalloon` at x 150 (y 3.5), mid-map, clear of ground units and other balloons. It refills 4 bombs (cap 7); it is not an enemy and counts toward no objective or health-balloon rule.
