TASK: B030
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Fx/FxManager.cs (rewritten)
- Biplanes/Assets/Biplanes/Scripts/Fx/FxFrames.cs (new, pure static helper)
- Biplanes/Assets/Biplanes/Scripts/Weapons/FlakShell.cs
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs
- Biplanes/Assets/Biplanes/Tests/EditMode/FxFramesTests.cs (new)
- docs/crew/handoffs/B030-mechanics.md
SUMMARY:
- FxManager now plays frame animations from one Update over one live-effect list:
  - explosion_0..5 at 0.08 s per frame (0.48 s), with a 0.85x->1.15x grow and the alpha fading over the last 0.12 s;
  - the new TriggerFlakBurst plays flak_burst_0..3 at 0.07 s per frame;
  - the new SpawnSmokePuff rises 0.3 u/s, grows 1.0x->1.6x and fades 0.9->0 over 1.2 s.
  Explosion and flak keep the random flipX/flipY; smoke flips only on X. Sprites are cached per kind on the first use. Caps: 20 explosions, 16 flak, 24 smoke. At a cap the oldest effect of that kind restarts at the new position. The live list is the only count, and entries destroyed by a scene unload are pruned, so the old _activeExplosions drift and the empty-array RecycleOldest are gone.
- FlakShell plays a burst on a player hit (OnHit) and on lifetime expiry (new OnLifetimeEnd override). A plain Recycle (pool dispose or reload) plays none. PlayerPlane: the unused _smokeTrailRoot field is removed. The new Update -> EmitSmoke spawns a puff 0.75 u behind the tail, along the body rotation, every SmokePuffInterval = 0.12 s while damage >= smokeThreshold and the plane is flying or falling as a wreck. It stops once the wreck lands. UpdateSmokeTrail now only does the damage tint.
VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes (+1 new file not yet in the csproj), PASS Biplanes.Editor, exit 0. Unity is open, so unity.sh compile was not run.
- Tests were compiled in a scratch build using the B018 method. The runtime DLL was built with compile_check helpers. The temp test csproj had all 16 Tests/EditMode/*.cs files listed. One extra step was needed: retarget it to v4.7.2, because nunit.framework doesn't resolve under v4.7.1 with dotnet SDK 10. Result: 0 errors, nothing left in the project. The tests were not run.
- With Unity closed: `bash tools/unity.sh test` runs FxFramesTests (8 tests). The existing FX-touching tests also need to pass:
  - EnemyWeaponsAndPoolTests (Bomb ground burst finds a GameObject named "Explosion" at the crossing point; the name is kept);
  - FlakShell_HitOnPlayer_*: no FxManager is seeded, so FlakShell uses FxManager.GetOrNull(), which returns null in EditMode instead of calling DontDestroyOnLoad.
- Playtest after Biplanes -> BuildScenes:
  - Kill an enemy: you see a 6-frame pencil explosion of about 0.5 s with no big zoom.
  - Dodge AAA: shells airburst after 3 s with a flak_burst animation; a hit on the player bursts on the plane.
  - Take >= 70% damage: a trail of puffs stays in the world, rises and fades. It continues while the wreck falls and stops at landing.
DECISIONS:
- Effects are scene-root GameObjects, so they die with the scene; FxManager itself stays DontDestroyOnLoad. They use the Default layer: the old code put explosions on the UI layer, and draw order is sortingOrder 10 per ARCHITECTURE.
- Missing frames: one warning per kind, listing the missing paths, and the frames that exist still play. If no frames exist, the effect is skipped and nothing throws.
- Times use Time.deltaTime, so FX freeze while paused (timeScale 0). EmitSmoke emits nothing at dt 0.
REQUESTS:
- orchestrator: ARCHITECTURE.md, §3 Fx, add: "`FxManager` (Fx/, singleton, `Instance` creates it; `static GetOrNull()` returns null outside Play mode when none exists). `TriggerExplosion(Vector3)`: explosion_0..5 at 0.08 s/frame, gentle 0.85x->1.15x grow, alpha fade over the last 0.12 s, max 20 live. `TriggerFlakBurst(Vector3)`: flak_burst_0..3 at 0.07 s/frame, max 16. `SpawnSmokePuff(Vector3)`: smoke_puff, world space, rises 0.3 u/s, 1.0x->1.6x, fades over 1.2 s, max 24. `CountActive(FxManager.Kind)`. At a cap the oldest effect of that kind restarts. Sprites load once; missing frames warn once and are skipped. Frame math lives in the pure `FxFrames` (Duration, FrameIndex, IsDone, Progress, FadeOutAlpha, Scale). sortingOrder 10."
- orchestrator: ARCHITECTURE.md, §3 Player, PlayerPlane: replace "smoke trail at ≥ 70% damage" with "smoke trail at ≥ smokeThreshold: `FxManager.SpawnSmokePuff` 0.75 u behind the tail every `SmokePuffInterval` (0.12 s) while flying or falling as a wreck (stops when the wreck lands), plus the amber damage tint".
- orchestrator: ARCHITECTURE.md, §3 Weapons, FlakShell: "plays `FxManager.TriggerFlakBurst` on a player hit and on lifetime expiry (airburst), not on a plain Recycle".
- orchestrator (tooling, optional): a `--tests` mode for compile_check.py must retarget the test csproj to v4.7.2. Otherwise nunit.framework fails to resolve (MSB3274), and every test file reports CS0246 for NUnit.
