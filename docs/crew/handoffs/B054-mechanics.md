TASK: B054
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/UI/HudView.cs (the objective text tracks which objective it shows; OnObjectiveCompleted reacts only to that one and then moves on)
- Biplanes/Assets/Biplanes/Scripts/Missions/EnemyPressure.cs (comment only)
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs (comment only, SpawnReinforcementFighter doc)
- Biplanes/Assets/Biplanes/Scripts/Core/ScoreKeeper.cs (comment only, ReinforcementFighterKey doc)
SUMMARY: HudView now remembers the shown objective, which is the index in MissionRunner.MissionData.objectives. ObjectiveCompleted for a different id is ignored while the shown objective is still open, so in M4 bombing the base keeps "Destroy N more barrage balloons". When the shown objective completes, the HUD moves on to the first open objective in mission order. A destroyAllOfType objective gets the tracker-driven counter (B053 unchanged). Any other type gets ObjectiveTracker.GetDescription, e.g. "Bomb the enemy base". "Objective Complete!" shows only when no objective is left open. The three stale comments now say reinforcement kills count toward destroy-all "fighter" (B050), while the required total stays the JSON count.
DECISIONS:
- Binding (SetObjective, done once): the shown objective is the first open destroyAllOfType on targetType. If there is none, it is the first open objective with that targetType, which matches MissionRunner's no-destroy-all fallback. The number comes from the tracker only for destroyAllOfType (ObjectiveFromTracker semantics unchanged).
- Id match: the payload equals obj.id. For an id-less entry, an empty payload or obj.type also matches. If the shown objective is already evaluated, any completion counts as its completion, which is robust to other payload keys.
- Moving on needs an enabled ObjectiveTracker. A standalone HUD (no runner/objective match) keeps the old rule: any completion shows "Objective Complete!".
- While a fixed-description objective is shown, EntityDestroyed doesn't touch the text, so the late balloon-kill event after the move-on can't overwrite it. Description colour is pencil.
- The hide coroutine is kept in a handle and stopped by SetObjective, so a stale 2 s hide can't blank a new mission's objective.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes, PASS Biplanes.Editor, exit 0.
- unity.sh not run (the editor is open, per the brief).
- Manual check in M4:
  - Bomb the base first. The HUD keeps "Destroy N more barrage balloons" and the counter keeps falling. Popping the last balloon shows "Objective Complete!" and the mission is won.
  - Pop all balloons first. The HUD shows "Bomb the enemy base" in pencil. Bombing it wins the mission.
  - In M1, fighter kills (JSON and reinforcement) still lower the counter.
REQUESTS:
- qa: add EditMode tests (FighterCounterQaTests style, a HUD under a MissionRunner with a tracker):
  - M4-shaped data (destroyAllOfType barrageBalloon id "pop_all_balloons" x3 + bombTarget enemyBase id "bomb_base"):
    - tracker.OnBombTargetDestroyed("enemy_base_01", "enemyBase") -> the text is still "Destroy 3 more barrage balloons".
    - Then 3 balloon kills -> "Objective Complete!".
  - Same data, balloons first -> the text is "Bomb the enemy base", and later balloon EntityDestroyed events don't change it. Then bomb the base -> "Objective Complete!".
  - Standalone HUD: GameEvents.RaiseObjectiveCompleted("x") still shows "Objective Complete!".
- orchestrator: UI_SPEC §3.3, Objective text row. Add: "Shows one objective at a time. Only that objective's ObjectiveCompleted changes it. The HUD then moves to the next open objective (counter for destroyAllOfType, else ObjectiveTracker.GetDescription), and shows 'Objective Complete!' only when none remain (B054)."
