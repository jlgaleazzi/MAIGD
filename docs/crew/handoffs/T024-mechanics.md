TASK: T024
AGENT: mechanics
STATUS: DONE
FILES:
  - Biplanes/Assets/Biplanes/Scripts/Enemies/StaticObstacle.cs (created)
SUMMARY:
  StaticObstacle class for building/tree terrain obstacles. Spawns from Spawn(position, type),
  has trigger Collider2D + Kinematic Rigidbody2D, applies instant 100% Collision damage to
  any Player-team IDamageable on contact, silently ignores enemy projectiles. Loads sprite
  from Resources/"Sprites/obstacles/{building|tree}_{type}" with white-sprite fallback.
VERIFY: In-editor: open Game.unity scene, add a StaticObstacle via the MissionRunner spawner
  (or create one manually by adding StaticObstacle component + BoxCollider2D trigger to a GO),
  place player plane into its collider — confirm player Health reaches 100% and EntityDestroyed
  fires with Team.Player. Also verify enemy bullet projectile that hits obstacle disappears
  without damaging it. If Unity not available, QA statically checks: namespace Biplanes.Enemies;
  public static Spawn(Vector3, string); OnTriggerEnter2D → IDamageable.Team == Player check;
  Collider2D.isTrigger = true; Rigidbody2D.bodyType = Kinematic.
REQUESTS: none
