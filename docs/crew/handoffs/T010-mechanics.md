TASK: T010
AGENT: mechanics
STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Scripts/Core/GameEvents.cs
SUMMARY: Created the static GameEvents event bus with 6 events and corresponding Raise* helper methods. Uses safe copy-then-invoke pattern for null-safe multicast invocation. Pure C# — no Unity dependencies, suitable for EditMode tests.
VERIFY: Static check: confirm all 6 events (ScoreChanged, PlayerDamageChanged, BombsChanged, EntityDestroyed, ObjectiveCompleted, MissionEnded) and all 6 Raise* methods exist with correct signatures. When Unity is available, run `tools/unity.sh compile` to verify clean compilation.
REQUESTS: none
