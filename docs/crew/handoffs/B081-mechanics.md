TASK: B081
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Enemies/ZeppelinAI.cs (DeathBurstPlan x spacing)
- Biplanes/Assets/Biplanes/Tests/EditMode/ZeppelinRamTests.cs (ThreeContacts_AfterGrace_Reach100_AndCrash assertion)
- docs/crew/handoffs/B081-mechanics.md
SUMMARY:
1. Plan_CoversTheWholeHull: this was a production bug in DeathBurstPlan, and the test's expectation was right. Both sides were spaced over n/2 rings, but with n = 12 the fore side has only 5 bursts (indices 2..10), so it stopped at ring 5/6 (x 3.408 instead of 3.975). Each side is now spaced over its own burst count (aft n/2, fore (n-1)/2), so both ends reach the centre ± spread. Timing, y offsets, sounds and the tail/gondola bursts are unchanged.
2. ThreeContacts_AfterGrace: B080 did not cause this, and the assertion was wrong. When the player's Health reaches 100% it always raises EntityDestroyed(gameObject.name = "PlayerPlane", Team.Player). That code is in Health.cs, which is committed and unchanged, and B080 touched neither Health nor PlayerPlane. B079 only compile-checked ZeppelinRamTests and never ran it in Unity (see its handoff), so this was the test's first real run. The test now asserts that the only event is the player's own death, and that neither "zeppelin" nor "zeppelinTurret" was raised. That keeps the B079 intent: the zeppelin and its turrets raise nothing from a ram.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes, PASS Biplanes.Editor, exit 0.
- `UNITY_PATH="/d/Program Files/6000.6.5f1/Editor/Unity.exe" bash tools/unity.sh test`: exit 0. Biplanes/Logs/editmode-results.xml reports total 771, passed 771, failed 0.
REQUESTS: none (the B080 ARCHITECTURE text still holds: bursts reach ±spread on both sides of the envelope centre).
