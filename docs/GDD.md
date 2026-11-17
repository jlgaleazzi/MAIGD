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

## Style Guide ("Blueprints")

The look is an engineer's drawing board: light lines on dark blueprint paper. Production
detail (stroke widths, SVG rules, pipeline, asset list) lives in `docs/ART_STYLE.md`; this
section is the design intent it must serve.

### Palette: duotone plus three accents
| Colour | Hex | Used for |
|---|---|---|
| Paper | `#0F2A4A` | Background, UI panel fill, the opaque silhouette behind each plane |
| Paper grid | `#1C3F66` | Faint 32 px blueprint grid on the far background |
| Ink | `#E8F1FA` | All crisp lines: planes, targets, UI borders and text, faction marks |
| Pencil | `#9DB4CC` | Background illustration, soft smoke, empty UI slots |
| Friendly | `#F2C14E` | Player bullets and full bomb icons only |
| Hostile | `#E4572E` | Enemy tracers, flak bursts, the damage bar past 70% and the smoke warning only |
| Heal | `#7BD389` | The health balloon cross only |

No other colours. Accents are rare on purpose, so anything that is coloured is a
projectile, a threat or a pickup, and the screen otherwise stays duotone.

### Three drawing styles, one per layer of meaning
1. **Crisp blueprint wireframes: everything you can hit or must avoid.** The player's
   biplane, fighters, bombers, the zeppelin, AAA guns, balloons, buildings and targets.
   Dense technical line art in ink with no fills: panel lines, rivets, ribs, struts and
   bracing wires, spoked wheels, hatched shading, a dashed propeller disc. Line weights are
   thin and consistent (outline about 1.25 px, structure 0.75 px, detail 0.5 px at sprite
   scale), so hand-drawn SVGs and the user's imported blueprint drawings
   (`blueprints/`) look like one set. Planes get an opaque paper silhouette so the
   background never shows through the airframe. Faction marks are ink white: a bullseye
   roundel for the player, a true cross pattee for the enemy. All aircraft art faces right;
   code mirrors it.
2. **Engraved pencil backgrounds: everything you fly past.** Three parallax layers (far,
   mid, near) drawn like an old pen-and-ink engraving: tone comes from hatching and
   cross-hatching, never flat fills; far things are big, simple and faint, near things are
   dense and outlined. Backgrounds use only paper, grid, pencil and faint ink, never an
   accent, and keep the flight band quiet so nothing in the scenery can pass for a plane,
   gun or mark. Four themes: farmland, front line, enemy land, storm sky.
3. **Loose pencil sketches: effects.** Explosions, smoke and flak are the one in-world
   exception to crisp lines (T944): wobbly contours drawn two or three times, cauliflower
   billows, hatched cores, wavy wisps (never straight), debris shards with motion lines.
   Explosions use ink and pencil only; flak keeps its hostile red flash. Effects animate
   frame by frame (explosion 6 frames, flak 4, a smoke-puff trail from a plane at 70%
   damage or more) rather than being scaled up, so the fine hatching stays sharp.

### UI
UI panels are paper with a 2 px ink wireframe border; text is ink in the default font.
The HUD uses the accents only for meaning: friendly yellow for bombs left, hostile red for
the damage bar past 70% and the smoke warning. Full layout is in `docs/UI_SPEC.md`.

### Sound effects
No audio exists in the game yet; this is the target. The sound matches the picture: a
period WWI air war heard **through the drawing board**. Real aircraft and guns, recorded
dry and slightly lo-fi like an old newsreel, layered with soft paper and pencil foley in
the places where the art is sketchy.

**Principles**
- **Period, not modern.** Rotary engines, Vickers/Spandau-style machine guns, black-powder
  thumps. No synth whooshes, laser zaps or modern UI bleeps.
- **Paper and pencil for the drawn layer.** UI, effects and transitions borrow drafting
  sounds: pencil scratch, paper rustle, a ruler tap, a stamp. The blueprint look is heard
  as well as seen, but always underneath the gameplay sound, never instead of it.
- **Sound follows colour.** What the palette marks as important is what the mix makes
  loud: threats (hostile red: flak, enemy fire, damage warning) and the player's own
  weapons (friendly yellow). Scenery is silent apart from a light wind bed.
- **Readable at a glance.** Every gameplay event has one short, distinct cue; an enemy hit
  on the player must never sound like the player hitting an enemy.
- **Restraint.** Short cues (most under 0.5 s), few simultaneous voices, and repeated
  cues vary slightly in pitch and volume so autofire and smoke trails don't drone.
- **Stereo by position.** Cues pan with the source's x on screen; off-screen threats
  are quieter, not silent.

**Cue list**
| Event | Sound |
|---|---|
| Player engine | Looping rotary-engine drone, pitch rising when climbing and falling when diving (follows the pitch section); coughs and sputters at 70% damage or more |
| Player machine gun (left mouse) | Dry, fast mechanical rattle per shot, light enough to hold for autofire |
| Bomb release / impact | Mechanical latch click and a short whistle; a deep dirt-and-wood thump on the ground |
| Bullet hits an enemy | Small metallic tick or canvas rip |
| Player takes a hit | Heavier wood-and-canvas crack, plus a short pencil scratch, clearly different from the enemy-hit sound |
| Enemy fighter / bomber gun | Same family as the player's gun, lower and more distant |
| Flak | A dull pop when fired from the ground; a sharp crack with a puff of air at the burst |
| Explosion (enemy downed, target destroyed) | Low boom with a paper-crumple tail, timed with the 6-frame sketch animation |
| Zeppelin | Slow, deep engine hum while on screen; its destruction is the biggest explosion in the game, a long rolling boom |
| Smoke trail | A faint crackle and pencil-scribble hiss under the engine while the plane smokes |
| Crash | Engine whine dropping in pitch as the wreck falls, ending in a crunch and silence |
| Health balloon pickup | Soft, warm chime-like ruler tap (the only "pleasant" in-world cue) |
| Damage warning (70%) | One short alarm-style bell when crossing the threshold, not a loop |
| UI: button, menu, pause | Pencil tick, page turn for screens, a muffled hush on pause |
| Briefing | Typewriter and paper sounds as the text appears; a stamp at "Begin" |
| Mission won / lost | Won: a short brass-and-snare flourish and a rubber-stamp thud. Lost: a muted, falling phrase |

**Music**: sparse and period-flavoured (snare, brass, solo piano), under the briefing and
result screens, and quiet or absent in flight so engines and guns carry the action.

### Hard rules
- Tone by line, not by fill (fills only for faction marks, accents and the plane
  silhouette).
- Gameplay objects crisp, scenery engraved, effects sketchy: never mix them up.
- Readability first: nothing at full-opacity ink in the backgrounds, and nothing in the
  scenery that looks like a gameplay object.
- Every sprite is reproducible from source (SVG, blueprint import or generator script);
  no hand-painted PNGs.

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
| 2026-11-17 | Added the Style Guide section, recording the style used so far: duotone palette with three accents, crisp blueprint wireframes for gameplay objects, engraved pencil backgrounds, and loose animated pencil-sketch effects (T944/B030) | orchestrator |
| 2026-11-17 | Added the Sound effects subsection to the Style Guide: period WWI sounds heard through the drawing board (paper and pencil foley for UI and effects), loudness follows the palette, and a cue list per game event. No audio is implemented yet | orchestrator |
