TASK: B068
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Enemies/BomberAI.cs (changed)
- Biplanes/Assets/Biplanes/Scripts/Enemies/BomberWreck.cs (new)
- Biplanes/Assets/Biplanes/Scripts/Core/GameBalance.cs (changed: BomberCrashData + GameBalance.bomberCrash)
- Biplanes/Assets/Biplanes/Resources/Balance.json (changed: enemyHealth.bomber 1000, new bomberCrash block)
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs (one line: BomberWreck.ClearAll() in TearDownPreviousMission)
- Biplanes/Assets/Biplanes/Tests/EditMode/BomberDamageTests.cs (new, 18 tests)
SUMMARY: The bomber now has 1000 HP (40 bullets or 10 bombs), and BomberAI.FallbackHitPoints is also 1000. It trails engine smoke through SpawnEnemySmokePuff, and the pure helper SmokeLevelFor sets the level: none above 70% health, light at 70% or below, thick at 40% or below, heavy at 15% or below. At 0 HP the kill still counts at the hit, as before. On the next FixedUpdate, BeginCrash plays the mid-air explosion, spawns a visual-only BomberWreck and removes the bomber at once. The wreck drifts, dives nose-down, falls under gravity and keeps smoking heavily. On the terrain it bursts with several explosions and removes itself.
VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes (+1 new file), PASS Biplanes.Editor, exit 0. I did not run Unity batch, as the brief said. The tests assembly is not covered by compile_check. I also tried a scratch dotnet build of it. The only errors were unresolved NUnit types (the NUnit DLL would not resolve outside Unity). Every Biplanes type and member the new test uses resolved.
- QA: run the EditMode tests in BomberDamageTests. Re-run BomberHitboxTests, EnemyFireTests, PlanePitchTests (Bomber_Climbing_DrawnNoseUp calls FixedUpdate on a live bomber, so it is unaffected), PlaneCollisionTests, ObjectiveTrackerTests, MissionEndRulesTests, EnemyPressureFixTests, WhiteOnlyAndPressureQaTests and MinimapQaTests.
- Play checks for mission 2:
  - The bomber shows no smoke until 12 or 13 bullets have hit (health at 70% or below). The smoke thickens at 25 bullets and is heaviest at 35. It comes from the engine at the nose.
  - The 40th bullet scores 500 and completes destroy_bomber at that moment.
  - The wreck then explodes once, dives and smokes. Escorts drop formation and start chasing the player.
  - Flying through the wreck does no damage.
  - The wreck lands with a 3-explosion burst and disappears.
DESIGN NOTES:
- The wreck is its own object rather than the dead bomber object. A dead bomber left in place would still be the cached `_escortTarget` of the FighterAI escorts, which would follow it down to the ground. It would also keep its colliders and BomberAI logic, and stay in EnemyAircraft and on the minimap.
- With the separate object, a destroyed BomberAI is Unity-null to everything that tracked it. That covers EnemyPressure.IsLiveAircraft, the minimap, IdTarget.Target in CheckTargetsReached and FighterAI.IsEscorting.
- destroyById is still reported synchronously in MissionRunner.OnEntityDestroyed at the killing hit, while the bomber object still exists. ResolveTargetX and TrackIdTarget did not change.
- The "bigger" ground explosion is a cluster of explosions rather than an upscaled sprite. The groundBurstCount explosions (default 3) are spread ±groundBurstSpread along the wreck, and only the first plays the sound. FxManager notes that big upscales blur the hatching, so FxManager is unchanged.
- Cleanup: MissionRunner teardown calls BomberWreck.ClearAll. A scene reload (Retry or the menu) destroys wrecks with the scene. A wreck still falling after maxFallSeconds bursts and removes itself.
- Existing tests: no test asserted bomber health 200, 8 bullets or 2 bombs. EnemyWeaponsAndPoolTests:611 and PlayerResistanceTests:213 use 200f only as an arbitrary MakeHealth value and do not read Balance, so they are unchanged.
REQUESTS:
- orchestrator: In ARCHITECTURE.md, change the Balance example line `"enemyHealth": {... "bomber": 200.0 ...}` to `"bomber": 1000.0`. In the paragraph that says "a bomber in 8 bullets or 2 bombs", replace that phrase with "a bomber in 40 bullets or 10 bombs (B068)".
- orchestrator: Add to the ARCHITECTURE.md Balance section: `"bomberCrash": { "gravity": 5.0, "driftFactor": 0.8, "diveDegreesPerSecond": 30.0, "maxDiveDegrees": 60.0, "groundBurstCount": 3, "groundBurstSpread": 0.9, "maxFallSeconds": 10.0 }` (mirrored as `BomberCrashData`, read with `BomberCrashData.Resolve(GameBalance.Get())`, defaults used when a key is missing or not positive).
- orchestrator: Add to the ARCHITECTURE.md enemies section, after the BomberAI description: "Bomber smoke and crash (B068): a live BomberAI trails `FxManager.SpawnEnemySmokePuff` puffs from its engine (`BomberAI.EngineSmokePoint`, 1.25 u along the drawn nose). `BomberAI.SmokeLevelFor(healthFraction)` gives None above 0.70, Light at 0.70 or below (one puff every 0.35 s), Thick at 0.40 or below (0.16 s) and Heavy at 0.15 or below (0.08 s). At 0 HP, Health raises EntityDestroyed at the hit (score, objectives and destroyById, unchanged). The next FixedUpdate, `BeginCrash` plays `TriggerExplosion` and spawns `BomberWreck.Create(...)`: a scene-root SpriteRenderer only, with no collider, Health, TeamTag or Rigidbody2D, not in EnemyAircraft and not an escort target. The BomberAI object is then destroyed at once. The wreck keeps driftFactor of the bomber's x speed, falls under bomberCrash.gravity, pitches nose-down to maxDiveDegrees and smokes at the Heavy rate. At `TerrainStrip.HeightAt(x) + 0.3`, or after maxFallSeconds, it plays groundBurstCount explosions (one sound) and removes itself. MissionRunner teardown calls `BomberWreck.ClearAll()`."
- orchestrator: Add a GDD Change Log line: the bomber takes 1000 HP (40 bullets or 10 bombs), smokes more as it takes damage, and on death explodes and falls to the ground as a wreck. The kill counts at the moment it is shot down.
