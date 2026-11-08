# T028 BomberAI — Handoff

```
TASK: T028
AGENT: mechanics
STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Scripts/Enemies/BomberAI.cs
SUMMARY: Created BomberAI class — enemy bomber that flies left slower than fighters, drops bombs when camera reaches targetX, and supports escort pattern. Static factory Spawn(Vector3) for MissionRunner. Health uses enemyHealth.bomber (200 points ≈ 8 bullet hits at 25 base × 8% multiplier).
VERIFY: Run a mission JSON with a bomber spawn entry that has targetX ≥ 0; confirm the bomber drops bombs when camera reaches targetX and that shooting it 8 player bullets destroys it. Also test escortOf by spawning two bombers where one follows the other.
REQUESTS: Bomb needs an enemy variant — Bomb.Spawn always sets team = Team.Player in Awake(), so enemy-bomber dropped bombs currently hit enemies but not the player. Request a new method Bomb.SpawnEnemy(Vector3, Vector2) that sets team = Team.Enemy and uses damageTaken.bomberGunner (10 per hit). Also Health.cs should support entity-specific health pools beyond percentage for future use (enemyHealth.bomber = 200 points is meant as an HP pool, not a multiplier).
```
