# Mission 5: Enemy Zeppelin

GDD goal: destroy the zeppelin before it reaches the player's HQ.
Win: `destroyById airship_01`. Lose: plane destroyed, the zeppelin reaches `failAtX` 252 (the HQ x), or the camera reaches the map end with the zeppelin alive.
Duration: 406 / 2.6 = 156 s (V065: length 580 -> 406, all x scaled by 0.7, 30% shorter; V014: scrollSpeed 2.0 -> 2.6, +30%, and length 540 -> 580 to keep the zeppelin fight before the map end; was 270 s).

Id: the zeppelin id is `airship_01`, not `zep`, because FighterAI matches `escortOf` as a substring of entity names and the turrets are named `zeppelinTurret`. "airship_01" matches only the hull.

Geometry: the HQ sits at x 360 = failAtX. The zeppelin (atX 420) spawns when the camera reaches atX minus (5*aspect+2): 409.1 at 16:9, 411.3 at 4:3 worst case is 420-8.7. It flies left at 1.0 u/s, so it needs 60 s to reach the HQ. Map-end arithmetic: see the V014 section below (at scroll 2.6 the map end is later than the 60 s HQ loss only with length 580). Escorts launch at 424 and 429, at least 4 above the zeppelin atX, so the hull exists before any escort looks it up (aspect spread is 2.2). Y 4.9 above the terrain (0.5 at x 420) puts the hull mid-sky with the underside turret above the ground and shootable. First contact is about 4 s after it appears; each pass offers 7 to 15 s of fire; the kill needs 9 hits on 3 turrets plus 40 hull hits, or bombs (100 each).

| x range | What happens | Skill |
|---|---|---|
| 0-54 | Intro, no threats | Warm up |
| 66-396 | Continuous fighter pairs (spacing 6) every 11 x, patterns cycle straight, sine, dive (31 pairs = 62 fighters) | Dogfight under fire, never a quiet sky |
| 120 | 2 AAA, spacing 6 | Flak dodging while fighters keep coming |
| 60 | Health balloon (V013, y 2.0) | Early top up at the end of the intro, before the first pair at 66 |
| 80 | Health balloon (V010, y 2.5) | Top up before the first AAA at 120 |
| 98 | Health balloon (V013, y 2.0) | Recover mid-build, 22 clear of the AAA at 120 |
| 150 | Health balloon (V008) | Recover after the first AAA |
| 200 | Health balloon (breather in ground terms; the sky stays busy) | Recover |
| 235 | 3 AAA, spacing 4 (peak) | Flak lanes |
| 255 | Health balloon | Recover |
| 290 | Health balloon (V008) | Recover after the flak peak |
| 320 | Health balloon | Recover |
| 385 | Health balloon (V008, replaces the 2 AAA) | Top up right before the zeppelin appears (camera about 409) |
| 360 | Player HQ (= failAtX) | Landmark, the line to defend |
| 396 | Last fighter pair; the zeppelin (id `airship_01`, 3 turrets, y 4.9, atX 420) takes over the sky | Objective climax: turrets first, then hull |
| 405 | Health balloon (V011, y 2.5; moved from 470) | Recover as the zeppelin and its escorts arrive (zeppelin 420, escorts 424/429) |
| 424, 429 | 5 escort fighters in two waves, follow the hull | Clear the escort |

Enemy pressure (V006, numbers superseded by the V014 section: scroll is now 2.6): fighters fly left at 3 u/s and the camera scrolls right at 2 u/s, so a fighter closes on the screen at 5 u/s. The screen is 17.8 wide (16:9), so an undisturbed fighter is visible for 17.8 / 5 = 3.6 s. A fighter appears (1.5 + random 0..6) / 5 = 0.3 to 1.5 s after its wave triggers; in a pair, the second plane follows 6 + random 0..6 x later (1.2 to 2.4 s). A pair therefore covers at least 0.3 + 1.2 + 3.6 = 5.1 s from its trigger. Pairs trigger every 11 x = 5.5 s of camera travel, so the worst case gap between one pair leaving and the next appearing is 5.5 + 1.5 - 5.1 = 1.9 s. Camera travel in 2 s = 4 units. Designed max gap: 1.9 s (about 9.5 units of camera at worst), only when every roll is worst case; the typical gap is zero (planes overlap). Live fighters: about 3 on average, at most 5 (two overlapping pairs plus a straggler), under the cap of 6. The player shooting fighters down early creates gaps; the runtime EnemyPressure net fills those. From x 66 to the zeppelin (about 300 x = 150 s) the sky is never designed to be empty; the zeppelin itself is an aircraft, and the last pair at 396 is on screen until the hull's nose enters (about camera 411). No fighters are added during the zeppelin fight beyond the 5 escorts.

Fighter count: 62 plus 5 escorts (was 15). This is far above 2x because the 2 s rule needs a plane about every 5 s; M4 uses single fighters, so M5 stays the hardest (0.36 vs 0.27 fighters per second, plus the zeppelin).

Balance (V008, QA T958 method: 100% start + 30% per balloon vs expected damage, dodging p=0.25):
- Before: 67 fighters about 109%, 7 AAA about 126%, 3 turrets about 36%: 271% expected vs 3 balloons, budget 190%. Unsurvivable on paper.
- Change: removed the 2 AAA at 385 (-36%, about 18% each) and added 3 balloons (150, 290, 385). Every fighter is kept.
- After: expected 235% (109 + 5 AAA about 90 + 36) vs 6 balloons, budget 280%. Headroom 45 points (19%). Dueling (about 300%) would still lose, which is intended for the hardest mission. Estimate only, not a playtest.
- V010: added 2 balloons (80, 405, the latter in the zeppelin stretch; V011 moved it from 470) -> 8 balloons, budget 340% vs expected 235%. Headroom 105 points; dueling (about 300%) is now roughly survivable.
- The zeppelin fight keeps its 3 turrets; the finale AAA at 385 is gone, the balloon takes its x.

- V013: added 2 balloons (60, 98), both in the first half (x < 270) -> 10 balloons (was 8; first half 4 -> 6), budget 400% vs expected 235%. Nothing else changed.

Difficulty knobs:
- zeppelin atX minus failAtX (60 = 60 s of flight). The map end must stay later than the HQ loss: (length - trigger x) / 2.6 > 60 s; length 580 gives about 65 s (see the V014 section).
- enemySpeed.zeppelin in Balance.json (requested change only through mechanics).
- Pair step (11 x; 12 would give a 2.4 s worst gap, 9 gives 0.9 s), pair size and spacing; escort counts at 424 and 429; AAA at 120 and 235.
- Health balloons at 60, 80, 98, 150, 200, 255, 290, 320, 385 and 405.
- zeppelin y (4.9, keep >= 3) and terrain under the finale.

## Speed +30% re-check (V014)
Player forward speed = scrollSpeed, raised 30%. Fighters still fly left at 3 u/s, so the closing speed on the screen is 3 + scroll. Numbers are from 3000 uncontested Monte Carlo runs of the spawn model (cumulative stagger 1.5 + 0..6 x per plane, min gap 2 x, 17.8 + 1.5 screen), nobody shooting.
- scrollSpeed 2.0 -> 2.6. Closing speed 5.60 u/s, so an undamaged fighter stays on screen 19.3 / 5.60 = **3.45 s** (was 3.86 s).
- Camera travel in 2 s: **5.2 units** (was 4.0).
- Max empty-sky gap (uncontested; fighter pairs x 66-396): was worst 1.9 s (V006); now **median 0.24 s, 99th 0.66 s, worst 0.74 s**. Waves trigger sooner in time (atX / scroll) while a plane's entry delay shrinks less (closing speed only +12 to +18%), so waves overlap more and the gaps close up instead of opening. Max alive at once: median 3, at most 3, under the cap of 6.
- No spacing change needed (all 62 pair fighters and 5 escorts kept).

Zeppelin vs the map end (V014, length 540 -> 580): the zeppelin (`airship_01`, atX 420, y 4.9, 1.0 u/s) spawns at x 420 when the camera is at 420 - (5 x aspect + 2) = 409.1 (16:9) or 411.3 (4:3), and needs (420 - 360) / 1.0 = 60 s to reach the HQ (failAtX 360). A player who never turns reaches the map end after (length - trigger) / 2.6. With length 540 that was (540 - 411.3) / 2.6 = 49.5 s at 4:3 (50.3 s at 16:9), so **the map end came first by about 10 s**. Length 580 gives (580 - 411.3) / 2.6 = **64.9 s at 4:3 (65.7 s at 16:9)**, so the HQ loss comes first with a 4.9 to 5.7 s margin (it was 5.5 to 6.7 s). Alternative not taken: moving atX to 413 or less would also move the escorts (424/429), the 385 and 405 balloons and the HQ approach. Terrain clamps at the last keyframe (540), so x 540-580 is flat ground at 0.5; nothing spawns there. The zeppelin reaches the camera about 4 s after it appears; the fight window to the HQ is still about 60 s. Duration is now 580 / 2.6 = 223 s.

- **Bomb recharge (V064):** one static `bombBalloon` at x 272 (y 3.5), mid-map, clear of ground units and other balloons. It refills 4 bombs (cap 7); it is not an enemy and counts toward no objective or health-balloon rule.

## Length 580 -> 406 (V065)
Everything x was scaled by 0.7 and rounded; the rest of this sheet quotes the old x values (old x * 0.7 = new x).
- Terrain keyframes 0/120/280/400/540 -> 0/84/196/280/378 (heights unchanged; clamps flat from 378 to 406).
- HQ and `failAtX` 360 -> 252. Zeppelin `airship_01` atX 420 -> 294. Escorts 424/429 -> 298/302 (kept 4 and 8 above the hull so the hull exists before escorts look it up, aspect spread 2.2).
- AAA: 2 at 120 -> 84 (spacing 6); 3 at 235 -> 165 (spacing 4). Group spacing kept. No ground unit sits within 80 x of another.
- Health balloons (10) 60/80/98/150/200/255/290/320/385/405 -> 42/56/69/105/140/179/203/224/270/284. First half (x < 203): 6, unchanged. Bomb balloon 272 -> 190 (middle third [135, 271]).
- Fighters: all 31 pairs and 5 escorts kept; pair step 11 -> about 7.7 x (still 3.0 s of camera travel per pair at 2.6 u/s: 7.7 / 2.6 = 3.0 s vs 11 / 2.6 = 4.2 s before). Pairs come about 30% faster per second, so the sky is denser, not emptier. Same counts, in a shorter run.
- Zeppelin vs map end: it triggers at camera 285.3 (4:3) / 283.1 (16:9), needs 294 - 252 = 42 s to reach the HQ. A player who never turns reaches the map end after (406 - 285.3) / 2.6 = 46.4 s (47.3 s at 16:9). HQ loss comes first with a 4.4 to 5.3 s margin (was 4.9 to 5.7 s).
- Honest tightness: the fight window to the HQ is 42 s (was 60 s), about 30% less, by construction. Each pass (closing 3.6 u/s, screen 17.8) offers about 5 s of fire; 40 hull hits at fireRate 8 is 5 s of clean fire plus 9 turret hits, so killing it takes about 2-3 passes of a player who turns, plus bombs (100 each, 10 to kill). Winnable on paper, but tighter than before; with the 5 escorts and the 4 turrets/fighters pressure it needs real skill. Fallback if QA finds it too tight: lower enemySpeed.zeppelin (mechanics request) or move the HQ/failAtX down.
- Balance exposure: fighters and AAA counts are the same over 30% less time, and healing balloons are the same 10, so the sky is more intense per second but the balloon count per second is higher too.

Difficulty knobs (updated): zeppelin atX minus failAtX is now 42 s of flight; the map end must stay later than the HQ loss: (406 - trigger) / 2.6 > 42 s.
