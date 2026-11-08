TASK: T016
AGENT: mechanics
STATUS: DONE
FILES:
  - Biplanes/Assets/Biplanes/Scripts/Weapons/Projectile.cs
  - Biplanes/Assets/Biplanes/Scripts/Weapons/ObjectPool.cs
SUMMARY:
  Created the Projectile abstract base class (speed, team, damage, lifetime fields,
  trigger collision logic with own-team skip, Init lifecycle for pooled objects) and
  a generic ObjectPool<T> class that pre-fills via factory delegate — no runtime Instantiate.
VERIFY:
  Static check (Unity not available): confirm both files in Biplanes/Assets/Biplanes/Scripts/Weapons/
  namespace Biplanes.Weapons; Projectile is abstract : MonoBehaviour with fields Team, Speed, Damage,
  Lifetime; ObjectPool<T> has Push, Pop, Capacity; OnTriggerEnter2D uses GetComponentInParent<IDamageable>
  and skips target.Team == team. When Unity is available: tools/unity.sh compile → exit 0.
REQUESTS: none
