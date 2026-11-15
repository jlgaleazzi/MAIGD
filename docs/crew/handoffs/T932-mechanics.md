TASK: T932
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Enemies/EnemyBase.cs (new)
- Biplanes/Assets/Biplanes/Scripts/Enemies/PlayerHQ.cs (new)
- Biplanes/Assets/Biplanes/Scripts/Enemies/HealthBalloon.cs (edit: Heal is now a no-op)
SUMMARY: EnemyBase is a self-contained IDamageable, Team.Enemy, GameObject named "enemyBase". It takes Bomb damage only, as a percent of Balance enemyHealth.enemyBase. On death it raises EntityDestroyed("enemyBase", Enemy) exactly once (a _dead guard; ScoreKeeper awards score.enemyBase), then the static EnemyBase.Destroyed event, then an FxManager explosion and Destroy.
PlayerHQ is a TeamTag Neutral ground structure that is not an IDamageable, so projectiles and bomb bursts ignore it and it is never scored. Both use a trigger BoxCollider2D fitted to the sprite, a Kinematic RB and sortingOrder 5, sit their bottom edge on the given ground point, and crash the player on contact (ApplyDamage(1, Collision), like a building). HealthBalloon.Heal no longer throws.
VERIFY: `python tools/compile_check.py`: exit 0 (PASS Biplanes, PASS Biplanes.Editor). Play test after T935 wires the spawns: mission 4. Bullets bounce off the base, 1 bomb (bombDamage 100 vs enemyHealth.enemyBase 100) destroys it with an explosion, and the score goes up by 1000 once. Mission 5: the HQ shows at x=650, and bullets and bombs pass through it.

FACTORY SIGNATURES (for T935, MissionRunner.SpawnEntities):
- `public static EnemyBase EnemyBase.Spawn(Vector3 position, string id)`
- `public static PlayerHQ PlayerHQ.Spawn(Vector3 position, string id)`
  - `position` is the ground point: `new Vector3(posX, GetTerrainHeightAt(posX) + entry.y, 0f)` (that is the existing `position` local for ground entries). The factory raises the object by half the sprite height, so its bottom edge rests on that y.
  - `id` is `entry.id`.
  - Suggested cases: `case "enemybase": entityGO = EnemyBase.Spawn(position, entry.id).gameObject; break;` and `case "playerhq": entityGO = PlayerHQ.Spawn(position, entry.id).gameObject; break;`
- Other public API:
  - EnemyBase: `const string TypeKey = "enemyBase"`, `string Id`, `bool KilledByBomb`, `float CurrentDamagePercent`, `static event Action<EnemyBase> Destroyed`.
  - PlayerHQ: `string Id`, `float WorldX`, `Vector3 GroundPosition`.

REQUESTS:
- mechanics (T935, MissionRunner/ObjectiveTracker): mission 4's bombTarget can't complete today.
  - `GameEvents.EntityDestroyed` carries no id.
  - `ObjectiveTracker.OnEntityDestroyed` does nothing for "bombTarget".
  - Nothing calls `ObjectiveTracker.OnBombApplied`.
  - Fix: subscribe to `EnemyBase.Destroyed` in StartMission and unsubscribe in teardown. When `b.KilledByBomb`, complete each unevaluated bombTarget objective whose `targetId == b.Id` (or whose targetType is "enemyBase" when targetId is empty). For example, add `ObjectiveTracker.OnBombTargetDestroyed(string id)`, or call `OnBombApplied("enemyBase")`, which matches by targetType only.
  - Score is already handled by ScoreKeeper. Don't add points in that handler.
- mechanics (T935 or the zeppelin task): mission 5 `failAtX` (620) and the HQ (atX 650) are separate numbers. If the zeppelin should fail on reaching the HQ, read `PlayerHQ.WorldX` from the spawned instance. Otherwise keep failAtX from the JSON.
- orchestrator: ARCHITECTURE §3 Enemies should list `EnemyBase` and `PlayerHQ`:
  - `EnemyBase`: ground-bound, trigger, Bomb-only damage from enemyHealth.enemyBase, name "enemyBase", static `Destroyed` event with `Id` and `KilledByBomb`.
  - `PlayerHQ`: Neutral TeamTag, not IDamageable, `WorldX`.
  - Both crash the player on contact. Ground-bound spawns sit their bottom edge on HeightAt(atX)+y, while AAAGun is centred on it.
- No new Balance fields are needed.
