TASK: B003 + B006
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs
- Biplanes/Assets/Biplanes/Scripts/Weapons/ObjectPool.cs
- Biplanes/Assets/Biplanes/Scripts/Weapons/Bullet.cs
- Biplanes/Assets/Biplanes/Scripts/Weapons/Bomb.cs
- Biplanes/Assets/Biplanes/Scripts/Core/ScoreKeeper.cs
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs
- docs/crew/handoffs/B003-mechanics.md
SUMMARY:
- B003: new private `_ignoreHeldLeft` in PlayerPlane. It is set on the takeoff tick when A is held. While it is set, OnMove treats A as not pressed (this includes _prevLeft), and it clears on the first tick without A, so the next A press is a fresh one. D, a fresh A, A alone on the runway (still nothing), and airborne starts (the flag is never set) are unchanged.
- B006a: ObjectPool<T> parents every item under its HideAndDontSave root from creation, tracks all items, and gains `Dispose()` and `IsDisposed`. Dispose deactivates and destroys every item, in flight or pooled, plus the root (DestroyImmediate outside Play mode). It is also hooked to Application.quitting in Play mode. Pop skips destroyed items; Push and Expand after Dispose are no-ops. Bullet.InitPool and Bomb.InitPool call `_pool?.Dispose()` before rebuilding. PlayerPlane caches the bullet, bomb and fallback sprites in statics, so there's no Texture2D/Sprite per Create. The missing plane sprite now logs one warning. Existing ObjectPool, InitPool and Spawn signatures are unchanged.
- B006b: new `ScoreKeeper.ResetScore()` (score = 0, raises ScoreChanged), called in StartMission right after the teardown. B006c: new `_missionEnded` flag in MissionRunner. HandleObjectiveComplete returns early once it is set, and sets it on the win. Update skips the survive-to-end check once it is set. A new GameEvents.MissionEnded subscription (cleared in teardown and OnDestroy) also sets it on a loss, so no win can follow a crash. StartMission clears the flag. The fail path had no repeat issue: PlayerPlane.TriggerCrash is already guarded by _hasCrashed.
VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes, PASS Biplanes.Editor, exit 0. The "+1 new file not yet in the csproj" note is the parallel agent's EnemyBomb.cs. No reflection-target names were renamed. Existing tests should still pass: the PlayerFacingTests TearDown destroys the items and roots, and Dispose and Pop tolerate destroyed items.
- Suggested EditMode test (QA): runway, Move(left, up), Move(left), Move(left): Facing stays +1. Then Move(), Move(left): Facing is -1.
- Suggested EditMode test (QA): Bullet.InitPool twice, then check that the first pool's items and its `Pool<Bullet>` root are destroyed, and that exactly one `Pool<Bullet>` root exists.
- Playtest: on the mission 1 runway, hold A, press W, and keep A held. The plane keeps facing right. Release A and press it again: it turns left.
- Playtest: chain Next 1→2→3, and also Retry. The Profiler's GameObject count stays flat, and the editor reports no "objects were not cleaned up" warnings after you leave Play mode.
- Playtest: win mission 1 with points, then press Next. Mission 2's HUD and best score start from 0.
- Playtest: after a win, the Console shows "all objectives complete" exactly once, and PlayerPrefs is written once.
REQUESTS:
- orchestrator: ARCHITECTURE.md §3 PlayerPlane: replace "Facing input is ignored on the takeoff tick, so W+A also takes off facing right." with "An A held at takeoff is ignored until it is released and pressed again, so W+A takes off and stays facing right."
- orchestrator: ARCHITECTURE.md §3 Weapons: add "Every pooled item lives under the pool's hidden root. `ObjectPool<T>.Dispose()` destroys the root and all items, including items in flight. Each `X.InitPool` disposes the previous pool before rebuilding, so pools are per mission and are freed with it."
- orchestrator: ARCHITECTURE.md §3 Missions: add "StartMission calls `ScoreKeeper.ResetScore()` after the teardown. The mission end (win via HandleObjectiveComplete, or loss via GameEvents.MissionEnded) is handled once per mission (`_missionEnded`, cleared in StartMission)."
- mechanics (parallel agent, FlakShell/EnemyBomb): call `_pool?.Dispose();` at the top of `FlakShell.InitPool` (and of any new EnemyBomb.InitPool) so those pools don't leak on Next or Retry either.
- orchestrator (design note): ScoreKeeper.OnMissionEnded(id, false) is never called on a loss, so a loss never saves a best score. That's pre-existing and was left unchanged. Decide whether a loss should record a best score.
