TASK: B051
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Enemies/FighterAI.cs
- Biplanes/Assets/Biplanes/Scripts/Fx/FxManager.cs
- Biplanes/Assets/Biplanes/Tests/EditMode/FighterSmokeTests.cs (new)
- docs/crew/handoffs/B051-mechanics.md
SUMMARY:
- FighterAI.Update -> internal EmitSmoke(dt): a live fighter with HitCount >= 1 spawns an enemy smoke puff at SmokeTailPoint (0.75 u opposite PlanePitch.NoseDirection, so it follows the flight direction and pitch). The interval is 0.25 s after 1 hit and 0.12 s from the 2nd hit; the 2nd hit thickens the trail at once. Nothing when the fighter is healthy, dead or paused (dt 0). ReEnter keeps the same object, damage and smoke, and a removed fighter stops by being destroyed.
- HitCount = max(damage increases seen per frame, damage total in player bullets rounded up). Pure helpers: SmokeInterval(hits, dead), HitsFromDamage(dmg%, maxHP, hitDamage), SmokeTailPoint(center, pitch, dir).
- FxManager: new Kind.EnemySmokePuff and SpawnEnemySmokePuff(Vector3). It uses the same sprite, motion and name ("SmokePuff") as the player's puffs, but has its own cap of 32. The player's SmokePuff cap stays at 24 and can never be recycled by enemy smoke.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes, PASS Biplanes.Editor, exit 0.
- Scratch test compile (B018 method): all 34 Tests/EditMode/*.cs, including FighterSmokeTests.cs, built with dotnet against a fresh runtime build. Two adjustments were needed: Unity's test csproj with ProjectReferences swapped for DLLs, and TargetFrameworkVersion raised to v4.7.2 so nunit.framework resolves. Result: 0 errors. Nothing was left in the project.
- The real EditMode suite is PENDING. The Unity editor has the project open, so `tools/unity.sh test` was not run. With Unity closed, run `UNITY_PATH="/d/Program Files/6000.6.5f1/Editor/Unity.exe" bash tools/unity.sh test`, then check FighterSmokeTests (7 tests: SmokeInterval_ByHitCount, HitsFromDamage_CountsBulletsRoundedUp, SmokeTailPoint_BehindTail_LeftRightAndPitch, EmitSmoke_HealthyNone_FirstHitSlow_SecondHitFast_PausedNone, EmitSmoke_DeadFighterNeverSmokes, EmitSmoke_RightFlyingAndReEntered_KeepsSmokingBehindTail, EnemySmoke_OwnCap_NeverStarvesPlayerSmoke) and confirm FxManagerTests still passes.
- Playtest: shoot a fighter once and it trails thin smoke from its tail. Shoot it a second time and the trail gets denser. The third hit makes it explode and the smoke stops. Check a right-flying reinforcement too: its smoke comes from its left (tail) side.
REQUESTS:
- orchestrator: ARCHITECTURE §3 FX (line ~218). Replace "`SpawnSmokePuff(Vector3)` (B051: also emitted by damaged enemy fighters; see FighterAI): smoke_puff, ... max 24" with "`SpawnSmokePuff(Vector3)` (player): smoke_puff, world space, rises 0.3 u/s, 1.0x->1.6x, fades over 1.2 s, max 24. `SpawnEnemySmokePuff(Vector3)` (B051, damaged fighters): identical puff as `Kind.EnemySmokePuff`, own cap 32, so enemy smoke never recycles the player's trail." The Kind list becomes Explosion, FlakBurst, SmokePuff, EnemySmokePuff.
- orchestrator: ARCHITECTURE §3 Enemies, FighterAI entry (line ~146). Add: "Smoke trail (B051): while alive and hit, `FxManager.SpawnEnemySmokePuff` at `SmokeTailPoint` (`SmokeTailOffset` 0.75 u opposite `NoseDirection`, so it follows the flight direction and pitch) every `SmokeInterval(HitCount)`: `SmokeIntervalLight` 0.25 s after 1 hit, `SmokeIntervalHeavy` 0.12 s from 2 hits (applied at once). None when healthy or dead, none while paused. `HitCount` = max(damage increases seen, `HitsFromDamage(dmg%, maxHP, player.bulletDamage)`). ReEnter keeps the smoke. Visual constants, not Balance tunables."
