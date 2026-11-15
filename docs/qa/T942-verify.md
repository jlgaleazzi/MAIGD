# T942 verify: airplane pitch and nose-aligned bullets (goal agent, gpt-oss:120b)

**Verdict: FAIL.** 1 P0, 2 P1 and 6 P2.
- The pitch helper, the Balance block and the player's climb/dive pitch are right when the plane faces right.
- **Facing left, the player's gun now fires backwards (to the right) from the tail.** This is a regression of T924/B002, and the existing test `PlayerFacingTests.OnFire_FacingLeft_SpawnsAtMirroredGunAndFliesLeft` will now fail.
- **The fighter's nose points the opposite way to its shot.** Shots more than 15° off level also leave off the nose.

Unity is open, so I couldn't run the tests or play the game. Everything below comes from the code, the compile check, the scratch build and the geometry script.

The agent's files:
- `Scripts/Core/PlanePitch.cs` (new)
- `Scripts/Core/GameBalance.cs` (`PitchData`, `pitch`)
- `Resources/Balance.json` (`pitch.maxDegrees` 15)
- `Scripts/Player/PlayerPlane.cs`
- `Scripts/Weapons/Bullet.cs`
- `Scripts/Enemies/FighterAI.cs`
- `Scripts/Enemies/BomberAI.cs`

I separated the agent's own edits from earlier uncommitted work by comparing its PATCH replies (`tools/goal_agent/replies/gpt-oss/*_reply.txt`) with the "before" text in its prompts.

## 1. Commands

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes (+1 new file not yet in the csproj: `PlanePitch.cs`), PASS Biplanes.Editor, exit 0 |
| `bash tools/unity.sh compile/test` | Not run: the editor has the project open |
| Tests scratch build (B018 method: runtime DLL built with compile_check's helpers; test csproj with its ProjectReferences removed, TargetFrameworkVersion v4.7.2, all `Tests/EditMode/*.cs` added) | 15 files (14 existing plus the new `PlanePitchTests.cs`), **0 errors**. No `zz_*` csproj or obj folder was left in the project |
| `python tools/validate_missions.py` | M1 and M4 fail "y must be in [0, 8.5]". That's the known V001, out of scope. The others are OK |

Static checks:
- `PlanePitch` is in Biplanes.Core, under Scripts/Core. It's a single public static class.
- `PitchData` lives in GameBalance.cs, which follows the existing pattern for Balance data classes.
- No `rb.velocity` and no `UnityEngine.Input`.
- Every API it uses exists (`PlanePitch.Step`, `GameBalance.pitch`, `EnemyBullet.Spawn/AimWithinCone/InFiringCone`, `Bullet.Spawn(Vector3, Quaternion, Vector2)`).
- No hand-written YAML. `PlanePitch.cs` has no `.meta` yet, because Unity hasn't refreshed since the agent wrote it. Unity creates the `.meta` on the next refresh (focus the editor).
- The hot paths have no per-frame allocations and no GetComponent calls: `GameBalance.Get()` returns a cached static, and `Quaternion.Euler` and `Vector2` are structs.
- No second Awake, Start or FixedUpdate was added. Fighter and bomber extend their existing ones, and the player's pitch runs inside `OnMove`.

## 2. Piece by piece against the GDD ("Flight attitude (pitch)")

| Item | Verdict | Evidence |
|---|---|---|
| `PlanePitch.Step` direction and clamp | OK | `target = clamp(vy·max, ±max)`, then `MoveTowardsAngle` by `max·dt`, then a final clamp. Climbing gives +, diving gives −, and it never exceeds ±max |
| Easing | Too slow (P2-1) | A fixed rate of max°/s (15°/s): 1 s from level to full, 2 s for a full reversal. The target saturates at 1 u/s, so the angle doesn't scale with climb speed |
| Balance default fallback | Inconsistent (P2-4) | Player and fighter use `?.maxDegrees ?? 15f`, so a missing *key* gives 0 and pitch is silently off. The bomber uses `> 0` and falls back to 15 |
| Player nose up/down when facing right | OK | `transform.rotation = Euler(0,0,pitch·facing)` |
| Player facing left, drawn nose | OK | With flipX the nose is local −x, and a z of −pitch raises it |
| Player facing left, **gun** | **P0-1** | `NoseDirection = (cos(pitch·facing), sin(pitch·facing))` = (cos p, −sin p): it points **right** and is mirrored vertically. The muzzle is `rot * (0.7,0,0)`, which is on the tail side |
| Hitboxes rotate with the visual | Yes | The BoxCollider2D is on the same root that gets rotated (player, fighter, bomber). The spec asked for a visual child; the root works but is a deviation (P2-3) |
| Crash fall (T937) | OK | After `TriggerCrash`, `OnMove` returns early, so `MoveRotation(_crashAngle)` is the only rotation writer. `_crashAngle` starts from `_rb.rotation`, which is the last pitch |
| Plane collision grace (B025) | Unaffected | Same trigger path. Only the collider orientation changed |
| Screen-bounds clamp | Unaffected | It clamps the centre position. At the top or bottom bound `_velocity.y` = 0, so the nose eases back to level, which is plausible |
| Ground check | Threshold moves (P2-5) | Rotated 1.6×0.8 box: the AABB half-height goes from 0.40 to 0.59 at 15°, so `bounds.min.y` drops by **0.19**, nose up or nose down. Climb-out after takeoff is safe: it rises 0.1 per tick and the tail sinks about 0.005 per tick |
| Smoke | Unaffected | `_smokeTrailRoot` is never assigned. Smoke is a sprite tint, so rotation can't misplace it |
| Reset on respawn, Retry or Next | OK | `Create` builds a new GameObject (identity rotation) and sets `_pitchAngle = 0` |
| Pause | OK | timeScale 0 stops FixedUpdate, so `OnMove` isn't called |
| Player bullets: speed vector | Wrong when facing left (P0-1) | `Bullet.Spawn(pos, rot, Vector2)` normalises the direction and `Init` sets `linearVelocity = dir·speed`. Non-horizontal directions work |
| Player bullets: sprite rotated | OK | It's passed `rot`. The bullet sprite is a symmetric 6×2 bar, so ±180° doesn't matter |
| Bullet lifetime and recycling | Unchanged | The 3 s lifetime coroutine is the same as before; there is no off-screen check (pre-existing). Tunnelling: the bullet steps 0.28 per tick, and the narrowest collider plus the 2×0.08 bullet radius is ≥ 0.46 |
| `Bullet.Spawn(pos, rot, int)` and the 2-argument overload | OK | They now go through the Vector2 overload, with the same behaviour |
| Fighter pitch from climb or dive | OK | vy = Δy/dt; z = −pitch with flipX, so a climbing fighter's nose goes up |
| Fighter nose vs shot | **P1-1, P1-2** | See the bugs |
| Bomber pitch | Latent sign bug (P2-2) | `Euler(0,0,+pitch)` on a flipX sprite. Bombers only move in y on the escort path, and no mission gives a bomber `escort`, so in play the bomber never pitches. Its flight is perfectly level, so that's correct today |
| Bomber gunner and bombs | Unchanged | The gunner fires from the centre in any direction. `EnemyBomb` keeps its spawn point and velocity. Player bombs: `BombOffset` isn't rotated and they inherit only x velocity. The GDD says "bombs still drop with gravity", so that's OK |
| Other readers of rotation | None | grep: no HUD, AI or aim code reads `transform.rotation`/`eulerAngles`. `MissionRunner.PlaceOnGround` reads `bounds.extents.y` once at spawn, at pitch 0. AAA leads with `PlayerPlane.Velocity`, which is unchanged. Zeppelin turrets aim at the player's position |

## 3. Regression: zeppelin turret line of fire (B021/T940) with a pitched player

I recomputed the T940 clear band as a function of the bullet's flight angle. This is the same box set and bullet radius 0.08, swept-circle vs AABB, with a scan step of 0.001. Script: `t942_geom.py` in the session scratchpad. θ is the world climb angle of the shot: + means the nose is up.

| Turret | Approach | −15° | −10° | −5° | 0° | +5° | +10° | +15° |
|---|---|---|---|---|---|---|---|---|
| Underside | from the left (facing right) | 0.00 | 0.00 | 0.02 | **0.31** | 0.40 | 0.49 | 0.58 |
| Underside | from the right (facing left) | 0.00 | 0.10 | 0.21 | **0.31** | 0.40 | 0.49 | 0.58 |
| Top fore | from the left | 0.55 | 0.49 | 0.43 | **0.37** | 0.20 | 0.02 | 0.00 |
| Top aft | from the right | 0.55 | 0.49 | 0.43 | **0.35** | 0.14 | 0.00 | 0.00 |

(For each top turret on its far side, the other top turret screens it at small angles, as in T940: one of the two dies first.)

What this means:
- **Mission 5 stays winnable facing right.**
  - Level flight keeps the T940 bands: when no key is held, the nose eases back to 0° within at most 1 s.
  - Climbing up at the underside turret *widens* its band, and diving down onto a top turret widens that band.
  - The only trap: diving down into the underside band and firing at once. Below about −5° every bullet hits a gondola, and a shielded hull absorbs it. Level off for about 0.7–1 s first, or come from below while climbing.
- Facing left is useless until P0-1 is fixed: every bullet flies right.
- `ZeppelinAndBaseTests.Zeppelin_EveryTurret_HasAClearHorizontalLineOfFire` still holds; the new test `Player_LevelFlight_FiresHorizontally_OnceEased` covers it.
- ARCHITECTURE §3 line 150 ("horizontal player bullets") should say "level-flight bullets; pitched shots widen or close the band as in T942".

Enemy fire cone and aim:
- `InFiringCone`/`AimWithinCone` still use flight-direction left with a 40° half-angle, so the fire cone didn't change.
- Only the muzzle moved, from `Forward·0.75` to `shotDir·0.75`.
- See P1-2 for the conflict between the cone and the GDD.

## 4. Tests

New: `Biplanes/Assets/Biplanes/Tests/EditMode/PlanePitchTests.cs`. It compiles in the scratch build but hasn't been run. Expected results against the current code:

| Test | Expected now |
|---|---|
| Step_Climb_RaisesNose_Dive_LowersNose, Step_IsEased_NotASnap, Step_ClampedToMax_BothWays, Step_LevelFlight_ReturnsToZero, Step_ZeroDt_NoChange, Balance_HasPitchBlock | pass |
| Player_NewPlane_StartsLevel, Player_FacingRight_Climb_NoseUp_Dive_NoseDown, Player_FacingLeft_Climb_DrawnNoseUp, Player_Pitch_NeverExceedsBalanceMax, Player_Hitbox_RotatesWithTheSprite | pass |
| Player_FacingRight_Climbing_BulletLeavesAlongTheNose, Player_LevelFlight_FiresHorizontally_OnceEased, Fighter_Climbing_DrawnNoseUp | pass |
| **Player_NoseDirection_MatchesDrawnNose_BothFacings** | **FAIL** (P0-1) |
| **Player_FacingLeft_Climbing_BulletLeavesAlongTheNose** | **FAIL** (P0-1) |
| **Fighter_ShotUpward_NoseAgreesWithBullet** | **FAIL** (P1-1) |
| **Fighter_ShotAtConeEdge_StillLeavesAlongTheNose** | **FAIL** (P1-2) |
| **Bomber_Climbing_DrawnNoseUp** | **FAIL** (P2-2) |
| Existing `PlayerFacingTests.OnFire_FacingLeft_SpawnsAtMirroredGunAndFliesLeft` | **now FAILS** (P0-1 regression) |

How the tests measure the nose:
- They read the drawn nose from `transform.rotation` × (flipX ? −x : +x).
- If mechanics moves the rotation to `_rb.MoveRotation` (P2-3), EditMode won't apply it, because no physics step runs. Expose the angle (for example a public `PitchDegrees`), and QA will switch the tests to read it.

## 5. Bugs

```
BUG: P0-1 Facing left, the player's machine gun fires backwards (right) from the tail
PRIORITY: P0 crash/blocks play
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs:507-512 (NoseDirection), :493-500 (OnFire muzzle/rotation)
STEPS: Static + test. Facing left at pitch p, visualAngle = -p, so NoseDirection = (cos -p, sin -p) = (cos p, -sin p): +x, and vertically mirrored. The muzzle is rot * GunOffset = (0.7cos p, -0.7 sin p), behind the plane. Before this change OnFire mirrored GunOffset.x by _facing and passed _facing. PlayerFacingTests.OnFire_FacingLeft_SpawnsAtMirroredGunAndFliesLeft (existing) and PlanePitchTests.Player_FacingLeft_Climbing_BulletLeavesAlongTheNose / Player_NoseDirection_MatchesDrawnNose_BothFacings (new) fail.
EXPECTED: GDD Controls / T924 facing: A turns the plane and it fires the way it faces. GDD Flight attitude: bullets leave along the nose direction.
ACTUAL: After turning left you can't hit anything ahead. Chasing the M2 bomber or the M5 zeppelin after passing it is impossible.
FIX HINT: NoseDirection = new Vector2(_facing * Mathf.Cos(p), Mathf.Sin(p)) with p = _pitchAngle in radians; muzzle = transform.position + (Vector3)(NoseDirection * GunOffset.x); bullet rotation = Euler(0,0,Atan2(nose.y, nose.x)).
```

```
BUG: P1-1 Fighter nose dips when it shoots upward (pitch sign inverted on fire)
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/FighterAI.cs:269
STEPS: Static + test. Shot up-left at angle a: aimAngle = 180 - a, and DeltaAngle(180, 180 - a) = -a, so _pitchAngle = -a (nose down). The visual z = -(-a) = +a turns the left-pointing nose down. PlanePitchTests.Fighter_ShotUpward_NoseAgreesWithBullet fails.
EXPECTED: GDD Flight attitude: enemy bullets leave the gun along the nose direction.
ACTUAL: Nose and shot are mirrored about level. Every fighter shot at a player above or below shows the nose pointing the other way.
FIX HINT: desiredPitch = Mathf.DeltaAngle(aimAngle, 180f) (positive = nose up for a left-flying plane).
```

```
BUG: P1-2 Fighter shots up to 40 degrees off level leave off the nose (pitch clamped at 15)
PRIORITY: P1 mechanic wrong vs GDD
OWNER: mechanics (design call: orchestrator)
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/FighterAI.cs:258-273 (cone enemyFire.fighterConeDegrees 40, pitch clamped to pitch.maxDegrees 15)
STEPS: Static + test. The player is 30 degrees above a fighter's flight line, inside its 40 degree cone. The shot flies at about 30 degrees and the nose at most 15. PlanePitchTests.Fighter_ShotAtConeEdge_StillLeavesAlongTheNose fails, even once P1-1 is fixed.
EXPECTED: GDD Flight attitude: "what you see is the way the shot flies".
ACTUAL: For any aim between 15 and 44 degrees (40 cone + 4 spread), the shot and nose disagree by up to about 29 degrees.
FIX HINT: clamp the shot to the nose range: pass Mathf.Min(_fire.fighterConeDegrees, maxDeg) as AimWithinCone's cone (keep or also narrow InFiringCone), so dir and nose match. This removes steep shots (balance: fewer fighter hits; check M3/M5 per T941 §4). Alternative: let fighters pitch further than the player. Either way, record it in ARCHITECTURE.
```

```
BUG: P2-1 Pitch eases at a fixed max-degrees-per-second; the nose lags the controls
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Core/PlanePitch.cs:19-22
STEPS: Static. Rate = maxDegrees*dt = 15 deg/s. A 0.3 s tap of W gives only 4.5 degrees. Full climb to full dive takes 2 s. Target = vy*maxDegrees saturates at 1 u/s (player speed 5), so it's on/off rather than proportional.
EXPECTED: GDD: "eased smoothly". Since the player now aims with pitch, the nose should follow W/S within about 0.2-0.3 s.
ACTUAL: About 1 s to reach full pitch. After a reversal, shots keep going the old way for up to a second (see the M5 dive trap in §3).
FIX HINT: a Balance key pitch.easeDegreesPerSecond (about 60-90) and a target of vy / referenceSpeed * max.
```

```
BUG: P2-2 Bomber pitch sign is not mirrored for its flipped sprite (latent)
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Biplanes/Assets/Biplanes/Scripts/Enemies/BomberAI.cs:214
STEPS: Static + test. transform.rotation = Euler(0,0,+pitch) while _sr.flipX = true, so a climbing bomber dips its nose. PlanePitchTests.Bomber_Climbing_DrawnNoseUp fails. In play it's latent: _velocity.y is non-zero only on the escort path, and no mission gives a bomber escortOf.
EXPECTED: GDD Flight attitude: nose up when climbing; the fighter does it right (sign = flipX ? -1 : 1).
ACTUAL: Inverted, as soon as any bomber climbs or dives.
```

```
BUG: P2-3 Pitch writes transform.rotation on the Rigidbody2D root every tick, not a visual child or MoveRotation
PRIORITY: P2 polish
OWNER: mechanics
WHERE: PlayerPlane.cs:479; FighterAI.cs:229,275; BomberAI.cs:214
STEPS: Static. Each tick it calls _rb.MovePosition(target), then sets transform.rotation directly. The spec hint asked for the visual child. ARCHITECTURE §3 moves bodies through Rigidbody2D. A transform write on a physics body forces a pose sync each step.
EXPECTED: Rotation through _rb.MoveRotation (as the T937 crash code does), or a child "Visual" with the sprite and colliders.
ACTUAL: Works on paper, and the hitboxes do rotate, but there's a risk of jitter or a swallowed MovePosition with interpolation. Watch it in the playtest (checklist P-3). If it's changed, expose the angle for the tests (§4).
```

```
BUG: P2-4 pitch.maxDegrees fallback differs between planes; a zero or missing key disables pitch on the player and fighters
PRIORITY: P2 polish
OWNER: mechanics
WHERE: PlayerPlane.cs:474; FighterAI.cs:222,272 (`?.maxDegrees ?? 15f`) vs BomberAI.cs:212 (`> 0f`)
STEPS: Static. A "pitch": {} block, or maxDegrees 0, gives max 0 for the player and fighters (no pitch, no error), but 15 for bombers. The spec: "fallback of 15 when the balance or the block is missing".
EXPECTED: One resolver, like DamageTakenData.PlaneCollisionOrDefault: PitchData.MaxDegreesOrDefault(GameBalance), value > 0 or 15.
ACTUAL: Three copies with two different rules.
```

```
BUG: P2-5 Pitched hitbox raises the ground-crash threshold by about 0.19
PRIORITY: P2 polish
OWNER: mechanics
WHERE: PlayerPlane.cs:301-305 (collider.bounds.min.y vs HeightAt), BoxCollider2D auto-sized 1.6x0.8
STEPS: Static. At 15 degrees the AABB half-height is 0.5*(1.6 sin15 + 0.8 cos15) = 0.59, against 0.40 level. A nose-up climb near the ground counts the tail's empty box corner, and a dive counts the nose corner.
EXPECTED: Crashes match the drawn plane (ARCHITECTURE §3 Physics: terrain compares against HeightAt).
ACTUAL: Pitched within 0.19 of the ground, you crash while the art is visibly clear. Accept it, or fit a smaller box or capsule to the fuselage.
```

```
BUG: P2-6 ARCHITECTURE doesn't describe pitch or nose fire, and §3 still says horizontal player bullets
PRIORITY: P2 polish
OWNER: orchestrator
WHERE: docs/ARCHITECTURE.md:150 (zeppelin line of fire), :177-178 (Bullet.Spawn ±1 only)
STEPS: Static. There's no entry for PlanePitch.Step, Balance pitch.maxDegrees, PlayerPlane.NoseDirection / FighterAI.NoseDirection, or Bullet.Spawn(Vector3, Quaternion, Vector2).
EXPECTED: Rule 4 (never invent APIs) needs the contract to list them.
ACTUAL: Missing. Also FighterAI.NoseDirection is set only at fire time (= the unclamped shot dir), so between shots it is stale. Define it as the current drawn nose.
```

Also noted, not filed separately:
- On a shot, the fighter snaps to the aim pitch (up to 30° in one tick) and then eases back at 15°/s. That's a visible jerk. Fold it into the P1-1 and P2-1 fixes.
- The fighter rotates even when the pool is exhausted and no shot leaves.

## 6. Manual playtest checklist (pitch)

Player:
- P-1. Hold W after takeoff. The nose rises smoothly and caps at about 15°. Release it and the nose eases back to level. Hold S and the nose dips. Check it in both facings: facing left, a climb must still point the drawn nose **up**.
- P-2. Fire while climbing, level and diving, facing right and facing left. Bullets leave from the drawn nose tip, fly the way the nose points, and the tracer is aligned. **The P0-1 check: facing left, bullets must fly left.**
- P-3. Climb and dive hard for 10 s. Movement stays smooth: no jitter, no stalls, and the plane doesn't stick in place (the P2-3 risk).
- P-4. Skim the ground pitched up and pitched down. Note the gap between the art and the terrain when the crash triggers (P2-5).
- P-5. Crash at full damage while pitched. The wreck tumbles from its current angle, falls, and explodes once on the ground (T937). Retry and Next start the new plane level.
- P-6. Ram a fighter while pitched. 50% damage, grace for 0.5 s, and the fighter dies (B025).
- P-7. Pause while pitched, then resume. The angle is kept and nothing jumps.

Enemies:
- E-1. Sine and dive fighters (M1, M3) visibly nose up when they rise and down when they fall. Escort fighters (M2, M5) follow with gentle pitch.
- E-2. A fighter shooting at a player above or below it: the nose turns **toward** the shot, not away (P1-1). It doesn't snap jarringly.
- E-3. A player 30° above or below a fighter's line: does the shot visibly leave off the nose (P1-2)?
- E-4. The M2 bomber flies level with no pitch. Its gunner fires in any direction. Its bombs drop as before.

Mission 5 turrets:
- M5-a. Facing right, fly level at world y about −0.2 to −0.4 and fire. The underside turret dies in 3 bullets.
- M5-b. Climb from below the underside turret while firing. The rising shots still reach it. This band is wider than when level.
- M5-c. Dive down into the underside band and fire at once. The gondolas absorb the shots until the nose levels out (about 1 s). Judge whether that feels unfair (P2-1).
- M5-d. Dive onto a top turret (world y about 1.8–2.1) while firing. The shots reach it.
- M5-e. After P0-1 is fixed: the same from the right side, facing left.
- M5-f. Kill all 3 turrets, then the hull. Win once.
