# Goal agent

A goal-oriented coding agent for Biplanes. The goal is "every GDD feature exists in the
code". It reads `docs/GDD.md`, scans the Unity project, finds what's missing and writes it,
one gap at a time, until nothing writable is left.

```
python tools/goal_agent --dry-run                  # what's missing, in priority order (no backend needed)
python tools/goal_agent --backend anthropic        # write it with Claude (needs ANTHROPIC_API_KEY)
python tools/goal_agent --backend openai           # write it with the local Ollama model (qwen3.6-crew)
python tools/goal_agent --backend openai --base-url http://spark-austin-1.local:11434/v1 --model gpt-oss:120b
                                                   # write it with a remote Ollama model (the Spark)
python tools/goal_agent --backend scaffold --verify none   # offline wiring test: placeholder stubs only
```

Run it from the repo root (`D:\Developer\MAIGD`, the folder that holds `tools/`), not from
`Biplanes/`. `spec.json` holds 36 requirements, all met as of the last run (see `RESULTS.md`
for what the agent built, why it picked each feature, and the Spark runs).

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
   `Resources/Missions/` or `Balance.json`; never `.meta`; one type per file (a new type may join a file that already holds several, such as
   `GameBalance.cs`), file name = type name, namespace `Biplanes.*`; valid JSON; no `...` placeholders; at most `--max-files` distinct files (a PATCH and a
   FILE on the same path count once);
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

### Tips for local models

- Keep each requirement to 3 files or fewer; `spec.json` splits big features into steps
  with `deps` (for example the six `PLANE-PITCH-*` / `BULLET-NOSE-*` steps).
- Replies may put the `FILE`/`PATCH`/`MERGE` headers inside or outside the code fence; both
  parse. Existing files are shown to the model up to 60000 characters.
- When a reply is rejected, rerun with `--save-replies tools/goal_agent/replies` and read
  `<ID>_attemptN_reply.txt`. That folder is gitignored.
- After the agent writes code, QA it: the pitch run needed nine fixes (`docs/qa/T942-verify.md`).

## What it has built on the Spark

`gpt-oss:120b` on the Spark (`spark-austin-1.local`) closed all six airplane-pitch steps on
2026-11-15, mostly on the first attempt:

| Step | What it added |
|---|---|
| `PLANE-PITCH-CORE` | `PlanePitch.Step` easing helper; `PitchData` and the `pitch` block in `GameBalance.cs` and `Balance.json` |
| `PLANE-PITCH-PLAYER` | The player's biplane tilts nose up when climbing, nose down when diving |
| `PLANE-PITCH-FIGHTER` | Enemy fighters tilt the same way |
| `PLANE-PITCH-BOMBER` | Enemy bombers tilt the same way |
| `BULLET-NOSE-PLAYER` | Player bullets leave along the nose (`PlayerPlane.NoseDirection`) |
| `BULLET-NOSE-ENEMY` | Fighter shots and the fighter's nose agree (`FighterAI.NoseDirection`) |

The Qwen models tried earlier produced nothing usable: replies were rejected for format, a
truncated `Balance.json`, or too many files. Fixing those rejections in the agent is what let
`gpt-oss:120b` succeed. QA then found nine bugs in the generated code (for example the
player's gun fired from the tail when facing left); they were fixed by hand and QA's re-check
passed. Nothing has run in Unity yet. Details are in `RESULTS.md`.

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

62 tests, all offline (no model, no Unity).
