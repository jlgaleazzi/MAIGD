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
   They shoot at the player and can only be destroyed by direct hits: **three** machine-gun
   hits bring one down. A hit fighter **trails pencil smoke** from its first hit, thicker after
   the second, so the player can see which planes are already damaged. **Fighters hunt the
   player:** every enemy fighter always faces the player's plane and chases it, turning around
   when the player gets behind it, climbing and diving to match its height and firing when its
   nose is on target. They hold a short distance instead of ramming, and spread out so they
   don't stack. At most three fighters shoot at the player at the same time; the rest of the
   pack keeps chasing and waits for a turn. A fighter never flies backwards: one ahead of the
   player flies at it, passes above or below, then turns and chases. Where the player cruises
   faster than the fighters (missions 2-4) a fighter left behind falls away and leaves the fight;
   in missions 1 and 5 the pack can keep up. Escort fighters stay with their bomber or zeppelin until the player comes close, then
   chase too. Bombers and zeppelins keep their own course.
2. **Anti-Aircraft Artillery (AAA):** fires up from the ground at the player. One **bomb**
   destroys it, and so do **five** machine-gun hits.
3. **Bombers:** bigger airplanes, usually with a fighter escort, that try to bomb friendly
   targets. They take **multiple hits** to destroy.
4. **Zeppelins:** big aircraft with several gun positions. The player must dodge their fire
   and disable the **turrets** by hitting them. A zeppelin takes **several hits** to destroy.

### Enemy pressure
The sky is never empty for long. Missions are packed with fighter waves, and during play
there is **never more than 2 seconds without an enemy aircraft on screen**. If the screen has
been clear of enemy planes for 2 seconds, a small wave of reinforcement fighters (2 by default)
enters from the right side of the screen. Reinforcements score like any fighter and **count toward a
"destroy them all" objective** like any other fighter of that type: every kill the player sees
moves the counter. The objective still needs the mission's own number of kills (the fighters
listed in the mission), so reinforcements only help reach it sooner. They stop once the
mission's objectives are complete, and never come while the plane still sits on the runway.
**Enemy aircraft always enter from the right side of the screen, never from the left**, also when the player flies left (then they come in from behind). An enemy fighter that gets past
the player and leaves the left edge comes back in from the right side while a "destroy them all" objective still needs it;
otherwise it leaves the battle for good.

### Mission start
Every mission waits for the player. After the briefing, the world holds still: the
background does not scroll, the plane holds its position, no enemy appears and no
reinforcement timer runs, until the player presses a flight key (W, A, S or D), the fire
button or the bomb key. That first input starts the mission. (Mission 1 already waited on the
runway; now every mission does.)

### Minimap
A minimap strip at the top centre of the screen shows the whole mission from start to end:
where the player is and which way they face, the part of the map on screen, live enemies,
enemies still to come (faint ticks), and the mission objectives (marked with diamonds, plus
the line a target must not cross). Like everything else it is drawn in white. Layout in
`docs/UI_SPEC.md` §3.3.1.

### Obstacles
1. **Barrage balloons:** low-flying balloons, usually protected by AAA. The player destroys
   them when the mission requires it. A balloon has 100 health (4 bullets or 1 bomb), and flying into one pops it but costs the player 33% damage.
2. **Buildings and trees:** flying too low can crash the player into them.

### Airplane damage
- Damage goes up with each hit. At **100%** the airplane is destroyed.
- At about **70%** damage, a smoke trail shows behind the plane.
- **Health balloons** appear at some points on the map. Flying through one **restores all of the plane's health (100%)**.
- **Recharge bomb balloons** carry a bomb on the envelope. Flying through one **gives 4 bombs, up to the maximum of 7**. Every mission has one in the middle of the map, and when the player has dropped all bombs, another appears ahead 4 seconds later (and again every 4 s if it is missed, until the bay has bombs).

### Controls (keyboard and mouse)
| Action | Input |
|---|---|
| Climb / dive | W / S |
| Face and fly left / right (the world scrolls the way you face) | A / D |
| Fire machine gun (hold to autofire) | Left mouse button |
| Drop bomb | Space |

### Flight attitude (pitch)
All airplanes (the player's and the enemies') **pitch** around their lateral axis: the nose tips up while the plane climbs and down while it dives, eased smoothly and limited to a modest angle, and the sprite and its hitboxes rotate with it. Machine-gun bullets (the player's and the fighters') leave the gun along the **nose direction**, so what you see is the way the shot flies. A fighter swings its nose onto you and fires only when you are close to where it points. The bomber's tail gunner is the one exception: it is a swivel gun that fires at you in any direction. Bombs fall **ballistically**: a released bomb keeps the plane's full velocity (forward speed and any climb or dive) and gravity bends it into a parabola, so it lands ahead of the drop point and the pilot has to lead the target. A bomb always leaves in the direction the plane faces, even right after a turn. The bomb's nose turns to follow its path.

## Missions (increasing difficulty)
1. **Destroy incoming airplanes:** the player starts at the airfield as a wave of enemy
   airplanes approaches. Take off and destroy every incoming enemy airplane.
2. **Destroy enemy bomber:** an enemy bomber with a fighter escort is approaching the
   airfield. Destroy it before it reaches its target and drops its bomb.
3. **Destroy enemy defenses:** fly into enemy territory and destroy AAA with bombs or gunfire while
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

### Palette: white on blueprint blue, nothing else
| Colour | Hex | Used for |
|---|---|---|
| Paper | `#0F2A4A` | Background, UI panel fill, the opaque silhouette behind each plane |
| Paper grid | `#1C3F66` | Faint 32 px blueprint grid on the far background |
| Ink | `#E8F1FA` | Every game element: planes, targets, bullets, bombs, flak, pickups, faction marks, UI borders, text and HUD |
| Pencil | `#9DB4CC` | Background illustration, soft smoke, empty UI slots |

**Only white is used for game elements** (user decision, 2026-11-18): ink white, drawn on
paper blue, with pencil for the softer layer. There are no colour accents. What tells the
player apart from the enemy, or a threat from a pickup, is **shape, motion and line
weight**: player bullets are solid 3-pixel line bursts and enemy tracers are dot-gap-dot bursts; flak
bursts are spiky pencil sketches; the health balloon carries a bold cross; a bomb still in
the rack is a bright ink icon, a spent one a faint pencil icon; the damage warning flashes.

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
   dense and outlined. Backgrounds use only paper, grid, pencil and faint ink, and keep the flight band quiet so nothing in the scenery can pass for a plane,
   gun or mark. Four themes: farmland, front line, enemy land, storm sky.
3. **Loose pencil sketches: effects.** Explosions, smoke and flak are the one in-world
   exception to crisp lines (T944): wobbly contours drawn two or three times, cauliflower
   billows, hatched cores, wavy wisps (never straight), debris shards with motion lines.
   Explosions and flak use ink and pencil only; a flak burst reads by its spiky star. Effects animate
   frame by frame (explosion 6 frames, flak 4, a smoke-puff trail from a plane at 70%
   damage or more) rather than being scaled up, so the fine hatching stays sharp.

### UI
UI panels are paper with a 2 px ink wireframe border; text is ink in the default font.
The HUD is ink white too: bombs left are bright ink icons and spent ones faint pencil; past 70%
the damage bar fill turns to hatching and it and the smoke warning flash. Full layout is in
`docs/UI_SPEC.md`.

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
- **Sound follows importance.** Threats (flak, enemy fire, the damage warning) and the
  player's own weapons are what the mix makes loud. Scenery is silent apart from a light
  wind bed.
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

### Narrative voice
Every word the player reads belongs to the same world as the art: a WWI squadron on the
western front, 1914-1918. Mission titles and briefings are orders from the squadron
commander to the pilot.
- **Setting:** period, but invented. The other side is always "the enemy"; no real
  nations, leaders, units or battles are named.
- **Briefings** are a commander's dispatch: second person ("you"), present tense, 2 or 3
  sentences and at most 40 words. The first sentence states the threat, the last gives the
  order, and the order names the mission's target. It mentions only enemies the player will meet in that
  mission. At most one exclamation mark.
- **Titles** are 2 to 4 words in Title Case and name the target or the order, matching
  the mission list above.
- **Tone:** urgent and plain, never jokey or gory. The player destroys machines, not
  people.
- **Vocabulary:** period words only. No modern technology or slang; use the left column
  instead of the right.

| Use | Instead of |
|---|---|
| anti-aircraft batteries | AAA batteries |
| anti-aircraft guns | AAA guns, AAA |
| bombs | payload, ordnance, munitions |
| destroy | neutralize, take out, eliminate |
| the enemy | Germany, Germans, German, the Kaiser, the Hun, Allies, Allied |
| wireless | radio |
| (remove) | radar, missile, jet, helicopter, drone, satellite, GPS, laser |

### Hard rules
- Tone by line, not by fill (fills only for faction marks and the plane silhouette). A filled shape no thicker than a line (a tapered stroke, a grass tuft, a
  rivet or stipple dot) counts as a line.
- Gameplay objects crisp, scenery engraved, effects sketchy: never mix them up.
- Readability first: nothing at full-opacity ink in the backgrounds, and nothing in the
  scenery that looks like a gameplay object.
- Every sprite is reproducible from source (SVG, blueprint import or generator script);
  no hand-painted PNGs.

### Enforced rules
The style loop (`tools/style_loop`) reads this table, the palette and the vocabulary table
above, and enforces them on every generated or existing asset. `auto` rules are checked
and fixed by code; `judge` rules are checked and fixed by the model against the prose of
this Style Guide. Change a rule here and the loop follows it.

| ID | Applies to | Rule | Check | Parameters |
|---|---|---|---|---|
| A-PALETTE | art | Only the four palette colours (white on blueprint blue) | auto | |
| A-NO-FILL | art | Tone by line, not by fill: fills only for faction marks and the paper silhouette | auto | mark_ids=roundel,cross,mark,faction; line_max_px=2 |
| A-LINE-WEIGHT | sprites | Thin, consistent lines (outline about 1.25 px) | auto | max_px=1.5; categories=player,enemies,targets,obstacles,pickups |
| A-BG-QUIET | backgrounds | No full-opacity ink in the scenery | auto | max_ink_opacity=0.45 |
| A-NO-TEXT | art | No lettering inside art; words are UI text | auto | |
| A-FX-SKETCH | fx | Effects are loose pencil sketches: contours drawn two or three times, hatched, wavy wisps | judge | |
| A-FACE-RIGHT | aircraft | Aircraft art faces right | judge | |
| N-VOCAB | text | Period vocabulary (table above) | auto | |
| N-LENGTH | briefing | 2 or 3 sentences, at most 40 words | auto | min_sentences=2; max_sentences=3; max_words=40 |
| N-EXCLAIM | briefing | At most one exclamation mark | auto | max=1 |
| N-TITLE | title | 2 to 4 words, Title Case | auto | min_words=2; max_words=4 |
| N-TARGET | briefing | The briefing names the mission's target | auto | |
| N-GROUNDED | briefing | The briefing mentions only enemies that are in the mission | auto | |
| N-FAITHFUL | briefing | The briefing matches the GDD's mission description: no invented enemies, weapons, places or objectives, and the player's weapon fits the target (guns for aircraft, bombs for ground targets) | judge | |
| N-VOICE | briefing | A commander's dispatch to the pilot: "you", present tense, threat first, order last, urgent, no gore | judge | |

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
| 2026-11-17 | Added Narrative voice (setting, briefings, titles, tone, period vocabulary) and the Enforced rules table to the Style Guide, so the style loop (`tools/style_loop`) can enforce the GDD directly | orchestrator |
| 2026-11-18 | Difficulty raised at the user's request: fighters take 3 hits (Balance `enemyHealth.fighter` 75 with `bulletDamage` 25), missions get more fighter waves, and the new Enemy pressure rule guarantees at most 2 s without an enemy aircraft on screen (reinforcement fighters, Balance `pressure`) | user |
| 2026-11-18 | Bombs fall in a realistic parabola (user request): they inherit the plane's full velocity, gravity comes from Balance `bomb.gravity` (about 10 units/s², was 12x Unity gravity, about 118), light air drag slows the forward speed, and the sprite turns along the path | user |
| 2026-11-18 | The falling player bomb gets real art (user request): `player_bomb`, a small horizontal copy of the HUD bomb icon in ink white (user: game elements are white), nose along its flight path, replacing the white placeholder rectangle | user |
| 2026-11-18 | AAA can be shot down (user request): five machine-gun hits destroy a gun (Balance `enemyHealth.aaa` 125 with `bulletDamage` 25); one bomb still destroys it outright. Mission 3 becomes easier on bombs, harder on time under fire | user |
| 2026-11-18 | White only (user decision): the three colour accents (friendly yellow, hostile red, heal green) are removed. Every game element is ink white on paper blue, with pencil for the soft layer; player/enemy/threat/pickup are told apart by shape, motion and line weight (solid player streaks vs dashed enemy tracers, bold health cross, filled vs outline bomb icons, flashing damage warning). Art, FX, HUD and code colours follow | user |
| 2026-11-18 | QA T957 design calls: (1) escaped fighters circle back in while a destroy-all objective still needs them, otherwise they are removed, so mission 1 stays winnable with 37 three-hit fighters; (2) Balance `damageTaken.fighterBullet` 8 -> 5, because missions 4-5 were unsurvivable on paper with 4-5x more fighters; (3) reinforcements enter on the side the player faces | orchestrator |
| 2026-11-18 | Minimap (user request): top-centre strip of the whole mission span with markers for the player, the camera window, live and upcoming enemies, and objectives (diamonds, failAtX line, HQ flag), white only (UI_SPEC §3.3.1) | user |
| 2026-11-20 | Bug fix rule (user): every mission waits for the player's first input (W/A/S/D, fire, bomb) before the background scrolls, the plane moves or enemies spawn | user |
| 2026-11-20 | Bug fix (user: the fighter counter only moved for some planes): reinforcement fighter kills now count toward destroy-all objectives; the required number is unchanged (the mission's listed fighters) | user |
| 2026-11-20 | Damaged enemy fighters trail pencil smoke from the first hit (thicker after the second), using the same smoke-puff effect as the damaged player plane (user request) | user |
| 2026-11-20 | Player plane 50% tougher (user request): Balance `player.resistance` 1.5 divides every non-fatal hit (enemy bullets, bomber gunner, flak, zeppelin turrets, mid-air plane collision); terrain/building/ram crashes stay fatal and health-balloon heals are unchanged. Resulting hits to crash: 3 mid-air plane contacts (was 2: one now costs 33.3%), 30 fighter bullets (was 20), 10 flak (was 7) | user |
| 2026-11-20 | Player plane 30% faster (user request): Balance `player.speed` 5 -> 6.5 u/s; enemy speeds unchanged (fighter 3, bomber 1.5, zeppelin 1). `player.speed` is the climb/dive and post-turn slide rate; the forward cruise speed is each mission's `scrollSpeed`, so (user choice: both) every mission's scrollSpeed also rises 30% (2 -> 2.6, 2.5 -> 3.25, 3 -> 3.9) | user |
| 2026-11-20 | V014: scrollSpeed x1.3 in all missions; mission 5 length 540 -> 580 so the zeppelin still reaches the HQ before the map end. Missions now last about 115/138/128/154/223 s (were about 150-270 s); fighter gaps got shorter, not longer. Lengths otherwise unchanged (pacing kept short unless the user asks for longer missions) | orchestrator |
| 2026-11-20 | Bug fix (user: bombs fell right while facing left): bombs inherit the plane's world velocity with the forward part always along the facing and at least the cruise speed (B057) | user |
| 2026-11-20 | Title screen (user request): the title reads "Biplanes" (the old path lettering read "SIPIAPAE") and gets a blueprint illustration of the player's biplane and an enemy fighter above it | user |
| 2026-11-20 | Enemy fighters always face and chase the player (user request; bombers and zeppelins unchanged): pursuit replaces the fixed straight/sine/dive paths (those become small entry flavours), with a standoff distance, separation and an escort leash; Balance `fighterChase` | user |
| 2026-11-20 | Balance call after B059: at most 3 fighters fire at once (attack slots) and each fighter keeps its own standoff 3.5-6, because in missions 1 and 5 the player (cruise 2.6) can't outrun fighters (3) and a pack of 6 firing together killed the player in about 11 s | orchestrator |
| 2026-11-20 | Health balloons restore 100% of the player's health (user request; was 30%): Balance `healthBalloonRestore` 100. Each balloon is now a full repair, so the mission heal budgets are far above the expected damage; balance by removing balloons if missions feel too easy | user |
| 2026-11-22 | Recharge bomb balloons (user request): new pickup `bombBalloon`, +4 bombs capped at 7 (Balance `bombBalloonGrant`); one placed mid-map in every mission, plus an automatic one `bombBalloonDelay` 4 s after the bay hits zero, spawned `bombBalloonLead` 12 units ahead of the player; re-armed if missed | user |
| 2026-11-22 | Player plane turns around with the same flip animation as the enemy fighters (user request): the sprite squashes edge-on and widens to the new facing over Balance `player.turnSeconds` 0.6, while controls, scrolling, shots and bombs switch at once and the hitbox is unchanged | user |
| 2026-11-22 | Bomber (user request): 1000 health (40 bullets or 10 bombs, was 200), smoke trail that thickens at 70%, 40% and 15% health, and when shot down it explodes and falls to the ground as a harmless wreck with a ground explosion burst; the kill counts the moment it is shot down | user |
| 2026-11-22 | Bomber rework follow-up (orchestrator call): ramming the bomber no longer destroys it, it loses 250 of its 1000 health (about 4 rams, each costing the player the usual collision damage; fighters still die from a ram). Winning by a kill shows the result screen about 2.5 s later (Balance `killWinDelaySeconds`) so the explosion and the falling wreck can be seen | orchestrator |
| 2026-11-22 | Barrage balloons (user request): 100 health (was 50); a collision pops the balloon (scored like a shot kill) and costs the player 33.3% (50 raw / resistance 1.5), no longer an obstacle crash. Fix: balloons were not simulated, so bullets, bombs and contact never hit them in play | user |
| 2026-11-22 | Enemy aircraft never spawn from the left (user request): every fighter, bomber, reinforcement and re-entry enters past the right screen edge flying left, whichever way the player faces; a fighter wholly past the left edge counts as escaped (a needed one re-enters from the right, others are removed); entering fighters get a speed boost equal to the camera's leftward speed until they reach the view, so the 2-second rule still holds | user |
| 2026-11-23 | Mission 5 is 30% shorter (user request): length 580 -> 406 (about 156 s at scroll 2.6, was 223 s); every spawn, the HQ and failAtX (360 -> 252), the zeppelin (atX 420 -> 294) and the balloons scaled by 0.7, same enemy counts and objective; the zeppelin still reaches the HQ about 4-5 s before the map end | user |
| 2026-11-23 | The bomber and the zeppelin hull show a small white damage bar above them while alive, shrinking as they take damage (user request); the zeppelin's bar tracks the hull only, so it stays full while the turrets are being shot | user |
| 2026-11-23 | Damage bar fix (user: it did not seem to update): the zeppelin bar shows overall progress (turrets plus hull) instead of the shielded hull only; the bar is thinner (14 px total on screen at any resolution) and flashes white on every hit | user |
| 2026-11-23 | Hidden cheat code (user request): in mission 5 typing H then Q jumps the plane and camera to the player HQ; passed spawns are skipped except the HQ, zeppelin and escorts; a run that used it never records a best score | user |
| 2026-11-23 | Zeppelin ram is no longer fatal (user request): touching the hull or a turret costs the player 33.3% like other plane contacts (shared 0.5 s grace window, three contacts crash); the zeppelin takes no damage from it | user |
| 2026-11-23 | Zeppelin finale (user request): destroying the last zeppelin turret calls in 4 escort fighters from the right (staggered, within the fighter cap, scored like reinforcements); the zeppelin goes down in a massive chain of about 15 explosions over 1.8 s while it sinks and fades, inside the 2.5 s win delay. No camera shake | user |
