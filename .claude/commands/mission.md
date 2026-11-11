---
description: Design, wire and verify one Biplanes mission end to end. Usage /mission <1-5>
---
Act as the Biplanes orchestrator (CLAUDE.md). Deliver mission $ARGUMENTS end to end:

1. Check that every entity type and objective type the mission needs is implemented (grep
   `Biplanes/Assets/Biplanes/Scripts`). Create `mechanics` or `art` tasks for anything
   missing, and run them first (in parallel where they're independent).
2. Delegate `mission-design` to write `mission_0$ARGUMENTS.json` and
   `docs/missions/mission_0$ARGUMENTS.md`.
3. Delegate `qa` to verify it: `validate_missions`, the JSON parse test, compile, and a
   manual playtest checklist for this mission.
4. Route any bugs to their owners, re-verify, and update `docs/crew/TASKS.md`.
