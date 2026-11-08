TASK: T035
AGENT: mechanics
STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Scripts/UI/BriefingView.cs
SUMMARY: Created BriefingView — a code-built UI panel showing mission title/briefing from MissionData JSON, with a "Begin Mission" button, 5-second auto-transition, Enter/Space skip, and 0.5s CanvasGroup alpha fade-in via Color.a lerp Coroutine.
VERIFY: Open the Game scene in Unity, set GameManager to Briefing state, click Play. Confirm: (1) canvas background fades in over 0.5s, (2) title reads "Mission 01: <title>" from mission_01.json, (3) briefing text displays correctly with line spacing, (4) pressing Enter or Space immediately triggers transition (check console for "[BriefingView] Starting mission 01: ..."), (5) without input the scene transitions after ~5 seconds.
REQUESTS: None. GameManager needs to set BriefingView.currentMission before OnEnable, and listen for RaiseObjectiveCompleted("mission_N_begun") to switch state to Playing and load MissionRunner.
