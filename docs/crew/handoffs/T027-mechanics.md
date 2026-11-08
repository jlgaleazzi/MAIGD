TASK: T027
AGENT: mechanics
STATUS: DONE
FILES:
  - Biplanes/Assets/Biplanes/Scripts/Enemies/AAAGun.cs (created)
  - Biplanes/Assets/Biplanes/Scripts/Core/Health.cs (modified: added CurrentDamagePercent property)
SUMMARY:
  Created the AAAGun enemy class implementing IDamageable with bomb-only damage filtering.
  It fires 3-shell flak bursts every 3s toward the player's predicted position, is ground-bound
  at y=-4.5, and awards AAA score on destruction via Balance.json lookup. Added CurrentDamagePercent
  read property to Health.cs so external code can query damage without private field access.
VERIFY:
  1. In MissionRunner, call `AAAGun.Spawn(new Vector3(x, -4.5f, 0f))` for an "aaa" spawn entry.
  2. After all spawns, iterate AAA guns and call `.Init(playerPlane)` to set PlayerRef.
  3. Play a mission with at least one AAA gun — verify: (a) it fires 3 shells toward the player
     every ~3 seconds, (b) player bullets do NO damage on contact, (c) one bomb destroys it,
     (d) smoke tint appears when damage ≥70%, (e) score is awarded on death.
REQUESTS:
  - MissionRunner must spawn AAAGun objects at y = -4.5f (ground level), call Init(player),
    and set up the FlakShell pool via FlakShell.InitPool if not already done.
  - Ensure Projectile collision logic calls `other.GetComponentInParent<IDamageable>()` —
    since AAAGun itself implements IDamageable and has no sibling Health, interface resolution
    will correctly target AAAGun.ApplyDamage (damage-source filtering).
  - Balance.json `enemyHealth.aaa = 100` is referenced but AAA gun is self-contained — the
    balance value should be read from there in Spawn() for display/editor purposes if needed.
