"""Tests for the goal agent. Run from the tools/ folder:

    python -m unittest goal_agent.tests.test_goal_agent -v

Everything runs against a tiny throw-away project in a temp folder, with a scripted backend.
"""
import json
import os
import shutil
import tempfile
import unittest

from goal_agent import agent as agent_mod
from goal_agent.agent import Agent, NoVerifier
from goal_agent.backends import BackendError, ScaffoldBackend
from goal_agent.gaps import MISSING, PARTIAL, STUB, detect_gaps, unmapped_gdd_items
from goal_agent.gdd import parse_gdd
from goal_agent.requirements import Requirement, SpecError, load_spec
from goal_agent.scanner import declared_types, scan_codebase
from goal_agent.scoring import build_context, priority_score, rank, score_gap
from goal_agent.writer import (WriteError, apply_changes, parse_reply, plan_changes)

GDD = """# Game

## 1. Features
- **Machine gun:** the plane has a forward-facing machine gun that
  wraps onto a second line.
- Zeppelins are big and have turrets that must be destroyed first.

### Table
| Action | Key |
|---|---|
| Fire machine gun | Space |

## Change Log
| Date | Change |
|---|---|
| 2026-01-01 | Hidden from feature items |
"""


def cs(name, area="Enemies", body=""):
    return (f"using UnityEngine;\nnamespace Biplanes.{area}\n{{\n    public class {name} : MonoBehaviour\n"
            f"    {{\n{body}    }}\n}}\n")


def block(kind, path, body, lang="csharp"):
    return f"```{lang}\n// {kind}: {path}\n{body}```\n"


def req(rid, impact=3, effort=3, checks=None, **kw):
    return Requirement(id=rid, title=rid.title(), gdd_anchor="machine gun", category="enemy",
                       impact=impact, effort=effort, checks=checks or [{"type": "class", "name": rid}], **kw)


class Project:
    """A temp repo: docs/, Biplanes/Assets/Biplanes/..., and a spec file."""

    def __init__(self, files=None, reqs=None):
        self.root = tempfile.mkdtemp(prefix="goal_agent_test_")
        self.base = os.path.join(self.root, "Biplanes", "Assets", "Biplanes")
        os.makedirs(os.path.join(self.root, "docs"))
        self.write("../../../docs/GDD.md", GDD)
        self.write("../../../docs/ARCHITECTURE.md", "# Architecture\nOne type per file.\n")
        for rel, text in (files or {}).items():
            self.write(rel, text)
        self.spec = os.path.join(self.root, "spec.json")
        if reqs is not None:
            self.set_spec(reqs)

    def write(self, rel, text, newline="\n"):
        full = os.path.normpath(os.path.join(self.base, *rel.split("/")))
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "w", encoding="utf-8", newline="") as f:
            f.write(text.replace("\n", newline))

    def read(self, rel):
        with open(os.path.join(self.base, *rel.split("/")), encoding="utf-8", newline="") as f:
            return f.read()

    def read_bytes(self, rel):
        with open(os.path.join(self.base, *rel.split("/")), "rb") as f:
            return f.read()

    def exists(self, rel):
        return os.path.exists(os.path.join(self.base, *rel.split("/")))

    def set_spec(self, reqs):
        entries = []
        for r in reqs:
            d = {k: v for k, v in r.__dict__.items() if k != "extra_anchors" or v}
            entries.append(d)
        with open(self.spec, "w", encoding="utf-8") as f:
            json.dump({"requirements": entries}, f)

    def agent(self, backend, verifier=None, **kw):
        kw.setdefault("log", lambda _m: None)
        return Agent(self.root, os.path.join(self.root, "docs", "GDD.md"), self.spec, backend,
                     verifier or NoVerifier(), **kw)

    def scan(self):
        return scan_codebase(self.root)

    def cleanup(self):
        shutil.rmtree(self.root, ignore_errors=True)


class ScriptedBackend:
    """Returns the scripted replies in order; remembers every prompt."""

    def __init__(self, *replies):
        self.replies = list(replies)
        self.prompts = []

    def complete(self, system, prompt):
        self.prompts.append(prompt)
        if not self.replies:
            raise AssertionError("backend called more often than scripted")
        r = self.replies.pop(0)
        if isinstance(r, Exception):
            raise r
        return r


class ScriptedVerifier:
    def __init__(self, *results):
        self.results = list(results)
        self.calls = 0

    def verify(self):
        self.calls += 1
        ok, msg = self.results.pop(0) if self.results else (True, "")
        return ok, msg, False


class Base(unittest.TestCase):
    def project(self, *a, **kw):
        p = Project(*a, **kw)
        self.addCleanup(p.cleanup)
        return p


# ------------------------------------------------------------------------------ GDD

class GddTests(unittest.TestCase):
    def test_items_merge_wrapped_lines_and_skip_change_log(self):
        doc = parse_gdd(GDD)
        texts = [i.text for i in doc.items]
        self.assertTrue(any("wraps onto a second line" in t for t in texts))
        self.assertFalse(any("Hidden from feature items" in t for t in texts))
        self.assertIn("Fire machine gun | Space", texts)
        self.assertNotIn("Action | Key", texts)         # table header row dropped

    def test_has_ignores_markdown_case_and_wrapping(self):
        doc = parse_gdd(GDD)
        self.assertTrue(doc.has("FORWARD-facing machine gun that wraps"))
        self.assertTrue(doc.has("Machine gun:"))
        self.assertFalse(doc.has("flamethrower"))

    def test_excerpt(self):
        self.assertIn("turrets", parse_gdd(GDD).excerpt("Zeppelins are big"))


# ------------------------------------------------------------------------------ scanner

class ScannerTests(Base):
    def test_declared_types_ignore_comments_and_strings(self):
        src = ('// class Fake\n/* class AlsoFake */\nstring s = "class Nope";\n'
               'namespace A { public sealed class Real : Base {}\n internal enum E {A}\n public interface IThing {} }')
        self.assertEqual(declared_types(src), ["Real", "E", "IThing"])

    def test_scan_reads_files_types_stubs_and_skips_meta(self):
        p = self.project({
            "Scripts/Enemies/Real.cs": cs("Real"),
            "Scripts/Enemies/Real.cs.meta": "guid: x",
            "Scripts/Enemies/Fake.cs": cs("Fake", body="        // GOAL-AGENT-SCAFFOLD\n"),
            "Resources/Balance.json": "{}",
            "Resources/Sprites/a.png": "x",
        }, [])
        scan = p.scan()
        self.assertEqual(scan.classes["Real"], "Scripts/Enemies/Real.cs")
        self.assertTrue(scan.files["Scripts/Enemies/Fake.cs"].stub)
        self.assertFalse(scan.files["Scripts/Enemies/Real.cs"].stub)
        self.assertNotIn("Scripts/Enemies/Real.cs.meta", scan.files)
        self.assertIsNone(scan.files["Resources/Sprites/a.png"].text)

    def test_crlf_files_are_normalised(self):
        p = self.project({}, [])
        p.write("Scripts/Enemies/Crlf.cs", cs("Crlf"), newline="\r\n")
        self.assertNotIn("\r", p.scan().text("Scripts/Enemies/Crlf.cs"))


# ------------------------------------------------------------------------------ spec

class SpecTests(Base):
    def _spec(self, p, entries):
        with open(p.spec, "w") as f:
            json.dump({"requirements": entries}, f)

    def base_entry(self, rid, **kw):
        return {"id": rid, "title": rid, "gdd_anchor": "machine gun", "category": "x", "impact": 3,
                "effort": 3, "checks": [{"type": "class", "name": "X"}], **kw}

    def test_rejects_bad_specs(self):
        p = self.project()
        for entries, msg in [
            ([self.base_entry("A"), self.base_entry("A")], "duplicate"),
            ([self.base_entry("A", impact=9)], "1..5"),
            ([self.base_entry("A", checks=[])], "at least one check"),
            ([self.base_entry("A", checks=[{"type": "nope"}])], "unknown check type"),
            ([self.base_entry("A", deps=["Z"])], "unknown dependency"),
            ([self.base_entry("A", deps=["B"]), self.base_entry("B", deps=["A"])], "cycle"),
        ]:
            self._spec(p, entries)
            with self.assertRaisesRegex(SpecError, msg):
                load_spec(p.spec)

    def test_shipped_spec_loads_and_anchors_exist_in_the_real_gdd(self):
        here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        gdd_path = os.path.join(os.path.dirname(os.path.dirname(here)), "docs", "GDD.md")
        if not os.path.isfile(gdd_path):
            self.skipTest("real GDD not found")
        reqs = load_spec(os.path.join(here, "spec.json"))
        with open(gdd_path, encoding="utf-8") as f:
            doc = parse_gdd(f.read())
        stale = [(r.id, a) for r in reqs for a in r.anchors if not doc.has(a)]
        self.assertEqual(stale, [])


# ------------------------------------------------------------------------------ gap detection

class GapTests(Base):
    def test_statuses(self):
        p = self.project({
            "Scripts/Enemies/Done.cs": cs("Done"),
            "Scripts/Enemies/Half.cs": cs("Half"),
            "Scripts/Enemies/Placeholder.cs": cs("Placeholder", body="        // GOAL-AGENT-SCAFFOLD\n"),
            "Resources/Balance.json": json.dumps({"a": {"b": 1}}),
        })
        reqs = [
            req("Done"),
            req("Half", checks=[{"type": "class", "name": "Half"}, {"type": "class", "name": "HalfTwo"}]),
            req("Gone"),
            req("Placeholder"),
            req("Key", checks=[{"type": "json_key", "path": "Resources/Balance.json", "key": "a.b"}]),
            req("KeyMissing", checks=[{"type": "json_key", "path": "Resources/Balance.json", "key": "a.c"}]),
            req("Pattern", checks=[{"type": "contains", "path": "Scripts/**/Half.cs", "pattern": "MonoBehaviour"}]),
        ]
        det = detect_gaps(reqs, p.scan(), parse_gdd(GDD))
        status = {g.req.id: g.status for g in det.gaps}
        self.assertEqual(status, {"Half": PARTIAL, "Gone": MISSING, "Placeholder": STUB, "KeyMissing": MISSING})
        self.assertEqual(sorted(det.satisfied), ["Done", "Key", "Pattern"])
        gap = next(g for g in det.gaps if g.req.id == "Half")
        self.assertEqual((gap.passed, gap.total, gap.progress), (1, 2, 0.5))
        self.assertTrue(any("HalfTwo" in f for f in gap.failures))

    def test_stale_anchor_and_unmapped_items(self):
        p = self.project({}, [])
        r = req("A")
        r.gdd_anchor = "this phrase is not in the gdd"
        doc = parse_gdd(GDD)
        self.assertEqual(detect_gaps([r], p.scan(), doc).stale_anchors, ["A"])
        self.assertTrue(unmapped_gdd_items(doc, [r]))
        self.assertFalse([i for i in unmapped_gdd_items(doc, [req("B")]) if "machine gun" in i.norm])


# ------------------------------------------------------------------------------ priority_score

class ScoringTests(Base):
    def gaps(self, reqs, files=None):
        p = self.project(files or {})
        det = detect_gaps(reqs, p.scan(), parse_gdd(GDD))
        return {g.req.id: g for g in det.gaps}, det

    def score(self, reqs, rid, attempts=None, files=None):
        gaps, det = self.gaps(reqs, files)
        ctx = build_context(reqs, det.gaps, attempts)
        return priority_score(gaps[rid], ctx)

    def test_higher_impact_wins(self):
        reqs = [req("Big", impact=5), req("Small", impact=2)]
        self.assertGreater(self.score(reqs, "Big"), self.score(reqs, "Small"))

    def test_cheaper_wins_at_equal_impact(self):
        reqs = [req("Cheap", effort=1), req("Dear", effort=5)]
        self.assertGreater(self.score(reqs, "Cheap"), self.score(reqs, "Dear"))

    def test_core_bonus(self):
        reqs = [req("Core", core=True), req("Content")]
        self.assertGreater(self.score(reqs, "Core"), self.score(reqs, "Content"))

    def test_blocked_gap_scores_zero_until_dependency_is_met(self):
        reqs = [req("Base"), req("Top", impact=5, effort=1, deps=["Base"])]
        self.assertEqual(self.score(reqs, "Top"), 0.0)
        self.assertGreater(self.score(reqs, "Base"), 0.0)
        # once Base exists the dependent becomes pickable
        files = {"Scripts/Enemies/Base.cs": cs("Base")}
        self.assertGreater(self.score(reqs, "Top", files=files), 0.0)

    def test_unblocking_more_gaps_raises_the_score(self):
        lone = [req("Foundation"), req("Other")]
        chain = [req("Foundation"), req("Other"), req("A", deps=["Foundation"]),
                 req("B", deps=["A"]), req("C", deps=["Foundation"])]
        self.assertGreater(self.score(chain, "Foundation"), self.score(lone, "Foundation"))
        gaps, det = self.gaps(chain)
        ctx = build_context(chain, det.gaps)
        self.assertEqual(ctx.unblocked_by("Foundation"), {"A", "B", "C"})     # transitive
        self.assertEqual(score_gap(gaps["Foundation"], ctx).unblock, 12.0)

    def test_partial_progress_is_cheaper_to_finish(self):
        reqs = [req("Half", checks=[{"type": "class", "name": "Half"}, {"type": "class", "name": "More"}]),
                req("None", checks=[{"type": "class", "name": "None_"}, {"type": "class", "name": "More2"}])]
        files = {"Scripts/Enemies/Half.cs": cs("Half")}
        self.assertGreater(self.score(reqs, "Half", files=files), self.score(reqs, "None", files=files))

    def test_failed_attempts_push_a_gap_down(self):
        reqs = [req("A")]
        self.assertGreater(self.score(reqs, "A"), self.score(reqs, "A", attempts={"A": 2}))

    def test_stub_counts_as_half_done(self):
        files = {"Scripts/Enemies/S.cs": cs("S", body="        // GOAL-AGENT-SCAFFOLD\n")}
        reqs = [req("S")]
        gaps, det = self.gaps(reqs, files)
        self.assertEqual(gaps["S"].progress, 0.5)

    def test_progress_mixes_real_evidence_stubs_and_gaps(self):
        files = {"Scripts/Enemies/Real.cs": cs("Real"),
                 "Scripts/Enemies/Fake.cs": cs("Fake", body="        // GOAL-AGENT-SCAFFOLD\n")}
        reqs = [req("Mixed", checks=[{"type": "class", "name": "Real"}, {"type": "class", "name": "Fake"},
                                     {"type": "class", "name": "Nowhere"}, {"type": "class", "name": "Nowhere2"}])]
        gaps, _ = self.gaps(reqs, files)
        self.assertAlmostEqual(gaps["Mixed"].progress, (1 + 0.5) / 4)

    def test_rank_is_best_first_and_deterministic(self):
        reqs = [req("B", impact=3), req("A", impact=3), req("Z", impact=5)]
        gaps, det = self.gaps(reqs)
        ranked = rank(list(gaps.values()), build_context(reqs, det.gaps))
        self.assertEqual([g.req.id for _, g in ranked], ["Z", "A", "B"])        # tie A/B -> id order

    def test_exact_formula(self):
        reqs = [req("X", impact=4, effort=2, core=True)]
        gaps, det = self.gaps(reqs)
        s = score_gap(gaps["X"], build_context(reqs, det.gaps))
        self.assertAlmostEqual(s.total, (10 * 4 + 8) / 2)


# ------------------------------------------------------------------------------ writer

class WriterTests(Base):
    def plan(self, p, reply):
        return plan_changes(reply, p.base, p.scan())

    def assertRejected(self, p, reply, text):
        with self.assertRaisesRegex(WriteError, text):
            self.plan(p, reply)

    def test_accepts_a_valid_new_file_and_normalises_the_path(self):
        p = self.project()
        ch = self.plan(p, block("FILE", "Biplanes/Assets/Biplanes/Scripts/Enemies/Foo.cs", cs("Foo")))
        self.assertEqual([(c.path, c.created) for c in ch], [("Scripts/Enemies/Foo.cs", True)])

    def test_path_policy(self):
        p = self.project()
        body = cs("Foo")
        self.assertRejected(p, block("FILE", "Scripts/Enemies/Foo.cs.meta", body), r"\.meta")
        self.assertRejected(p, block("FILE", "Scripts/../../evil.cs", body), r"\.\.")
        self.assertRejected(p, block("FILE", "/etc/Foo.cs", body), "relative")
        self.assertRejected(p, block("FILE", "C:/x/Foo.cs", body), "relative")
        self.assertRejected(p, block("FILE", "docs/GDD.md", "x"), "only .cs, .json")
        self.assertRejected(p, block("FILE", "Resources/Sprites/Foo.cs", body), "writable areas")
        self.assertRejected(p, block("FILE", "Scenes/Main.unity", "x"), "only .cs, .json")

    def test_csharp_rules(self):
        p = self.project()
        self.assertRejected(p, block("FILE", "Scripts/Enemies/Foo.cs", cs("Bar")), "file name must match")
        two = cs("Foo").replace("    }\n}", "    }\n    public class Extra {}\n}")
        self.assertRejected(p, block("FILE", "Scripts/Enemies/Foo.cs", two), "one type per file")
        self.assertRejected(p, block("FILE", "Scripts/Enemies/Foo.cs", "public class Foo {}\n"), "namespace")
        self.assertRejected(p, block("FILE", "Scripts/Enemies/Foo.cs", cs("Foo", body="        // ...\n")), "placeholder")
        self.assertRejected(p, block("FILE", "Scripts/Enemies/Foo.cs", cs("Foo")[:-3]), "unbalanced")
        self.assertRejected(p, "just prose, no blocks", "no FILE, PATCH or MERGE")

    def test_merge_adds_keys_to_an_existing_json_without_rewriting_it(self):
        big = {"player": {"speed": 5.0, "fireRate": 8.0}, "damageTaken": {"flak": 15.0}}
        p = self.project({"Resources/Balance.json": json.dumps(big, indent=2)}, [])
        ch = self.plan(p, block("MERGE", "Resources/Balance.json", '{"pitch": {"maxDegrees": 20}, "player": {"speed": 6.0}}', "json"))
        merged = json.loads(ch[0].new_text)
        self.assertEqual(merged["pitch"], {"maxDegrees": 20})
        self.assertEqual(merged["player"], {"speed": 6.0, "fireRate": 8.0})      # sibling key kept
        self.assertEqual(merged["damageTaken"], {"flak": 15.0})

    def test_merge_rejects_bad_bodies_and_a_json_rewrite_points_at_merge(self):
        p = self.project({"Resources/Balance.json": json.dumps({"a": {"b": 1, "c": 2, "d": 3, "e": 4}}, indent=2)}, [])
        self.assertRejected(p, block("MERGE", "Resources/Balance.json", "{oops", "json"), "not valid JSON")
        self.assertRejected(p, block("MERGE", "Scripts/Enemies/Foo.cs", "{}", "json"), "only .cs, .json|MERGE works")
        self.assertRejected(p, block("FILE", "Resources/Balance.json", '{"a": {}}', "json"), "MERGE block")

    def test_parser_drops_a_think_section_and_accepts_loose_headers(self):
        body = cs("Foo")
        fence = "`" * 3
        reply = ("<think>I will write Foo. " + fence + "csharp\n// FILE: wrong/Path.cs\n" + fence + " </think>\n"
                 + fence + "csharp\n\n  // File: `Scripts/Enemies/Foo.cs` (new file)\n" + body + fence + "\n")
        blocks = parse_reply(reply)
        self.assertEqual([(k, path) for k, path, _ in blocks], [("FILE", "Scripts/Enemies/Foo.cs")])
        also = parse_reply("</think>\n" + fence + "csharp\n**FILE:** Scripts/Enemies/Foo.cs\n" + body + fence + "\n")
        self.assertEqual(also[0][1], "Scripts/Enemies/Foo.cs")

    def test_empty_and_truncated_replies_get_a_specific_reason(self):
        p = self.project()
        fence = "`" * 3
        self.assertRejected(p, "<think>only thinking</think>", "empty")
        self.assertRejected(p, fence + "csharp\n// FILE: Scripts/Enemies/Foo.cs\nclass Foo {", "truncated")

    def test_header_outside_the_fence_and_unfenced_patch_and_merge_blocks(self):
        # Layout seen from a real local model: headers on their own line, some blocks unfenced.
        fence = "`" * 3
        reply = ("// FILE: Scripts/Enemies/Foo.cs\n" + fence + "csharp\n" + cs("Foo") + fence + "\n\n"
                 "// MERGE: Resources/Balance.json\n{ \"pitch\": { \"maxDegrees\": 15.0 } }\n\n"
                 "// PATCH: Scripts/Enemies/Real.cs\n<<<<<<< SEARCH\nX\n=======\nY\n>>>>>>> REPLACE\n")
        blocks = parse_reply(reply)
        self.assertEqual([(k, path) for k, path, _ in blocks],
                         [("FILE", "Scripts/Enemies/Foo.cs"), ("MERGE", "Resources/Balance.json"),
                          ("PATCH", "Scripts/Enemies/Real.cs")])
        self.assertTrue(blocks[0][2].lstrip().startswith("using"))       # fence lines stripped
        self.assertEqual(json.loads(blocks[1][2]), {"pitch": {"maxDegrees": 15.0}})
        self.assertIn("<<<<<<< SEARCH", blocks[2][2])

    def test_several_patch_blocks_for_one_file_count_as_one_file(self):
        real = cs("Real", body="        int a = 1;\n        int b = 2;\n")
        p = self.project({"Scripts/Enemies/Real.cs": real}, [])
        def patch(s, r):
            return "// PATCH: Scripts/Enemies/Real.cs\n<<<<<<< SEARCH\n" + s + "\n=======\n" + r + "\n>>>>>>> REPLACE\n"
        reply = patch("        int a = 1;", "        int a = 10;") * 1 + patch("        int b = 2;", "        int b = 20;")
        reply = reply + "".join(f"// FILE: Scripts/Enemies/F{i}.cs\n" + cs(f"F{i}") + "\n" for i in range(5))
        ch = self.plan(p, reply)                     # 1 patched + 5 new = 6 distinct files: allowed
        real_ch = next(c for c in ch if c.path == "Scripts/Enemies/Real.cs")
        self.assertIn("int a = 10;", real_ch.new_text)
        self.assertIn("int b = 20;", real_ch.new_text)
        too_many = reply + "// FILE: Scripts/Enemies/F9.cs\n" + cs("F9") + "\n"
        with self.assertRaisesRegex(WriteError, "7 files"):
            self.plan(p, too_many)

    def test_patching_a_file_that_already_holds_several_types(self):
        multi = cs("Many").replace("    }\n}", "    }\n    public class DataA {}\n    public class DataB {}\n}")
        single = cs("Solo")
        p = self.project({"Scripts/Core/Many.cs": multi, "Scripts/Core/Solo.cs": single}, [])

        def patch(rel, search, replace):
            return "// PATCH: " + rel + "\n<<<<<<< SEARCH\n" + search + "\n=======\n" + replace + "\n>>>>>>> REPLACE\n"

        # a multi-type file (like GameBalance.cs) may take one more sibling type
        ch = self.plan(p, patch("Scripts/Core/Many.cs", "    public class DataB {}",
                                "    public class DataB {}\n    public class DataC {}"))
        self.assertIn("DataC", ch[0].new_text)
        # a single-type file may not grow a second type
        with self.assertRaisesRegex(WriteError, "one type per file"):
            self.plan(p, patch("Scripts/Core/Solo.cs", "    }\n}", "    }\n    public class Extra {}\n}"))

    def test_merge_header_followed_by_a_fenced_block_with_an_inner_header(self):
        fence = "`" * 3
        reply = ("// MERGE: Resources/Balance.json\n{ \"pitch\": { \"maxDegrees\": 15.0 } }\n\n"
                 + fence + "csharp\n// FILE: Scripts/Enemies/Foo.cs\n" + cs("Foo") + fence + "\n")
        blocks = parse_reply(reply)
        self.assertEqual([(k, path) for k, path, _ in blocks],
                         [("MERGE", "Resources/Balance.json"), ("FILE", "Scripts/Enemies/Foo.cs")])
        self.assertEqual(json.loads(blocks[0][2]), {"pitch": {"maxDegrees": 15.0}})

    def test_json_must_parse(self):
        p = self.project()
        self.assertRejected(p, block("FILE", "Resources/Missions/mission_09.json", "{oops", "json"), "invalid JSON")
        ok = self.plan(p, block("FILE", "Resources/Missions/mission_09.json", "{}", "json"))
        self.assertEqual(ok[0].path, "Resources/Missions/mission_09.json")

    def test_too_many_files(self):
        p = self.project()
        reply = "".join(block("FILE", f"Scripts/Enemies/F{i}.cs", cs(f"F{i}")) for i in range(7))
        self.assertRejected(p, reply, "limit")

    def test_patch_keeps_crlf_and_applies_once(self):
        p = self.project()
        p.write("Scripts/Enemies/Foo.cs", cs("Foo", body="        int a = 1;\n"), newline="\r\n")
        reply = block("PATCH", "Scripts/Enemies/Foo.cs",
                      "<<<<<<< SEARCH\n        int a = 1;\n=======\n        int a = 2;\n        int b = 3;\n>>>>>>> REPLACE\n")
        ch = self.plan(p, reply)
        apply_changes(ch, p.base)
        text = p.read("Scripts/Enemies/Foo.cs")
        self.assertIn("int a = 2;\r\n        int b = 3;", text)
        self.assertNotIn("\r\r", text)
        self.assertFalse(ch[0].created)

    def test_patch_errors(self):
        p = self.project()
        p.write("Scripts/Enemies/Foo.cs", cs("Foo", body="        int a = 1;\n        int a = 1;\n"))
        hunk = lambda s: block("PATCH", "Scripts/Enemies/Foo.cs", f"<<<<<<< SEARCH\n{s}\n=======\nx\n>>>>>>> REPLACE\n")
        self.assertRejected(p, hunk("        int zzz;"), "not found")
        self.assertRejected(p, hunk("        int a = 1;"), "occurs 2 times")
        self.assertRejected(p, block("PATCH", "Scripts/Enemies/Foo.cs", "no hunk"), "no '<<<<<<< SEARCH")
        self.assertRejected(p, block("PATCH", "Scripts/Enemies/Missing.cs",
                                     "<<<<<<< SEARCH\na\n=======\nb\n>>>>>>> REPLACE\n"), "does not exist")

    def test_rewrite_limits(self):
        p = self.project()
        big = cs("Foo", body="        int x;\n" * 220)
        p.write("Scripts/Enemies/Foo.cs", big)
        self.assertRejected(p, block("FILE", "Scripts/Enemies/Foo.cs", cs("Foo")), "PATCH blocks")
        p.write("Scripts/Enemies/Bar.cs", cs("Bar", body="        int x;\n" * 40))
        self.assertRejected(p, block("FILE", "Scripts/Enemies/Bar.cs", cs("Bar")), "truncated")

    def test_apply_then_undo_restores_everything(self):
        p = self.project({"Scripts/Enemies/Old.cs": cs("Old", body="        int a;\n")})
        before = p.read_bytes("Scripts/Enemies/Old.cs")
        reply = block("FILE", "Scripts/Enemies/New.cs", cs("New")) + block(
            "FILE", "Scripts/Enemies/Old.cs", cs("Old", body="        int a;\n        int b;\n"))
        undo = apply_changes(self.plan(p, reply), p.base)
        self.assertTrue(p.exists("Scripts/Enemies/New.cs"))
        p.write("Scripts/Enemies/New.cs.meta", "guid")                                  # Unity imported it
        undo.undo()
        self.assertFalse(p.exists("Scripts/Enemies/New.cs"))
        self.assertFalse(p.exists("Scripts/Enemies/New.cs.meta"))
        self.assertEqual(p.read_bytes("Scripts/Enemies/Old.cs"), before)


# ------------------------------------------------------------------------------ the loop

def good(name):
    return block("FILE", f"Scripts/Enemies/{name}.cs", cs(name))


class LoopTests(Base):
    def test_writes_rescans_and_stops_when_everything_is_met(self):
        p = self.project(reqs=[req("Alpha", impact=5), req("Beta", impact=2)])
        backend = ScriptedBackend(good("Alpha"), good("Beta"))
        res = p.agent(backend).run()
        self.assertEqual(res.stop_reason, "complete")
        self.assertTrue(res.complete)
        self.assertEqual([i.req_id for i in res.iterations], ["Alpha", "Beta"])      # best score first
        self.assertTrue(all(i.outcome == "closed" for i in res.iterations))
        self.assertTrue(p.exists("Scripts/Enemies/Alpha.cs") and p.exists("Scripts/Enemies/Beta.cs"))
        self.assertEqual(res.detection.gaps, [])

    def test_nothing_to_do_when_already_complete(self):
        p = self.project({"Scripts/Enemies/Alpha.cs": cs("Alpha")}, [req("Alpha")])
        res = p.agent(ScriptedBackend()).run()
        self.assertEqual((res.stop_reason, res.iterations), ("complete", []))

    def test_dependencies_are_written_in_order_even_if_dependent_scores_higher(self):
        p = self.project(reqs=[req("Top", impact=5, effort=1, deps=["Base"]), req("Base", impact=1, effort=5)])
        backend = ScriptedBackend(good("Base"), good("Top"))
        res = p.agent(backend).run()
        self.assertEqual([i.req_id for i in res.iterations], ["Base", "Top"])
        self.assertTrue(res.complete)

    def test_prompt_carries_gdd_excerpt_failures_architecture_and_context_files(self):
        p = self.project({"Scripts/Enemies/Example.cs": cs("Example")},
                         [req("Alpha", context_files=["Scripts/Enemies/Example.cs"], new_files=["Scripts/Enemies/Alpha.cs"],
                              hint="Be careful.")])
        backend = ScriptedBackend(good("Alpha"))
        p.agent(backend).run()
        prompt = backend.prompts[0]
        for needle in ("forward-facing machine gun", "type Alpha is not declared", "One type per file",
                       "Existing file: Scripts/Enemies/Example.cs", "Create: Scripts/Enemies/Alpha.cs",
                       "Be careful.", "Example -> Scripts/Enemies/Example.cs"):
            self.assertIn(needle, prompt)

    def test_rejected_reply_is_retried_with_the_reason(self):
        p = self.project(reqs=[req("Alpha")])
        backend = ScriptedBackend("I would write the class like this...", good("Alpha"))
        res = p.agent(backend).run()
        self.assertTrue(res.complete)
        self.assertIn("Your previous attempt was rejected", backend.prompts[1])
        self.assertIn("no FILE, PATCH or MERGE blocks", backend.prompts[1])
        self.assertEqual([a.ok for a in res.iterations[0].attempts], [False, True])

    def test_compile_failure_reverts_and_feeds_the_error_back(self):
        p = self.project(reqs=[req("Alpha")])
        backend = ScriptedBackend(good("Alpha"), good("Alpha"))
        verifier = ScriptedVerifier((False, "Alpha.cs(5,9): error CS0103: The name 'x' does not exist"), (True, ""))
        res = p.agent(backend, verifier).run()
        self.assertTrue(res.complete)
        self.assertIn("error CS0103", backend.prompts[1])
        self.assertIn("reverted", backend.prompts[1])
        self.assertEqual(verifier.calls, 2)

    def test_failed_write_is_removed_from_disk(self):
        p = self.project(reqs=[req("Alpha")])
        backend = ScriptedBackend(good("Alpha"), good("Alpha"))
        verifier = ScriptedVerifier((False, "error"), (False, "error"))
        res = p.agent(backend, verifier, max_visits=1).run()
        self.assertFalse(p.exists("Scripts/Enemies/Alpha.cs"))
        self.assertIn("Alpha", res.stuck)
        self.assertEqual(res.stop_reason, "nothing_to_write")

    def test_write_that_does_not_close_the_gap_is_reverted(self):
        p = self.project(reqs=[req("Alpha")])
        wrong = block("FILE", "Scripts/Enemies/Wrong.cs", cs("Wrong"))        # compiles, but isn't Alpha
        backend = ScriptedBackend(wrong, good("Alpha"))
        res = p.agent(backend).run()
        self.assertTrue(res.complete)
        self.assertFalse(p.exists("Scripts/Enemies/Wrong.cs"))
        self.assertIn("not closer to done", backend.prompts[1])

    def test_partial_progress_is_kept(self):
        checks = [{"type": "class", "name": "One"}, {"type": "class", "name": "Two"}]
        p = self.project(reqs=[req("Pair", checks=checks)])
        backend = ScriptedBackend(good("One"), good("Two"))
        res = p.agent(backend).run()
        self.assertEqual([i.outcome for i in res.iterations], ["improved", "closed"])
        self.assertTrue(res.complete)

    def test_a_change_that_breaks_a_met_requirement_is_reverted(self):
        files = {"Scripts/Enemies/Keep.cs": cs("Keep", body="        int marker_value;\n")}
        keep = req("Keep", checks=[{"type": "class", "name": "Keep"},
                                   {"type": "contains", "path": "Scripts/Enemies/Keep.cs", "pattern": "marker_value"}])
        p = self.project(files, [keep, req("Alpha")])
        breaker = good("Alpha") + block("FILE", "Scripts/Enemies/Keep.cs", cs("Keep", body="        int other_values;\n"))
        backend = ScriptedBackend(breaker, good("Alpha"))
        res = p.agent(backend).run()
        self.assertTrue(res.complete)
        self.assertIn("broke requirements", backend.prompts[1])
        self.assertIn("marker_value", p.read("Scripts/Enemies/Keep.cs"))

    def test_unwritable_gaps_are_reported_not_attempted(self):
        art = req("Sprite", writable=False, owner="art", checks=[{"type": "file", "path": "Resources/Sprites/x.png"}])
        p = self.project(reqs=[art, req("Alpha")])
        backend = ScriptedBackend(good("Alpha"))
        res = p.agent(backend).run()
        self.assertEqual(res.stop_reason, "nothing_to_write")
        self.assertEqual([i.req_id for i in res.iterations], ["Alpha"])
        self.assertEqual(res.detection.gap_ids, ["Sprite"])
        self.assertIn("needs the art agent", agent_mod.explain_remaining(res.detection.gaps[0], res, []))

    def test_a_blocker_that_cannot_be_written_keeps_dependents_blocked(self):
        art = req("Sprite", writable=False, checks=[{"type": "file", "path": "Resources/Sprites/x.png"}])
        p = self.project(reqs=[art, req("Needs", deps=["Sprite"])])
        res = p.agent(ScriptedBackend()).run()
        self.assertEqual((res.stop_reason, res.iterations), ("nothing_to_write", []))
        needs = next(g for g in res.detection.gaps if g.req.id == "Needs")
        self.assertIn("blocked by Sprite", agent_mod.explain_remaining(needs, res, []))

    def test_iteration_budget(self):
        p = self.project(reqs=[req("A"), req("B"), req("C")])
        res = p.agent(ScriptedBackend(good("A"), good("B")), max_iterations=2).run()
        self.assertEqual(res.stop_reason, "max_iterations")
        self.assertEqual(len(res.iterations), 2)
        self.assertEqual(res.detection.gap_ids, ["C"])

    def test_dry_run_plans_and_writes_nothing(self):
        p = self.project(reqs=[req("A", impact=5), req("B", deps=["A"])])
        res = p.agent(None, dry_run=True).run()
        self.assertEqual(res.stop_reason, "dry_run")
        self.assertEqual([g.req.id for _, g in res.plan], ["A", "B"])
        self.assertEqual(res.plan[1][0], 0.0)
        self.assertFalse(p.exists("Scripts/Enemies/A.cs"))
        self.assertIn("blocked by A", agent_mod.render_report(res, p.agent(None).reqs))

    def test_backend_error_stops_the_run(self):
        p = self.project(reqs=[req("A"), req("B")])
        res = p.agent(ScriptedBackend(BackendError("connection refused"))).run()
        self.assertEqual(res.stop_reason, "backend_error")
        self.assertEqual(len(res.iterations), 1)

    def test_scaffold_backend_only_produces_stubs_so_the_gap_stays_open(self):
        p = self.project(reqs=[req("Alpha", new_files=["Scripts/Enemies/Alpha.cs"])])
        res = p.agent(ScaffoldBackend(), max_iterations=5).run()
        self.assertFalse(res.complete)
        gap = res.detection.gaps[0]
        self.assertEqual(gap.status, STUB)
        self.assertIn("GOAL-AGENT-SCAFFOLD", p.read("Scripts/Enemies/Alpha.cs"))
        self.assertEqual(res.iterations[0].outcome, "improved")
        self.assertIn("Alpha", res.stuck)             # the stub can't be improved by the scaffold writer

    def test_stub_gap_prompt_names_the_stub_file_to_fix(self):
        files = {"Scripts/Enemies/Alpha.cs": cs("Alpha", body="        void M() { throw new System.NotImplementedException(); }\n")}
        p = self.project(files, [req("Alpha")])
        fixed = block("PATCH", "Scripts/Enemies/Alpha.cs",
                      "<<<<<<< SEARCH\n        void M() { throw new System.NotImplementedException(); }\n=======\n        void M() { }\n>>>>>>> REPLACE\n")
        backend = ScriptedBackend(fixed)
        res = p.agent(backend).run()
        self.assertIn("You may change (PATCH): Scripts/Enemies/Alpha.cs", backend.prompts[0])
        self.assertTrue(res.complete)

    def test_rescan_happens_after_each_write(self):
        # Beta depends on Alpha; if the loop didn't re-scan it would never unblock Beta.
        p = self.project(reqs=[req("Alpha"), req("Beta", deps=["Alpha"])])
        res = p.agent(ScriptedBackend(good("Alpha"), good("Beta"))).run()
        self.assertTrue(res.complete)


# ------------------------------------------------------------------------------ backends and verifier

class FakeServer:
    """A local HTTP server that records requests and answers with a canned status and body."""

    def __init__(self, status=200, body=None):
        import http.server
        import threading
        outer = self
        self.requests, self.status, self.body = [], status, body or {}

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_POST(self):
                length = int(self.headers.get("content-length", 0))
                headers = {k.lower(): v for k, v in self.headers.items()}      # urllib re-capitalises names
                outer.requests.append((self.path, headers, json.loads(self.rfile.read(length))))
                payload = json.dumps(outer.body).encode()
                self.send_response(outer.status)
                self.send_header("content-type", "application/json")
                self.send_header("content-length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)

            def log_message(self, *a):
                pass

        self.httpd = http.server.HTTPServer(("127.0.0.1", 0), Handler)
        self.url = f"http://127.0.0.1:{self.httpd.server_address[1]}"
        threading.Thread(target=self.httpd.serve_forever, daemon=True).start()

    def close(self):
        self.httpd.shutdown()
        self.httpd.server_close()


class BackendTests(Base):
    def server(self, **kw):
        s = FakeServer(**kw)
        self.addCleanup(s.close)
        return s

    def test_anthropic_request_shape_and_reply_parsing(self):
        from goal_agent.backends import AnthropicBackend
        s = self.server(body={"content": [{"type": "text", "text": "hello "}, {"type": "text", "text": "world"}]})
        out = AnthropicBackend("some-model", "KEY", base_url=s.url).complete("SYS", "PROMPT")
        self.assertEqual(out, "hello world")
        path, headers, body = s.requests[0]
        self.assertEqual(path, "/v1/messages")
        self.assertEqual(headers["x-api-key"], "KEY")
        self.assertEqual(headers["anthropic-version"], "2023-06-01")
        self.assertEqual((body["model"], body["system"]), ("some-model", "SYS"))
        self.assertEqual(body["messages"], [{"role": "user", "content": "PROMPT"}])

    def test_openai_compatible_request_shape_and_reply_parsing(self):
        from goal_agent.backends import OpenAICompatBackend
        s = self.server(body={"choices": [{"message": {"content": "from ollama"}}]})
        out = OpenAICompatBackend("qwen", s.url + "/v1").complete("SYS", "PROMPT")
        self.assertEqual(out, "from ollama")
        path, headers, body = s.requests[0]
        self.assertEqual(path, "/v1/chat/completions")
        self.assertEqual([m["role"] for m in body["messages"]], ["system", "user"])

    def test_http_errors_and_odd_replies_become_backend_errors(self):
        from goal_agent.backends import AnthropicBackend, OpenAICompatBackend
        with self.assertRaisesRegex(BackendError, "HTTP 500"):
            AnthropicBackend("m", "K", base_url=self.server(status=500, body={"error": "boom"}).url).complete("s", "p")
        with self.assertRaisesRegex(BackendError, "empty reply"):
            AnthropicBackend("m", "K", base_url=self.server(body={"content": []}).url).complete("s", "p")
        with self.assertRaisesRegex(BackendError, "unexpected reply"):
            OpenAICompatBackend("m", self.server(body={"nope": 1}).url).complete("s", "p")
        with self.assertRaisesRegex(BackendError, "cannot reach"):
            OpenAICompatBackend("m", "http://127.0.0.1:1").complete("s", "p")

    def test_anthropic_needs_a_key_and_make_backend_validates(self):
        from goal_agent.backends import AnthropicBackend, make_backend
        saved = os.environ.pop("ANTHROPIC_API_KEY", None)
        self.addCleanup(lambda: saved and os.environ.__setitem__("ANTHROPIC_API_KEY", saved))
        with self.assertRaisesRegex(BackendError, "ANTHROPIC_API_KEY"):
            AnthropicBackend()
        with self.assertRaises(BackendError):
            make_backend("nonsense")
        self.assertEqual(make_backend("scaffold").name, "scaffold")


class VerifierTests(Base):
    def verifier(self, code, text="out"):
        p = self.project()
        os.makedirs(os.path.join(p.root, "tools"))
        with open(os.path.join(p.root, "tools", "compile_check.py"), "w") as f:
            f.write(f"import sys\nprint({text!r})\nsys.exit({code})\n")
        return agent_mod.CompileVerifier(p.root)

    def test_exit_codes(self):
        self.assertEqual(self.verifier(0).verify(), (True, "out", False))
        ok, msg, skipped = self.verifier(1, "error CS1002").verify()
        self.assertEqual((ok, skipped), (False, False))
        self.assertIn("error CS1002", msg)
        ok, msg, skipped = self.verifier(3, "no dotnet").verify()
        self.assertEqual((ok, skipped), (True, True))

    def test_missing_script_is_skipped(self):
        p = self.project()
        self.assertEqual(agent_mod.CompileVerifier(p.root).verify()[::2], (True, True))


if __name__ == "__main__":
    unittest.main()
