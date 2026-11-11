# Biplanes crew: shared rules (every agent reads this)

We're building **Biplanes**, a WWI side-scrolling shooter in **Unity 6 LTS** for
Windows, macOS and WebGL. The design lives in `docs/GDD.md`, and the binding technical
contract lives in `docs/ARCHITECTURE.md`.

## The crew
| Agent | Mode | Owns |
|---|---|---|
| `orchestrator` | primary | Plan, task board, GDD/architecture/UI spec, delegation, integration |
| `art` | subagent | SVG masters, sprites, art style |
| `mechanics` | subagent | All C# runtime and editor code, Balance.json, builds |
| `mission-design` | subagent | The 5 mission JSON files, mission docs, balance tuning proposals |
| `qa` | subagent | Tests, compile/build verification, bug reports, playtest checklists |

The crew runs in two harnesses that share this file, the board, the handoffs and the file
ownership:
- **opencode** (`.opencode/agents/`) runs on the local model.
- **Claude Code** (`.claude/agents/`) runs the same four subagents, and the main session
  plays `orchestrator` (see `CLAUDE.md`). Claude crew tasks may touch up to 6 files.

## Working rules (this crew runs on a local 35B model, so keep things small and explicit)
1. **One task at a time.** A task touches **at most 3 files**. If yours needs more, finish
   the first 3, then report `STATUS: PARTIAL` and list what's left.
2. **Read before you write.** Read only the docs your task brief names, plus the files you
   will edit. Don't scan the whole repo.
3. **Stay in your lane.** Write only to paths your role owns (ARCHITECTURE.md §2). If you
   need a change elsewhere, ask for it in the handoff under `REQUESTS:`.
4. **Never invent APIs.** Use only the class, method and event names in
   `docs/ARCHITECTURE.md` or in files that already exist. If something is missing, say so
   under `REQUESTS:`.
5. **Never hand-write Unity YAML** (.unity/.prefab/.asset/.meta). See ARCHITECTURE §0.
6. **Write whole files.** When you create a file, write the complete file. No `// ...`
   placeholders and no "rest unchanged".
7. **Finish with a handoff** at `docs/crew/handoffs/<TASK-ID>-<agent>.md`:

```
TASK: T012
AGENT: mechanics
STATUS: DONE | PARTIAL | BLOCKED
FILES: <every file created or changed>
SUMMARY: <3 lines max>
VERIFY: <the exact command or steps QA should run>
REQUESTS: <changes needed from other agents, or "none">
```

Then reply to the orchestrator with only the handoff content.

## Environment
- Shell: bash. Unity project root: `Biplanes/`. Python 3 for tools.
- `tools/unity.sh <compile|test|scenes|webgl|windows|mac|all>` runs Unity in batch mode
  when `UNITY_PATH` is set. If it exits with code 3 ("Unity not available"), that's not a
  failure. Note it in the handoff, and QA falls back to static checks.
- `python tools/compile_check.py` compile-checks both assemblies with dotnet against Unity's
  compiled DLLs. It works even while the Unity editor has the project open. Exit codes:
  0 = pass, 1 = errors, 3 = can't run.
