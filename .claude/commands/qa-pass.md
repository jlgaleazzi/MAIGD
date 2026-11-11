---
description: Run a full Biplanes QA milestone pass with the qa agent. Usage /qa-pass <M#>
---
Delegate a full milestone pass for milestone $ARGUMENTS to the `qa` subagent, as its
instructions describe: compile, tests, builds, data checks and a manual playtest
checklist. It writes `docs/qa/$ARGUMENTS-report.md` and a handoff.

Then integrate the result as orchestrator: turn every bug into a task on
`docs/crew/TASKS.md` for its owner, and report the verdict and the open P0/P1 bugs.
