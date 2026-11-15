TASK: B020
AGENT: mechanics
STATUS: DONE (7 files, all on the brief's WRITE list. That is 1 over the 6-file Claude limit, so I'm flagging it.)
FILES:
- Biplanes/Assets/Biplanes/Scripts/Enemies/FighterAI.cs
- Biplanes/Assets/Biplanes/Scripts/Enemies/BomberAI.cs
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs
- Biplanes/Assets/Biplanes/Scripts/Missions/ObjectiveTracker.cs
- tools/validate_missions.py
- Biplanes/Assets/Biplanes/Tests/EditMode/MissionDataRulesTests.cs
- Biplanes/Assets/Biplanes/Tests/EditMode/MissionEndRulesTests.cs
SUMMARY:
- P2-1 and P2-2 (code side), escort lookup:
  - New `FighterAI.IsEscortTarget(GameObject, id)` and `FighterAI.FindEscortTarget(id)` match whole ids only. A match is either an exact "[id]" token in the object's name, or a BomberAI whose `SpawnId == id`.
  - FighterAI.Start and BomberAI.Start both use this lookup. The old substring match is gone.
  - `BomberAI.Spawn(position, string id = null)` stores the id in `SpawnId`. MissionRunner passes `entry.id`.
  - The bomber keeps the name "bomber" on purpose: Health raises EntityDestroyed with `gameObject.name`, so renaming it to "bomber [id]" would break score and objective keys.
- P2-3, objective text: ObjectiveTracker.GetDescription now gives "Destroy all fighters", "Destroy all AAA guns", "Destroy the enemy bomber", "Destroy the enemy zeppelin" and "Bomb the enemy base". It never shows raw ids. New helper: `public static string ObjectiveTracker.ReadableType(typeKey, plural)`.
- P2-5, validator: `BOMBER_SPAWN_OFFSET = 8`, and a new error rule `ZEPPELIN_MIN_Y = 3` (zeppelin spawn y must be >= 3).
- Tests:
  - The M2 escort test now spawns the bomber with its id and checks with the runtime rule.
  - New tests: `EscortOf_MatchesWholeIdsOnly` ("zep" does not match zeppelinTurret or "[zep2]"), and `GetDescriptionFor_ReadsTypesNotRawIds`.
  - The expected text "Destroy all fighter" is now "Destroy all fighters".
VERIFY:
- `python tools/compile_check.py` → `PASS Biplanes (+6 new file(s) not yet in the csproj)`, `PASS Biplanes.Editor`, exit 0.
- `python tools/validate_missions.py` → exit 1, same as before. M2, M3 and M5 are OK (M5 zeppelin y is 4.9). M1 spawns[1]/[5] and M4 spawns[2]/[9] still fail only on the known "y must be in [0, 8.5]" items.
- The EditMode tests are not run, because compile_check doesn't build Tests and Unity is open. QA: scratch-build the tests as in T938, then run `bash tools/unity.sh test` when the editor is closed. These should be green:
  - `M2_EscortOf_NameLookupFindsTheBomber`
  - `M5_EscortOf_NameLookupFindsOnlyTheZeppelinHull` (id is now airship_01)
  - `EscortOf_MatchesWholeIdsOnly`
  - `GetDescriptionFor_UsesObjectiveText`
  - `GetDescriptionFor_ReadsTypesNotRawIds`
- Manual, M2: the escorts at camera 80/85 now close on the bomber instead of flying straight.
REQUESTS:
- orchestrator: ARCHITECTURE §5 `escortOf` needs a line saying it matches the whole spawn id: a "[id]" token in the entity name, or `BomberAI.SpawnId`. Substrings don't match.
- orchestrator: ARCHITECTURE §3 BomberAI needs its factory updated to `Spawn(Vector3 position, string id = null)` and the `SpawnId` property added.
- orchestrator: the validate_missions rules need two additions: zeppelin y >= 3, and a bomber near-edge offset of 8 (the 4:3 minimum).
- ui (optional): HudView.cs:307/323 builds its own "Destroy N more {label}s" text. It could use `ObjectiveTracker.ReadableType` to get plurals that match, for example "AAA guns".
