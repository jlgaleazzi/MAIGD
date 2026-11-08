TASK: T015
AGENT: mechanics
STATUS: DONE
FILES:
  - Biplanes/Assets/Biplanes/Scripts/Core/ScoreKeeper.cs (created)
  - Biplanes/Assets/Biplanes/Scripts/Core/SaveSystem.cs (created)
SUMMARY:
  ScoreKeeper singleton subscribes to GameEvents.EntityDestroyed, looks up kill scores
  from GameBalance.scoreData via a switch on entityType string, and raises ScoreChanged
  so the HUD updates. Public AddPoints() lets callers add objective bonuses manually.
VERIFY:
  1. Run `tools/unity.sh compile` — expect exit code 0 (clean compile).
  2. In-engine: play any mission with enemy spawns, destroy an enemy fighter → HUD score should increase by 100 (default from Balance.json).
  3. End a mission (win or lose) and check PlayerPrefs key "best_score_1" reflects the final score.
REQUESTS: none
