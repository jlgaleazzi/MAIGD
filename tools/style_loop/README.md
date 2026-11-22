# Style loop

A self-correcting **Generator → Evaluator → Refiner** loop that enforces the Biplanes
Style Guide on art and on the words the player reads. The rules are not written into the
code. They are read from the **"Style Guide" section of [`docs/GDD.md`](../../docs/GDD.md)**
on every run.

```
python tools/style_loop audit                     # evaluate every art master and mission text
python tools/style_loop audit --fix               # ... and fix what code can fix
python tools/style_loop audit --fix --backend openai --base-url http://spark-austin-1.local:11434/v1 --model gpt-oss:120b
                                                  # ... and let the model fix the rest
python tools/style_loop generate briefing --mission 3 --backend anthropic
python tools/style_loop generate svg --asset fx/smoke_trail --request "a thin trailing smoke wisp" --backend anthropic
```

Run it from the repo root. Options: `--backend none|anthropic|openai` (the default `none` runs
the code checks and fixes only), `--model`, `--base-url`, `--max-iterations N` (default 4),
`--no-judge`, `--dry-run`, `--rasterize` (re-render PNGs after SVGs change), `--report FILE`
(default `tools/style_loop/last_run.md`), `--only svg|text`, or file paths to audit only those.
Exit code: 0 everything obeys the Style Guide, 1 violations remain, 2 bad setup.

## Where the rules come from

`guide.py` parses four things out of the GDD's Style Guide section:

| GDD part | Becomes |
|---|---|
| Palette table (`Colour / Hex / Used for`) | The allowed colours, and which of them are accents |
| Narrative-voice vocabulary table (`Use / Instead of`) | Banned terms and their period replacements |
| Enforced rules table (`ID / Applies to / Rule / Check / Parameters`) | Which rules run, on what, with which numbers (max stroke, max words, accent owners...) |
| The whole section's prose | What the model reads when it generates, judges and refines |

Change a number in the GDD table, for example `max_words=40`, and the next run enforces the
new value. Remove a row and that rule stops. A rule whose `Check` is `judge`, or whose ID has
no code check, is judged by the model against the prose.

## The loop

```
             ┌────────────── rolled back + reason ──────────────┐
             v                                                   │
Generator ─> Evaluator ──violations──> Refiner ──candidate──> Evaluator ──fewer?──> keep
 (model)     code checks (auto rules)   1. code fixes (no model)              │
             + model judge (judge rules) 2. model, for the rest               └─ none left: clean
```

- **Generator** (`loop.Generator`) asks the model for a first candidate, either an SVG master
  or a mission title and briefing. The prompt holds the Style Guide text and the list of rules
  that will be checked.
- **Evaluator** (`loop.Evaluator`, `rules.check`) runs a code check for every `auto` rule. It
  resolves SVG inheritance, opacities, transform scale and SVG's default black fill. It asks the
  model to judge the `judge` rules and returns structured violations.
- **Refiner** (`loop.Refiner`) first applies the deterministic code fix for each fixable rule.
  For example, it snaps a colour to the nearest allowed one, turns a fill into a stroke, clamps
  a stroke width, fades scenery ink, or swaps a modern word for its period word. It then gives
  whatever is left (unfixable and judged violations) to the model, with the violations and the
  rules in the prompt.
- **Guard rails on the model:** the model's candidate goes through the code fixes before it is
  judged, so a slip such as "payload" is corrected, not rolled back. A title with no title
  violation is restored if the model renamed it. Unicode hyphens become plain `-`, because the
  UI font may not have them. The prompt names the enemies the mission really spawns and the
  banned words.
- **Self-correction:** every candidate is evaluated again before it is kept. A step that breaks
  the file, or doesn't reduce the violation count, is rolled back, and the reason goes into the
  next refine prompt. Code fixes are accepted on the code checks alone, so a noisy judge can't
veto a correct deterministic fix, and the judge counts once per rule it flags. The loop stops when nothing is left (`clean`), after two rounds without
  progress (`stalled`), at `--max-iterations`, or, with no backend, when only model work is left
  (`needs_model`).
- **Minimal edits:** code fixes patch only the changed attributes in the original SVG text, so
  comments, formatting and attribute order survive. Mission files change only their `title` and
  `briefing` values.
- **Generator scripts own their output:** SVGs in a folder with a `gen_*.py` (the pencil FX) are
  reported but not edited, because the next regeneration would undo the fix. The report says to
  fix the script.

## Rules enforced today

| ID | What the code checks | Code fix |
|---|---|---|
| A-PALETTE | Every declared colour is in the palette. Shapes left to SVG's default black fill count too. Mask luminance gradients are exempt | Nearest allowed colour |
| A-ACCENT | Each accent appears only in the assets its parameters name | Ink stroke, no fill |
| A-NO-FILL | Fills only for faction marks, accents, paper, and shapes no thicker than a line (tufts, rivets) | Fill becomes a stroke |
| A-LINE-WEIGHT | Stroke width at sprite size, through transforms and `<use>`, at most `max_px` | Clamp the width |
| A-BG-QUIET | Backgrounds: no accents, ink at most `max_ink_opacity` | Pencil instead of the accent, element opacity lowered |
| A-NO-TEXT | No `<text>` in art | (model) |
| N-VOCAB | No term from the "Instead of" column | Period word from the "Use" column, or (model) when it says "(remove)" |
| N-LENGTH | Sentence and word counts | (model) |
| N-EXCLAIM | At most `max` exclamation marks | Keep the last ones |
| N-TITLE | Word count, Title Case | Capitalise |
| N-TARGET | The briefing names each objective's target | (model) |
| N-GROUNDED | The briefing names no enemy type that isn't spawned in the mission | (model) |
| A-FX-SKETCH, A-FACE-RIGHT, N-VOICE | Judged by the model | (model) |

## Results on the game (2026-11-17, Spark `gpt-oss:120b`)

`audit --fix` over all 45 assets (40 SVG masters, 5 mission texts):

| Artifact | Found | Fixed by | Outcome |
|---|---|---|---|
| `obstacles/barrage_balloon` | A-LINE-WEIGHT: a 2 px dotted line | code (clamped to 1.5 px), PNG re-rendered | clean |
| `obstacles/building_barn` | A-LINE-WEIGHT x2: 2 px dotted lines | code, PNG re-rendered | clean |
| missions 2, 3, 4 | N-VOCAB: "payload", "AAA batteries", "AAA guns" | code (bombs, anti-aircraft ...) | clean |
| mission 3 | N-TARGET: no target named | resolved by the vocab fix ("anti-aircraft") | clean |
| missions 1-5 | N-VOICE / N-FAITHFUL (judge): no "you", order not last | model refine, 1-2 rounds | clean |
| `fx/*` (pencil FX) | A-FX-SKETCH (judge) on 2 flak frames | none: generator-owned, report only | open, judge opinion |

Reports: `RUN_spark_first_pass.md` (art fixes and the first text attempt, since reverted)
and `RUN_spark_text.md` (the final mission texts). The PNGs and `rasterize.py --check` are
40/40 OK after the fixes.

What the self-correction caught while this was built:
- The first text runs produced briefings with enemies that aren't in the mission, such as a
  zeppelin in mission 4. That led to the `N-GROUNDED` code rule and the `N-FAITHFUL` judge rule.
- A rewrite that cleared two judge opinions but added a zeppelin was kept by the first scoring
  rule. Now a candidate that breaks any new code-checked rule is never kept.
- The real cause of the made-up enemies was a parser bug: the model was given the GDD's numbered
  *Enemies* list instead of the *Missions* list. With the correct paragraph, the edits became
  minimal and faithful.
- The model judge on raw SVG geometry is unreliable (it called the triple-contour flak frames
  "single clean strokes"). Art judgments are reported, not auto-applied; code rules carry the
  art enforcement.

## Files

| File | Role |
|---|---|
| `guide.py` | Parses the GDD Style Guide into a `StyleGuide` |
| `artifacts.py` | `SvgArtifact`, `TextArtifact`, loading, minimal-diff saving |
| `svgstyle.py` | Resolved SVG paint: inheritance, opacity, scale, default fill, shape thickness |
| `rules.py` | Code checks and code fixes per rule ID |
| `loop.py` | Generator, Evaluator, Refiner, prompts, the loop |
| `report.py` | The Markdown report (`last_run.md`) |
| `tests/test_style_loop.py` | Offline tests, including a fake model for the loop paths |

The model backends are shared with `tools/goal_agent` (`backends.py`: Anthropic Messages API,
or any OpenAI-compatible server such as the Spark's Ollama).

## Tests

```
cd tools && python -m unittest style_loop.tests.test_style_loop -v
```
