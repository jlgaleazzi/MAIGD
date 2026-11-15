"""CLI:  python tools/goal_agent [options]

    python tools/goal_agent --dry-run                       what is missing, in priority order
    python tools/goal_agent --backend anthropic             write the missing code with Claude
    python tools/goal_agent --backend openai                ... with the local Ollama model
    python tools/goal_agent --backend scaffold --verify none   offline wiring test (stubs only)

Exit: 0 every requirement met, 1 requirements still open, 2 bad setup.
"""
import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))      # so `goal_agent` imports as a package

from goal_agent.agent import Agent, CompileVerifier, NoVerifier, render_report  # noqa: E402
from goal_agent.backends import BackendError, make_backend  # noqa: E402
from goal_agent.gaps import unmapped_gdd_items  # noqa: E402
from goal_agent.requirements import SpecError  # noqa: E402

REPO = os.path.dirname(os.path.dirname(HERE))


def main(argv=None) -> int:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass
    ap = argparse.ArgumentParser(description="Goal-oriented coding agent: GDD -> gaps -> code")
    ap.add_argument("--root", default=REPO, help="repository root (default: this repo)")
    ap.add_argument("--gdd", help="GDD markdown (default: <root>/docs/GDD.md)")
    ap.add_argument("--spec", default=os.path.join(HERE, "spec.json"), help="requirements spec")
    ap.add_argument("--backend", choices=["anthropic", "openai", "scaffold"], default="anthropic")
    ap.add_argument("--model", help="model name for the backend")
    ap.add_argument("--base-url", help="OpenAI-compatible base URL (default: local Ollama)")
    ap.add_argument("--verify", choices=["compile", "none"], default="compile",
                    help="gate every write with tools/compile_check.py (default) or not")
    ap.add_argument("--max-iterations", type=int, default=10)
    ap.add_argument("--max-attempts", type=int, default=2, help="writer attempts per gap per visit")
    ap.add_argument("--dry-run", action="store_true", help="detect and rank only; write nothing")
    ap.add_argument("--report", help="also write the markdown report to this file")
    ap.add_argument("--max-files", type=int,
                    help="most files one write may touch (default: 6 for Claude, 3 for local models, as AGENTS.md)")
    ap.add_argument("--save-replies", metavar="DIR",
                    help="save every prompt and raw model reply here (diagnose rejected replies)")
    ap.add_argument("--show-unmapped", action="store_true", help="list GDD items no requirement tracks")
    args = ap.parse_args(argv)

    root = os.path.abspath(args.root)
    gdd_path = args.gdd or os.path.join(root, "docs", "GDD.md")
    if not os.path.isfile(gdd_path):
        print(f"GDD not found: {gdd_path}", file=sys.stderr)
        return 2
    try:
        backend = None
        if not args.dry_run:
            backend = make_backend(args.backend, args.model, args.base_url)
        verifier = CompileVerifier(root) if args.verify == "compile" else NoVerifier()
        agent = Agent(root, gdd_path, args.spec, backend, verifier, args.max_iterations,
                      args.max_attempts, dry_run=args.dry_run, save_replies=args.save_replies,
                      max_files=args.max_files or (6 if args.backend == "anthropic" else 3))
    except (BackendError, SpecError, OSError) as e:
        print(f"setup error: {e}", file=sys.stderr)
        return 2

    result = agent.run()
    report = render_report(result, agent.reqs)
    print("\n" + report)
    if args.show_unmapped:
        print("## GDD items without a requirement\n")
        for item in unmapped_gdd_items(agent.gdd, agent.reqs):
            print(f"- line {item.line} [{item.section}] {item.text[:110]}")
    if args.report:
        with open(args.report, "w", encoding="utf-8") as f:
            f.write(report)
    return 0 if result.complete else 1


if __name__ == "__main__":
    sys.exit(main())
