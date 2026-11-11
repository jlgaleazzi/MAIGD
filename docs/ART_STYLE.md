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

## Pencil background rules
- `stroke="#9DB4CC"`, `stroke-width` 1 to 2, `opacity` 0.6 to 0.9. Use 2 or 3 slightly
  offset overlapping paths per contour to imitate sketching. Light hatching for shading
  (parallel 45° lines). No fills except very faint hatching.
- Backgrounds are horizontally **tileable** layers: `far` (mountains/clouds),
  `mid` (hills/tree lines), `near` (fences/field rows). Each is 1920×1080 with matching
  left/right edges.

## Asset list (name = file name; size in px)
| Category | Name | Size | Notes |
|---|---|---|---|
| player | `player_biplane` | 160×80 | **From `blueprints/airplane_blueprint_256px.png`** via `tools/import_blueprints.py` (not SVG). "U" plus roundel bullseye on the fuselage; the roundel is redrawn ink / paper / ink by the importer |
| player | `player_biplane_frame2` | 160×80 | Propeller animation frame 2. **From the same blueprint** via `tools/import_blueprints.py`: identical to `player_biplane` (same crop and pivot) except the propeller, which is a dashed disc with a short foreshortened blade |
| enemies | `enemy_fighter` | 150×75 | **From `blueprints/fokker_blueprint_transparent_256px.png`** via `tools/import_blueprints.py` (not SVG), mirrored to face right. Iron Crosses on fuselage and tail, redrawn as ink crosses pattee by the importer; line weight normalized to the player |
| enemies | `enemy_bomber` | 320×140 | Twin engine, gunner positions, Iron Crosses |
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
| fx | `explosion_0..5` | 128×128 | 6-frame ink burst, then dissipating dashed rings |
| fx | `smoke_puff` | 48×48 | Pencil-hatched puff |
| fx | `flak_burst_0..3` | 96×96 | Hostile-tinted burst |
| backgrounds | `bg_<theme>_far/mid/near` | 1920×1080 | Themes: `farmland` (M1–2), `frontline` (M3), `enemyland` (M4), `stormsky` (M5) |
| ui | `ui_bomb_icon`, `ui_damage_frame`, `ui_panel`, `ui_button` | various | Blueprint-styled |
| ui | `title_logo` | 1200×300 | "BIPLANES" in blueprint stencil lettering, built from paths (no font dependency) |

## Blueprint-sourced sprites
Some sprites come from the user's line-art blueprint PNGs in `blueprints/` instead of an
SVG master. `tools/import_blueprints.py` (its `SPRITES` table is the list) recolours the
lines to `ink`, mirrors drawings that face left, optionally thickens thin lines, and fits
them to the asset-list size with 2 px padding. Currently:
- `player_biplane` <- `blueprints/airplane_blueprint_256px.png`
- `player_biplane_frame2` <- the same blueprint, propeller erased and redrawn
- `enemy_fighter` <- `blueprints/fokker_blueprint_transparent_256px.png` (mirrored,
  alpha gain 2.2)

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
