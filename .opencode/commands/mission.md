---
description: Design, wire and verify a single mission end to end. Usage /mission <1-5>
agent: orchestrator
---
Deliver mission $1 end to end:
1. Check that every entity type and objective type mission $1 needs is implemented
   (grep Scripts/). Create mechanics or art tasks for anything missing, and run them first.
2. Delegate `mission-design` to write mission_0$1.json and docs/missions/mission_0$1.md.
3. Delegate `qa` to verify it (validate_missions, the JSON parse test, compile, and a
   manual playtest checklist for this mission).
4. Route any bugs to their owners, re-verify, and update TASKS.md.
