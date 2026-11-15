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
# Bombers ignore atX for placement: they spawn at camera + 5*aspect + 1.5 + rand(0..6),
# and the camera is at about atX when the spawn triggers. The near edge is 10.4 at 16:9,
# 9.5 at 16:10 (WebGL 960x600) and 8.17 at 4:3; use the 4:3 minimum (QA T938 P2-5).
BOMBER_SPAWN_OFFSET = 8
# A zeppelin's y is height above the terrain; below 3 its underside turret sinks into
# the ground, can't be shot, and the hull stays shielded (QA T938 P1-1).
ZEPPELIN_MIN_Y = 3


def target_spawn_x(spawn: dict) -> float:
    """World x where a destroyById target appears (B018 failAtX rule)."""
    at_x = spawn.get("atX", 0)
    if spawn.get("type") == "bomber":
        return at_x + BOMBER_SPAWN_OFFSET
    return at_x  # zeppelin and every other type spawn at atX


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
        if s.get("type") == "zeppelin" and s.get("y", 0) < ZEPPELIN_MIN_Y:
            errors.append(f"{where} zeppelin y {s.get('y', 0)} must be >= {ZEPPELIN_MIN_Y} "
                          "(height above terrain; lower buries its underside turret)")
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
        fail_x = o.get("failAtX", -1)
        if fail_x >= 0 and o.get("type") != "destroyById":
            warnings.append(f"{where} failAtX is ignored on '{o.get('type')}' objectives")
        elif fail_x >= 0:
            # B018: failAtX is the WORLD x the target must not reach while flying left,
            # so it must lie left of where the target spawns.
            target = ids.get(o.get("targetId"))
            if target is None:
                errors.append(f"{where} failAtX target '{o.get('targetId')}' is never spawned")
            else:
                spawn_x = target_spawn_x(target)
                if fail_x >= spawn_x:
                    errors.append(f"{where} failAtX {fail_x} must be < the target's spawn x "
                                  f"{spawn_x} ({target.get('type')} '{target.get('id')}')")

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
