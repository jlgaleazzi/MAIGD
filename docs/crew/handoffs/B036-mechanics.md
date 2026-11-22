TASK: B036
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Weapons/BombBallistics.cs (new)
- Biplanes/Assets/Biplanes/Scripts/Weapons/Bomb.cs
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs
- Biplanes/Assets/Biplanes/Scripts/Core/GameBalance.cs
- Biplanes/Assets/Biplanes/Resources/Balance.json
- Biplanes/Assets/Biplanes/Tests/EditMode/BombBallisticsTests.cs (new)
SUMMARY:
- Bombs now fly a parabola. They start with PlayerPlane's real per-frame `_velocity` (x and y) times `bomb.inheritFactor`. Each physics tick, `BombBallistics.Step` (semi-implicit Euler: v.y -= g*dt, v.x *= max(0, 1 - drag*dt), pos += v*dt) moves them, and the nose turns along atan2(v.y, v.x). A bomb travelling left gets flipY, so its fins stay upright. The old 12x gravity is gone. The ground burst, ImpactPoint, the 0.5 burst radius, BombImpact and direct hits are unchanged.
- Bombs use the `Sprites/player/player_bomb` sprite (loaded once and cached; the solid placeholder is a fallback with one warning) and a horizontal 0.3 x 0.12 CapsuleCollider2D that turns with the body. Balance changes: new `bomb` block (gravity 10, drag 0.15, inheritFactor 1.0, alignToVelocity true; `BombData.Resolve` falls back to these), and `enemyHealth.aaa` is 125.
- The plane's damage tint is white only: no tint below smokeThreshold; at the threshold it is halfway from white to pencil grey #9DB4CC, and at 100% it is full pencil grey. The amber colours are removed.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes (+2 new file(s) not yet in the csproj), PASS Biplanes.Editor, exit 0. `tools/unity.sh` wasn't run because the editor has the project open.
- Tests compiled in a scratch build, using the B018 method (runtime DLL built with compile_check's helpers, test csproj retargeted to v4.7.2 with all 26 Tests/EditMode/*.cs files; temp csproj removed afterwards). BombBallisticsTests and every other test file compiled with 0 errors, except HudBombIconsTests.cs(70,41) and (92,49): `HudView` has no `FriendlyCol`. That file belongs to the parallel B038b HudView change, not to this task.
- With Unity closed: `bash tools/unity.sh test` and run BombBallisticsTests (11 tests): zero-velocity drop, forward drop lands at about vx*t, leftward drop, dive vs level vs climb, drag only on x, dt independence (10 small steps vs 1 large step, and 100 Hz vs 10 Hz over 1 s), dt<=0, AngleDegrees, left drop turns downward, BombData.Resolve fallbacks, and Balance.json bomb block plus aaa 125. Also rerun EnemyWeaponsAndPoolTests.Bomb_Update_BelowGround_* (the ground burst in Update is unchanged), BomberHitboxTests.PooledBomb_*, and AudioFixesQaTests (PlayerPlane.Create builds the new bomb pool).
- Play (user): Unity -> Biplanes -> BuildScenes, then Play.
  - Level flight: the bomb arcs forward and turns nose-down.
  - Facing left: it points left and turns downward.
  - Diving: it lands sooner. Climbing: it lofts first.
  - At 70% or more damage the plane goes grey, never amber.
- Note: player_bomb.png has no .meta yet. Unity imports it on the next focus, and SpriteImportSettings sets 100 px/unit. Until then, bombs log one warning and use the placeholder.
REQUESTS:
- orchestrator (ARCHITECTURE §3 "Bomb flight"):
  - Record that the step runs in Bomb.FixedUpdate (MovePosition/MoveRotation on the kinematic body, Rigidbody2D.linearVelocity zeroed and gravityScale 0, velocity kept in `Bomb.Velocity`), not in Update. The brief said Update, but the crew physics rule says FixedUpdate. The terrain check and ground burst stay in Update.
  - Record that a zero-velocity bomb points nose down (`BombBallistics.RestAngleDegrees` = -90).
  - Record that the collider is a horizontal CapsuleCollider2D of 0.3 x 0.12.
  - In §3 Player, the damage tint line should read: "white below smokeThreshold, halfway to pencil grey #9DB4CC at the threshold, full pencil grey at 100% (no colour)".
  - Gravity drops from about 118 to 10 u/s^2, so bombs fall much slower and the arc is wide. Mission designers may want to recheck AAA approach distances.
- qa:
  - No existing test assumes the old 12x fall or `gravityMultiplier`.
  - These tests touch Bomb and should be rerun: EnemyWeaponsAndPoolTests.Bomb_Update_BelowGround_BurstsOnSurfaceAtCrossingX_KillsAaaThere (calls Update with `_prevPos`; still valid), BomberHitboxTests.PooledBomb_OverlappingTwoBomberBoxes_*, AudioFixesQaTests (TryDropBomb with Vector2.zero, PlayerPlane.Create pool), AAAGunBulletTests (B037; it expects aaa 125, which Balance.json now has).
  - EnemyWeaponsAndPoolTests line ~283 "one player bomb destroys the AAA" now depends on B037's bomb-kills-outright rule in AAAGun.cs, because bombDamage 100 < aaa 125. I didn't verify this against B037's code.
- B038b owner: HudBombIconsTests.cs references `HudView.FriendlyCol`, which no longer exists. The test project won't compile until that is reconciled.
