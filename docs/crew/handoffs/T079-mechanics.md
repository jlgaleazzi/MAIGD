TASK: T079
AGENT: mechanics
STATUS: DONE
FILES:
  - Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs
  - Biplanes/Assets/Biplanes/Editor/ProjectBuilder.cs
SUMMARY: MissionRunner creates ParallaxBackground + TerrainStrip after ScrollController (replacing the previous
         mission's on a no-reload restart), delegates GetTerrainHeightAt to HeightAt, rests runway starts on the
         ground and raises airborne starts below 1.5 above ground to 1.5. Cameras clear to paper #0F2A4A.
VERIFY: Focus Unity, Biplanes > BuildScenes, Biplanes > BuildWindows, play missions 1-5 (T080 checklist).
REQUESTS: mission-design: playerStart.y is -4.0 in all five missions, but §5 defines y as height above the
          terrain, so it means 4 units underground. Missions 2-5 now log a warning and start at 1.5. Set a real
          height (for example 3.0) for airborne starts and 0 for mission 1's runway start.
