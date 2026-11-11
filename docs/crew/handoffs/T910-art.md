TASK: T910
AGENT: art
STATUS: DONE
FILES: art-src/enemies/aaa_gun.svg, art-src/enemies/zeppelin_hull.svg, art-src/enemies/zeppelin_turret.svg, docs/crew/handoffs/T910-art.md (PNGs re-rendered by tools/rasterize.py: Biplanes/Assets/Biplanes/Resources/Sprites/enemies/{aaa_gun,zeppelin_hull,zeppelin_turret}.png)
SUMMARY: Redrew all three in the blueprint detail style (1.25/0.75/0.5 px ink strokes, rivets, hatching, stroked serial marks, filled hostile Iron Crosses). AAA gun has a spoked wheel, trail with spade, outrigger, breech block, recoil cylinder and shield. Zeppelin has ring frames, girders, gas cells, 3 gondolas, cruciform tail with 2 crosses and a dimension line. Turret is a saddle and ring mount with an MG. Sizes unchanged (100x80, 900x220, 60x40).
After looking at the PNGs I moved the AAA handwheel off the breech and enlarged its shield cross, pulled the zeppelin's dimension and callout marks inside the 8 px padding, and scaled the turret gun up 10% so it reads at 1x.
VERIFY: python tools/rasterize.py --check  (aaa_gun, zeppelin_hull, zeppelin_turret report OK; element counts are 136/187/69, all under 400). Composite the three PNGs on #0F2A4A next to player_biplane.png and enemy_fighter.png at 1x and 3x.
REQUESTS: none. Note: the zeppelin SVG was generated with a small script that is not in the repo. Its coordinates are plain SVG, so the file can be edited by hand. The turret's base is a curved saddle with its bottom at about y=37, so mechanics may want to check where it sits on the hull. enemy_bomber.svg was not touched.
