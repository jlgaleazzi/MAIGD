# Style loop report (2026-11-17 14:42)

Command: `audit --fix`, backend: openai gpt-oss:120b, rules from `docs/GDD.md` (16 rules, 7 colours, 25 banned terms).

| Artifact | Status | Violations at start | Left | Rounds |
|---|---|---|---|---|
| mission 1 | clean | N-VOICE x1 | none | 2 |
| mission 2 | clean | N-FAITHFUL x1, N-VOCAB x1, N-VOICE x1 | none | 1 |
| mission 3 | clean | N-FAITHFUL x1, N-VOCAB x1, N-VOICE x1 | none | 1 |
| mission 4 | clean | N-VOCAB x1, N-VOICE x1 | none | 2 |
| mission 5 | clean | N-VOICE x1 | none | 1 |

Files written: `Biplanes/Assets/Biplanes/Resources/Missions/mission_01.json`, `Biplanes/Assets/Biplanes/Resources/Missions/mission_02.json`, `Biplanes/Assets/Biplanes/Resources/Missions/mission_03.json`, `Biplanes/Assets/Biplanes/Resources/Missions/mission_04.json`, `Biplanes/Assets/Biplanes/Resources/Missions/mission_05.json`

## mission 1: clean

Found:
- [N-VOICE] briefing: The briefing does not address the pilot with "you"; it lacks the required second‑person address. To comply, rewrite the sentences to include "you" (e.g., "You see a wave of enemy fighters inbound on the airfield. You must take off and shoot them all down before they reach our position!") (needs the model)

| Round | Action | Before | After | Kept | Note |
|---|---|---|---|---|---|
| 1 | model refine | 1 | 1 | no | it left 1 violation(s) (was 1): [N-VOICE] briefing: The briefing does not end with a clear commander’s order; it describes actions ('You take off and shoot them') instead of issuing an imperative command to the pilot, violating the requirement that the last sentence give the order and name the targe |
| 2 | model refine | 1 | 0 | yes |  |

Change:

- briefing before: Wave of enemy fighters inbound on the airfield. Take off and shoot them all down before they reach our position!
- briefing after:  You see a wave of enemy fighters inbound on the airfield. Take off and destroy all enemy fighters before they reach our position!

## mission 2: clean

Found:
- [N-VOCAB] briefing: 'payload' is not period vocabulary; use 'bombs'
- [N-FAITHFUL] briefing sentence 2: Uses the term "payload", which is not the bomb described in the GDD and counts as an invented weapon; replace with "bomb" to match the mission description. (needs the model)
- [N-VOICE] briefing sentences: The briefing does not address the pilot with "you" and is written in third‑person; rewrite to use second‑person (e.g., "You see an enemy bomber..."), keeping the threat first and the order last. (needs the model)

| Round | Action | Before | After | Kept | Note |
|---|---|---|---|---|---|
| 1 | code fix N-VOCAB x1 | 3 | 1 | yes |  |
| 1 | model refine | 1 | 0 | yes |  |

Change:

- briefing before: An enemy bomber with fighter escort is approaching our airfield. Destroy it before it drops its payload on us!
- briefing after:  You see an enemy bomber with fighter escort approaching our airfield. Destroy it before it drops its bombs on us!

## mission 3: clean

Found:
- [N-VOCAB] briefing: 'AAA batteries' is not period vocabulary; use 'anti-aircraft batteries'
- [N-FAITHFUL] briefing: Uses prohibited term "AAA batteries" (and "AAA") instead of the allowed wording "anti‑aircraft batteries" and includes a disallowed vocabulary word. Replace with "anti‑aircraft batteries" and keep only the permitted enemy types (aaa, fighter, healthBalloon). (needs the model)
- [N-VOICE] briefing: The briefing begins with an order ("Fly into enemy territory and destroy…") rather than describing the threat first, and the command is not placed at the end. Reorder the sentences so the threat (enemy AAA and fighters firing) is described first, then end with a second‑person present‑tense command to the player. (needs the model)

| Round | Action | Before | After | Kept | Note |
|---|---|---|---|---|---|
| 1 | code fix N-VOCAB x1 | 3 | 1 | yes |  |
| 1 | model refine | 1 | 0 | yes |  |

Change:

- briefing before: Fly into enemy territory and destroy the AAA batteries with bombs. Dodge their fire and enemy aircraft while you get close enough to bomb!
- briefing after:  Dodge their fire and enemy aircraft while you get close enough to bomb! Fly into enemy territory and destroy the anti-aircraft batteries with bombs.

## mission 4: clean

Found:
- [N-VOCAB] briefing: 'AAA guns' is not period vocabulary; use 'anti-aircraft guns'
- [N-VOICE] briefing: The briefing does not address the pilot with "you", starts with a directive rather than stating the threat, and therefore fails the commander‑dispatch format (second‑person, present tense, threat first, order last). (needs the model)

| Round | Action | Before | After | Kept | Note |
|---|---|---|---|---|---|
| 1 | code fix N-VOCAB x1 | 2 | 2 | yes |  |
| 1 | model refine | 2 | 1 | yes |  |
| 2 | model refine | 1 | 0 | yes |  |

Change:

- briefing before: Go deep into enemy territory. Pop the barrage balloons, destroy the AAA guns, and finally drop a bomb on their hangar base!
- briefing after:  Barrage balloons and anti-aircraft guns guard the enemy territory you must fly through. You drop a bomb on the enemy base.

## mission 5: clean

Found:
- [N-VOICE] briefing: The briefing does not address the pilot with "you"; it uses third‑person phrasing ("The enemy...") instead of a commander’s direct address. To satisfy N-VOICE, rewrite the briefing to start with "You" (e.g., "You see the enemy deploy its newest and most powerful weapon…"). (needs the model)

| Round | Action | Before | After | Kept | Note |
|---|---|---|---|---|---|
| 1 | model refine | 1 | 0 | yes |  |

Change:

- briefing before: The enemy deploys its newest and most powerful weapon — a massive zeppelin with anti-aircraft turrets and a fighter escort. Destroy it before it reaches our HQ!
- briefing after:  You see the enemy deploy its newest and most powerful weapon—a massive zeppelin with anti-aircraft guns and a fighter escort. Destroy it before it reaches your HQ!

