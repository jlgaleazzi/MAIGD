# Biplanes: Game Design Document

Author: Juan Galeazzi. Final draft, 10/25/2026. Converted from `Biplanes2.pdf`.
This file is the source of truth for the crew. Only the orchestrator edits it, and every
change is logged in the Change Log at the bottom.

## 1. Executive Summary

Biplanes is a side-scrolling WWI airplane shooter. The player survives waves of enemy
airplanes and ground fire and flies through a sky full of obstacles (barrage balloons,
mountains, rural buildings) before reaching the mission objective.

- **Win conditions:** the player scores points by destroying enemies and completes the
  stated mission objectives.
- **Loss state:** each hit from enemy fire adds damage to the player's biplane until it
  crashes. The biplane also crashes if it hits terrain or buildings.
- **Visual style: Blueprints.** Duotone with wireframes for the main elements (hero plane,
  enemies, targets). Background elements are drawn more loosely, like a pencil illustration.

## 2. Game Mechanics

- **Core loop:** the player moves the airplane up, down, left and right to shoot enemies
  and to avoid enemy fire, terrain and buildings.
- **Scoring:** each enemy kill adds points. Harder enemies are worth more.
- **Milestones / save progress:** 5 missions, each harder than the one before.

### Player airplane
A WWI-era biplane with a forward-facing machine gun and room for **7 bombs** for ground
attacks. It is marked with a **bullseye (roundel)** on the center of its body.

### Enemies
1. **Airplanes:** marked with an **Iron Cross** on the center of the body and on the tail.
   They shoot at the player and can only be destroyed by direct hits.
2. **Anti-Aircraft Artillery (AAA):** fires up from the ground at the player. It can only
   be destroyed with **bombs**.
3. **Bombers:** bigger airplanes, usually with a fighter escort, that try to bomb friendly
   targets. They take **multiple hits** to destroy.
4. **Zeppelins:** big aircraft with several gun positions. The player must dodge their fire
   and disable the **turrets** by hitting them. A zeppelin takes **several hits** to destroy.

### Obstacles
1. **Barrage balloons:** low-flying balloons, usually protected by AAA. The player destroys
   them when the mission requires it.
2. **Buildings and trees:** flying too low can crash the player into them.

### Airplane damage
- Damage goes up with each hit. At **100%** the airplane is destroyed.
- At about **70%** damage, a smoke trail shows behind the plane.
- **Health balloons** appear at some points on the map. Flying through one restores health.

### Controls (keyboard and mouse)
| Action | Input |
|---|---|
| Climb / dive | W / S |
| Face and fly left / right (the world scrolls the way you face) | A / D |
| Fire machine gun (hold to autofire) | Left mouse button |
| Drop bomb | Space |

### Flight attitude (pitch)
All airplanes (the player's and the enemies') **pitch** around their lateral axis: the nose tips up while the plane climbs and down while it dives, eased smoothly and limited to a modest angle, and the sprite and its hitboxes rotate with it. Machine-gun bullets (the player's and the fighters') leave the gun along the **nose direction**, so what you see is the way the shot flies. A fighter swings its nose onto you and fires only when you are close to where it points. The bomber's tail gunner is the one exception: it is a swivel gun that fires at you in any direction. Bombs still drop with gravity from the plane's position.

## Missions (increasing difficulty)
1. **Destroy incoming airplanes:** the player starts at the airfield as a wave of enemy
   airplanes approaches. Take off and destroy every incoming enemy airplane.
2. **Destroy enemy bomber:** an enemy bomber with a fighter escort is approaching the
   airfield. Destroy it before it reaches its target and drops its bomb.
3. **Destroy enemy defenses:** fly into enemy territory and destroy AAA with bombs while
   dodging fire from the ground and from enemy airplanes.
4. **Destroy enemy base:** go deeper into enemy territory, destroying barrage balloons,
   AAA and enemy planes, and finally drop a bomb on the enemy base.
5. **Enemy Zeppelin:** the enemy deploys its newest and most powerful weapon, a massive
   zeppelin with anti-aircraft turrets and a fighter escort. Destroy it before it reaches
   its destination, the player's HQ.

## 3. AI Architecture (the crew)
| Agent | Responsibility |
|---|---|
| Orchestration | Breaks the high-level concept into specialized roles; manages the GDD, architecture, UI and QA strategy |
| Art | Creates all visual elements; sets the game's style and aesthetics |
| Game Mechanics | Builds the core framework logic, physics and collision handlers |
| Mission Design | Builds each of the five missions |
| QA | Checks the game is playable on the target platforms, the controls work, and there are no bugs |

## 4. Technical Strategy
- **Engine / deployment:** Unity. The first release is playable on PC/Mac and Web.
- **Constraints:** Unity is new to the developer, so expect a learning curve. Six weeks,
  solo developer.
- **Token budget:** the agents run locally on an NVIDIA DGX Spark with opencode and
  Qwen3.6 35B. There's no budget to manage, but the model isn't a frontier model and
  processing is slower.

## Change Log
| Date | Change | By |
|---|---|---|
| 2026-10-31 | Converted from PDF | setup |
| 2026-11-10 | Reversible flight (user decision): A/D turn the plane Defender-style, and the world scrolls in the facing direction; mission progress = farthest x reached (T924) | orchestrator |
| 2026-11-12 | Bombs keep inheriting the plane's real world x-velocity, so a bomb dropped during the post-turn swing can drift against the facing (T925 P2, accepted as physically consistent) | orchestrator |
| 2026-11-12 | AAA flak cannot fire shallower than 30° above the horizon (FlakShell MinUpward), even though guns now aim at the true intercept: low approaches and planes already past the gun are harder to hit. Accepted as a fairness rule (T929 P2-1) | orchestrator |
| 2026-11-13 | Mission end and design calls from QA T936: a mission is lost when the camera reaches the end of the map with an objective still open; mission 3 asks for at most 5 bomb-only AAA because the player carries 7 bombs and gets no refill; `failAtX` is the world x a target (bomber, zeppelin) must not reach, and mission 5 puts the HQ on the side the zeppelin flies toward | orchestrator |
| 2026-11-13 | Applied: M3 has 5 AAA plus extra fighter waves. M2 failAtX 10 (bomber spawns near x 90, about 55 s of flight), AAA removed from M2 (AAA debuts in M3), length 450. M5 length 540, HQ at x 360 = failAtX, zeppelin spawns at x 420 (id `airship_01`, y 4.9 above terrain so the hull centre is near world y 0.9, 1.0 u/s, 60 s to reach the HQ; the camera cannot reach the map end first) | orchestrator |
| 2026-11-14 | Playtest call: touching an enemy fighter or bomber in mid-air is no longer an instant crash. It destroys the enemy and gives the player a heavy but survivable hit (Balance `damageTaken.planeCollision`, 50%), so damage accumulates as the GDD describes. Terrain, buildings, trees and rammed zeppelin parts stay fatal | orchestrator |
| 2026-11-14 | Controls changed by the user: machine gun moves from Space to the left mouse button (hold to autofire), bombs move from Enter to Space. Movement stays W/S/A/D | user |
| 2026-11-15 | New feature requested by the user: airplane pitch (nose follows climb and dive) and machine-gun bullets fired along the nose direction, for the player and the enemies. Side effect to re-check: player bullets were horizontal only, which the zeppelin turret line-of-fire geometry (T939/B021) and the enemy-fire cone assumed | user |
| 2026-11-15 | Pitch decisions after QA T942: fighters fire along their nose and only when the player is within 10 degrees of it (nose eases toward the player, up to 40 degrees); the bomber's gunner stays a swivel gun and is the documented exception to nose-aligned bullets | orchestrator |
