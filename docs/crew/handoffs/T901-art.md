TASK: T901
AGENT: art
STATUS: DONE
FILES: 
  - art-src/enemies/enemy_fighter.svg (150×75)
  - art-src/enemies/enemy_bomber.svg (320×140)
  - art-src/enemies/zeppelin_hull.svg (900×220)
  - art-src/enemies/zeppelin_turret.svg (60×40)
  - art-src/enemies/aaa_gun.svg (100×80)
  - art-src/obstacles/barrage_balloon.svg (90×140)
  - art-src/obstacles/building_barn.svg (140×110)
  - art-src/obstacles/building_house.svg (140×110)
  - art-src/obstacles/tree_a.svg (80×120)
  - art-src/obstacles/tree_b.svg (80×120)
  - art-src/targets/enemy_base.svg (260×120)
  - art-src/targets/airfield.svg (400×100)
  - art-src/targets/player_hq.svg (400×100)
SUMMARY: Generated 13 SVG masters and rasterized PNGs for all enemy aircraft, ground obstacles, and terrain targets. All follow duotone wireframe rules (#E8F1FA strokes on transparent background). Iron Cross faction marks (hostile #E4572E) on fighters, bombers, zeppelins, and enemy base. Bullseye roundel (friendly #F2C14E) on airfield and player_hq. Blueprint dimension callouts on bomber and zeppelin.
VERIFY: 
  1. Run `python3 tools/rasterize.py --check` — all files should pass palette + size checks.
  2. Verify enemy aircraft face right (+x) in SVG (propeller on right edge). Code will flip enemies flying left.
  3. Check PNGs exist at `Biplanes/Assets/Biplanes/Resources/Sprites/<category>/<name>.png`.
  4. Visual spot-check: zoom into zeppelin_hull.png to confirm rib lines and gondola are legible at small size.
REQUESTS: none
