# T924 verification: reversible (Defender-style) flight

Task: T925 (qa). Verifies T924 (mechanics). Date: 2026-11-10.

## Verdict: PASS

The T924 change does what its handoff and ARCHITECTURE §3/§4 say, and it introduces no P0 or P1
regressions. One P1 bug is pre-existing (the "Next" mission path never resets the camera
or the previous mission's objects), but T924's new limits and FarthestX now inherit
that stale camera x, so it is listed first. The other findings are P2.

| Priority | Count | Caused by T924 |
|---|---|---|
| P0 | 0 | 0 |
| P1 | 1 | 0 (pre-existing; T924 inherits the stale camera x) |
| P2 | 6 | 3 |

## Checks run

| Check | Result |
|---|---|
| `python tools/compile_check.py` | `PASS Biplanes`, `PASS Biplanes.Editor`, exit 0 |
| Test assembly compile (scratch script: builds Biplanes.dll, then `Tests/EditMode/*.cs` against it, nunit.framework 3.5 (net472), UnityEngine/UnityEditor.TestRunner) | PASS. compile_check.py skips `/Tests/`, so this was a one-off check. Nothing was written to the project. |
| `bash tools/unity.sh test` | Not run. The Unity editor has the project open, so batch mode is blocked. Run the tests from the Test Runner window (EditMode). |
| Static review of the diff (7 files) | See below |
| GDD conformance | GDD §Controls: "Face and fly left / right (the world scrolls the way you face) — A / D". Matches. GDD changelog: "mission progress = farthest x reached". Spawns use FarthestX. surviveToEnd uses the current x, which is equivalent because the camera is clamped at `length`. |

## Static review: what was checked and holds

- **ScrollController easing.** `MoveTowards(v, Direction*speed, 2*max(speed,|v|)/ease*dt)`
  lands exactly on ±scrollSpeed (MoveTowards returns the target when it's within one step).
  A full reversal is 2·speed at 2·speed/ease, so it takes exactly `turnEaseSeconds` (30 ticks
  at 0.02 s with ease 0.6). Velocity is monotonic, with no overshoot or oscillation, and a
  turn reversed mid-ease returns cleanly. A speed drop eases down exactly (it uses `|v|` in
  accel). The tests cover this.
- **Clamping.** At a limit the camera is pinned exactly and velocity is zeroed only when
  pushing outward. While pinned, the velocity is zeroed every tick (there's no wind-up),
  so turning away unsticks on the next tick. `FarthestX` is updated after the clamp, so it is
  exactly `maxX` at the end, and surviveToEnd's `cameraX >= length` is reached. Tested.
- **Facing input.** D faces right and A faces left. When both are held, the last one
  pressed wins; holding both doesn't toggle; releasing one falls back to the key still held;
  pressing both on the same tick keeps the facing. `_prevLeft/_prevRight` also update on
  the runway, so holding A and then pressing D takes off facing right. Climb-out still
  forces up unless S is held. Tested.
- **Screen anchor.** The anchor is applied after the ride-along (`target.x += scrollDx`), so
  the screen x moves relative to the camera at `player.speed`. MoveTowards means no
  overshoot or jitter at the anchor. The anchor (viewport 0.3/0.7, ±3.56 at 16:9) is well
  inside the bounds clamp (camera ±(halfWidth − 0.5)). The clamp would only win when
  halfWidth < 0.83, which isn't a real aspect. `_velocity` is the true world velocity
  (ride-along + anchor easing + clamp), so bombs are physically consistent. See P2-3 for
  how it feels.
- **Bullets.** `Spawn(pos, rot, dir)` → `forwardDir.x = dir<0 ? -1 : 1`. The 2-argument
  overload still fires +x. The gun offset x is mirrored (−0.7 facing left). The bullet can
  spawn inside the player's own box collider (as it already could facing right), but
  `Bullet.team = Player` and `Projectile.OnTriggerEnter2D` skips same-team targets, so it
  can't self-hit. A bullet spawned inside an enemy that overlaps the nose hits it, which is
  the intended point-blank behaviour.
- **TerrainStrip window.** `[ChunkIndex(cam−30), ChunkIndex(cam+30)]` with 20-unit chunks
  always covers the visible ±8.9. Chunks outside the window are destroyed both ways, and one
  missing chunk is built per frame (plenty at 2–3 u/s). Create builds the initial window
  synchronously.
- **Spawn triggers.** Both the plane trigger (`atX`) and the ground trigger
  (`atX − (halfWidth+2)`) compare against `FarthestX`, and `_spawnedFlags` prevents repeats.
  Turning back can't re-trigger or advance a spawn.
- **Create reuse.** It reuses the live Instance and resets direction (+1), speed, velocity
  (full speed), limits (±∞) and `FarthestX` (= current camera x). The turn ease is reloaded
  from Balance. A destroyed Instance (after a scene reload) compares `== null` through
  Unity's null check, so a new one is created. Tested. The only gap is that the camera x
  itself is not reset (P1-1).
- **Rightward-only assumptions (grep `MissionDistanceX`, `camX`, `cameraX`, `scrollSpeed`,
  `halfWidth`, `orthographicSize`, `position.x <`).** There are no off-screen despawns
  in Enemies/ that assume a leftward exit. ParallaxBackground uses `Mathf.Repeat`, which
  works both ways. Enemy planes always enter at the right edge; that's intended, since
  spawns only fire while FarthestX grows. BomberAI's `CheckAndDropBomb` uses the current
  camera x, as noted by mechanics; `targetX` can't be set by MissionRunner, so it's dormant.
  No other hits.

## Bugs

```
BUG: "Next" mission keeps the old camera x, so the new mission's limits, FarthestX and spawns start from the previous mission's end
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs:131-136 (StartMission), with ResultView.cs:320 (OnNextClicked → gm.StartMission without a scene reload)
STEPS: Static evidence. Win mission 1 (the camera is pinned at maxX = 300) and press Next.
  StartMission(2) never moves the camera back, so ScrollController.Create sets
  FarthestX = 300 and SetLimits(300, 400). Every spawn group with atX ≤ ~300 (−11 for
  ground) fires on the first Update, all at once. The player can't fly left of x = 300, and
  surviveToEnd completes after 100 units instead of 400. The same path also leaves the
  previous PlayerPlane alive (still subscribed to the static PlayerInput events, and it
  calls ScrollController.SetDirection too), the previous enemies alive, adds a second
  ObjectiveTracker component, and subscribes GameEvents.EntityDestroyed += OnEntityDestroyed
  again, so each kill is counted twice.
EXPECTED: ARCHITECTURE §3 Camera/§5: each mission starts at its own start x with
  FarthestX = start, limits [start, length], and a clean world. T924's REQUESTS say
  Create "resets" on mission restart.
ACTUAL: Pre-existing (the camera was never reset before T924 either, and spawns already
  used camera x). T924 now also derives minX and FarthestX from the stale x. Suggested
  fix: in StartMission, before ScrollController.Create, move Camera.main back to x = 0
  (keep y/z), destroy the previous _playerPlane and _spawnedEntities, and unsubscribe
  OnEntityDestroyed and destroy the old ObjectiveTracker before adding new ones. Retry
  is not affected, because it reloads the scene.
```

```
BUG: length <= startCameraX gives the camera no upper limit at all
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs:135
STEPS: Static. If data.length <= startCameraX, maxCameraX = +Infinity. surviveToEnd then
  completes on the first Update, but if the mission has no surviveToEnd objective the camera
  scrolls right forever. The current data can't reach this (lengths 300–700, start 0)
  unless P1-1 happens with a shorter next mission.
EXPECTED: a bounded camera. SetLimits already collapses maxX to minX when max < min, so
  passing data.length unconditionally (pinning at start) would be safer.
ACTUAL: unbounded scroll in that edge case. This matches what the T924 handoff and
  ARCHITECTURE §3 describe, so it's a design choice to revisit, not a deviation.
```

```
BUG: Holding A and W on the runway takes off and turns left on the same tick
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs:330-349
STEPS: Mission 1, on the runway: hold A, then press W. The takeoff block clears
  _onRunway, and UpdateFacing runs in the same tick with left && !right, so Facing = -1
  right away. The camera is pinned at minX = 0, so the plane just swings to the 70% anchor.
EXPECTED: ARCHITECTURE §3 PlayerPlane / T924 handoff: "D or W takes off facing right".
ACTUAL: W+A takes off facing left. It's harmless and arguably fine, but it doesn't match the
  wording. Either skip UpdateFacing on the takeoff tick or reword the spec.
```

```
BUG: Bombs dropped during the post-turn anchor swing drift opposite to the facing
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs:352-372 (the _velocity used by OnDropBomb)
STEPS: Fly right at a steady speed, press A, and drop a bomb within ~1.4 s. The plane's
  world velocity is camera (~-2) + swing (+5) = about +3, so the bomb drifts right while
  the plane faces left.
EXPECTED: the T924 playtest step 10 says "facing left: a bomb drifts left". That's true
  only after the swing settles (velocity = -scrollSpeed).
ACTUAL: physically consistent with the plane's world motion, but it may read as a bug
  in playtest. Decide whether bombs should inherit Facing*scrollSpeed instead. Otherwise
  this is a design note.
```

```
BUG: ARCHITECTURE §3 Missions still gives the ground spawn trigger as "camera x >= atX - half screen width - 2"
PRIORITY: P2 polish
OWNER: orchestrator (docs)
WHERE: docs/ARCHITECTURE.md §3 Missions (around line 160)
STEPS: Read the paragraph. The first sentence says spawns use FarthestX, but the ground
  entry sentence still says "camera x".
EXPECTED: "FarthestX >= atX - half screen width - 2", to match MissionRunner.cs:349-350.
ACTUAL: inconsistent wording. The code is right.
```

```
BUG: Pure T924 logic is only testable through reflection on private members
PRIORITY: P2 polish (testability)
OWNER: mechanics
WHERE: ScrollController.FixedUpdate (private); PlayerPlane anchor math inline in OnMove, plus private _velocity, _speed, _screenAnchor and OnFire
STEPS: Tests/EditMode/ScrollControllerTests.cs and PlayerFacingTests.cs reach these by
  reflection. A rename breaks the tests at runtime, not at compile time.
EXPECTED: small public static pure helpers, for example
  `ScrollController.StepVelocity(v, dir, speed, ease, dt)` and
  `PlayerPlane.AnchorWorldX(camX, halfWidth, facing, screenAnchor)`, plus a
  `PlayerPlane.Velocity` getter.
ACTUAL: reflection-based tests. They work, but they're brittle. No production code was changed.
```

```
BUG: Pooled bullets fly at the default speed 10 on their first use instead of Balance bulletSpeed 14
PRIORITY: P2 polish (pre-existing, not T924)
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Weapons/Bullet.cs Start(); Projectile.Init
STEPS: Static. Projectile.Init sets linearVelocity = direction * speed when the bullet is
  popped and activated. Bullet.Start, which loads bulletSpeed and bulletDamage, runs
  later that frame. So the first activation of each of the 40 pooled bullets uses
  speed 10 and damage 25 (the inspector defaults). Damage happens to match Balance, but speed doesn't.
EXPECTED: ARCHITECTURE §4 bulletSpeed 14 on every shot.
ACTUAL: the first 40 shots are slower. Load the Balance values in Init, or in Awake.
```

Testing note (not a bug): `com.unity.test-framework` (1.8.0) is only a transitive
dependency (depth 1 in packages-lock.json). It isn't listed in Packages/manifest.json. The
new test asmdef needs it. Add it to the manifest explicitly so it can't disappear.

## EditMode tests added

`Biplanes/Assets/Biplanes/Tests/EditMode/` (asmdef `Biplanes.Tests.EditMode`, Editor only,
references Biplanes, UnityEngine.TestRunner, UnityEditor.TestRunner, nunit.framework.dll,
`UNITY_INCLUDE_TESTS`):

- `ScrollControllerTests` (15 tests): Create defaults; steady scroll distance; negative speed
  clamp; invalid SetDirection ignored; reversal eases through 0, settles **exactly** on
  −speed within turnEaseSeconds ±1 tick, monotonic with no overshoot; no snap on the
  first tick; a mid-turn reversal returns exactly to +speed without oscillating; a speed
  drop eases exactly; minX pin is exact, with zero velocity and no jitter over 20 ticks;
  turning away from minX/maxX unsticks; maxX pin is exact, with FarthestX = maxX;
  SetLimits(max<min) collapses; FarthestX keeps the true peak while flying back; Create
  reuses the Instance and resets direction, speed, velocity, FarthestX and limits.
- `PlayerFacingTests` (15 tests): starts facing right; A turns left, flips the sprite and
  sets the ScrollController direction; D turns back; neither held keeps the facing; both
  held (last pressed wins, no toggle, falls back on release); both pressed on the same tick
  keeps the facing; runway A does nothing; runway D takes off facing right with climb-out;
  climb-out forces up and S overrides it; anchor easing at ±player.speed for each facing;
  the anchor lands exactly and sits still; the anchors are mirrored and screenAnchor is
  in (0, 0.5); Bullet.Spawn direction sign and the 2-argument overload is +x; OnFire
  facing left spawns left of the plane and flies left.

They have not been run in Unity yet (the editor is open). Risk to watch: `Projectile.Init`
calls `StartCoroutine` in EditMode for the two bullet tests. If Unity rejects that in
EditMode, those 2 tests fail in their setup, and the cause is the test harness, not T924.

## Manual playtest checklist

T924's list (PC first, then WebGL in Chrome, Firefox and Safari):

1. Mission 1 takeoff: on the runway, A does nothing. D or W takes off facing right with the
   automatic climb-out, and the world scrolls right at scrollSpeed.
2. After takeoff the plane settles at about 30% from the left edge. W/S climb and dive at
   the same speed as before.
3. Press A: the sprite flips at once, and the scroll slows, stops and reverses over about
   0.6 s with no snap.
4. After the turn the plane swings to about 70% in about 1.5 s. Press D and it swings back
   to about 30%.
5. Hold Space after turning: bullets leave the left (nose) side and fly left. Turn right and
   they fly right.
6. Fly left to the mission start: the camera stops dead and the plane stays on screen. Fly
   right to the mission length: the camera stops and surviveToEnd completes.
7. Pass some spawn groups, turn back over them, then turn right again: nothing spawns twice
   or early.
8. Flying left, the ground is drawn the whole way with no gaps or pop-in, and the same is
   true after turning right again.
9. The parallax reverses after a turn, and the far layer is the slowest.
10. Regression: ground-contact crash, ramming a fighter or bomber, and the bomb's forward
    drift, all facing right and facing left. When facing left, a bomb drifts left once the
    swing has settled (see P2-3).

Added by QA:

11. Hold D, then also press A: the plane turns left. Release A (D still held): it turns
    right. Hold both: no flicker.
12. Press A and D on exactly the same frame from neither held: the facing doesn't change.
13. On the runway, hold A, then press W: note that the plane takes off and turns left
    (P2-2). Check that it isn't confusing.
14. At the mission start limit, facing left: the plane sits at 70% with the camera still.
    Press D: the camera starts moving right within a tick (it doesn't stick).
15. Turn left right as a fighter group's atX is reached: the group may still spawn at the
    right edge (behind you) while the camera decelerates. Check it reads fine.
16. Drop a bomb in the first second after a turn: it drifts against the facing (P2-3).
    Decide whether that's acceptable.
17. Win mission 1, press **Next**: check whether mission 2 starts with a burst of spawns
    and a camera that can't go back to x 0 (P1-1). Compare with Retry, which reloads the
    scene and should be clean.
18. Fly left for a long time at the start limit, then right: terrain chunks are rebuilt
    with no hitch, and memory is stable (the Profiler shows chunk count ≤ 4).
19. WebGL at a narrow window (for example a portrait browser): the plane still reaches both
    anchors without hitting the screen-edge clamp.
20. Run Test Runner → EditMode → Biplanes.Tests.EditMode: all 30 tests pass.
