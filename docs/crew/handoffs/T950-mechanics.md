TASK: T950
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Fx/FxManager.cs
- Biplanes/Assets/Biplanes/Scripts/Weapons/Bomb.cs
- Biplanes/Assets/Biplanes/Scripts/Enemies/AAAGun.cs
- Biplanes/Assets/Biplanes/Scripts/Enemies/FighterAI.cs
- Biplanes/Assets/Biplanes/Scripts/Enemies/BomberAI.cs
- Biplanes/Assets/Biplanes/Scripts/Weapons/FlakShell.cs (doc comment only)
SUMMARY:
- FxManager: `TriggerExplosion(Vector3 position, bool playSound = true)` plays SfxId.Explosion at the position, and `TriggerFlakBurst(Vector3 position, bool playSound = true)` plays SfxId.FlakBurst. Both go through a private `PlaySfx`, which uses `AudioManager.GetOrNull()` with a null check. The cue plays even if the FX frames are missing. All existing callers compile unchanged. SpawnSmokePuff stays silent.
- Bomb ground impact: `TriggerExplosion(p, false)` plus `SfxId.BombImpact` at the impact point, so the cue isn't doubled. A direct trigger hit on an enemy keeps the default Explosion cue.
- AAAGun: plays FlakFire at the gun once per shell, only when `FlakShell.Spawn` returns non-null. An exhausted pool means no shell and no sound.
- FighterAI and BomberAI: play EnemyGun at the shooter's position once per EnemyBullet, after a successful `EnemyBullet.Spawn`.
- FlakShell needed no code change. Its burst cue comes from TriggerFlakBurst, which PlayBurst already guards with `activeInHierarchy`, so a plain Recycle, pool dispose or scene reload plays nothing. Zeppelin-turret shells get the same burst cue.
- No sound is played from Recycle, ObjectPool disposal, OnDestroy or scene-unload paths. Every hook sits on a fire, impact or lifetime-end event.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes (+1 new file(s) not yet in the csproj), PASS Biplanes.Editor, exit 0. `tools/unity.sh compile` was not run because the Unity editor has the project open.
- No tests were added (the new test file was optional). Existing tests call `TriggerExplosion(pos)`/`TriggerFlakBurst(pos)` positionally and no test looks them up by reflection, so they stay source-compatible. EditMode runs have no AudioManager (GetOrNull returns null), so the hooks are no-ops there.
- Play check, once the WAVs land:
  - A bomb on the ground gives one thump and no explosion cue.
  - A bomb on an enemy gives an explosion cue.
  - AAA bursts give 3 flak_fire cues per burst, each followed by a flak_burst at the airburst or hit.
  - Fighter and bomber shots give one enemy_gun each, panned by x.
  - Retry is silent (no cues from the teardown).
REQUESTS:
- orchestrator: in ARCHITECTURE.md §3 FX, note the signatures `TriggerExplosion(Vector3, bool playSound = true)` and `TriggerFlakBurst(Vector3, bool playSound = true)`. In §3 Audio "Hooks", note:
  - Bomb ground impact plays bomb_impact instead of explosion.
  - AAAGun plays flak_fire per shell actually spawned.
  - FighterAI and BomberAI play enemy_gun per bullet actually spawned.
- Note for whoever does T951/ZeppelinAI: ZeppelinAI's multi-explosion death calls `TriggerExplosion` several times, so it now plays several Explosion cues. If it should play a single ExplosionBig instead, pass `playSound: false` on those calls and play ExplosionBig once.
