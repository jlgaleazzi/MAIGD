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
| `friendly` | `#F2C14E` | Player bullseye fill and player bullets only |
| `hostile` | `#E4572E` | Iron Cross, enemy tracers, flak bursts only |
| `heal` | `#7BD389` | Health balloon cross only |

Don't use any color outside this table. Accents are used only for faction marks and
projectiles, so the screen stays duotone.

## Wireframe rules (sprites)
- SVG, `viewBox` in pixels at **100 px = 1 Unity unit**. Transparent background.
- Strokes: `stroke="#E8F1FA"`, `fill="none"`, `stroke-width` 3 (outline), 1.5 (inner
  structure lines such as struts, ribs and wires), `stroke-linecap="round"`,
  `stroke-linejoin="round"`.
- Show the construction: wing ribs, struts, bracing wires and propeller disc as dashed
  circles (`stroke-dasharray="4 4"`). Add 1 or 2 small dimension-line callouts on large
  sprites (bomber, zeppelin) for the blueprint flavor.
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
| player | `player_biplane` | 160×80 | Bullseye roundel on the fuselage center (friendly fill) |
| player | `player_biplane_frame2` | 160×80 | Propeller animation frame |
| enemies | `enemy_fighter` | 150×75 | Iron Cross on fuselage and tail (hostile) |
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

## Pipeline
1. Write the SVG to `art-src/<category>/<name>.svg`.
2. Run `python tools/rasterize.py` (renders every changed SVG to
   `Biplanes/Assets/Biplanes/Resources/Sprites/<category>/<name>.png` at 1×, and at 2× for UI).
3. Check the sprite sheet preview `docs/art/preview.png` the script writes. Open it and
   look at it if your model can read images. Otherwise check the script's report.
