# Mission 2: Destroy the Enemy Bomber

GDD goal: destroy the bomber with its fighter escort before it reaches the airfield.
Win: `destroyById bomber_1`. Lose: plane destroyed, the bomber reaches `failAtX` 10 (flying left, the airfield), or the camera reaches the map end with the bomber alive.
Duration: 450 / 2.5 = 180 s.

Geometry: the bomber is triggered when the camera reaches x 80 and appears 10.4 to 16.4 right of the camera, so it spawns at about x 90 to 96 (always greater than failAtX 10). It flies left at 1.5 u/s, so it needs about 53 to 57 s to reach x 10. The camera closes at 4 u/s on the first pass, so contact comes in about 3 s. A turn-around then gives a 1 u/s catch-up, and many chances to hit it. The bomb run starts at x 15.

| x range | What happens | Skill |
|---|---|---|
| 0-45 | Intro, no threats | Warm up |
| 50 | 2 straight fighters | Dogfight basics |
| 65 | Health balloon (breather) | Recover |
| 80 | Bomber enters with 2 escort fighters (escort pattern) | Read the formation |
| 85 | Third escort fighter | Clear the escort, then the bomber |
| 150 | 3 sine fighters (harassment if the bomber is still alive) | Tracking while chasing |

The objective climax is the bomber kill itself (200 hp, gunner fire); the mission ends on the kill.
AAA from the old layout was removed: AAA is introduced in mission 3.

Difficulty knobs:
- failAtX (lower = more time to catch the bomber; must stay below the spawn x of about 90).
- Bomber atX (later = longer intro; the escort atX values should track it).
- Escort count and pattern at 80 and 85 (2 to 4 allowed).
- Health balloon at 65 (remove for hard mode).
- Harassment wave at 150.
