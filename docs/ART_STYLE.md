# Biplanes: Art Style Guide ("Blueprints")

Owner: art agent. Per the GDD: duotone. Main elements (hero, enemies, targets) are crisp
**wireframes**. Backgrounds are looser **pencil illustration**.

## Palette (duotone plus one accent per team)
| Token | Hex | Use |
|---|---|---|
| `paper` | `#0F2A4A` | Blueprint background fill (deep blueprint blue) |
| `paper-grid` | `#1C3F66` | Blueprint grid lines, 1px, 32px spacing |
| `ink` | `#E8F1FA` | All wireframe strokes (near-white chalk) |
| `pencil` | `#9DB4CC` | Background pencil strokes (mountains, buildings, clouds) |
| `friendly` | `#F2C14E` | Player bullets only |
| `hostile` | `#E4572E` | Enemy tracers and flak bursts only |
| `heal` | `#7BD389` | Health balloon cross only |

Don't use any color outside this table. Accents are used only for projectiles, flak and
the health cross, so the screen stays duotone. **Faction marks are ink white** (user
decision, T918).

## Blueprint detail style (sprites)
This replaces the old wireframe stroke rules (3 / 1.5 px). The target look is the user's
blueprints in `blueprints/`: dense technical line art. It applies to SVG masters and to
blueprint-sourced sprites alike.
- SVG, `viewBox` in pixels at **100 px = 1 Unity unit**. Transparent background.
- Line widths **at final sprite scale**: outline about **1.25 px**, structure about
  **0.75 px** (struts, ribs, panel lines, wires), fine detail about **0.5 px**. Strokes are
  ink `stroke="#E8F1FA"`, `fill="none"`, `stroke-linecap="round"`,
  `stroke-linejoin="round"`. Fine detail may use `opacity` 0.6 to 0.9.
- Show the construction detail: panel lines, rivet dots, ribs, struts and bracing wires,
  wheel spokes and hubs, hatching for shading, and small serial or label marks drawn as
  tiny stroked paths (no `<text>`). Propeller: a dashed disc plus a blade line.
- Dimension and callout lines are optional on large sprites (bomber, zeppelin).
- **No 3 px strokes, and no fills** except faction marks (ink) and accents.
- Accents (only these): `friendly` #F2C14E for player bullets, `hostile` #E4572E for
  enemy tracers and flak, `heal` #7BD389 for the health cross.
- Faction marks are **ink white**: player roundel = bullseye ink / paper / ink; enemy
  Iron Cross = a true cross pattee (arms narrow at the centre, flared at the ends) filled
  ink, on the fuselage and on the tail where visible.
- Line-weight check: blueprint-sourced sprites are normalized to the same stroke metric
  (see "Blueprint-sourced sprites"); SVG sprites should look the same weight beside them.
- **Every aircraft faces right** (+x). Code flips enemies that fly left.
- Pivot: the sprite's visual center. Keep 8px of padding inside the viewBox.

## Engraved-sketch background rules
Technique source: old pen-and-ink engraving technique, Earth Atlantis-like (technique
only; all subjects are original WWI scenes). Light lines on dark blue paper.
- **Tone comes from line**, never flat fills: hatching that follows the form, cross-hatching
  in shadows; shadow sides dense, lit sides sparse or empty. Paper-coloured fills are
  allowed only to hide what is behind a shape (occlusion).
- **Depth by fading:** distant subjects are big, simple, faint and barely outlined; near
  subjects are small-scale, dense, high-contrast and heavily outlined.
- **Cellular masses** in the foreground (stones, bricks, rubble, sandbags): each cell
  outlined and hatched on its own, hatch angle varying cell to cell. Lattice girders where
  the theme suits. Grass and foliage as tufts of tapering strokes.
- Palette: `paper`, `paper-grid`, `pencil`, `ink` only. No accents in backgrounds.

| Layer | Base | Subjects | Strokes |
|---|---|---|---|
| `far` | The **only opaque layer**: paper `<rect>`, faint 32 px paper-grid grid (opacity ≤ 0.5), 3–6 faint stains (paper-grid, opacity ≤ 0.35) | Sky and horizon: large faded silhouettes, engraved clouds | pencil / paper-grid at 0.2–0.45, 0.75–1 px, mostly hatching |
| `mid` | **Transparent** (no full rect) | Middle distance, y ≥ 480 | pencil at 0.4–0.7, outlines 1–1.5 px, single and cross hatching |
| `near` | **Transparent** (no full rect) | Foreground band, y ≥ 760 | pencil at 0.6–0.85, ink at most 0.45, outlines 1.5–2 px, dense cross-hatching and cellular masses |

Draw order far -30, mid -20, near -10; scroll speeds 0.1 / 0.3 / 0.6. mid and near must be
transparent outside their content (check the PNG alpha).

**Readability (hard rules):** no full-opacity ink anywhere (sprites are ink at 1.0, 1.25 px);
keep the flight band y 120–700 quiet in mid and near (far may rise into it, faintly); the
terrain strip covers about the bottom 50 px; nothing that could pass for gameplay (planes,
balloons, guns, crosses, roundels, zeppelins).

**Tiling (hard rule):** path y-values at x=0 and x=1920 match; a shape crossing an edge is
drawn twice, shifted by ±1920; `<pattern>` tiles use `patternUnits="userSpaceOnUse"` and a
width that divides 1920 (4, 6, 8, 12, 16, 32 …) so hatching meets at the seam.

**SVG technique:** hatch patterns at light 7–8 px, medium 4–5 px, dense cross 3–4 px; apply
with `fill="url(#id)"` or a `<clipPath>`; reuse cells and tufts with `<defs>` + `<use>`;
round caps and joins; no `<text>`, no filters; under 400 elements per file. Self-review:
composite far+mid+near with sprites at game scale, check each layer side by side for
seams, and look at near at 1:1.

## Pencil FX (explosions, smoke, flak)
FX are the one in-world exception to crisp wireframes: they are hand-drawn pencil sketches
(T944). Planes and targets stay crisp. Masters are generated by
`art-src/fx/gen_pencil_fx.py` (seeded; edit it, re-run it, then `rasterize.py`).
- **Contour:** loose, drawn 2–3 times with ±1–2 px jitter: 1.25 px at full opacity, then
  0.75 px at ~0.6, then 0.5 px at ~0.4. Never one clean line.
- **Billows:** cauliflower lobes (each chord bulges outward), plus short inner C-arcs for
  billows inside the mass. Bursts use a spiky star outline with slightly curved edges.
- **Shading by strokes only:** 45° hatching clipped to the shape, cross-hatched at −45°
  at the core/underside (spacing 2.5–3.5 px dense, 4.5–6 px light). No fills.
- **Wisps:** wavy S-curves (amplitude ≤ 0.3 × wave length), never straight lines.
- **Debris:** small stroked 3–4 sided shards with two short motion lines toward the centre.
- **Colour:** ink `#E8F1FA` for bright/near strokes, pencil `#9DB4CC` for soft smoke and
  dissipating frames, opacity 0.35–1. Only flak may use `hostile` `#E4572E` (flash, hot
  core, embers). Explosions use no accents.
- Keep ~6 px padding; transparent background; check at 1:1 on `paper`.

## Asset list (name = file name; size in px)
| Category | Name | Size | Notes |
|---|---|---|---|
| player | `player_biplane` | 160×80 | **From `blueprints/airplane_blueprint_256px.png`** via `tools/import_blueprints.py` (not SVG). "U" plus roundel bullseye on the fuselage; the roundel is redrawn ink / paper / ink by the importer |
| player | `player_biplane_frame2` | 160×80 | Propeller animation frame 2. **From the same blueprint** via `tools/import_blueprints.py`: identical to `player_biplane` (same crop and pivot) except the propeller, which is a dashed disc with a short foreshortened blade |
| enemies | `enemy_fighter` | 150×75 | **From `blueprints/fokker_blueprint_transparent_256px.png`** via `tools/import_blueprints.py` (not SVG), mirrored to face right. Iron Crosses on fuselage and tail, redrawn as ink crosses pattee by the importer; line weight normalized to the player |
| enemies | `enemy_bomber` | 320×140 | **From `blueprints/enemy_bomber_blueprint_clean_256px.png`** via `tools/import_blueprints.py` (not SVG), mirrored to face right. Two-seat biplane with a rear gunner's gun; Iron Crosses on fuselage and tail, redrawn as ink crosses pattee by the importer; serial "VII" trimmed to "V" (mirroring would read "IIV"); line weight normalized to the player |
| enemies | `zeppelin_hull` | 900×220 | Rib lines, gondola, Iron Cross on the tail fin |
| enemies | `zeppelin_turret` | 60×40 | Separate sprite, mounted on the hull |
| enemies | `aaa_gun` | 100×80 | Ground gun with an angled barrel |
| obstacles | `barrage_balloon` | 90×140 | Tether line to the bottom |
| obstacles | `building_barn`, `building_house` | 140×110 | Wireframe (collidable) |
| obstacles | `tree_a`, `tree_b` | 80×120 | Wireframe (collidable) |
| targets | `enemy_base` | 260×120 | Hangar plus flag, hostile mark |
| targets | `airfield`, `player_hq` | 400×100 | Friendly mark |
| pickups | `health_balloon` | 70×100 | heal-colored cross |
| projectiles | `bullet`, `enemy_bullet`, `bomb`, `flak_shell` | 16–24 | Accent colored |
| fx | `explosion_0..5` | 128×128 | Pencil FX (section above). 0 spiky flash + rays + debris (FxManager plays all 6 frames at 0.08 s each, B030), 1–3 growing hatched billows, 4–5 broken contour and wavy wisps. Ink/pencil only, no accents |
| fx | `smoke_puff` | 48×48 | Pencil FX: one lobed puff, sketchy double contour, hatched underside, two small wisps |
| fx | `flak_burst_0..3` | 96×96 | Pencil FX: 0 hostile hatched star flash + ink rays, 1–2 ink billows with a hostile hatched hot core, 3 broken cloud with a few hostile embers |
| backgrounds | `bg_<theme>_far/mid/near` | 1920×1080 | Engraved sketch, tileable; only `far` opaque, `mid`/`near` transparent. Themes: `farmland` (M1–2), `frontline` (M3), `enemyland` (M4), `stormsky` (M5) |
| ui | `ui_bomb_icon`, `ui_damage_frame`, `ui_panel`, `ui_button` | various | Blueprint-styled |
| ui | `title_logo` | 1200×300 | "BIPLANES" in blueprint stencil lettering, built from paths (no font dependency) |

## Blueprint-sourced sprites
**Aircraft backing:** every plane sprite (the blueprint sprites, and SVG aircraft listed
in `rasterize.py` `BACKED`, currently none) gets an opaque `paper` silhouette
under its lines (`import_blueprints.add_backing`), so no background shows through the
plane. The silhouette is the drawing closed by 7 px, which bridges the gaps between
wings and struts, with its holes filled. The lines are unchanged.

Some sprites come from the user's line-art blueprint PNGs in `blueprints/` instead of an
SVG master. `tools/import_blueprints.py` (its `SPRITES` table is the list) recolours the
lines to `ink`, mirrors drawings that face left, optionally thickens thin lines, and fits
them to the asset-list size with 2 px padding. Currently:
- `player_biplane` <- `blueprints/airplane_blueprint_256px.png`
- `player_biplane_frame2` <- the same blueprint, propeller erased and redrawn
- `enemy_fighter` <- `blueprints/fokker_blueprint_transparent_256px.png` (mirrored,
  alpha gain 2.2)
- `enemy_bomber` <- `blueprints/enemy_bomber_blueprint_clean_256px.png` (mirrored,
  alpha gain 0.65: its 1 px lines are fully opaque, so they are dimmed to the metric)

Their old SVG masters are retired in `art-src/_retired/` (skipped by `rasterize.py`). A
sprite in the `SPRITES` table must not also have an SVG in `art-src/`.

**Mark colouring.** Each `SPRITES` entry has a `marks` list in *source-pixel*
coordinates of the blueprint (before mirroring): `roundel` (centre, radius), `cross`
(centre, arm length, half-width at centre and at the arm end) and `prop` (propeller disc
for animation frames). The importer draws them vector-style at final size (8x
supersampled) on top of the line art: opaque fills (ink / paper / ink bullseye; ink
cross, colours set by `MARK_FRIENDLY` / `MARK_HOSTILE`) with a 1.25 px ink outline, so the blueprint's own white mark is
covered. No PNG is hand-painted; re-running the importer reproduces every sprite. An
`erase` list (source-px boxes) removes parts before the overlay, for example the blurred
propeller in `player_biplane_frame2`. Frames of one blueprint share its crop box, so they
line up pixel for pixel.

**Line-weight normalization.** `python tools/import_blueprints.py --metrics` prints, per
sprite at final size with the marks excluded: the mean effective stroke width (every
horizontal and vertical run of line pixels with alpha >= 15% and at most 4 px long is a
cross-section; width = sum(alpha) / 255) and the mean alpha of line pixels. Every
blueprint sprite must be within 10% of `player_biplane` on both (the reference: about
0.89 px and 0.44). Tune a sprite with `gain` (alpha multiplier, preferred: keeps lines
crisp) and only then `grow` (max-filter widening in source px, which gets blobby fast).

## Pipeline
1. Write the SVG to `art-src/<category>/<name>.svg`.
2. Run `python tools/rasterize.py` (renders every changed SVG to
   `Biplanes/Assets/Biplanes/Resources/Sprites/<category>/<name>.png` at 1×, and at 2× for UI).
3. Check the sprite sheet preview `docs/art/preview.png` the script writes. Open it and
   look at it if your model can read images. Otherwise check the script's report.
4. Blueprint-sourced sprites: run `python tools/import_blueprints.py` (`rasterize.py`
   also runs it, so `python tools/rasterize.py --check` covers every sprite). Add
   `--metrics` to check the line-weight normalization.

## Sound (placeholder SFX, M6)
Design intent and cue list: GDD Style Guide, "Sound effects". Every clip is generated by
`tools/synth_sfx.py` (Python standard library only, seeded per clip, byte-identical on
re-run). Never hand-place a WAV; real recordings may later replace a clip under the same name.
- **Regenerate:** `python tools/synth_sfx.py` writes
  `Biplanes/Assets/Biplanes/Resources/Audio/sfx/<name>.wav`. `--check` regenerates in
  memory, compares with disk and validates, writing nothing (exit 1 on any error).
  `--only name ...` limits the run. The script prints a per-clip report (duration, peak and
  RMS dBFS, DC, first/last sample, loop seam jump, low <300 Hz / mid / high >3 kHz energy %).
- **Format:** mono, 16-bit PCM, 44.1 kHz. Gameplay peak -3 dBFS; UI cues (`ui_*`,
  `typewriter`) peak -13 dBFS (about 10 dB quieter); `stamp` and the muted `lose` -6 dBFS.
  One-shots: 20 Hz DC blocker, 2 ms fade-in, 10 ms fade-out (first/last sample 0). Loops are
  synthesised circularly (events wrap, noise filtered twice round the ring, tones with whole
  periods) so the last sample runs into the first.

| Clip | Length | Recipe |
|---|---|---|
| engine_loop | 1.5 s loop | 9-cyl rotary: 54 noise pops at 36 Hz (band-pass 650 Hz + 1.8 kHz bark), 118 Hz body per pop, prop-wash noise, faint 36/72 Hz drone |
| player_gun | 0.13 s | high-passed noise crack + metal ring 2.25/3.4/5.15 kHz + small thump + bolt tick at 45 ms |
| enemy_gun | 0.2 s | same family, ring 1.35/2.05/2.9 kHz, low-passed 2.6 kHz, early reflections (distance) |
| bomb_release | 0.45 s | double latch tick, then whistle 1.9 -> 1.15 kHz with breathy noise |
| bomb_impact | 0.5 s | sine 75 -> 34 Hz + low-passed noise + wood modes + dirt crackle |
| hit_enemy | 0.1 s | metallic tick 3.25/4.75 kHz + 45 ms grainy canvas rip (bright, light) |
| hit_player | 0.3 s | low-passed crack + wood modes 225/470/910 Hz + 115 Hz thump + pencil scratch at 60 ms |
| flak_fire | 0.35 s | dull pop: sine 125 -> 55 Hz + 600 Hz noise + low echoes |
| flak_burst | 0.45 s | sharp high-passed crack + 95 Hz body + 800 Hz air puff + short crackle |
| explosion | 1.0 s | boom 62 -> 30 Hz + 320 Hz rumble + paper-crumple crackle tail (2.1 kHz) |
| explosion_big | 2.6 s | four staggered booms (rolling), 140 Hz rumble bed, 2.3 s crumple tail |
| zeppelin_hum | 2.0 s loop | 15 Hz engine thuds (46/92 Hz) + beating 55/55.5 Hz hum + 150 Hz rumble |
| smoke_crackle | 1.5 s loop | sparse crackle grains (2.6 kHz) + 4.2 kHz scribble hiss in whole-period strokes |
| crash | 1.0 s | landing crunch only (the engine loop does the whine-down): 0.1 s rising wind whistle, crunch at 0.10 s (dirt thump 82 -> 30 Hz + 320 Hz dirt noise + wood splinter + wood modes 230/410/760 Hz + metal 1.17/1.74/2.63 kHz), two wood creaks (0.36 s, 0.58 s), debris + paper-crumple fade to silence by ~0.9 s |
| pickup | 0.45 s | soft wood tap (520/1340 Hz) + G5/D6 fifth chime with a G4 under it |
| damage_warning | 0.55 s | one bell at 1046 Hz, inharmonic partials (0.5, 1.19, 1.5, 2, 2.51, 2.66, 3.01) |
| ui_click | 0.05 s | pencil tick: high-passed click + tiny 2.5 kHz ring |
| ui_page | 0.25 s | fluttering 2.6 kHz paper rustle swell + soft low flap |
| ui_pause | 0.12 s | muffled hush: 900 Hz low-passed noise swell |
| typewriter | 0.08 s | key clack: click + typebar ring 1.15/2.65/4.1 kHz + small thud |
| stamp | 0.35 s | dull thud 92 -> 55 Hz + low noise + paper slap |
| win | 2.0 s | snare roll crescendo, brass G4 C5 E5 -> G5/C5/C4 chord, accent + stamp thud |
| lose | 2.3 s | muted brass (low-passed 1.1 kHz) G4 F4 Eb4 -> C4 drooping 3.5%, soft thud |
