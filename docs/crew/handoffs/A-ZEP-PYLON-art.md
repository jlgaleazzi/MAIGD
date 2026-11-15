TASK: A-ZEP-PYLON
AGENT: art
STATUS: DONE
FILES:
- art-src/enemies/zeppelin_hull.svg (new `<g id="pylon">`, 11 elements, placed before the control car)
- Biplanes/Assets/Biplanes/Resources/Sprites/enemies/zeppelin_hull.png (re-rasterized, still 900x220)
SUMMARY:
- Source check: zeppelin_hull.png comes from the SVG. tools/import_blueprints.py only handles the player biplane, the fighter and the bomber, and blueprints/ has no zeppelin. So the PNG was regenerated from the SVG.
- The pylon is a tapered strut fairing. It runs from a hull doubler plate (x 368-402, y 174/176.5) down to a foot plate (x 376-394, y 201-205.3). The side members go from x 373/397 at y 174 to x 380/390 at y 201, at 1.25 px. There is a mid rib at y 187 (0.75), X bracing (0.5, opacity 0.7) and 5 rivet dots. It is ink only, in the hull's line weights, with no fills.
- After looking: I composited the flipped turret at hull px (385, 220), spanning y 200-240, over the rendered hull. In round 1 the foot plate ended at y 203.5, which left a visible gap above the centre of the turret saddle (the flipped saddle top curves from y 203 at its ends to about 206 at its centre). In round 2 I lowered the foot to y 201-205.3 with a curved underside and moved the foot rivets to y 203.2. The pylon now visibly seats on the saddle.
- Canvas: the pylon ends at y 205.3, inside the 220 px canvas, with about 15 px to spare. The turret's overhang below y 220 belongs to the separate turret sprite, so nothing in the hull clips it. The pylon is clear of the aft engine car (x 252-314) and the midships car (x 462-534).
VERIFY:
- python tools/rasterize.py -> "OK art-src\enemies\zeppelin_hull.svg", 40/40 OK, 4/4 blueprint OK. The PNG size is still (900, 220).
- Palette: the only new colour is #E8F1FA (ink).
- In Unity, playtest M5: the underside turret should hang from the pylon foot with no gap. If the turret's saddle sits more than about 2 px off (the turret pivot is not the sprite centre), adjust the foot y in `#pylon`.
REQUESTS:
- none
