TASK: T076
AGENT: mechanics
STATUS: DONE
FILES:
  - Biplanes/Assets/Biplanes/Scripts/Missions/TerrainStrip.cs (new)
SUMMARY: TerrainStrip per ARCHITECTURE §3 Missions: Instance, Create(points, length), HeightAt(x) and
         GroundBaseY = -4.5. Ground is drawn as 20-unit runtime-texture chunks (32 px/unit, paper fill,
         2 px pencil ridge, sortingOrder 0). Only chunks in [camX-10, camX+30] exist; one built per frame after Create.
VERIFY: tools/unity.sh compile. Static: compiled with dotnet against Library/ScriptAssemblies (0 errors).
        In play: a ground line rises from x=0 to x=120 in mission 1, and chunk count under TerrainStrip stays at 3.
REQUESTS: qa (T080): EditMode tests for HeightAt (before first, between, after last, no keyframes).
