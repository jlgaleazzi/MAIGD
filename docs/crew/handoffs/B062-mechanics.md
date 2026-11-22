TASK: B062
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Resources/Balance.json
- Biplanes/Assets/Biplanes/Scripts/Enemies/HealthBalloon.cs
- Biplanes/Assets/Biplanes/Tests/EditMode/MissionDataRulesTests.cs
- Biplanes/Assets/Biplanes/Tests/EditMode/HealthBalloonFullHealTests.cs (new)
SUMMARY:
- Balance `healthBalloonRestore` is now 100 (was 30). HealthBalloon's fallback is the new public const `FallbackRestore = 100f` (was the magic 30f). Collect still heals through `Health.ApplyDamage(-restore, DamageSource.Heal)`. No multiplier or resistance applies to that path, and Health clamps the result to 0..100, so the damage goes to exactly 0%. The death snap fires only on positive damage, and a balloon collected at full health is still used up and recycled.
- Tests: `M4AndM5_HealBudget_CoversTheV008PaperEstimate` is replaced by `M4AndM5_HealBudget_EveryBalloonIsAFullRepair`. It asserts that restore >= 100 and equals `HealthBalloon.FallbackRestore`, and it keeps the V008 counts (M4 >= 4 balloons and <= 5 AAA, M5 >= 6 balloons and <= 5 AAA). The other balloon placement tests are unchanged. New HealthBalloonFullHealTests: at 95% damage a balloon takes the player to 0% and PlayerDamageChanged reports 0; at full health the player stays at 0% and the balloon is still consumed; resistance doesn't scale a heal; a -250 heal clamps at 0. The other `-30f` Heal calls in the audio tests pass an explicit amount, don't read Balance, and still hold, so I left them alone.
- HUD/plane check found no bugs, so HudView.cs and PlayerPlane.cs are unchanged. HudView.OnPlayerDamageChanged works from the reported state alone: below smokeThreshold the bar is solid ink at alpha 1, flashing stops and the DAMAGE warning hides, and UpdateDamageWarningCue re-arms the latch. PlayerPlane's IsSmoking (puffs and the crackle loop) and DamageTint read CurrentDamagePercent every frame or fixed tick. A drop to 0% clears all of these at once. The existing HudBombIconsTests already cover the 80 -> 50 drop. A crashed (dead) plane can't be healed: ApplyDamage returns early when IsDead.
VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes, PASS Biplanes.Editor, exit 0. I didn't run `tools/unity.sh`: the editor has the project open (Temp/UnityLockfile).
- Tests compiled in a scratch build, not run. I used the B018 method with a scratchpad script: the runtime DLL was built with compile_check's helpers, and Biplanes.Tests.EditMode.csproj was retargeted to v4.7.2 with all 42 Tests/EditMode/*.cs files listed explicitly. Result: 0 errors. Nothing was left in the project. That build included the chase tests as they stood on disk at the time, while QA T967 was editing them in parallel.
- Pending, needs Unity closed: `bash tools/unity.sh test`. Check HealthBalloonFullHealTests (4 tests) and MissionDataRulesTests.M4AndM5_HealBudget_EveryBalloonIsAFullRepair.
- Play check: take damage past 70% (smoke, hatched flashing bar, DAMAGE text), then fly through a health balloon. The bar should empty to solid ink, and the smoke, crackle loop and DAMAGE text should stop. Crossing 70% again should replay the warning cue.
REQUESTS:
- orchestrator: none for docs. GDD line 87, the 2026-11-20 change-log row and ARCHITECTURE §4 (`healthBalloonRestore: 100.0`) already match.
- orchestrator (design note, optional): a falling wreck can still fly through a balloon and use it up. The heal does nothing because Health is dead. If that matters, HealthBalloon.Collect could skip a player whose Health.IsDead. It isn't in B062's scope.
