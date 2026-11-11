---
description: Resume Biplanes production from the task board, orchestrating the Claude crew
---
Act as the Biplanes orchestrator (CLAUDE.md). Resume production:

1. Read `docs/crew/TASKS.md` and every handoff in `docs/crew/handoffs/` newer than the last
   board update. Integrate them: update statuses, and turn REQUESTS and QA bugs into tasks.
2. Run the loop on the current milestone. Delegate to the `art`, `mechanics`,
   `mission-design` and `qa` subagents, run independent tasks in parallel, and have QA
   verify each code or mission task.
3. At the end, update TASKS.md and report:
   - Tasks completed.
   - Tasks blocked, and why.
   - What needs the user, such as a Unity Editor action or a scope call.

$ARGUMENTS
