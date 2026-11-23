TASK: B077
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Enemies/EnemyHealthBar.cs (pixel-sized height, hit flash, min first step, Tick(dt))
- Biplanes/Assets/Biplanes/Scripts/Enemies/ZeppelinAI.cs (OverallFraction / OverallHealthFraction, bar rebound to overall)
- Biplanes/Assets/Biplanes/Tests/EditMode/EnemyHealthBarTests.cs (updated for B077: pixel height, overall zeppelin)
- Biplanes/Assets/Biplanes/Tests/EditMode/EnemyHealthBarLiveTests.cs (new, 8 tests: real Bullet path + helpers)
- docs/crew/handoffs/B077-mechanics.md
BomberAI.cs needed no change: it still passes BomberHeight, which is now the no-camera fallback.
SUMMARY:
Root cause, bomber: I found no binding or refresh defect.
- The hitbox colliders sit on the bomber root, so `GetComponentInParent<IDamageable>()` resolves the same Health that `EnemyHealthBar.Attach` binds.
- Play mode runs Health.Awake inside AddComponent with the TeamTag already set (Team = Enemy), and runs BomberAI.Awake before Attach. Neither rebinds or resets anything.
- Nothing else touches the bar. No code calls Layout or Attach again, re-enables it, scales it or masks it. All draw order is on the default sorting layer, at 11/12/13 above the bomber's 5.
The failure is visibility.
- One bullet is 2.5% of a 1.6-unit bar. That is about 4 px at 1080p and under 2 px in a small Game view.
- A hit gave no other feedback, and the frame and fill are both light.
- The bar height was in world units (0.12 inner + 0.03 frames). It came out 19 px tall at 1080p and about 38 px at 4K, which is why it looked thick.
Root cause, zeppelin: this is a real defect. The bar showed hull damage only, and the hull absorbs every hit while turrets live (IsShielded). So the bar never moved during the whole turret phase.
Fixes:
1. Zeppelin overall progress. The bar is bound to `(1 - OverallHealthFraction) * 100`. The pure helper is `ZeppelinAI.OverallFraction(turretRemainingHp, turretMaxHp, hullRemainingHp, hullMaxHp)` = Clamp01((tR + hR) / (tM + hM)). Negative inputs count as 0, and a total max <= 0 gives 1.
   - tR is the sum over living turrets of zeppelinTurret * (1 - dmg/100). Dead or removed turrets count 0.
   - tM = turretsSpawned (3) * enemyHealth.zeppelinTurret (75).
   - hR = zeppelinHull * (1 - hullDmg/100), and hM = zeppelinHull (1000).
   - Total 1225: a turret bullet moves the bar about 2.0%, a dead turret about 6.1%, and the hull is the last 81.6%.
2. Thinner bar at a fixed pixel size. The inner height is `InnerHeightPx` = 10 px and the frame is `FramePx` = 2 px per side, so the bar is 14 px tall at any resolution.
   - Conversion: px per unit = `cam.pixelHeight / (2 * orthographicSize)` (`PixelsPerUnit`), then `PixelsToWorld(px, ppu, fallback)`.
   - It is recomputed in every Tick, and the layout is redone only when ppu changes.
   - Without an orthographic camera it falls back to 0.09 u inner (BomberHeight / ZeppelinHeight) and 0.02 u frame (OutlineThickness).
   - The brief asked for 14 px inner plus 2 px frames (18 px total). That is the same as the old bar at 1080p, so I chose 14 px total to make it visibly thinner. It is one constant if you prefer 14 inner.
   - Widths are unchanged: 1.6 / 3.0 u.
3. Visible change on each hit:
   - The frame turns ink #E8F1FA for `FlashSeconds` = 0.15 s whenever the fill drops. It stays white only.
   - `VisibleFillWidth` draws any damaged bar at least `MinStepPx` = 2 px short of full, so even a sub-pixel first hit shows. Normal 2.5% steps are drawn exactly.
   - The pixel sprite is now created with SpriteMeshType.FullRect explicitly.
`Refresh()` = `Tick(0)` (no flash decay). LateUpdate calls `Tick(Time.deltaTime)`.
VERIFY:
- `python tools/compile_check.py`: exit 0 (PASS Biplanes, PASS Biplanes.Editor).
- I also built the EditMode test assembly with dotnet, which passed. I used a scratch copy of Biplanes.Tests.EditMode.csproj against the freshly built runtime DLL, with TargetFrameworkVersion v4.7.2. The new EnemyHealthBarLiveTests.cs is included.
- I did not run Unity batch or the Test Runner, as the brief said.
- To run: Test Runner > EditMode > EnemyHealthBarTests and EnemyHealthBarLiveTests, plus ZeppelinAndBaseTests, BomberHitboxTests, BomberDamageTests and AudioFixesQaTests (they spawn the zeppelin).
- The live test `Bomber_RealBulletHits_MoveTheBarEveryHit` builds the bomber with BomberAI.Spawn and a real Bullet (Awake run). It calls Projectile.OnTriggerEnter2D with each real hitbox collider for 12 hits. After each hit it asserts:
  - the fraction is 1 - 0.025*i;
  - the fill width matches VisibleFillWidth and strictly shrinks;
  - the fill is left-anchored and all three renderers are on;
  - the flash is on.
  It also asserts the hitbox resolves to the bar's Health and checks the sorting order and sorting layer.
- Playtest:
  - Mission 2 bomber: the bar is about 14 px tall, and every hit shrinks it with a white frame blink.
  - Mission 5 zeppelin: the bar drops about 2% per turret bullet and about 6% per turret kill. Hull hits are absorbed until all 3 turrets are down, then it drains to empty.
  - Check at two window sizes that the thickness stays the same.
REQUESTS: orchestrator: In ARCHITECTURE.md "Enemy damage bar (B076)", make these changes.
- Replace the sizes with: "Width 1.6 u (bomber) / 3.0 u (zeppelin). The height is pixel-based (B077): inner `InnerHeightPx` 10 px plus a `FramePx` 2 px pencil frame, 14 px total on screen. It is converted with ppu = camera.pixelHeight / (2 * orthographicSize) and re-laid out when ppu changes. Without a camera it falls back to 0.09 u inner and 0.02 u frame."
- Add: "Each drop in the fill flashes the frame ink for 0.15 s. A damaged bar is drawn at least 2 px short of full."
- Replace "ZeppelinAI.Spawn ... bound to the HULL damage, so turrets don't count" with: "The zeppelin bar shows OVERALL progress, `ZeppelinAI.OverallFraction` = (sum of living turrets' remaining HP + remaining hull HP) / (3 x enemyHealth.zeppelinTurret + enemyHealth.zeppelinHull), 1225 with the current Balance. It moves during the shielded turret phase."
