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

### Controls (keyboard)
| Action | Key |
|---|---|
| Move | W A S D |
| Fire machine gun | Space |
| Drop bomb | Enter |

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
