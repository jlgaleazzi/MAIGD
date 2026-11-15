"""Goal-oriented coding agent for the Biplanes project.

Reads docs/GDD.md, scans the Unity project, finds what the GDD requires but the code
doesn't have yet, and writes it, one prioritised gap at a time, until nothing writable is
left.

    gdd.py           GDD markdown -> items (traceability, unmapped-feature report)
    scanner.py       project files -> classes, text, assets
    requirements.py  spec.json -> requirements and their evidence checks
    gaps.py          Gap-Detection Pass: requirements x scan -> gaps
    scoring.py       priority_score(): which gap to close next
    writer.py        Code-Writing Step: prompt, parse, validate, apply, revert
    backends.py      LLM backends (Anthropic, OpenAI-compatible/Ollama, offline scaffold)
    agent.py         the Iteration Loop (scan, detect, rank, write, verify, re-scan)

Run it:  python tools/goal_agent --help
"""

__version__ = "0.1.0"
