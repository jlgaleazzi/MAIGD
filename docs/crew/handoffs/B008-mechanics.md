TASK: B007 + B008 + B009
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Weapons/Bomb.cs
- Biplanes/Assets/Biplanes/Scripts/Weapons/FlakShell.cs
- Biplanes/Assets/Biplanes/Scripts/Weapons/EnemyBomb.cs
- Biplanes/Assets/Biplanes/Scripts/Enemies/AAAGun.cs
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs
- Biplanes/Assets/Biplanes/Scripts/Core/Health.cs
- docs/crew/handoffs/B008-mechanics.md
SUMMARY:
- B008: when Bomb reaches the terrain, it runs `BurstDamage` before it recycles. This is a `Physics2D.OverlapCircle(point, burstRadius = 0.5, ContactFilter2D{useTriggers}, static Collider2D[16])` (non-allocating). It applies `damage` with DamageSource.Bomb once to each live Team.Enemy IDamageable. It skips the player and Neutral objects (HealthBalloon), skips a target the bomb already hit through its trigger (`_directHit`, cleared in Spawn), and dedupes targets that have several colliders. AAA still filters to Bomb (and Collision) only.
- B009a: removed `ScoreKeeper.AddPoints` from AAAGun.HandleDeath. Score path: OnDamageTaken raises `EntityDestroyed("aaa", Enemy)`, and ScoreKeeper.OnEntityDestroyed adds `score.aaa` (150) once. B009b: new `public Vector2 PlayerPlane.Velocity => _velocity` (the field is not renamed). AAAGun.PredictPosition uses `PlayerRef.Velocity`, and the unused `_playerRb` cache is removed. B009c: FlakShell/EnemyBomb.EnsurePool call `_pool?.Dispose()` before they rebuild.
- B007: Health.GetDamageMultiplier maps DamageSource.Bomb to `damageTaken.bomberGunner` (10). An enemy bomb now does 10% to the player. Balance.json is unchanged.
VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes (+1 new file not yet in the csproj, EnemyBomb.cs from B005), PASS Biplanes.Editor, exit 0. No members read by Tests/EditMode by reflection were renamed (`_velocity`, `_pool`, `_root`, `_groundY` are kept).
- Static: `grep -n "AddPoints" Scripts/Enemies/AAAGun.cs` finds only a comment. `grep -n "_playerRb" Scripts/Enemies/AAAGun.cs` finds nothing.
- Suggested EditMode test: spawn an AAAGun at y = 0 (with TerrainStrip at 0, or call the burst through a bomb placed just below the ground) and run a Bomb Update below the ground next to the gun. Expect the gun to be dead. With a HealthBalloon and a Team.Player Health in the radius, expect neither to change.
- Playtest (mission 3): bomb AAA 10 times from high altitude. Every bomb that bursts on a gun destroys it, and each kill adds exactly 150. Flak bursts now lead a scrolling plane (they aim ahead of it, not behind). Retry several times, then quit: no errors.
REQUESTS:
- orchestrator: ARCHITECTURE.md §3 Weapons, Bomb: add "On reaching the terrain, a Bomb damages every live Team.Enemy IDamageable within 0.5 units (OverlapCircle) before it recycles, once per target and never the target it already hit directly, so fast bombs can't tunnel past ground targets."
- orchestrator: ARCHITECTURE.md §3 Player: add "`PlayerPlane.Velocity` (read-only) is the world velocity over the last physics tick. Rigidbody2D.linearVelocity is always zero for the MovePosition-driven body." §3 Enemies, AAAGun: the predicted position uses PlayerPlane.Velocity. §4 damageTaken: player bomb hits use bomberGunner.
- mission-design (optional): the burst radius is a Bomb SerializeField (0.5), because Balance.json has no field for it. Add `player.bombBurstRadius` only if tuning is wanted.
