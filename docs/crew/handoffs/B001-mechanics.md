TASK: B001 + B002
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs
- Biplanes/Assets/Biplanes/Scripts/Weapons/Bullet.cs
- Biplanes/Packages/manifest.json
- docs/crew/handoffs/B001-mechanics.md
SUMMARY:
- B001: StartMission (after the JSON parses, before ScrollController.Create) calls the new TearDownPreviousMission() and ResetCameraToMissionStart(). TearDown unsubscribes OnEntityDestroyed and deactivates then destroys the old PlayerPlane, every _spawnedEntities object and the old HUD canvas. It keeps one ObjectiveTracker, which gets re-Inited, and removes any extras. Destroy runs in Play mode, DestroyImmediate otherwise. ResetCamera sets Camera.main x = 0 (const MissionStartCameraX) and keeps y/z. The subscription is now -= then +=, and OnDestroy always unsubscribes. PlayerPlane OnMove/OnFire/OnDropBomb now ignore an inactive plane, so the old plane can't call SetDirection before its deferred Destroy.
- B002a: the takeoff tick skips UpdateFacing, so W+A takes off facing right. A alone on the runway still does nothing. B002b: SetLimits(startCameraX, data.length) is unconditional (SetLimits collapses max < min, so the camera is pinned at start). B002c: Bullet loads bulletSpeed and bulletDamage in Awake (it runs at pool construction, before the first Init), and Start is removed. B002d: "com.unity.test-framework": "1.8.0" added to manifest.json, matching packages-lock.json.
- Bomb drift is unchanged, as decided. The reflection targets the tests use (_velocity, _speed, _screenAnchor, FixedUpdate, OnFire) were not renamed.
VERIFY:
- `python tools/compile_check.py` -> PASS Biplanes, PASS Biplanes.Editor, exit 0. compile_check doesn't build the test asmdef; tests only use unchanged private names via reflection.
- Unity: let the Package Manager resolve (no version change expected), then Test Runner -> EditMode -> all 30 tests.
- Playtest: win mission 1, press Next. Mission 2 starts at camera x 0 with no spawn burst, one PlayerPlane in the Hierarchy, one HUD Canvas and one ObjectiveTracker on MissionRunner. Flying left stops at x 0. Each kill is logged once by ObjectiveTracker.
- Playtest: on the mission 1 runway, hold A and press W. The plane takes off facing right; after that, A turns it left. A alone on the runway does nothing.
- Playtest: the first burst after a mission start flies at speed 14 (the same as later bursts).
REQUESTS:
- orchestrator: ARCHITECTURE.md §3 Missions: replace "After `ScrollController.Create` it calls `SetLimits(startCameraX, data.length)`, with no upper limit when length ≤ startCameraX." with "Before `ScrollController.Create` it tears down the previous mission (player, spawned entities, HUD, extra ObjectiveTracker, EntityDestroyed subscription) and moves Camera.main back to x = 0 (y/z kept), so 'Next' without a scene reload starts clean. After Create it calls `SetLimits(startCameraX, data.length)`. When length ≤ startCameraX, SetLimits collapses it and the camera stays pinned at the start." Also apply the QA wording fix in the same paragraph: "camera x >= atX - half screen width - 2" becomes "FarthestX >= ...".
- orchestrator: ARCHITECTURE.md §3 PlayerPlane: "D or W takes off facing right; facing input is ignored on the takeoff tick (W+A also takes off facing right)."
- mechanics (follow-up, P2, pre-existing): the pooled projectiles from the previous mission leak on Next. PlayerPlane.InitWeaponPools recreates the Bullet/Bomb pools without destroying the old pooled GameObjects, and any enemy flak pools are probably the same. This is harmless but grows with each Next.
- mechanics (follow-up, P2, pre-existing): ParallaxBackground/TerrainStrip are replaced with Destroy (deferred), so TerrainStrip.Instance can point at the old strip for the rest of that frame, depending on how Create assigns Instance. Not observed; worth a QA look.
