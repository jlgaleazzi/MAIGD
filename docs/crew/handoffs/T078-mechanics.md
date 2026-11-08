TASK: T078
AGENT: mechanics
STATUS: DONE
FILES:
  - Biplanes/Assets/Biplanes/Scripts/Missions/ParallaxBackground.cs (new)
  - Biplanes/Assets/Biplanes/Resources/Balance.json
  - Biplanes/Assets/Biplanes/Scripts/Core/GameBalance.cs
SUMMARY: ParallaxBackground loads bg_{theme}_far/mid/near; 2 tiles per layer placed in LateUpdate with
         offset = Repeat(camX*f, w). Sorting -30/-20/-10. Missing layer = one warning, skipped. Balance.json
         gains background {farFactor 0.1, midFactor 0.3, nearFactor 0.6}, mirrored by GameBalance.BackgroundData.
VERIFY: tools/unity.sh compile. In play: three layers scroll at different speeds with no gaps at tile seams.
REQUESTS: qa (T080): EditMode test for the offset formula (tiles cover [camX-8.9, camX+8.9] for any camX).
