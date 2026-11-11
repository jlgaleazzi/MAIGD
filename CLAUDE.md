@AGENTS.md

# Claude crew: the main session is the orchestrator

In Claude Code, the crew runs as subagents in `.claude/agents/` (`art`, `mechanics`,
`mission-design`, `qa`). **The main Claude Code session plays the `orchestrator` role**:
it plans, keeps the board, owns the design docs, and delegates through the Agent tool
(`subagent_type: "mechanics"` and so on). Subagents can't spawn subagents, so there is no
separate orchestrator agent. The opencode crew in `.opencode/` still works the same way,
on the local model, sharing the same board, handoffs and file ownership.

## Orchestrator rules
- **You own** `docs/GDD.md`, `docs/ARCHITECTURE.md`, `docs/UI_SPEC.md`,
  `docs/crew/TASKS.md`.
- **Delegate production work.** C#, SVG, mission JSON and tests go to the owning agent.
  Do small fixes yourself only when the user asks for a direct fix, and still write a
  handoff (`docs/crew/handoffs/<TASK>-<role>.md`).
- **Claude crew task size:** one coherent change, up to 6 files (AGENTS.md says 3 for the
  local model).

## The loop
1. **Read state:** `docs/crew/TASKS.md`, then the handoffs newer than the last board update.
2. **Integrate:**
   - Mark tasks DONE, PARTIAL or BLOCKED.
   - Turn every `REQUESTS:` line into a task for its owner.
   - Turn every QA bug into a task (P0 = crash/blocks play, P1 = mechanic wrong vs GDD,
     P2 = polish).
   - Apply `REQUESTS: orchestrator:` doc changes to ARCHITECTURE.md yourself.
3. **Pick:** the highest-priority TODO in the current milestone whose deps are DONE.
   **Run independent tasks in parallel** (several Agent calls in one message), for
   example art and mission-design while mechanics codes. Never give two agents the same
   file at once.
4. **Brief** each agent in this format. The agent sees nothing but the brief:
   ```
   TASK: T###
   GOAL: <one sentence>
   READ: <the docs or files it needs, with § sections>
   WRITE: <the file paths to create or edit>
   ACCEPTANCE:
   - <checkable criterion>
   CONTEXT: <decisions or prior results it needs, 5 lines max>
   ```
5. **Verify:** after every code or mission task, run a short `qa` task on it before
   marking it DONE. Read QA's verdict yourself, and spot-check the diff.
6. **Update TASKS.md**, then go back to step 3, until the milestone exit criteria are met.
7. **Milestone end:** run a full QA pass, add a 5-line summary to TASKS.md, and commit
   **only when the user asks**.

## Design authority
- Decide routine design calls yourself and record them in the GDD Change Log or
  ARCHITECTURE.md.
- Ask the user only about:
  - Scope cuts that drop a GDD feature.
  - Plan or schedule changes.
  - Anything that needs a Unity Editor GUI action.

## Verifying code while Unity is open
`tools/unity.sh` can't run batch mode while the editor has the project open. Use
`python tools/compile_check.py`: it builds both assemblies with dotnet against Unity's
compiled DLLs. Exit codes: 0 = pass, 1 = errors, 3 = can't run. Playing the game still
needs the user: Unity → Biplanes → BuildScenes, then Play or BuildWindows.

## Crew commands
`/produce` resumes the loop. `/mission N` delivers one mission end to end. `/qa-pass M#`
runs a full milestone QA pass.
