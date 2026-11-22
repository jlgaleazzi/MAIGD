"""CLI:  python tools/style_loop <command> [options]          (run from the repo root)

    audit                       evaluate every art master and mission text against the GDD
    audit --fix                 ... and fix the violations (code fixes; plus the model with --backend)
    generate svg --asset fx/smoke_trail --request "a thin smoke trail puff"
    generate briefing --mission 3

    --backend none|anthropic|openai  (default none: code checks and fixes only)
    --model, --base-url              e.g. --backend openai --base-url http://spark-austin-1.local:11434/v1 --model gpt-oss:120b
    --max-iterations N               refine rounds per artifact (default 4)
    --no-judge                       skip the model judge for `judge` rules
    --dry-run                        never write files
    --rasterize                      run tools/rasterize.py after SVGs change
    --report FILE                    Markdown report (default tools/style_loop/last_run.md)

Exit: 0 everything obeys the Style Guide, 1 violations remain, 2 bad setup.
"""
import argparse
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))      # so `style_loop` and `goal_agent` import as packages

from goal_agent.backends import BackendError, make_backend  # noqa: E402
from style_loop import artifacts as A  # noqa: E402
from style_loop.guide import GuideError, load_guide  # noqa: E402
from style_loop.loop import Result, run  # noqa: E402
from style_loop.report import render  # noqa: E402

REPO = os.path.dirname(os.path.dirname(HERE))


def _backend(args):
    if args.backend == "none":
        return None
    return make_backend(args.backend, args.model, args.base_url)


def main(argv=None) -> int:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass
    ap = argparse.ArgumentParser(prog="style_loop", description=__doc__.split("\n")[0])
    ap.add_argument("command", choices=["audit", "generate"])
    ap.add_argument("what", nargs="?", help="generate: svg or briefing; audit: a file")
    ap.add_argument("paths", nargs="*", help="audit only these files (repo-relative)")
    ap.add_argument("--fix", action="store_true")
    ap.add_argument("--only", choices=["svg", "text"])
    ap.add_argument("--asset", help="generate svg: <category>/<name>, e.g. fx/smoke_trail")
    ap.add_argument("--mission", type=int, help="generate briefing: mission number")
    ap.add_argument("--request", default="", help="what to make (generate)")
    ap.add_argument("--backend", default="none", choices=["none", "anthropic", "openai"])
    ap.add_argument("--model")
    ap.add_argument("--base-url")
    ap.add_argument("--max-iterations", type=int, default=4)
    ap.add_argument("--no-judge", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--rasterize", action="store_true")
    ap.add_argument("--gdd", default=os.path.join(REPO, "docs", "GDD.md"))
    ap.add_argument("--report", default=os.path.join(HERE, "last_run.md"))
    args = ap.parse_args(argv)

    try:
        guide = load_guide(args.gdd)
        guide.source = os.path.relpath(args.gdd, REPO).replace(os.sep, "/")
        backend = _backend(args)
    except (GuideError, BackendError, OSError) as e:
        print(f"setup error: {e}", file=sys.stderr)
        return 2
    with open(args.gdd, encoding="utf-8") as f:
        gdd_text = f.read()
    print(f"Style Guide from {os.path.relpath(args.gdd, REPO)}: {len(guide.palette)} colours, "
          f"{len(guide.rules)} rules ({sum(r.check == 'auto' for r in guide.rules.values())} auto), "
          f"{sum(len(b) for _, b in guide.vocab)} banned terms")

    work, results = [], []
    if args.command == "generate":
        if args.what == "svg":
            if not args.asset or "/" not in args.asset:
                print("generate svg needs --asset <category>/<name>", file=sys.stderr)
                return 2
            cat, name = args.asset.split("/", 1)
            rel = f"art-src/{cat}/{name}.svg"
            work.append((A.SvgArtifact(rel, cat, name, ""), args.request or f"Draw the {name.replace('_', ' ')}."))
        elif args.what == "briefing":
            rel = f"{A.MISSIONS_DIR.replace(os.sep, '/')}/mission_{args.mission:02d}.json" if args.mission else None
            if not rel or not os.path.exists(os.path.join(REPO, rel)):
                print("generate briefing needs --mission N of an existing mission", file=sys.stderr)
                return 2
            work.append((A.load_mission(REPO, rel, gdd_text),
                         args.request or "Write a new title and briefing for this mission."))
        else:
            print("generate needs 'svg' or 'briefing'", file=sys.stderr)
            return 2
        if backend is None:
            print("generate needs --backend anthropic|openai", file=sys.stderr)
            return 2
    else:
        if args.what:
            args.paths = [args.what] + args.paths
            args.what = None
        paths = args.paths or ((A.all_svgs(REPO) if args.only != "text" else []) +
                               (A.all_missions(REPO) if args.only != "svg" else []))
        for rel in paths:
            rel = rel.replace("\\", "/")
            art = A.load_svg(REPO, rel) if rel.endswith(".svg") else A.load_mission(REPO, rel, gdd_text)
            work.append((art, None))

    for art, request in work:
        print(f"- {art.label}")
        try:
            if args.command == "audit" and not args.fix:
                res = run(art, guide, backend, max_iterations=0, judge=not args.no_judge, log=lambda *_: None)
                res.status = "clean" if not res.violations else "violations"
            elif args.command == "audit" and getattr(art, "generated_by", None):
                res = run(art, guide, backend, max_iterations=0, judge=not args.no_judge, log=lambda *_: None)
                res.status = "clean" if not res.violations else "report_only"
            else:
                res = run(art, guide, backend, max_iterations=args.max_iterations,
                          generate=request if args.command == "generate" else None,
                          judge=not args.no_judge)
        except Exception as e:      # one bad file must not stop the audit
            print(f"  error: {e}")
            res = Result(art.label, "error", art, art, [], [])
            res.error = str(e)
        for v in res.violations:
            print(f"    {v.line()}")
        results.append(res)

    written = []
    if not args.dry_run:
        for res in results:
            keep = res.changed and (args.command == "generate" or args.fix)
            if keep and (res.status == "clean" or args.command == "audit"):
                A.save(REPO, res.final)
                written.append(res.final.path)
    if written and args.rasterize and any(p.endswith(".svg") for p in written):
        subprocess.run([sys.executable, os.path.join(REPO, "tools", "rasterize.py")], cwd=REPO)

    report = render(results, guide, args, written)
    os.makedirs(os.path.dirname(os.path.abspath(args.report)), exist_ok=True)
    with open(args.report, "w", encoding="utf-8") as f:
        f.write(report)
    left = sum(len([v for v in r.violations if v.rule != "JUDGE"]) for r in results)
    print(f"\n{len(results)} artifact(s), {left} violation(s) left, {len(written)} file(s) written. "
          f"Report: {os.path.relpath(args.report, REPO)}")
    return 0 if left == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
