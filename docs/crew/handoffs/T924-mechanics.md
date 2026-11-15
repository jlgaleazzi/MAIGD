TASK: T924
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Core/ScrollController.cs
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs
- Biplanes/Assets/Biplanes/Scripts/Weapons/Bullet.cs
- Biplanes/Assets/Biplanes/Scripts/Missions/TerrainStrip.cs
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs
- Biplanes/Assets/Biplanes/Scripts/Core/GameBalance.cs
- Biplanes/Assets/Biplanes/Resources/Balance.json
- docs/crew/handoffs/T924-mechanics.md
SUMMARY: The plane is now reversible, Defender-style. A/D set PlayerPlane.Facing, which flips the sprite, aims the bullets and gun offset, and calls ScrollController.SetDirection. The camera velocity eases through 0 over turnEaseSeconds, and the plane's screen x eases to viewport 0.3 (facing right) or 0.7 (facing left) at player.speed. The camera is clamped to [mission start x, length]. Spawns trigger on FarthestX. Terrain keeps chunks within ±30 of the camera.
VERIFY:
- Compile: `python tools/compile_check.py` passed: PASS Biplanes, PASS Biplanes.Editor, exit 0. The Unity editor has the project open, so this was the static check, not a Unity batch compile.
- Static: ParallaxBackground is unchanged. `Mathf.Repeat(camX * f, w)` returns values in [0, w) for negative or decreasing camX, so it already works both ways.
- Manual playtest (mission 1, then any airborne mission):
  1. Takeoff is the same as before. On the runway, A does nothing. D or W takes off facing right with the auto climb-out. The world scrolls right at the mission's scrollSpeed.
  2. After takeoff, the plane drifts to about 30% from the left edge and stays there. W/S still climb and dive at the old speed.
  3. Press A: the sprite flips at once. The scroll slows, stops and speeds up leftward over about 0.6 s, with no snap.
  4. After the turn, the plane swings to the trailing third (about 70% from the left edge) in about 1 to 2 s. Press D and it swings back to about 30%.
  5. Hold Space after turning: bullets leave the plane's left (nose) side and fly left. Turn right again: they fire right.
  6. Fly left until the camera reaches the mission start x: the camera stops dead and the plane stays on screen. Fly right to the mission length: the camera stops there and surviveToEnd completes.
  7. Pass a few spawn groups, turn back over that ground, then turn right again. No group spawns a second time, and nothing appears earlier than it would have.
  8. While flying left, the ground is drawn the whole way, with no gaps or pop-in at the left edge. It is also drawn after you turn right again.
  9. The parallax layers scroll the other way after a turn, and the far layer moves slowest.
  10. Regression: the ground-contact crash, ramming a fighter or bomber, and a bomb's inherited forward drift all still work. Also try them facing left: a bomb drifts left.
REQUESTS:
- orchestrator, please make these ARCHITECTURE.md changes:
  - §3 Camera: replace "ScrollController moves the camera right at scrollSpeed ... "Mission distance" = the camera's x." with: "ScrollController scrolls the camera in `Direction` (+1/-1, set by `PlayerPlane` via `SetDirection(Facing)`) at `scrollSpeed` from the mission JSON. On a turn the x `Velocity` eases linearly toward `Direction * scrollSpeed`, so a full reversal takes `player.turnEaseSeconds`. `SetLimits(minX, maxX)` clamps the camera x, and the velocity is 0 while it is pinned at a limit. `FarthestX` = the largest camera x reached so far. "Mission distance" (`MissionDistanceX`) = the current camera x. `Create` resets and reuses an existing Instance (direction +1, full speed, no limits, FarthestX = camera x)."
  - §3 Player: "W/A/S/D move" becomes "W/S climb and dive. A/D set `PlayerPlane.Facing` (-1/+1; when both are held, the last one pressed wins; when neither is held, Facing is kept). The sprite uses `flipX = Facing < 0`."
  - §3 PlayerPlane: add "`public int Facing` (starts at +1). Airborne, the plane rides along with the camera, and its screen x eases at `player.speed` toward viewport x `screenAnchor` when facing right, or `1 - screenAnchor` when facing left. On the runway, A does nothing. D or W takes off facing right. Bullets fly in the Facing direction, and the gun offset x is mirrored."
  - §3 Weapons: add `Bullet.Spawn(Vector3 position, Quaternion rotation, int direction)`. The 2-argument overload is kept and fires +x.
  - §3 TerrainStrip: "Only the chunks overlapping [camera x - 10, camera x + 30] exist" becomes "[camera x - 30, camera x + 30]".
  - §3/§5 MissionRunner: after `ScrollController.Create`, it calls `SetLimits(startCameraX, data.length)`, where startCameraX is the camera x at start; when length <= startCameraX there is no upper limit. Spawn triggers (plane atX, and the ground look-ahead `atX - (halfWidth + 2)`) compare against `FarthestX`. surviveToEnd and the HUD use the current camera x.
  - §4 Balance.json "player": add `"turnEaseSeconds": 0.6, "screenAnchor": 0.3`. In code, the fallbacks are 0.6 and 0.3 when the field is missing or 0.
- mission-design (for information): spawns are now gated by the farthest x reached. Turning back never re-triggers them. Enemies that have already spawned stay alive off-screen and can be revisited. FighterAI and BomberAI still fly left in world space.
- Note: `MissionRunner._cameraTransform` was an empty placeholder block and is now unused (the field is kept, and the scene may wire it). BomberAI's targetX check still uses the current camera x, so after a turn-back it reacts to the camera, not to FarthestX. This was left unchanged on purpose because the brief keeps MissionDistanceX as the current x.
