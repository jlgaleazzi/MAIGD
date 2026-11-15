TASK: V001a
AGENT: mechanics
STATUS: DONE
FILES:
- tools/validate_missions.py
SUMMARY:
- Replaced the old failAtX rule (failAtX > the target's atX) with the B018 rule. For destroyById objectives with failAtX >= 0, failAtX must be < the target's spawn x. The new helper `target_spawn_x(spawn)` gives that x: atX for zeppelins and every other type except bombers, and atX + BOMBER_SPAWN_OFFSET (10) for bombers, which spawn about 10.4 to 16.4 right of the camera.
- The target is looked up by targetId among the id'd spawns. If it is missing, the validator reports the error "failAtX target '<id>' is never spawned", alongside the existing targetId error.
- failAtX >= 0 on a non-destroyById objective now gives a warning (the runtime ignores it), not an error.
VERIFY:
- `python tools/validate_missions.py`: mission_02, 03 and 05 are OK. mission_01 and 04 FAIL only on "y must be in [0, 8.5]", the known negative-y issue, left unfixed as briefed. Exit 1.
- Checked the new rule on scratch copies:
  - M2 with failAtX 95 fails (the spawn x is 90); with 89 it passes.
  - M5 with failAtX 430 fails (the spawn x is 430); with 429 it passes.
  - An unknown targetId is reported as an error.
- No C# was touched, so no compile check was needed.
REQUESTS:
- none
