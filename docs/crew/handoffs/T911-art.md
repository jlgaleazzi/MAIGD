TASK: T911
AGENT: art
STATUS: DONE
FILES: art-src/obstacles/barrage_balloon.svg, art-src/obstacles/building_barn.svg, art-src/obstacles/building_house.svg, art-src/obstacles/tree_a.svg, art-src/obstacles/tree_b.svg, art-src/pickups/health_balloon.svg, docs/crew/handoffs/T911-art.md (plus the six PNGs rendered by tools/rasterize.py under Biplanes/Assets/Biplanes/Resources/Sprites/obstacles/ and pickups/)
SUMMARY: Redrew all six in blueprint detail style. Line weights are 1.25 outline, 0.75 structure and 0.5 fine detail. Each sprite uses shared <defs>/<pattern>/<use> elements, 45-70 elements per file, and keeps its old size.
  - barrage_balloon: gores, seam bands, rivet dots, crown valve, mirrored fins and a ventral fin, rigging to a load ring, ticked tether and winch block.
  - barn: gambrel front with shingle-butt band, board siding, frame posts and girts, Z-braced double doors, loft door, windows, lean-to and eave hatching.
  - house: half-timbered, with hipped shingle roof, dormer, brick chimney, shuttered windows, plank door and stone plinth.
  - trees: fir with branch whorls and hatched crown; oak with limb structure and a scalloped, hatched canopy.
  - health_balloon: gores, rigging, wicker basket, and a #7BD389 cross outlined in ink.
  Self-review: compared with the player and Fokker sprites on #0F2A4A at 1x, 2x and 3x, over 2 rounds. Detail density and line weight match, the silhouettes read at 1x, and nothing is clipped (bboxes keep 4+ px padding).
  - Fixes after looking: removed the stray height-dimension line on tree_a because it read as a separate object at 1x; pulled tree_a's tip in to 4 px padding; re-seated the fir whorls so the limbs end at the tier tips, and dropped the skirt lines that made the tiers look like umbrella ribs.
VERIFY: python tools/rasterize.py --check  (expect the six obstacles/pickups lines OK; 41/41 OK at time of handoff). Optionally composite Sprites/obstacles/*.png and Sprites/pickups/health_balloon.png next to player/player_biplane.png on #0F2A4A.
REQUESTS: none. Note: the barn and house carry tiny label marks ("->2", "H3") just outside the walls, and the tree labels sit beside the trunks. If colliders are fit to visible pixels rather than to the fixed sprite size, mechanics may want to ignore those few px.
