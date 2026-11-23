TASK: B078
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Missions/CheatCodes.cs (new: MonoBehaviour listener + pure helpers + nested pure `CheatCodes.CheatMatcher`)
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs (CheatCodes wiring, `CheatUsed`, `CanUseHqCheat`, `TryHqCheat()`, `JumpToHq()`)
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs (new `WarpTo(Vector2, int)`, `ScreenAnchor` getter)
- Biplanes/Assets/Biplanes/Scripts/Missions/TerrainStrip.cs (new `RefreshWindowNow()`)
- Biplanes/Assets/Biplanes/Scripts/Core/SaveSystem.cs (`BestScoreLocked`, pure `ShouldSaveBest`)
- Biplanes/Assets/Biplanes/Tests/EditMode/CheatCodeTests.cs (new, 20 tests)
SUMMARY: In mission 5, typing H then Q (each key at most 1.5 s after the previous one; any other key resets the attempt, and a fresh H starts a new one) moves the camera so the HQ sits on the plane's trailing screen anchor. The plane warps to 3.5 units above the HQ, level and facing the zeppelin (+x). Pending spawns the jump passes are skipped, except the HQ, the zeppelin, its escorts and objective targets. The run is flagged CheatUsed, and SaveSystem refuses best-score saves until the next StartMission.
VERIFY:
- `python tools/compile_check.py`: exit 0 (PASS Biplanes, PASS Biplanes.Editor). Unity batch was not run, as the brief asked.
- I also built the full EditMode test assembly with dotnet against the new runtime DLL (a scratch script, targeting .NET 4.7.2): 0 errors. The tests have not been run yet. QA: Unity Test Runner > EditMode > `Biplanes.Tests.CheatCodeTests`.
- Manual play: start mission 5, fly, then type H Q.
  - Expect: an instant cut to the HQ. The plane is above the HQ and faces right. No pile of enemies appears; the ground is drawn on the next frame. The zeppelin and its escorts arrive normally as you fly on, and pressure reinforcements arrive after the quiet limit.
  - Win the mission: the result "Best:" stays as before and PlayerPrefs `best_score_5` is unchanged.
  - Typing H Q in missions 1-4, or while paused, on the briefing, on the runway, after a crash or on the result screen, does nothing.
  - H and Q are bound to nothing else, so typing them never fires or drops a bomb.
EXACT RULES:
- Gate (`CheatCodes.CanUseCheat` + UI check): `MissionData.id == 5`, GameManager state Playing, `MissionRunner.HasStarted` (after the B049 start gate), the player exists and is not on the runway or crashed, the mission has not ended, no Menu/Briefing/Pause/Result view is showing, and timeScale > 0. When the gate is closed the matcher resets. In missions 1-4 the listener returns early on the mission id.
- Keys: `Keyboard.current.allKeys`, `wasPressedThisFrame`. Letters A-Z map to chars and any other key maps to `OtherKey`, which counts as a wrong key. Mouse buttons are ignored.
- Placement:
  - hqX = atX of the playerHQ spawn entry.
  - facing = +1 if the zeppelin is at or right of hqX, else -1. The zeppelin position comes from the live zeppelin, else its spawn entry atX.
  - cameraX = hqX - (anchorViewport - 0.5) * 2 * halfWidth, where anchorViewport is screenAnchor facing right and 1 - screenAnchor facing left, clamped to [0, length]. In mission 5 this gives ~255.6.
  - Plane y = clamp(ground + 3.5, at most viewTop - 1, at least ground + 2). The HQ roof is ground + 1.
  - Order: move the camera, then `PlayerPlane.WarpTo`. WarpTo sets the Rigidbody2D and transform position, snaps facing and heading (no turn squash), sets pitch, velocity and climb-out to 0, re-bases the ride-along camera x so the next tick adds no scroll jump, and calls `ScrollController.SetDirection(facing)`.
  - Then `TerrainStrip.RefreshWindowNow()` builds every chunk around the new camera at once.
  - Known edge: if the scroll velocity was opposite (the player was flying left), the camera eases to the new direction like a normal turn. The plane rides along, so it keeps its screen position.
- Spawn skipping (`CheatCodes.SkipPassedSpawns`): every pending entry whose trigger is at or before the new camera x is marked spawned, i.e. skipped. The trigger is `MissionRunner.SpawnTriggerX`: atX for planes, atX - (halfWidth + HalfLength + 0.5) for the zeppelin, atX - (halfWidth + 2) for the rest. An entry is kept instead (`CheatCodes.KeepOnJump`) when it is:
  - type playerHQ or zeppelin;
  - an escort (`escortOf` set);
  - an entry whose id is any objective's targetId;
  - an entry whose type an open destroyAllOfType objective counts.
  Kept entries stay pending, so the next Update spawns them through the normal path: planes enter from the right edge, the zeppelin at its JSON atX. Entries ahead of the new camera x are untouched.
  - Mission 5: all fighter, AAA, health balloon and bomb balloon groups up to atX ≈ 255 are skipped. The HQ spawns at once under the plane. The zeppelin (trigger ≈ 280), its escorts and fighters from atX 262 on spawn normally later.
  - Live fighters left behind leave through EnemyPressure's left-exit rule. The quiet timer and reinforcements run unchanged.
  - A repeat jump is idempotent: same spot, nothing new skipped.
- Score: `MissionRunner.CheatUsed = true` and `SaveSystem.BestScoreLocked = true`. `SaveSystem.SaveBestScore` then saves nothing, which covers both the ScoreKeeper.OnMissionEnded path and the ResultView path. Unlocks still work. `StartMission` clears both flags (Retry, Next, menu). The result screen shows nothing special.
NOTES:
- 6 code files. Two are beyond the brief's WRITE list:
  - PlayerPlane: needed to set the facing and re-base the ride-along without a slide.
  - TerrainStrip: without RefreshWindowNow the ground under the new view would appear one chunk per frame, a visible gap of about 4 frames.
- I suppressed the best score in SaveSystem rather than ScoreKeeper so that ResultView's own `SaveBestScore` call is also blocked, without editing UI.
- There is no on-screen "HQ" confirmation (it would need HudView, a 7th file). The jump logs a Debug.Log line instead.
- The brief reads "H, Q," (trailing comma). I implemented the two-key code HQ. The sequence is the constant `CheatCodes.HqSequence`, so adding a key is a one-line change.
REQUESTS:
- orchestrator: ARCHITECTURE.md §3 (Missions / Mission start gate area), add: "Mission-5 HQ cheat (B078): CheatCodes (one per mission, added by MissionRunner.StartMission) reads Keyboard.current; typing H then Q (≤1.5 s between keys, any other key resets) while mission 5 is Playing, started, airborne, not crashed, not ended and no UI screen is up calls MissionRunner.JumpToHq. The camera moves so the playerHQ spawn x sits on the plane's trailing screen anchor; the plane warps (PlayerPlane.WarpTo) 3.5 above the ground there, facing the zeppelin; TerrainStrip.RefreshWindowNow rebuilds the ground at once. Pending spawns whose trigger the new camera x has passed are skipped, except playerHQ, zeppelin, escorts (escortOf), objective targetIds and open destroyAllOfType types, which spawn normally next Update. The run is flagged MissionRunner.CheatUsed and SaveSystem.BestScoreLocked blocks best-score saves until the next StartMission."
- orchestrator: GDD Change Log: "B078: hidden mission-5 debug/cheat code H-Q jumps to the player HQ; cheat runs never record a best score."
- orchestrator (optional follow-up for mechanics): a small white "HQ" HUD line for 2 s via a GameEvents hook in HudView, if wanted.
