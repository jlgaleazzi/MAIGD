TASK: T019
AGENT: mechanics
STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Scripts/Weapons/Bomb.cs
SUMMARY: Created Bomb class (inherits Projectile) for player ground attacks. Static Spawn(position, parentVelocity) accepts full inherited velocity so bombs arc forward from plane momentum. Update() applies gravity (12×) to vertical velocity each frame. OnHit deals bombDamage (100 from Balance.json), triggers FxManager explosion VFX hook, then recycles to pool.
VERIFY: Run the Game scene in play mode during a mission where the player has dropped bombs; verify that dropped bombs arc downward under gravity with forward momentum matching the plane's dive angle, deal high damage to AAA buildings (100%), and recycle back to the pool without Instantiate/Destroy spam in Console logs. Confirm Bomb.cs compiles by inspecting its using directives against Biplanes.Core and Biplanes.Weapons namespaces.
REQUESTS: Need FxManager.cs (Biplanes.Fx) created so Bomb's explosion VFX hook has a target — call `FxManager.Instance.TriggerExplosion(Vector3 position)`. Also need BombBay.cs to wire up Enter-key input, spawn from the bomb pool via Bomb.Spawn(), and decrement a bomb counter for HUD display.
