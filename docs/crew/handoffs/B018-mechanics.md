TASK: B018
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Missions/ObjectiveTracker.cs
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs
- Biplanes/Assets/Biplanes/Scripts/Enemies/ZeppelinAI.cs
- Biplanes/Assets/Biplanes/Scripts/Enemies/BomberAI.cs
- Biplanes/Assets/Biplanes/Tests/EditMode/ObjectiveTrackerTests.cs
- Biplanes/Assets/Biplanes/Tests/EditMode/MissionEndRulesTests.cs
SUMMARY:
- failAtX is now the WORLD x the target entity must not reach. `CheckFailAtX(cameraX)` is removed. The new ObjectiveTracker calls are `GetFailAtXFor(targetId)` and `CheckTargetReached(targetId, entityX)`; both look only at open destroyById objectives with failAtX >= 0. The camera position plays no part, so turning back doesn't dodge the loss.
- MissionRunner gives each spawned bomber/zeppelin its targetX through the new `SetTargetX`. The objective's failAtX comes first; the spawn entry's targetX is the fallback, and it drives only the bomb run, never a loss. Each Update, `CheckTargetsReached()` polls the live, not-dead, watched id targets. One at x <= failAtX loses the mission through `HandleObjectiveFailed` → `GameManager.LoseMission("Objective failed: <desc> (the <bomber|zeppelin> reached its target)")`. Killing the target first completes the objective, and a completed objective never fails.
- Despawns: ZeppelinAI no longer despawns behind the camera while it still has a targetX ahead of it. BomberAI has no despawn rule at all. BomberAI also gets `TargetX`/`HasTargetX`/`ReachedTarget`/`SetTargetX`, and its bomb run is keyed to its own x (it starts at targetX + 5), not to the camera. A spawn already at or left of its targetX is a data error: MissionRunner logs a warning and ignores the target, so nothing is watched and there's no instant loss. The map end still ends the mission.
VERIFY:
- `python tools/compile_check.py` → PASS Biplanes (+6 new file(s) not yet in the csproj), PASS Biplanes.Editor, exit 0. The `tools/unity.sh compile` step wasn't run: the editor has the project open.
- Tests compiled in a scratch build (not run):
  - The runtime DLL was built from source with compile_check's helpers.
  - The test csproj had all 8 Tests/EditMode/*.cs files added explicitly. The generated Biplanes.Tests.EditMode.csproj lists only 3 of them.
  - Result: 0 errors. Nothing was left in the project.
- Run the tests with Unity closed: `bash tools/unity.sh test`, then ObjectiveTrackerTests and MissionEndRulesTests. New or replaced tests:
  - GetFailAtXFor_*
  - CheckTargetReached_FiresWhenTheEntityXReachesFailAtX
  - CheckTargetReached_DestroyedFirst_NeverFails
  - Runner_M2_CameraAtOrPastFailAtX_DoesNotLose
  - Runner_BomberReachingTargetX_LosesOnce_CameraIrrelevant
  - Runner_BomberDestroyedBeforeTargetX_WinsAndNeverLoses
  - Runner_ZeppelinReachingTargetX_Loses
  - Runner_SpawnAlreadyPastTargetX_IsIgnored_NoInstantLoss
  - Zeppelin_WithTargetAhead_NeverDespawnsBehindCamera_WithoutTargetItDoes
  - Runner_M5_DespawnedZeppelinNeverCounts_MapEndLoses
  - Runner_TargetReached_ReasonIsNotPlayerCrashed
  - Runner_AnyEnd_DisablesPlayerInputAndStopsScroll, which now uses a map-end loss.
- Playtest after V003: in M2/M5, let the target fly, outrun it or turn back. You lose exactly when the bomber/zeppelin itself crosses failAtX, and the reason reads "... reached its target". Kill it first: you win, and there is no later loss.
- Risk: with the CURRENT mission JSON, both missions log the "already at or past its targetX" warning and fall back to a map-end loss:
  - M2: the bomber spawns at about camera + 10 to 16 (about 60), with failAtX 370.
  - M5: the zeppelin spawns at 60, with failAtX 620.
  - This is expected until V003 lands.
REQUESTS:
- mission-design (V003):
  - failAtX must be a world x LEFT of where the target spawns, because bombers and zeppelins fly left.
  - M5: put the HQ at failAtX, left of the zeppelin's spawn atX. Zeppelins keep atX as their spawn x.
  - M2: bombers ignore atX for placement. They spawn about 10.4 to 16.4 right of the camera x at trigger time, so the camera is at about atX when they spawn. failAtX must be well left of atX + 10. The bomber flies at enemySpeed.bomber, about 1.5 u/s.
  - Remember the camera can't scroll left of the mission start (x 0).
- orchestrator: update ARCHITECTURE.md:
  - §3 Enemies, BomberAI: "`BomberAI` (`targetX` = world x it flies toward; `SetTargetX(x)`, `TargetX`, `HasTargetX`, `ReachedTarget`; starts its bomb run 5 units before targetX; never despawns)".
  - §3 Enemies, ZeppelinAI: "`SetTargetX(x)`/`TargetX`/`HasTargetX`/`ReachedTarget`. The hull is removed silently 20 units behind the left screen edge only when it has no targetX ahead of it."
  - §3 Missions, the failAtX bullet, replace with: "`failAtX`: the WORLD x the destroyById target must not reach (it flies left). At spawn, MissionRunner gives the bomber/zeppelin `SetTargetX(tracker.GetFailAtXFor(id))`, falling back to the spawn entry's targetX, which only drives behaviour. A target at or right of the spawn x logs a warning and is ignored. Each Update, `CheckTargetsReached` passes every live, not-dead watched id target's x to `ObjectiveTracker.CheckTargetReached(id, x)`. A hit loses once via `HandleObjectiveFailed` → `GameManager.LoseMission("Objective failed: <desc> (the <type> reached its target)")`. The camera x is not used."
  - §5, the destroyById line: "If `failAtX` ≥ 0 and the target entity's world x reaches `failAtX` (flying left) before it is destroyed, the mission is lost."
  - §5, the end rule: "... or a failAtX target reaches its x".
  - §5, the optional `targetX` spawn field: "behaviour only (bomber bomb run); the objective's failAtX overrides it for id targets".
- orchestrator (tooling, optional): compile_check.py skips Tests/ and the generated test csproj is stale (3 of 8 files). A `--tests` mode that adds all Tests/EditMode/*.cs would let QA compile-check the tests without Unity. The scratch approach is described above.
