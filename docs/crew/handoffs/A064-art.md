TASK: A064
AGENT: art
STATUS: DONE
FILES: art-src/pickups/bomb_balloon.svg (new), Biplanes/Assets/Biplanes/Resources/Sprites/pickups/bomb_balloon.png (new, via rasterize.py; .meta left to Unity), docs/ART_STYLE.md (asset-list row), docs/art/preview.png (regenerated contact sheet)
SUMMARY: 70x100 bomb_balloon with the same envelope, rigging, basket and canvas as health_balloon, so the same collider fits. In place of the cross it has a bold ink aerial bomb pointing nose down (group id="bomb_mark"): a paper knock-out with a 1.25 px ink outline, a solid ink teardrop body, solid V tail fins, a centre fin and a nose fuse. A zigzag hazard band under the lower seam and a two-tick basket label set it further apart from health_balloon. It is white only, with no text.
After looking: round 1 looked like a vase or amphora (straight neck and box fins), so I changed the tail to flared V fins and removed the belly band. Round 2's fins were too thin to see at half size, so round 3 made them solid ink. Now the round body and V fins read as a bomb at 1x and 0.5x and look clearly different from the cross.
VERIFY: python tools/rasterize.py --check (42/42 OK, 5/5 blueprint OK); python -m tools.style_loop audit (pickups/bomb_balloon clean, 0 violations). Check that Resources.Load<Sprite>("Sprites/pickups/bomb_balloon") resolves after Unity imports the PNG.
REQUESTS: none
