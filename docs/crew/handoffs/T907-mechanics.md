TASK: T907
AGENT: mechanics
STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Scripts/Weapons/Projectile.cs, Biplanes/Assets/Biplanes/Scripts/Core/Health.cs, Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs, Biplanes/Assets/Biplanes/Scripts/Enemies/FighterAI.cs, Biplanes/Assets/Biplanes/Scripts/Enemies/BomberAI.cs
SUMMARY: Bullets now hit enemy planes. Planes and projectiles are all kinematic, so Projectile.Init and PlayerPlane.Create now set Rigidbody2D.useFullKinematicContacts. FighterAI and BomberAI explode and are destroyed once their Health is dead, and they are named "fighter"/"bomber" so ScoreKeeper finds their score keys. Health ignores hits once dead. Hitting a fighter or bomber in mid-air destroys both planes (PlayerPlane.OnTriggerEnter2D, Collision damage).
VERIFY: Compiles against Library/ScriptAssemblies with dotnet (Unity was open, so no batch run). Play mission 1: shoot a fighter and check that it bursts, disappears and the score rises by 100. Fly into a fighter and check that both explode and the mission is lost.
REQUESTS: orchestrator: ARCHITECTURE §3 should state that every kinematic body that must report trigger overlaps sets useFullKinematicContacts, and that the player-vs-enemy-plane collision rule destroys both. Balance: Health has no per-type max HP, so bullet 25 x fighterBullet 8 = 200 kills a bomber in one hit, and enemyHealth.* is unused. A Health max-HP task is needed. The ObjectiveTracker destroyAllOfType/destroyById branches still only log.

FOLLOW-UP (same task): enemy hit points
FILES: Scripts/Core/Health.cs, Scripts/Enemies/FighterAI.cs, Scripts/Enemies/BomberAI.cs, Scripts/Enemies/BarrageBalloon.cs, Scripts/Enemies/AAAGun.cs, docs/ARCHITECTURE.md
SUMMARY: Health.SetMaxHitPoints(hp) adds an enemy mode where damage is counted in points out of enemyHealth.* and collision is fatal. Fighter 25 HP (1 bullet), bomber 200 (8 bullets/2 bombs), balloon 50 (2 bullets), AAA 100 (1 bomb, bullets still bounce off). Enemies are named by score key (fighter/bomber/aaa/barrageBalloon) so ScoreKeeper awards points. Balloons now die (burst, removed) and no longer take a double hit from their own trigger.
VERIFY: dotnet compile succeeded. Play: a bomber takes 8 bullets, a balloon 2, and score rises per Balance.score.
REQUESTS: BomberAI speed is computed from enemyHealth.enemyBase / 10 (= 10 units/s, far faster than fighters); it needs a real Balance field. Zeppelin turret/hull and enemyBase classes don't exist yet.

FOLLOW-UP 2: enemy speed
FILES: Resources/Balance.json, Scripts/Core/GameBalance.cs, Scripts/Enemies/BomberAI.cs, Scripts/Enemies/FighterAI.cs, docs/ARCHITECTURE.md
SUMMARY: New Balance block enemySpeed {fighter 3.0, bomber 1.5} (EnemySpeedData). BomberAI no longer derives its speed from enemyHealth.enemyBase/10 (=10 u/s). FighterAI reads its speed from the same block.
REQUESTS: none

FOLLOW-UP 3: enemy plane entry
FILES: Scripts/Missions/MissionRunner.cs, Scripts/Enemies/FighterAI.cs, docs/ARCHITECTURE.md
SUMMARY: Fighters and bombers now spawn off the right screen edge with random x stagger (random arrival times) and random height in the open sky, not at the camera centre at one y. FighterAI.Configure applies the JSON pattern (sine/dive/straight/escort), which was ignored before.
REQUESTS: mission-design: plane `y` values are no longer used for placement. Ground spawns (aaa, balloons, buildings, trees) still appear at atX when the camera reaches it, i.e. mid-screen; they should be placed off-screen too. BomberAI has no Configure (escortOf/targetX are never set from JSON).

FOLLOW-UP 4: ground spawns
FILES: Scripts/Missions/MissionRunner.cs, docs/ARCHITECTURE.md
SUMMARY: Non-plane spawns keep x = atX but trigger one half-screen + 2 units early, so they scroll in from the right edge instead of appearing mid-screen.
REQUESTS: none
