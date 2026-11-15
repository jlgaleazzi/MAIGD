# B012 verify (T931): bomber hitboxes, sprite flip, recycled-projectile guard

**Verdict: PASS** (0 P0, 0 P1, 1 P2)

Scope: `Scripts/Enemies/BomberAI.cs` (B012 hunk; the EnemyBomb hunk was verified earlier),
`Scripts/Weapons/Projectile.cs`, sprite `Resources/Sprites/enemies/enemy_bomber.png` (+ .meta,
read only), against handoffs `B012-mechanics.md` and `T914-art.md`.

## Checks run

| Check | Result |
|---|---|
| `python tools/compile_check.py` | PASS Biplanes, PASS Biplanes.Editor, exit 0 |
| Tests scratch build: runtime DLL built from source, then all 5 `Tests/EditMode/*.cs` built against it (generated test csproj, ProjectReferences swapped for ScriptAssemblies DLLs, v4.7.2), everything in the session scratch dir | PASS, 0 errors. Nothing written into the project except the new test file |
| `bash tools/unity.sh test` | Not run: the Unity editor has the project open. Run the EditMode suite from the Test Runner |
| Static rules (BomberAI.cs, Projectile.cs, new test) | No `.velocity`, no `UnityEngine.Input`, namespaces match folders, one public class per file. No .prefab/.unity/.asset/.meta written |
| Sprite import (.meta) | `spritePixelsToUnits: 100`, `alignment: 0` (Center), `spritePivot: {0.5, 0.5}`. 320x140 px = 3.2 x 1.4 units, as the hitbox maths assumes |

## Hitboxes against the measured alpha mask (Pillow, alpha > 127, image mirrored for flipX)

Opaque bbox of the unmirrored PNG: x 7..311, y 2..137 (19 361 px). Box rectangles converted
with x_px = 160 + x*100, y_px = 70 - y*100 on the mirrored image:

| Box (as applied) | Mirrored px | Opaque inside | Weakest row / column |
|---|---|---|---|
| Fuselage 2.64x0.40 at (-0.12,-0.08) | x 16..280, y 58..98 | **93.3%** | top row 0.64. One 12 px empty strip on the top edge at x 228..244 (the fuselage dips just ahead of the tail), 2-3 px deep |
| Wing block 0.70x1.02 at (-0.69,+0.13) | x 56..126, y 6..108 | **93.2%** | right column 0.63 (strut edge) |
| Tail fin 0.46x0.50 at (+1.27,+0.09) | x 264..310, y 36..86 | **94.4%** | bottom row 0.65 |

Together the boxes cover 79.7% of the opaque pixels. Largest uncovered opaque areas:

| Area | Mirrored px | Size | Consequence |
|---|---|---|---|
| Propeller disc at the nose | x 8..29, y 17..124 | 1156 px | Horizontal shots at y 17..108 still meet the wing box just behind it; only its lower part (y 108..124) is a pass-through |
| Wheel and undercarriage | x 64..119, y 108..137 | 885 px | Pass-through band, see the P2 bug |
| Top of the tail fin | x 275..312, y 24..36 | 414 px | Shots at that height still meet the wing box; rams grazing only the fin tip miss |
| Gunner's gun, cockpit area | x 142..177, y 20..57 | 412 px | Thin gun; shots still meet the wing box. Deliberate (handoff) |
| Upper-wing leading edge | x 38..55, y 28..57 | 298 px | Shots still meet the wing box behind it |

Debug overlay (boxes in red over the mirrored sprite):
`C:\Users\JUANLU~1\AppData\Local\Temp\claude\0528dffa73e43d53\ee40c63b-e029-45ba-8e86-0c38e6405307\scratchpad\b012_overlay.png`

**Mirroring:** the unflipped constants (fuselage +0.12, wing +0.69, tail -1.27) match the
right-facing PNG: the propeller is at x ~290..312 and the fin at x 7..70. `AddHitboxes` multiplies
every x by -1 because `FlipsToFlyLeft` is true, and the same constant drives `_sr.flipX`, so the
sprite and the boxes can't diverge. In the mirrored image the nose and wing block are on the left
(x 8..126) and the tail is on the right (x 264..312). The bomber flies -x, so it flies nose first.

## Flip

- ART_STYLE: "Every aircraft faces right (+x). Code flips enemies that fly left." T914 mirrored
  the blueprint to face right. The bomber moves at `-_speed`, so `flipX = true` is correct.
- FighterAI does the same thing: `_sr.flipX = true; _sr.sortingOrder = 5;` (FighterAI.cs:96-97).
- Nothing else depended on the unflipped sprite:
  - The bomb drop is at `transform.position` (centre).
  - The bomber has no gunner or muzzle code.
  - Escorts (FighterAI and BomberAI escort mode) lerp to the target's `transform.position`, with no offsets.
  - The death explosion is at `transform.position`.
  - BomberAI has no smoke tint.
  - `_collider` is only cached, never used.
  - `MissionRunner.PlaceOnGround` reads `Collider2D.bounds`, but only on the player.

## Recycled-projectile guard (`Projectile.OnTriggerEnter2D`)

- `Recycle()` goes to `ObjectPool.Push`, which calls `SetActive(false)`, so `activeInHierarchy` is false for every later callback in the same step. The second box's callback is dropped.
- **Spawn and hit in the same step:** `ObjectPool.Pop` calls `SetActive(true)` before `Init`. Pool roots are active (`Pool<T>` root; `FlakShellPool`/`EnemyBombPool` scene roots), so a fresh projectile passes the guard.
- **Bullet:**
  - A hit recycles the bullet, so it hits once.
  - A Neutral (balloon) hit returns without recycling, so the bullet stays active and can still hit an enemy behind the balloon (B011 kept).
- **Bomb:**
  - `OnHit` sets `_directHit`, damages, explodes and recycles. Later boxes are ignored.
  - The ground burst runs in `Update`, not in the trigger path, so the guard doesn't touch it.
  - A recycled bomb doesn't burst.
  - `_directHit` is still cleared in `Spawn`.
  - A Neutral hit doesn't recycle.
- **FlakShell, EnemyBomb:** the bomber is Team.Enemy, so the base team check drops their hits before `OnHit` (unchanged). Player hits recycle, and later callbacks are dropped.
- **Ramming:**
  - Every bomber box that overlaps the player triggers its own `PlayerPlane.OnTriggerEnter2D` call.
  - The first call applies collision damage to both planes and calls `TriggerCrash`.
  - The later calls return on `_hasCrashed`. `TriggerCrash` is also guarded, and the bomber is already `IsDead`.
  - The result is one crash and one enemy kill.

## Draw order

The bomber uses `sortingOrder` 5, which matches ARCHITECTURE §3 Draw order (enemies = 5). It draws
above terrain (0, `TerrainStrip.SortingOrder`) and backgrounds (-30/-20/-10), and below FX (10),
projectiles (15) and the player (20). This matches FighterAI and AAAGun (5).

## EditMode tests (compiled, not run)

New file `Biplanes/Assets/Biplanes/Tests/EditMode/BomberHitboxTests.cs`, 6 tests. Unity
still has to generate its `.meta`.
- `BomberAI_FlipsToFlyLeft_IsTrue`
- `BomberAI_Spawn_HasThreeMirroredTriggerHitboxes`: 3 trigger BoxCollider2D on the root, none on children. Sizes and mirrored offsets are exact: wing x < 0, tail x > 0. Every box lies inside 3.2 x 1.4 units.
- `BomberAI_AwakeFallback_AddsTheSameHitboxes`: a bare BomberAI gets the same 3 boxes after Awake.
- `Bullet_Inactive_IgnoresOnTriggerEnter2D`: an inactive bullet deals 0. As a control, the same bullet deals one hit once it is active.
- `PooledBullet_OverlappingTwoBomberBoxes_DamagesOnce`: a bullet spawned from the pool hits the fuselage (one bulletDamage), is recycled, then hits the wing box and deals nothing more.
- `PooledBomb_OverlappingTwoBomberBoxes_DamagesOnce_AndRecordsDirectHit`: one bomb hit, `_directHit` = the bomber, recycled, no second hit from the tail box.

## Bugs

```
BUG: Bomber wheel/undercarriage band (and the propeller's lower tip) has no hitbox
PRIORITY: P2 polish
OWNER: mechanics
WHERE: Scripts/Enemies/BomberAI.cs:261-271 (hitbox constants); sprite px (unmirrored) x 201..256, y 108..137
STEPS: Static evidence (alpha mask). Below the wing box's bottom edge (local y -0.38) the wheel,
  gear legs and the propeller's lower tip are solid ink (885 + ~200 px; 10x10 blocks 100% filled
  at mirrored x 65, y 110). No box covers local y -0.38..-0.67, so a horizontal bullet, or a
  player plane grazing the undercarriage, passes through visible ink.
  The mechanics handoff calls this deliberate.
EXPECTED: GDD/ARCHITECTURE §3 Physics: hittable objects have a collider that covers what the
  player sees. A shot that visibly hits the plane should count, within a few px of inset.
ACTUAL: About a 0.29-unit-tall band under the bomber (about 0.55 units wide at the wheel)
  never registers hits or rams. Optional fix: a 4th box of about 0.50x0.26 at mirrored
  local (-0.69, -0.52), or extend the wing box down to about y 132 px.
  Low impact: bullets travel horizontally and the band is thin.
```

Not re-filed (known, tracked): BomberAI.targetX has no setter, so bombers never bomb in real
missions (T047).

## Manual playtest (bomber, mission 1, Gizmos on)

1. Select the `bomber` object in the Hierarchy. Check that the sprite faces left (propeller and wings on the left, tail fin on the right) and that the three green boxes sit inside the ink: the long fuselage band, the tall box over the wings, and the small box on the fin.
2. Fly level with the bomber and fire through the empty corner above the rear fuselage, between the gunner's gun and the tail fin, at about the fin-top height but behind the wings. The bullets pass the gap and then hit the wing block (one damage step per bullet). Fire at the fuselage and the fin: each bullet registers once, with no double hit where the fuselage and wing boxes overlap. Count shots to kill: `enemyHealth.bomber / bulletDamage` (rounded up).
3. Fire level with the wheel, below the lower wing. Bullets pass through (known P2).
4. Drop a bomb straight onto the bomber's wings or fuselage. It explodes on contact with one bomb hit (no extra damage from the ground burst later).
5. Ram the bomber head-on, and once into the tail fin. The player crashes once (one Lost screen, no double explosion or log) and the bomber is destroyed.
6. Check draw order over the terrain and all three background layers: the bomber is in front of the scenery, explosions and bullets draw over it, and the player draws over it when they cross.
