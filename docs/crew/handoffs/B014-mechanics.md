TASK: B014
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Resources/Balance.json
- Biplanes/Assets/Biplanes/Scripts/Core/GameBalance.cs
- Biplanes/Assets/Biplanes/Scripts/Core/DamageSource.cs
- Biplanes/Assets/Biplanes/Scripts/Core/Health.cs
- Biplanes/Assets/Biplanes/Scripts/Weapons/FlakShell.cs
- Biplanes/Assets/Biplanes/Scripts/Enemies/ZeppelinAI.cs
- Biplanes/Assets/Biplanes/Scripts/Enemies/ZeppelinTurret.cs
- docs/crew/handoffs/B014-mechanics.md
SUMMARY:
Balance gets enemySpeed.zeppelin = 1.0 (EnemySpeedData.zeppelin). ZeppelinAI reads it in Awake into _speed, falling back to the named constant FallbackSpeed = 1.0 when it is missing or <= 0 (the BomberAI pattern). The ZeppelinSpeed constant is gone.
DamageSource gets ZeppelinTurret, appended after Heal so existing ordinals don't move. Health.GetDamageMultiplier maps it to damageTaken.zeppelinTurret (12). Every other mapping is unchanged.
FlakShell adds Spawn(Vector3, Vector2 direction, DamageSource source): exact direction, no forced climb, zero direction = straight down. The shell stores the source per spawn and reports it in OnHit. The old 2-argument Spawn still climbs and uses Flak, so AAAGun is untouched. ZeppelinTurret.Fire calls the new overload with DamageSource.ZeppelinTurret and no longer overwrites the velocity. Aim and cadence are unchanged.

DETAILS:
- Switches on DamageSource checked: Health (player mode: new case; enemy mode: unchanged, but a non-Collision source counts as points, which ZeppelinTurret never reaches because FlakShell.OnHit hits Team.Player only). AAAGun, BarrageBalloon, ZeppelinAI and ZeppelinTurret all ignore it through their default branches. EnemyBase accepts Bomb only. Nothing indexes an array by DamageSource.
- EditMode tests: the FlakShell tests call only the 2-argument Spawn, ShellSpeed, Damage, Team and the private statics _pool/_root. All are unchanged, and those tests still expect Flak and the forced climb, which the 2-argument path keeps.
- A pooled shell's source is reset on every Spawn, so an AAA shell reused after a turret shot reports Flak again.

VERIFY:
- `python tools/compile_check.py`: exit 0 ("PASS Biplanes (+6 new file(s) not yet in the csproj)", "PASS Biplanes.Editor"). Unity batch compile and tests weren't run because the editor is open.
- EditMode (in Unity): the existing FlakShell_* and AAAGun_* tests must still pass.
- Suggested new tests (QA):
  - FlakShell.Spawn(0, (1,-1), DamageSource.ZeppelinTurret) gives a velocity of magnitude ShellSpeed with y < 0.
  - A hit on a player-mode Health adds exactly damageTaken.zeppelinTurret.
  - A shell then respawned with the 2-argument Spawn adds damageTaken.flak.
  - The zero direction overload flies (0, -ShellSpeed).
- Play, mission 5 (after T935): the zeppelin drifts left at 1.0 u/s. Turret shells fly down at the player, and each hit adds 12% (AAA shells still add 15%).

REQUESTS:
- mechanics/orchestrator (possible future Balance fields; kept as named constants per the brief):
  - ZeppelinTurret: FireCooldown 2.5 s, FireRangeMargin 2 (also hard-coded in AAAGun).
  - ZeppelinAI: TurretFirstShotStagger 0.8 s, DespawnBehindMargin 20, ScreenTopClearance 1.3.
  - FlakShell: ShellSpeed 6, ShellLifetime 3 s.
  - The ZeppelinAI death-explosion offsets and the hitbox geometry are sprite data, not tunables.
- qa: add the EditMode tests suggested under VERIFY.
- orchestrator: ARCHITECTURE.md changes:
  - §3 Core interfaces: `public enum DamageSource { Bullet, Bomb, Flak, Collision, Heal, ZeppelinTurret }`. The list currently omits Heal, which already exists; ZeppelinTurret is appended last.
  - §3 Enemies, zeppelin sentence: replace "Turrets fire aimed flak at the player while near the screen." with "Turrets fire aimed `FlakShell`s at the player along the exact aim, downward included, while near the screen; hits use `DamageSource.ZeppelinTurret`, scaled by `damageTaken.zeppelinTurret`. The hull flies left at `enemySpeed.zeppelin`."
  - §3 Weapons, after the "Enemy projectiles apply 1 hit unit..." sentence, add: "`FlakShell.Spawn(position, dir)` (AAA) always climbs and hits with `DamageSource.Flak` (`damageTaken.flak`). `FlakShell.Spawn(position, dir, source)` fires along `dir` exactly and hits with `source`. The player's Health maps `ZeppelinTurret` to `damageTaken.zeppelinTurret`."
  - §4 Balance.json: `"enemySpeed": { "fighter": 3.0, "bomber": 1.5, "zeppelin": 1.0 }`. In the prose, change "how fast each enemy plane flies left" to "how fast each enemy flyer (fighter, bomber, zeppelin) flies left".
