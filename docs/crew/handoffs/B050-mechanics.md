TASK: B050
AGENT: mechanics
STATUS: PARTIAL
FILES:
- Biplanes/Assets/Biplanes/Scripts/Missions/ObjectiveTracker.cs (reinforcement kills count as "fighter" for destroyAllOfType; new `CountsAsKillOf` and `RemainingKills`)
SUMMARY: ObjectiveTracker now counts "fighterReinforcement" kills as "fighter" kills for destroyAllOfType. The required number is still the JSON spawn total. The objective completes once at that number, and extra kills after that change nothing. The tracker side of the bug is fixed, but the number the player sees on screen still won't move on reinforcement kills. HudView keeps its own counter with an exact type match. HudView is read-only for this task, so that part is listed under REQUESTS. That is why the status is PARTIAL: the user-visible symptom stays until that request lands.
DECISIONS:
- `public static bool ObjectiveTracker.CountsAsKillOf(string targetType, string entityType)`: returns true for the same key (case-insensitive), or for target "fighter" with entity `ScoreKeeper.ReinforcementFighterKey`. OnEntityDestroyed uses it. It is public so HUD, minimap and MissionRunner can match the tracker exactly.
- `public int ObjectiveTracker.RemainingKills(int index)`: returns max(0, required - kills) for an open destroyAllOfType objective, and 0 otherwise. It lets the HUD show the tracker's own count instead of keeping a second one.
- The required count (`CountOfType` over spawns) still uses an exact match. JSON never spawns "fighterReinforcement", so required = the JSON total.
- After completion, `obj.evaluated` causes later kills to be skipped, and `Complete` is guarded, so ObjectiveCompleted is raised only once.
- bombTarget / destroyById matching is unchanged.
- Fighter death-path audit. Every kill goes through Health.ApplyDamage. Health raises `EntityDestroyed(gameObject.name, Team)` once, and the `if (IsDead) return;` guard at the top blocks a second raise. FighterAI.FixedUpdate then only bursts and Destroys, with no second raise. Health.Team comes from the TeamTag that FighterAI.Spawn adds as Enemy before Health, so it is Team.Enemy. Names are "fighter" for JSON fighters and escorts, and "fighterReinforcement" for reinforcements (MissionRunner.cs:448). Both now map to fighter.
  - Bullet: Bullet.OnHit -> FighterAI.ApplyDamage (IsDead-guarded) -> Health. Exactly 1 raise.
  - Bomb: the direct hit, then BurstDamage skips `_directHit`, already-damaged targets and IsDead targets. Exactly 1 raise.
  - Mid-air collision: PlayerPlane.OnTriggerEnter2D skips enemies with `enemy.IsDead`, and Collision is fatal. Exactly 1 raise.
  - Player crashing or crashed: PlayerPlane.OnTriggerEnter2D returns early on `_hasCrashed`, so touching a fighter while crashing kills nothing and raises nothing. That is not a miscount, because the fighter lives on. Bullets or bombs already in flight still kill and count normally.
  - FighterAI.Start `ApplyDamage(0f, Bullet)`: 0 damage and never fatal, so no raise.
  - Escaped or removed (EnemyPressure.cs:182): `Destroy` with no raise. This only happens to reinforcements past 20 u (they don't count toward the required number) or to JSON fighters that no open destroyAllOfType objective needs. Silent by design.
  - Result: no fighter death path raises zero or two events. I found no second cause in the files I may edit. FighterAI.cs and FxManager.cs (owned by B051) were only read, not edited, and the audit found no bug in either.
- Second cause, user-visible (HUD). MissionRunner.SetActiveObjectiveFromJson calls `HudView.SetObjective("fighter", N)`, and the HUD shows "Destroy N more fighters". HudView.OnEntityDestroyed (HudView.cs:397-411) decrements its own `_objectiveRemaining` only when `entityType.Equals(_objectiveTargetType)`. A "fighterReinforcement" kill is skipped, so the on-screen number still won't move on reinforcement kills until HudView changes.
  - It also ignores team, and it never re-syncs with the tracker. If both keep their own counts, they can drift apart again.
  - So the HUD already shows a number ("Destroy N more fighters"), and adding "(N left)" is not needed.
- Minimap (MinimapView.cs:451-452): it still excludes reinforcements from fighter objective-target markers, which is now stale under the new rule.
- EnemyPressure.cs:176: `needed = !reinforcement && OpenDestroyAllCounts(name)`. That stays correct. Reinforcements are not part of the required total, so they shouldn't circle back.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes, PASS Biplanes.Editor, exit 0.
- `tools/unity.sh` was not run, per the brief (B049 is using batch mode). No tests were run.
REQUESTS:
- mechanics or UI owner (P1, the visible half of B050). HudView.OnEntityDestroyed (HudView.cs:400-401) should match with `ObjectiveTracker.CountsAsKillOf(_objectiveTargetType, entityType)` and ignore teams other than `Team.Enemy`.
  - Better: the HUD reads the remaining number from the tracker instead of keeping its own counter. MissionRunner would pass `_objectiveTracker.RemainingKills(i)`, or HudView would refresh from it on EntityDestroyed after the tracker updates. Note that MissionRunner subscribes to EntityDestroyed before HudView, so the order would need care.
  - Also: the text uses `entityType.ToLowerInvariant()+"s"`, so a reinforcement kill would print "fighterreinforcements". Use `ObjectiveTracker.ReadableType(_objectiveTargetType, true)` instead.
  - These changes touch HudView.cs and possibly MissionRunner.cs, which B049 owns right now. Schedule them after B049.
- UI owner (P2): MinimapView.cs:451-452 should drop the `!= ReinforcementFighterKey` exclusion, so reinforcements show as fighter objective targets like any other fighter. Update MinimapTests.cs:169 and MinimapQaTests.cs:134 to match.
- qa: update these asserts that encode "reinforcement kills never count":
  - ObjectiveTrackerTests.cs:155-161. The M1 test adds 5 reinforcement kills after total-1 fighters and expects `_kills == total-1`. Now 5 more count, the objective completes and `_completed.Count` becomes 1. Move the reinforcement kills into the "count toward" set, or drop them.
  - ObjectiveTrackerTests.cs:484-487. Runner_Next_...: 3 reinforcement kills now count (and win early). Assert the new rule instead.
  - EnemyPressureTests.cs:158-180, Reinforcement_ScoresLikeAFighter_ButNotForDestroyAll. With required 1, the first reinforcement kill now completes the objective. Rename the test and assert completion on the reinforcement kill.
  - New tests to add:
    - `CountsAsKillOf("fighter","fighterReinforcement")` is true, `("bomber","fighterReinforcement")` is false, and the reverse `("fighterReinforcement","fighter")` is false.
    - The required count is unchanged by reinforcement spawns.
    - Kills after completion raise nothing more.
    - `RemainingKills` drops by one on each counted kill and floors at 0.
  - EnemyPressureFixTests.cs:344 (`OpenDestroyAllCounts(ReinforcementFighterKey)` is false) is still correct. Reinforcements should not re-enter.
- orchestrator: ARCHITECTURE §3 "Enemy pressure" already says B050 counts reinforcements. Please add these points:
  - The shared matcher `ObjectiveTracker.CountsAsKillOf` and the new `RemainingKills(int)` API.
  - The MissionRunner.cs:431 doc comment ("never counts toward destroyAllOfType") is now stale. Fix it when B049 is done with that file.
