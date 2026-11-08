TASK: T902
AGENT: art
STATUS: DONE
FILES: 
- `art-src/fx/explosion_0.svg` (128×128) through `explosion_5.svg` — 6-frame ink burst, expanding dashed rings from center point to wispy final ring
- `art-src/fx/smoke_puff.svg` (48×48) — pencil-hatched puff with overlapping circles
- `art-src/fx/flak_burst_0..3.svg` (96×96) — 4-frame hostile-tinted (#E4572E) burst sequence
- `art-src/pickups/health_balloon.svg` (70×100) — balloon with heal-colored (#7BD389) cross and wireframe basket
- `art-src/ui/ui_panel.svg` (640×120) — blueprint-styled panel with corner anchors
- `art-src/ui/ui_button.svg` (160×56) — blueprint button with inner wireframe
- `art-src/ui/ui_bomb_icon.svg` (48×64) — cylinder bomb with tail fins and fuse
- `art-src/ui/ui_damage_frame.svg` (320×32) — blueprint damage bar with tick marks
- `art-src/ui/title_logo.svg` (1200×300) — "BIPLANES" built from pure paths on blueprint grid, no font dependency
- `art-src/backgrounds/bg_frontline_far/mid/near.svg` — WWI trench warfare: distant bunkers/trenches, barbed wire, shell craters and parapets
- `art-src/backgrounds/bg_enemyland_far/mid/near.svg` — ruined city skyline with spires/factories/smokestacks, rubble/barricades
- `art-src/backgrounds/bg_stormsky_far/mid/near.svg` — storm clouds, lightning bolts (#F2C14E), heavy rain streaks
- `package.json` — Unity UPM package `com.biplanes.art`, exports all sprite paths, depends on com.unity.ugui
- All 30 corresponding PNGs in `Biplanes/Assets/Biplanes/Resources/Sprites/<category>/`

SUMMARY: Generated all remaining FX (11 files), pickups (1 file), UI (5 files), and three missing background themes (9 files) plus UPM package manifest. All SVG masters pass palette validation, naming conventions, and element-count checks (rasterize.py: 40/40 OK). Background layers are horizontally tileable with matching left/right edge y-values.
VERIFY: `python3 tools/rasterize.py --check` — all new files should report OK. No off-palette colors in any SVG. UI title_logo is 1200×300 with paths-only letterforms (no `<text>` tags). Backgrounds are all 1920×1080 and tileable.
REQUESTS: QA to verify PNG output quality for `title_logo` at 2× scale (2400×600) — cairosvg rendered it successfully but PIL preview could not open it. Also needs Unity import check since these are new categories (fx/, pickups/) with no existing .meta files.
