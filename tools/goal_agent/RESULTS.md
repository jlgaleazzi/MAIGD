# What the goal agent built, and why it picked each feature

## Short version

The goal agent is a tool. It reads `docs/GDD.md`, scans the Unity project, finds which GDD
features are missing and ranks them with `priority_score()`. It never wrote game code
itself: it has not been run against a live model (no API key, no reachable Ollama at the
time). The features below were written by the Claude crew (mechanics, mission-design,
art, QA), working through the agent's ranked list in the order it gave.

So there are two separate things:

1. **The agent** (`tools/goal_agent/`, about 2,200 lines, 54 passing unit tests): gap
   detection, priority scoring, code-writing step with safety gates, iteration loop, three
   backends (Claude, local/remote Ollama, offline scaffold).
2. **The features it asked for**, delivered by the crew and checked by QA.

## What the agent found

First dry run on the real repo: 28 requirements, 20 already met, 8 open.
Ranked by `priority_score()`:

| # | Gap | Status | Score | Why it landed there |
|---|---|---|---|---|
| 1 | Enemy base (ground target the player bombs) | partial | 22.7 | Impact 3, effort 2, and it unblocks the spawn step. Already half built, so cost is only 1.5 |
| 2 | Health balloons restore health | stub | 20.0 | Impact 3, effort 2. A stub counts as half done, so cost is 1.5 |
| 3 | Player HQ (what the zeppelin attacks) | missing | 12.0 | Cheap (effort 2) but lowest impact (2), unblocks one step |
| 4 | Main menu with mission select | missing | 10.0 | Impact 3 but effort 3, and nothing waits on it |
| 5 | Zeppelin with turrets | missing | 8.5 | Biggest feature (effort 4), so the lowest score of the open ones |
| 6-8 | MissionRunner spawns base, HQ, zeppelin | missing | 0.0 | Blocked until the thing they spawn exists |

## How the score is calculated

```
value = 10*impact + 8*core + 4*(unmet requirements it unblocks)
cost  = effort * (1 + 0.5*failed_attempts) * (1 - 0.5*progress)
score = value / cost        # 0 while a dependency is unmet
```

Worked example, enemy base: value = 10*3 + 4*1 = 34. It is half built, so progress is 0.5
and cost = 2 * (1 - 0.5*0.5) = 1.5. Score = 34 / 1.5 = 22.7. The same formula gives 20.0 for the health balloon (30 / 1.5), 12.0 for the HQ
(24 / 2), 10.0 for the menu (30 / 3) and 8.5 for the zeppelin (34 / 4).

In short, the agent prefers features that are valuable, cheap, already partly built, and
that unlock other work. It leaves blocked items until their dependency lands.

## What was delivered (all compile; none played yet)

- Enemy base, player HQ, zeppelin with three turrets and a shielded hull, health balloon
  healing (the stub that threw `NotImplementedException` is gone).
- Main menu with mission select; MissionRunner spawns the base, HQ and zeppelin.
- Objective types that actually complete or fail. Before this, missions 1-3 could not be won.
- Later, driven by your playtest, and added to the agent's spec as requirements: enemy
  fire, and the new controls (left mouse fires, Space bombs).

Dry run before the pitch requirements below: 30 requirements, 30 met. "Met" means the code evidence is there (the
class exists, the file contains what the check looks for). It does not prove the game
plays: run the EditMode tests and playtest in Unity.

## Open work for the agent: airplane pitch (2026-11-15)

The GDD got a "Flight attitude (pitch)" section, and the work was added to `spec.json` as
six small steps (at most 3 files each, the local-model task size in AGENTS.md). A first
attempt as two big requirements (6 files in one write) failed on the Spark model: it forgot a
field, added a second `Awake`, or went over the file limit. Smaller steps, and the same order
the scoring uses:

| Step | What it asks for | Depends on |
|---|---|---|
| `PLANE-PITCH-CORE` | `PlanePitch.Step(...)` helper, `PitchData` + `pitch` field in `GameBalance.cs`, `pitch.maxDegrees` in `Balance.json` | none (ranked first, score 30) |
| `PLANE-PITCH-PLAYER` | The player's biplane pitches (sprite and hitboxes, mirrored when facing left) | CORE |
| `PLANE-PITCH-FIGHTER` | Enemy fighters pitch | CORE |
| `PLANE-PITCH-BOMBER` | Enemy bombers pitch | CORE |
| `BULLET-NOSE-PLAYER` | Player bullets leave along the nose (`PlayerPlane.NoseDirection`) | PLAYER |
| `BULLET-NOSE-ENEMY` | Enemy shots and the fighter's nose agree (`FighterAI.NoseDirection`) | FIGHTER, BOMBER |

Built on the Spark with `gpt-oss:120b` (2026-11-15): all six steps closed, mostly on the first
attempt. Now 36 requirements, 36 met. QA (T942) then found nine bugs in that code, among them the
player's gun firing from the tail when facing left; they were fixed by hand (B029) and QA T943
passed. Nothing has run in Unity yet.

Side effect to re-check once bullets follow the nose: player bullets were strictly
horizontal, and the zeppelin turret line-of-fire geometry (T939/B021) and the enemy-fire
cone assumed that. QA must re-verify both.

## Run it

```
python tools/goal_agent --dry-run
python tools/goal_agent --backend openai --base-url http://<host>:11434/v1 --model <model>
```

Run from the repo root (`D:\Developer\MAIGD`), not from the Unity project folder.

Remote Ollama (for example the DGX Spark), one iteration, from the repo root:

```
python tools/goal_agent --backend openai --base-url http://spark-austin-1.local:11434/v1 --model qwen3.6:35b-a3b-mtp-q4_K_M --max-iterations 1
```

The host must listen on the network (`OLLAMA_HOST=0.0.0.0`) and the model name must match
`curl http://spark-austin-1.local:11434/api/tags` exactly. Changes land in the working
tree uncommitted, so `git` can undo them.
See `README.md` in this folder for all options.
