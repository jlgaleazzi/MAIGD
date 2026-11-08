TASK: T030
AGENT: mechanics
STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Scripts/Missions/MissionData.cs
SUMMARY: Created all [Serializable] data classes for the mission JSON schema:
  PlayerStartData (struct → class for JsonUtility compatibility), TerrainPoint, SpawnEntry,
  ObjectiveEntry, and root MissionData. All public fields match JSON keys exactly so
  JsonUtility.FromJson<MissionData>() deserializes correctly. No UnityEngine dependency —
  pure System.Serializable types that compile on any C# compiler.
VERIFY: Place a sample mission JSON (e.g. mission_01.json content from ARCHITECTURE §5) under Resources/Missions/ and run:
  var data = JsonUtility.FromJson<MissionData>(textAsset.text);
  Debug.Log(data.id + " " + data.title + " " + data.playerStart.x + " " + data.terrain.Length + " " + data.spawns[0].type + " " + data.objectives[0].id);
Expected: "1 Destroy Incoming Airplanes -6 (playerStart.x) 2 (terrain count) fighter kill_all_fighters"
REQUESTS: none
