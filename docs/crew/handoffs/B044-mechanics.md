TASK: B044
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/UI/MinimapView.cs (new)
- Biplanes/Assets/Biplanes/Scripts/UI/HudView.cs (builds the minimap; adds `Minimap`, `LeftReserve`, `RightReserve`)
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs (read-only accessors, no behaviour change)
- Biplanes/Assets/Biplanes/Tests/EditMode/MinimapTests.cs (new)
- docs/crew/handoffs/B044-mechanics.md
SUMMARY:
- `MinimapView.Create(parent, leftReserve, rightReserve)` is called from HudView.BuildVisuals. It builds a top-centre strip 12 px from the top: 480x44, paper at 70% alpha, 1 px sliced ink border. Layers, back to front: pencil terrain polyline (HeightAt every 5 u, 8 to 120 segments, built once per mission, terrain or width change); upcoming enemy ticks (pencil at 40% alpha, at atX on the bottom edge, gone once spawned); dashed ink failAtX lines and 6 px ink HQ flags; pencil camera bracket; ground enemies (AAA tick on the terrain line, barrage balloon ring, 6 px enemy-base square, all pencil); aircraft (fighter and reinforcement 3 px ink dot, bomber 5x3, zeppelin 12x4); 9 px hollow ink diamonds; 7 px ink player triangle on top (0 or 180 degrees from Facing). Health balloons, buildings and trees are not drawn.
- The strip is visible only in Playing or Paused. It finds GameManager once with FindAnyObjectByType and gets the runner from GetComponentInParent once. It refreshes at 10 Hz on unscaled time. Markers are pooled Images: Begin/Next/End, and unused ones are deactivated, never destroyed. The 7 procedural white sprites are cached statically. raycastTarget is off everywhere. Diamonds and failAtX lines come only from unevaluated objectives while `runner.ObjectivesOpen`, so a completed objective or an ended mission clears them.
- The frame width is `FrameWidth(canvasW, 232, 316)`: up to 480, shrunk so the centred frame keeps 12 px clear of the damage frame and the score/objective column. It is 480 at 1920, 1728, 1680, 1440 and 1280 px wide, and 368 at 1024. MissionRunner gains `GroundEnemies` (AAAGun, BarrageBalloon and EnemyBase, registered at spawn and cleared in teardown and OnDestroy), `MissionLength`, `SpawnEntryCount` and `IsSpawnPending(i)`.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes (+1 new file not yet in the csproj), PASS Biplanes.Editor, exit 0. I didn't run `tools/unity.sh` because the editor has the project open.
- Tests compiled in a scratch build (B018 method), not run:
  - The runtime DLL was built with compile_check's helpers.
  - A temp copy of Biplanes.Tests.EditMode.csproj was retargeted v4.7.1 -> v4.7.2, with all 30 Tests/EditMode/*.cs listed explicitly.
  - Result: 0 errors. The temp csproj files were removed, and no `zz_*` files are left.
- With Unity closed, run `bash tools/unity.sh test`. The new tests are in MinimapTests:
  - MapX_SpansZeroToLength_AndClamps
  - MapY_SpansTheFlightBand_AndClamps
  - FacingAngle_RightIsZero_LeftIs180
  - VisibleOnlyWhilePlayingOrPaused
  - FrameWidth_Is480_At1920_AndNeverOverlapsTheCornerWidgets
  - Hud_BuildsMinimap_TopCentre_44pxHigh_NoRaycasts_WhiteOnly
  - IsObjectiveTarget_ById_BombTarget_AndDestroyAllLiveOnly
  - EnemySpawnTypes_ExcludeHealthBalloonsAndScenery
  - Spawning_MovesTheMarker_FromUpcomingToLive_AndCompletionDropsTheDiamond
  - Re-run HudBombIconsTests too: the HUD now holds the minimap Images, which are all sprited and ink, pencil or paper.
- Play test (user):
  - Unity -> Biplanes -> BuildScenes, then Play missions 1 to 5.
  - The strip appears at the top centre, hides on the briefing and results screens, and stays visible behind Pause.
  - The bracket follows the camera, and the triangle flips on A/D.
  - Ticks vanish as their groups spawn.
  - M2 shows the bomber's diamond and a dashed failAtX line. M5 shows the zeppelin's.
REQUESTS:
- orchestrator: UI_SPEC §3.3.1. Please record these decisions, or reject them and I'll change the code:
  - (a) Barrage balloons are drawn as 5 px pencil rings, not 3 px: a 3 px ring reads as a dot.
  - (b) Enemy bases are drawn as solid pencil squares.
  - (c) A pending destroyById or bombTarget target (from its spawn id, or by type when targetId is empty) shows its diamond on its upcoming tick until it spawns. destroyAllOfType diamonds go on live enemies only, as specced.
  - (d) The failAtX line disappears when its objective completes, like the diamonds.
  - (e) The frame is 480 px at most and shrinks on canvases narrower than about 1288 px, so it stays 12 px clear of the corner widgets. It is 368 px at 1024 and never narrower than 120 px (below about 776 px wide it would overlap).
  - (f) The flight band runs from the lowest sampled ground to the camera top (the plane is clamped to the screen).
- orchestrator: ARCHITECTURE §3 "Minimap". Please add these APIs:
  - MissionRunner `GroundEnemies`, `MissionLength`, `SpawnEntryCount`, `IsSpawnPending(int)`.
  - MinimapView `Create(parent, leftReserve, rightReserve)`, `Bind(runner)`, `Refresh()`.
  - The pure helpers `MapX`, `MapY`, `FacingAngle`, `FrameWidth`, `VisibleIn`, `IsEnemySpawnType`, `IsObjectiveTarget`.
  - HudView `Minimap`, `LeftReserve`, `RightReserve`.
  - The HQ flag is read from the JSON playerHQ spawns (atX + spacing*i), so it shows before the HQ spawns.
- orchestrator (optional): the mission HUD canvas uses the default constant-pixel CanvasScaler (MissionRunner.CreateHud), so "scales with the canvas" means screen pixels. Every HUD widget is therefore small on 4K. Switching to ScaleWithScreenSize 1920x1080 would be a separate HUD-wide task.
