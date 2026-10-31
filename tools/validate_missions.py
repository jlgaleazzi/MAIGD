"""Validate Biplanes mission JSON against docs/ARCHITECTURE.md section 5.

    python tools/validate_missions.py            validate all mission_0N.json
    python tools/validate_missions.py 3          validate mission_03.json only
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MISSIONS = ROOT / "Biplanes" / "Assets" / "Biplanes" / "Resources" / "Missions"

TYPES = {"fighter", "bomber", "aaa", "zeppelin", "barrageBalloon", "healthBalloon",
         "building", "tree", "enemyBase", "airfield", "playerHQ"}
GROUND = {"aaa", "building", "tree", "enemyBase", "airfield", "playerHQ"}
BOMB_TARGETS = {"aaa", "enemyBase"}
PATTERNS = {"static", "straight", "sine", "dive", "escort", "drift"}
OBJECTIVES = {"destroyAllOfType", "destroyById", "bombTarget", "surviveToEnd"}
THEMES = {"farmland", "frontline", "enemyland", "stormsky"}
TOP_KEYS = {"id", "title", "briefing", "scrollSpeed", "length", "playerStart", "background",
            "terrain", "spawns", "objectives"}
TARGET_SECONDS = {1: 150, 2: 180, 3: 210, 4: 240, 5: 270}
BOMB_CAPACITY = 7


def validate(path: Path, number: int) -> list:
    errors, warnings = [], []
    try:
        m = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        return [f"invalid JSON: {exc}"]

    missing = TOP_KEYS - m.keys()
    if missing:
        errors.append(f"missing keys: {sorted(missing)}")
        return errors
    if m["id"] != number:
        errors.append(f"id is {m['id']}, expected {number}")
    if m["background"] not in THEMES:
        errors.append(f"background '{m['background']}' not in {sorted(THEMES)}")
    if m["scrollSpeed"] <= 0 or m["length"] <= 0:
        errors.append("scrollSpeed and length must be > 0")
    else:
        secs = m["length"] / m["scrollSpeed"]
        target = TARGET_SECONDS.get(number)
        if target and abs(secs - target) > target * 0.25:
            warnings.append(f"duration {secs:.0f}s is far from the {target}s target")

    xs = [t.get("atX") for t in m["terrain"]]
    if not xs or xs != sorted(xs):
        errors.append("terrain must be non-empty and sorted by atX")

    ids, bombable = {}, 0
    last_x = -1e9
    for i, s in enumerate(m["spawns"]):
        where = f"spawns[{i}]"
        for key in ("atX", "type", "y", "count", "spacing", "pattern", "id"):
            if key not in s:
                errors.append(f"{where} missing '{key}'")
        if s.get("type") not in TYPES:
            errors.append(f"{where} type '{s.get('type')}' not allowed")
        if s.get("pattern") not in PATTERNS:
            errors.append(f"{where} pattern '{s.get('pattern')}' not allowed")
        if s.get("atX", 0) < last_x:
            errors.append(f"{where} spawns must be sorted by atX")
        last_x = s.get("atX", last_x)
        if not 0 <= s.get("atX", 0) <= m["length"]:
            errors.append(f"{where} atX outside [0, length]")
        if s.get("type") in GROUND and s.get("y", 0) != 0:
            errors.append(f"{where} ground unit must have y = 0")
        if not 0 <= s.get("y", 0) <= 8.5:
            errors.append(f"{where} y must be in [0, 8.5]")
        if s.get("count", 1) < 1:
            errors.append(f"{where} count must be >= 1")
        if s.get("id"):
            if s.get("count", 1) != 1:
                errors.append(f"{where} spawns with an id must have count 1")
            if s["id"] in ids:
                errors.append(f"{where} duplicate id '{s['id']}'")
            ids[s["id"]] = s
        if s.get("pattern") == "escort" and not s.get("escortOf"):
            errors.append(f"{where} escort pattern needs escortOf")
        if s.get("type") in BOMB_TARGETS:
            bombable += s.get("count", 1)
    for i, s in enumerate(m["spawns"]):
        if s.get("escortOf") and s["escortOf"] not in ids:
            errors.append(f"spawns[{i}] escortOf '{s['escortOf']}' has no matching id")

    if not m["objectives"]:
        errors.append("at least one objective is required")
    spawned_types = {s.get("type") for s in m["spawns"]}
    for i, o in enumerate(m["objectives"]):
        where = f"objectives[{i}]"
        if o.get("type") not in OBJECTIVES:
            errors.append(f"{where} type '{o.get('type')}' not allowed")
        if o.get("type") == "destroyAllOfType" and o.get("targetType") not in spawned_types:
            errors.append(f"{where} targetType '{o.get('targetType')}' is never spawned")
        if o.get("type") in ("destroyById", "bombTarget") and o.get("targetId") not in ids:
            errors.append(f"{where} targetId '{o.get('targetId')}' is never spawned")
        if o.get("type") == "bombTarget" and ids.get(o.get("targetId"), {}).get("type") not in BOMB_TARGETS:
            errors.append(f"{where} bombTarget must point at aaa or enemyBase")
        if o.get("failAtX", -1) >= 0:
            target = ids.get(o.get("targetId"), {})
            if o["failAtX"] <= target.get("atX", 0):
                errors.append(f"{where} failAtX must be beyond the target's spawn atX")

    if bombable > BOMB_CAPACITY:
        warnings.append(f"{bombable} bomb targets but only {BOMB_CAPACITY} bombs and no refills")
    if "healthBalloon" not in spawned_types:
        warnings.append("no health balloon in this mission")
    return errors + [f"warning: {w}" for w in warnings]


def main(argv: list) -> int:
    numbers = [int(a) for a in argv] if argv else range(1, 6)
    hard_fail = False
    for n in numbers:
        path = MISSIONS / f"mission_{n:02d}.json"
        if not path.exists():
            print(f"SKIP mission_{n:02d}.json (not written yet)")
            continue
        problems = validate(path, n)
        errors = [p for p in problems if not p.startswith("warning")]
        hard_fail |= bool(errors)
        print(f"{'FAIL' if errors else 'OK  '} mission_{n:02d}.json")
        for p in problems:
            print(f"     - {p}")
    return 1 if hard_fail else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
