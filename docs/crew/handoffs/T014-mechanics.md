TASK: T014
AGENT: mechanics
STATUS: DONE
FILES:
  - Biplanes/Assets/Biplanes/Scripts/Core/ScrollController.cs (created)
SUMMARY:
  ScrollController drives the mission camera rightward at scrollSpeed from the
  mission JSON. MissionDistanceX property returns camera x for spawner thresholds.
  Singleton access via Instance for MissionRunner integration. Fixed at Create()
  factory — no prefab assets needed.
VERIFY: Run `tools/unity.sh compile`. Then in-game: open a mission and confirm
  (a) the camera scrolls right continuously, (b) MissionRunner can read
  ScrollController.Instance.MissionDistanceX to spawn entities at correct thresholds,
  (c) SetScrollSpeed(0f) freezes horizontal movement during briefing.
REQUESTS: none
