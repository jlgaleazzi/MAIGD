"""Offline tests: no model, no Unity.   cd tools && python -m unittest style_loop.tests.test_style_loop -v"""
import json
import os
import shutil
import tempfile
import unittest

from style_loop import artifacts as A
from style_loop import rules as R
from style_loop.guide import GuideError, load_guide, parse_guide
from style_loop.loop import parse_judgement, parse_reply, run

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
GDD = os.path.join(REPO, "docs", "GDD.md")

MINI_GDD = """# x
## Style Guide
### Palette
| Colour | Hex | Used for |
|---|---|---|
| Paper | `#0F2A4A` | bg |
| Ink | `#E8F1FA` | lines |
| Pencil | `#9DB4CC` | soft |
| Hostile | `#E4572E` | flak only |

| Use | Instead of |
|---|---|
| bombs | payload |
| anti-aircraft guns | AAA guns, AAA |
| (remove) | radar |

| ID | Applies to | Rule | Check | Parameters |
|---|---|---|---|---|
| A-PALETTE | art | palette only | auto | |
| A-ACCENT | art | accents | auto | hostile=flak_burst |
| A-NO-FILL | art | lines not fills | auto | mark_ids=cross; line_max_px=1.5 |
| A-LINE-WEIGHT | sprites | thin | auto | max_px=1.5; categories=enemies |
| A-BG-QUIET | backgrounds | quiet | auto | max_ink_opacity=0.45 |
| A-NO-TEXT | art | no text | auto | |
| A-FACE-RIGHT | aircraft | faces right | judge | |
| N-VOCAB | text | vocab | auto | |
| N-LENGTH | briefing | len | auto | min_sentences=2; max_sentences=3; max_words=20 |
| N-EXCLAIM | briefing | ! | auto | max=1 |
| N-TITLE | title | title | auto | min_words=2; max_words=4 |
| N-TARGET | briefing | target | auto | |
| N-GROUNDED | briefing | only present enemies | auto | |
| N-VOICE | briefing | voice | judge | |
## 3. Next
"""


def svg(body, w=100, extra=""):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="100" viewBox="0 0 100 100"{extra}>'
            f'{body}</svg>\n')


def sart(body, cat="enemies", name="aaa_gun", **kw):
    return A.SvgArtifact(f"art-src/{cat}/{name}.svg", cat, name, svg(body, **kw))


def tart(title="Destroy the Bomber", briefing="An enemy bomber comes. Destroy the bomber now!", targets=("bomber",)):
    return A.TextArtifact("m.json", "2", title, briefing, targets, "")


class FakeBackend:
    """Replies in order; records prompts."""
    name = "fake"

    def __init__(self, replies):
        self.replies, self.prompts = list(replies), []

    def complete(self, system, prompt):
        self.prompts.append(prompt)
        return self.replies.pop(0) if self.replies else '{"violations": []}'


class GuideTests(unittest.TestCase):
    def setUp(self):
        self.g = parse_guide(MINI_GDD)

    def test_palette_vocab_rules_come_from_the_gdd(self):
        self.assertEqual(self.g.palette["hostile"], "#E4572E")
        self.assertIn(("bombs", ["payload"]), self.g.vocab)
        self.assertEqual(self.g.rules["N-LENGTH"].param_int("max_words", 0), 20)
        self.assertEqual(self.g.rules["A-FACE-RIGHT"].check, "judge")
        self.assertEqual(self.g.accents(), {"hostile": "#E4572E"})

    def test_missing_tables_fail_loudly(self):
        with self.assertRaises(GuideError):
            parse_guide("## Style Guide\nnothing\n## 3. x")
        with self.assertRaises(GuideError):
            parse_guide("# no section")

    def test_real_gdd_parses(self):
        g = load_guide(GDD)
        self.assertGreaterEqual(len(g.palette), 7)
        for rid in ("A-PALETTE", "A-NO-FILL", "N-VOCAB", "N-VOICE"):
            self.assertIn(rid, g.rules)

    def test_rule_removed_from_gdd_is_not_enforced(self):
        g = parse_guide(MINI_GDD.replace("| N-EXCLAIM | briefing | ! | auto | max=1 |\n", ""))
        self.assertFalse([v for v in R.check(g, tart(briefing="Bombers come! Destroy the bomber!")) if v.rule == "N-EXCLAIM"])


class SvgRuleTests(unittest.TestCase):
    def setUp(self):
        self.g = parse_guide(MINI_GDD)

    def rules_hit(self, art):
        return sorted({v.rule for v in R.check(self.g, art)})

    def test_clean_art_passes(self):
        self.assertEqual(self.rules_hit(sart('<path d="M0 0L10 10" stroke="#E8F1FA" stroke-width="1" fill="none"/>')), [])

    def test_off_palette_colour_snaps_to_nearest(self):
        art = sart('<path d="M0 0L10 10" stroke="#FFFFFF" fill="none"/>')
        self.assertEqual(self.rules_hit(art), ["A-PALETTE"])
        fixed, counts = R.auto_fix(self.g, art)
        self.assertIn('stroke="#E8F1FA"', fixed.text)
        self.assertEqual(R.check(self.g, fixed), [])

    def test_default_black_fill_is_caught_but_zero_area_lines_are_not(self):
        self.assertIn("A-PALETTE", self.rules_hit(sart('<path d="M0 0L50 0L50 50Z" stroke="#E8F1FA"/>')))
        self.assertEqual(self.rules_hit(sart('<path d="M0 0L50 0" stroke="#E8F1FA"/>')), [])

    def test_accent_only_where_allowed(self):
        body = '<path d="M0 0L9 9" stroke="#E4572E" fill="none"/>'
        self.assertEqual(self.rules_hit(sart(body, "fx", "flak_burst_0")), [])
        art = sart(body, "fx", "explosion_0")
        self.assertEqual(self.rules_hit(art), ["A-ACCENT"])
        fixed, _ = R.auto_fix(self.g, art)
        self.assertNotIn("#E4572E", fixed.text)

    def test_fills_lines_marks_and_thin_slivers(self):
        panel = '<rect x="0" y="0" width="40" height="40" fill="#E8F1FA"/>'
        self.assertEqual(self.rules_hit(sart(panel)), ["A-NO-FILL"])
        self.assertEqual(self.rules_hit(sart('<g id="cross">' + panel + '</g>')), [])           # faction mark
        self.assertEqual(self.rules_hit(sart('<circle r="0.5" cx="5" cy="5" fill="#E8F1FA"/>')), [])  # rivet
        self.assertEqual(self.rules_hit(sart('<rect width="40" height="40" fill="#0F2A4A"/>')), [])   # paper
        fixed, _ = R.auto_fix(self.g, sart(panel))
        self.assertIn('fill="none"', fixed.text)
        self.assertIn('stroke="#E8F1FA"', fixed.text)

    def test_line_weight_respects_transform_scale(self):
        thick = '<path d="M0 0L9 9" stroke="#E8F1FA" stroke-width="1" fill="none" transform="scale(2)"/>'
        art = sart(thick)
        self.assertEqual(self.rules_hit(art), ["A-LINE-WEIGHT"])
        fixed, _ = R.auto_fix(self.g, art)
        self.assertEqual(R.check(self.g, fixed), [])
        self.assertEqual(self.rules_hit(sart(thick.replace("#E8F1FA", "#9DB4CC"), "backgrounds", "bg_x_far")), [])

    def test_use_of_group_reads_the_childrens_own_widths(self):
        body = ('<defs><g id="w"><path d="M0 0L9 9" stroke-width="0.75"/></g></defs>'
                '<g stroke="#E8F1FA" fill="none" stroke-width="1"><use href="#w" transform="scale(1.6)"/></g>')
        self.assertEqual(self.rules_hit(sart(body)), [])

    def test_background_ink_must_be_faint(self):
        art = sart('<path d="M0 0L9 9" stroke="#E8F1FA" fill="none"/>', "backgrounds", "bg_x_mid")
        self.assertEqual(self.rules_hit(art), ["A-BG-QUIET"])
        fixed, _ = R.auto_fix(self.g, art)
        self.assertEqual(R.check(self.g, fixed), [])

    def test_mask_luminance_colours_are_not_paint(self):
        body = ('<defs><linearGradient id="g"><stop stop-color="white"/><stop stop-color="black"/></linearGradient>'
                '<mask id="m"><rect width="9" height="9" fill="url(#g)"/></mask></defs>'
                '<path d="M0 0L9 9" stroke="#9DB4CC" fill="none" mask="url(#m)"/>')
        self.assertEqual(self.rules_hit(sart(body, "backgrounds", "bg_x_far")), [])

    def test_text_in_art_needs_the_model(self):
        v = R.check(self.g, sart('<text fill="none" stroke="#E8F1FA">HI</text>'))
        self.assertEqual([x.rule for x in v], ["A-NO-TEXT"])
        self.assertFalse(v[0].fixable)

    def test_code_fix_keeps_comments_and_formatting(self):
        text = ('<?xml version="1.0"?>\n<!-- keep me -->\n<svg xmlns="http://www.w3.org/2000/svg" width="100" '
                'height="100" viewBox="0 0 100 100">\n  <path d="M0 0L9 9" stroke="#FFFFFF" fill="none"/>\n</svg>\n')
        art = A.SvgArtifact("art-src/enemies/x.svg", "enemies", "x", text)
        fixed, _ = R.auto_fix(self.g, art)
        self.assertEqual(fixed.text, text.replace("#FFFFFF", "#E8F1FA"))


class TextRuleTests(unittest.TestCase):
    def setUp(self):
        self.g = parse_guide(MINI_GDD)

    def test_vocab_replaced_longest_first_with_case(self):
        art = tart(briefing="AAA guns guard it. Destroy the AAA and drop your payload on the bomber.")
        fixed, counts = R.auto_fix(self.g, art)
        self.assertEqual(fixed.briefing, "Anti-aircraft guns guard it. Destroy the anti-aircraft guns and drop "
                                         "your bombs on the bomber.")

    def test_mid_sentence_acronym_stays_lower_case(self):
        fixed, _ = R.auto_fix(self.g, tart(briefing="Bomb the AAA guns near the bomber. Go now!"))
        self.assertEqual(fixed.briefing, "Bomb the anti-aircraft guns near the bomber. Go now!")

    def test_grounded_flags_enemies_not_in_the_mission(self):
        art = A.TextArtifact("m.json", "2", "Destroy the Bomber",
                             "Anti-aircraft guns and zeppelins guard the bomber. Destroy the bomber!",
                             ("bomber",), "", ("bomber", "fighter"))
        v = [x for x in R.check(self.g, art) if x.rule == "N-GROUNDED"]
        self.assertEqual(len(v), 2)
        self.assertFalse(v[0].fixable)

    def test_unicode_hyphen_still_names_the_target(self):
        art = A.TextArtifact("m.json", "3", "Destroy the Guns", "Enemy anti‑aircraft guns wait. Destroy them!",
                             ("aaa",), "", ("aaa",))
        self.assertEqual(R.check(self.g, art), [])

    def test_removed_terms_need_the_model(self):
        v = R.check(self.g, tart(briefing="Enemy radar sees the bomber. Destroy the bomber."))
        self.assertEqual([(x.rule, x.fixable) for x in v], [("N-VOCAB", False)])

    def test_length_title_target_exclaim(self):
        art = tart(title="bomber", briefing="Go! Now! Up! Fast!", targets=("zeppelin",))
        hit = sorted({v.rule for v in R.check(self.g, art)})
        self.assertEqual(hit, ["N-EXCLAIM", "N-LENGTH", "N-TARGET", "N-TITLE"])
        fixed, _ = R.auto_fix(self.g, art)
        self.assertEqual(fixed.briefing.count("!"), 1)
        self.assertTrue(fixed.briefing.endswith("Fast!"))
        self.assertEqual(fixed.title, "Bomber")


class LoopTests(unittest.TestCase):
    def setUp(self):
        self.g = parse_guide(MINI_GDD)

    def test_code_only_loop_fixes_what_it_can_and_reports_the_rest(self):
        art = tart(briefing="AAA fire everywhere. Our radar is down, so take out the bomber!")
        res = run(art, self.g, None, log=lambda *_: None)
        self.assertEqual(res.status, "needs_model")
        self.assertIn("Anti-aircraft guns", res.final.briefing)
        self.assertEqual([v.rule for v in res.violations], ["N-VOCAB"])     # radar: no period word

    def test_model_refines_what_code_cannot(self):
        good = '{"title": "Destroy the Bomber", "briefing": "An enemy bomber nears the airfield. Destroy the bomber!"}'
        fake = FakeBackend([good, '{"violations": []}'])
        art = tart(briefing="Our radar sees it. Destroy the bomber!")
        res = run(art, self.g, fake, judge=False, log=lambda *_: None)
        self.assertEqual(res.status, "clean")
        self.assertIn("VIOLATIONS TO FIX", fake.prompts[0])
        self.assertIn("radar", fake.prompts[0])
        self.assertIn("STYLE GUIDE", fake.prompts[0])

    def test_bad_model_revision_is_rolled_back_and_explained(self):
        worse = '{"title": "x", "briefing": "Radar! Radar! Radar!"}'
        good = '{"title": "Destroy the Bomber", "briefing": "An enemy bomber nears. Destroy the bomber!"}'
        fake = FakeBackend([worse, good])
        art = tart(briefing="Our radar sees it. Destroy the bomber!")
        res = run(art, self.g, fake, judge=False, log=lambda *_: None)
        self.assertEqual(res.status, "clean")
        self.assertFalse(res.steps[0].kept)
        self.assertIn("ROLLED BACK", fake.prompts[1])

    def test_judge_violations_drive_a_refine(self):
        art = tart()
        fake = FakeBackend([
            '{"violations": [{"rule": "N-VOICE", "where": "briefing", "why": "not second person"}]}',
            '{"title": "Destroy the Bomber", "briefing": "An enemy bomber nears. You must destroy the bomber!"}',
            '{"violations": []}'])
        res = run(art, self.g, fake, log=lambda *_: None)
        self.assertEqual(res.status, "clean")
        self.assertIn("You must", res.final.briefing)

    def test_model_slips_that_code_can_fix_are_fixed_not_rolled_back(self):
        slip = '{"title": "Destroy the Bomber", "briefing": "A bomber nears with its payload. Destroy the bomber!"}'
        fake = FakeBackend([slip])
        res = run(tart(briefing="Our radar sees it. Destroy the bomber!"), self.g, fake, judge=False, log=lambda *_: None)
        self.assertEqual(res.status, "clean")
        self.assertIn("its bombs", res.final.briefing)

    def test_model_may_not_rename_a_title_that_breaks_no_rule(self):
        reply = '{"title": "Bomber Assault", "briefing": "An enemy bomber nears. Destroy the bomber!"}'
        res = run(tart(briefing="Our radar sees it. Destroy the bomber!"), self.g, FakeBackend([reply]),
                  judge=False, log=lambda *_: None)
        self.assertEqual(res.final.title, "Destroy the Bomber")

    def test_new_hard_violation_is_never_kept_even_if_judge_clears(self):
        art = A.TextArtifact("m.json", "2", "Destroy the Bomber", "A bomber nears. Destroy the bomber!",
                             ("bomber",), "", ("bomber", "fighter"))
        fake = FakeBackend([
            '{"violations": [{"rule": "N-VOICE", "why": "a"}, {"rule": "N-VOICE", "why": "b"}]}',
            '{"title": "Destroy the Bomber", "briefing": "A zeppelin nears. You destroy the bomber!"}',
            '{"violations": []}'])
        res = run(art, self.g, fake, max_iterations=1, log=lambda *_: None)
        self.assertEqual(res.final, art)
        self.assertFalse(res.steps[-1].kept)

    def test_judge_ignores_rules_it_was_not_asked_about(self):
        v = parse_judgement('{"violations": [{"rule": "A-PALETTE", "why": "x"}, {"rule": "N-VOICE", "why": "y"}]}',
                            ["N-VOICE"])
        self.assertEqual([x.rule for x in v], ["N-VOICE"])

    def test_generate_then_fix(self):
        reply = "```svg\n" + svg('<path d="M0 0L9 9" stroke="#FFFFFF" fill="none"/>') + "```"
        fake = FakeBackend([reply])
        art = A.SvgArtifact("art-src/enemies/new_gun.svg", "enemies", "new_gun", "")
        res = run(art, self.g, fake, generate="draw a gun", judge=False, log=lambda *_: None)
        self.assertEqual(res.status, "clean")
        self.assertIn("#E8F1FA", res.final.text)
        self.assertEqual(res.steps[0].action, "generate")

    def test_parse_reply_rejects_junk(self):
        with self.assertRaises(A.ArtifactError):
            parse_reply(sart(""), "no svg here")
        with self.assertRaises(A.ArtifactError):
            parse_reply(tart(), '{"title": ""}')


class SaveTests(unittest.TestCase):
    def test_mission_save_changes_only_the_two_values(self):
        d = tempfile.mkdtemp()
        try:
            rel = "m/mission_09.json"
            os.makedirs(os.path.join(d, "m"))
            raw = '{\n  "id": 9,\n  "title": "Old",\n  "briefing": "Old — text.",\n  "length": 10\n}\n'
            with open(os.path.join(d, rel), "w", encoding="utf-8") as f:
                f.write(raw)
            art = A.load_mission(d, rel)
            A.save(d, art.with_text("New Title", "New text."))
            with open(os.path.join(d, rel), encoding="utf-8") as f:
                out = f.read()
            self.assertEqual(out, raw.replace('"Old"', '"New Title"').replace("Old — text.", "New text."))
            self.assertEqual(json.loads(out)["length"], 10)
        finally:
            shutil.rmtree(d)

    def test_svg_save_refuses_broken_xml(self):
        with self.assertRaises(A.ArtifactError):
            A.save(tempfile.gettempdir(), A.SvgArtifact("x.svg", "", "x", "<svg"))


class RepoAuditTests(unittest.TestCase):
    def test_mission_paragraph_comes_from_the_missions_list(self):
        with open(GDD, encoding="utf-8") as f:
            gdd = f.read()
        self.assertIn("bomber", A.gdd_mission_paragraph(gdd, 2).lower())
        self.assertIn("enemy base", A.gdd_mission_paragraph(gdd, 4).lower())
        self.assertNotIn("zeppelin", A.gdd_mission_paragraph(gdd, 4).lower())

    def test_every_repo_asset_loads_and_checks(self):
        g = load_guide(GDD)
        with open(GDD, encoding="utf-8") as f:
            gdd = f.read()
        for rel in A.all_svgs(REPO):
            R.check(g, A.load_svg(REPO, rel))
        for rel in A.all_missions(REPO):
            R.check(g, A.load_mission(REPO, rel, gdd))


if __name__ == "__main__":
    unittest.main()
