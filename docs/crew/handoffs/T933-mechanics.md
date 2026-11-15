TASK: T933
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Enemies/ZeppelinAI.cs (new)
- Biplanes/Assets/Biplanes/Scripts/Enemies/ZeppelinTurret.cs (new)
- docs/crew/handoffs/T933-mechanics.md
SUMMARY:
ZeppelinAI is a self-contained IDamageable hull with no Health component, so the shield can't be bypassed. It flies left with the sprite mirrored by flipX and its 5 trigger boxes mirrored to match, and it has 3 ZeppelinTurret children that are built only in Spawn (2 on top, 1 underneath). While any turret is alive the hull absorbs every hit (the projectile is used up, no damage), unless hullVulnerableEarly is set. On death it raises EntityDestroyed("zeppelin", Enemy) once, plays 3 explosions and destroys itself with its turrets.
Each turret takes enemyHealth.zeppelinTurret from bullets and bombs, fires an aimed FlakShell every 2.5 s only within 2 units of the screen edge, and raises EntityDestroyed("zeppelinTurret", Enemy) once.
Ramming the hull or a turret crashes the player. A rammed turret is destroyed; the hull is not.

FACTORY (for T935, MissionRunner "zeppelin" case):
  public static ZeppelinAI Spawn(Vector3 position, string id)   // id = entry.id, may be null/empty
  public void Init(PlayerPlane player)                          // optional; the hull otherwise finds the player once in Start
  Suggested case:
    case "zeppelin": { var z = ZeppelinAI.Spawn(position, entry.id); if (_playerPlane != null) z.Init(_playerPlane); entityGO = z.gameObject; break; }
  Use position = (posX, spawnYBase) as for ground entries. Don't add it to the plane random-placement path, since `y` matters.
  The hull clamps its centre to at most cameraTop - 1.3 in Start. Mission 5's y 5.0 becomes 3.7 with camera size 5 at y 0.
  Public read-only state: Id, TurretsRemaining, IsShielded, CurrentDamagePercent, IsDead, ScoreKey = "zeppelin" (ZeppelinTurret.ScoreKey = "zeppelinTurret").
  The GameObject is named "zeppelin [<id>]", or "zeppelin" when there is no id. That way FighterAI's escortOf name-contains lookup finds it.

DETAILS:
- Speed: Balance has no enemySpeed.zeppelin, so ZeppelinAI.ZeppelinSpeed = 1.0 is a named constant (slower than the bomber's 1.5).
- Turret flak damage: FlakShell applies DamageSource.Flak, so the player's Health scales it by damageTaken.flak (15). damageTaken.zeppelinTurret (12) is NOT used, because neither DamageSource nor FlakShell has a path for it.
- FlakShell.Spawn forces shells upward (dir.y >= 0.5) because it was built for ground AAA. The turret overwrites the shell's Rigidbody2D.linearVelocity right after Spawn with the real aim direction × FlakShell.ShellSpeed, so it can fire down at the player. The aim uses the same intercept solve as AAAGun.PredictPosition.
- Despawn: the hull is destroyed silently (no event, no score) once it is more than 20 units past the left screen edge. No other flyer despawns today, so this adds a new behaviour.
- Turrets are child colliders on the hull's kinematic Rigidbody2D (useFullKinematicContacts = true), so a projectile resolves to the turret it touched. Turret boxes don't overlap hull boxes.
- Turret positions, local and art-facing-right (x is negated in game): (1.90, 1.00) and (-1.30, 1.00) on top; (-0.65, -0.82) underneath with flipY, between gondolas 1 and 2. They come from zeppelin_hull.png (900×220 px, 100 ppu) and zeppelin_turret.png (60×40 px).
- The other named constants with no Balance field: ZeppelinTurret.FireCooldown 2.5 s, FireRangeMargin 2 (same as AAAGun), TurretFirstShotStagger 0.8 s, DespawnBehindMargin 20, ScreenTopClearance 1.3, and the death explosion offsets (-2.5, 0, 2.5).

VERIFY:
- `python tools/compile_check.py`: exit 0 ("PASS Biplanes (+6 new file(s) not yet in the csproj)", "PASS Biplanes.Editor"). Unity batch compile wasn't run because the editor is open.
- In play, after T935, mission 5:
  - Hull bullets do nothing while turrets live.
  - Each turret dies to 3 bullets (75 hp / 25) and awards +200.
  - After the last turret, the hull dies to 40 bullets (1000 / 25) or 10 bombs, awards +2500 once, and shows 3 explosions.
  - Turrets fire flak down at the player while on screen.
  - Ramming the hull or a turret crashes the player.

REQUESTS:
- mechanics (T935): add the "zeppelin" spawn case to MissionRunner using the factory above. It also needs id-aware destroyById and failAtX handling. GameEvents.EntityDestroyed carries only (type, team), so ZeppelinAI raises "zeppelin" without the id. ObjectiveTracker.destroyById and CheckFailAtX are still stubs. Suggest tracking the spawned ZeppelinAI by entry.id: destroyed means its GameObject is gone after an EntityDestroyed("zeppelin"); fail means camera x >= failAtX while it is alive.
- mechanics: add `enemySpeed.zeppelin` (suggest 1.0) to Balance.json and EnemySpeedData, then replace ZeppelinAI.ZeppelinSpeed with it.
- mechanics: give FlakShell a Spawn overload that doesn't force the climb, plus a way to use damageTaken.zeppelinTurret (for example a DamageSource.ZeppelinTurret or a damage scale argument). That would remove the velocity override in ZeppelinTurret.Fire.
- mechanics: the escort fighters in mission 5 spawn at atX 40, before the zeppelin at atX 60. FighterAI only looks up its escort target in Start, so they will fall back to straight flight. Either re-resolve the escort later or have mission-design spawn the escorts at or after 60.
- orchestrator: in ARCHITECTURE §3 Enemies, document:
  - ZeppelinAI.Spawn(Vector3, string id) and Init(PlayerPlane).
  - 3 turrets; a shielded hull absorbs hits.
  - Self-contained IDamageable for both hull and turret (like AAAGun).
  - Turret flak uses damageTaken.flak.
  - The hull is removed 20 units behind the left screen edge without score.
  - Ramming the hull or a turret crashes the player.
