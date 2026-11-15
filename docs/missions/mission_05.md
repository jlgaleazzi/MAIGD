# Mission 5: Enemy Zeppelin

GDD goal: destroy the zeppelin before it reaches the player's HQ.
Win: `destroyById airship_01`. Lose: plane destroyed, the zeppelin reaches `failAtX` 360 (the HQ x), or the camera reaches the map end with the zeppelin alive.
Duration: 540 / 2.0 = 270 s.

Id: the zeppelin id is `airship_01`, not `zep`, because FighterAI matches `escortOf` as a substring of entity names and the turrets are named `zeppelinTurret`. "airship_01" matches only the hull.

Geometry: the HQ sits at x 360 = failAtX. The zeppelin (atX 420) spawns when the camera reaches atX minus (5*aspect+2): 409.1 at 16:9, 411.3 at 4:3 worst case is 420-8.7. It flies left at 1.0 u/s, so it needs 60 s to reach the HQ. Map-end arithmetic: a player who keeps flying right reaches camera 540 after (540-420+10.9)/2 = 65.5 s at the widest aspect, which is later than the 60 s the zeppelin needs, so the HQ loss always comes first (5.5 s margin; at 4:3 it is 6.7 s). Escorts launch at 424 and 429, at least 4 above the zeppelin atX, so the hull exists before any escort looks it up (aspect spread is 2.2). Y 4.9 above the terrain (0.5 at x 420) puts the hull mid-sky with the underside turret above the ground and shootable. First contact is about 4 s after it appears; each pass offers 7 to 15 s of fire; the kill needs 9 hits on 3 turrets plus 40 hull hits, or bombs (100 each).

| x range | What happens | Skill |
|---|---|---|
| 0-54 | Intro, no threats | Warm up |
| 70 | 3 straight fighters | Dogfight |
| 120 | 2 AAA, spacing 6 | Flak dodging |
| 170 | 3 sine fighters | Tracking |
| 200 | Health balloon (breather) | Recover |
| 235 | 3 AAA, spacing 4 (peak) | Flak lanes |
| 275 | 4 straight fighters | Crowd control |
| 320 | Health balloon | Recover before the finale |
| 360 | Player HQ (= failAtX) | Landmark, the line to defend |
| 385 | 2 AAA, spacing 5 | Pressure while the zeppelin closes |
| 420 | Zeppelin (id `airship_01`, 3 turrets, y 4.9) | Objective climax: turrets first, then hull |
| 424, 429 | 5 escort fighters in two waves, follow the hull | Clear the escort |

Difficulty knobs:
- zeppelin atX minus failAtX (60 = 60 s of flight; keep atX <= 423 at length 540 so the map end stays later than the HQ loss).
- enemySpeed.zeppelin in Balance.json (requested change only through mechanics).
- Escort counts at 424 and 429; AAA at 385.
- Health balloons at 200 and 320.
- zeppelin y (4.9, keep >= 3) and terrain under the finale.
