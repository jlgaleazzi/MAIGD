TASK: B037
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Enemies/AAAGun.cs
- Biplanes/Assets/Biplanes/Tests/EditMode/AAAGunBulletTests.cs (new)
- docs/crew/handoffs/B037-mechanics.md
SUMMARY:
- AAAGun.ApplyDamage now handles DamageSource.Bullet. It adds `damage / MaxHitPoints() * 100` percent, where MaxHitPoints is Balance `enemyHealth.aaa`, or 125 when Balance or the value is missing or <= 0. With 125 / 25, the 5th bullet kills. A float guard snaps >= 99.999 to 100. Hits on a dead gun return early. Any Bomb hit (direct or ground burst; both call ApplyDamage(…, Bomb)) sets damage to 100 regardless of its value. Collision is unchanged (percent * 100). Flak and other sources are still ignored. GetMultiplier was removed.
- The death path is the same: OnDamageTaken raises EntityDestroyed("aaa", Enemy) once on the killing hit, ScoreKeeper scores it, and LateUpdate → HandleDeath plays the one explosion. Smoke tint still reads _currentDamagePercent, so bullets now drive it too: 4 bullets = 80%.
- Non-fatal bullet hits play AudioManager.GetOrNull() HitEnemy, at most one per Health.HitCueMinInterval (`_lastHitCueTime`, same pattern as ZeppelinTurret). Ignored, fatal and dead-gun hits play no tick.
VERIFY:
- `python tools/compile_check.py` → PASS Biplanes (+1 new file(s) not yet in the csproj), PASS Biplanes.Editor, exit 0. `tools/unity.sh compile` wasn't run because Unity has the project open.
- Tests compiled in a scratch build (B018 method), not run. The runtime DLL was built with compile_check's helpers. The temp test csproj was retargeted to v4.7.2 with all 25 Tests/EditMode/*.cs files. Result: 0 errors. No temp files were left in the project.
- Run with Unity closed: `bash tools/unity.sh test`, AAAGunBulletTests. These tests swap GameBalance._instance for a Load() with aaa 125 / bulletDamage 25 and restore it in TearDown:
  - Bullets_FiveHitsOf25_Vs125_KillExactlyOnFifth
  - Bullets_AtDeadGun_DoNothing
  - Bullets_MissingBalanceValue_FallsBackTo125
  - Bullets_UseBalanceHitPoints
  - Bullets_FourHits_ReachSmokeRange
  - Bomb_FreshGun_AnyDamageValue_KillsOutright
  - Bomb_AfterBullets_KillsOutright_OnceOnly
  - Bomb_GroundBurst_KillsGun
  - Flak_IsIgnored
  - Collision_StillFatal
  - HitEnemy_NonFatalBullet_Throttled_IgnoredHitsSilent_FatalNoTick
  - BulletKill_HandleDeath_PlaysOneExplosion
- Play check: in M3, 5 machine-gun hits destroy an AAA (score.aaa once, one explosion), and one bomb still destroys a fresh AAA.
REQUESTS:
- qa: EnemyWeaponsAndPoolTests.AAAGun_TriggerCollider_TakesBombDamageOnly now fails. It asserts `ApplyDamage(25f, Bullet)` leaves CurrentDamagePercent at 0 ("bullets and flak must not damage AAA"). Change it to expect 25 / enemyHealth.aaa * 100 (20% at 125), and keep the flak-ignored and one-bomb-kills asserts. Other AAAGun tests should still pass unchanged: AudioFixesQaTests AAAGun_BombDirectKill_PlaysExactlyOneExplosion and AAAGun_HandleDeath_WithoutFxManager_CreatesNone_InEditMode, and EnemyWeaponsAndPoolTests Bomb_GroundBurst_KillsAdjacentAAA_*, AAAGun_BombKill_ScoresAaaExactlyOnce, Bomb_Update_BelowGround_BurstsOnSurfaceAtCrossingX_KillsAaaThere and the PredictPosition tests.
- mechanics (B036 owner): Balance.json still has `enemyHealth.aaa: 100.0`, so until B036 sets 125 a gun dies to 4 bullets.
- orchestrator: ARCHITECTURE §3 AAAGun already describes B037. Optionally add "fallback 125 hp when enemyHealth.aaa is missing; collision still fatal".
