TASK: T039c
AGENT: mechanics
STATUS: DONE
FILES:
  Biplanes/Assets/Biplanes/Scripts/Missions/ObjectiveTracker.cs  (edit — line 93)
  Biplanes/Assets/Biplanes/Scripts/Enemies/BarrageBalloon.cs     (edit — line 2)
  Biplanes/Assets/Biplanes/Scripts/Core/ScoreKeeper.cs           (edit — lines 90, 95)
  Biplanes/Assets/Biplanes/Editor/ProjectBuilder.cs              (new — 381 lines)
SUMMARY:
  Fixed ObjectiveTracker.Start missing parameter type annotation (ObjectiveEntry[]).
  Removed unused UnityEngine.Rendering.Universal import from BarrageBalloon.
  Renamed SaveSystem.SetBestScore→SaveBestScore and SetUnlockedMission→SaveUnlockedMission in ScoreKeeper.
  Created ProjectBuilder.cs with all six [MenuItem] methods: BuildScenes, ConfigureProject, BuildWebGL, BuildWindows, BuildMac, BuildAll.
VERIFY: Run `tools/unity.sh compile` — must exit 0. Then open Unity editor and verify "Biplanes/" menu appears with all 6 commands (BuildScenes, ConfigureProject, BuildWebGL, BuildWindows, BuildMac, BuildAll).
REQUESTS: none
