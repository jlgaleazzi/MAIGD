TASK: T908
AGENT: art
STATUS: DONE
FILES: docs/ART_STYLE.md, docs/crew/handoffs/T908-art.md (tools/import_blueprints.py reviewed and left unchanged; its re-run rewrote player_biplane.png and enemy_fighter.png with identical settings)
SUMMARY: Reviewed both imported sprites on #0F2A4A at 1x and 4x: both face right, nothing is clipped, the 1x lines read, and the roundel/"UO" and both Iron Crosses are visible (the tail cross is faint at 1x). No setting changes were needed. ART_STYLE.md now has a "Blueprint-sourced sprites" section, updated asset rows for player_biplane, enemy_fighter and player_biplane_frame2, and a pipeline step.
  Review findings (no fix applied):
  - enemy_fighter is off-centre: the Fokker source has 16 px of empty margin on its propeller side, so after the mirror the art spans x 4..140 of 150 (about 6 px left of centre, against the "pivot = visual centre" rule). It also renders smaller than it could (136 px wide vs 156 for the player).
  - Mirroring reverses the small "L-03" serial on the Fokker. It's barely legible at 1x, so this is cosmetic.
  - The Fokker stays visibly fainter than the player even with thicken on. This is acceptable, but it's the first thing to revisit if enemies read poorly in play.
  Effect on other assets:
  - player_biplane_frame2.svg (T006 propeller frame, not yet used by code) is still the old SVG style and won't match. It needs redoing from the blueprint (for example a second blueprint with a different propeller blur, or an importer variant).
  - enemy_bomber, zeppelin, aaa_gun and the other aircraft are still SVG wireframes. The blueprint sprites are denser and more detailed, so the styles now mismatch on screen. User decision needed: supply more blueprints (bomber first), or have art restyle the SVGs toward the blueprint look.
  Open style question: faction marks are ink white, not friendly #F2C14E / hostile #E4572E as ART_STYLE's palette rules intend. They weren't hand-painted. Decide whether the importer should tint the mark regions (for example via a per-sprite mask) or whether ink-only marks become the rule.
VERIFY: python tools/import_blueprints.py  (expect 2/2 OK); python tools/rasterize.py --check  (expect 42/42 OK, including the 2/2 blueprint lines). Optionally open Biplanes/Assets/Biplanes/Resources/Sprites/{player/player_biplane,enemies/enemy_fighter}.png on a #0F2A4A background.
REQUESTS: orchestrator (tools/ owner): in tools/import_blueprints.py build(), crop the source to its alpha bbox (im = im.crop(alpha.getbbox()) after the flip/thicken) before fitting, so enemy_fighter is centred and about 10% larger. This is outside the SPRITES/thicken/padding settings delegated for T908, so I didn't make it. User/orchestrator: decide on more blueprints vs SVG restyle, redoing player_biplane_frame2, and accent-coloured faction marks.
