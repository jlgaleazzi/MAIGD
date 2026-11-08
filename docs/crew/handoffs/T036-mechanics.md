TASK: T036
AGENT: mechanics
STATUS: DONE
FILES:
  - Biplanes/Assets/Biplanes/Scripts/UI/PauseView.cs (created, 236 lines)
SUMMARY: Created PauseView — a canvas-overlay view that displays "PAUSED" with Resume
  and Quit buttons per UI_SPEC §3.4 layout. Time.timeScale is set to 0 while visible;
  OnEnable/OnDisable control visibility via CanvasGroup alpha and raycast blocking.
VERIFY: In the Game scene, attach PauseView as a child of the Playing canvas, enable it
  during gameplay, press Escape → verify PAUSED panel appears centred with two buttons;
  click Resume → timeScale returns to 1 and panel hides; click Quit → MainMenu loads.
REQUESTS: T037 — GameManager (Core/GameManager.cs) must be created next so that
  PauseView.OnResumeClicked() can call GameManager.Instance.ResumeGame() and
  PauseView.SetGameManager(gm) wiring in Awake. Without it the Resume button falls
  back to a direct resume with no HUD-canvas restore logic.
