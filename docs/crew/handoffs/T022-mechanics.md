TASK: T022
AGENT: mechanics
STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs
SUMMARY: Implemented PlayerPlane MonoBehaviour with kinematic Rigidbody2D + MovePosition movement. Takeoff state (onRunway=true) transitions on W/D press. Movement clamped to camera bounds minus 0.5 margin via ViewportToWorldPoint. Smoke trail visual activates at ≥70% damage from Balance.json. Crash at 100% notifies GameManager.Instance.SetState(Lost) or raises GameEvents.RaiseMissionEnded as fallback. Static Create() factory builds the GameObject entirely from code with Sprite fallback to white texture.
VERIFY: In Unity, add PlayerPlane to a scene with Camera.main and PlayerInput. Confirm (1) plane is stationary on runway until W/D pressed, (2) movement speed matches Balance.player.speed, (3) plane never escapes camera bounds by >0.5 world units, (4) sprite tints amber at 70%+ damage, (5) smoke trail GameObject activates at that threshold, (6) crashing at 100% triggers GameState.Lost transition.
REQUESTS: 
- GameManager.cs must be created in Core/ with GameState enum {Menu, Briefing, Playing, Paused, Won, Lost} and Instance singleton + SetState() method (Architecture §3).
- BombBay.cs (T023) needed for Enter→bomb drop mechanic referenced by GDD.
- Health.cs does not expose an OnDeath event — only a virtual OnDestroyed(). PlayerPlane checks CurrentDamagePercent >= 100f in FixedUpdate as the crash trigger instead of subscribing to an event.
