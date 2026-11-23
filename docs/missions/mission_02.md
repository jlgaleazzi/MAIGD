# Mission 2: Destroy the Enemy Bomber

GDD goal: destroy the bomber with its fighter escort before it reaches the airfield.
Win: `destroyById bomber_1`. Lose: plane destroyed, the bomber reaches `failAtX` 10 (flying left, the airfield), or the camera reaches the map end with the bomber alive.
Duration: 450 / 3.25 = 138 s (V014: scrollSpeed 2.5 -> 3.25, +30%; was 180 s). Fighters: 29 (3 are the bomber's escorts).

Geometry: the bomber is triggered when the camera reaches x 80 and appears 10.4 to 16.4 right of the camera, so it spawns at about x 90 to 96 (always greater than failAtX 10). It flies left at 1.5 u/s, so it needs about 53 to 57 s to reach x 10. The camera closes at 4 u/s on the first pass, so contact comes in about 3 s. A turn-around then gives a 1 u/s catch-up. The bomb run starts at x 15.

## Screen time and spacing (V005, GDD "Enemy pressure")
- Camera 2.5 u/s: **5 units in 2 s**. Fighter closing speed 5.5 u/s, screen 17.8 + 1.5 sprite, so a fighter is on screen about **3.5 s**. The bomber group (1.5 u/s, closing 4 u/s) is on screen about 4.8 s.
- Waves at spacing 8 to 10 (planes 1.4 to 2.9 s apart on average) trigger every 19 to 21 units (7.6 to 8.4 s).
- Simulated 4000 uncontested runs from the first wave (20 s) to the last dense wave at x 170 (68 s): **median longest gap about 1.1 s, 99th percentile about 1.9 s, worst draw about 2.4 s.** Design max gap 2 s, covering the time a player needs to find and kill the bomber (earliest contact about 35 s).
- After x 170 the waves thin to x 210 and x 280 (a gap of about 6 s between them, by design: the stream only has to cover the bomber hunt). While the player turns back to chase the bomber, `FarthestX` does not advance and no new wave triggers, so the Enemy pressure net covers that.
- Max alive at once: 5 (escorts count).

| x range | What happens | Skill |
|---|---|---|
| 0-45 | Intro, no threats | Warm up |
| 50, 61, 70 | 2, 2 and 1 straight fighters | Dogfight basics, 3 hits per kill |
| 30 | Health balloon (V012, in the intro lull before the first fighters) | Recover |
| 65 | Health balloon (breather) | Recover |
| 80 | Bomber enters with 2 escort fighters (escort pattern) | Read the formation |
| 85 | Third escort fighter | Clear the escort, then the bomber |
| 93-170 | 5 waves of 3 (sine, straight, dive, sine, dive), spacing 8 | Keep the bomber in sight while tracking |
| 100 | Health balloon (V012, after the escort/bomber fight, before the dive group at 131) | Recover |
| 135 | Second health balloon | Recover mid-chase |
| 195, 250, 320 | Health balloons (V009), in the lulls after the sine/dive groups at 150-170, 210 and before 280 | Recover |
| 210, 280 | 3 sine, then 3 dive (thin stream, harassment if the bomber is alive) | Tracking while chasing |

The objective climax is the bomber kill itself (1000 hp = 40 bullets or 10 bombs, gunner fire; it smokes as it is damaged, then explodes and falls); the mission ends on the kill.
AAA stays out: it is introduced in mission 3.

Difficulty knobs:
- failAtX (lower means more time to catch the bomber; must stay below the spawn x of about 90).
- Bomber atX (later means a longer intro; the escort atX values should track it).
- Escort count and pattern at 80 and 85 (2 to 4 allowed).
- Wave count and spacing at 93-170 (denser means more alive at once).
- Health balloons at 30, 65, 100, 135, 195, 250 and 320.
- Tail waves at 210 and 280.

Heal budget (V012): balloons 5 -> 7 (added 30 and 100, both in the first half, x < 225). Budget 100% + 30% x 5 = 250% before, 100% + 30% x 7 = 310% after.

Heal budget (V009): balloons 2 -> 5 (65, 135, 195, 250, 320). Budget 100% + 30% x 2 = 160% before, 100% + 30% x 5 = 250% after.

## Speed +30% re-check (V014)
Player forward speed = scrollSpeed, raised 30%. Fighters still fly left at 3 u/s, so the closing speed on the screen is 3 + scroll. Numbers are from 3000 uncontested Monte Carlo runs of the spawn model (cumulative stagger 1.5 + 0..6 x per plane, min gap 2 x, 17.8 + 1.5 screen), nobody shooting.
- scrollSpeed 2.5 -> 3.25. Closing speed 6.25 u/s, so an undamaged fighter stays on screen 19.3 / 6.25 = **3.09 s** (was 3.51 s).
- Camera travel in 2 s: **6.5 units** (was 5.0).
- Max empty-sky gap (uncontested; dense block x 93-170): was 93-170 dense block: median 0.89 s, 99th 1.9 s, worst 2.3 s; now **median 0.0 s, 99th 0.85 s, worst 1.13 s**. Waves trigger sooner in time (atX / scroll) while a plane's entry delay shrinks less (closing speed only +12 to +18%), so waves overlap more and the gaps close up instead of opening. Max alive at once: median 3, at most 3 (excluding escorts), under the cap of 6.
- No spacing change needed. Sparse tail (210 and 280) is a 12 s and 21 s lull by design, as before (it only covers the bomber hunt; EnemyPressure covers the rest).

Bomber catchability (bomber_1, targetX = failAtX 10): the bomber triggers when the camera reaches x 80 (24.6 s into the mission) and appears 10.4 to 16.4 right of the camera at 16:9, so it spawns at about x 90 to 96. It flies left at 1.5 u/s and needs (90 to 96 - 10) / 1.5 = **53 to 57 s** to reach failAtX (unchanged). The camera closes on it at 3.25 + 1.5 = 4.75 u/s, so it is level with the plane in about 2.2 to 3.5 s (was 2.6 to 4.1 s), leaving **about 50 s** to turn back and kill it. After the turn-around the plane gains 3.25 - 1.5 = 1.75 u/s on the bomber (was 1.0), so catching up is easier. Map end: the camera needs (450 - 80) / 3.25 = 114 s after the trigger, far later than the 53 to 57 s fail time, so failAtX always comes first. No change to length, atX or failAtX.

- **Bomb recharge (V064):** one static `bombBalloon` at x 225 (y 3.5), mid-map, clear of ground units and other balloons. It refills 4 bombs (cap 7); it is not an enemy and counts toward no objective or health-balloon rule.
