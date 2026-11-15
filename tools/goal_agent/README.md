# Goal agent

A goal-oriented coding agent for Biplanes. The goal is "every GDD feature exists in the
code". It reads `docs/GDD.md`, scans the Unity project, finds what's missing and writes it,
one gap at a time, until nothing writable is left.

```
python tools/goal_agent --dry-run                  # what's missing, in priority order (no backend needed)
python tools/goal_agent --backend anthropic        # write it with Claude (needs ANTHROPIC_API_KEY)
python tools/goal_agent --backend openai           # write it with the local Ollama model (qwen3.6-crew)
python tools/goal_agent --backend scaffold --verify none   # offline wiring test: placeholder stubs only
```

Other options: `--model`, `--base-url`, `--max-iterations N`, `--max-attempts N`,
`--verify compile|none`, `--report FILE`, `--show-unmapped`, `--spec FILE`,
`--save-replies DIR` (keep every prompt and raw model reply, to see why a reply was rejected),
`--max-files N` (most files one write may touch; default 6 for Claude, 3 for local models, as in AGENTS.md).
Exit code: 0 every requirement met, 1 requirements still open, 2 bad setup.

## How it works

| Part | Where |
|---|---|
| Requirements: GDD feature -> evidence the code must contain | `spec.json`, `requirements.py` |
| Reads the GDD; checks every spec anchor still exists in it; lists GDD items nothing tracks | `gdd.py` |
| Scan: files, declared C# types, JSON, assets, placeholder markers | `scanner.py` |
| **Gap-Detection Pass**: requirements x scan -> missing / partial / stub | `gaps.py` |
| **`priority_score()`**: which gap to close next | `scoring.py` |
| **Code-Writing Step**: prompt, parse, validate, apply, undo | `writer.py`, `backends.py` |
| **Iteration Loop**: scan, detect, rank, write, verify, re-scan, stop | `agent.py` |

### priority_score

```
value = 10*impact + 8*core + 4*(unmet requirements it unblocks, transitively)
cost  = effort * (1 + 0.5*failed_attempts) * (1 - 0.5*progress)
score = value / cost          # 0.0 while a dependency is unmet: never picked
```

### A write is kept only if

1. the reply parses and validates: paths inside `Scripts/`, `Editor/`, `Tests/`,
   `Resources/Missions/` or `Balance.json`; never `.meta`; one type per file, file name =
   type name, namespace `Biplanes.*`; valid JSON; no `...` placeholders; at most 6 files;
   big existing C# files change through PATCH blocks, existing JSON files through MERGE blocks
   (only the new keys, deep-merged), never rewrites;
2. `tools/compile_check.py` passes (`--verify compile`, the default; skipped with a note
   when it can't run, for example no `dotnet`);
3. a re-scan shows the gap closed or further along;
4. no requirement that was met before is broken.

Otherwise the files are restored and the reason goes back to the backend on the next
attempt. A gap that fails twice in two visits is marked stuck.

### Stop reasons

`complete` (all met), `nothing_to_write` (what's left is art, blocked, stuck or a stub),
`max_iterations`, `backend_error`, `dry_run`.

## What it will not do

- Art assets, scenes, prefabs, `.meta` files (other roles own them; they show up as "needs
  the art agent").
- Decide design. It only builds what `spec.json` ties to a GDD phrase. Add a requirement to
  `spec.json` for a new GDD feature; `--show-unmapped` lists GDD items nothing covers.
- Prove the game plays. Compile checks and file evidence say the code is *there*; run the
  EditMode tests and playtest in Unity.

## Tests

```
cd tools && python -m unittest goal_agent.tests.test_goal_agent -v
```
