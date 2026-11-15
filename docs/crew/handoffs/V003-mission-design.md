TASK: V003
AGENT: mission-design
STATUS: PARTIAL (data done; validator rule is stale, see REQUESTS)
FILES:
- Biplanes/Assets/Biplanes/Resources/Missions/mission_02.json
- Biplanes/Assets/Biplanes/Resources/Missions/mission_05.json
- docs/missions/mission_02.md
- docs/missions/mission_05.md
SUMMARY:
- M5: length 700 to 540 (270 s, the target). PlayerHQ `hq_01` at x 360 = failAtX 360. Zeppelin id `zep`, atX 430, y 0.9 (was y 5.0, id zeppelin_01). Escort fighters use escortOf `zep` at atX 420/425 (after the zeppelin's own trigger at camera about 419, so the escort lookup finds it). Spawn 430 > failAtX 360 numerically (70 u, 70 s at 1.0 u/s). Zeppelin appears about 205 s in; it reaches the HQ about 275 s in at the latest. Terrain reshaped to stay low at 0.5 near the finale. Build is fighters, AAA, 2 balloons, then HQ and the escort wave.
- M2: length 400 to 450 (180 s, the target). Bomber id `bomber_1`, atX 80, failAtX 370 to 10. The bomber spawns at camera + 10.4 to 16.4, so about x 90 to 96 > 10 (about 55 s of flight, bomb run from x 15). Escorts: removed the ids (V001 id/count issue), fixed y -1.0 to 2.5, escortOf `bomber_1`. Removed the two AAA groups (AAA is introduced in M3), added a warm-up fighter wave at 50 and a health balloon at 65.
- Both missions: playerStart y -4.0 to 3.0 (an airborne start below 1.5 is raised with a warning).
- Feasibility M5: closing speed 3 u/s gives about 4 s to first contact; each pass is 7 to 15 s of fire. The kill needs about 5 s of clean fire (turrets 225 hp, then hull 1000 hp at 200 dps) so 3 to 4 passes fit in 70 s: doable, not trivial.
VERIFY:
- `python tools/validate_missions.py`: M2 and M5 now fail only with "failAtX must be beyond the target's spawn atX" (the validator still uses the old camera-based rule). Nothing else fails; the duration warning on M5 is gone. M1 and M4 have the known V001 y issues; M3 is OK.
- Not run in Unity (no play).
REQUESTS:
- qa/mechanics (tools/validate_missions.py): change the failAtX rule to the B018 meaning: failAtX must be >= 0 and LESS than the target's spawn x (zeppelin: spawn atX; bomber: atX + about 10). Then M2 and M5 pass. I do not own the file.
- orchestrator: M2 and M5 geometry numbers are in the sheets; add to GDD log if wanted. Spawn x was assumed to be bomber atX + 10 (min margin) for the check: 90 > 10.
