TASK: B021
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Enemies/ZeppelinAI.cs (underside turret offset y -0.82 -> -1.10; new `public static float HalfLength` = 4.5)
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs (new const ZeppelinEntryMarginX = 0.5; zeppelin look-ahead = orthoSize*aspect + ZeppelinAI.HalfLength + 0.5)
- Biplanes/Assets/Biplanes/Tests/EditMode/ZeppelinAndBaseTests.cs (line-of-fire minClearBand 0.2 -> 0.25; asserts underside turret local y -1.10)
- Biplanes/Assets/Biplanes/Tests/EditMode/MissionDataRulesTests.cs (TriggerX models the zeppelin look-ahead; new M5_Zeppelin_SpawnsWhollyBeyondTheRightScreenEdge)
SUMMARY:
- T939 P1-1: the underside turret now hangs below the gondola line (hull-local (-0.65, -1.10), hitbox y -1.25..-0.95). Bullet centres in y -1.33..-1.02 (0.31 band) reach it from either side without touching a gondola. The top turrets are unchanged (0.37). At M5 (hull world y 0.9) the turret centre is at world -0.20 and its hitbox bottom at -0.35, which is >= 3.3 above the ground (max -3.667 on x 420->360) and on screen (view y -5..5).
- T939 P2-1: the zeppelin triggers at camera atX - (5a + 4.5 + 0.5), so the 9-unit hull is wholly 0.5 past the right edge when it appears. The M5 trigger is now 408.3 (4:3), 407.0 (16:10), 406.1 (16:9) and 403.3 (21:9). The fastest time to the map end after the spawn is 65.8 s or more against 60 s to the HQ (it was 64.3 s), and the escorts (fighters at camera 424/429) still come after the hull.
VERIFY:
- python tools/compile_check.py -> exit 0 (PASS Biplanes (+6 new file(s) not yet in the csproj), PASS Biplanes.Editor).
- Test scratch build (script in the session scratchpad): the runtime DLL is built from source, then all 9 Tests/EditMode/*.cs files are built against it (ProjectReferences become ScriptAssemblies DLLs, retargeted to v4.7.2). Result: 0 errors, and no zz_* files are left in the project.
- bash tools/unity.sh test was not run, because the editor has the project open. Run it in Unity (Test Runner, EditMode): Zeppelin_EveryTurret_HasAClearHorizontalLineOfFire (expected band 0.31 for the underside turret), Zeppelin_Spawn_NameTurretsAndMirroredGeometry, M5_ZeppelinUndersideTurret_StaysShootableAboveTheTerrain (3.47 >= 1.5), M5_ZeppelinReachesTheHQ_BeforeTheCameraCanReachTheMapEnd, and the new M5_Zeppelin_SpawnsWhollyBeyondTheRightScreenEdge.
- Playtest M5: the zeppelin flies in from off the right edge. Fly level just under the gondolas (about world y -0.2 to -0.4) and fire: the underside turret dies to 3 bullets. After all 3 turrets, 40 hull bullets win the mission.
REQUESTS:
- art: the underside turret now hangs about 0.26 units (26 px) below the envelope ink bottom (px y 174). The turret sprite (flipY) spans hull px y 200..240 at px x about 385, partly below the 220-px hull canvas. Please add a short mount pylon/strut in zeppelin_hull.svg at px x about 375..395, from y 174 down to about 200, so the turret doesn't look like it floats. No code change is needed. The turret sprite itself is unchanged.
- orchestrator: in ARCHITECTURE §3 ZeppelinAI, add: "the underside turret hangs below the gondola line so horizontal player bullets can reach it (every turret needs a clear horizontal line of fire, since a shielded hull box absorbs bullets); `ZeppelinAI.HalfLength` (half hull length, 4.5)". In the MissionRunner spawn-trigger text (§5), add: "zeppelins trigger at atX - (orthographicSize*aspect + ZeppelinAI.HalfLength + ZeppelinEntryMarginX 0.5), so the whole hull starts off screen. Other ground objects keep + GroundEntryMarginX 2."
- qa / mission-design (optional): tools/validate_missions.py ZEPPELIN_MIN_Y = 3 still gives the underside turret 1.9 units over flat ground at the spawn x (it was 2.18). That is above the 1.5 minimum, so no change is needed.
