TASK: B029
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Core/PlanePitch.cs (rewritten: shared defaults, ease, target, body angle, nose, rotated-box lowest point)
- Biplanes/Assets/Biplanes/Scripts/Core/GameBalance.cs (PitchData: 3 new keys + Resolve; EnemyFireData: aimToleranceDegrees + DefaultAimToleranceDegrees)
- Biplanes/Assets/Biplanes/Resources/Balance.json (pitch block: fighterMaxDegrees 40, easeDegreesPerSecond 60, fullPitchSpeed 3; enemyFire: aimToleranceDegrees 10)
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs
- Biplanes/Assets/Biplanes/Scripts/Enemies/FighterAI.cs
- Biplanes/Assets/Biplanes/Scripts/Enemies/BomberAI.cs
- Biplanes/Assets/Biplanes/Tests/EditMode/PlanePitchTests.cs, EnemyFireTests.cs, PlayerFacingTests.cs
SUMMARY:
- P0-1: NoseDirection = (Facing*cos p, sin p); the muzzle is pos + nose*GunOffset.x and the bullet is rotated to atan2(nose). Facing left the round leaves the nose and flies left; level flight is exactly the old (+-0.7, 0) offset. P2-3: the pitch is applied with Rigidbody2D.MoveRotation(BodyAngle) on player, fighter and bomber (no transform writes). The sprite and hitboxes sit on the body, so they turn together. The crash tumble starts from BodyAngle. Public PitchAngle/BodyAngle/NoseDirection are on all three planes. P2-2: the bomber uses Facing -1. P2-4: one resolver PitchData.Resolve with PlanePitch.Default* consts. P2-1: ease = pitch.easeDegreesPerSecond (60, full pitch in 0.25 s) and target = vy/fullPitchSpeed*max. P2-5: the terrain check uses HitboxLowestPoint() (rotated box, exact); the about 0.19 corner drop at 15 deg is accepted and tested.
- P1-1/P1-2 (fighters fire along the nose): while the player is in the 40 deg cone and range, UpdatePitch eases the nose toward the bearing (sign: PitchToward = climb angle of the bearing, so nose up for a player above), clamped to pitch.fighterMaxDegrees. Otherwise the nose follows the climb/dive. TickFire fires only when the player is within enemyFire.aimToleranceDegrees of the nose. The shot is the nose +-spreadDegrees, from the nose. Cadence, range, cone, speed and damage are unchanged.
- M1 fire estimate: tracking runs before and during the 0.4-1.2 s first-shot delay, and acquiring a 10-25 deg bearing takes 0.17-0.4 s, so the shot count per pass is unchanged: 1, or 2 if the player hangs back. The worst-case wave of 4 is still 4-8 shots (32-64% if all hit). The whole of M1 is still at most about 12-24 shots. Real hits drop: shots can be up to 10+4 deg off the player (1.4+ units at 8 range), and inside about 3 units a dodging player outruns the 60 deg/s nose. Survivable, slightly easier than B023.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes (+1 new file not yet in the csproj: PlanePitch.cs), PASS Biplanes.Editor, exit 0. `tools/unity.sh` was not run because the editor has the project open.
- Tests scratch build (B018 method; runtime DLL from compile_check helpers, test csproj without ProjectReferences, v4.7.2, all 15 Tests/EditMode/*.cs): runtime PASS, tests PASS, 0 errors. No zz_* csproj left; the Biplanes/obj folder pre-dates this task (Nov 6/12) and is gitignored. The tests were not run.
- With Unity closed: `bash tools/unity.sh test`. Expect every test to pass: all of PlanePitchTests, including the 5 QA marked FAIL, now reading BodyAngle/PitchAngle because MoveRotation is not applied in EditMode. Also PlayerFacingTests.OnFire_FacingLeft_SpawnsAtMirroredGunAndFliesLeft and the new OnFire_FacingLeftOrRight_WhileClimbing_BulletLeavesTheNose, and the EnemyFireTests plus the new Fighter_HoldsFire_UntilItsNoseIsOnThePlayer and Fighter_Shot_FliesAlongTheNose_WithinSpread_NotStraightAtThePlayer.
- Playtest (Unity: Biplanes > BuildScenes, Play): QA checklist T942 §6 P-1..P-7, E-1..E-4, M5-a..f. Extra: E-2/E-3, a fighter visibly turns its nose onto a player above or below (up to 40 deg) and its tracers leave the nose; it holds fire until the nose is on you. P-3: no jitter with MoveRotation.
REQUESTS:
- orchestrator: ARCHITECTURE.md:
  - §3 Player: "Pitch: PlanePitch.Step eases PitchAngle (nose up +) toward vy/pitch.fullPitchSpeed*pitch.maxDegrees at pitch.easeDegreesPerSecond. The body turns with _rb.MoveRotation(BodyAngle = PitchAngle*Facing). NoseDirection = (Facing*cos p, sin p); bullets spawn at pos + NoseDirection*0.7 via Bullet.Spawn(Vector3, Quaternion, Vector2). The terrain check uses HitboxLowestPoint() (rotated box; up to about 0.19 lower than level at 15 deg, accepted)."
  - §3 Enemies, FighterAI: replace the aim sentence with "the nose eases toward the bearing to the player while the player is inside fighterConeDegrees and range (limit pitch.fighterMaxDegrees), else follows climb/dive; it fires only when the player is within enemyFire.aimToleranceDegrees of the nose, along the nose +-spreadDegrees, from the nose. Public PitchAngle, BodyAngle, NoseDirection (current drawn nose)."
  - §3 Enemies, BomberAI: "pitch from climb/dive, Facing -1, MoveRotation; the gunner stays a swivel gun from the centre (not nose-bound)."
  - §3 line 150 (zeppelin line of fire): "level-flight player bullets; pitched shots widen or close the band (T942 §3)".
  - §3 Weapons (lines 177-178): add Bullet.Spawn(Vector3, Quaternion, Vector2).
  - §4 Balance: `pitch {maxDegrees 15, fighterMaxDegrees 40, easeDegreesPerSecond 60, fullPitchSpeed 3}` read via PitchData.Resolve (defaults PlanePitch.Default*); `enemyFire.aimToleranceDegrees 10`.
- orchestrator (design): the GDD says enemy machine-gun bullets leave along the nose. The bomber's gunner is a swivel gun, so I kept it firing from the centre in any direction. Record it as an exception in the GDD, or file a task if bombers should be nose-bound too.
- orchestrator (scope note): enemyFire.aimToleranceDegrees went into the enemyFire block of Balance.json, outside the "pitch block only" line in the brief, because acceptance P1-2 named that key.
- qa: run the tests and the T942 §6 checklist; recheck M5-c (with a 60 deg/s ease, a dive-and-fire now levels in about 0.25 s).
