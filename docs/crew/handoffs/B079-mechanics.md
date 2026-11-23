TASK: B079
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs (new HitZeppelin: the zeppelin hull or a turret, checked first in OnTriggerEnter2D, costs damageTaken.planeCollision via DamageSource.PlaneCollision (50 / 1.5 = 33.3%) once per shared PlaneCollisionGraceSeconds; it crashes only at 100%; a dead zeppelin is ignored; doc comments updated)
- Biplanes/Assets/Biplanes/Scripts/Enemies/ZeppelinAI.cs (removed the fatal OnTriggerEnter2D ram: no Collision damage, no ForceCrash)
- Biplanes/Assets/Biplanes/Scripts/Enemies/ZeppelinTurret.cs (removed its fatal OnTriggerEnter2D ram. It also ForceCrashed the player and killed the turret itself, which would have kept the instant crash)
- Biplanes/Assets/Biplanes/Tests/EditMode/ZeppelinRamTests.cs (new: 5 tests)
- Biplanes/Assets/Biplanes/Tests/EditMode/PlaneCollisionTests.cs (stale ForceCrash_ZeppelinRam_StillCrashesAtOnce renamed to ForceCrash_StillCrashesAtOnce)
SUMMARY: A zeppelin ram (hull or any turret) is now one plane contact (33.3%). Hull, turrets and every other plane contact share one grace window, and the third contact crashes the plane. Hull, turrets and shield take no damage, and a ram raises no EntityDestroyed and no score. ForceCrash stays as a public API but nothing calls it in runtime code now.
  Stuck-inside behaviour: the plane only reacts to OnTriggerEnter2D (there is no Stay handler), so sitting inside the hull costs nothing beyond the entry, the same as a bomber. A long pass that enters another zeppelin collider (gondola or turret box) after the 0.5 s window is charged again, also the same as crossing a bomber's several hitboxes. I added no push-away. P2 for QA: decide in play whether a long pass that costs 2 contacts feels too harsh.
VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes, PASS Biplanes.Editor, exit 0. compile_check skips /Tests/, so the test files were not compiled by it. Unity batch was not run, as the brief said.
- QA: Unity Test Runner EditMode: ZeppelinRamTests (all 5), PlaneCollisionTests, BomberRamTests, BarrageBalloonCollisionTests, ZeppelinAndBaseTests.
- Play M4/M5 (zeppelin missions): fly into the hull, and you get 33.3% with no crash and the zeppelin intact. Fly into it 3 times, at least 0.5 s apart, and the plane crashes.
REQUESTS:
- orchestrator: ARCHITECTURE.md, in the plane-collision / zeppelin text: replace "ramming the zeppelin (hull or turret) crashes the player / ForceCrash" with "Zeppelin hull or turret contact (B079): one plane contact, damageTaken.planeCollision via DamageSource.PlaneCollision (33.3% after resistance), sharing PlaneCollisionGraceSeconds with fighters, bombers and barrage balloons. The zeppelin, its turrets and its shield take no damage, and there is no score or EntityDestroyed. Handled in PlayerPlane.HitZeppelin; ZeppelinAI and ZeppelinTurret have no trigger handler." Also drop zeppelins from the "still fatal" list.
- orchestrator: in the GDD Change Log, the zeppelin ram is no longer fatal (33.3% like other plane contacts).
